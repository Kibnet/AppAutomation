using EasyUse.Session.Contracts;
using EasyUse.TUnit.Core;
using DotnetDebug.UiTests.FlaUI.EasyUse.Pages;
using FlaUI.EasyUse.Session;
using TUnit.Core;

namespace DotnetDebug.UiTests.FlaUI.EasyUse.Tests.UIAutomationTests;

[InheritsTests]
public sealed class MainWindowFlaUiRuntimeTests : MainWindowScenariosBase<MainWindowFlaUiRuntimeTests.FlaUiRuntimeSession>
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

    protected override FlaUiRuntimeSession LaunchSession(DesktopProjectLaunchOptions options)
    {
        return new FlaUiRuntimeSession(DesktopAppSession.LaunchFromProject(options));
    }

    protected override MainWindowPage CreatePage(FlaUiRuntimeSession session)
    {
        return new MainWindowPage(session.Inner.MainWindow, session.Inner.ConditionFactory);
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
