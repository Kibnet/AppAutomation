using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DotnetDebug.AppAutomation.TestHost;
using DotnetDebug.Avalonia;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

[NotInParallel("DesktopUi")]
public sealed class NotificationOcclusionHeadlessTests
{
    [Test]
    [Arguments("")]
    [Arguments("reorder")]
    [Arguments("recreate")]
    [Arguments("successive")]
    [Arguments("late")]
    public async Task RegisteredBlockersAreDismissedWithoutTouchingOtherToastOrHistory(string mode)
    {
        using var fixture = Launch();
        var page = fixture.Page;
        var row = GridRowKeyReader.CaptureFirst(page.Table);
        page.EnterText(static p => p.Mode, mode);
        page.ClickButton(static p => p.ShowOther);
        if (mode == "late") page.ClickButton(static p => p.ArmLate);
        else page.ClickButton(static p => p.ShowBackground);

        // Deliberately do not resolve/read the notification property before the first grid action.
        page.OpenGridRow(static p => p.Table, row);

        await Assert.That(page.Marker("Dismiss").Text).IsEqualTo(mode == "successive" ? "3" : "1");
        await Assert.That(page.Marker("Down").Text).IsEqualTo("2");
        await Assert.That(page.Marker("Up").Text).IsEqualTo("2");
        await Assert.That(page.Marker("Open").Text).IsEqualTo("1");
        await Assert.That(page.Marker("Visible").Text).IsEqualTo("1");
        await Assert.That(page.Marker("History").Text).Contains("other|");
        await Assert.That(page.Marker("History").Text).Contains("background");
        await Assert.That(page.Marker("LastDownKey").Text).IsEqualTo(row.Conditions.Single().Value);
        await Assert.That(page.Opened.Text).IsEqualTo(row.Conditions.Single().Value);
    }

