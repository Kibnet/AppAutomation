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
        return CreateLaunchOptionsCore(descriptor, launchOptions, repositoryRoot, scenarioTransport: null);
    }

    public static DesktopAppLaunchOptions CreateLaunchOptions<TPayload>(
        AvaloniaDesktopAppDescriptor descriptor,
        AutomationLaunchScenario<TPayload> scenario,
        AvaloniaDesktopLaunchOptions? launchOptions = null,
        string? repositoryRoot = null)
    {
        var scenarioTransport = AutomationLaunchScenarioTransport.Create(scenario);
        try
        {
            return CreateLaunchOptionsCore(descriptor, launchOptions, repositoryRoot, scenarioTransport);
        }
        catch
        {
            scenarioTransport.Dispose();
            throw;
        }
    }

    private static DesktopAppLaunchOptions CreateLaunchOptionsCore(
        AvaloniaDesktopAppDescriptor descriptor,
        AvaloniaDesktopLaunchOptions? launchOptions,
        string? repositoryRoot,
        AutomationLaunchScenarioTransport.ScenarioTransport? scenarioTransport)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        launchOptions ??= new AvaloniaDesktopLaunchOptions();
        var buildConfiguration = ValidateValue(launchOptions.BuildConfiguration, nameof(launchOptions.BuildConfiguration));
        var isolatedBuildArtifacts = PrepareIsolatedBuildArtifacts(launchOptions);
        var buildOutputRoot = isolatedBuildArtifacts?.RootPath;

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
                launchOptions.BuildOncePerProcess,
                buildOutputRoot);
        }

        var executablePath = ResolveExecutablePath(projectPath, descriptor, buildConfiguration, buildOutputRoot);
        return new DesktopAppLaunchOptions
        {
            ExecutablePath = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            MainWindowTimeout = launchOptions.MainWindowTimeout,
            PollInterval = launchOptions.PollInterval,
            Arguments = launchOptions.Arguments,
            EnvironmentVariables = MergeEnvironmentVariables(
                launchOptions.EnvironmentVariables,
                scenarioTransport?.Context.ToEnvironmentVariables()),
            DisposeCallback = AutomationLaunchScenarioTransport.CombineCallbacks(
                scenarioTransport is null ? null : scenarioTransport.Dispose,
                isolatedBuildArtifacts is null ? null : isolatedBuildArtifacts.Dispose)
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
        bool buildOncePerProcess,
        string? buildOutputRoot)
    {
        var buildKey = $"{projectPath}|{buildConfiguration}|{descriptor.DesktopTargetFramework}|{buildOutputRoot}";

        lock (BuildLock)
        {
            if (buildOncePerProcess && BuiltProjectKeys.Contains(buildKey))
            {
                return;
            }

            RunBuild(solutionRoot, projectPath, descriptor.DesktopTargetFramework, buildConfiguration, buildTimeout, buildOutputRoot);
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
        TimeSpan buildTimeout,
        string? buildOutputRoot)
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

        if (!string.IsNullOrWhiteSpace(buildOutputRoot))
        {
            Directory.CreateDirectory(buildOutputRoot);
            processInfo.ArgumentList.Add("--artifacts-path");
            processInfo.ArgumentList.Add(buildOutputRoot);
        }

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
        string buildConfiguration,
        string? buildOutputRoot)
    {
        if (!string.IsNullOrWhiteSpace(buildOutputRoot))
        {
            return ResolveExecutablePathFromArtifacts(buildOutputRoot, projectPath, descriptor, buildConfiguration);
        }

        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Could not determine project directory for '{projectPath}'.");
        var executableBasePath = Path.Combine(projectDirectory, "bin");
        var executablePath = Path.Combine(executableBasePath, buildConfiguration, descriptor.DesktopTargetFramework, descriptor.ExecutableName);

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "Desktop app executable was not found. Verify TargetFramework, BuildConfiguration and ExecutableName.",
                executablePath);
        }

        return executablePath;
    }

    private static IsolatedBuildArtifacts? PrepareIsolatedBuildArtifacts(AvaloniaDesktopLaunchOptions launchOptions)
    {
        if (!launchOptions.UseIsolatedBuildOutput)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(launchOptions.IsolatedBuildRoot))
        {
            var temporaryDirectory = TemporaryDirectory.Create("AppAutomationDesktopBuild");
            return new IsolatedBuildArtifacts(temporaryDirectory.FullPath, temporaryDirectory.Dispose);
        }

        var rootPath = Path.GetFullPath(launchOptions.IsolatedBuildRoot);
        Directory.CreateDirectory(rootPath);
        return new IsolatedBuildArtifacts(rootPath, Dispose: null);
    }

    private static IReadOnlyDictionary<string, string?> MergeEnvironmentVariables(
        IReadOnlyDictionary<string, string?> existingVariables,
        IReadOnlyDictionary<string, string?>? additionalVariables)
    {
        if (additionalVariables is null || additionalVariables.Count == 0)
        {
            return existingVariables;
        }

        var merged = new Dictionary<string, string?>(existingVariables, StringComparer.Ordinal);
        foreach (var entry in additionalVariables)
        {
            merged[entry.Key] = entry.Value;
        }

        return merged;
    }

    private static string ValidateValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }

    private static string ResolveExecutablePathFromArtifacts(
        string artifactsRoot,
        string projectPath,
        AvaloniaDesktopAppDescriptor descriptor,
        string buildConfiguration)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var searchRoot = Path.Combine(Path.GetFullPath(artifactsRoot), "bin", projectName);
        if (!Directory.Exists(searchRoot))
        {
            throw new DirectoryNotFoundException(
                $"Artifacts output root was not found for project '{projectName}'. Expected '{searchRoot}'.");
        }

        var candidates = Directory.EnumerateFiles(searchRoot, descriptor.ExecutableName, SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new FileNotFoundException(
                "Desktop app executable was not found in artifacts output.",
                Path.Combine(searchRoot, descriptor.ExecutableName));
        }

        var configurationSegment = buildConfiguration.ToLowerInvariant();
        var preferredCandidates = candidates
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}{configurationSegment}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || path.Contains($"{Path.AltDirectorySeparatorChar}{configurationSegment}{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (preferredCandidates.Length == 1)
        {
            return preferredCandidates[0];
        }

        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        throw new InvalidOperationException(
            $"Multiple artifacts outputs matched '{descriptor.ExecutableName}':{Environment.NewLine}{string.Join(Environment.NewLine, candidates)}");
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

    private sealed record IsolatedBuildArtifacts(string RootPath, Action? Dispose);

}
