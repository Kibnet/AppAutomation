using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.Authoring.Tests.UIAutomationTests;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using DotnetDebug.AppAutomation.TestHost;
using AppAutomation.TUnit;
using AppAutomation.FlaUI.Session;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

[InheritsTests]
public sealed class MainWindowFlaUiRuntimeTests : MainWindowScenariosBase<MainWindowFlaUiRuntimeTests.FlaUiRuntimeSession>
{
    protected override FlaUiRuntimeSession LaunchSession()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        return new FlaUiRuntimeSession(DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions()));
    }

    protected override MainWindowPage CreatePage(FlaUiRuntimeSession session)
    {
        return MainWindowFlaUiPageFactory.Create(session.Inner);
    }

    public sealed class FlaUiRuntimeSession : IUiTestSession
    {
        public FlaUiRuntimeSession(DesktopAppSession inner)
        {
            Inner = inner;
        }

        public DesktopAppSession Inner { get; }

        public void Dispose()
        {
            Inner.Dispose();
        }
    }
}
