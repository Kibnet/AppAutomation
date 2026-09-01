using System.Globalization;

namespace AppAutomation.Abstractions;

/// <summary>
/// Creates a new series of short generated values for one test invocation.
/// </summary>
public static class RecordedValueGenerator
{
    private static readonly object Sync = new();
    private static long _lastTimestampMilliseconds;

    /// <summary>
    /// Starts a value series with a process-local monotonic UTC timestamp.
    /// </summary>
    public static RecordedValueSeries Start()
    {
        long timestampMilliseconds;
        lock (Sync)
        {
            var currentMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            timestampMilliseconds = Math.Max(currentMilliseconds, _lastTimestampMilliseconds + 1);
            _lastTimestampMilliseconds = timestampMilliseconds;
        }

        return new RecordedValueSeries(DateTimeOffset.FromUnixTimeMilliseconds(timestampMilliseconds));
    }
}

/// <summary>
/// Formats generated values that belong to one test invocation.
/// </summary>
public sealed class RecordedValueSeries
{
    private readonly string _timestamp;

    internal RecordedValueSeries(DateTimeOffset timestamp)
    {
        _timestamp = timestamp.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Creates the value for a positive one-based ordinal in this series.
    /// </summary>
    public string Create(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, 1);
        return $"Recorded_{_timestamp}_{ordinal.ToString(CultureInfo.InvariantCulture)}";
    }
}
