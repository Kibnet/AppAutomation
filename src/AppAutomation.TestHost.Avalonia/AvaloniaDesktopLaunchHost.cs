using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security;
using System.Text.Json;
using System.Text;
using AppAutomation.Session.Contracts;

namespace AppAutomation.TestHost.Avalonia;

public static class AvaloniaDesktopLaunchHost
{
    private static readonly object BuildLock = new();
    private static readonly object IsolatedBuildArtifactsLock = new();
    private static readonly HashSet<string> BuiltProjectKeys = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SharedIsolatedBuildArtifacts> SharedIsolatedBuildArtifactsByKey =
        new(StringComparer.OrdinalIgnoreCase);
    private static bool _isolatedBuildCleanupRegistered;

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

        var resolvedRepositoryRoot = string.IsNullOrWhiteSpace(repositoryRoot)
            ? FindRepositoryRoot(descriptor)
            : Path.GetFullPath(repositoryRoot);
        var projectPath = ResolveDesktopProjectPath(resolvedRepositoryRoot, descriptor);
        var isolatedBuildArtifacts = PrepareIsolatedBuildArtifacts(
            launchOptions,
            projectPath,
            descriptor.DesktopTargetFramework,
            buildConfiguration);
        var buildOutputRoot = isolatedBuildArtifacts?.RootPath;

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
        var dotnetExecutablePath = ResolveDotnetExecutablePath(solutionRoot);
        var processInfo = new ProcessStartInfo
        {
            FileName = dotnetExecutablePath,
            WorkingDirectory = solutionRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (Path.IsPathRooted(dotnetExecutablePath))
        {
            processInfo.Environment["DOTNET_HOST_PATH"] = dotnetExecutablePath;

            var dotnetRoot = Path.GetDirectoryName(dotnetExecutablePath);
            if (!string.IsNullOrWhiteSpace(dotnetRoot))
            {
                processInfo.Environment["DOTNET_ROOT"] = dotnetRoot;
            }
        }

        processInfo.ArgumentList.Add("build");
        processInfo.ArgumentList.Add(projectPath);
        processInfo.ArgumentList.Add("-c");
        processInfo.ArgumentList.Add(buildConfiguration);
        processInfo.ArgumentList.Add("-f");
        processInfo.ArgumentList.Add(targetFramework);

        if (!string.IsNullOrWhiteSpace(buildOutputRoot))
        {
            Directory.CreateDirectory(buildOutputRoot);
            var isolatedBuildPropsPath = EnsureIsolatedBuildPropsFile(solutionRoot, buildOutputRoot);
            processInfo.ArgumentList.Add($"-p:DirectoryBuildPropsPath={isolatedBuildPropsPath}");
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

    private static string ResolveDotnetExecutablePath(string solutionRoot)
    {
        var pinnedSdkVersion = TryReadPinnedSdkVersion(solutionRoot);
        foreach (var candidate in EnumerateDotnetExecutableCandidates(solutionRoot))
        {
            if (IsUsableDotnetHost(candidate, pinnedSdkVersion))
            {
                return Path.GetFullPath(candidate!);
            }
        }

        return "dotnet";
    }

    private static IEnumerable<string?> EnumerateDotnetExecutableCandidates(string solutionRoot)
    {
        yield return Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

        var resolverCliDirectory = Environment.GetEnvironmentVariable("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR");
        if (!string.IsNullOrWhiteSpace(resolverCliDirectory))
        {
            yield return Path.Combine(resolverCliDirectory, GetDotnetExecutableName());
        }

        yield return TryGetParentDotnetHostPath();

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            yield return Path.Combine(dotnetRoot, GetDotnetExecutableName());
        }

        yield return Path.Combine(solutionRoot, ".dotnet", GetDotnetExecutableName());
    }

    private static string GetDotnetExecutableName()
    {
        return OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    }

    private static bool IsUsableDotnetHost(string? path, string? pinnedSdkVersion)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = Path.GetFullPath(path);
        if (!File.Exists(normalizedPath))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(pinnedSdkVersion))
        {
            return true;
        }

