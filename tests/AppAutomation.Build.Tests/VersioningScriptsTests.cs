using System.Diagnostics;
using System.Xml.Linq;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Build.Tests;

public sealed class VersioningScriptsTests
{
    [Test]
    public async Task ResolvePackageVersion_ParsesReleaseTag()
    {
        var result = InvokePowerShellScript("-Tag", "appautomation-v2.1.0-preview.1");

        using (Assert.Multiple())
        {
            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.StandardOutput.Trim()).IsEqualTo("2.1.0-preview.1");
        }
    }

    [Test]
    public async Task ResolvePackageVersion_UsesExplicitVersion()
    {
        var result = InvokePowerShellScript("-Version", "2.1.0");

        using (Assert.Multiple())
        {
            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.StandardOutput.Trim()).IsEqualTo("2.1.0");
        }
    }

    [Test]
    public async Task ResolvePackageVersion_UsesConfiguredFallbackVersion()
    {
        var result = InvokePowerShellScript();

        using (Assert.Multiple())
        {
            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.StandardOutput.Trim()).IsEqualTo(ReadConfiguredVersion());
        }
    }

    [Test]
    public async Task ResolvePackageVersion_RejectsInvalidTag()
    {
        var result = InvokePowerShellScript("-Tag", "2.1.0");

        using (Assert.Multiple())
        {
            await Assert.That(result.ExitCode == 0).IsEqualTo(false);
            await Assert.That(result.CombinedOutput).Contains("appautomation-v");
        }
    }

    [Test]
    public async Task Repository_SourceFiles_DoNotContainLegacyNaming()
    {
        var legacyTerm = string.Concat("Easy", "Use");
        var matches = FindLegacyOccurrences(legacyTerm);
        if (matches.Count > 0)
        {
            throw new InvalidOperationException(
                "Legacy naming remains in: " + string.Join(", ", matches));
        }

        await Assert.That(matches.Count).IsEqualTo(0);
    }

    private static ScriptResult InvokePowerShellScript(params string[] arguments)
    {
        var repoRoot = GetRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "eng", "resolve-package-version.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start PowerShell process.");

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ScriptResult(process.ExitCode, standardOutput, standardError);
    }

    private static string ReadConfiguredVersion()
    {
        var repoRoot = GetRepoRoot();
        var document = XDocument.Load(Path.Combine(repoRoot, "eng", "Versions.props"));
        var propertyGroup = document.Root?.Element("PropertyGroup")
            ?? throw new InvalidOperationException("PropertyGroup was not found in eng/Versions.props.");
        var versionElement = propertyGroup.Element("AppAutomationVersion")
            ?? throw new InvalidOperationException("AppAutomationVersion was not found in eng/Versions.props.");
        return versionElement.Value.Trim();
    }

    private static IReadOnlyList<string> FindLegacyOccurrences(string legacyTerm)
    {
        var repoRoot = GetRepoRoot();
        var matches = new List<string>();
        foreach (var path in EnumerateSearchTargets(repoRoot))
        {
            if (File.ReadAllText(path).Contains(legacyTerm, StringComparison.Ordinal))
            {
                matches.Add(Path.GetRelativePath(repoRoot, path));
            }
        }

        return matches;
    }

    private static IEnumerable<string> EnumerateSearchTargets(string repoRoot)
    {
        foreach (var filePath in new[]
                 {
                     Path.Combine(repoRoot, "README.md"),
                     Path.Combine(repoRoot, "ControlSupportMatrix.md"),
                     Path.Combine(repoRoot, "Directory.Build.props"),
                     Path.Combine(repoRoot, "Directory.Build.targets")
                 })
        {
            if (File.Exists(filePath))
            {
                yield return filePath;
            }
        }

        foreach (var directoryPath in new[]
                 {
                     Path.Combine(repoRoot, ".github"),
                     Path.Combine(repoRoot, "docs"),
                     Path.Combine(repoRoot, "eng"),
                     Path.Combine(repoRoot, "sample"),
                     Path.Combine(repoRoot, "src")
                 })
        {
            if (!Directory.Exists(directoryPath))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                if (IsSearchTarget(filePath))
                {
                    yield return filePath;
                }
            }
        }
    }

    private static bool IsSearchTarget(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(static segment => segment is "bin" or "obj" or ".git" or ".vs" or "artifacts"))
        {
            return false;
        }

        return Path.GetExtension(path) switch
        {
            ".cs" => true,
            ".csproj" => true,
            ".json" => true,
            ".md" => true,
            ".props" => true,
            ".ps1" => true,
            ".sln" => true,
            ".targets" => true,
            ".yaml" => true,
            ".yml" => true,
            _ => false
        };
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

    private sealed record ScriptResult(
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
    }
}
