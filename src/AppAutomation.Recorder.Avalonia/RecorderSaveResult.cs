namespace AppAutomation.Recorder.Avalonia;

public sealed record RecorderSaveResult(
    bool Success,
    string Message,
    string? PageFilePath,
    string? ScenarioFilePath,
    IReadOnlyList<string> Diagnostics)
{
    public static RecorderSaveResult Failed(string message, params string[] diagnostics)
    {
        return new RecorderSaveResult(false, message, null, null, diagnostics);
    }

    public static RecorderSaveResult Completed(
        string message,
        string? pageFilePath,
        string? scenarioFilePath,
        IReadOnlyList<string>? diagnostics = null)
    {
        return new RecorderSaveResult(
            true,
            message,
            pageFilePath,
            scenarioFilePath,
            diagnostics ?? Array.Empty<string>());
    }
}
