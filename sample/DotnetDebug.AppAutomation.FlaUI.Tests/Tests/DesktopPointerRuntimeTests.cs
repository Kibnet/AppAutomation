using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Input;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using DotnetDebug.AppAutomation.TestHost;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

[NotInParallel("DesktopUi")]
public sealed class DesktopPointerRuntimeTests
{
    private static readonly UiControlDefinition ClickTarget = Definition("PointerClickTarget");
    private static readonly UiControlDefinition TooltipTarget = Definition("PointerDisabledTooltipTarget");
    private static readonly UiControlDefinition DragSource = Definition("PointerDragSource");
    private static readonly UiControlDefinition DropTarget = Definition("PointerDropTarget");
    private static string? _artifactsDirectory;

    [Test]
    public async Task ClickRightClickAndDoubleClick_ReachNativeEvents_AndRestoreCursor()
    {
        using var session = LaunchFixture();
        var page = CreatePage(session);
        var saved = ReadPosition();
        RecordPosition("clicks-before", saved);
        CaptureFixture(session, "clicks-before.png");

        await page.PointerClickAsync(ClickTarget);
        await WaitForTextAsync(session, "PointerLeftPresses", "1");
        await WaitForTextAsync(session, "PointerLeftReleases", "1");
        await AssertRestoredAsync(saved, "single-click-restored");

        // Immediate consecutive single-click requests must not become a native double-click.
        await page.PointerClickAsync(ClickTarget);
        await WaitForTextAsync(session, "PointerLeftPresses", "2");
        await WaitForTextAsync(session, "PointerLeftReleases", "2");
        await Assert.That(ReadText(session, "PointerDoubleClicks")).IsEqualTo("0");
        await AssertRestoredAsync(saved, "second-single-click-restored");

        await page.PointerClickAsync(ClickTarget, new PointerClickOptions { Button = PointerButton.Right });
        await WaitForTextAsync(session, "PointerRightPresses", "1");
        await WaitForTextAsync(session, "PointerRightReleases", "1");
        await AssertRestoredAsync(saved, "right-click-restored");

        await page.PointerClickAsync(ClickTarget, new PointerClickOptions { ClickCount = 2 });
        await WaitForTextAsync(session, "PointerLeftPresses", "4");
        await WaitForTextAsync(session, "PointerLeftReleases", "4");
        try
        {
            await WaitForTextAsync(session, "PointerDoubleClicks", "1");
        }
        finally
        {
            CaptureFixture(session, "clicks-after.png");
        }
        await AssertRestoredAsync(saved, "double-click-restored");
        CaptureFixture(session, "clicks-after.png");
    }

    [Test]
    public async Task SemanticInvoke_ChangesUi_WithoutPhysicalInput()
    {
        using var session = LaunchFixture();
        var page = CreatePage(session);
        var saved = ReadPosition();
        var traceBefore = DesktopPointerController.GetTraceSnapshot();

        page.ClickButton(static candidate => candidate.ActionButton);
        await WaitForTextAsync(session, "PointerInvocations", "1");

        using (Assert.Multiple())
        {
            await Assert.That(ReadText(session, "PointerLeftPresses")).IsEqualTo("0");
            await Assert.That(ReadText(session, "PointerLeftReleases")).IsEqualTo("0");
            await Assert.That(DesktopPointerController.GetTraceSnapshot()).IsEqualTo(traceBefore);
        }

        await AssertRestoredAsync(saved, "semantic-invoke-no-movement");
    }

    [Test]
    public async Task DisabledTooltip_IsVerifiedAndCapturedWhileHovered_ThenCursorReturns()
    {
        using var session = LaunchFixture();
        var page = CreatePage(session);
        var saved = ReadPosition();
        await Assert.That(Find(session, TooltipTarget.LocatorValue).IsEnabled).IsFalse();
        var callbackCount = 0;

        await page.HoverAsync(TooltipTarget, async cancellationToken =>
        {
            callbackCount++;
            var tooltip = await WaitForTooltipAsync(session, cancellationToken);
            await Assert.That(tooltip.Name).IsEqualTo("Tooltip belongs to disabled target");
            await Assert.That(PointerIsInside(Find(session, TooltipTarget.LocatorValue))).IsTrue();
            CaptureFixture(session, "tooltip-open.png");
            RecordPosition("tooltip-verified-before-restore", ReadPosition());
        });

        await Assert.That(callbackCount).IsEqualTo(1);
        await AssertRestoredAsync(saved, "disabled-tooltip-restored");
    }

