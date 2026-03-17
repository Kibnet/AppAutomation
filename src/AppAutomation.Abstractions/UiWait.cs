using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppAutomation.Abstractions;

/// <summary>
/// Provides polling-based wait utilities for UI automation scenarios.
/// </summary>
/// <remarks>
/// <para>
/// This class provides synchronous and asynchronous methods for waiting until a condition is met,
/// with configurable timeout and poll intervals. It is commonly used to wait for UI controls
/// to reach a specific state (e.g., enabled, visible, containing specific text).
/// </para>
/// <para>
/// For UI test scenarios, prefer the fluent extension methods in <see cref="UiPageExtensions"/>
/// which provide better diagnostics and automatic retry behavior.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Wait for a condition with default options
/// var value = UiWait.Until(
///     () => textBox.Text,
///     text => text.Contains("Hello"),
///     timeoutMessage: "Text did not contain 'Hello'");
/// 
/// // Non-throwing version
/// var result = UiWait.TryUntil(
///     () => button.IsEnabled,
///     enabled => enabled);
/// if (!result.Success)
/// {
///     Console.WriteLine($"Timed out after {result.Elapsed}");
/// }
/// </code>
/// </example>
public static class UiWait
{
    /// <summary>
    /// Polls a value factory until a condition is met, without throwing on timeout.
    /// </summary>
    /// <typeparam name="T">The type of value being polled.</typeparam>
    /// <param name="valueFactory">A function that produces the current value to test.</param>
    /// <param name="condition">A predicate that determines if the desired state has been reached.</param>
    /// <param name="options">Wait options including timeout and poll interval. Uses <see cref="UiWaitOptions.Default"/> if <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to cancel the wait operation.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <returns>A <see cref="UiWaitResult{T}"/> indicating success/failure, the last observed value, and elapsed time.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="valueFactory"/> or <paramref name="condition"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
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

    /// <summary>
    /// Polls a value factory until a condition is met, throwing <see cref="TimeoutException"/> on timeout.
    /// </summary>
    /// <typeparam name="T">The type of value being polled.</typeparam>
    /// <param name="valueFactory">A function that produces the current value to test.</param>
    /// <param name="condition">A predicate that determines if the desired state has been reached.</param>
    /// <param name="options">Wait options including timeout and poll interval. Uses <see cref="UiWaitOptions.Default"/> if <see langword="null"/>.</param>
    /// <param name="timeoutMessage">Custom message for the <see cref="TimeoutException"/>. If <see langword="null"/>, a default message is used.</param>
    /// <param name="cancellationToken">Token to cancel the wait operation.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <returns>The value that satisfied the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="valueFactory"/> or <paramref name="condition"/> is <see langword="null"/>.</exception>
    /// <exception cref="TimeoutException">Thrown when the condition is not met within the timeout period.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
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

    /// <summary>
    /// Asynchronously polls a value factory until a condition is met, without throwing on timeout.
    /// </summary>
    /// <typeparam name="T">The type of value being polled.</typeparam>
    /// <param name="valueFactory">A function that produces the current value to test.</param>
    /// <param name="condition">A predicate that determines if the desired state has been reached.</param>
    /// <param name="options">Wait options including timeout and poll interval. Uses <see cref="UiWaitOptions.Default"/> if <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to cancel the wait operation.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <returns>A task that resolves to a <see cref="UiWaitResult{T}"/> indicating success/failure, the last observed value, and elapsed time.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="valueFactory"/> or <paramref name="condition"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
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

    /// <summary>
    /// Asynchronously polls a value factory until a condition is met, throwing <see cref="TimeoutException"/> on timeout.
    /// </summary>
    /// <typeparam name="T">The type of value being polled.</typeparam>
    /// <param name="valueFactory">A function that produces the current value to test.</param>
    /// <param name="condition">A predicate that determines if the desired state has been reached.</param>
    /// <param name="options">Wait options including timeout and poll interval. Uses <see cref="UiWaitOptions.Default"/> if <see langword="null"/>.</param>
    /// <param name="timeoutMessage">Custom message for the <see cref="TimeoutException"/>. If <see langword="null"/>, a default message is used.</param>
    /// <param name="cancellationToken">Token to cancel the wait operation.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <returns>A task that resolves to the value that satisfied the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="valueFactory"/> or <paramref name="condition"/> is <see langword="null"/>.</exception>
    /// <exception cref="TimeoutException">Thrown when the condition is not met within the timeout period.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
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
