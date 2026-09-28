using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Headless;

namespace AppAutomation.TestHost.Avalonia.Tests;

internal static class HeadlessTestRuntime
{
    public const string HeadlessRuntimeConstraint = "HeadlessRuntime";

    public static IDisposable StartHeadlessRuntime()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(TestAvaloniaApp));
        HeadlessRuntime.SetSession(session);
        return new HeadlessSessionScope(session);
    }

    private sealed class TestAvaloniaApp : global::Avalonia.Application
    {
        public override void Initialize()
        {
            Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
            Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(
                new Uri("avares://AppAutomation.TestHost.Avalonia.Tests"))
            {
                Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
            });
        }
    }

    private sealed class HeadlessSessionScope : IDisposable
    {
        private readonly HeadlessUnitTestSession _session;

        public HeadlessSessionScope(HeadlessUnitTestSession session)
        {
            _session = session;
        }

        public void Dispose()
        {
            HeadlessRuntime.SetSession(null);
            _session.Dispose();
        }
    }
}