    [Test]
    public async Task AlreadyOpenTooltip_IsAccepted_WithoutRequiringAnAdditionalTextElement()
    {
        using var session = LaunchFixture();
        var page = CreatePage(session);
        var saved = ReadPosition();
        page.OpenTooltipButton.Invoke();
        _ = await WaitForTooltipAsync(session, CancellationToken.None);

        await page.HoverAsync(TooltipTarget, async cancellationToken =>
        {
            var tooltip = await WaitForTooltipAsync(session, cancellationToken);
            await Assert.That(tooltip.Name).IsEqualTo("Tooltip belongs to disabled target");
            CaptureFixture(session, "tooltip-already-open.png");
        });

        await AssertRestoredAsync(saved, "already-open-tooltip-restored");
    }

    [Test]
    public async Task DragAndDrop_RaisesNativeDropAndReordersPayload_ThenReleasesAndRestores()
    {
        using var session = LaunchFixture();
        var page = CreatePage(session);
        var saved = ReadPosition();
        await Assert.That(ReadText(session, "PointerOrder")).IsEqualTo("A,B");
        CaptureFixture(session, "drag-before.png");

        await page.DragAndDropAsync(DragSource, DropTarget, new PointerDragOptions
        {
            Timeout = TimeSpan.FromSeconds(15),
            Duration = TimeSpan.FromMilliseconds(500)
        });

        try
        {
            await WaitForTextAsync(session, "PointerDrops", "1");
        }
        finally
        {
            File.WriteAllText(Artifact("drag-result.json"), JsonSerializer.Serialize(new
            {
                Drops = ReadText(session, "PointerDrops"),
                Order = ReadText(session, "PointerOrder"),
                Result = ReadText(session, "PointerDragResult"),
                Overs = ReadText(session, "PointerDragOvers"),
                FixtureEventTrace = Artifact("fixture-native-events.jsonl")
            }));
            CaptureFixture(session, "drag-after.png");
        }
        await WaitForTextAsync(session, "PointerOrder", "B,A");
        await WaitForTextAsync(session, "PointerDragResult", "Move");
        await Assert.That(int.Parse(ReadText(session, "PointerDragOvers"), CultureInfo.InvariantCulture)).IsGreaterThan(0);
        await AssertRestoredAsync(saved, "native-drop-restored");
        CaptureFixture(session, "drag-after.png");
    }

    [Test]
    public async Task FailedHover_PreservesAssertionFailure_AndRestoresCursor()
    {
        using var session = LaunchFixture();
        var page = CreatePage(session);
        var saved = ReadPosition();
        var failure = new InvalidOperationException("Expected tooltip verification failure.");

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            page.HoverAsync(TooltipTarget, _ => Task.FromException(failure)));

