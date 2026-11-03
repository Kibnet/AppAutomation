using Avalonia;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
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
    [Arguments(Key.E, true, "text")]
    [Arguments(Key.E, false, "text")]
    [Arguments(Key.T, true, "text")]
    [Arguments(Key.A, true, "checkbox")]
    [Arguments(Key.F, true, "text")]
    [Arguments(Key.A, true, "number")]
    [Arguments(Key.A, true, "progress")]
    [Arguments(Key.T, true, "date")]
    [NotInParallel]
    public async Task CaptureHotkeysWaitWhileCheckStillAsserts(Key key, bool enabled, string kind)
    {
        var headless = GetBareHeadlessSession();
        var result = headless.Dispatch(() =>
        {
            Control target = kind switch
            {
                "checkbox" => new CheckBox { IsChecked = true },
                "number" => new NumericUpDown { Value = 12 },
                "progress" => new ProgressBar { Value = 50 },
                "date" => new DatePicker { SelectedDate = new DateTime(2026, 10, 29) },
                _ => new TextBox { Text = "Ready" }
            };
            target.IsEnabled = enabled;
            AutomationProperties.SetAutomationId(target, "Target");
            var window = new Window { Width = 300, Height = 120, Content = target };
            window.Show();
            window.UpdateLayout();
            var options = new AppAutomationRecorderOptions { ShowOverlay = false };
            using var session = new RecorderSession(window, options);
            session.Start();
            session.SetLastSpatialCaptureTargetForTesting(target, window, new Point(10, 10));
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = key,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift
            });
            var wait = session.ExportPreview();
            RecordedStep replayStep;
            var factory = new RecorderStepFactory(options, window);
            if (key != Key.F)
            {
                session.Clear();
                RecorderCheckTargetSelection? selection = null;
                session.CheckTargetSelected += (_, e) => selection = e.Selection;
                session.BeginCheckTargetSelection();
                session.SelectCheckTargetForTesting(target);
                var details = (IRecorderCheckpointSessionDetails)session;
                if (key == Key.E) details.CaptureEnabledAssertion(selection!, enabled);
                else details.CaptureCurrentValueAssertion(selection!);
                replayStep = (key == Key.E
                    ? factory.TryCreateEnabledAssertionStep(target, selection!.ValueSnapshot, enabled)
                    : factory.TryCreateAssertionStep(selection!.ValueSnapshot, RecorderAssertionMode.Auto)).Step!
                    with
                { WaitForValue = true };
            }
            else replayStep = factory.TryCreateAssertionStep(target, RecorderAssertionMode.Exists).Step!;
            var check = session.ExportPreview();
            window.Close();
            return (wait, check, replayStep);
        }, CancellationToken.None).GetAwaiter().GetResult();
        var method = kind is "date" or "progress" ? "UiWait.Until" : "Page." + (key switch
        {
            Key.E => "WaitUntilIsEnabled",
            Key.T => "WaitUntilTextEquals",
            Key.A when kind == "number" => "WaitUntilValueEquals",
            Key.A => "WaitUntilIsChecked",
            _ => "WaitUntilExists"
        });
        await Assert.That(result.wait).Contains(method + "(");
        if (kind == "date") await Assert.That(result.wait).Contains("() => Page.Target.SelectedDate");
        if (kind == "progress")
        {
            await Assert.That(result.wait).Contains("UiWait.Until(() => Page.Target.Value, value => value == 50);");
            await Assert.That(result.wait).DoesNotContain("WaitUntilProgressAtLeast");
            await Assert.That(result.check).Contains("Assert.That(Page.Target.Value).IsEqualTo(50)");
        }
        await Assert.That(result.wait).DoesNotContain("Assert.That");
        if (key == Key.E) await Assert.That(result.wait).Contains(enabled ? ", true);" : ", false);");
        if (key != Key.F) await Assert.That(result.check).Contains("await Assert.That(");
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage, RecorderScenarioDestinationSources.CompilableScenario);
        var saved = await project.AutosaveAsync(project.CreateSaveContext("Hotkey wait", "hotkey-draft"), [result.replayStep]);
        await Assert.That(RecorderGeneratedSourceCompiler.Compile(project.RootPath)).IsEmpty();
        await Assert.That(RecorderAutosaveStateSerializer.TryRead(saved.ScenarioFilePath!, out var state, out var error)).IsTrue().Because(error);
        await Assert.That(project.Generator.GeneratePreview(state!.Steps)).IsEqualTo(result.wait);
    }

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
        var headless = GetBareHeadlessSession();
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
        var headless = GetBareHeadlessSession();
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
        var headless = GetBareHeadlessSession();
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
        var headless = GetBareHeadlessSession();
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

    // Reuse the registry-owned session without this assembly's deferred Fluent templates.
    private static HeadlessUnitTestSession GetBareHeadlessSession() =>
        HeadlessUnitTestSession.GetOrStartForAssembly(assembly: null);

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
}
