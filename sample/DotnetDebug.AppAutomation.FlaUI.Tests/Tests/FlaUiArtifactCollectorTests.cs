using AppAutomation.Abstractions;
using DotnetDebug.AppAutomation.TestHost;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Session;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

public sealed class FlaUiArtifactCollectorTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task CollectAsync_ReturnsFlaUiSpecificArtifacts()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions());
        var resolver = new FlaUiControlResolver(session.MainWindow, session.ConditionFactory);
        var failureContext = new UiFailureContext(
            OperationName: "WaitUntilNameEquals",
            AdapterId: resolver.Capabilities.AdapterId,
            Timeout: TimeSpan.FromSeconds(1),
            StartedAtUtc: DateTimeOffset.UtcNow,
            FinishedAtUtc: DateTimeOffset.UtcNow,
            Capabilities: resolver.Capabilities,
            Artifacts: Array.Empty<UiFailureArtifact>(),
            PageTypeFullName: "DotnetDebug.AppAutomation.Authoring.Pages.MainWindowPage",
            ControlPropertyName: "ResultText",
            LocatorValue: "ResultText",
            LocatorKind: UiLocatorKind.AutomationId,
            LastObservedValue: string.Empty);

        var artifacts = await resolver.CollectAsync(failureContext);
        var kinds = artifacts.Select(static artifact => artifact.Kind).ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(kinds).Contains("logical-tree");
            await Assert.That(kinds).Contains("process-info");
            await Assert.That(kinds).Contains("window-handle");
            await Assert.That(kinds.Any(static kind => kind is "screenshot" or "screenshot-unavailable")).IsEqualTo(true);
        }
    }
}
