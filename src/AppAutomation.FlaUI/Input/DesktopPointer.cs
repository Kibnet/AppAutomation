using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using AppAutomation.Abstractions;
using FlaUI.Core.AutomationElements;

namespace AppAutomation.FlaUI.Input;

/// <summary>
/// Explicit physical desktop gestures. Each call saves the current cursor position after acquiring
/// ownership and restores it after the gesture, including cancellation and assertion failures.
/// </summary>
public static class DesktopPointer
{
    private static readonly DesktopPointerController Controller = new(new WindowsPointerBackend(), new DesktopInputCoordinator());
    private static readonly ConcurrentDictionary<int, PointerSession> Sessions = new();

    /// <summary>Associates physical operations on this app with its session lifetime.</summary>
    public static IDisposable RegisterSession(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var processId = window.Properties.ProcessId.Value;
        var session = new PointerSession();
        if (!Sessions.TryAdd(processId, session))
        {
            // An unregistered raw caller may have introduced the default session for this process.
            if (Sessions.TryRemove(processId, out var previous)) previous.Dispose();
            if (!Sessions.TryAdd(processId, session)) throw new InvalidOperationException("A pointer session was concurrently registered for this application.");
        }
        return new Registration(session);
    }

    /// <summary>Clicks a freshly resolved target, then returns the physical cursor.</summary>
    public static Task ClickAsync(Func<AutomationElement> target, PointerClickOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new PointerClickOptions();
        return ClickCore(target, options, null, null, cancellationToken);
    }

    /// <summary>Clicks an existing element with the same ownership and restoration guarantees.</summary>
    public static void Click(AutomationElement target, PointerClickOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ClickCore(() => target, options ?? new PointerClickOptions(), null, null, default,
            hasFreshIntent: false).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Holds hover through the callback and restores the cursor afterwards. The callback may only read
    /// and assert, must promptly return its Task, and must use the token for waits. A late task cannot
    /// issue another pointer operation; nested gestures are rejected without waiting for ownership.
    /// </summary>
    public static Task HoverAsync(Func<AutomationElement> target, Func<CancellationToken, Task> verifyWhileHovered,
        PointerHoverOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifyWhileHovered);
        options ??= new PointerHoverOptions();
        var (session, remaining, processId) = PrepareCall(target, options.Timeout, cancellationToken);
        return Controller.RunAsync("hover", remaining, operation =>
        {
            var resolved = DesktopPointerTargetResolver.Resolve(target, requireEnabled: false, expectedProcessId: processId);
            operation.Check();
            operation.Log("target", resolved.Point, $"pid={resolved.ProcessId}; hwnd={resolved.Window}; locator={resolved.Locator}");
            return operation.HoverAsync(resolved.Point, verifyWhileHovered, resolved.ValidateOwner, resolved.ValidateExact);
        }, cancellationToken, session);
    }

    /// <summary>Performs a bounded, event-delivered drag and returns the cursor after releasing its button.</summary>
    public static Task DragAndDropAsync(Func<AutomationElement> source, Func<AutomationElement> target,
        PointerDragOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        options ??= new PointerDragOptions();
        if (options.Duration > options.Timeout) throw new ArgumentOutOfRangeException(nameof(options), "Drag duration exceeds timeout.");
        var (session, remaining, processId) = PrepareCall(source, options.Timeout, cancellationToken);
        return Controller.RunAsync("drag", remaining, operation =>
        {
            // Resolve the drop location first: bringing it into view may invalidate source geometry.
            _ = DesktopPointerTargetResolver.Resolve(target, requireEnabled: true, expectedProcessId: processId);
            _ = DesktopPointerTargetResolver.Resolve(source, requireEnabled: true, expectedProcessId: processId);
            // Source preparation may have scrolled the viewport. Read both endpoints again without
            // another scroll: if both cannot remain visible together, fail before injecting Down.
            var destination = DesktopPointerTargetResolver.Resolve(target, true, expectedProcessId: processId, allowPreparation: false);
            var origin = DesktopPointerTargetResolver.Resolve(source, true, expectedProcessId: processId, allowPreparation: false);
            operation.Check();
            operation.Log("source", origin.Point, origin.Locator);
            operation.Log("destination", destination.Point, destination.Locator);
            return operation.DragAsync(origin.Point, destination.Point, options, origin.ValidateOwner,
                validateSource: origin.ValidateExact, validateDestination: destination.ValidateExact);
        }, cancellationToken, session);
    }

    /// <summary>Returns the bounded, content-free trace for attaching to failure artifacts.</summary>
    public static string GetTraceSnapshot() => DesktopPointerController.GetTraceSnapshot();