        var dotnetRoot = Path.GetDirectoryName(normalizedPath);
        if (string.IsNullOrWhiteSpace(dotnetRoot))
        {
            return false;
        }

        return Directory.Exists(Path.Combine(dotnetRoot, "sdk", pinnedSdkVersion));
    }

    private static string? TryReadPinnedSdkVersion(string solutionRoot)
    {
        var globalJsonPath = Path.Combine(solutionRoot, "global.json");
        if (!File.Exists(globalJsonPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(globalJsonPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("sdk", out var sdkElement))
            {
                return null;
            }

            if (!sdkElement.TryGetProperty("version", out var versionElement))
            {
                return null;
            }

            return versionElement.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetParentDotnetHostPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            var processInfo = new ProcessBasicInformation();
            var status = NtQueryInformationProcess(
                currentProcess.Handle,
                processInformationClass: 0,
                ref processInfo,
                Marshal.SizeOf<ProcessBasicInformation>(),
                out _);
            if (status != 0)
            {
                return null;
            }

            var parentProcessId = processInfo.InheritedFromUniqueProcessId.ToInt32();
            if (parentProcessId <= 0)
            {
                return null;
            }

            using var parentProcess = Process.GetProcessById(parentProcessId);
            var parentPath = parentProcess.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(parentPath))
            {
                return null;
            }

            return string.Equals(
                Path.GetFileName(parentPath),
                GetDotnetExecutableName(),
                StringComparison.OrdinalIgnoreCase)
                ? parentPath
                : null;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
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

    private static IsolatedBuildArtifacts? PrepareIsolatedBuildArtifacts(
        AvaloniaDesktopLaunchOptions launchOptions,
        string projectPath,
        string targetFramework,
        string buildConfiguration)
    {
        if (!launchOptions.UseIsolatedBuildOutput)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(launchOptions.IsolatedBuildRoot))
        {
            return AcquireSharedIsolatedBuildArtifacts(
                projectPath,
                targetFramework,
                buildConfiguration,
                keepAlive: launchOptions.BuildOncePerProcess);
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

    private static IsolatedBuildArtifacts AcquireSharedIsolatedBuildArtifacts(
        string projectPath,
        string targetFramework,
        string buildConfiguration,
        bool keepAlive)
    {
        lock (IsolatedBuildArtifactsLock)
        {
            EnsureIsolatedBuildCleanupRegistered();

            var normalizedProjectPath = Path.GetFullPath(projectPath);
            var registryKey = $"{normalizedProjectPath}|{targetFramework}|{buildConfiguration}";
            if (!SharedIsolatedBuildArtifactsByKey.TryGetValue(registryKey, out var sharedArtifacts))
            {
                sharedArtifacts = new SharedIsolatedBuildArtifacts(
                    registryKey,
                    CreateSharedIsolatedBuildRoot(registryKey));
                SharedIsolatedBuildArtifactsByKey.Add(registryKey, sharedArtifacts);
            }

            if (keepAlive)
            {
                sharedArtifacts.MarkKeepAlive();
            }

            sharedArtifacts.LeaseCount++;
            return new IsolatedBuildArtifacts(
                sharedArtifacts.RootPath,
                () => ReleaseSharedIsolatedBuildArtifacts(sharedArtifacts.RegistryKey));
        }
    }

    private static string CreateSharedIsolatedBuildRoot(string registryKey)
    {
        var processQualifiedKey = $"{Environment.ProcessId}|{registryKey}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(processQualifiedKey))).Substring(0, 12);
        var rootPath = Path.Combine(Path.GetTempPath(), $"AppAutomationDesktopBuild-p{Environment.ProcessId}-{hash}");
        Directory.CreateDirectory(rootPath);
        return rootPath;
    }

    private static void ReleaseSharedIsolatedBuildArtifacts(string registryKey)
    {
        SharedIsolatedBuildArtifacts? releasedArtifacts = null;

        lock (IsolatedBuildArtifactsLock)
        {
            if (!SharedIsolatedBuildArtifactsByKey.TryGetValue(registryKey, out var sharedArtifacts))
            {
                return;
            }

            if (sharedArtifacts.LeaseCount > 0)
            {
                sharedArtifacts.LeaseCount--;
            }

            if (sharedArtifacts.KeepAlive || sharedArtifacts.LeaseCount > 0)
            {
                return;
            }

            SharedIsolatedBuildArtifactsByKey.Remove(registryKey);
            releasedArtifacts = sharedArtifacts;
        }

        if (releasedArtifacts is not null)
        {
            TryDeleteDirectory(releasedArtifacts.RootPath);
        }
    }

    private static void EnsureIsolatedBuildCleanupRegistered()
    {
        if (_isolatedBuildCleanupRegistered)
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += static (_, _) => CleanupSharedIsolatedBuildArtifacts();
        _isolatedBuildCleanupRegistered = true;
    }

    private static void CleanupSharedIsolatedBuildArtifacts()
    {
        SharedIsolatedBuildArtifacts[] artifactsToCleanup;
        lock (IsolatedBuildArtifactsLock)
        {
            artifactsToCleanup = SharedIsolatedBuildArtifactsByKey.Values.ToArray();
            SharedIsolatedBuildArtifactsByKey.Clear();
        }

        foreach (var artifacts in artifactsToCleanup)
        {
            TryDeleteDirectory(artifacts.RootPath);
        }
    }

    private static void TryDeleteDirectory(string rootPath)
    {
        try
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for auto-created isolated build roots.
        }
    }

    private static string EnsureIsolatedBuildPropsFile(string solutionRoot, string buildOutputRoot)
    {
        var normalizedBuildRoot = Path.GetFullPath(buildOutputRoot);
        Directory.CreateDirectory(normalizedBuildRoot);

        var buildPropsPath = Path.Combine(normalizedBuildRoot, "AppAutomation.IsolatedBuild.props");
        var repositoryBuildPropsPath = Path.Combine(solutionRoot, "Directory.Build.props");
        var baseOutputRoot = EscapeBuildPath(Path.Combine(normalizedBuildRoot, "bin")) + @"\";
        var baseIntermediateRoot = EscapeBuildPath(Path.Combine(normalizedBuildRoot, "obj")) + @"\";
        var importLine = File.Exists(repositoryBuildPropsPath)
            ? $"  <Import Project=\"{EscapeBuildPath(repositoryBuildPropsPath)}\" />{Environment.NewLine}"
            : string.Empty;

        var content = $$"""
<Project>
{{importLine}}  <PropertyGroup>
    <BaseOutputPath>{{baseOutputRoot}}$(MSBuildProjectName)\</BaseOutputPath>
    <BaseIntermediateOutputPath>{{baseIntermediateRoot}}$(MSBuildProjectName)\</BaseIntermediateOutputPath>
    <MSBuildProjectExtensionsPath>{{baseIntermediateRoot}}$(MSBuildProjectName)\</MSBuildProjectExtensionsPath>
    <DefaultItemExcludes>$(DefaultItemExcludes);$(MSBuildProjectDirectory)\bin\**;$(MSBuildProjectDirectory)\obj\**</DefaultItemExcludes>
  </PropertyGroup>
</Project>
""";

        if (!File.Exists(buildPropsPath) || !string.Equals(File.ReadAllText(buildPropsPath), content, StringComparison.Ordinal))
        {
            File.WriteAllText(buildPropsPath, content);
        }

        return buildPropsPath;
    }

    private static string EscapeBuildPath(string path)
    {
        return SecurityElement.Escape(Path.GetFullPath(path)) ?? Path.GetFullPath(path);
    }

    private sealed record IsolatedBuildArtifacts(string RootPath, Action? Dispose);

    private sealed class SharedIsolatedBuildArtifacts
    {
        public SharedIsolatedBuildArtifacts(string registryKey, string rootPath)
        {
            RegistryKey = registryKey;
            RootPath = rootPath;
        }

        public string RegistryKey { get; }

        public string RootPath { get; }

        public int LeaseCount { get; set; }

        public bool KeepAlive { get; private set; }

        public void MarkKeepAlive()
        {
            KeepAlive = true;
        }
    }

}
