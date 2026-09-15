using System.Globalization;

namespace AppAutomation.Abstractions;

internal static class GridCellValueNormalizer
{
    public static GridCellValueSnapshot Normalize(
        string gridPropertyName,
        GridCellValueSnapshot snapshot,
        GridColumnDefinition column)
    {
        var kind = column.ValueKind ?? (column.EditorKind.HasValue
            ? InferValueKind(column.EditorKind)
            : snapshot.ValueKind);
        var cultureName = column.CultureName ?? snapshot.CultureName;
        if (snapshot.IsDisplayOnly)
        {
            return NormalizeTypedValue(snapshot with { ValueKind = kind, CultureName = cultureName }, column);
        }

        if (snapshot.IsNull && string.IsNullOrWhiteSpace(column.DisplayValuePath))
        {
            return snapshot with
            {
                ValueKind = kind,
                CultureName = cultureName
            };
        }

        var projectedValue = ResolveProjectedValue(gridPropertyName, snapshot, column);
        if (projectedValue is null && !string.IsNullOrWhiteSpace(column.DisplayValuePath))
        {
            return snapshot with
            {
                RawValue = null,
                DisplayText = null,
                ValueKind = kind,
                CultureName = cultureName
            };
        }

        var normalized = NormalizeTypedValue(snapshot with
        {
            DisplayText = string.IsNullOrWhiteSpace(column.DisplayValuePath)
                ? snapshot.DisplayText
                : Convert.ToString(projectedValue, ResolveCulture(column)),
            RawValue = projectedValue,
            ValueKind = kind,
            CultureName = cultureName
        }, column);
        return normalized with
        {
            DisplayText = FormatProjectedValue(normalized.RawValue, snapshot.DisplayText, column)
        };
    }

    public static GridCellValueSnapshot Normalize(
        string gridPropertyName,
        GridCellValueSnapshot snapshot,
        GridRuntimeColumn column)
    {
        var definition = GridColumnDefinition.Auto(column.SourceFieldName).AsValue(column.ValueKind);
        if (!string.IsNullOrWhiteSpace(column.DisplayValuePath))
        {
            definition = definition.DisplayValueFrom(column.DisplayValuePath);
        }

        if (!string.IsNullOrWhiteSpace(column.FormatString))
        {
            definition = definition.FormatWith(column.FormatString, column.CultureName);
        }

        if (column.BooleanTrueDisplayText is not null
            && column.BooleanFalseDisplayText is not null)
        {
            definition = definition.WithBooleanDisplayText(
                column.BooleanTrueDisplayText,
                column.BooleanFalseDisplayText);
        }

        return Normalize(
            gridPropertyName,
            snapshot with { CultureName = column.CultureName ?? snapshot.CultureName },
            definition);
    }

    private static GridCellValueSnapshot NormalizeTypedValue(
        GridCellValueSnapshot snapshot,
        GridColumnDefinition column)
    {
        var rawValue = snapshot.ValueKind switch
        {
            GridCellValueKind.Number when GridValueConversion.TryConvertNumber(snapshot, out var number, out _) => number,
            GridCellValueKind.Date when GridValueConversion.TryConvertDate(snapshot, out var date, out _) => date,
            GridCellValueKind.Time when GridValueConversion.TryConvertTime(snapshot, out var time, out _) => time,
            GridCellValueKind.Boolean when snapshot.RawValue is bool value => value,
            GridCellValueKind.Boolean when snapshot.DisplayText is { } text
                && TryParseConfiguredBoolean(text, column, out var configuredBoolean) => configuredBoolean,
            GridCellValueKind.Boolean when bool.TryParse(snapshot.DisplayText, out var boolean) => boolean,
            _ => snapshot.RawValue
        };
        return snapshot with { RawValue = rawValue };
    }

    public static GridCellValueKind InferValueKind(GridCellEditorKind? editorKind)
    {
        return editorKind switch
        {
            GridCellEditorKind.Number => GridCellValueKind.Number,
            GridCellEditorKind.Date => GridCellValueKind.Date,
            GridCellEditorKind.Time => GridCellValueKind.Time,
            GridCellEditorKind.ComboBox or GridCellEditorKind.SearchPicker => GridCellValueKind.Selection,
            GridCellEditorKind.CheckBox => GridCellValueKind.Boolean,
            GridCellEditorKind.Color => GridCellValueKind.Color,
            _ => GridCellValueKind.Text
        };
    }

    private static object? ResolveProjectedValue(
        string gridPropertyName,
        GridCellValueSnapshot snapshot,
        GridColumnDefinition column)
    {
        if (string.IsNullOrWhiteSpace(column.DisplayValuePath))
        {
            return snapshot.RawValue;
        }

        var source = snapshot.ValueSource ?? snapshot.RawValue;
        if (source is null)
        {
            throw new InvalidOperationException(
                $"Grid '{gridPropertyName}' column '{column.LogicalName}' cannot resolve configured display path "
                + $"'{column.DisplayValuePath}' because the provider did not expose a value source.");
        }

        if (!GridPropertyValueReader.TryReadPath(
                source,
                column.DisplayValuePath,
                out var projected,
                out var unresolvedSegment))
        {
            throw new InvalidOperationException(
                $"Grid '{gridPropertyName}' column '{column.LogicalName}' could not resolve configured display path "
                + $"'{column.DisplayValuePath}' on '{source.GetType().FullName}'. Missing or unreadable segment: "
                + $"'{unresolvedSegment ?? "<unknown>"}'.");
        }

        return projected;
    }

    private static string? FormatProjectedValue(
        object? value,
        string? providerDisplayText,
        GridColumnDefinition column)
    {
        if (value is null)
        {
            return providerDisplayText;
        }

        if (value is bool boolean
            && column.BooleanTrueDisplayText is not null
            && column.BooleanFalseDisplayText is not null)
        {
            return boolean
                ? column.BooleanTrueDisplayText
                : column.BooleanFalseDisplayText;
        }

        if (string.IsNullOrWhiteSpace(column.FormatString))
        {
            return string.IsNullOrWhiteSpace(column.DisplayValuePath)
                ? providerDisplayText ?? Convert.ToString(value, CultureInfo.InvariantCulture)
                : FormatValue(value, column);
        }

        return FormatValue(value, column);
    }

    public static string? FormatValue(object? value, GridColumnDefinition? column) =>
        GridCellDisplayFormatter.Format(value, column?.FormatString, column?.CultureName);

    private static CultureInfo ResolveCulture(GridColumnDefinition? column) =>
        string.IsNullOrWhiteSpace(column?.CultureName)
            ? CultureInfo.InvariantCulture
            : CultureInfo.GetCultureInfo(column.CultureName);

    private static bool TryParseConfiguredBoolean(
        string displayText,
        GridColumnDefinition column,
        out bool value)
    {
        if (column.BooleanTrueDisplayText is not null
            && string.Equals(displayText, column.BooleanTrueDisplayText, StringComparison.Ordinal))
        {
            value = true;
            return true;
        }

        if (column.BooleanFalseDisplayText is not null
            && string.Equals(displayText, column.BooleanFalseDisplayText, StringComparison.Ordinal))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }
}
