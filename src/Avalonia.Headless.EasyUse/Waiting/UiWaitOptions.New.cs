namespace Avalonia.Headless.EasyUse.Waiting;

public sealed record UiWaitOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public static UiWaitOptions Default { get; } = new();

    internal global::FlaUI.EasyUse.Waiting.UiWaitOptions ToLegacy()
    {
        return new global::FlaUI.EasyUse.Waiting.UiWaitOptions
        {
            Timeout = Timeout,
            PollInterval = PollInterval,
            TimeProvider = TimeProvider
        };
    }
}