namespace AppAutomation.Recorder.Avalonia;

public sealed record RecorderSaveResult(
    bool Success,
    string Message,
    string? PageFilePath,
    string? ScenarioFilePath,
    IReadOnlyList<string> Diagnostics,
    int PersistedStepCount,
    int SkippedStepCount)
{
    public static RecorderSaveResult Failed(string message, params string[] diagnostics)
    {
        return new RecorderSaveResult(false, message, null, null, diagnostics, 0, 0);
    }

    public static RecorderSaveResult Completed(
        string message,
        string? pageFilePath,
        string? scenarioFilePath,
        int persistedStepCount,
        int skippedStepCount,
        IReadOnlyList<string>? diagnostics = null)
    {
        return new RecorderSaveResult(
            true,
            message,
            pageFilePath,
            scenarioFilePath,
            diagnostics ?? Array.Empty<string>(),
            persistedStepCount,
            skippedStepCount);
    }
}
