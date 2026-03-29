using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Build.Tests;

public sealed class ConsumerDocsTests
{
    [Test]
    public async Task ConsumerDocs_UseLatestByDefaultForOnboarding_AndPinnedVersionsForRelease()
    {
        var repoRoot = GetRepoRoot();
        var configuredVersion = ReadConfiguredVersion(repoRoot);
        var readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
        var quickstart = File.ReadAllText(Path.Combine(repoRoot, "docs", "appautomation", "quickstart.md"));
        var publishing = File.ReadAllText(Path.Combine(repoRoot, "docs", "appautomation", "publishing.md"));

        using (Assert.Multiple())
        {
            await Assert.That(ContainsStandaloneCommand(readme, "dotnet new install AppAutomation.Templates")).IsEqualTo(true);
            await Assert.That(ContainsStandaloneCommand(quickstart, "dotnet new install AppAutomation.Templates")).IsEqualTo(true);
            await Assert.That(readme).Contains($"AppAutomation.Templates@{configuredVersion}");
            await Assert.That(quickstart).Contains($"AppAutomation.Templates@{configuredVersion}");
            await Assert.That(readme).DoesNotContain("AppAutomation.Templates::");
            await Assert.That(quickstart).DoesNotContain("AppAutomation.Templates::");
            await Assert.That(readme).Contains("dotnet new tool-manifest");
            await Assert.That(quickstart).Contains("dotnet new tool-manifest");
            await Assert.That(ContainsStandaloneCommand(readme, "dotnet tool install AppAutomation.Tooling")).IsEqualTo(true);
            await Assert.That(ContainsStandaloneCommand(quickstart, "dotnet tool install AppAutomation.Tooling")).IsEqualTo(true);
            await Assert.That(readme).Contains($"dotnet tool install AppAutomation.Tooling --version {configuredVersion}");
            await Assert.That(quickstart).Contains($"dotnet tool install AppAutomation.Tooling --version {configuredVersion}");
            await Assert.That(ContainsStandaloneCommand(readme, "dotnet new appauto-avalonia --name MyApp")).IsEqualTo(true);
            await Assert.That(ContainsStandaloneCommand(quickstart, "dotnet new appauto-avalonia --name MyApp")).IsEqualTo(true);
            await Assert.That(readme).Contains($"--AppAutomationVersion {configuredVersion}");
            await Assert.That(quickstart).Contains($"--AppAutomationVersion {configuredVersion}");
            await Assert.That(readme).Contains("dotnet tool run appautomation doctor --repo-root .");
            await Assert.That(quickstart).Contains("dotnet tool run appautomation doctor --repo-root .");
            await Assert.That(readme).Contains("dotnet test --project");
            await Assert.That(quickstart).Contains("dotnet test --project");
            await Assert.That(readme).Contains("dotnet test --solution");
            await Assert.That(quickstart).Contains("dotnet test --solution");
            await Assert.That(readme).Contains("Headless session is not initialized");
            await Assert.That(quickstart).Contains("Headless session is not initialized");
            await Assert.That(publishing).Contains("eng/sync-consumer-assets.ps1");
            await Assert.That(publishing).Contains("eng/verify-published-consumer.ps1");
            await Assert.That(publishing).Contains($"-Version {configuredVersion}");
            await Assert.That(publishing).Contains("scripted completion");
            await Assert.That(publishing).Contains("untouched `dotnet new` output");
            await Assert.That(publishing).Contains("dotnet tool run appautomation doctor --strict");
        }
    }

    [Test]
    public async Task TemplateConfig_DefaultVersion_MatchesConfiguredVersion()
    {
        var repoRoot = GetRepoRoot();
        var configuredVersion = ReadConfiguredVersion(repoRoot);
        var templatePath = Path.Combine(
            repoRoot,
            "src",
            "AppAutomation.Templates",
            "content",
            "AppAutomation.Avalonia.Consumer",
            ".template.config",
            "template.json");

        var rawTemplate = File.ReadAllText(templatePath);
        using var document = JsonDocument.Parse(rawTemplate);
        var symbols = document.RootElement.GetProperty("symbols");
        var versionDefault = symbols.GetProperty("AppAutomationVersion").GetProperty("defaultValue").GetString();
        var flaUiTargetFrameworkDefault = symbols.GetProperty("FlaUiTargetFramework").GetProperty("defaultValue").GetString();

        using (Assert.Multiple())
        {
            await Assert.That(versionDefault).IsEqualTo(configuredVersion);
            await Assert.That(flaUiTargetFrameworkDefault).IsEqualTo("net8.0-windows7.0");
        }
    }

    [Test]
    public async Task GeneratedHeadlessScaffold_IsExecutableAndNotTodoBased()
    {
        var repoRoot = GetRepoRoot();
        var hooksPath = Path.Combine(
            repoRoot,
            "src",
            "AppAutomation.Templates",
            "content",
            "AppAutomation.Avalonia.Consumer",
            "tests",
            "SampleApp.UiTests.Headless",
            "Infrastructure",
            "HeadlessSessionHooks.cs");
        var nextStepsPath = Path.Combine(
            repoRoot,
            "src",
            "AppAutomation.Templates",
            "content",
            "AppAutomation.Avalonia.Consumer",
            "APPAUTOMATION_NEXT_STEPS.md");

        var hooks = File.ReadAllText(hooksPath);
        var nextSteps = File.ReadAllText(nextStepsPath);

        using (Assert.Multiple())
        {
            await Assert.That(hooks).DoesNotContain("TODO:");
            await Assert.That(hooks).Contains("HeadlessUnitTestSession.StartNew");
            await Assert.That(hooks).Contains("HeadlessRuntime.SetSession");
            await Assert.That(nextSteps).Contains("AvaloniaAppType");
            await Assert.That(nextSteps).Contains("dotnet tool run appautomation doctor --repo-root . --strict");
        }
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

    private static string ReadConfiguredVersion(string repoRoot)
    {
        var document = XDocument.Load(Path.Combine(repoRoot, "eng", "Versions.props"));
        var propertyGroup = document.Root?.Element("PropertyGroup")
            ?? throw new InvalidOperationException("PropertyGroup was not found in eng/Versions.props.");
        var versionElement = propertyGroup.Element("AppAutomationVersion")
            ?? throw new InvalidOperationException("AppAutomationVersion was not found in eng/Versions.props.");
        return versionElement.Value.Trim();
    }

    private static bool ContainsStandaloneCommand(string content, string command)
    {
        var escapedCommand = Regex.Escape(command);
        return Regex.IsMatch(content, $@"(?m)^\s*{escapedCommand}\s*$");
    }
}
