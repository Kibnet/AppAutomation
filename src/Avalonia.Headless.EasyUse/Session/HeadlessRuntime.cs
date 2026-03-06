using Avalonia.Headless;

namespace Avalonia.Headless.EasyUse.Session;

public static class HeadlessRuntime
{
    private static readonly object Sync = new();
    private static HeadlessUnitTestSession? _session;

    public static void SetSession(HeadlessUnitTestSession? session)
    {
        lock (Sync)
        {
            _session = session;
        }
    }

    public static HeadlessUnitTestSession Session
    {
        get
        {
            lock (Sync)
            {
                return _session ?? throw new InvalidOperationException(
                    "Headless session is not initialized. Call HeadlessRuntime.SetSession from test hooks.");
            }
        }
    }

    public static T Dispatch<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        return Session.Dispatch(action, cancellationToken).GetAwaiter().GetResult();
    }

    public static void Dispatch(Action action, CancellationToken cancellationToken = default)
    {
        Session.Dispatch(
            () =>
            {
                action();
                return true;
            },
            cancellationToken).GetAwaiter().GetResult();
    }
}
