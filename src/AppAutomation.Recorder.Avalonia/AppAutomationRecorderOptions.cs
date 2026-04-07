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

    public RecorderHotkeys Hotkeys { get; init; } = RecorderHotkeys.Default;

    public RecorderOverlayOptions Overlay { get; init; } = new();

    public RecorderValidationOptions Validation { get; init; } = new();

    public IList<RecorderControlHint> ControlHints { get; } = new List<RecorderControlHint>();

    public IList<IRecorderAssertionExtractor> AssertionExtractors { get; } = new List<IRecorderAssertionExtractor>();
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

public sealed class RecorderHotkeys
{
    public static RecorderHotkeys Default { get; } = new();

    public string? StartStop { get; init; } = "Ctrl+Shift+R";

    public string? Save { get; init; } = "Ctrl+Shift+S";

    public string? Export { get; init; } = "Ctrl+Shift+X";

    public string? Clear { get; init; } = "Ctrl+Shift+C";

    public string? CaptureAssertAuto { get; init; } = "Ctrl+Shift+A";

    public string? CaptureAssertText { get; init; } = "Ctrl+Shift+T";

    public string? CaptureAssertEnabled { get; init; } = "Ctrl+Shift+E";

    public string? CaptureAssertChecked { get; init; } = "Ctrl+Shift+K";

    public string? ToggleOverlayMinimize { get; init; } = "Ctrl+Shift+M";
}

public sealed class RecorderOverlayOptions
{
    public bool EnableExportButton { get; init; } = true;

    public bool ShowShortcutLegend { get; init; } = true;

    public bool StartMinimized { get; init; }
}

public sealed class RecorderValidationOptions
{
    public bool ValidateSelectors { get; init; } = true;

    public bool CaptureInvalidSteps { get; init; } = true;
}
