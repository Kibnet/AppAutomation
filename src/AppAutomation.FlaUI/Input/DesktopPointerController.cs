using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using AppAutomation.Abstractions;

namespace AppAutomation.FlaUI.Input;

/// <summary>Physical input stopped because the shared desktop changed unexpectedly.</summary>
public sealed class PointerInterferenceException(string message) : InvalidOperationException(message);

/// <summary>The operation could not safely release its buttons and restore the saved cursor position.</summary>
public sealed class PointerCleanupException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>A content-free record of a physical pointer operation.</summary>
public sealed record PointerTraceEntry(Guid OperationId, DateTimeOffset Timestamp, int ProcessId,
    string Kind, string Stage, int? X, int? Y, string? Detail);

internal sealed class PointerSession : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _operations = new();
    private int _faulted;
    private int _disposed;
    internal CancellationToken Token => _cancellation.Token;
    internal bool IsFaulted => Volatile.Read(ref _faulted) != 0;
    internal void Fault() => Interlocked.Exchange(ref _faulted, 1);
    internal void Begin(Guid id)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (IsFaulted) throw new PointerCleanupException("This desktop session is faulted after unsafe pointer cleanup; create a new session.");
        _operations[id] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Volatile.Read(ref _disposed) != 0) { End(id); throw new ObjectDisposedException(nameof(PointerSession)); }
    }
    internal void End(Guid id) { if (_operations.TryRemove(id, out var completed)) completed.TrySetResult(); }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancellation.Cancel();
        if (!Task.WhenAll(_operations.Values.Select(value => value.Task)).Wait(TimeSpan.FromSeconds(1)))
        {
            Fault();
            throw new PointerCleanupException("Session disposal cancelled pointer work, but it did not finish its bounded cleanup within one second.");
        }
        // The token remains usable by a late callback; disposing its source here races with its registrations.
    }
}

internal sealed class DesktopPointerController(IPointerBackend backend, IDesktopInputCoordinator coordinator)
{
    internal const string OperationKey = "AppAutomation.Pointer.OperationId";
    internal const string CleanupKey = "AppAutomation.Pointer.CleanupErrors";
    private static readonly AsyncLocal<CallbackContext?> Callback = new();
    private static readonly ConcurrentQueue<PointerTraceEntry> Trace = new();
    private static readonly object TraceFileLock = new();
    internal static string GetTraceSnapshot() => string.Join(Environment.NewLine, Trace.Select(entry => JsonSerializer.Serialize(entry))) + Environment.NewLine;
    internal static bool IsTerminalFailure(Exception error) => error.Data.Contains(OperationKey)
        || error is PointerInterferenceException or PointerCleanupException
        || error is AggregateException aggregate && aggregate.InnerExceptions.Any(IsTerminalFailure)
        || error.InnerException != null && IsTerminalFailure(error.InnerException);

    internal static void RejectNestedOperation()
    {
        if (Callback.Value is { } context)
            throw new InvalidOperationException(context.Closed
                ? "The hover callback has ended; its context cannot start another pointer operation."
                : "Nested pointer operations inside a hover callback are forbidden. Only reads and assertions are allowed.");
    }

