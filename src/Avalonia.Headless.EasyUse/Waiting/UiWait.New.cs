namespace Avalonia.Headless.EasyUse.Waiting;

public static class UiWait
{
    public static UiWaitResult<T> TryUntil<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        UiWaitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = global::FlaUI.EasyUse.Waiting.UiWait.TryUntil(
            valueFactory,
            condition,
            options?.ToLegacy(),
            cancellationToken);

        return new UiWaitResult<T>(result.Success, result.Value, result.Elapsed);
    }

    public static T Until<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        UiWaitOptions? options = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        return global::FlaUI.EasyUse.Waiting.UiWait.Until(
            valueFactory,
            condition,
            options?.ToLegacy(),
            timeoutMessage,
            cancellationToken);
    }

    public static async Task<UiWaitResult<T>> TryUntilAsync<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        UiWaitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await global::FlaUI.EasyUse.Waiting.UiWait.TryUntilAsync(
            valueFactory,
            condition,
            options?.ToLegacy(),
            cancellationToken);

        return new UiWaitResult<T>(result.Success, result.Value, result.Elapsed);
    }

    public static Task<T> UntilAsync<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        UiWaitOptions? options = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        return global::FlaUI.EasyUse.Waiting.UiWait.UntilAsync(
            valueFactory,
            condition,
            options?.ToLegacy(),
            timeoutMessage,
            cancellationToken);
    }
}