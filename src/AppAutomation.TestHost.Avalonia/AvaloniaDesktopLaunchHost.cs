using System.Diagnostics;
using AppAutomation.Session.Contracts;

namespace AppAutomation.TestHost.Avalonia;

public static class AvaloniaDesktopLaunchHost
{
    private static readonly object BuildLock = new();
    private static readonly HashSet<string> BuiltProjectKeys = new(StringComparer.OrdinalIgnoreCase);

    public static DesktopAppLaunchOptions CreateLaunchOptions(
        AvaloniaDesktopAppDescriptor descriptor,
        AvaloniaDesktopLaunchOptions? launchOptions = null,
        string? repositoryRoot = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        launchOptions ??= new AvaloniaDesktopLaunchOptions();
        var buildConfiguration = ValidateValue(launchOptions.BuildConfiguration, nameof(launchOptions.BuildConfiguration));

        var resolvedRepositoryRoot = string.IsNullOrWhiteSpace(repositoryRoot)
            ? FindRepositoryRoot(descriptor)
            : Path.GetFullPath(repositoryRoot);
        var projectPath = ResolveDesktopProjectPath(resolvedRepositoryRoot, descriptor);

        if (launchOptions.BuildBeforeLaunch)
        {
            EnsureProjectBuilt(
                resolvedRepositoryRoot,
                projectPath,
                descriptor,
                buildConfiguration,
                launchOptions.BuildTimeout,
                launchOptions.BuildOncePerProcess);
        }

        var executablePath = ResolveExecutablePath(projectPath, descriptor, buildConfiguration);
        return new DesktopAppLaunchOptions
        {
            ExecutablePath = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            MainWindowTimeout = launchOptions.MainWindowTimeout,
            PollInterval = launchOptions.PollInterval,
            Arguments = launchOptions.Arguments,
            EnvironmentVariables = launchOptions.EnvironmentVariables
        };
    }

    public static string FindRepositoryRoot(AvaloniaDesktopAppDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

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

                if (HasRepositoryMarkers(current.FullName, descriptor))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root for Avalonia desktop app.");
    }

    private static bool HasRepositoryMarkers(string rootPath, AvaloniaDesktopAppDescriptor descriptor)
    {
        foreach (var solutionFileName in descriptor.SolutionFileNames)
        {
            if (File.Exists(Path.Combine(rootPath, solutionFileName)))
            {
                return true;
            }
        }

        foreach (var relativeProjectPath in descriptor.DesktopProjectRelativePaths)
        {
            if (File.Exists(Path.Combine(rootPath, relativeProjectPath)))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveDesktopProjectPath(string repositoryRoot, AvaloniaDesktopAppDescriptor descriptor)
    {
        foreach (var relativeProjectPath in descriptor.DesktopProjectRelativePaths)
        {
            var candidatePath = Path.GetFullPath(Path.Combine(repositoryRoot, relativeProjectPath));
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        throw new FileNotFoundException(
            "Desktop project file was not found.",
            string.Join(" | ", descriptor.DesktopProjectRelativePaths.Select(path => Path.Combine(repositoryRoot, path))));
    }

    private static void EnsureProjectBuilt(
        string solutionRoot,
        string projectPath,
        AvaloniaDesktopAppDescriptor descriptor,
        string buildConfiguration,
        TimeSpan buildTimeout,
        bool buildOncePerProcess)
    {
        var buildKey = $"{projectPath}|{buildConfiguration}|{descriptor.DesktopTargetFramework}";

        lock (BuildLock)
        {
            if (buildOncePerProcess && BuiltProjectKeys.Contains(buildKey))
            {
                return;
            }

            RunBuild(solutionRoot, projectPath, descriptor.DesktopTargetFramework, buildConfiguration, buildTimeout);
            if (buildOncePerProcess)
            {
                BuiltProjectKeys.Add(buildKey);
            }
        }
    }

    private static void RunBuild(
        string solutionRoot,
        string projectPath,
        string targetFramework,
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
        processInfo.ArgumentList.Add(targetFramework);

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

    private static string ResolveExecutablePath(
        string projectPath,
        AvaloniaDesktopAppDescriptor descriptor,
        string buildConfiguration)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Could not determine project directory for '{projectPath}'.");
        var executablePath = Path.Combine(
            projectDirectory,
            "bin",
            buildConfiguration,
            descriptor.DesktopTargetFramework,
            descriptor.ExecutableName);

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "Desktop app executable was not found. Verify TargetFramework, BuildConfiguration and ExecutableName.",
                executablePath);
        }

        return executablePath;
    }

    private static string ValidateValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
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
