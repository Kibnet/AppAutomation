using AppAutomation.Abstractions;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderStepFactory
{
    internal Control? FindMultipleSelectionGrid(Control? source) => _gridCapture.FindMultipleSelectionGrid(source);

    internal static bool IsMultipleSelectionRow(Control source, Control grid) =>
        RecorderGridCaptureResolver.IsMultipleSelectionRow(source, grid);

    internal StepCreationResult CreateGridRowSetStep(Control grid, IReadOnlyList<object?> selected)
    {
        var resolution = _gridCapture.CaptureRowSet(grid, selected);
        return resolution.Capture is { } capture
            ? CreateStep(grid, new RecordedStep(RecordedActionKind.SelectGridRows, capture.Control)
            {
                GridSelectedRows = capture.Rows
            })
            : StepCreationResult.Unsupported(resolution.Error!);
    }
}
