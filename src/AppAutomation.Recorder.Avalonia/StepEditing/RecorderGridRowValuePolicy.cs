namespace AppAutomation.Recorder.Avalonia;

internal enum RecorderGridRowValueSourceStatus
{
    Valid,
    GridDerived,
    Empty,
    Mismatch
}

internal static class RecorderGridRowValuePolicy
{
    public static RecorderGridRowValueSourceStatus Validate(
        RecordedStep source,
        RecordedGridRowCondition condition)
    {
        if (source.GridRowConditions is { Count: > 0 }
            || source.RowIndex.HasValue
            || source.Control.ControlType == AppAutomation.Abstractions.UiControlType.Grid
            || source.ValueAccessorKind is RecorderValueAccessorKind.GridCellText
                or RecorderValueAccessorKind.GridCellValue)
        {
            return RecorderGridRowValueSourceStatus.GridDerived;
        }

        if (string.IsNullOrWhiteSpace(source.StringValue))
        {
            return RecorderGridRowValueSourceStatus.Empty;
        }

        return string.Equals(source.StringValue, condition.Value, StringComparison.Ordinal)
            ? RecorderGridRowValueSourceStatus.Valid
            : RecorderGridRowValueSourceStatus.Mismatch;
    }
}
