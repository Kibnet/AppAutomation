using System.Reflection;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Headless;

namespace AppAutomation.TestHost.Avalonia.Tests;

internal static class HeadlessTestRuntime
{
    public const string HeadlessRuntimeConstraint = "HeadlessRuntime";

    public static IDisposable StartHeadlessRuntime(
        AvaloniaTestIsolationLevel isolationLevel = AvaloniaTestIsolationLevel.PerTest)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(TestAvaloniaApp), isolationLevel);
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
            try
            {
                _session.Dispose();
            }
            catch (NullReferenceException) when (HasMissingDispatchTask(_session))
            {
                // Avalonia 12.0.4 can publish a session before its Task.Run result is
                // assigned. Dispose still cancels its queue, then dereferences that task.
            }
            finally
            {
                HeadlessRuntime.SetSession(null);
            }
        }

        private static bool HasMissingDispatchTask(HeadlessUnitTestSession session)
        {
            var field = typeof(HeadlessUnitTestSession)
                .GetField("_dispatchTask", BindingFlags.Instance | BindingFlags.NonPublic);
            return field is not null && field.GetValue(session) is null;
        }
    }
}
