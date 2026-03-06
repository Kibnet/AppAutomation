using Avalonia.Headless;
using DotnetDebug.Avalonia;
using Avalonia.Headless.EasyUse.Session;
using TUnit.Core;

namespace DotnetDebug.UiTests.Avalonia.Headless.Infrastructure;

public sealed class HeadlessSessionHooks
{
    private static HeadlessUnitTestSession? _session;

    [Before(TestSession)]
    public static void SetupSession()
    {
        _session = HeadlessUnitTestSession.StartNew(typeof(App));
        HeadlessRuntime.SetSession(_session);
    }

    [After(TestSession)]
    public static void CleanupSession()
    {
        HeadlessRuntime.SetSession(null);
        _session?.Dispose();
        _session = null;
    }

    public static HeadlessUnitTestSession Session =>
        _session ?? throw new InvalidOperationException("Headless test session is not initialized.");
}
