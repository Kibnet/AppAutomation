using AppAutomation.Abstractions;

namespace AppAutomation.Recorder.Avalonia;

/// <summary>Converts a grid cell's model and display value into the declared semantic kind.</summary>
internal static class RecorderGridValueConversion
{
    internal static GridCellValueKind InferGridCellValueKind(
        object? rawValue,
        GridCellEditorKind? editorKind)
    {
        if (rawValue is bool)
        {
            return GridCellValueKind.Boolean;
        }

        if (rawValue is byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal)
        {
            return GridCellValueKind.Number;
        }

        if (rawValue is DateOnly or DateTime or DateTimeOffset)
        {
            return GridCellValueKind.Date;
        }

        if (rawValue is TimeOnly or TimeSpan)
        {
            return GridCellValueKind.Time;
        }

        return editorKind switch
        {
            GridCellEditorKind.Number => GridCellValueKind.Number,
            GridCellEditorKind.Date => GridCellValueKind.Date,
            GridCellEditorKind.Time => GridCellValueKind.Time,
            GridCellEditorKind.CheckBox => GridCellValueKind.Boolean,
            GridCellEditorKind.ComboBox => GridCellValueKind.Selection,
            GridCellEditorKind.SearchPicker => GridCellValueKind.Reference,
            GridCellEditorKind.Color => GridCellValueKind.Color,
            _ => GridCellValueKind.Text
        };
    }

    internal static bool TryConvertGridNumber(
        object? rawValue,
        string displayText,
        GridColumnDefinition? column,
        out double? number)
    {
        if (rawValue is null && string.IsNullOrWhiteSpace(displayText))
        {
            number = null;
            return true;
        }

        try
        {
            if (rawValue is IConvertible convertible && rawValue is not string)
            {
                number = convertible.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
        }

        var culture = GetGridCulture(column);
        var success = double.TryParse(
            displayText,
            System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
            culture,
            out var parsed);
        number = success ? parsed : null;
        return success;
    }

    internal static bool TryConvertGridDate(
        object? rawValue,
        string displayText,
        GridColumnDefinition? column,
        out DateTime? date)
    {
        switch (rawValue)
        {
            case null when string.IsNullOrWhiteSpace(displayText):
                date = null;
                return true;
            case DateTime value:
                date = value.Date;
                return true;
            case DateTimeOffset value:
                date = value.Date;
                return true;
            case DateOnly value:
                date = value.ToDateTime(TimeOnly.MinValue);
                return true;
        }

        var success = DateTime.TryParse(
            displayText,
            GetGridCulture(column),
            System.Globalization.DateTimeStyles.AllowWhiteSpaces,
            out var parsed);
        date = success ? parsed.Date : null;
        return success;
    }

    internal static bool TryConvertGridTime(
        object? rawValue,
        string displayText,
        GridColumnDefinition? column,
        out TimeSpan? time)
    {
        switch (rawValue)
        {
            case null when string.IsNullOrWhiteSpace(displayText):
                time = null;
                return true;
            case TimeSpan value:
                time = value;
                return true;
            case TimeOnly value:
                time = value.ToTimeSpan();
                return true;
            case DateTime value:
                time = value.TimeOfDay;
                return true;
            case DateTimeOffset value:
                time = value.TimeOfDay;
                return true;
        }

        var success = TimeSpan.TryParse(displayText, GetGridCulture(column), out var parsed);
        time = success ? parsed : null;
        return success;
    }

    internal static bool TryConvertGridBoolean(
        object? rawValue,
        string displayText,
        out bool? boolean)
    {
        if (rawValue is null && string.IsNullOrWhiteSpace(displayText))
        {
            boolean = null;
            return true;
        }

        if (rawValue is bool value)
        {
            boolean = value;
            return true;
        }

        var success = bool.TryParse(displayText, out var parsed);
        boolean = success ? parsed : null;
        return success;
    }

    private static System.Globalization.CultureInfo GetGridCulture(GridColumnDefinition? column)
    {
        return string.IsNullOrWhiteSpace(column?.CultureName)
            ? System.Globalization.CultureInfo.InvariantCulture
            : System.Globalization.CultureInfo.GetCultureInfo(column.CultureName);
    }
}
