using AppAutomation.Avalonia.Headless.Session;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.Authoring.Tests.UIAutomationTests;
using DotnetDebug.AppAutomation.TestHost;
using AppAutomation.TUnit;
using AppAutomation.Avalonia.Headless.Automation;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

[InheritsTests]
public sealed class MainWindowHeadlessRuntimeTests : MainWindowScenariosBase<MainWindowHeadlessRuntimeTests.HeadlessRuntimeSession>
{
    protected override HeadlessRuntimeSession LaunchSession()
    {
        return new HeadlessRuntimeSession(DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions()));
    }

    protected override MainWindowPage CreatePage(HeadlessRuntimeSession session)
    {
        return new MainWindowPage(new HeadlessControlResolver(session.Inner.MainWindow));
    }

    public sealed class HeadlessRuntimeSession : IUiTestSession
    {
        public HeadlessRuntimeSession(DesktopAppSession inner)
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
