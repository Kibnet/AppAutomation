namespace EasyUse.Session.Contracts;

public sealed class DesktopAppLaunchOptions
{
    public required string ExecutablePath { get; init; }

    public string? WorkingDirectory { get; init; }

    public TimeSpan MainWindowTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(200);
}
