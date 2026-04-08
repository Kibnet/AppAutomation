namespace AppAutomation.Recorder.Avalonia;

public interface IAppAutomationRecorderSession : IDisposable
{
    RecorderSessionState State { get; }

    int StepCount { get; }

    int PersistableStepCount { get; }

    string LatestPreview { get; }

    string LatestStatus { get; }

    RecorderValidationStatus LatestValidationStatus { get; }

    void Start();

    void Stop();

    void Clear();

    string ExportPreview();

    Task<RecorderSaveResult> SaveAsync(CancellationToken cancellationToken = default);

    Task<RecorderSaveResult> SaveToDirectoryAsync(string outputDirectory, CancellationToken cancellationToken = default);
}
