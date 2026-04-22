using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Build.Tests;

public sealed class PublishNugetScriptTests
{
    [Test]
    public async Task PublishNuget_DoesNotRunPublishedConsumerVerificationAutomatically()
    {
        var script = ReadPublishNugetScript();

        await Assert.That(script).DoesNotContain("verify-published-consumer.ps1");
    }

    [Test]
    public async Task PublishNuget_DisablesImplicitSymbolPush_WhenSymbolPackagesExist()
    {
        var script = ReadPublishNugetScript();

        await Assert.That(script).Contains("$disableImplicitSymbolPush = $symbolPackages.Count -gt 0");
        await Assert.That(script).Contains("$disableImplicitSymbolPush");
        await Assert.That(script).Contains("if ($disableImplicitSymbolPush)");
        await Assert.That(script).Contains("\"--no-symbols\"");
    }

    [Test]
    public async Task PublishNuget_ResolvesSymbolPackages_BeforeMainPackagePush()
    {
        var script = ReadPublishNugetScript();
        var symbolPackagesIndex = script.IndexOf("$symbolPackages = Get-ChildItem", StringComparison.Ordinal);
        var packageLoopIndex = script.IndexOf("foreach ($package in $packageFiles)", StringComparison.Ordinal);

        await Assert.That(symbolPackagesIndex >= 0).IsEqualTo(true)
            .Because("publish-nuget.ps1 must know whether symbols are handled separately before pushing .nupkg files.");

        await Assert.That(packageLoopIndex > symbolPackagesIndex).IsEqualTo(true)
            .Because("the .nupkg push must add --no-symbols when symbol packages are present.");
    }

    private static string ReadPublishNugetScript()
    {
        var repoRoot = GetRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot, "eng", "publish-nuget.ps1"));
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
