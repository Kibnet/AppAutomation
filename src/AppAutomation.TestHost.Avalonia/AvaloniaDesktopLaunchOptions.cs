using AppAutomation.Session.Contracts;

namespace AppAutomation.TestHost.Avalonia;

public sealed class AvaloniaDesktopLaunchOptions
{
    public string BuildConfiguration { get; init; } = "Debug";

    public bool BuildBeforeLaunch { get; init; } = true;

    public bool BuildOncePerProcess { get; init; } = true;

    public TimeSpan BuildTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan MainWindowTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    public DesktopWindowPlacement? WindowPlacement { get; init; }

    public bool UseIsolatedBuildOutput { get; init; }

    public string? IsolatedBuildRoot { get; init; }
}
