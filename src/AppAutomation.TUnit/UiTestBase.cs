using AppAutomation.Abstractions;
using TUnit.Core;

namespace AppAutomation.TUnit;

public abstract class UiTestBase<TSession, TPage>
    where TSession : class, IUiTestSession
    where TPage : class
{
    protected const string DesktopUiConstraint = "DesktopUi";
    private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    private TSession? _session;
    private TPage? _page;

    protected TSession Session =>
        _session ?? throw new InvalidOperationException("UI test session is not initialized.");

    protected TPage Page =>
        _page ?? throw new InvalidOperationException("Page is not initialized.");

    protected abstract TSession LaunchSession();

    protected abstract TPage CreatePage(TSession session);

    protected static void WaitUntil(
        Func<bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        _ = WaitUntil(
            () => condition(),
            static success => success,
            timeout,
            pollInterval,
            timeoutMessage,
            cancellationToken);
    }

    protected static T WaitUntil<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        return UiWait.Until(
            valueFactory,
            condition,
            CreateWaitOptions(timeout, pollInterval),
            timeoutMessage,
            cancellationToken);
    }

    protected static async Task<T> WaitUntilAsync<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        return await UiWait.UntilAsync(
            valueFactory,
            condition,
            CreateWaitOptions(timeout, pollInterval),
            timeoutMessage,
            cancellationToken);
    }

    protected static void RetryUntil(
        Func<bool> attempt,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        _ = WaitUntil(
            attempt,
            static success => success,
            timeout,
            pollInterval,
            timeoutMessage,
            cancellationToken);
    }

    [Before(Test)]
    public void SetupUiSession()
    {
        _session = LaunchSession();
        _page = CreatePage(_session);
    }

    [After(Test)]
    public void CleanupUiSession()
    {
        _session?.Dispose();
        _session = null;
        _page = null;
    }

    private static UiWaitOptions CreateWaitOptions(TimeSpan? timeout, TimeSpan? pollInterval)
    {
        return new UiWaitOptions
        {
            Timeout = timeout ?? DefaultWaitTimeout,
            PollInterval = pollInterval ?? DefaultPollInterval
        };
    }
}
