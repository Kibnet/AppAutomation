using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Build.Tests;

public sealed class PublishNugetScriptTests
{
    [Test]
    public async Task PublishNuget_DoesNotRunPublishedConsumerVerificationAutomatically()
    {
        var repoRoot = GetRepoRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "eng", "publish-nuget.ps1"));

        await Assert.That(script).DoesNotContain("verify-published-consumer.ps1");
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
