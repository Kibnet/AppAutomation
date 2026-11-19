using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Input;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using DotnetDebug.AppAutomation.TestHost;
using DotnetDebug.Avalonia;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

[NotInParallel("DesktopUi")]
public sealed class NotificationOcclusionFlaUiTests
{
    [Test]
    public async Task BackgroundToastAfterExplicitSuccessDismissKeepsTheCapturedRowAndHistory()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = Launch();
        var page = CreatePage(session);
        page.ClickButton(static p => p.ShowSuccess);
        await Assert.That(page.Toast.Text).IsEqualTo("success");
        var row = GridRowKeyReader.CaptureFirst(page.Table);
        page.Toast.Dismiss();
        await Assert.That(page.Marker("Dismiss").Text).IsEqualTo("1");
        page.ClickButton(static p => p.ShowBackground);
        page.WaitUntilNotificationContains(static p => p.Toast, "background");
        CaptureScene(session, "notification-before.png");

        page.OpenGridRow(static p => p.Table, row);
        CaptureScene(session, "notification-after.png");

        await Assert.That(page.Marker("Dismiss").Text).IsEqualTo("2");
        await Assert.That(page.Marker("Down").Text).IsEqualTo("2");
        await Assert.That(page.Marker("Up").Text).IsEqualTo("2");
        await Assert.That(page.Marker("Open").Text).IsEqualTo("1");
        await Assert.That(page.Marker("Visible").Text).IsEqualTo("0");
        await Assert.That(page.Marker("History").Text).IsEqualTo("success|background");
        await Assert.That(page.Opened.Text).IsEqualTo(row.Conditions.Single().Value);
    }

    [Test]
    [Arguments("")]
    [Arguments("reorder")]
    [Arguments("recreate")]
    [Arguments("successive")]
    [Arguments("physical-dismiss")]
    public async Task EagerRegisteredBlockersKeepOtherToastAndOpenOnlyTheOriginalRow(string mode)
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = Launch();
        var page = CreatePage(session);
        var row = GridRowKeyReader.CaptureFirst(page.Table);
        page.EnterText(static p => p.Mode, mode);
        page.ClickButton(static p => p.ShowOther);
        page.ClickButton(static p => p.ShowBackground);

        // No notification property has been resolved; registration must come from the declared chain.
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
        if (mode == "physical-dismiss")
        {
            await Assert.That(page.Marker("FirstClickCount").Text).IsEqualTo("1");
            await Assert.That(page.Marker("OtherPresses").Text).IsEqualTo("1");
        }
    }

    [Test]
    public async Task DeferredDismissAllowsUiHeartbeatBeforeTheOriginalGesture()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = Launch();
        var page = CreatePage(session);
        var row = GridRowKeyReader.CaptureFirst(page.Table);
        page.EnterText(static p => p.Mode, "async");
        page.ClickButton(static p => p.ShowBackground);
        var opening = Task.Run(() => page.OpenGridRow(static p => p.Table, row));
        try
        {
            await UiWait.UntilAsync(() =>
            {
                if (opening.IsFaulted) opening.GetAwaiter().GetResult();
                return page.Marker("Pending").Text;
            }, static value => value == "1",
                new UiWaitOptions { Timeout = TimeSpan.FromSeconds(3), PollInterval = TimeSpan.FromMilliseconds(20) });
            await Assert.That(opening.IsCompleted).IsFalse();
            page.ClickButton(static p => p.Heartbeat);
            await Assert.That(page.Marker("Heartbeat").Text).IsEqualTo("1");
            await Assert.That(page.Marker("Down").Text).IsEqualTo("0");
            page.ClickButton(static p => p.CompleteAsyncClose);
            await opening.WaitAsync(TimeSpan.FromSeconds(6));
            await Assert.That(page.Marker("DownBeforeAsyncClose").Text).IsEqualTo("0");
            await Assert.That(page.Marker("Down").Text).IsEqualTo("2");
            await Assert.That(page.Marker("Dismiss").Text).IsEqualTo("1");
            await Assert.That(page.Opened.Text).IsEqualTo(row.Conditions.Single().Value);
        }
        finally
        {
            if (!opening.IsCompleted) page.ClickButton(static p => p.CompleteAsyncClose);
        }
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
    public async Task UnsupportedBlockersOrLostRowsRefuseTheOriginalGesture(string mode, string dismissCount)
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = Launch();
        var page = CreatePage(session);
        var row = GridRowKeyReader.CaptureFirst(page.Table);
        page.EnterText(static p => p.Mode, mode);
        page.ClickButton(static p => p.ShowBackground);

        await Assert.That(() => page.OpenGridRow(static p => p.Table, row, timeoutMs: 2000)).Throws<UiOperationException>();

        await Assert.That(page.Marker("Dismiss").Text).IsEqualTo(dismissCount);
        await Assert.That(page.Marker("Down").Text).IsEqualTo("0");
        await Assert.That(page.Marker("OtherPresses").Text).IsEqualTo("0");
        await Assert.That(page.Marker("Open").Text).IsEqualTo("0");
        await Assert.That(page.Opened.Text).IsEqualTo("None");
        await Assert.That(page.Marker("History").Text).IsEqualTo("background");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ANotificationArrivingDuringInputNeverReplaysAnAttemptedGesture(bool appearOnMotion)
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = Launch();
        var page = CreatePage(session);
        var row = GridRowKeyReader.CaptureFirst(page.Table);
        if (appearOnMotion) page.ClickButton(static p => p.ArmLate);
        else page.ClickButton(static p => p.ArmAfterDown);
        var previous = DoubleClickTrace().Select(static entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        UiOperationException? failure = null;
        try { page.OpenGridRow(static p => p.Table, row); }
        catch (UiOperationException error)
        {
            failure = error;
        }
        var trace = DoubleClickTrace().Where(entry => !previous.Contains(entry.Id)).ToArray();

        // Native WM_MOUSEMOVE is asynchronous: it may be processed before or after the first
        // input packet. Classify by actual controller packets, not by the row's routed events.
        if (appearOnMotion && failure is null)
        {
            await Assert.That(trace.Count(static entry => entry.Stage == "notification-dismiss")).IsEqualTo(1);
            await Assert.That(trace.Count(static entry => entry.Stage == "down")).IsEqualTo(2);
            await Assert.That(page.Marker("Dismiss").Text).IsEqualTo("1");
            await Assert.That(page.Marker("Down").Text).IsEqualTo("2");
            await Assert.That(page.Marker("Open").Text).IsEqualTo("1");
            await Assert.That(page.Marker("Visible").Text).IsEqualTo("0");
            await Assert.That(page.Opened.Text).IsEqualTo(row.Conditions.Single().Value);
            return;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.InnerException is PointerInterferenceException).IsTrue();
        await Assert.That(trace.Count(static entry => entry.Stage == "down")).IsEqualTo(1);
        await Assert.That(trace.Count(static entry => entry.Stage == "notification-dismiss")).IsEqualTo(0);
        if (!appearOnMotion) await Assert.That(page.Marker("Down").Text).IsEqualTo("1");
        await Assert.That(page.Marker("Open").Text).IsEqualTo("0");
        await Assert.That(page.Marker("Dismiss").Text).IsEqualTo("0");
        await Assert.That(page.Marker("Visible").Text).IsEqualTo("1");
    }

    [Test]
    public async Task AReplacedLocatorTargetIsResolvedAgainBeforeTheSingleClick()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = Launch();
        var page = CreatePage(session);
        page.EnterText(static p => p.Mode, "input-target;replace-input");
        page.ClickButton(static p => p.ShowBackground);

        await page.PointerClickAsync(new UiControlDefinition("RecoveryTarget", UiControlType.TextBox, "RecoveryLocatorTarget"));

        await Assert.That(page.Marker("InputDown").Text).IsEqualTo("1");
        await Assert.That(page.Marker("OldInputDown").Text).IsEqualTo("0");
        await Assert.That(page.Marker("Dismiss").Text).IsEqualTo("1");
        await Assert.That(page.Marker("Visible").Text).IsEqualTo("0");
    }

    [Test]
    public async Task RawTargetsDoNotAcquireNotificationPermissionsFromTheOwningWindow()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = Launch();
        var page = CreatePage(session);
        var raw = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId("RecoveryLocatorTarget"))
            ?? throw new InvalidOperationException("The native locator target was not found.");
        page.EnterText(static p => p.Mode, "input-target");
        page.ClickButton(static p => p.ShowBackground);

        UiWait.Until(() =>
        {
            var bounds = raw.BoundingRectangle;
            var point = new System.Drawing.Point((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2));
            for (var hit = raw.Automation.FromPoint(point); hit is not null; hit = hit.Parent)
                if (hit.Properties.AutomationId.ValueOrDefault == "RecoveryToast") return true;
            return false;
        }, static blocked => blocked,
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(3), PollInterval = TimeSpan.FromMilliseconds(20) },
            "The raw target must be covered before checking refusal without notification permissions.");

        Exception? failure = null;
        try { await DesktopPointer.ClickAsync(() => raw); }
        catch (Exception exception) { failure = exception; }

        await Assert.That(failure).IsNotNull();
        await Assert.That(page.Marker("Dismiss").Text).IsEqualTo("0");
        await Assert.That(page.Marker("InputDown").Text).IsEqualTo("0");
        await Assert.That(page.Marker("Visible").Text).IsEqualTo("1");
    }

    private static DesktopAppSession Launch()
    {
        var defaults = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(buildBeforeLaunch: false);
        var session = DesktopAppSession.Launch(new DesktopAppLaunchOptions
        {
            ExecutablePath = defaults.ExecutablePath, WorkingDirectory = defaults.WorkingDirectory,
            Arguments = defaults.Arguments, DisposeCallback = defaults.DisposeCallback,
            MainWindowTimeout = defaults.MainWindowTimeout, PollInterval = defaults.PollInterval,
            WindowPlacement = defaults.WindowPlacement,
            EnvironmentVariables = new Dictionary<string, string?>(defaults.EnvironmentVariables, StringComparer.Ordinal)
            {
                ["APPAUTOMATION_NATIVE_GRID_RUNTIME_FIXTURE"] = "presentation",
                [GridPresentationFixture.NotificationRecoveryEnvironmentVariable] = "1"
            }
        });
        try
        {
            session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(global::FlaUI.Core.Definitions.WindowVisualState.Maximized);
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private static Page CreatePage(DesktopAppSession session) => new(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory)
        .WithGridAutomation(GridPresentationFixture.CreateCatalog())
        .WithNotification(new UiControlDefinition("RecoveryToast", UiControlType.AutomationElement, "RecoveryToast"),
            NotificationControlParts.ByAutomationIds("RecoveryToastText", "RecoveryToastClose")));

    private static void CaptureScene(DesktopAppSession session, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("APPAUTOMATION_NOTIFICATION_ARTIFACTS")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults", "notification-occlusion");
        var file = Path.Combine(Directory.CreateDirectory(directory).FullName, fileName);
        global::FlaUI.Core.Capturing.Capture.Element(session.MainWindow).ToFile(file);
        Console.WriteLine($"Notification recovery screenshot: {file}");
    }

    private static IEnumerable<(string Id, string Stage)> DoubleClickTrace()
    {
        foreach (var line in DesktopPointer.GetTraceSnapshot().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.GetProperty("Kind").GetString() == "double-click")
                yield return (root.GetProperty("OperationId").GetString()!, root.GetProperty("Stage").GetString()!);
        }
    }

    private sealed class Page(IUiControlResolver resolver) : UiPage(resolver)
    {
        public IGridControl Table => Resolve<IGridControl>(new("PresentationGrid", UiControlType.Grid, "PresentationGrid"));
        public INotificationControl Toast => Resolve<INotificationControl>(new("RecoveryToast", UiControlType.AutomationElement, "RecoveryToast"));
        public ILabelControl Opened => Resolve<ILabelControl>(new("Opened", UiControlType.Label, "PresentationOpenedRow"));
        public ITextBoxControl Mode => Resolve<ITextBoxControl>(new("Mode", UiControlType.TextBox, "RecoveryMode"));
        public IButtonControl ShowSuccess => Button("ShowSuccess");
        public IButtonControl ShowBackground => Button("ShowBackground");
        public IButtonControl ShowOther => Button("ShowOther");
        public IButtonControl ArmLate => Button("ArmLate");
        public IButtonControl ArmAfterDown => Button("ArmAfterDown");
        public IButtonControl Heartbeat => Resolve<IButtonControl>(new("Heartbeat", UiControlType.Button, "RecoveryPulseHeartbeat"));
        public IButtonControl CompleteAsyncClose => Button("CompleteAsyncClose");
        public ILabelControl Marker(string name) => Resolve<ILabelControl>(new("Recovery" + name, UiControlType.Label, "Recovery" + name));
        private IButtonControl Button(string name) => Resolve<IButtonControl>(new(name, UiControlType.Button, "Recovery" + name));
    }
}
