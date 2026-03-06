using EasyUse.Session.Contracts;
using TUnit.Core;

namespace EasyUse.TUnit.Core;

public abstract class UiTestBase<TSession, TPage>
    where TSession : class, IUiTestSession
    where TPage : class
{
    protected const string DesktopUiConstraint = "DesktopUi";

    private TSession? _session;
    private TPage? _page;

    protected TSession Session =>
        _session ?? throw new InvalidOperationException("UI test session is not initialized.");

    protected TPage Page =>
        _page ?? throw new InvalidOperationException("Page is not initialized.");

    protected abstract DesktopProjectLaunchOptions CreateLaunchOptions();

    protected abstract TSession LaunchSession(DesktopProjectLaunchOptions options);

    protected abstract TPage CreatePage(TSession session);

    [Before(Test)]
    public void SetupUiSession()
    {
        _session = LaunchSession(CreateLaunchOptions());
        _page = CreatePage(_session);
    }

    [After(Test)]
    public void CleanupUiSession()
    {
        _session?.Dispose();
        _session = null;
        _page = null;
    }
}
