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
    SearchAndSelectGridCell = 31,
    WaitUntilProgressAtLeast = 32,
    WaitUntilListBoxContains = 33,
    WaitUntilHasItemsAtLeast = 34,
    WaitUntilNotificationContains = 35,
    SetDateRangeFilter = 36,
    SetNumericRangeFilter = 37,
    SelectExportFolder = 38,
    EditGridCellText = 39,
    EditGridCellNumber = 40,
    EditGridCellDate = 41,
    SelectGridCellComboItem = 42,
    WaitUntilExists = 43,
    SelectMultiItems = 44,
    CancelMultiSelection = 45,
    ApplyFilterSelection = 46,
    CancelFilterSelection = 47,
    EnterSearch = 48,
    ClearSearch = 49,
    ApplySearchFromHistory = 50,
    WaitUntilGridContainsRow = 51,
    WaitUntilValueEquals = 52,
    SetTime = 53,
    WaitUntilTimeEquals = 54,
    EditGridCellTime = 55,
    SetExpanded = 56,
    WaitUntilIsExpanded = 57,
    SetColor = 58,
    WaitUntilColorEquals = 59,
    EditGridCellColor = 60,
    InvokeMenuItem = 61,
    InvokeContextMenuItem = 62,
    CaptureCheckpoint = 63,
    AssertValue = 64
}

public enum RecorderAssertionMode
{
    Auto = 0,
    Text = 1,
    Enabled = 2,
    Checked = 3,
    Exists = 4
}

public enum RecorderValidationStatus
{
    Valid = 0,
    Warning = 1,
    Invalid = 2
}

public enum RecorderValueKind
{
    Text = 0,
    Number = 1,
    Boolean = 2,
    Date = 3,
    Time = 4,
    Color = 5,
    StringSet = 6,
    GridCellText = 7
}

public enum RecorderValueAccessorKind
{
    Text = 0,
    SelectedItemText = 1,
    SelectedItems = 2,
    NumericValue = 3,
    SelectedDate = 4,
    SelectedTime = 5,
    Color = 6,
    IsChecked = 7,
    IsToggled = 8,
    IsSelected = 9,
    IsExpanded = 10,
    IsEnabled = 11,
    GridCellText = 12
}

internal enum RecorderComparisonKind
{
    Equal = 0,
    Contains = 1,
    Equivalent = 2,
    HasValue = 3,
    IsEmpty = 4,
    NotEqual = 5
}

internal enum RecorderHasValueAssertionKind
{
    NotEmpty = 0,
    NotNull = 1,
    Empty = 2,
    Null = 3
}

internal static class RecorderValueAssertions
{
    public static bool TryGetHasValueAssertionKind(
        RecorderValueKind valueKind,
        out RecorderHasValueAssertionKind assertionKind)
    {
        switch (valueKind)
        {
            case RecorderValueKind.Text:
            case RecorderValueKind.Color:
            case RecorderValueKind.StringSet:
            case RecorderValueKind.GridCellText:
                assertionKind = RecorderHasValueAssertionKind.NotEmpty;
                return true;
            case RecorderValueKind.Date:
            case RecorderValueKind.Time:
                assertionKind = RecorderHasValueAssertionKind.NotNull;
                return true;
            default:
                assertionKind = default;
                return false;
        }
    }

    public static bool TryGetPresenceAssertionKind(
        RecorderValueKind valueKind,
        bool expectEmpty,
        out RecorderHasValueAssertionKind assertionKind)
    {
        if (!TryGetHasValueAssertionKind(valueKind, out var positiveKind))
        {
            assertionKind = default;
            return false;
        }

        assertionKind = (positiveKind, expectEmpty) switch
        {
            (RecorderHasValueAssertionKind.NotEmpty, false) => RecorderHasValueAssertionKind.NotEmpty,
            (RecorderHasValueAssertionKind.NotEmpty, true) => RecorderHasValueAssertionKind.Empty,
            (RecorderHasValueAssertionKind.NotNull, false) => RecorderHasValueAssertionKind.NotNull,
            (RecorderHasValueAssertionKind.NotNull, true) => RecorderHasValueAssertionKind.Null,
            _ => throw new InvalidOperationException($"Unsupported presence assertion kind '{positiveKind}'.")
        };
        return true;
    }
}

internal sealed record RecorderAssertionCapability(
    UiControlType ControlType,
    IReadOnlySet<RecorderValueKind> ValueKinds,
    IReadOnlySet<RecorderValueAccessorKind> AccessorKinds,
    bool RequiresConcreteTarget = false)
{
    public bool SupportsSemanticValue => ValueKinds.Count > 0;
}

internal static class RecorderAssertionCapabilities
{
    private static readonly IReadOnlySet<RecorderValueKind> NoValueKinds =
        new HashSet<RecorderValueKind>();
    private static readonly IReadOnlySet<RecorderValueAccessorKind> NoAccessors =
        new HashSet<RecorderValueAccessorKind>();

