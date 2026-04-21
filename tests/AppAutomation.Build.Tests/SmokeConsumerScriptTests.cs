using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Build.Tests;

public sealed class SmokeConsumerScriptTests
{
    [Test]
    public async Task SmokeConsumer_RuntimeProject_IsExecutableTestProject()
    {
        var repoRoot = GetRepoRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "eng", "smoke-consumer.ps1"));
        var runtimeProject = ExtractRuntimeProjectTemplate(script);

        using (Assert.Multiple())
        {
            await Assert.That(runtimeProject).Contains("<OutputType>Exe</OutputType>");
            await Assert.That(runtimeProject).Contains("<IsTestProject>true</IsTestProject>");
        }
    }

    [Test]
    public async Task SmokeConsumer_RestoresLocalToolBeforeRunningDoctor()
    {
        var repoRoot = GetRepoRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "eng", "smoke-consumer.ps1"));

        var isolatedCliHome = script.IndexOf("$env:DOTNET_CLI_HOME = $dotnetCliHome", StringComparison.Ordinal);
        var toolManifest = script.IndexOf("\"new\", \"tool-manifest\"", StringComparison.Ordinal);
        var toolManifestOutput = script.IndexOf("\"--output\", \".config\"", StringComparison.Ordinal);
        var toolInstall = script.IndexOf("\"tool\", \"install\"", StringComparison.Ordinal);
        var toolRestore = script.IndexOf("\"tool\", \"restore\"", StringComparison.Ordinal);
        var toolRunDoctor = script.IndexOf("\"tool\", \"run\", \"appautomation\", \"--\", \"doctor\"", StringComparison.Ordinal);

        using (Assert.Multiple())
        {
            await Assert.That(isolatedCliHome >= 0).IsEqualTo(true);
            await Assert.That(toolManifest > isolatedCliHome).IsEqualTo(true);
            await Assert.That(toolManifest >= 0).IsEqualTo(true);
            await Assert.That(toolManifestOutput > toolManifest).IsEqualTo(true);
            await Assert.That(toolInstall > toolManifestOutput).IsEqualTo(true);
            await Assert.That(toolRestore > toolInstall).IsEqualTo(true);
            await Assert.That(toolRunDoctor > toolRestore).IsEqualTo(true);
        }
    }

    private static string ExtractRuntimeProjectTemplate(string script)
    {
        const string runtimeProjectWriteMarker = "Set-Content -Path $runtimeProjectPath";
        const string projectStartMarker = "<Project Sdk=\"Microsoft.NET.Sdk\">";
        const string projectEndMarker = "</Project>";

        var runtimeProjectWrite = script.IndexOf(runtimeProjectWriteMarker, StringComparison.Ordinal);
        if (runtimeProjectWrite < 0)
        {
            throw new InvalidOperationException("Could not locate smoke consumer runtime project write.");
        }

        var projectStart = script.LastIndexOf(projectStartMarker, runtimeProjectWrite, StringComparison.Ordinal);
        if (projectStart < 0)
        {
            throw new InvalidOperationException("Could not locate smoke consumer runtime project template.");
        }

        var projectEnd = script.IndexOf(projectEndMarker, projectStart, StringComparison.Ordinal);
        if (projectEnd < 0)
        {
            throw new InvalidOperationException("Could not locate the end of the smoke consumer runtime project template.");
        }

        projectEnd += projectEndMarker.Length;
        return script[projectStart..projectEnd];
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
}
