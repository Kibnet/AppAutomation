using System.Text;
using System.Text.Json;

namespace AppAutomation.Recorder.Avalonia.CodeGeneration;

internal static class RecorderAutosaveStateSerializer
{
    internal const string MarkerPrefix = "// AppAutomation recorder autosave state: ";
    private const int HeaderLineLimit = 8;
    private const int MaximumPayloadLength = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public static string CreateMarker(RecorderAutosaveState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        return MarkerPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static bool TryRead(
        string filePath,
        out RecorderAutosaveState? state,
        out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        state = null;
        error = null;
        string? marker;
        try
        {
            marker = File.ReadLines(filePath)
                .Take(HeaderLineLimit)
                .FirstOrDefault(static line => line.StartsWith(MarkerPrefix, StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"Recorder autosave recovery file '{filePath}' could not be read: {exception.Message}";
            return false;
        }

        if (marker is null)
        {
            return false;
        }

        var payload = marker[MarkerPrefix.Length..];
        if (payload.Length == 0 || payload.Length > MaximumPayloadLength)
        {
            error = $"Recorder autosave recovery file '{filePath}' contains an invalid recovery payload.";
            return false;
        }

        try
        {
            state = JsonSerializer.Deserialize<RecorderAutosaveState>(
                Convert.FromBase64String(payload),
                JsonOptions);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or NotSupportedException)
        {
            error = $"Recorder autosave recovery file '{filePath}' contains an invalid recovery payload: {exception.Message}";
            return false;
        }

        if (state is null || state.Steps.Count == 0)
        {
            state = null;
            error = $"Recorder autosave recovery file '{filePath}' contains no recorded steps.";
            return false;
        }

        return true;
    }
}

internal sealed record RecorderAutosaveState(
    string ScenarioName,
    string DraftIdentity,
    string MethodName,
    DateTimeOffset SavedAtUtc,
    IReadOnlyList<RecordedStep> Steps);

internal sealed record RecorderAutosaveRestoreResult(
    bool Found,
    bool Success,
    string Message,
    string? DraftIdentity,
    IReadOnlyList<RecordedStep> Steps)
{
    public static RecorderAutosaveRestoreResult NotFound(string message) =>
        new(false, false, message, null, Array.Empty<RecordedStep>());

    public static RecorderAutosaveRestoreResult Failed(string message) =>
        new(true, false, message, null, Array.Empty<RecordedStep>());

    public static RecorderAutosaveRestoreResult Restored(
        string message,
        string draftIdentity,
        IReadOnlyList<RecordedStep> steps) =>
        new(true, true, message, draftIdentity, steps);
}
