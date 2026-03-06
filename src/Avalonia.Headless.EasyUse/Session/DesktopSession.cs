using System.Diagnostics;
using System.Reflection;
using AvaloniaWindow = Avalonia.Controls.Window;
using EasyUse.Session.Contracts;
using FlaUI.Core.Conditions;

namespace Avalonia.Headless.EasyUse.Session;

public sealed class DesktopAppSession : IDisposable
{
    private static readonly object BuildLock = new();
    private static readonly HashSet<string> BuiltProjectKeys = new(StringComparer.OrdinalIgnoreCase);

    private readonly AvaloniaWindow _nativeWindow;
    private bool _disposed;

    private DesktopAppSession(AvaloniaWindow nativeWindow)
    {
        _nativeWindow = nativeWindow;
        MainWindow = new FlaUI.Core.AutomationElements.Window(nativeWindow);
        ConditionFactory = new ConditionFactory();
    }

    public FlaUI.Core.AutomationElements.Window MainWindow { get; }

    public ConditionFactory ConditionFactory { get; }

    public static DesktopAppSession LaunchFromProject(DesktopProjectLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ProjectRelativePath))
        {
            throw new ArgumentException("ProjectRelativePath is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.TargetFramework))
        {
            throw new ArgumentException("TargetFramework is required.", nameof(options));
        }

        var solutionRoot = FindSolutionRoot(options.SolutionFileName);
        var projectPath = Path.GetFullPath(Path.Combine(solutionRoot, options.ProjectRelativePath));
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("Project file was not found.", projectPath);
        }

        if (options.BuildBeforeLaunch)
        {
            EnsureProjectBuilt(solutionRoot, projectPath, options);
        }

        var assemblyPath = ResolveAssemblyPath(projectPath, options);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("Built app assembly was not found.", assemblyPath);
        }

        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
            ?? Assembly.LoadFrom(assemblyPath);
        var windowType = ResolveMainWindowType(assembly);

        var window = HeadlessRuntime.Dispatch(() =>
        {
            var created = Activator.CreateInstance(windowType) as AvaloniaWindow;
            if (created is null)
            {
                throw new InvalidOperationException($"Could not instantiate window type '{windowType.FullName}'.");
            }
            return created;
        });

        return new DesktopAppSession(window);
    }

    public static DesktopAppSession Launch(DesktopAppLaunchOptions options)
    {
        throw new PlatformNotSupportedException(
            "Executable-based desktop launch is not supported in Avalonia headless mode. Use LaunchFromProject.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static string ResolveAssemblyPath(string projectPath, DesktopProjectLaunchOptions options)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Could not determine project directory for '{projectPath}'.");

        var assemblyName = options.ExecutableName;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            assemblyName = Path.GetFileNameWithoutExtension(projectPath);
        }

        if (assemblyName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            assemblyName = Path.GetFileNameWithoutExtension(assemblyName);
        }

        return Path.Combine(
            projectDirectory,
            "bin",
            options.BuildConfiguration,
            options.TargetFramework,
            $"{assemblyName}.dll");
    }

    private static Type ResolveMainWindowType(Assembly assembly)
    {
        var type = assembly
            .GetTypes()
            .FirstOrDefault(candidate =>
                typeof(AvaloniaWindow).IsAssignableFrom(candidate)
                && !candidate.IsAbstract
                && candidate.GetConstructor(Type.EmptyTypes) is not null
                && string.Equals(candidate.Name, "MainWindow", StringComparison.Ordinal));

        if (type is not null)
        {
            return type;
        }

        type = assembly
            .GetTypes()
            .FirstOrDefault(candidate =>
                typeof(AvaloniaWindow).IsAssignableFrom(candidate)
                && !candidate.IsAbstract
                && candidate.GetConstructor(Type.EmptyTypes) is not null);

        return type ?? throw new InvalidOperationException(
            $"Could not find concrete Avalonia Window type in assembly '{assembly.FullName}'.");
    }

    private static string FindSolutionRoot(string solutionFileName)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string?[] candidates =
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location)
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var current = new DirectoryInfo(candidate);
            while (current is not null)
            {
                var normalized = current.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!visited.Add(normalized))
                {
                    break;
                }

                if (File.Exists(Path.Combine(current.FullName, solutionFileName)))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException($"Could not locate solution root ({solutionFileName}).");
    }

    private static void EnsureProjectBuilt(string solutionRoot, string projectPath, DesktopProjectLaunchOptions options)
    {
        var buildKey = $"{projectPath}|{options.BuildConfiguration}|{options.TargetFramework}";

        lock (BuildLock)
        {
            if (options.BuildOncePerProcess && BuiltProjectKeys.Contains(buildKey))
            {
                return;
            }

            RunBuild(solutionRoot, projectPath, options);
            if (options.BuildOncePerProcess)
            {
                BuiltProjectKeys.Add(buildKey);
            }
        }
    }

    private static void RunBuild(string solutionRoot, string projectPath, DesktopProjectLaunchOptions options)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = solutionRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        processInfo.ArgumentList.Add("build");
        processInfo.ArgumentList.Add(projectPath);
        processInfo.ArgumentList.Add("-c");
        processInfo.ArgumentList.Add(options.BuildConfiguration);
        processInfo.ArgumentList.Add("-f");
        processInfo.ArgumentList.Add(options.TargetFramework);

        using var process = Process.Start(processInfo)
            ?? throw new InvalidOperationException("Unable to start dotnet build process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(options.BuildTimeout);
        try
        {
            process.WaitForExitAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw new TimeoutException($"Build timed out after {options.BuildTimeout.TotalSeconds} seconds.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to build project. ExitCode={process.ExitCode}{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup for timed-out build.
        }
    }
}
