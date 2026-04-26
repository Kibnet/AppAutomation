using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Build.Tests;

public sealed class ReleaseWorkflowTests
{
    [Test]
    public async Task PublishPackagesWorkflow_KeepsReleasePublishedAsCanonicalTrigger()
    {
        var workflow = ReadPublishPackagesWorkflow();
        var triggerBlock = ExtractBetween(workflow, "on:", "jobs:");

        using (Assert.Multiple())
        {
            await Assert.That(triggerBlock).Contains("release:");
            await Assert.That(triggerBlock).Contains("types:");
            await Assert.That(triggerBlock).Contains("- published");
            await Assert.That(triggerBlock).DoesNotContain("  push:");
        }
    }

    [Test]
    public async Task PublishPackagesWorkflow_ManualRecovery_RequiresExistingRelease()
    {
        var workflow = ReadPublishPackagesWorkflow();

        using (Assert.Multiple())
        {
            await Assert.That(workflow).Contains("Resolve existing GitHub release target");
            await Assert.That(workflow).Contains("/releases/tags/");
            await Assert.That(workflow).Contains("No existing GitHub release was found for version");
        }
    }

    [Test]
    public async Task PublishPackagesWorkflow_AttachesAssets_OnReleaseAndManualPublish()
    {
        var workflow = ReadPublishPackagesWorkflow();
        const string publishCondition = "github.event_name == 'release' || (github.event_name == 'workflow_dispatch' && github.event.inputs.publish == 'true')";

        using (Assert.Multiple())
        {
            await Assert.That(workflow).Contains("Attach NuGet packages to existing GitHub release");
            await Assert.That(workflow).Contains(publishCondition);
            await Assert.That(workflow).Contains("gh release upload");
            await Assert.That(workflow).Contains("--clobber");
            await Assert.That(workflow).DoesNotContain("softprops/action-gh-release");
        }
    }

    private static string ReadPublishPackagesWorkflow()
    {
        var repoRoot = GetRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "publish-packages.yml");
        return File.ReadAllText(workflowPath).ReplaceLineEndings("\n");
    }

    private static string ExtractBetween(string text, string startMarker, string endMarker)
    {
        var startIndex = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            throw new InvalidOperationException($"Start marker '{startMarker}' was not found.");
        }

        var endIndex = text.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            throw new InvalidOperationException($"End marker '{endMarker}' was not found.");
        }

        return text[startIndex..endIndex];
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
