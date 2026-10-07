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
        ClickAsync(() => target, options).GetAwaiter().GetResult();
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
            prepare == null ? null : _ => prepare(), _ => point, default, reason).GetAwaiter().GetResult();
    }

    internal static void ClickPrepared(Func<AutomationElement> target, Action<AutomationElement> prepare,
        Func<AutomationElement, Point>? pointFactory = null, PointerClickOptions? options = null,
        string reason = "grid physical fallback")
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ClickCore(target, options ?? new PointerClickOptions(), prepare, pointFactory, default, reason).GetAwaiter().GetResult();
    }

    internal static void ClickFallback(AutomationElement target, string reason, PointerClickOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ClickFallback(() => target, reason, options);
    }

    internal static void ClickFallback(Func<AutomationElement> target, string reason, PointerClickOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ClickCore(target, options ?? new PointerClickOptions(), null, null, default, reason).GetAwaiter().GetResult();
    }

    internal static void Scroll(AutomationElement target, Point point, double amount, bool horizontal = false, Action? prepare = null)
    {
        var (session, remaining, processId) = PrepareCall(() => target, TimeSpan.FromSeconds(5), default);
        Controller.RunAsync("wheel-fallback", remaining, operation =>
        {
            operation.Log("physical-fallback", detail: "grid wheel fallback");
            var resolved = DesktopPointerTargetResolver.Resolve(() => target, true, prepare == null ? null : _ => prepare(), _ => point, processId);
            operation.Check();
            operation.Scroll(point, amount, horizontal, resolved.ValidateExact);
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
        string? reason = null)
    {
        var (session, remaining, processId) = PrepareCall(target, options.Timeout, token);
        return Controller.RunAsync(options.ClickCount == 2 ? "double-click" : "click", remaining, operation =>
        {
            if (reason != null) operation.Log("physical-fallback", detail: reason);
            var resolved = DesktopPointerTargetResolver.Resolve(target, true, prepare, pointFactory, processId);
            operation.Check();
            operation.Log("target", resolved.Point, $"pid={resolved.ProcessId}; hwnd={resolved.Window}; locator={resolved.Locator}");
            return operation.ClickAsync(resolved.Point, options, resolved.ValidateExact);
        }, token, session);
    }

    private static (PointerSession Session, TimeSpan Remaining, int ProcessId) PrepareCall(Func<AutomationElement> target, TimeSpan timeout, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(target);
        DesktopPointerController.RejectNestedOperation();
        token.ThrowIfCancellationRequested();
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1) throw new ArgumentOutOfRangeException(nameof(timeout));
        var timer = Stopwatch.StartNew();
        // Read identity only, without focus, scrolling, cursor reads or physical preparation. Fresh
        // target geometry is resolved under ownership on this same caller thread before the first await.
        var element = target() ?? throw new InvalidOperationException("The pointer target resolver returned null.");
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
