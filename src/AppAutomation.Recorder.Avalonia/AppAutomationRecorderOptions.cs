using Microsoft.Extensions.Logging;

namespace AppAutomation.Recorder.Avalonia;

public sealed class AppAutomationRecorderOptions
{
    public string ScenarioName { get; init; } = "Scenario";

    public string? AuthoringProjectDirectory { get; init; }

    public string OutputSubdirectory { get; init; } = "Recorded";

    public string? PageNamespace { get; init; }

    public string? PageClassName { get; init; }

    public string? ScenarioNamespace { get; init; }

    public string? ScenarioClassName { get; init; }

    public bool AllowNameLocators { get; init; }

    public bool ShowOverlay { get; init; } = true;

    public RecorderOverlayTheme? OverlayTheme { get; init; }

    public ILogger? Logger { get; init; }

    public IList<RecorderControlHint> ControlHints { get; } = new List<RecorderControlHint>();
}

public enum RecorderOverlayTheme
{
    Light = 0,
    Dark = 1
}

public sealed record RecorderControlHint(string LocatorValue, RecorderActionHint ActionHint);

public enum RecorderActionHint
{
    None = 0,
    SpinnerTextBox = 1
}