    [Test]
    public async Task DeferredCloseKeepsTheUiResponsiveWhileTheOriginalWorkerWaits()
    {
        using var fixture = Launch();
        var page = fixture.Page;
        var row = GridRowKeyReader.CaptureFirst(page.Table);
        page.EnterText(static p => p.Mode, "async");
        page.ClickButton(static p => p.ShowBackground);
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ui = HeadlessRuntime.Dispatch(() =>
        {
            T Find<T>(string id) where T : Control => fixture.Session.MainWindow.GetVisualDescendants().OfType<T>()
                .Single(control => AutomationProperties.GetAutomationId(control) == id);
            var marker = Find<TextBlock>("RecoveryPending");
            EventHandler<AvaloniaPropertyChangedEventArgs> changed = (_, change) =>
            {
                if (change.Property == TextBlock.TextProperty && marker.Text == "1") pending.TrySetResult(true);
            };
            marker.PropertyChanged += changed;
            if (marker.Text == "1") pending.TrySetResult(true);
            return (Pending: marker, Changed: changed, Heartbeat: Find<Button>("RecoveryPulseHeartbeat"),
                Complete: Find<Button>("RecoveryCompleteAsyncClose"), HeartbeatMarker: Find<TextBlock>("RecoveryHeartbeat"),
                Down: Find<TextBlock>("RecoveryDown"));
        });
        var opening = Task.Run(() => page.OpenGridRow(static p => p.Table, row));
        Exception? primaryFailure = null;
        try
        {
            await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.That(opening.IsCompleted).IsFalse();
            var heartbeat = await PulseUiAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.That(heartbeat.Text).IsEqualTo("1");
            await Assert.That(heartbeat.Down).IsEqualTo("0");
            await CompleteUiAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await opening.WaitAsync(TimeSpan.FromSeconds(6));
            await Assert.That(page.Marker("DownBeforeAsyncClose").Text).IsEqualTo("0");
            await Assert.That(page.Marker("Down").Text).IsEqualTo("2");
            await Assert.That(page.Marker("Dismiss").Text).IsEqualTo("1");
            await Assert.That(page.Opened.Text).IsEqualTo(row.Conditions.Single().Value);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                if (!opening.IsCompleted)
                    ui.Complete.Dispatcher.Post(() => ui.Complete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                await opening.WaitAsync(TimeSpan.FromSeconds(6));
            }
            catch when (primaryFailure is not null)
            {
                // Preserve the original assertion/operation failure while observing the worker task.
            }
            finally
            {
                if (opening.IsCompleted)
                {
                    try { HeadlessRuntime.Dispatch(() => ui.Pending.PropertyChanged -= ui.Changed); }
                    catch when (primaryFailure is not null) { }
                }
            }
        }

        async Task<(string? Text, string? Down)> PulseUiAsync() => await ui.Heartbeat.Dispatcher.InvokeAsync(() =>
        {
            ui.Heartbeat.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return (ui.HeartbeatMarker.Text, ui.Down.Text);
        });
        async Task CompleteUiAsync() => await ui.Complete.Dispatcher.InvokeAsync(() =>
            ui.Complete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
    }

    [Test]
    [Arguments("unknown", "0")]
    [Arguments("missing", "0")]
    [Arguments("disabled", "0")]
    [Arguments("hidden", "0")]
    [Arguments("ambiguous", "0")]
    [Arguments("blocked-close", "0")]
    [Arguments("no-progress", "1")]
    [Arguments("remove-target", "1")]
    [Arguments("duplicate-target", "1")]
    public async Task UnsupportedOrUnresolvedBlockersFailBeforeAnyOriginalPress(string mode, string dismissCount)
    {
        using var fixture = Launch();
        var page = fixture.Page;
        var row = GridRowKeyReader.CaptureFirst(page.Table);
        page.EnterText(static p => p.Mode, mode);
        page.ClickButton(static p => p.ShowBackground);

        await Assert.That(() => page.OpenGridRow(static p => p.Table, row, timeoutMs: 300)).Throws<UiOperationException>();

        await Assert.That(page.Marker("Dismiss").Text).IsEqualTo(dismissCount);
        await Assert.That(page.Marker("Down").Text).IsEqualTo("0");
        await Assert.That(page.Marker("Open").Text).IsEqualTo("0");
        await Assert.That(page.Opened.Text).IsEqualTo("None");
        await Assert.That(page.Marker("History").Text).IsEqualTo("background");
    }

    [Test]
    [Arguments("")]
    [Arguments("throw-after-down")]
    public async Task AToastAppearingAfterOriginalDownIsNeverDismissedOrReplayed(string mode)
    {
        using var fixture = Launch();
        var page = fixture.Page;
        var row = GridRowKeyReader.CaptureFirst(page.Table);
        page.EnterText(static p => p.Mode, mode);
        page.ClickButton(static p => p.ArmAfterDown);

        await Assert.That(() => page.OpenGridRow(static p => p.Table, row)).Throws<UiOperationException>();

        await Assert.That(page.Marker("Down").Text).IsEqualTo("1");
        await Assert.That(page.Marker("Open").Text).IsEqualTo("0");
        await Assert.That(page.Marker("Dismiss").Text).IsEqualTo("0");
        await Assert.That(page.Marker("Visible").Text).IsEqualTo("1");
    }

    [Test]
    public async Task BackgroundToastAfterExplicitSuccessDismissDoesNotReplaceTheCapturedRowGesture()
    {
        using var fixture = Launch();
        var page = fixture.Page;
        page.ClickButton(static p => p.ShowSuccess);
        await Assert.That(page.Toast.Text).IsEqualTo("success");
        var row = GridRowKeyReader.CaptureFirst(page.Table);
        page.Toast.Dismiss();
        await Assert.That(page.Marker("Dismiss").Text).IsEqualTo("1");
        page.ClickButton(static p => p.ShowBackground);

        page.OpenGridRow(static p => p.Table, row);

        await Assert.That(page.Marker("Dismiss").Text).IsEqualTo("2");
        await Assert.That(page.Marker("Down").Text).IsEqualTo("2");
        await Assert.That(page.Marker("Up").Text).IsEqualTo("2");
        await Assert.That(page.Marker("Open").Text).IsEqualTo("1");
        await Assert.That(page.Marker("Visible").Text).IsEqualTo("0");
        await Assert.That(page.Marker("History").Text).IsEqualTo("success|background");
        await Assert.That(page.Opened.Text).IsEqualTo(row.Conditions.Single().Value);
    }

    private static Fixture Launch()
    {
        var variables = new[] { "APPAUTOMATION_NATIVE_GRID_RUNTIME_FIXTURE", GridPresentationFixture.NotificationRecoveryEnvironmentVariable };
        var previous = variables.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            Environment.SetEnvironmentVariable(variables[0], "presentation");
            Environment.SetEnvironmentVariable(variables[1], "1");
            var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
            try
            {
                HeadlessRuntime.Dispatch(() =>
                {
                    session.MainWindow.Show();
                    session.MainWindow.UpdateLayout();
                });
                return new Fixture(session, new Page(new HeadlessControlResolver(session.MainWindow)
                    .WithGridAutomation(GridPresentationFixture.CreateCatalog())
                    .WithNotification(new UiControlDefinition("RecoveryToast", UiControlType.AutomationElement, "RecoveryToast"),
                        NotificationControlParts.ByAutomationIds("RecoveryToastText", "RecoveryToastClose"))));
            }
            catch { session.Dispose(); throw; }
        }
        finally
        {
            for (var index = 0; index < variables.Length; index++)
                Environment.SetEnvironmentVariable(variables[index], previous[index]);
        }
    }

