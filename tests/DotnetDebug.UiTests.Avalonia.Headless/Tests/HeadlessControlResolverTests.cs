using Avalonia.Headless.EasyUse.Automation;
using Avalonia.Headless.EasyUse.Session;
using EasyUse.Automation.Abstractions;
using EasyUse.TestHost;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.UiTests.Avalonia.Headless.Tests.UIAutomationTests;

public sealed class HeadlessControlResolverTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task Resolve_DoesNotFallbackToName_ForAutomationIdLocator_WhenDisabled()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var resolver = new HeadlessControlResolver(session.MainWindow);
        var definition = new UiControlDefinition(
            "MathTabByName",
            UiControlType.TabItem,
            "Math",
            UiLocatorKind.AutomationId,
            FallbackToName: false);

        Exception? exception = null;
        try
        {
            resolver.Resolve<IUiControl>(definition);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        await Assert.That(exception is InvalidOperationException).IsEqualTo(true);
    }
}
