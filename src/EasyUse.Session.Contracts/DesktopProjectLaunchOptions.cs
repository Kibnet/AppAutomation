namespace EasyUse.Session.Contracts;

public sealed record class DesktopProjectLaunchOptions
{
    public required string ProjectRelativePath { get; init; }

    public required string TargetFramework { get; init; }

    public string BuildConfiguration { get; init; } = "Debug";

    public string SolutionFileName { get; init; } = "DotnetDebug.sln";

    public string? ExecutableName { get; init; }

    public bool BuildBeforeLaunch { get; init; } = true;

    public bool BuildOncePerProcess { get; init; } = true;

    public TimeSpan BuildTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan MainWindowTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(200);
}