    /// <remarks>Acquisition, preparation and target resolution run synchronously on the calling thread.
    /// Only primitive pointer actions and bounded waits continue asynchronously.</remarks>
    internal async Task RunAsync(string kind, TimeSpan timeout, Func<PointerOperation, Task> action,
        CancellationToken cancellationToken = default, PointerSession? session = null)
    {
        RejectNestedOperation();
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        ArgumentNullException.ThrowIfNull(action);
        var id = Guid.NewGuid();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session?.Token ?? default);
        deadline.CancelAfter(timeout);
        var operation = new PointerOperation(backend, id, kind, deadline.Token, timeout);
        IDisposable? ownership = null;
        Exception? primary = null;
        var registered = false;
        try
        {
            session?.Begin(id);
            registered = session != null;
            operation.Log("queued", detail: typeof(DesktopPointerController).Assembly.GetName().Version?.ToString());
            ownership = coordinator.Acquire(backend.DesktopKey, deadline.Token);
            if (ownership is IDesktopClickTimeline timeline)
            {
                var cooldown = timeline.RemainingCooldown(backend.DoubleClickTime);
                if (cooldown > TimeSpan.Zero && deadline.Token.WaitHandle.WaitOne(cooldown))
                    deadline.Token.ThrowIfCancellationRequested();
            }
            backend.EnsureDesktopAvailable();
            operation.WaitForNeutralInput();
            operation.SavePosition();
            await action(operation).ConfigureAwait(false);
            operation.Check();
        }
        catch (Exception error)
        {
            primary = error;
            error.Data[OperationKey] = id.ToString();
            operation.Log("failed", detail: ErrorCode(error));
        }
        finally
        {
            if (ownership != null)
            {
                try
                {
                    var cleanup = await operation.CleanupAsync().ConfigureAwait(false);
                    if (cleanup.Count != 0)
                    {
                        session?.Fault();
                        var cleanupFailure = new PointerCleanupException("Pointer cleanup failed; the cursor may not have been restored.", new AggregateException(cleanup));
                        cleanupFailure.Data[OperationKey] = id.ToString();
                        if (primary != null) primary.Data[CleanupKey] = cleanupFailure;
                        else primary = cleanupFailure;
                    }
                    if (ownership is IDesktopClickTimeline timeline && operation.LastButtonUptimeMilliseconds is { } buttonTime)
                        timeline.RecordButtonEvent(buttonTime);
                }
                catch (Exception cleanupError)
                {
                    session?.Fault();
                    cleanupError.Data[OperationKey] = id.ToString();
                    if (primary == null)
                    {
                        primary = new PointerCleanupException("Unexpected pointer cleanup failure.", cleanupError);
                        primary.Data[OperationKey] = id.ToString();
                    }
                    else primary.Data[CleanupKey] = cleanupError;
                }
                finally
                {
                    try { ownership.Dispose(); }
                    catch (Exception releaseError)
                    {
                        session?.Fault();
                        releaseError.Data[OperationKey] = id.ToString();
                        if (primary == null) primary = new PointerCleanupException("Desktop pointer ownership could not be released.", releaseError);
                        else primary.Data[CleanupKey] = releaseError;
                    }
                }
            }
            operation.Log("released");
            if (registered) session!.End(id);
        }
        if (primary != null)
        {
            var trace = string.Join(Environment.NewLine, operation.Entries.Select(entry => JsonSerializer.Serialize(entry))) + Environment.NewLine;
            primary.Data["AppAutomation.Pointer.Trace"] = trace;
            primary.Data["AppAutomation.Pointer.Artifacts"] = (IReadOnlyList<UiFailureArtifact>)[
                new UiFailureArtifact("pointer-trace", "pointer-trace", $"artifacts/ui-failures/flaui/{id}/pointer-trace.jsonl",
                    "application/x-ndjson", false, trace)
            ];
            ExceptionDispatchInfo.Capture(primary).Throw();
        }
    }

    private static string ErrorCode(Exception error) => $"{error.GetType().Name}; HResult=0x{error.HResult:X8}";

    internal sealed class PointerOperation(IPointerBackend backend, Guid id, string kind, CancellationToken token, TimeSpan timeout)
    {
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly List<PointerTraceEntry> _entries = [];
        private readonly HashSet<PointerButton> _potentiallyDown = [];
        private Point? _saved;
        private Point _expected;
        private PointerButtons _expectedButtons;
        private long? _lastButtonUptimeMilliseconds;
        private long _lastDownTimestamp;
        internal long? LastButtonUptimeMilliseconds => _lastButtonUptimeMilliseconds;
        internal IReadOnlyList<PointerTraceEntry> Entries => _entries;
        internal CancellationToken Token => token;
        internal TimeSpan Remaining => timeout - _elapsed.Elapsed;

        internal void Log(string stage, Point? point = null, string? detail = null)
        {
            var entry = new PointerTraceEntry(id, DateTimeOffset.UtcNow, Environment.ProcessId, kind, stage, point?.X, point?.Y, detail);
            _entries.Add(entry);
            Trace.Enqueue(entry);
            while (Trace.Count > 1024) Trace.TryDequeue(out _);
            var path = Environment.GetEnvironmentVariable("APPAUTOMATION_POINTER_TRACE_FILE");
            if (!string.IsNullOrWhiteSpace(path))
            {
                try { lock (TraceFileLock) File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine); }
                catch (Exception) { /* Diagnostics must never prevent physical cleanup. */ }
            }
        }

        internal void WaitForNeutralInput()
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                backend.EnsureDesktopAvailable();
                var state = backend.ReadState();
                if (state.Buttons == PointerButtons.None && state.Modifiers == 0) return;
                if (token.WaitHandle.WaitOne(10)) token.ThrowIfCancellationRequested();
            }
        }

        internal void SavePosition()
        {
            var state = backend.ReadState();
            if (state.Buttons != PointerButtons.None || state.Modifiers != 0)
                throw new PointerInterferenceException("Input changed before saving the cursor position.");
            _saved = _expected = state.Position;
            Log("saved", state.Position);
        }

        internal void Check()
        {
            token.ThrowIfCancellationRequested();
            backend.EnsureDesktopAvailable();
            var state = backend.ReadState();
            if (state.Position != _expected || state.Buttons != _expectedButtons || state.Modifiers != 0)
                throw new PointerInterferenceException($"Unexpected pointer input: expected {_expected}, {_expectedButtons}; actual {state.Position}, {state.Buttons}, guardedKeys={state.Modifiers} (modifiers or Escape).");
        }

        internal void Move(Point point, bool deliverMotion = false)
        {
            Check();
            if (!backend.IsOnMonitor(point)) throw new InvalidOperationException($"Pointer target {point} is outside every physical monitor.");
            Log("move-requested", point);
            if (point == _expected) return;
            var before = _expected;
            if (deliverMotion) backend.MoveWithInput(point);
            else backend.SetPosition(point);
            var actual = backend.ReadState();
            // A rejected/no-op SetPhysicalCursorPos may be retried once. A different actual position
            // can be hardware interference and is never chased.
            if (actual.Position == before && actual.Buttons == _expectedButtons && actual.Modifiers == 0)
            {
                if (deliverMotion) backend.MoveWithInput(point);
                else backend.SetPosition(point);
                actual = backend.ReadState();
            }
            if (actual.Position != point || actual.Buttons != _expectedButtons || actual.Modifiers != 0)
                throw new PointerInterferenceException($"Pointer move did not match the request {point}; actual {actual.Position}, {actual.Buttons}.");
            _expected = point;
            Log("moved", actual.Position, deliverMotion ? "input-motion" : null);
        }

        internal void Down(PointerButton button, long? doubleClickStart = null)
        {
            Check();
            if (doubleClickStart is { } firstDown && Stopwatch.GetElapsedTime(firstDown) >= backend.DoubleClickTime)
                throw new TimeoutException("The second click exceeded the system double-click interval; no second single click was injected.");
            _potentiallyDown.Add(button); // Down may be delivered even if the provider throws.
            _expectedButtons |= WindowsPointerBackend.Mask(button);
            _lastButtonUptimeMilliseconds = Environment.TickCount64;
            _lastDownTimestamp = Stopwatch.GetTimestamp();
            backend.Down(button);
            Check();
            Log("down", _expected, button.ToString());
        }

        internal void Up(PointerButton button)
        {
            Check();
            backend.Up(button);
            _expectedButtons &= ~WindowsPointerBackend.Mask(button);
            Check();
            _potentiallyDown.Remove(button);
            _lastButtonUptimeMilliseconds = Environment.TickCount64;
            Log("up", _expected, button.ToString());
        }

        internal async Task ClickAsync(Point point, PointerClickOptions options, Action? validateOwner = null)
        {
            Move(point);
            long? firstDown = null;
            for (var index = 0; index < options.ClickCount; index++)
            {
                if (index != 0) await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(60, backend.DoubleClickTime.TotalMilliseconds / 3)), token).ConfigureAwait(false);
                validateOwner?.Invoke();
                Down(options.Button, firstDown);
                firstDown ??= _lastDownTimestamp;
                Up(options.Button);
                _lastButtonUptimeMilliseconds = Environment.TickCount64;
            }
        }

        internal async Task HoverAsync(Point point, Func<CancellationToken, Task> verify, Action? validateOwner = null,
            Action? validateExact = null)
        {
            Move(point);
            validateExact?.Invoke();
            validateOwner?.Invoke();
            var previous = Callback.Value;
            var context = new CallbackContext();
            // CancelAsync sets token state immediately but cannot block the deadline/cleanup thread
            // on arbitrary cancellation registrations. Its task is observed separately.
            var callbackCancellation = new CancellationTokenSource();
            Task? callbackCancellationTask = null;
            void RequestCallbackCancellation() => Interlocked.CompareExchange(ref callbackCancellationTask, callbackCancellation.CancelAsync(), null);
            var cancellationRegistration = token.Register(RequestCallbackCancellation);
            Task? verification = null;
            Exception? callbackError = null;
            try
            {
                Callback.Value = context;
                verification = verify(callbackCancellation.Token) ?? throw new InvalidOperationException("The hover callback returned null.");
                // Stop flowing the callback guard into controller continuations, but inherited callback
                // continuations retain this shared context and see Closed after a timeout.
                Callback.Value = previous;
                while (!verification.IsCompleted)
                {
                    Check();
                    validateOwner?.Invoke();
                    await Task.WhenAny(verification, Task.Delay(16, token)).ConfigureAwait(false);
                }
                await verification.ConfigureAwait(false);
                Check();
            }
            catch (Exception error) { callbackError = error; throw; }
            finally
            {
                context.Closed = true;
                Callback.Value = previous;
                cancellationRegistration.Dispose();
                RequestCallbackCancellation();
                _ = callbackCancellationTask!.ContinueWith(cancelled =>
                {
                    if (cancelled.IsFaulted)
                    {
                        var failure = cancelled.Exception;
                        if (callbackError != null) callbackError.Data["AppAutomation.Pointer.CallbackCancellationError"] = failure;
                    }
                    // Dispose only after CancelAsync has dispatched its registrations. Disposing sooner
                    // can unregister pending callbacks; waiting for them here would break bounded cleanup.
                    callbackCancellation.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                if (verification != null)
                    _ = verification.ContinueWith(completed => _ = completed.Exception, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        internal async Task DragAsync(Point start, Point end, PointerDragOptions options, Action<Point>? validateOwner = null,
            bool requireDragThreshold = true, Action? validateSource = null, Action? validateDestination = null)
        {
            if (options.Duration > Remaining) throw new ArgumentOutOfRangeException(nameof(options), "Drag duration exceeds the remaining operation deadline.");
            var threshold = backend.DragThreshold;
            if (requireDragThreshold && Math.Abs((long)end.X - start.X) <= threshold.Width && Math.Abs((long)end.Y - start.Y) <= threshold.Height)
                throw new ArgumentException("The drag path must cross the system drag threshold.", nameof(end));
            Move(start);
            validateOwner?.Invoke(start);
            validateSource?.Invoke();
            Down(options.Button);
            var steps = Math.Max(1, (int)Math.Floor(options.Duration.TotalMilliseconds / 16));
            var interval = options.Duration / steps;
            for (var step = 1; step <= steps; step++)
            {
                await Task.Delay(interval, token).ConfigureAwait(false);
                var point = step == steps ? end : new Point(
                    (int)Math.Round(start.X + ((long)end.X - start.X) * (double)step / steps),
                    (int)Math.Round(start.Y + ((long)end.Y - start.Y) * (double)step / steps));
                validateOwner?.Invoke(point);
                Move(point, deliverMotion: true);
            }
            // Give the destination event loop a separate dispatch opportunity before dropping.
            await Task.Delay(16, token).ConfigureAwait(false);
            validateOwner?.Invoke(end);
            validateDestination?.Invoke();
            Up(options.Button);
        }

        internal void Scroll(Point point, double amount, bool horizontal, Action? validateOwner = null)
        {
            Move(point);
            validateOwner?.Invoke();
            Check();
            backend.Wheel(amount, horizontal);
            Check();
            Log("wheel", point, $"amount={amount}; horizontal={horizontal}");
        }

        internal async Task<List<Exception>> CleanupAsync()
        {
            var errors = new List<Exception>();
            if (_saved == null) return errors;
            var budget = Stopwatch.StartNew();
            foreach (var button in _potentiallyDown.ToArray())
            {
                try
                {
                    backend.EnsureDesktopAvailable();
                    backend.Up(button);
                    if ((backend.ReadState().Buttons & WindowsPointerBackend.Mask(button)) != 0)
                        throw new PointerCleanupException($"The owned {button} button remains pressed after Up.");
                    _potentiallyDown.Remove(button);
                    _lastButtonUptimeMilliseconds = Environment.TickCount64;
                    Log("cleanup-up", detail: button.ToString());
                }
                catch (Exception error) { errors.Add(error); Log("cleanup-up-failed", detail: ErrorCode(error)); }
            }
            try
            {
                // Never move a cursor with any button held, including foreign buttons pressed without
                // moving during hover. Never release a button this operation did not inject.
                while (true)
                {
                    backend.EnsureDesktopAvailable();
                    var state = backend.ReadState();
                    if (state.Buttons == PointerButtons.None && _potentiallyDown.Count == 0) break;
                    if (budget.Elapsed >= TimeSpan.FromSeconds(1))
                        throw new PointerCleanupException("Cursor restoration skipped because AllUp could not be confirmed within the cleanup budget.");
                    await Task.Delay(10).ConfigureAwait(false);
                }
                if (!backend.IsOnMonitor(_saved.Value)) throw new PointerCleanupException("The monitor containing the saved cursor position is no longer available.");
                Log("restore-requested", _saved);
                var stateBeforeRestore = backend.ReadState();
                if (stateBeforeRestore.Buttons != PointerButtons.None) throw new PointerCleanupException("A button became pressed immediately before cursor restoration.");
                backend.SetPosition(_saved.Value);
                var restored = backend.ReadState();
                Log("restored", restored.Position, restored.Buttons.ToString());
                if (restored.Position != _saved.Value || restored.Buttons != PointerButtons.None)
                    throw new PointerCleanupException($"Cursor restoration could not be confirmed at {_saved.Value}; actual {restored.Position}, {restored.Buttons}.");
            }
            catch (Exception error) { errors.Add(error); Log("restore-failed", detail: ErrorCode(error)); }
            return errors;
        }
    }

    private sealed class CallbackContext { internal volatile bool Closed; }
}
