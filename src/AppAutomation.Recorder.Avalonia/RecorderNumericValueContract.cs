using AppAutomation.Abstractions;

namespace AppAutomation.Recorder.Avalonia;

internal static class RecorderNumericValueContract
{
    public static bool TryCreateNumericTextSnapshot(
        RecorderSemanticValueSnapshot snapshot,
        out RecorderSemanticValueSnapshot numericSnapshot)
    {
        var prototype = snapshot.Prototype;
        if (prototype.Control.ControlType == UiControlType.TextBox
            && prototype.ValueKind == RecorderValueKind.Text
            && prototype.ValueAccessorKind == RecorderValueAccessorKind.Text
            && NumericDisplayValueReader.TryParse(prototype.StringValue, out var value, out _))
        {
            numericSnapshot = new RecorderSemanticValueSnapshot(
                prototype with
                {
                    ValueKind = RecorderValueKind.Number,
                    ValueAccessorKind = RecorderValueAccessorKind.NumericText,
                    DoubleValue = value,
                    StringValue = null
                },
                snapshot.Description with { ValueKind = RecorderValueKind.Number });
            return true;
        }

        numericSnapshot = snapshot;
        return false;
    }

    public static bool TryCreateGridReference(
        RecordedControlDescriptor control,
        RecorderValueKind valueKind,
        RecorderValueAccessorKind accessorKind,
        IReadOnlyList<RecordedGridRowCondition>? gridRowConditions,
        string? gridTargetColumnName,
        out RecorderGridValueReference? gridValueReference,
        out string error,
        string? repeatedItemKey = null)
    {
        ArgumentNullException.ThrowIfNull(control);

        gridValueReference = null;
        if (valueKind != RecorderValueKind.Number)
        {
            error = "Selected value does not expose a numeric value.";
            return false;
        }

        if (accessorKind == RecorderValueAccessorKind.NumericText)
        {
            if (control.ControlType != UiControlType.TextBox
                || repeatedItemKey is not null
                || gridRowConditions is { Count: > 0 }
                || !string.IsNullOrWhiteSpace(gridTargetColumnName))
            {
                error = "A displayed numeric text value requires a standalone TextBox control.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        if (accessorKind == RecorderValueAccessorKind.NumericValue)
        {
            if (control.ControlType == UiControlType.MultiItemControlCollection
                && string.IsNullOrWhiteSpace(repeatedItemKey))
            {
                error = "A multi-item Spinner numeric value requires a stable item key.";
                return false;
            }

            if (control.ControlType != UiControlType.MultiItemControlCollection
                && repeatedItemKey is not null)
            {
                error = "A standalone numeric value cannot define a multi-item key.";
                return false;
            }

            if (gridRowConditions is { Count: > 0 }
                || !string.IsNullOrWhiteSpace(gridTargetColumnName))
            {
                error = "A standalone numeric value cannot define a grid-cell address.";
                return false;
            }

            if (!Enum.IsDefined(control.ControlType))
            {
                error = $"Numeric control has an unsupported control type '{control.ControlType}'.";
                return false;
            }

            var capability = RecorderAssertionCapabilities.Get(control.ControlType);
            if (!capability.ValueKinds.Contains(RecorderValueKind.Number)
                || !capability.AccessorKinds.Contains(RecorderValueAccessorKind.NumericValue))
            {
                error = "Selected control does not expose a numeric value.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        if (accessorKind != RecorderValueAccessorKind.GridCellValue)
        {
            error = "Selected value does not expose a numeric accessor.";
            return false;
        }

        if (repeatedItemKey is not null)
        {
            error = "A numeric grid-cell value cannot define a multi-item key.";
            return false;
        }

        if (control.ControlType != UiControlType.Grid)
        {
            error = "A numeric grid-cell value must reference a logical Grid control.";
            return false;
        }

        if (gridRowConditions is not { Count: > 0 }
            || string.IsNullOrWhiteSpace(gridTargetColumnName)
            || gridRowConditions.Any(static condition => string.IsNullOrWhiteSpace(condition.ColumnName)))
        {
            error = "A numeric grid-cell value must define a stable row selector and a logical target column.";
            return false;
        }

        var normalizedConditions = gridRowConditions
            .Select(static condition => new RecordedGridRowCondition(
                condition.ColumnName.Trim(),
                condition.Value ?? string.Empty))
            .ToArray();
        var duplicateColumn = normalizedConditions
            .GroupBy(static condition => condition.ColumnName, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateColumn is not null)
        {
            error = $"Numeric grid-cell row selector contains duplicate column '{duplicateColumn.Key}'.";
            return false;
        }

        gridValueReference = new RecorderGridValueReference(
            normalizedConditions,
            gridTargetColumnName.Trim());
        error = string.Empty;
        return true;
    }
}
