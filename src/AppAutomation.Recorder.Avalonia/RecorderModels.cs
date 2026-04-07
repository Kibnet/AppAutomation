using AppAutomation.Abstractions;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

internal enum RecordedActionKind
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
    WaitUntilIsEnabled = 15
}

internal enum RecorderAssertionMode
{
    Auto = 0,
    Text = 1,
    Enabled = 2,
    Checked = 3
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
    string? Warning = null);

internal sealed record StepCreationResult(bool Success, RecordedStep? Step, string Message)
{
    public static StepCreationResult Unsupported(string message) => new(false, null, message);

    public static StepCreationResult Created(RecordedStep step, string? message = null)
    {
        return new StepCreationResult(true, step, message ?? string.Empty);
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
