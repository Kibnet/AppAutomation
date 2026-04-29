using Microsoft.Extensions.Logging;

namespace AppAutomation.Recorder.Avalonia;

internal static class RecorderDiagnosticsEventIds
{
    public static readonly EventId CaptureFailed = new(4101, nameof(CaptureFailed));

    public static readonly EventId SelectorValidationFailed = new(4102, nameof(SelectorValidationFailed));

    public static readonly EventId ActionValidationFailed = new(4103, nameof(ActionValidationFailed));

    public static readonly EventId RuntimeValidationFailed = new(4104, nameof(RuntimeValidationFailed));

    public static readonly EventId RuntimeValidationWarning = new(4105, nameof(RuntimeValidationWarning));

    public static readonly EventId DiagnosticsSnapshotFailed = new(4106, nameof(DiagnosticsSnapshotFailed));

    public static readonly EventId CommandHandled = new(4107, nameof(CommandHandled));

    public static readonly EventId SaveFailed = new(4108, nameof(SaveFailed));
}
