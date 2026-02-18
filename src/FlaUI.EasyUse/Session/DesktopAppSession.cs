using System.Diagnostics;
using System.Reflection;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace FlaUI.EasyUse.Session;

public sealed class DesktopAppSession : IDisposable
{
    private static readonly object BuildLock = new();
    private static readonly HashSet<string> BuiltProjectKeys = new(StringComparer.OrdinalIgnoreCase);

    private readonly Application _application;
    private readonly UIA3Automation _automation;
    private bool _disposed;

    private DesktopAppSession(Application application, UIA3Automation automation, Window mainWindow, ConditionFactory conditionFactory)
    {
        _application = application;
        _automation = automation;
        MainWindow = mainWindow;
        ConditionFactory = conditionFactory;
    }

    public Window MainWindow { get; }

    public ConditionFactory ConditionFactory { get; }

    public static DesktopAppSession LaunchFromProject(DesktopProjectLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!System.OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Desktop UI automation is supported only on Windows.");
        }

        if (string.IsNullOrWhiteSpace(options.ProjectRelativePath))
        {
            throw new ArgumentException("ProjectRelativePath is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.TargetFramework))
        {
            throw new ArgumentException("TargetFramework is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.BuildConfiguration))
        {
            throw new ArgumentException("BuildConfiguration is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.SolutionFileName))
        {
            throw new ArgumentException("SolutionFileName is required.", nameof(options));
        }

        if (options.BuildTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "BuildTimeout must be greater than zero.");
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

        var executablePath = ResolveExecutablePath(projectPath, options);

        return Launch(new DesktopAppLaunchOptions
        {
            ExecutablePath = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            MainWindowTimeout = options.MainWindowTimeout,
            PollInterval = options.PollInterval
        });
    }

    public static DesktopAppSession Launch(DesktopAppLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            throw new ArgumentException("ExecutablePath is required.", nameof(options));
        }

        if (options.MainWindowTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MainWindowTimeout must be greater than zero.");
        }

        if (options.PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "PollInterval must be greater than zero.");
        }

        var executablePath = Path.GetFullPath(options.ExecutablePath);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Desktop app executable was not found.", executablePath);
        }

        var workingDirectory = options.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = Path.GetDirectoryName(executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = true
        };

        var application = Application.Launch(startInfo);
        var automation = new UIA3Automation();

        try
        {
            var mainWindowResult = Retry.WhileNull(
                () => application.GetMainWindow(automation),
                timeout: options.MainWindowTimeout,
                interval: options.PollInterval,
                throwOnTimeout: false);

            if (!mainWindowResult.Success || mainWindowResult.Result is null)
            {
                throw new TimeoutException("Main window was not found within timeout.");
            }

            var conditionFactory = new ConditionFactory(new UIA3PropertyLibrary());
            return new DesktopAppSession(application, automation, mainWindowResult.Result, conditionFactory);
        }
        catch
        {
            automation.Dispose();
            TryTerminateApplication(application);
            application.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _automation.Dispose();
        TryTerminateApplication(_application);
        _application.Dispose();
        GC.SuppressFinalize(this);
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

    private static string ResolveExecutablePath(string projectPath, DesktopProjectLaunchOptions options)
    {
        var executableName = options.ExecutableName;
        if (string.IsNullOrWhiteSpace(executableName))
        {
            executableName = Path.GetFileNameWithoutExtension(projectPath);
            if (System.OperatingSystem.IsWindows())
            {
                executableName = $"{executableName}.exe";
            }
        }

        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Could not determine project directory for '{projectPath}'.");
        var executablePath = Path.Combine(
            projectDirectory,
            "bin",
            options.BuildConfiguration,
            options.TargetFramework,
            executableName);

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "Desktop app executable was not found. Verify TargetFramework and BuildConfiguration.",
                executablePath);
        }

        return executablePath;
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
                $"Failed to build desktop app. ExitCode={process.ExitCode}{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
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

    private static void TryTerminateApplication(Application application)
    {
        try
        {
            if (!application.HasExited)
            {
                application.Close();
            }
        }
        catch
        {
            // Best effort close.
        }

        try
        {
            if (!application.HasExited)
            {
                application.Kill();
            }
        }
        catch
        {
            // Best effort kill.
        }
    }
}
