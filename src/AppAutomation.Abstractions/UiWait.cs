using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppAutomation.Abstractions;

public static class UiWait
{
    public static UiWaitResult<T> TryUntil<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        UiWaitOptions? options = null,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        ArgumentNullException.ThrowIfNull(condition);

        var log = logger ?? NullLogger.Instance;
        var waitOptions = ValidateOptions(options);
        var stopwatch = Stopwatch.StartNew();
        var retryCount = 0;

        log.LogInformation("Wait started with timeout {TimeoutMs}ms, poll interval {PollIntervalMs}ms",
            (int)waitOptions.Timeout.TotalMilliseconds, (int)waitOptions.PollInterval.TotalMilliseconds);

        var lastValue = valueFactory();

        while (stopwatch.Elapsed < waitOptions.Timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (condition(lastValue))
            {
                log.LogInformation("Wait completed successfully after {ElapsedMs}ms and {RetryCount} retries",
                    (int)stopwatch.Elapsed.TotalMilliseconds, retryCount);
                return new UiWaitResult<T>(true, lastValue, stopwatch.Elapsed);
            }

            var remainingMs = (int)(waitOptions.Timeout - stopwatch.Elapsed).TotalMilliseconds;
            if (remainingMs < waitOptions.PollInterval.TotalMilliseconds * 2)
            {
                log.LogWarning("Timeout approaching: {RemainingMs}ms remaining after {RetryCount} retries",
                    remainingMs, retryCount);
            }

            log.LogDebug("Condition not met, retry {RetryCount} after {ElapsedMs}ms",
                retryCount, (int)stopwatch.Elapsed.TotalMilliseconds);

            Thread.Sleep(waitOptions.PollInterval);
            lastValue = valueFactory();
            retryCount++;
        }

        var success = condition(lastValue);
        if (success)
        {
            log.LogInformation("Wait completed successfully after {ElapsedMs}ms and {RetryCount} retries",
                (int)stopwatch.Elapsed.TotalMilliseconds, retryCount);
        }
        else
        {
            log.LogWarning("Wait timed out after {ElapsedMs}ms and {RetryCount} retries",
                (int)stopwatch.Elapsed.TotalMilliseconds, retryCount);
        }

        return new UiWaitResult<T>(success, lastValue, stopwatch.Elapsed);
    }

    public static T Until<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        UiWaitOptions? options = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;
        var result = TryUntil(valueFactory, condition, options, cancellationToken, logger);
        if (result.Success)
        {
            return result.Value;
        }

        var message = timeoutMessage ?? $"Condition was not met within {ValidateOptions(options).Timeout.TotalMilliseconds} ms.";
        log.LogError("Wait operation failed: {TimeoutMessage}", message);
        throw new TimeoutException(message);
    }

    public static async Task<UiWaitResult<T>> TryUntilAsync<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        UiWaitOptions? options = null,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        ArgumentNullException.ThrowIfNull(condition);

        var log = logger ?? NullLogger.Instance;
        var waitOptions = ValidateOptions(options);
        var stopwatch = Stopwatch.StartNew();
        var retryCount = 0;

        log.LogInformation("Async wait started with timeout {TimeoutMs}ms, poll interval {PollIntervalMs}ms",
            (int)waitOptions.Timeout.TotalMilliseconds, (int)waitOptions.PollInterval.TotalMilliseconds);

        var lastValue = valueFactory();

        while (stopwatch.Elapsed < waitOptions.Timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (condition(lastValue))
            {
                log.LogInformation("Async wait completed successfully after {ElapsedMs}ms and {RetryCount} retries",
                    (int)stopwatch.Elapsed.TotalMilliseconds, retryCount);
                return new UiWaitResult<T>(true, lastValue, stopwatch.Elapsed);
            }

            var remainingMs = (int)(waitOptions.Timeout - stopwatch.Elapsed).TotalMilliseconds;
            if (remainingMs < waitOptions.PollInterval.TotalMilliseconds * 2)
            {
                log.LogWarning("Timeout approaching: {RemainingMs}ms remaining after {RetryCount} retries",
                    remainingMs, retryCount);
            }

            log.LogDebug("Condition not met, retry {RetryCount} after {ElapsedMs}ms",
                retryCount, (int)stopwatch.Elapsed.TotalMilliseconds);

            await Task.Delay(waitOptions.PollInterval, waitOptions.TimeProvider, cancellationToken);
            lastValue = valueFactory();
            retryCount++;
        }

        var success = condition(lastValue);
        if (success)
        {
            log.LogInformation("Async wait completed successfully after {ElapsedMs}ms and {RetryCount} retries",
                (int)stopwatch.Elapsed.TotalMilliseconds, retryCount);
        }
        else
        {
            log.LogWarning("Async wait timed out after {ElapsedMs}ms and {RetryCount} retries",
                (int)stopwatch.Elapsed.TotalMilliseconds, retryCount);
        }

        return new UiWaitResult<T>(success, lastValue, stopwatch.Elapsed);
    }

    public static async Task<T> UntilAsync<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        UiWaitOptions? options = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;
        var result = await TryUntilAsync(valueFactory, condition, options, cancellationToken, logger);
        if (result.Success)
        {
            return result.Value;
        }

        var message = timeoutMessage ?? $"Condition was not met within {ValidateOptions(options).Timeout.TotalMilliseconds} ms.";
        log.LogError("Async wait operation failed: {TimeoutMessage}", message);
        throw new TimeoutException(message);
    }

    private static UiWaitOptions ValidateOptions(UiWaitOptions? options)
    {
        var waitOptions = options ?? UiWaitOptions.Default;
        if (waitOptions.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be greater than zero.");
        }

        if (waitOptions.PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "PollInterval must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(waitOptions.TimeProvider);
        return waitOptions;
    }
}
