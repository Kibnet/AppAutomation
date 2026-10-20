using Avalonia;
using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderHotkeyGestureTests
{
    [Test]
    [NotInParallel]
    public async Task RepeatedDeliveryOfSameHotkeyEvent_HandlesCommandOnce()
    {
        var commandCount = RunGesture(window =>
        {
            var keyDown = CreateKeyEvent(InputElement.KeyDownEvent);
            window.RaiseEvent(keyDown);
            window.RaiseEvent(keyDown);
        });

        await Assert.That(commandCount).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task RepeatedKeyDownWithoutKeyUp_HandlesCommandOnce()
    {
        var commandCount = RunGesture(window =>
        {
            window.RaiseEvent(CreateKeyEvent(InputElement.KeyDownEvent));
            window.RaiseEvent(CreateKeyEvent(InputElement.KeyDownEvent));
        });

        await Assert.That(commandCount).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task KeyUpBetweenQuickPresses_HandlesCommandTwice()
    {
        var commandCount = RunGesture(window =>
        {
            window.RaiseEvent(CreateKeyEvent(InputElement.KeyDownEvent));
            window.RaiseEvent(CreateKeyEvent(InputElement.KeyUpEvent));
            window.RaiseEvent(CreateKeyEvent(InputElement.KeyDownEvent));
        });

        await Assert.That(commandCount).IsEqualTo(2);
    }

    [Test]
    [NotInParallel]
    public async Task HoldingStartStopHotkey_DoesNotStopRecordingAgain()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var state = headless.Dispatch(() =>
        {
            var window = new Window { Content = new TextBlock { Text = "Content" } };
            window.Show();
            using var session = new RecorderSession(
                window,
                new AppAutomationRecorderOptions { ShowOverlay = false });
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.R,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift
            });
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.R,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift
            });
            var result = session.State;
            window.Close();
            return result;
        }, CancellationToken.None).GetAwaiter().GetResult();

        await Assert.That(state).IsEqualTo(RecorderSessionState.Recording);
    }

    [Test]
    [NotInParallel]
    public async Task TwoTextAssertionHotkeyGestures_RecordTwoNotificationChecks()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var result = headless.Dispatch(() =>
        {
            var text = new SelectableTextBlock { Text = "Позиция создана" };
            var notification = new Border { Child = text };
            AutomationProperties.SetAutomationId(notification, "ToastNotification");
            AutomationProperties.SetAutomationId(text, "ToastNotificationText");
            var window = new Window { Content = notification };
            window.Show();
            window.UpdateLayout();
            var options = new AppAutomationRecorderOptions { ShowOverlay = false };
            options.NotificationHints.Add(new RecorderNotificationHint(
                "ToastNotification",
                NotificationControlParts.ByAutomationIds("ToastNotificationText")));
            using var session = new RecorderSession(window, options);
            session.Start();
            var focused = text.Focus();
            window.RaiseEvent(CreateTextAssertionKeyEvent(InputElement.KeyDownEvent));
            window.RaiseEvent(CreateTextAssertionKeyEvent(InputElement.KeyDownEvent));
            window.RaiseEvent(CreateTextAssertionKeyEvent(InputElement.KeyUpEvent));
            window.RaiseEvent(CreateTextAssertionKeyEvent(InputElement.KeyDownEvent));
            var captured = (focused, session.StepJournal.ToArray());
            window.Close();
            return captured;
        }, CancellationToken.None).GetAwaiter().GetResult();

        using (Assert.Multiple())
        {
            await Assert.That(result.focused).IsTrue();
            await Assert.That(result.Item2.Length).IsEqualTo(2);
            await Assert.That(result.Item2[0].Preview).Contains("WaitUntilNotificationContains");
            await Assert.That(result.Item2[1].Preview).Contains("WaitUntilNotificationContains");
        }
    }

    [Test]
    [NotInParallel]
    public async Task FocusedNotificationText_TakesPrecedenceOverStaleSpatialTarget()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var result = headless.Dispatch(() =>
        {
            var staleSurface = new Border { Height = 100 };
            var text = new SelectableTextBlock { Text = "Позиция создана" };
            var notification = new Border { Child = text };
            AutomationProperties.SetAutomationId(notification, "ToastNotification");
            AutomationProperties.SetAutomationId(text, "ToastNotificationText");
            var window = new Window
            {
                Width = 300,
                Height = 200,
                Content = new StackPanel { Children = { staleSurface, notification } }
            };
            window.Show();
            window.UpdateLayout();
            var options = new AppAutomationRecorderOptions { ShowOverlay = false };
            options.NotificationHints.Add(new RecorderNotificationHint(
                "ToastNotification",
                NotificationControlParts.ByAutomationIds("ToastNotificationText")));
            using var session = new RecorderSession(window, options);
            session.Start();
            text.Focus();
            session.SetLastSpatialCaptureTargetForTesting(staleSurface, window, new Point(10, 10));
            window.RaiseEvent(CreateTextAssertionKeyEvent(InputElement.KeyDownEvent));
            var steps = session.StepJournal.ToArray();
            var status = session.LatestStatus;
            window.Close();
            return (steps, status);
        }, CancellationToken.None).GetAwaiter().GetResult();

        await Assert.That(result.steps).Count().IsEqualTo(1).Because(result.status);
        await Assert.That(result.steps[0].Preview).Contains("WaitUntilNotificationContains");
        await Assert.That(result.steps[0].Preview).Contains("Позиция создана");
    }

    private static int RunGesture(Action<Window> gesture)
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        return headless.Dispatch(() =>
        {
            var window = new Window { Content = new TextBlock { Text = "Content" } };
            window.Show();
            using var session = new RecorderSession(
                window,
                new AppAutomationRecorderOptions { ShowOverlay = false });
            var commandCount = 0;
            session.ExportRequested += (_, _) => commandCount++;
            gesture(window);
            window.Close();
            return commandCount;
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static KeyEventArgs CreateKeyEvent(RoutedEvent routedEvent) => new()
    {
        RoutedEvent = routedEvent,
        Key = Key.X,
        KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift
    };

    private static KeyEventArgs CreateTextAssertionKeyEvent(RoutedEvent routedEvent) => new()
    {
        RoutedEvent = routedEvent,
        Key = Key.T,
        KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift
    };

    private sealed class TestApplication : Application
    {
        // Hotkey behavior does not require FluentTheme's deferred control templates.
    }
}
