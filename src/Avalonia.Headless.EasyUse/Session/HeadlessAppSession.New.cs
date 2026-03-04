namespace Avalonia.Headless.EasyUse.Session;

public sealed class HeadlessAppSession : IDisposable
{
    private readonly global::FlaUI.EasyUse.Session.DesktopAppSession _inner;

    private HeadlessAppSession(global::FlaUI.EasyUse.Session.DesktopAppSession inner)
    {
        _inner = inner;
        MainWindow = new global::Avalonia.Headless.EasyUse.AutomationElements.Window(inner.MainWindow);
        ConditionFactory = new global::Avalonia.Headless.EasyUse.Conditions.ConditionFactory();
    }

    public global::Avalonia.Headless.EasyUse.AutomationElements.Window MainWindow { get; }

    public global::Avalonia.Headless.EasyUse.Conditions.ConditionFactory ConditionFactory { get; }

    public static HeadlessAppSession LaunchFromProject(global::FlaUI.EasyUse.Session.DesktopProjectLaunchOptions options)
    {
        return new HeadlessAppSession(global::FlaUI.EasyUse.Session.DesktopAppSession.LaunchFromProject(options));
    }

    public void Dispose() => _inner.Dispose();
}