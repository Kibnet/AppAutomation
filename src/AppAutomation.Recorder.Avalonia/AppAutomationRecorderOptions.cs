using AppAutomation.Abstractions;
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

    public IList<RecorderGridHint> GridHints { get; } = new List<RecorderGridHint>();

    public IList<RecorderGridActionHint> GridActionHints { get; } = new List<RecorderGridActionHint>();

    public IList<RecorderSearchPickerHint> SearchPickerHints { get; } = new List<RecorderSearchPickerHint>();

    public IList<RecorderLocatorAlias> LocatorAliases { get; } = new List<RecorderLocatorAlias>();

    public IList<IRecorderAssertionExtractor> AssertionExtractors { get; } = new List<IRecorderAssertionExtractor>();
}

public enum RecorderOverlayTheme
{
    Light = 0,
    Dark = 1
}

public sealed record RecorderControlHint(
    string LocatorValue,
    RecorderActionHint ActionHint,
    UiControlType? TargetControlType = null,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool? FallbackToName = null);

public sealed record RecorderLocatorAlias(
    string SourceLocatorValue,
    string TargetLocatorValue,
    UiControlType TargetControlType = UiControlType.AutomationElement,
    UiLocatorKind SourceLocatorKind = UiLocatorKind.AutomationId,
    UiLocatorKind TargetLocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = false);

public sealed record RecorderGridHint(
    string SourceLocatorValue,
    string TargetLocatorValue,
    IReadOnlyList<string> ColumnPropertyNames,
    UiLocatorKind SourceLocatorKind = UiLocatorKind.AutomationId,
    UiLocatorKind TargetLocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = false);

public sealed record RecorderGridActionHint(
    string SourceLocatorValue,
    string TargetGridLocatorValue,
    RecorderGridUserActionKind ActionKind,
    UiLocatorKind SourceLocatorKind = UiLocatorKind.AutomationId,
    UiLocatorKind TargetGridLocatorKind = UiLocatorKind.AutomationId,
    bool TargetFallbackToName = false,
    string? ColumnName = null,
    int? RowIndex = null,
    int? ColumnIndex = null);

public sealed record RecorderSearchPickerHint(
    string LocatorValue,
    SearchPickerParts Parts,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = false);

public enum RecorderGridUserActionKind
{
    OpenRow = 0,
    SortByColumn = 1,
    ScrollToEnd = 2,
    CopyCell = 3,
    Export = 4
}

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
