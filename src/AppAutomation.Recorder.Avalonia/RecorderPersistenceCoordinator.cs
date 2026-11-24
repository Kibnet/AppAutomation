namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderPersistenceCoordinator : IDisposable
{
    private const string DisposedMessage = "Recorder is disposed.";
    private readonly object _sync = new();
    private readonly Func<RecorderPersistenceSnapshot> _capture;
    private readonly Func<long> _currentRevision;
    private readonly Func<bool> _canAutosave;
    private readonly Func<CancellationToken, Task<RecorderSaveResult>> _autosave;
    private readonly Action<string, RecorderValidationStatus?> _status;
    private readonly Action _changed;
    private Operation? _active;
    private Operation? _queued;
    private long? _pendingRevision;
    private long? _savedRevision;
    private bool _suspended;
    private bool _disposed;
    private bool _isCapturingSnapshot;

    internal RecorderPersistenceCoordinator(
        Func<RecorderPersistenceSnapshot> capture,
        Func<long> currentRevision,
        Func<bool> canAutosave,
        Func<CancellationToken, Task<RecorderSaveResult>> autosave,
        Action<string, RecorderValidationStatus?> status,
        Action changed)
    {
        _capture = capture;
        _currentRevision = currentRevision;
        _canAutosave = canAutosave;
        _autosave = autosave;
        _status = status;
        _changed = changed;
    }

    internal bool IsBusy => _active is not null;
    // Clipboard reservation and persistence admission remain one transaction, as before extraction.
    internal object SyncRoot => _sync;
    internal string BusyDescription => _active?.BusyDescription ?? string.Empty;

    internal RecorderPersistenceSnapshot CaptureSnapshot()
    {
        _isCapturingSnapshot = true;
        try { return _capture(); }
        finally { _isCapturingSnapshot = false; }
    }

    internal void MarkSaved(long revision)
    {
        lock (_sync)
        {
            _savedRevision = revision;
            if (_pendingRevision <= revision) _pendingRevision = null;
        }
    }

    internal void SuspendAutosave()
    {
        lock (_sync) _suspended = true;
    }

    internal void ResumeAutosave()
    {
        lock (_sync)
        {
            _suspended = false;
            StartPendingAutosave();
        }
    }

    internal Task<RecorderSaveResult> RunAsync(string name,
        Func<CancellationToken, Task<RecorderSaveResult>> operation, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_disposed) return Task.FromResult(RecorderSaveResult.Failed(DisposedMessage));
            if (_active is { } active)
            {
                if (active.IsAutosave && _queued is null)
                {
                    var queued = new Operation(name, operation, cancellationToken);
                    _queued = queued;
                    // A queued Save has not yet captured or saved the newer revision.
                    _status($"{name} queued until autosave completes.", null);
                    return queued.Completion.Task;
                }
                _status($"{name} ignored while '{active.BusyDescription}' is in progress.", RecorderValidationStatus.Warning);
                return Task.FromResult(RecorderSaveResult.Failed($"{active.BusyDescription} is already in progress."));
            }
            return Start(new Operation(name, operation, cancellationToken));
        }
    }

    internal void RequestAutosave()
    {
        lock (_sync)
        {
            if (_disposed || _isCapturingSnapshot || !_canAutosave()) return;
            var revision = _currentRevision();
            if (_savedRevision == revision) return;
            if (_suspended || _active is not null)
            {
                _pendingRevision = revision;
                return;
            }
            StartAutosave();
        }
    }

    private void StartAutosave()
    {
        _pendingRevision = null;
        Start(new Operation("Autosave", _autosave, CancellationToken.None, IsAutosave: true));
    }

    private void StartPendingAutosave()
    {
        if (_disposed || _suspended || _active is not null || _pendingRevision is null) return;

        if (_canAutosave()) StartAutosave();
        else _pendingRevision = null;
    }

    private Task<RecorderSaveResult> Start(Operation operation)
    {
        _active = operation;
        _ = ExecuteAsync(operation);
        return operation.Completion.Task;
    }

    private async Task ExecuteAsync(Operation operation)
    {
        try
        {
            _status($"{operation.Name} in progress...", null);
            operation.CancellationToken.ThrowIfCancellationRequested();
            operation.Completion.TrySetResult(_disposed
                ? RecorderSaveResult.Failed(DisposedMessage)
                : await operation.Execute(operation.CancellationToken));
        }
        catch (OperationCanceledException)
        {
            var message = $"{operation.Name} cancelled.";
            operation.Completion.TrySetResult(RecorderSaveResult.Failed(message));
            _status(message, null);
        }
        catch (Exception error)
        {
            var message = $"{operation.Name} failed: {error.Message}";
            operation.Completion.TrySetResult(RecorderSaveResult.Failed(message, error.ToString()));
            _status(message, RecorderValidationStatus.Invalid);
        }
        finally
        {
            lock (_sync)
            {
                _active = null;
                if (_queued is { } queued)
                {
                    _queued = null;
                    Start(queued);
                }
                else
                {
                    StartPendingAutosave();
                }
            }
            _changed();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _pendingRevision = null;
            var queued = _queued;
            _queued = null;
            queued?.Completion.TrySetResult(RecorderSaveResult.Failed(DisposedMessage));
        }
    }

    private sealed record Operation(string Name,
        Func<CancellationToken, Task<RecorderSaveResult>> Execute, CancellationToken CancellationToken,
        bool IsAutosave = false)
    {
        public string BusyDescription { get; } = $"{Name}...";
        public TaskCompletionSource<RecorderSaveResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
