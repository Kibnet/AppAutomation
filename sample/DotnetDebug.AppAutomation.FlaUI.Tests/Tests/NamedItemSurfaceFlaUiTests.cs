using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using DotnetDebug.AppAutomation.TestHost;
using DotnetDebug.Avalonia;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

[NotInParallel("DesktopUi")]
public sealed class NamedItemSurfaceFlaUiTests
{
    [Test]
    public async Task ReplaysTheNamedItemAfterReorderingAndReveal()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = Launch();
        await CreatePage(session).VerifyReplayAfterReorder();
    }

    [Test]
    public async Task RefusesIncompleteAndAmbiguousItemSetsBeforeInput()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = Launch();
        await CreatePage(session).VerifyInvalidTargetsDoNotOpenAnItem();
    }

    private static NamedItemSurfacePage CreatePage(DesktopAppSession session) =>
        new(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));

    private static DesktopAppSession Launch()
    {
        var defaults = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(buildBeforeLaunch: false);
        return DesktopAppSession.Launch(new DesktopAppLaunchOptions
        {
            ExecutablePath = defaults.ExecutablePath,
            WorkingDirectory = defaults.WorkingDirectory,
            Arguments = defaults.Arguments,
            DisposeCallback = defaults.DisposeCallback,
            MainWindowTimeout = defaults.MainWindowTimeout,
            PollInterval = defaults.PollInterval,
            WindowPlacement = defaults.WindowPlacement,
            EnvironmentVariables = new Dictionary<string, string?>(defaults.EnvironmentVariables, StringComparer.Ordinal)
            {
                [NamedItemSurfaceFixture.EnvironmentVariable] = "1"
            }
        });
    }
}
