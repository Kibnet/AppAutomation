using AppAutomation.Abstractions;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

public interface IRecorderAssertionExtractor
{
    bool TryCreate(Control control, RecorderAssertionMode mode, out RecorderAssertionCandidate? candidate);
}

public sealed record RecorderAssertionCandidate(
    UiControlType ControlType,
    RecordedActionKind ActionKind,
    string? StringValue = null,
    bool? BoolValue = null,
    double? DoubleValue = null,
    DateTime? DateValue = null,
    string? Warning = null);
