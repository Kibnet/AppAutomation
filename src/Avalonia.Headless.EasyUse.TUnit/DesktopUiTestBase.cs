using FlaUI.EasyUse.Session;
using TUnit.Core;

namespace FlaUI.EasyUse.TUnit;

public abstract class DesktopUiTestBase<TPage> where TPage : class
{
    protected const string DesktopUiConstraint = "DesktopUi";

    private DesktopAppSession? _session;
    private TPage? _page;

    protected DesktopAppSession Session =>
        _session ?? throw new InvalidOperationException("Desktop app session is not initialized.");

    protected TPage Page =>
        _page ?? throw new InvalidOperationException("Page is not initialized.");

    protected abstract DesktopProjectLaunchOptions CreateLaunchOptions();

    protected abstract TPage CreatePage(DesktopAppSession session);

    [Before(Test)]
    public void SetupDesktopSession()
    {
        _session = DesktopAppSession.LaunchFromProject(CreateLaunchOptions());
        _page = CreatePage(_session);
    }

    [After(Test)]
    public void CleanupDesktopSession()
    {
        _session?.Dispose();
        _session = null;
        _page = null;
    }
}