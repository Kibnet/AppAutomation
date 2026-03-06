namespace EasyUse.TUnit.Core.Waiting;

public sealed record UiWaitOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public static UiWaitOptions Default { get; } = new();
}
