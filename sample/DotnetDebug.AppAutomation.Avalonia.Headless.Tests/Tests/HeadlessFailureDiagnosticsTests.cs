using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using DotnetDebug.AppAutomation.Authoring.Pages;
using AppAutomation.Abstractions;
using DotnetDebug.AppAutomation.TestHost;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

public sealed class HeadlessFailureDiagnosticsTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task WaitUntilNameEquals_OnTimeout_CollectsHeadlessArtifacts()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));

        UiOperationException? exception = null;
        try
        {
            page.WaitUntilNameEquals(static candidate => candidate.ResultText, "Never matches", timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.AdapterId).IsEqualTo("avalonia-headless");
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("ResultText");
            await Assert.That(exception.FailureContext.LocatorValue).IsEqualTo("ResultText");
            await Assert.That(exception.FailureContext.LocatorKind).IsEqualTo(UiLocatorKind.AutomationId);
            await Assert.That(exception.FailureContext.Artifacts.Select(static artifact => artifact.Kind).ToArray()).Contains("logical-tree");
            await Assert.That(exception.FailureContext.Artifacts.Select(static artifact => artifact.Kind).ToArray()).Contains("control-state");
            await Assert.That(exception.InnerException is TimeoutException).IsEqualTo(true);
        }
    }
}
