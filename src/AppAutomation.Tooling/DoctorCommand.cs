using System.Text.Json;

namespace AppAutomation.Tooling;

internal static class DoctorCommand
{
    public static int Run(string[] args)
    {
        var options = Parse(args);
        var issues = Analyze(options);

        foreach (var issue in issues)
        {
            var prefix = issue.Severity switch
            {
                DoctorIssueSeverity.Info => "info",
                DoctorIssueSeverity.Warning => "warn",
                DoctorIssueSeverity.Error => "error",
                _ => "info"
            };

            Console.WriteLine($"[{prefix}] {issue.Message}");
        }

        var errorCount = issues.Count(static issue => issue.Severity == DoctorIssueSeverity.Error);
        var warningCount = issues.Count(static issue => issue.Severity == DoctorIssueSeverity.Warning);
        Console.WriteLine();
        Console.WriteLine($"Doctor summary: {errorCount} error(s), {warningCount} warning(s), root={options.RepositoryRoot}");

        if (errorCount > 0)
        {
            return 1;
        }

        return options.Strict && warningCount > 0 ? 1 : 0;
    }

    private static DoctorOptions Parse(string[] args)
    {
        var repositoryRoot = Directory.GetCurrentDirectory();
        var strict = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--repo-root", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for --repo-root.");
                }

                repositoryRoot = Path.GetFullPath(args[++index]);
            }
            else if (string.Equals(argument, "--strict", StringComparison.OrdinalIgnoreCase))
            {
                strict = true;
            }
            else
            {
                throw new ArgumentException($"Unknown doctor argument '{argument}'.");
            }
        }

        return new DoctorOptions(repositoryRoot, strict);
    }

    private static IReadOnlyList<DoctorIssue> Analyze(DoctorOptions options)
    {
        var issues = new List<DoctorIssue>();
        if (!Directory.Exists(options.RepositoryRoot))
        {
            return
            [
                new DoctorIssue(DoctorIssueSeverity.Error, $"Repository root was not found: {options.RepositoryRoot}")
            ];
        }

        var projectPaths = Directory.EnumerateFiles(options.RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (projectPaths.Length == 0)
        {
            return
            [
                new DoctorIssue(DoctorIssueSeverity.Error, "No .csproj files were found under the specified repository root.")
            ];
        }

        var inspections = projectPaths.Select(ProjectInspection.Load).ToArray();
        var appAutomationConsumers = inspections
            .Where(static project => project.UsesAppAutomationPackage || project.UsesAppAutomationSourceDependency)
            .ToArray();

        if (appAutomationConsumers.Length == 0)
        {
            issues.Add(new DoctorIssue(
                DoctorIssueSeverity.Error,
                "No AppAutomation packages or source dependencies were found. Add PackageReference to AppAutomation.* or intentionally wire the framework before running doctor again."));
            return issues;
        }

        if (appAutomationConsumers.Any(static project => project.UsesAppAutomationSourceDependency))
        {
            issues.Add(new DoctorIssue(
                DoctorIssueSeverity.Warning,
                "Source dependency on AppAutomation was detected. Consumer repos should prefer NuGet packages and keep source-vendoring only as a fallback."));
        }
        else
        {
            issues.Add(new DoctorIssue(
                DoctorIssueSeverity.Info,
                "AppAutomation is referenced through packages only. NuGet-first adoption path looks healthy."));
        }

        ValidateTopology(appAutomationConsumers, issues);
        ValidateTargetFrameworks(appAutomationConsumers, issues);
        ValidateGeneratedScaffold(options.RepositoryRoot, appAutomationConsumers, issues);
        ValidateNuGetConfiguration(options.RepositoryRoot, issues);
        ValidateGlobalJson(options.RepositoryRoot, issues);

        return issues;
    }

    private static void ValidateTopology(IEnumerable<ProjectInspection> inspections, List<DoctorIssue> issues)
    {
        var projects = inspections.ToArray();
        if (!projects.Any(static project => project.IsAuthoring))
        {
            issues.Add(new DoctorIssue(DoctorIssueSeverity.Warning, "Canonical Authoring project was not detected."));
        }

        if (!projects.Any(static project => project.IsHeadless))
        {
            issues.Add(new DoctorIssue(DoctorIssueSeverity.Warning, "Canonical Headless project was not detected."));
        }

        if (!projects.Any(static project => project.IsFlaUi))
        {
            issues.Add(new DoctorIssue(DoctorIssueSeverity.Warning, "Canonical FlaUI project was not detected."));
        }

        if (!projects.Any(static project => project.IsTestHost))
        {
            issues.Add(new DoctorIssue(DoctorIssueSeverity.Warning, "Canonical TestHost project was not detected."));
        }
    }

    private static void ValidateTargetFrameworks(IEnumerable<ProjectInspection> inspections, List<DoctorIssue> issues)
    {
        foreach (var inspection in inspections)
        {
            if (inspection.IsFlaUi && inspection.TargetFrameworks.All(static tfm => !tfm.Contains("-windows", StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new DoctorIssue(
                    DoctorIssueSeverity.Error,
                    $"FlaUI project '{inspection.Name}' must target a Windows TFM. Current: {string.Join(", ", inspection.TargetFrameworks)}"));
            }

            foreach (var targetFramework in inspection.TargetFrameworks)
            {
                if (!targetFramework.StartsWith("net8.0", StringComparison.OrdinalIgnoreCase)
                    && !targetFramework.StartsWith("net10.0", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new DoctorIssue(
                        DoctorIssueSeverity.Warning,
                        $"Project '{inspection.Name}' targets '{targetFramework}'. The recommended AppAutomation compatibility path is net8.0 or net10.0."));
                }
            }
        }
    }

    private static void ValidateGeneratedScaffold(
        string repositoryRoot,
        IEnumerable<ProjectInspection> inspections,
        List<DoctorIssue> issues)
    {
        var markers = new[]
        {
            new ScaffoldMarker("REPLACE_WITH_YOUR_", "replace the generated REPLACE_WITH_YOUR_* placeholders"),
            new ScaffoldMarker("Reference your Avalonia App type here", "set AvaloniaAppType in the generated TestHost"),
            new ScaffoldMarker("Reference your Avalonia app and return the root Window instance here", "replace the headless launch placeholder in TestHost"),
            new ScaffoldMarker("TODO: Start your Avalonia Headless session and register it via HeadlessRuntime.SetSession(...).", "replace the generated HeadlessSessionHooks TODO"),
            new ScaffoldMarker("TODO: Dispose the Avalonia Headless session and clear HeadlessRuntime.SetSession(null).", "replace the generated HeadlessSessionHooks cleanup TODO")
        };

        var inspectedFiles = inspections
            .Select(static inspection => Path.GetDirectoryName(inspection.Path))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .SelectMany(static path => Directory.EnumerateFiles(path!, "*", SearchOption.AllDirectories))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static path => Path.GetExtension(path) is ".cs" or ".csproj" or ".md" or ".json")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var hits = new List<string>();
        foreach (var filePath in inspectedFiles)
        {
            var contents = File.ReadAllText(filePath);
            foreach (var marker in markers)
            {
                if (contents.Contains(marker.Token, StringComparison.Ordinal))
                {
                    hits.Add($"{Path.GetRelativePath(repositoryRoot, filePath)} ({marker.Description})");
                    break;
                }
            }
        }

        if (hits.Count == 0)
        {
            return;
        }

        issues.Add(new DoctorIssue(
            DoctorIssueSeverity.Warning,
            "Generated scaffold still contains placeholder markers:" + Environment.NewLine + string.Join(
                Environment.NewLine,
                hits.Select(static hit => $"  - {hit}"))));
    }

    private static void ValidateNuGetConfiguration(string repositoryRoot, List<DoctorIssue> issues)
    {
        var configPaths = Directory.EnumerateFiles(repositoryRoot, "NuGet.Config", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (configPaths.Length == 0)
        {
            issues.Add(new DoctorIssue(
                DoctorIssueSeverity.Warning,
                "NuGet.Config was not found. If your organization uses internal mirrors or packageSourceMapping, configure them before adopting AppAutomation packages."));
            return;
        }

        issues.Add(new DoctorIssue(
            DoctorIssueSeverity.Info,
            $"NuGet configuration detected in {configPaths.Length} location(s)."));
    }

    private static void ValidateGlobalJson(string repositoryRoot, List<DoctorIssue> issues)
    {
        var globalJsonPath = Path.Combine(repositoryRoot, "global.json");
        if (!File.Exists(globalJsonPath))
        {
            issues.Add(new DoctorIssue(
                DoctorIssueSeverity.Warning,
                "global.json was not found. Pinning the SDK is recommended for repeatable AppAutomation adoption."));
            return;
        }

        try
        {
            using var stream = File.OpenRead(globalJsonPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("sdk", out var sdkElement)
                || !sdkElement.TryGetProperty("version", out var versionElement))
            {
                issues.Add(new DoctorIssue(
                    DoctorIssueSeverity.Warning,
                    "global.json was found, but sdk.version is missing."));
                return;
            }

            var versionText = versionElement.GetString();
            if (!TryParseSdkVersion(versionText, out var version))
            {
                issues.Add(new DoctorIssue(
                    DoctorIssueSeverity.Warning,
                    $"global.json sdk.version '{versionText}' could not be parsed."));
                return;
            }

            if (version.Major < 8)
            {
                issues.Add(new DoctorIssue(
                    DoctorIssueSeverity.Error,
                    $"Pinned SDK {version} is too old for the recommended AppAutomation compatibility path."));
            }
            else
            {
                issues.Add(new DoctorIssue(
                    DoctorIssueSeverity.Info,
                    $"Pinned SDK detected: {version}."));
            }
        }
        catch (Exception ex)
        {
            issues.Add(new DoctorIssue(
                DoctorIssueSeverity.Warning,
                $"Failed to inspect global.json: {ex.Message}"));
        }
    }

    private static bool TryParseSdkVersion(string? versionText, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return false;
        }

        var stablePart = versionText.Split('-', 2, StringSplitOptions.TrimEntries)[0];
        if (!Version.TryParse(stablePart, out var parsedVersion) || parsedVersion is null)
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }

    private sealed record DoctorOptions(string RepositoryRoot, bool Strict);

    private sealed record ScaffoldMarker(string Token, string Description);
}