    /// <summary>Identifies failures after ownership began, which must not trigger another physical fallback.</summary>
    public static bool IsTerminalFailure(Exception error) => DesktopPointerController.IsTerminalFailure(error);

    internal static void ClickAt(AutomationElement target, Point point, PointerButton button = PointerButton.Left,
        int clickCount = 1, Action? prepare = null, string reason = "grid coordinate fallback")
    {
        ArgumentNullException.ThrowIfNull(target);
        ClickCore(() => target, new PointerClickOptions { Button = button, ClickCount = clickCount },
            prepare == null ? null : _ => prepare(), _ => point, default, reason, hasFreshIntent: false).GetAwaiter().GetResult();
    }

    internal static void ClickPrepared(Func<AutomationElement> target, Action<AutomationElement> prepare,
        Func<AutomationElement, Point>? pointFactory = null, PointerClickOptions? options = null,
        string reason = "grid physical fallback", AutomationElement? initialTarget = null, PointerPhaseTrace? profile = null,
        bool hasFreshIntent = false)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ClickCore(target, options ?? new PointerClickOptions(), prepare, pointFactory, default, reason, initialTarget, profile, hasFreshIntent)
            .GetAwaiter().GetResult();
    }

    internal static void ClickFallback(AutomationElement target, string reason, PointerClickOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ClickCore(() => target, options ?? new PointerClickOptions(), null, null, default, reason,
            hasFreshIntent: false).GetAwaiter().GetResult();
    }

    internal static void ClickFallback(Func<AutomationElement> target, string reason, PointerClickOptions? options = null,
        AutomationElement? initialTarget = null, Action? validateBeforePress = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ClickCore(target, options ?? new PointerClickOptions(), null, null, default, reason, initialTarget,
            validateBeforePress: validateBeforePress).GetAwaiter().GetResult();
    }

    internal static void SendGuardedKey(int processId, Func<AutomationElement> target, UiOperationTimeoutBudget budget,
        global::FlaUI.Core.WindowsAPI.VirtualKeyShort key, Action<AutomationElement> validateForeground)
    {
        DesktopPointerController.RejectNestedOperation();
        _ = budget.Remaining;
        var session = Sessions.GetOrAdd(processId, static _ => new PointerSession());
        Controller.RunAsync("picker-key", budget.Remaining, operation =>
        {
            void Check()
            {
                operation.Check();
                _ = budget.Remaining;
            }
            Check();
            var panel = target();
            var identity = panel.Properties.RuntimeId.Value;
            if (identity is not { Length: > 0 } || panel.Properties.ProcessId.Value != processId
                || !panel.Properties.IsKeyboardFocusable.Value
                || !panel.IsEnabled || panel.IsOffscreen)
                throw new InvalidOperationException("The picker panel cannot safely receive a keyboard step.");
            DesktopPointerTargetResolver.EnsureForeground(panel, processId, Check);
            validateForeground(panel);
            Check();
            panel.Focus();
            UiWait.Until(() =>
            {
                Check();
                if (!WindowsPointerBackend.ForegroundBelongsTo(processId))
                    throw new PointerInterferenceException("The picker lost the foreground while acquiring keyboard focus.");
                // Focus polling observes the live peer; only the final fresh resolution can authorize the key.
                validateForeground(panel);
                var currentId = panel.Properties.RuntimeId.Value;
                if (currentId is null || !currentId.AsSpan().SequenceEqual(identity))
                    throw new InvalidOperationException("The picker panel changed while acquiring keyboard focus.");
                return panel.Properties.HasKeyboardFocus.Value;
            }, static focused => focused,
                new UiWaitOptions { Timeout = budget.Remaining, PollInterval = TimeSpan.FromMilliseconds(25) },
                "The picker panel did not acquire keyboard focus.", operation.Token);
            Check();
            var fresh = target();
            var freshIdentity = fresh.Properties.RuntimeId.Value;
            if (!WindowsPointerBackend.ForegroundBelongsTo(processId) || !fresh.Properties.HasKeyboardFocus.Value
                || freshIdentity is null || !freshIdentity.AsSpan().SequenceEqual(identity)
                || !fresh.IsEnabled || fresh.IsOffscreen)
                throw new PointerInterferenceException("The picker panel lost keyboard ownership before its native step.");
            Check();
            validateForeground(fresh);
            Check();
            global::FlaUI.Core.Input.Keyboard.Type(key);
            Check();
            return Task.CompletedTask;
        }, session: session).GetAwaiter().GetResult();
    }

    internal delegate void SelectionClick(Func<AutomationElement> target, Func<AutomationElement, Point> point,
        PointerModifier modifier, Action verifyBeforeClick, bool validateOnly = false);

    internal static void WithSelectionClicks(AutomationElement owner, TimeSpan timeout, Action<SelectionClick> select)
    {
        var preparation = FlaUiPointerPreparation.Capture();
        var (session, remaining, processId) = PrepareCall(() => owner, timeout, default, owner);
        Controller.RunAsync("grid-row-set", remaining, operation =>
        {
            select((target, point, modifier, verifyBeforeClick, validateOnly) =>
            {
                operation.RunBeforeOriginalPress(() =>
                {
                    operation.Check();
                    // Activate before resolving a row: activation can rebuild its UIA projection.
                    DesktopPointerTargetResolver.EnsureForeground(owner, processId, operation.Check);
                    operation.Check();
                    DesktopPointerTarget ResolveCurrent() => DesktopPointerTargetResolver.Resolve(target, true,
                        pointFactory: point, expectedProcessId: processId, allowPreparation: false,
                        rejectInteractiveDescendantHit: true, check: operation.Check, preparation: preparation);
                    var resolved = ResolveCurrent();
                    if (operation.WaitForGestureSeparation(resolved.Point)) resolved = ResolveCurrent();
                    if (validateOnly) { operation.Check(); return; }
                    operation.ClickForSelection(resolved.Point, modifier, () =>
                    {
                        verifyBeforeClick();
                        resolved.ValidateExact(preparation);
                    });
                }, obstruction => preparation?.Recover(target(), obstruction, operation) == true);
            });
            return Task.CompletedTask;
        }, session: session).GetAwaiter().GetResult();
    }

    internal static T WithForegroundClick<T>(AutomationElement owner, TimeSpan timeout, string reason,
        Action prepareForeground, Func<Action<Func<AutomationElement>>, T> execute)
    {
        var preparation = FlaUiPointerPreparation.Capture();
        var (session, remaining, processId) = PrepareCall(() => owner, timeout, default);
        T result = default!;
        Controller.RunAsync("click", remaining, operation =>
        {
            operation.Check();
            if (!WindowsPointerBackend.ForegroundBelongsTo(processId))
            {
                prepareForeground();
                RequireForeground("The popup owner could not acquire the foreground before opening.");
                operation.Log("foreground-ready", detail: "activated before opening popup");
            }
            operation.Check();

            var canClick = true;
            try
            {
                result = execute(target =>
                {
                    if (!canClick) throw new InvalidOperationException("A popup operation may send only one click.");
                    canClick = false;
                    operation.Check();
                    RequireForeground("The popup owner lost the foreground before the click.");
                    ClickInOperation(operation, target, new PointerClickOptions(), null, null, processId, reason,
                        activateWindow: false, preparation: preparation).GetAwaiter().GetResult();
                });
            }
            finally
            {
                canClick = false;
            }
            return Task.CompletedTask;

            void RequireForeground(string message)
            {
                if (WindowsPointerBackend.ForegroundBelongsTo(processId)) return;
                operation.Log("foreground-refused", detail: DesktopPointerTargetResolver.DescribeForeground(owner, processId));
                throw new PointerInterferenceException(message);
            }
        }, session: session).GetAwaiter().GetResult();
        return result;
    }

    internal static void Scroll(AutomationElement target, Func<AutomationElement, Point> pointFactory,
        double amount, bool horizontal = false, Action? prepare = null)
    {
        ArgumentNullException.ThrowIfNull(pointFactory);
        var (session, remaining, processId) = PrepareCall(() => target, TimeSpan.FromSeconds(5), default);
        Controller.RunAsync("wheel-fallback", remaining, operation =>
        {
            operation.Log("physical-fallback", detail: "grid wheel fallback");
            var resolved = DesktopPointerTargetResolver.Resolve(() => target, true,
                prepare == null ? null : _ => prepare(), pointFactory, processId);
            operation.Check();
            operation.Scroll(resolved.Point, amount, horizontal, resolved.ValidateExact);
            return Task.CompletedTask;
        }, session: session).GetAwaiter().GetResult();
    }

    internal static void Drag(AutomationElement target, Point start, Point end, Action? prepare = null, TimeSpan? timeout = null)
    {
        var (session, remaining, processId) = PrepareCall(() => target, timeout ?? TimeSpan.FromSeconds(5), default);
        Controller.RunAsync("scrollbar-drag-fallback", remaining, operation =>
        {
            operation.Log("physical-fallback", detail: "grid scrollbar fallback");
            var resolved = DesktopPointerTargetResolver.Resolve(() => target, true, prepare == null ? null : _ => prepare(), _ => start, processId);
            operation.Check();
            // Scrollbar thumbs capture on Down and can legitimately move by less than the shell
            // drag-and-drop threshold; keep the requested endpoint to preserve virtual-row coverage.
            var dragRemaining = operation.Remaining;
            if (dragRemaining <= TimeSpan.Zero)
                throw new TimeoutException("The scrollbar drag exhausted its timeout before pointer input.");
            var options = timeout.HasValue ? new PointerDragOptions
            {
                Timeout = remaining,
                Duration = TimeSpan.FromMilliseconds(Math.Min(200, dragRemaining.TotalMilliseconds / 2))
            } : new PointerDragOptions();
            return operation.DragAsync(start, end, options, resolved.ValidateOwner,
                requireDragThreshold: false, validateSource: resolved.ValidateExact);
        }, session: session).GetAwaiter().GetResult();
    }

    private static Task ClickCore(Func<AutomationElement> target, PointerClickOptions options,
        Action<AutomationElement>? prepare, Func<AutomationElement, Point>? pointFactory, CancellationToken token,
        string? reason = null, AutomationElement? initialTarget = null, PointerPhaseTrace? profile = null,
        bool hasFreshIntent = true, Action? validateBeforePress = null)
    {
        var preparation = FlaUiPointerPreparation.Capture();
        profile?.Mark("pointer-register-session");
        var (session, remaining, processId) = PrepareCall(target, options.Timeout, token, initialTarget);
        return Controller.RunAsync(options.ClickCount == 2 ? "double-click" : "click", remaining,
            operation => ClickInOperation(operation, target, options, prepare, pointFactory, processId, reason,
                profile: profile, preparation: preparation, hasFreshIntent: hasFreshIntent,
                validateBeforePress: validateBeforePress),
            token, session, profile);
    }

    private static Task ClickInOperation(DesktopPointerController.PointerOperation operation, Func<AutomationElement> target,
        PointerClickOptions options, Action<AutomationElement>? prepare, Func<AutomationElement, Point>? pointFactory,
        int processId, string? reason, bool activateWindow = true, PointerPhaseTrace? profile = null,
        FlaUiPointerPreparation? preparation = null, bool hasFreshIntent = true, Action? validateBeforePress = null)
    {
        if (reason != null) operation.Log("physical-fallback", detail: reason);
        var preparationOnce = new PointerPreparationOnce();
        operation.RunBeforeOriginalPress(() =>
        {
            DesktopPointerTarget ResolveCurrent() => DesktopPointerTargetResolver.Resolve(target, true, element =>
                preparationOnce.Run(() => prepare?.Invoke(element)), pointFactory, processId,
                activateWindow: activateWindow, profile: profile,
                rejectInteractiveDescendantHit: options.RejectInteractiveDescendantHit, check: operation.Check,
                checkBeforePreparation: hasFreshIntent, preparation: preparation);
            var resolved = ResolveCurrent();
            if (operation.WaitForGestureSeparation(resolved.Point)) resolved = ResolveCurrent();
            profile?.Mark("pointer-check-before-click", $"remaining={operation.Remaining.TotalMilliseconds:0}ms");
            operation.Check();
            operation.Log("target", resolved.Point, $"pid={resolved.ProcessId}; hwnd={resolved.Window}; locator={resolved.Locator}");
            profile?.Mark("pointer-send-click", $"remaining={operation.Remaining.TotalMilliseconds:0}ms");
            operation.ClickAsync(resolved.Point, options, () =>
            {
                validateBeforePress?.Invoke();
                resolved.ValidateExact(preparation);
            }).GetAwaiter().GetResult();
        }, obstruction => hasFreshIntent && preparation?.Recover(target(), obstruction, operation) == true);
        return Task.CompletedTask;
    }

    private static (PointerSession Session, TimeSpan Remaining, int ProcessId) PrepareCall(
        Func<AutomationElement> target, TimeSpan timeout, CancellationToken token, AutomationElement? initialTarget = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        DesktopPointerController.RejectNestedOperation();
        token.ThrowIfCancellationRequested();
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1) throw new ArgumentOutOfRangeException(nameof(timeout));
        var timer = Stopwatch.StartNew();
        // Read identity only, without focus, scrolling, cursor reads or physical preparation. Fresh
        // target geometry is resolved under ownership on this same caller thread before the first await.
        var element = initialTarget ?? target() ?? throw new InvalidOperationException("The pointer target resolver returned null.");
        var processId = element.Properties.ProcessId.Value;
        var session = Sessions.GetOrAdd(processId, _ => new PointerSession());
        var remaining = timeout - timer.Elapsed;
        if (remaining <= TimeSpan.Zero) throw new TimeoutException("Pointer target lookup exhausted the operation timeout.");
        return (session, remaining, processId);
    }

    private sealed class Registration(PointerSession session) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            session.Dispose();
            // Keep a disposed tombstone: an in-flight identity lookup or a late raw caller must not
            // recreate an implicit session after disposal. A new explicit RegisterSession may replace it.
        }
    }
}
