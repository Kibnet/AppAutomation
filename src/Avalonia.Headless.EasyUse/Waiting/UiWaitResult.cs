namespace FlaUI.EasyUse.Waiting;

public readonly record struct UiWaitResult<T>(bool Success, T Value, TimeSpan Elapsed);