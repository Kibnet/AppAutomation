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
/// <para>
/// A callback already in progress cannot be interrupted by the timeout. Its result is rejected if
/// it completes after the deadline; no further callback is started after timeout or cancellation.
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
        var wait = new PollingWait<T>(valueFactory, condition, options, cancellationToken, logger, "Wait");
        while (wait.TryPoll(out var delay))
        {
            Thread.Sleep(delay);
        }

        return wait.GetResult();
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
        => GetValueOrThrow(TryUntil(valueFactory, condition, options, cancellationToken, logger),
            options, timeoutMessage, logger, "Wait");

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
        var wait = new PollingWait<T>(valueFactory, condition, options, cancellationToken, logger, "Async wait");
        while (wait.TryPoll(out var delay))
        {
            await Task.Delay(delay, wait.TimeProvider, cancellationToken);
        }

        return wait.GetResult();
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
        => GetValueOrThrow(await TryUntilAsync(valueFactory, condition, options, cancellationToken, logger),
            options, timeoutMessage, logger, "Async wait");

    private static T GetValueOrThrow<T>(UiWaitResult<T> result, UiWaitOptions? options,
        string? timeoutMessage, ILogger? logger, string waitKind)
    {
        if (result.Success) return result.Value;
        var timeout = (options ?? UiWaitOptions.Default).Timeout;
        var message = timeoutMessage ?? $"Condition was not met within {timeout.TotalMilliseconds} ms.";
        (logger ?? NullLogger.Instance).LogError("{WaitKind} operation failed: {TimeoutMessage}", waitKind, message);
        throw new TimeoutException(message);
    }

    private struct PollingWait<T>
    {
        private readonly Func<T> _valueFactory;
        private readonly Predicate<T> _condition;
        private readonly UiWaitOptions _options;
        private readonly CancellationToken _cancellationToken;
        private readonly ILogger _log;
        private readonly string _kind;
        private readonly long _startedAt;
        private T _lastValue = default!;
        private int _retryCount;
        private bool _success;

        public PollingWait(Func<T> valueFactory, Predicate<T> condition, UiWaitOptions? options,
            CancellationToken cancellationToken, ILogger? logger, string kind)
        {
            ArgumentNullException.ThrowIfNull(valueFactory);
            ArgumentNullException.ThrowIfNull(condition);
            _valueFactory = valueFactory;
            _condition = condition;
            _options = ValidateOptions(options);
            _cancellationToken = cancellationToken;
            _log = logger ?? NullLogger.Instance;
            _kind = kind;
            _startedAt = Stopwatch.GetTimestamp();
            _log.LogInformation("{WaitKind} started with timeout {TimeoutMs}ms, poll interval {PollIntervalMs}ms",
                _kind, (int)_options.Timeout.TotalMilliseconds, (int)_options.PollInterval.TotalMilliseconds);
        }

        public TimeProvider TimeProvider => _options.TimeProvider;
        private TimeSpan Elapsed => Stopwatch.GetElapsedTime(_startedAt);

        public bool TryPoll(out TimeSpan delay)
        {
            delay = default;
            if (!CanContinue()) return false;

            _lastValue = _valueFactory();
            if (!CanContinue()) return false;

            var matches = _condition(_lastValue);
            if (!CanContinue()) return false;
            if (matches)
            {
                _success = true;
                return false;
            }

            var remaining = _options.Timeout - Elapsed;
            if (remaining <= TimeSpan.Zero) return false;
            if (remaining.TotalMilliseconds < _options.PollInterval.TotalMilliseconds * 2)
            {
                _log.LogWarning("Timeout approaching: {RemainingMs}ms remaining after {RetryCount} retries",
                    (int)remaining.TotalMilliseconds, _retryCount);
            }

            _log.LogDebug("Condition not met, retry {RetryCount} after {ElapsedMs}ms", _retryCount, (int)Elapsed.TotalMilliseconds);
            _cancellationToken.ThrowIfCancellationRequested();
            remaining = _options.Timeout - Elapsed;
            if (remaining <= TimeSpan.Zero) return false;
            delay = remaining < _options.PollInterval ? remaining : _options.PollInterval;
            _retryCount++;
            return true;
        }

        public UiWaitResult<T> GetResult()
        {
            var elapsedMs = (int)Elapsed.TotalMilliseconds;
            if (_success)
            {
                _log.LogInformation("{WaitKind} completed successfully after {ElapsedMs}ms and {RetryCount} retries",
                    _kind, elapsedMs, _retryCount);
            }
            else
            {
                _log.LogWarning("{WaitKind} timed out after {ElapsedMs}ms and {RetryCount} retries",
                    _kind, elapsedMs, _retryCount);
            }
            _cancellationToken.ThrowIfCancellationRequested();
            return new UiWaitResult<T>(_success, _lastValue, Elapsed);
        }

        private bool CanContinue()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return Elapsed < _options.Timeout;
        }
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
