namespace AppAutomation.Recorder.Avalonia;

internal enum RecorderStepEditKind
{
    Checkpoint = 0,
    Assertion = 1,
    Text = 2,
    Number = 3,
    Boolean = 4,
    Date = 5,
    Time = 6,
    StringSet = 7,
    GeneratedValue = 8,
    CopiedValue = 9,
    Composite = 10,
    Integer = 11
}

internal enum RecorderExpectedValueSourceKind
{
    None = 0,
    Literal = 1,
    Checkpoint = 2,
    GeneratedValue = 3,
    Calculated = 4
}

internal enum RecorderStepRetargetRole
{
    ValueSource = 0,
    AssertionTarget = 1,
    CalculatedLeftOperand = 2,
    CalculatedRightOperand = 3
}

internal sealed record RecorderGridRowVariableOption(
    string DisplayName,
    RecorderGridRowValueReference Reference)
{
    public override string ToString() => DisplayName;
}

internal sealed record RecorderStepEditDraft(
    Guid StepId,
    long Revision,
    RecorderStepEditKind EditKind,
    RecordedActionKind ActionKind,
    string ControlName,
    RecorderValueKind? ValueKind,
    RecorderValueAccessorKind? ValueAccessorKind,
    string? VariableName,
    string? StringValue,
    string? ItemValue,
    bool? BoolValue,
    int? IntValue,
    double? DoubleValue,
    string? NumericInputText,
    string? NumericInputCultureName,
    double? SecondDoubleValue,
    DateTime? DateValue,
    DateTime? SecondDateValue,
    TimeSpan? TimeValue,
    IReadOnlyList<string>? StringValues,
    RecorderComparisonKind? ComparisonKind,
    RecorderExpectedValueSourceKind ExpectedSource,
    Guid? ExpectedCheckpointId,
    Guid? ExpectedGeneratedValueId,
    RecorderNumericExpectedExpression? NumericExpectedExpression,
    RecorderDateExpression? DateExpression,
    RecorderDateExpression? SecondDateExpression,
    string CurrentPreview,
    string GeneratedPreview,
    IReadOnlyList<RecorderCheckpointOption> CompatibleCheckpoints,
    IReadOnlyList<RecorderGeneratedValueOption> CompatibleGeneratedValues,
    IReadOnlyList<RecordedGridRowCondition>? GridRowConditions,
    string? GridTargetColumnName,
    IReadOnlyList<RecorderGridRowVariableOption> AvailableGridRowVariables,
    bool SupportsCalculatedExpectedValue,
    RecordedStep? RetargetPrototype = null)
{
    public RecorderGridRowSourceMode GridRowSourceMode { get; init; }

    public int? GridCapturedRowPosition { get; init; }
}

internal sealed record RecorderStepEditResult(
    bool Success,
    string Message,
    RecorderStepEditDraft? Draft = null)
{
    public static RecorderStepEditResult Applied(string message) => new(true, message);

    public static RecorderStepEditResult Rejected(string message, RecorderStepEditDraft draft) =>
        new(false, message, draft);
}

internal sealed record RecorderStepEditPreviewResult(
    bool Success,
    string Preview,
    string Message);