    public static RecorderAssertionCapability Get(UiControlType controlType)
    {
        return controlType switch
        {
            UiControlType.TextBox => Value(
                controlType,
                RecorderValueKind.Text,
                RecorderValueAccessorKind.Text),
            UiControlType.Label => Value(
                controlType,
                RecorderValueKind.Text,
                RecorderValueAccessorKind.Text),
            UiControlType.ListBox => Value(
                controlType,
                RecorderValueKind.Text,
                RecorderValueAccessorKind.SelectedItemText),
            UiControlType.CheckBox => Value(
                controlType,
                RecorderValueKind.Boolean,
                RecorderValueAccessorKind.IsChecked),
            UiControlType.ComboBox => Value(
                controlType,
                RecorderValueKind.Text,
                RecorderValueAccessorKind.SelectedItemText),
            UiControlType.RadioButton => Value(
                controlType,
                RecorderValueKind.Boolean,
                RecorderValueAccessorKind.IsSelected),
            UiControlType.ToggleButton => Value(
                controlType,
                RecorderValueKind.Boolean,
                RecorderValueAccessorKind.IsToggled),
            UiControlType.Slider or UiControlType.ProgressBar or UiControlType.Spinner => Value(
                controlType,
                RecorderValueKind.Number,
                RecorderValueAccessorKind.NumericValue),
            UiControlType.Calendar or UiControlType.DateTimePicker => Value(
                controlType,
                RecorderValueKind.Date,
                RecorderValueAccessorKind.SelectedDate),
            UiControlType.TabItem or UiControlType.TreeItem => Value(
                controlType,
                RecorderValueKind.Boolean,
                RecorderValueAccessorKind.IsSelected),
            UiControlType.GridCell or UiControlType.DataGridViewCell => Value(
                controlType,
                RecorderValueKind.GridCellText,
                RecorderValueAccessorKind.GridCellText,
                requiresConcreteTarget: true),
            UiControlType.Grid or UiControlType.DataGridView => Contextual(
                controlType,
                RecorderValueKind.GridCellText,
                RecorderValueAccessorKind.GridCellText),
            UiControlType.SearchPicker => Value(
                controlType,
                RecorderValueKind.Text,
                RecorderValueAccessorKind.SelectedItemText),
            UiControlType.Notification => Value(
                controlType,
                RecorderValueKind.Text,
                RecorderValueAccessorKind.Text),
            UiControlType.MultiSelect or UiControlType.ComboBoxFilter => Value(
                controlType,
                RecorderValueKind.StringSet,
                RecorderValueAccessorKind.SelectedItems),
            UiControlType.Search => Value(
                controlType,
                RecorderValueKind.Text,
                RecorderValueAccessorKind.Text),
            UiControlType.TimePicker => Value(
                controlType,
                RecorderValueKind.Time,
                RecorderValueAccessorKind.SelectedTime),
            UiControlType.Expander => Value(
                controlType,
                RecorderValueKind.Boolean,
                RecorderValueAccessorKind.IsExpanded),
            UiControlType.ColorPicker => Value(
                controlType,
                RecorderValueKind.Color,
                RecorderValueAccessorKind.Color),
            UiControlType.AutomationElement
                or UiControlType.Button
                or UiControlType.Tab
                or UiControlType.Tree
                or UiControlType.DataGridViewRow
                or UiControlType.GridRow
                or UiControlType.DateRangeFilter
                or UiControlType.NumericRangeFilter
                or UiControlType.Dialog
                or UiControlType.FolderExport
                or UiControlType.ShellNavigation
                or UiControlType.Menu
                or UiControlType.MenuItem => StateOnly(controlType),
            _ => throw new ArgumentOutOfRangeException(
                nameof(controlType),
                controlType,
                "UiControlType does not have an assertion capability classification.")
        };
    }

    private static RecorderAssertionCapability StateOnly(UiControlType controlType) =>
        new(controlType, NoValueKinds, NoAccessors);

    private static RecorderAssertionCapability Value(
        UiControlType controlType,
        RecorderValueKind valueKind,
        RecorderValueAccessorKind accessorKind,
        bool requiresConcreteTarget = false) =>
        new(
            controlType,
            new HashSet<RecorderValueKind> { valueKind },
            new HashSet<RecorderValueAccessorKind> { accessorKind },
            requiresConcreteTarget);

    private static RecorderAssertionCapability Contextual(
        UiControlType controlType,
        RecorderValueKind valueKind,
        RecorderValueAccessorKind accessorKind) =>
        Value(controlType, valueKind, accessorKind, requiresConcreteTarget: true);
}

internal sealed record RecorderSemanticValueDescription(
    RecorderValueKind ValueKind,
    string SuggestedCheckpointName,
    string CurrentValueText);

internal sealed record RecorderSemanticValueSnapshot(
    RecordedStep Prototype,
    RecorderSemanticValueDescription Description);

internal sealed record RecorderCheckpointOption(
    Guid CheckpointId,
    string VariableName,
    RecorderValueKind ValueKind,
    string ControlName);

internal sealed record RecorderGeneratedValueOption(
    Guid GeneratedValueId,
    string VariableName,
    int Ordinal,
    string PreviewValue);

