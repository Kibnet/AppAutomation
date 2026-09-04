using System.Globalization;

namespace AppAutomation.Abstractions;

internal static class GridCellValueNormalizer
{
    public static GridCellValueSnapshot Create(
        string displayText,
        GridColumnDefinition column)
    {
        var culture = string.IsNullOrWhiteSpace(column.CultureName)
            ? CultureInfo.CurrentUICulture
            : CultureInfo.GetCultureInfo(column.CultureName);
        var kind = column.ValueKind ?? InferValueKind(column.EditorKind);
        object? rawValue = displayText;
        switch (kind)
        {
            case GridCellValueKind.Date
                when DateTime.TryParse(displayText, culture, DateTimeStyles.AllowWhiteSpaces, out var date):
                rawValue = date;
                break;
            case GridCellValueKind.Time
                when TimeSpan.TryParse(displayText, culture, out var time):
                rawValue = time;
                break;
            case GridCellValueKind.Boolean
                when bool.TryParse(displayText, out var boolean):
                rawValue = boolean;
                break;
        }

        var snapshot = new GridCellValueSnapshot(displayText, rawValue, kind)
        {
            CultureName = column.CultureName
        };
        if (kind == GridCellValueKind.Number
            && GridValueConversion.TryConvertNumber(snapshot, out var number, out _))
        {
            snapshot = snapshot with { RawValue = number };
        }

        return snapshot;
    }

    public static GridCellValueSnapshot Normalize(
        string gridPropertyName,
        GridCellValueSnapshot snapshot,
        GridColumnDefinition column)
    {
        if (snapshot.IsNull)
        {
            return new GridCellValueSnapshot(
                DisplayText: null,
                RawValue: null,
                column.ValueKind ?? InferValueKind(column.EditorKind))
            {
                CultureName = column.CultureName
            };
        }

        var projectedValue = ResolveProjectedValue(gridPropertyName, snapshot, column);
        var displayText = FormatProjectedValue(projectedValue, snapshot.DisplayText, column);
        var normalized = Create(displayText ?? string.Empty, column);
        return normalized with
        {
            RawValue = projectedValue is null or string
                ? normalized.RawValue
                : projectedValue,
            ValueSource = snapshot.ValueSource
        };
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

        if (string.IsNullOrWhiteSpace(column.FormatString))
        {
            return string.IsNullOrWhiteSpace(column.DisplayValuePath)
                ? providerDisplayText ?? Convert.ToString(value, CultureInfo.InvariantCulture)
                : Convert.ToString(value, ResolveCulture(column));
        }

        return value is IFormattable formattable
            ? formattable.ToString(column.FormatString, ResolveCulture(column))
            : Convert.ToString(value, ResolveCulture(column));
    }

    private static CultureInfo ResolveCulture(GridColumnDefinition column) =>
        string.IsNullOrWhiteSpace(column.CultureName)
            ? CultureInfo.InvariantCulture
            : CultureInfo.GetCultureInfo(column.CultureName);
}
