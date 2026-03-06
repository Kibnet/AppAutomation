using Avalonia.Headless.EasyUse.Session;
using DotnetDebug.UiTests.FlaUI.EasyUse.Pages;
using EasyUse.Session.Contracts;
using EasyUse.TUnit.Core;
using TUnit.Core;

namespace DotnetDebug.UiTests.FlaUI.EasyUse.Tests.UIAutomationTests;

[InheritsTests]
public sealed class MainWindowHeadlessRuntimeTests : MainWindowScenariosBase<MainWindowHeadlessRuntimeTests.HeadlessRuntimeSession>
{
    protected override DesktopProjectLaunchOptions CreateLaunchOptions()
    {
        return new DesktopProjectLaunchOptions
        {
            SolutionFileName = "DotnetDebug.sln",
            ProjectRelativePath = Path.Combine("src", "DotnetDebug.Avalonia", "DotnetDebug.Avalonia.csproj"),
            BuildConfiguration = "Debug",
            TargetFramework = "net9.0"
        };
    }

    protected override HeadlessRuntimeSession LaunchSession(DesktopProjectLaunchOptions options)
    {
        return new HeadlessRuntimeSession(DesktopAppSession.LaunchFromProject(options));
    }

    protected override MainWindowPage CreatePage(HeadlessRuntimeSession session)
    {
        return new MainWindowPage(session.Inner.MainWindow, session.Inner.ConditionFactory);
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