internal sealed record RecordedControlDescriptor(
    string ProposedPropertyName,
    UiControlType ControlType,
    string LocatorValue,
    UiLocatorKind LocatorKind,
    bool FallbackToName,
    string AvaloniaTypeName,
    string? Warning);

internal enum RecorderDateReferenceKind
{
    Exact = 0,
    RelativeToToday = 1
}

internal sealed record RecorderDateExpression(
    RecorderDateReferenceKind ReferenceKind,
    int DayOffset);

internal sealed record RecorderDateOperandConfiguration(
    DateTime? ExactDate,
    RecorderDateReferenceKind ReferenceKind,
    int DayOffset);

internal sealed record RecorderStepDateConfiguration(
    Guid StepId,
    RecorderDateOperandConfiguration Primary,
    RecorderDateOperandConfiguration? Secondary);

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
    DateTime? SecondDateValue = null,
    double? SecondDoubleValue = null,
    FilterPopupCommitMode? FilterCommitMode = null,
    FolderExportCommitMode? FolderExportCommitMode = null,
    GridCellEditCommitMode? GridCellEditCommitMode = null,
    IReadOnlyList<RecorderRuntimeValidationFinding>? RuntimeValidationFindings = null,
    IReadOnlyList<string>? StringValues = null,
    TimeSpan? TimeValue = null,
    RecorderValueKind? ValueKind = null,
    RecorderValueAccessorKind? ValueAccessorKind = null,
    RecorderComparisonKind? ComparisonKind = null,
    Guid? CheckpointId = null,
    string? CheckpointVariableName = null,
    Guid? ExpectedCheckpointId = null,
    bool HasExpectedLiteral = false,
    RecorderDateExpression? DateExpression = null,
    RecorderDateExpression? SecondDateExpression = null,
    Guid? GeneratedValueId = null,
    string? GeneratedValueVariableName = null,
    int? GeneratedValueOrdinal = null,
    bool DefinesGeneratedValue = false,
    Guid? ExpectedGeneratedValueId = null)
{
    public IReadOnlyList<RecordedGridRowCondition>? GridRowConditions { get; init; }

    public string? GridTargetColumnName { get; init; }

    public RecorderStepValidationState? ValidationBeforeGraphError { get; init; }
}

internal sealed record RecordedGridRowCondition(string ColumnName, string Value);

internal sealed record RecorderStepValidationState(
    RecorderValidationStatus ValidationStatus,
    string? ValidationMessage,
    bool CanPersist,
    RecorderStepReviewState ReviewState,
    string? FailureCode);

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

internal sealed record SearchPickerSelectionCaptureResult(
    bool IsConfigured,
    bool HasSelection,
    TextBox? SearchInput,
    StepCreationResult StepResult);

internal sealed record SingleSelectCaptureResult(
    bool IsConfigured,
    bool HasSelection,
    RecorderSingleSelectHint? Hint,
    StepCreationResult StepResult);

internal sealed record ColorPickerCaptureResult(
    bool IsConfigured,
    bool HasCandidateValue,
    bool HasColor,
    RecorderColorPickerHint? Hint,
    StepCreationResult StepResult);

internal sealed record GridComboSelectionContext(
    Control SelectionSource,
    Control GridSource,
    RecorderGridHint GridHint,
    int RowIndex,
    int ColumnIndex);

internal sealed record GridComboSelectionContextResolution(
    bool IsConfigured,
    GridComboSelectionContext? Context,
    string? Error);

internal sealed record GridComboSelectionCaptureResult(
    bool IsConfigured,
    bool HasSelection,
    GridComboSelectionContext? Context,
    StepCreationResult StepResult);

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
    string AutosaveDirectory,
    string PageNamespace,
    string PageClassName,
    string ScenarioNamespace,
    string ScenarioClassName,
    int? ScenarioGenericArity,
    string? ScenarioTypeParameterSignature,
    string ScenarioName,
    string AppName);

internal sealed record RecorderScenarioSaveContext(
    RecordedScenarioDestination Destination,
    string ScenarioName,
    string DraftIdentity);

internal sealed record RecorderOutputDescription(
    bool IsConfigured,
    string ScenarioFilePathDisplay,
    string? OutputDirectory,
    string? PageFilePathDisplay);

internal sealed record ScannedClassInfo(
    string Namespace,
    string Name,
    string SourceFilePath,
    string ModifiersText,
    string TypeParameterListText,
    string TypeParameterSignature,
    int GenericArity,
    bool IsPartial);

internal sealed record ScenarioDestinationDiscoveryResult(
    IReadOnlyList<RecordedScenarioDestination> Destinations,
    string? Error)
{
    public bool Success => Error is null;

    public static ScenarioDestinationDiscoveryResult Failed(string error)
    {
        return new ScenarioDestinationDiscoveryResult(Array.Empty<RecordedScenarioDestination>(), error);
    }
}

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
    IReadOnlyDictionary<string, ExistingControlInfo> ExistingControlsByTypedKey,
    IReadOnlySet<string> ExistingControlPropertyNames,
    IReadOnlySet<string> ExistingScenarioMethodNames);
