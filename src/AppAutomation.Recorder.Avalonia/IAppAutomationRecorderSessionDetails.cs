using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

public interface IAppAutomationRecorderSessionDetails
{
    event EventHandler? SessionChanged;

    bool IsBusy { get; }

    string BusyDescription { get; }

    string SessionSummary { get; }

    bool IsDiagnosticLogFileEnabled { get; }

    string DiagnosticLogFilePath { get; }

    int DiagnosticLogEntryCount { get; }

    int WarningStepCount { get; }

    int InvalidStepCount { get; }

    int IgnoredStepCount { get; }

    IReadOnlyList<RecorderStepJournalEntry> StepJournal { get; }

    void RemoveStep(Guid stepId);

    void SetStepIgnored(Guid stepId, bool isIgnored);

    bool RetryStepValidation(Guid stepId);

    void SetDiagnosticLogFileEnabled(bool isEnabled);
}

internal interface IRecorderStepReorderSessionDetails
{
    bool CanMoveStep(Guid stepId, RecorderStepMoveDirection direction);

    bool MoveStep(Guid stepId, RecorderStepMoveDirection direction);
}

internal interface IRecorderCheckpointSessionDetails
{
    event EventHandler<RecorderCheckTargetSelectedEventArgs>? CheckTargetSelected;

    event EventHandler<RecorderNumericOperandTargetSelectedEventArgs>? NumericOperandTargetSelected;

    IReadOnlyList<RecorderCheckpointOption> Checkpoints { get; }

    bool IsCheckTargetSelectionActive { get; }

    bool IsNumericOperandTargetSelectionActive { get; }

    void BeginCheckTargetSelection();

    void CancelCheckTargetSelection();

    void BeginNumericOperandTargetSelection();

    void CancelNumericOperandTargetSelection();

    void CaptureCheckpoint(RecorderCheckTargetSelection selection, string? variableName = null);

    void CaptureCheckpointAssertion(
        RecorderCheckTargetSelection selection,
        Guid checkpointId,
        RecorderComparisonKind comparisonKind = RecorderComparisonKind.Equal);

    void CapturePresenceAssertion(RecorderCheckTargetSelection selection, bool expectEmpty);

    void CaptureEnabledAssertion(RecorderCheckTargetSelection selection, bool expectedEnabled);

    void CaptureCalculatedAssertion(
        RecorderCheckTargetSelection selection,
        RecorderNumericExpectedExpression expression);

    void CaptureLiteralAssertion(
        RecorderCheckTargetSelection selection,
        string expectedText,
        RecorderComparisonKind comparisonKind,
        RecorderDateExpression? dateExpression = null);
}

internal interface IRecorderGeneratedValueSessionDetails
{
    event EventHandler<RecorderGeneratedValueTargetSelectedEventArgs>? GeneratedValueTargetSelected;

    IReadOnlyList<RecorderGeneratedValueOption> GeneratedValues { get; }

    bool IsGeneratedValueTargetSelectionActive { get; }

    void BeginGeneratedValueTargetSelection(Guid? generatedValueId = null);

    void CancelGeneratedValueTargetSelection();

    void ApplyGeneratedValue(RecorderGeneratedValueTargetSelection selection);

    void CaptureGeneratedValueAssertion(
        RecorderCheckTargetSelection selection,
        Guid generatedValueId,
        RecorderComparisonKind comparisonKind = RecorderComparisonKind.Equal);
}

internal interface IRecorderRelativeDateSessionDetails
{
    bool TryGetDateConfiguration(
        Guid stepId,
        out RecorderStepDateConfiguration? configuration);

    bool SetStepDateExpressions(
        Guid stepId,
        RecorderDateExpression? primary,
        RecorderDateExpression? secondary);
}

internal sealed record RecorderCheckTargetSelection(
    Control Target,
    RecorderSemanticValueSnapshot? ValueSnapshot,
    string? ValueDescriptionError,
    bool IsEnabled,
    bool CanCaptureAssertions = true)
{
    public RecorderSemanticValueDescription? ValueDescription => ValueSnapshot?.Description;
}

internal sealed class RecorderCheckTargetSelectedEventArgs(RecorderCheckTargetSelection selection) : EventArgs
{
    public RecorderCheckTargetSelection Selection { get; } = selection;
}

internal sealed record RecorderNumericOperandTargetSelection(
    Control Target,
    RecorderNumericOperand? Operand,
    string? ControlName,
    string? Error);

internal sealed class RecorderNumericOperandTargetSelectedEventArgs(
    RecorderNumericOperandTargetSelection selection) : EventArgs
{
    public RecorderNumericOperandTargetSelection Selection { get; } = selection;
}

internal sealed record RecorderGeneratedValueTargetSelection(
    TextBox Input,
    RecorderGeneratedValueOption GeneratedValue,
    bool DefinesGeneratedValue,
    string ControlName);

internal sealed class RecorderGeneratedValueTargetSelectedEventArgs(
    RecorderGeneratedValueTargetSelection selection) : EventArgs
{
    public RecorderGeneratedValueTargetSelection Selection { get; } = selection;
}

internal enum RecorderStepMoveDirection
{
    Earlier = 0,
    Later = 1
}

public enum RecorderStepReviewState
{
    Active = 0,
    NeedsReview = 1,
    Ignored = 2
}

public sealed record RecorderStepJournalEntry(
    Guid StepId,
    string Preview,
    string StatusMessage,
    RecorderValidationStatus ValidationStatus,
    bool CanPersist,
    bool IsIgnored,
    RecorderStepReviewState ReviewState,
    string? FailureCode,
    DateTimeOffset? LastValidationAt);
