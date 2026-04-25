using AppAutomation.Abstractions;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

public enum RecordedActionKind
{
    EnterText = 0,
    ClickButton = 1,
    SetChecked = 2,
    SetToggled = 3,
    SelectComboItem = 4,
    SetSliderValue = 5,
    SetSpinnerValue = 6,
    SelectTabItem = 7,
    SelectTreeItem = 8,
    SetDate = 9,
    WaitUntilTextEquals = 10,
    WaitUntilTextContains = 11,
    WaitUntilIsChecked = 12,
    WaitUntilIsToggled = 13,
    WaitUntilIsSelected = 14,
    WaitUntilIsEnabled = 15,
    SelectListBoxItem = 16,
    WaitUntilGridRowsAtLeast = 17,
    WaitUntilGridCellEquals = 18,
    SearchAndSelect = 19,
    OpenGridRow = 20,
    SortGridByColumn = 21,
    ScrollGridToEnd = 22,
    CopyGridCell = 23,
    ExportGrid = 24,
    ConfirmDialog = 25,
    CancelDialog = 26,
    DismissDialog = 27,
    DismissNotification = 28,
    OpenOrActivateShellPane = 29,
    ActivateShellPane = 30,
    SearchAndSelectGridCell = 31
}

public enum RecorderAssertionMode
{
    Auto = 0,
    Text = 1,
    Enabled = 2,
    Checked = 3
}

public enum RecorderValidationStatus
{
    Valid = 0,
    Warning = 1,
    Invalid = 2
}

internal sealed record RecordedControlDescriptor(
    string ProposedPropertyName,
    UiControlType ControlType,
    string LocatorValue,
    UiLocatorKind LocatorKind,
    bool FallbackToName,
    string AvaloniaTypeName,
    string? Warning);

internal sealed record RecordedStep(
    RecordedActionKind ActionKind,
    RecordedControlDescriptor Control,
    string? StringValue = null,
    bool? BoolValue = null,
    double? DoubleValue = null,
    DateTime? DateValue = null,
    string? Warning = null,
    RecorderValidationStatus ValidationStatus = RecorderValidationStatus.Valid,
    string? ValidationMessage = null,
    bool CanPersist = true,
    Guid StepId = default,
    bool IsIgnored = false,
    RecorderStepReviewState ReviewState = RecorderStepReviewState.Active,
    string? FailureCode = null,
    DateTimeOffset? LastValidationAt = null,
    int? IntValue = null,
    int? RowIndex = null,
    int? ColumnIndex = null,
    string? ItemValue = null,
    IReadOnlyList<RecorderRuntimeValidationFinding>? RuntimeValidationFindings = null);

internal enum RecorderRuntimeValidationTarget
{
    Headless = 0,
    FlaUI = 1
}

internal enum RecorderRuntimeValidationSeverity
{
    Info = 0,
    Warning = 1,
    Invalid = 2
}

internal sealed record RecorderRuntimeValidationFinding(
    RecorderRuntimeValidationTarget Target,
    RecorderRuntimeValidationSeverity Severity,
    string Code,
    string Message,
    bool BlocksTarget)
{
    public bool ShouldSurface => Severity != RecorderRuntimeValidationSeverity.Info || BlocksTarget;
}

internal sealed record StepCreationResult(bool Success, RecordedStep? Step, string Message)
{
    public static StepCreationResult Unsupported(string message) => new(false, null, message);

    public static StepCreationResult Created(RecordedStep step, string? message = null)
    {
        return new StepCreationResult(true, step, message ?? string.Empty);
    }
}

internal sealed record ResolvedControlResult(
    bool Success,
    RecordedControlDescriptor? Control,
    string Message,
    RecorderValidationStatus ValidationStatus,
    string? ValidationMessage,
    bool CanPersist)
{
    public static ResolvedControlResult Unsupported(string message)
    {
        return new ResolvedControlResult(
            false,
            null,
            message,
            RecorderValidationStatus.Invalid,
            message,
            false);
    }

    public static ResolvedControlResult Created(
        RecordedControlDescriptor control,
        string? message = null,
        RecorderValidationStatus validationStatus = RecorderValidationStatus.Valid,
        string? validationMessage = null,
        bool canPersist = true)
    {
        return new ResolvedControlResult(
            true,
            control,
            message ?? string.Empty,
            validationStatus,
            validationMessage,
            canPersist);
    }
}

internal sealed record AuthoringTargetConfiguration(
    string ProjectDirectory,
    string OutputDirectory,
    string PageNamespace,
    string PageClassName,
    string ScenarioNamespace,
    string ScenarioClassName,
    string ScenarioName,
    string AppName);

internal sealed record RecorderOutputDescription(
    bool IsConfigured,
    string ScenarioFilePathDisplay,
    string? OutputDirectory,
    string? PageFilePathDisplay);

internal sealed record ScannedClassInfo(
    string Namespace,
    string Name,
    string ModifiersText,
    string TypeParameterListText,
    bool IsPartial);

internal sealed record ExistingControlInfo(
    string PropertyName,
    UiControlType ControlType,
    string LocatorValue,
    UiLocatorKind LocatorKind,
    bool FallbackToName);

internal sealed record AuthoringProjectSnapshot(
    ScannedClassInfo? PageClass,
    ScannedClassInfo? ScenarioClass,
    IReadOnlyDictionary<string, ExistingControlInfo> ExistingControlsByKey,
    IReadOnlySet<string> ExistingControlPropertyNames,
    IReadOnlySet<string> ExistingScenarioMethodNames);
