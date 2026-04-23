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
