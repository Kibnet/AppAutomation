using System.Diagnostics;

namespace FlaUI.EasyUse.Waiting;

public static class UiWait
{
    public static UiWaitResult<T> TryUntil<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        UiWaitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        ArgumentNullException.ThrowIfNull(condition);

        var waitOptions = ValidateOptions(options);
        var stopwatch = Stopwatch.StartNew();
        var lastValue = valueFactory();

        while (stopwatch.Elapsed < waitOptions.Timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (condition(lastValue))
            {
                return new UiWaitResult<T>(true, lastValue, stopwatch.Elapsed);
            }

            Thread.Sleep(waitOptions.PollInterval);
            lastValue = valueFactory();
        }

        var success = condition(lastValue);
        return new UiWaitResult<T>(success, lastValue, stopwatch.Elapsed);
    }

    public static T Until<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        UiWaitOptions? options = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        var result = TryUntil(valueFactory, condition, options, cancellationToken);
        if (result.Success)
        {
            return result.Value;
        }

        throw new TimeoutException(timeoutMessage ?? $"Condition was not met within {ValidateOptions(options).Timeout.TotalMilliseconds} ms.");
    }

    public static async Task<UiWaitResult<T>> TryUntilAsync<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        UiWaitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        ArgumentNullException.ThrowIfNull(condition);

        var waitOptions = ValidateOptions(options);
        var stopwatch = Stopwatch.StartNew();
        var lastValue = valueFactory();

        while (stopwatch.Elapsed < waitOptions.Timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (condition(lastValue))
            {
                return new UiWaitResult<T>(true, lastValue, stopwatch.Elapsed);
            }

            await Task.Delay(waitOptions.PollInterval, waitOptions.TimeProvider, cancellationToken);
            lastValue = valueFactory();
        }

        var success = condition(lastValue);
        return new UiWaitResult<T>(success, lastValue, stopwatch.Elapsed);
    }

    public static async Task<T> UntilAsync<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        UiWaitOptions? options = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        var result = await TryUntilAsync(valueFactory, condition, options, cancellationToken);
        if (result.Success)
        {
            return result.Value;
        }

        throw new TimeoutException(timeoutMessage ?? $"Condition was not met within {ValidateOptions(options).Timeout.TotalMilliseconds} ms.");
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