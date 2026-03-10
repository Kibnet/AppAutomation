using System.Diagnostics;
using DotnetDebug.Avalonia;
using AppAutomation.Session.Contracts;

namespace DotnetDebug.AppAutomation.TestHost;

public static class DotnetDebugAppLaunchHost
{
    private static readonly string[] SolutionFileNames =
    [
        "AppAutomation.sln"
    ];
    private static readonly string[] DesktopProjectRelativePaths =
    [
        "sample\\DotnetDebug.Avalonia\\DotnetDebug.Avalonia.csproj"
    ];
    private const string DesktopTargetFramework = "net10.0";
    private static readonly object BuildLock = new();
    private static readonly HashSet<string> BuiltProjectKeys = new(StringComparer.OrdinalIgnoreCase);

    public static DesktopAppLaunchOptions CreateDesktopLaunchOptions(
        string buildConfiguration = "Debug",
        bool buildBeforeLaunch = true,
        bool buildOncePerProcess = true,
        TimeSpan? buildTimeout = null,
        TimeSpan? mainWindowTimeout = null,
        TimeSpan? pollInterval = null)
    {
        if (string.IsNullOrWhiteSpace(buildConfiguration))
        {
            throw new ArgumentException("Build configuration is required.", nameof(buildConfiguration));
        }

        var solutionRoot = FindRepositoryRoot();
        var projectPath = ResolveDesktopProjectPath(solutionRoot);
        if (buildBeforeLaunch)
        {
            EnsureProjectBuilt(
                solutionRoot,
                projectPath,
                buildConfiguration,
                buildTimeout ?? TimeSpan.FromMinutes(2),
                buildOncePerProcess);
        }

        var executablePath = ResolveExecutablePath(projectPath, buildConfiguration);
        return new DesktopAppLaunchOptions
        {
            ExecutablePath = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            MainWindowTimeout = mainWindowTimeout ?? TimeSpan.FromSeconds(20),
            PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(200)
        };
    }

    public static HeadlessAppLaunchOptions CreateHeadlessLaunchOptions()
    {
        return new HeadlessAppLaunchOptions
        {
            CreateMainWindow = static () => new MainWindow()
        };
    }

    private static string FindRepositoryRoot()
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string?[] candidates =
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
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

                if (HasRepositoryMarkers(current.FullName))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root for DotnetDebug sample.");
    }

    private static bool HasRepositoryMarkers(string rootPath)
    {
        foreach (var solutionFileName in SolutionFileNames)
        {
            if (File.Exists(Path.Combine(rootPath, solutionFileName)))
            {
                return true;
            }
        }

        foreach (var relativeProjectPath in DesktopProjectRelativePaths)
        {
            if (File.Exists(Path.Combine(rootPath, relativeProjectPath)))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveDesktopProjectPath(string repositoryRoot)
    {
        foreach (var relativeProjectPath in DesktopProjectRelativePaths)
        {
            var candidatePath = Path.GetFullPath(Path.Combine(repositoryRoot, relativeProjectPath));
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        throw new FileNotFoundException(
            "Desktop project file was not found for DotnetDebug sample.",
            string.Join(" | ", DesktopProjectRelativePaths.Select(path => Path.Combine(repositoryRoot, path))));
    }

    private static void EnsureProjectBuilt(
        string solutionRoot,
        string projectPath,
        string buildConfiguration,
        TimeSpan buildTimeout,
        bool buildOncePerProcess)
    {
        var buildKey = $"{projectPath}|{buildConfiguration}|{DesktopTargetFramework}";

        lock (BuildLock)
        {
            if (buildOncePerProcess && BuiltProjectKeys.Contains(buildKey))
            {
                return;
            }

            RunBuild(solutionRoot, projectPath, buildConfiguration, buildTimeout);
            if (buildOncePerProcess)
            {
                BuiltProjectKeys.Add(buildKey);
            }
        }
    }

    private static void RunBuild(
        string solutionRoot,
        string projectPath,
        string buildConfiguration,
        TimeSpan buildTimeout)
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
        processInfo.ArgumentList.Add(buildConfiguration);
        processInfo.ArgumentList.Add("-f");
        processInfo.ArgumentList.Add(DesktopTargetFramework);

        using var process = Process.Start(processInfo)
            ?? throw new InvalidOperationException("Unable to start dotnet build process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(buildTimeout);
        try
        {
            process.WaitForExitAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw new TimeoutException($"Build timed out after {buildTimeout.TotalSeconds} seconds.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to build desktop app. ExitCode={process.ExitCode}{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
        }
    }

    private static string ResolveExecutablePath(string projectPath, string buildConfiguration)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Could not determine project directory for '{projectPath}'.");
        var executablePath = Path.Combine(
            projectDirectory,
            "bin",
            buildConfiguration,
            DesktopTargetFramework,
            "DotnetDebug.Avalonia.exe");

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "Desktop app executable was not found. Verify TargetFramework and BuildConfiguration.",
                executablePath);
        }

        return executablePath;
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
