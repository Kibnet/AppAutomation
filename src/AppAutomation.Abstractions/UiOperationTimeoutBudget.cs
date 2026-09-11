using System.Diagnostics;

namespace AppAutomation.Abstractions;

internal sealed class UiOperationTimeoutBudget
{
    private readonly TimeSpan _timeout;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly string _operation;

    private UiOperationTimeoutBudget(int timeoutMs, string operation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        _timeout = TimeSpan.FromMilliseconds(timeoutMs);
        _operation = operation;
    }

    public TimeSpan Remaining
    {
        get
        {
            var remaining = _timeout - _stopwatch.Elapsed;
            return remaining > TimeSpan.Zero
                ? remaining
                : throw new TimeoutException($"The {_operation} operation exceeded its timeout.");
        }
    }

    public int RemainingMilliseconds => Math.Max(1, (int)Math.Ceiling(Remaining.TotalMilliseconds));

    public static UiOperationTimeoutBudget Start(int timeoutMs, string operation) => new(timeoutMs, operation);
}
