using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderButtonClickCaptureTests
{
    [Test]
    public async Task TwoSeparatePointerClicksOnSameButtonRecordTwoSteps()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var stepCounts = headless.Dispatch(() =>
        {
            var (window, button, _, session) = CreateFixture();
            try
            {
                var point = button.TranslatePoint(
                    new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                Click(window, point);
                var first = session.StepJournal.Count;
                Click(window, point);
                return (first, session.StepJournal.Count, session.PersistableStepCount);
            }
            finally
            {
                session.Dispose();
                window.Close();
            }
        }, CancellationToken.None).GetAwaiter().GetResult();

        using (Assert.Multiple())
        {
            await Assert.That(stepCounts.first).IsEqualTo(1);
            await Assert.That(stepCounts.Count).IsEqualTo(2);
            await Assert.That(stepCounts.PersistableStepCount).IsEqualTo(2);
        }
    }

    [Test]
    public async Task RepeatedDeliveryOfSameRoutedClickAfterAnotherButtonRecordsOneStepForThatEvent()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var stepCount = headless.Dispatch(() =>
        {
            var (window, button, otherButton, session) = CreateFixture();
            try
            {
                var click = new RoutedEventArgs(Button.ClickEvent);
                button.RaiseEvent(click);
                otherButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                button.RaiseEvent(click);
                return session.StepJournal.Count;
            }
            finally
            {
                session.Dispose();
                window.Close();
            }
        }, CancellationToken.None).GetAwaiter().GetResult();

        await Assert.That(stepCount).IsEqualTo(2);
    }

    [Test]
    public async Task TwoDistinctClickEventsWithoutPointerGestureRecordTwoSteps()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var stepCount = headless.Dispatch(() =>
        {
            var (window, button, _, session) = CreateFixture();
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return session.StepJournal.Count;
            }
            finally
            {
                session.Dispose();
                window.Close();
            }
        }, CancellationToken.None).GetAwaiter().GetResult();

        await Assert.That(stepCount).IsEqualTo(2);
    }

    [Test]
    public async Task TwoRoutedClicksWithinOnePointerGestureRecordOneStep()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var stepCount = headless.Dispatch(() =>
        {
            var (window, button, _, session) = CreateFixture();
            try
            {
                session.BeginPointerGestureForTesting();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                session.EndPointerGestureForTesting();
                return session.StepJournal.Count;
            }
            finally
            {
                session.Dispose();
                window.Close();
            }
        }, CancellationToken.None).GetAwaiter().GetResult();

        await Assert.That(stepCount).IsEqualTo(1);
    }

    private static (Window Window, Button Button, Button OtherButton, RecorderSession Session) CreateFixture()
    {
        var button = new Button { Content = "Add position", Width = 180, Height = 40 };
        AutomationProperties.SetAutomationId(button, "AddPositionButton");
        var otherButton = new Button { Content = "Another action", Width = 180, Height = 40 };
        AutomationProperties.SetAutomationId(otherButton, "OtherButton");
        var window = new Window
        {
            Width = 320,
            Height = 180,
            Content = new StackPanel { Children = { button, otherButton } }
        };
        window.Show();
        window.UpdateLayout();
        var session = new RecorderSession(window, new AppAutomationRecorderOptions { ShowOverlay = false });
        session.Start();
        return (window, button, otherButton, session);
    }

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class TestApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }
}
