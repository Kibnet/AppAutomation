using Avalonia.Headless;
using AppAutomation.Avalonia.Headless.Session;
using SampleApp.AppAutomation.TestHost;
using TUnit.Core;

namespace SampleApp.UiTests.Headless.Infrastructure;

public static class HeadlessSessionHooks
{
    private static HeadlessUnitTestSession? _session;

    [Before(TestSession)]
    public static void SetupSession()
    {
        _session = HeadlessUnitTestSession.StartNew(SampleAppAppLaunchHost.AvaloniaAppType);
        HeadlessRuntime.SetSession(_session);
    }

    [After(TestSession)]
    public static void CleanupSession()
    {
        HeadlessRuntime.SetSession(null);
        _session?.Dispose();
        _session = null;
    }
}
