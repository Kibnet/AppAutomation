namespace AppAutomation.Session.Contracts;

public sealed class DesktopAppLaunchOptions
{
    public required string ExecutablePath { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    public TimeSpan MainWindowTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(200);
}
