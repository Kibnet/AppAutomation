using System.Text.RegularExpressions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Build.Tests;

public sealed partial class TemplateContentTests
{
    private const string VersionPlaceholder = "__APPAUTOMATION_VERSION__";

    [Test]
    public async Task AvaloniaConsumerTemplate_UsesVersionPlaceholder_ForAppAutomationPackages()
    {
        var projectFiles = GetTemplateProjectFiles().ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(projectFiles.Length).IsEqualTo(4);
        }

        foreach (var projectFile in projectFiles)
        {
            var contents = File.ReadAllText(projectFile);
            var relativePath = Path.GetRelativePath(GetRepoRoot(), projectFile);
            var versions = AppAutomationPackageVersionRegex()
                .Matches(contents)
                .Select(static match => match.Groups["version"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            await Assert.That(versions).Contains(VersionPlaceholder)
                .Because($"Template project '{relativePath}' must keep package version tokenized.");

            await Assert.That(versions.Any(static version => ConcretePackageVersionRegex().IsMatch(version))).IsEqualTo(false)
                .Because($"Template project '{relativePath}' must not hardcode an AppAutomation package version.");
        }
    }

    private static IEnumerable<string> GetTemplateProjectFiles()
    {
        var repoRoot = GetRepoRoot();
        var templateRoot = Path.Combine(
            repoRoot,
            "src",
            "AppAutomation.Templates",
            "content",
            "AppAutomation.Avalonia.Consumer",
            "tests");

        return Directory.EnumerateFiles(templateRoot, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal);
    }

    private static string GetRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AppAutomation.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AppAutomation.sln.");
    }

    [GeneratedRegex("PackageReference Include=\"AppAutomation\\.[^\"]+\" Version=\"(?<version>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AppAutomationPackageVersionRegex();

    [GeneratedRegex("^\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ConcretePackageVersionRegex();
}