    private sealed class Fixture(DesktopAppSession session, Page page) : IDisposable
    {
        public DesktopAppSession Session { get; } = session;
        public Page Page { get; } = page;
        public void Dispose() => session.Dispose();
    }

    private sealed class Page(IUiControlResolver resolver) : UiPage(resolver)
    {
        public IGridControl Table => Resolve<IGridControl>(new("PresentationGrid", UiControlType.Grid, "PresentationGrid"));
        public INotificationControl Toast => Resolve<INotificationControl>(new("RecoveryToast", UiControlType.AutomationElement, "RecoveryToast"));
        public ILabelControl Opened => Resolve<ILabelControl>(new("Opened", UiControlType.Label, "PresentationOpenedRow"));
        public IButtonControl ShowSuccess => Resolve<IButtonControl>(new("ShowSuccess", UiControlType.Button, "RecoveryShowSuccess"));
        public IButtonControl ShowBackground => Resolve<IButtonControl>(new("ShowBackground", UiControlType.Button, "RecoveryShowBackground"));
        public IButtonControl ShowOther => Resolve<IButtonControl>(new("ShowOther", UiControlType.Button, "RecoveryShowOther"));
        public IButtonControl ArmLate => Resolve<IButtonControl>(new("ArmLate", UiControlType.Button, "RecoveryArmLate"));
        public IButtonControl ArmAfterDown => Resolve<IButtonControl>(new("ArmAfterDown", UiControlType.Button, "RecoveryArmAfterDown"));
        public ITextBoxControl Mode => Resolve<ITextBoxControl>(new("Mode", UiControlType.TextBox, "RecoveryMode"));
        public IButtonControl Heartbeat => Resolve<IButtonControl>(new("Heartbeat", UiControlType.Button, "RecoveryPulseHeartbeat"));
        public IButtonControl CompleteAsyncClose => Resolve<IButtonControl>(new("CompleteAsyncClose", UiControlType.Button, "RecoveryCompleteAsyncClose"));
        public ILabelControl Marker(string name) => Resolve<ILabelControl>(new("Recovery" + name, UiControlType.Label, "Recovery" + name));
    }
}
