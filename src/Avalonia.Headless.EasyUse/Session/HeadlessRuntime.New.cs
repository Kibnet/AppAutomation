namespace Avalonia.Headless.EasyUse.Session;

public static class HeadlessRuntime
{
    public static void SetSession(global::Avalonia.Headless.HeadlessUnitTestSession? session)
    {
        global::FlaUI.EasyUse.Session.HeadlessRuntime.SetSession(session);
    }
}