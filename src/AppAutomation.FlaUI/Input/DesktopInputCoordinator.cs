using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace AppAutomation.FlaUI.Input;

internal interface IDesktopInputCoordinator
{
    IDisposable Acquire(string desktopKey, CancellationToken cancellationToken);
}

internal interface IDesktopClickTimeline
{
    TimeSpan RemainingCooldown(TimeSpan doubleClickTime);
    void RecordButtonEvent(long uptimeMilliseconds);
}

/// <summary>The native mutex is acquired and released exclusively by its dedicated owner thread.</summary>
internal sealed class DesktopInputCoordinator : IDesktopInputCoordinator
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalQueues = new(StringComparer.Ordinal);

    internal static TimeSpan ComputeCooldown(long currentUptimeMilliseconds, long lastUptimeMilliseconds, TimeSpan interval)
    {
        // TickCount64 is shared by processes on this OS. Reboots, corrupt state and future timestamps
        // are conservatively treated as a recent click; wall-clock changes cannot shorten this wait.
        if (lastUptimeMilliseconds < 0 || currentUptimeMilliseconds < lastUptimeMilliseconds) return interval;
        var elapsedMilliseconds = currentUptimeMilliseconds - lastUptimeMilliseconds;
        return elapsedMilliseconds < interval.TotalMilliseconds
            ? interval - TimeSpan.FromMilliseconds(elapsedMilliseconds) : TimeSpan.Zero;
    }

    public IDisposable Acquire(string desktopKey, CancellationToken cancellationToken)
    {
        var queue = LocalQueues.GetOrAdd(desktopKey, _ => new SemaphoreSlim(1, 1));
        queue.Wait(cancellationToken);
        var lease = new MutexLease(desktopKey, queue, cancellationToken);
        try { lease.WaitUntilAcquired(cancellationToken); return lease; }
        catch { lease.Dispose(); throw; }
    }

    private sealed class MutexLease : IDisposable, IDesktopClickTimeline
    {
        private readonly SemaphoreSlim _queue;
        private readonly ManualResetEventSlim _release = new(false);
        private readonly TaskCompletionSource _acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _stop;
        private readonly string _timelinePath;
        private bool _abandoned;
        private int _disposed;

        internal MutexLease(string key, SemaphoreSlim queue, CancellationToken token)
        {
            _queue = queue;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            var name = "Local\\AppAutomation.Pointer." + hash;
            _timelinePath = Path.Combine(Path.GetTempPath(), "AppAutomation.Pointer", hash + ".uptime-ms");
            var thread = new Thread(() => Own(name)) { IsBackground = true, Name = "AppAutomation desktop pointer mutex" };
            thread.Start();
        }

        internal void WaitUntilAcquired(CancellationToken token) => _acquired.Task.WaitAsync(token).GetAwaiter().GetResult();

        private void Own(string name)
        {
            var owned = false;
            try
            {
                using var mutex = new Mutex(false, name);
                try
                {
                    try { owned = WaitHandle.WaitAny([mutex, _stop.Token.WaitHandle]) == 0; }
                    catch (AbandonedMutexException) { owned = true; _abandoned = true; }
                    if (!owned) throw new OperationCanceledException(_stop.Token);
                    _acquired.TrySetResult();
                    _release.Wait();
                }
                finally { if (owned) mutex.ReleaseMutex(); }
            }
            catch (Exception error)
            {
                if (!_acquired.TrySetException(error)) _finished.TrySetException(error);
            }
            finally { _finished.TrySetResult(); }
        }

        public TimeSpan RemainingCooldown(TimeSpan doubleClickTime)
        {
            // The file contains only the last injected button's OS uptime in milliseconds. Access is protected by the
            // desktop mutex, so it also survives sequential short-lived test processes.
            if (_abandoned) { _abandoned = false; return doubleClickTime; }
            if (!File.Exists(_timelinePath)) return TimeSpan.Zero;
            var content = File.ReadAllText(_timelinePath);
            if (!long.TryParse(content, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var milliseconds))
                return doubleClickTime;
            return ComputeCooldown(Environment.TickCount64, milliseconds, doubleClickTime);
        }

        public void RecordButtonEvent(long uptimeMilliseconds)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_timelinePath)!);
            File.WriteAllText(_timelinePath, uptimeMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Cancel();
            _release.Set();
            // The owner performs no UIA calls and cannot wait on user code.
            try { _finished.Task.GetAwaiter().GetResult(); }
            finally
            {
                _stop.Dispose();
                _release.Dispose();
                _queue.Release();
            }
        }
    }
}