        await Assert.That(observed).IsSameReferenceAs(failure);
        var artifacts = observed.Data["AppAutomation.Pointer.Artifacts"] as IReadOnlyList<UiFailureArtifact>;
        await Assert.That(artifacts?.Any(artifact => artifact.Kind == "pointer-trace")).IsTrue();
        await AssertRestoredAsync(saved, "failed-hover-restored");
    }

    [Test]
    public async Task CancelledHover_CompletesCleanupBeforeReturningCancellation()
    {
        using var session = LaunchFixture();
        var page = CreatePage(session);
        var saved = ReadPosition();
        using var cancellation = new CancellationTokenSource();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            page.HoverAsync(TooltipTarget, token =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(token);
            }, cancellationToken: cancellation.Token));

        await AssertRestoredAsync(saved, "cancelled-hover-restored");
    }

    [Test]
    public async Task ReplacedTargetInSameProcess_DoesNotReceiveSecondClick()
    {
        using var session = LaunchFixture(replaceClickTarget: true);
        var page = CreatePage(session);
        var saved = ReadPosition();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            page.PointerClickAsync(ClickTarget, new PointerClickOptions { ClickCount = 2 }));

        await Assert.That(DesktopPointer.IsTerminalFailure(failure)).IsTrue();
        await WaitForTextAsync(session, "PointerLeftPresses", "1");
        await WaitForTextAsync(session, "PointerInvocations", "1");
        await Assert.That(Find(session, "PointerReplacement").IsAvailable).IsTrue();
        await Assert.That(ReadText(session, "PointerReplacementPresses")).IsEqualTo("0");
        await AssertRestoredAsync(saved, "replaced-target-restored");
    }

    [Test]
    public async Task UniqueAutomationId_TakesPrecedenceOverUnrelatedNameFallback()
    {
        using var session = LaunchFixture();
        var page = CreatePage(session);
        var saved = ReadPosition();
        await Assert.That(Find(session, "PointerNameCollision").Name).IsEqualTo(ClickTarget.LocatorValue);

        await page.PointerClickAsync(ClickTarget with { FallbackToName = true });

        await WaitForTextAsync(session, "PointerLeftPresses", "1");
        await WaitForTextAsync(session, "PointerInvocations", "1");
        await Assert.That(ReadText(session, "PointerReplacementPresses")).IsEqualTo("0");
        await AssertRestoredAsync(saved, "primary-id-precedes-name-restored");
    }

    [Test]
    public async Task DuplicateAutomationIds_AreRejectedBeforeMovementOrButtonDown()
    {
        using var session = LaunchFixture();
        var page = CreatePage(session);
        var saved = ReadPosition();
        var traceBefore = DesktopPointer.GetTraceSnapshot();
        var duplicates = session.MainWindow.FindAllDescendants(
            session.ConditionFactory.ByAutomationId("PointerDuplicateId"));
        await Assert.That(duplicates.Length).IsEqualTo(2);

        var failure = await Assert.ThrowsAsync<UiControlResolutionException>(() =>
            page.PointerClickAsync(Definition("PointerDuplicateId") with { FallbackToName = true }));

        using (Assert.Multiple())
        {
            await Assert.That(failure.Failure).IsEqualTo(UiControlResolutionFailure.Ambiguous);
            await Assert.That(DesktopPointer.GetTraceSnapshot()).IsEqualTo(traceBefore);
            await Assert.That(ReadText(session, "PointerLeftPresses")).IsEqualTo("0");
            await Assert.That(ReadText(session, "PointerRightPresses")).IsEqualTo("0");
            await Assert.That(ReadText(session, "PointerReplacementPresses")).IsEqualTo("0");
        }
        await AssertRestoredAsync(saved, "ambiguous-id-no-input");
    }

    [Test]
    public async Task ExternalMovementDuringHover_StopsWithoutChasing_AndRestores()
    {
        using var session = LaunchFixture();
        var page = CreatePage(session);
        var saved = ReadPosition();
        var callbacks = 0;

        var failure = await Assert.ThrowsAsync<PointerInterferenceException>(() =>
            page.HoverAsync(TooltipTarget, async token =>
            {
                callbacks++;
                var hovered = ReadPosition();
                // A controlled external actor moves the real cursor without using the controller.
                using (WindowsPointerBackend.PhysicalDpiScope.Enter())
                {
                    if (!SetPhysicalCursorPos(hovered.X + 4, hovered.Y + 3))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                await Task.Delay(100, token);
            }));

        await Assert.That(callbacks).IsEqualTo(1);
        var trace = (string)failure.Data["AppAutomation.Pointer.Trace"]!;
        await Assert.That(trace.Split('\n').Count(line => line.Contains("\"Stage\":\"move-requested\"", StringComparison.Ordinal))).IsEqualTo(1);
        await AssertRestoredAsync(saved, "external-movement-restored");
    }

    [Test]
    public async Task DisposedRegistration_DoesNotReviveRawPointerSession()
    {
        using var session = LaunchFixture();
        var saved = ReadPosition();
        using var registration = DesktopPointer.RegisterSession(session.MainWindow);
        registration.Dispose();

        var failure = await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            DesktopPointer.ClickAsync(() => Find(session, "PointerClickTarget")));

        await Assert.That(DesktopPointer.IsTerminalFailure(failure)).IsTrue();
        await Assert.That(ReadText(session, "PointerLeftPresses")).IsEqualTo("0");
        await AssertRestoredAsync(saved, "disposed-session-no-input");
    }

    private static DesktopAppSession LaunchFixture(bool replaceClickTarget = false)
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        var defaults = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(buildBeforeLaunch: false);
        var environment = new Dictionary<string, string?>(defaults.EnvironmentVariables, StringComparer.Ordinal)
        {
            ["APPAUTOMATION_POINTER_FIXTURE"] = "1",
            ["APPAUTOMATION_POINTER_REPLACEMENT_FIXTURE"] = replaceClickTarget ? "1" : "0",
            ["APPAUTOMATION_POINTER_FIXTURE_TRACE_PATH"] = Artifact("fixture-native-events.jsonl")
        };
        return DesktopAppSession.Launch(new DesktopAppLaunchOptions
        {
            ExecutablePath = defaults.ExecutablePath,
            WorkingDirectory = defaults.WorkingDirectory,
            Arguments = defaults.Arguments,
            EnvironmentVariables = environment,
            DisposeCallback = defaults.DisposeCallback,
            MainWindowTimeout = defaults.MainWindowTimeout,
            PollInterval = defaults.PollInterval,
            WindowPlacement = defaults.WindowPlacement
        });
    }

    private static PointerPage CreatePage(DesktopAppSession session) =>
        new(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));

    private static UiControlDefinition Definition(string id) =>
        new(id, UiControlType.AutomationElement, id, FallbackToName: false);

    private static AutomationElement Find(DesktopAppSession session, string id) =>
        session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(id))
        ?? throw new InvalidOperationException($"Pointer fixture control '{id}' was not found.");

    private static string ReadText(DesktopAppSession session, string id) => Find(session, id).Name;

    private static Task<string> WaitForTextAsync(DesktopAppSession session, string id, string expected) =>
        UiWait.UntilAsync(() => ReadText(session, id), actual => actual == expected,
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(3), PollInterval = TimeSpan.FromMilliseconds(25) },
            $"Native pointer fixture '{id}' did not become '{expected}'.");

    private static Task<AutomationElement> WaitForTooltipAsync(DesktopAppSession session, CancellationToken cancellationToken) =>
        WaitForTooltipCoreAsync(session, cancellationToken);

    private static async Task<AutomationElement> WaitForTooltipCoreAsync(DesktopAppSession session, CancellationToken cancellationToken)
    {
        var tooltip = await UiWait.UntilAsync(() => FindVisibleTooltip(session), element => element is not null,
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(3), PollInterval = TimeSpan.FromMilliseconds(25) },
            "The disabled target's native tooltip was not visible.", cancellationToken);
        return tooltip!;
    }

    private static AutomationElement? FindVisibleTooltip(DesktopAppSession session)
    {
        var processId = session.MainWindow.Properties.ProcessId.Value;
        var roots = session.MainWindow.Automation.GetDesktop()
            .FindAllChildren(factory => factory.ByProcessId(processId));
        foreach (var root in roots)
        {
            var tooltip = root.FindFirstDescendant(session.ConditionFactory.ByAutomationId("PointerTooltipText"));
            if (tooltip is not null && !tooltip.IsOffscreen
                && tooltip.BoundingRectangle.Width > 0 && tooltip.BoundingRectangle.Height > 0)
                return tooltip;
        }

        return null;
    }

    private static async Task AssertRestoredAsync(Point expected, string stage)
    {
        var actual = ReadPosition();
        RecordPosition(stage, actual);
        using (Assert.Multiple())
        {
            await Assert.That(actual).IsEqualTo(expected);
            foreach (var key in new[] { 0x01, 0x02, 0x04, 0x05, 0x06 })
                await Assert.That(GetAsyncKeyState(key) & 0x8000).IsEqualTo(0);
        }
        File.WriteAllText(Artifact("pointer-trace.jsonl"), DesktopPointer.GetTraceSnapshot());
    }

    private static Point ReadPosition()
    {
        using var dpi = WindowsPointerBackend.PhysicalDpiScope.Enter();
        if (!GetPhysicalCursorPos(out var point))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the physical cursor for the native assertion.");
        return new Point(point.X, point.Y);
    }

    private static bool PointerIsInside(AutomationElement target)
    {
        using var dpi = WindowsPointerBackend.PhysicalDpiScope.Enter();
        return target.BoundingRectangle.Contains(ReadPosition());
    }

    private static void CaptureFixture(DesktopAppSession session, string name)
    {
        using var dpi = WindowsPointerBackend.PhysicalDpiScope.Enter();
        var handle = session.MainWindow.Properties.NativeWindowHandle.Value;
        if (!GetClientRect(handle, out var rect))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var origin = new NativePoint();
        if (!ClientToScreen(handle, ref origin))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        Capture.Rectangle(new Rectangle(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top))
            .ToFile(Artifact(name));
    }

    private static void RecordPosition(string stage, Point point) =>
        File.AppendAllText(Artifact("cursor-observations.jsonl"),
            JsonSerializer.Serialize(new { Stage = stage, X = point.X, Y = point.Y, Timestamp = DateTimeOffset.UtcNow })
            + Environment.NewLine);

    private static string Artifact(string fileName)
    {
        if (_artifactsDirectory is null)
        {
            var explicitDirectory = Environment.GetEnvironmentVariable("APPAUTOMATION_POINTER_ARTIFACT_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(explicitDirectory))
            {
                _artifactsDirectory = Path.GetFullPath(explicitDirectory);
            }
            else
            {
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                while (root.Parent is not null && !File.Exists(Path.Combine(root.FullName, "AppAutomation.sln")))
                    root = root.Parent;
                _artifactsDirectory = Path.Combine(root.FullName, "artifacts", "pointer",
                    $"runtime-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}");
            }
            Directory.CreateDirectory(_artifactsDirectory);
        }

        return Path.Combine(_artifactsDirectory, fileName);
    }

    private sealed class PointerPage(IUiControlResolver resolver) : UiPage(resolver)
    {
        public IButtonControl ActionButton => Resolve<IButtonControl>(ClickTarget with { ControlType = UiControlType.Button });
        public IButtonControl OpenTooltipButton => Resolve<IButtonControl>(Definition("PointerOpenTooltip") with { ControlType = UiControlType.Button });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr handle, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPhysicalCursorPos(int x, int y);
}
