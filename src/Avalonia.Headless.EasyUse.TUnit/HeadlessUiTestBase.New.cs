using TUnit.Core;

namespace Avalonia.Headless.EasyUse.TUnit;

public abstract class HeadlessUiTestBase<TPage> where TPage : class
{
    protected const string HeadlessUiConstraint = "AvaloniaHeadlessUi";

    private global::Avalonia.Headless.EasyUse.Session.HeadlessAppSession? _session;
    private TPage? _page;

    protected global::Avalonia.Headless.EasyUse.Session.HeadlessAppSession Session =>
        _session ?? throw new InvalidOperationException("Headless app session is not initialized.");

    protected TPage Page =>
        _page ?? throw new InvalidOperationException("Page is not initialized.");

    protected abstract global::FlaUI.EasyUse.Session.DesktopProjectLaunchOptions CreateLaunchOptions();

    protected abstract TPage CreatePage(global::Avalonia.Headless.EasyUse.Session.HeadlessAppSession session);

    [Before(Test)]
    public void SetupHeadlessSession()
    {
        _session = global::Avalonia.Headless.EasyUse.Session.HeadlessAppSession.LaunchFromProject(CreateLaunchOptions());
        _page = CreatePage(_session);
    }

    [After(Test)]
    public void CleanupHeadlessSession()
    {
        _session?.Dispose();
        _session = null;
        _page = null;
    }
}
