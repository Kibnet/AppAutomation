namespace AppAutomation.Abstractions;

/// <summary>
/// Reads a displayed grid cell through provider-neutral row and column metadata.
/// </summary>
public static class GridValueReader
{
    /// <summary>Reads a displayed cell using zero-based row and column indexes.</summary>
    public static string ReadCellText(IGridControl grid, int rowIndex, int columnIndex)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);

        var row = grid.GetRowByIndex(rowIndex)
            ?? throw new InvalidOperationException(
                $"Grid row {rowIndex} does not exist. Current row count: {grid.Rows.Count}.");
        if (columnIndex >= row.Cells.Count)
        {
            throw new InvalidOperationException(
                $"Grid column index {columnIndex} does not exist in row {rowIndex}. Current cell count: {row.Cells.Count}.");
        }

        return row.Cells[columnIndex].Value;
    }

    /// <summary>Reads a displayed cell after re-resolving one stable row selector.</summary>
    public static string ReadCellText(
        IGridControl grid,
        GridRowSelector rowSelector,
        string columnName)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(rowSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

        if (grid is IAddressableGridControl addressableGrid)
        {
            return addressableGrid
                .ReadCell(new GridCellAddress(rowSelector, columnName), timeoutMs: 5000)
                .DisplayText
                ?? string.Empty;
        }

        var targetColumnIndex = GridRuntimeResolver.ResolveColumnIndex(grid, columnName);
        var matchingRows = GridRuntimeResolver.FindMatchingRowIndexes(grid, rowSelector);
        if (matchingRows.Count != 1)
        {
            throw new InvalidOperationException(
                $"Grid row selector matched {matchingRows.Count} rows; exactly one row is required.");
        }

        return ReadCellText(grid, matchingRows[0], targetColumnIndex);
    }

    /// <summary>Reads a typed, null-aware cell value through a stable address.</summary>
    public static GridCellValueSnapshot ReadCellValue(
        IGridControl grid,
        GridRowSelector rowSelector,
        string columnName,
        int timeoutMs = 5000)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(rowSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);

        if (grid is IAddressableGridControl addressableGrid)
        {
            return addressableGrid.ReadCell(new GridCellAddress(rowSelector, columnName), timeoutMs);
        }

        return new GridCellValueSnapshot(ReadCellText(grid, rowSelector, columnName));
    }

    /// <summary>Reads a nullable numeric cell value through a stable address.</summary>
    public static double? ReadCellNumber(
        IGridControl grid,
        GridRowSelector rowSelector,
        string columnName,
        int timeoutMs = 5000)
    {
        return ConvertNumber(ReadCellValue(grid, rowSelector, columnName, timeoutMs));
    }

    /// <summary>Reads a nullable date cell value through a stable address.</summary>
    public static DateTime? ReadCellDate(
        IGridControl grid,
        GridRowSelector rowSelector,
        string columnName,
        int timeoutMs = 5000)
    {
        return ConvertDate(ReadCellValue(grid, rowSelector, columnName, timeoutMs));
    }

    /// <summary>Reads a nullable time cell value through a stable address.</summary>
    public static TimeSpan? ReadCellTime(
        IGridControl grid,
        GridRowSelector rowSelector,
        string columnName,
        int timeoutMs = 5000)
    {
        return ConvertTime(ReadCellValue(grid, rowSelector, columnName, timeoutMs));
    }

    /// <summary>Reads a nullable boolean cell value through a stable address.</summary>
    public static bool? ReadCellBoolean(
        IGridControl grid,
        GridRowSelector rowSelector,
        string columnName,
        int timeoutMs = 5000)
    {
        return ConvertBoolean(ReadCellValue(grid, rowSelector, columnName, timeoutMs));
    }

    private static double? ConvertNumber(GridCellValueSnapshot snapshot)
    {
        if (snapshot.IsNull)
        {
            return null;
        }

        if (GridValueConversion.TryConvertNumber(snapshot, out var value, out var diagnostic))
        {
            return (double)value;
        }

        throw new InvalidOperationException(diagnostic ?? CreateConversionException(snapshot, "number").Message);
    }

    private static DateTime? ConvertDate(GridCellValueSnapshot snapshot)
    {
        if (snapshot.IsNull)
        {
            return null;
        }

        return snapshot.RawValue switch
        {
            DateTime value => value.Date,
            DateTimeOffset value => value.Date,
            DateOnly value => value.ToDateTime(TimeOnly.MinValue),
            _ when DateTime.TryParse(
                snapshot.DisplayText,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out var parsed) => parsed.Date,
            _ when DateTime.TryParse(
                snapshot.DisplayText,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out var parsed) => parsed.Date,
            _ => throw CreateConversionException(snapshot, "date")
        };
    }

    private static TimeSpan? ConvertTime(GridCellValueSnapshot snapshot)
    {
        if (snapshot.IsNull)
        {
            return null;
        }

        return snapshot.RawValue switch
        {
            TimeSpan value => value,
            TimeOnly value => value.ToTimeSpan(),
            DateTime value => value.TimeOfDay,
            DateTimeOffset value => value.TimeOfDay,
            _ when TimeSpan.TryParse(
                snapshot.DisplayText,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ when TimeSpan.TryParse(
                snapshot.DisplayText,
                System.Globalization.CultureInfo.CurrentCulture,
                out var parsed) => parsed,
            _ => throw CreateConversionException(snapshot, "time")
        };
    }

    private static bool? ConvertBoolean(GridCellValueSnapshot snapshot)
    {
        if (snapshot.IsNull)
        {
            return null;
        }

        return snapshot.RawValue switch
        {
            bool value => value,
            _ when bool.TryParse(snapshot.DisplayText, out var parsed) => parsed,
            _ => throw CreateConversionException(snapshot, "boolean")
        };
    }

    private static InvalidOperationException CreateConversionException(
        GridCellValueSnapshot snapshot,
        string expectedKind)
    {
        return new InvalidOperationException(
            $"Grid cell value '{snapshot.DisplayText ?? "<null>"}' cannot be read as {expectedKind}.");
    }
}

internal static class GridValueConversion
{
    private const System.Globalization.NumberStyles SupportedNumberStyles =
        System.Globalization.NumberStyles.Number | System.Globalization.NumberStyles.AllowExponent;

    public static bool TryConvertNumber(
        GridCellValueSnapshot snapshot,
        out decimal value,
        out string? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (TryConvertTypedNumber(snapshot.RawValue, out value))
        {
            diagnostic = null;
            return true;
        }

        var text = snapshot.DisplayText;
        if (string.IsNullOrWhiteSpace(text))
        {
            value = default;
            diagnostic = $"Grid cell value '{text ?? "<null>"}' cannot be read as number.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.CultureName))
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(snapshot.CultureName);
            if (decimal.TryParse(text, SupportedNumberStyles, culture, out value))
            {
                diagnostic = null;
                return true;
            }

            diagnostic =
                $"Grid cell value '{text}' cannot be read as number using configured culture '{culture.Name}'.";
            return false;
        }

        var parsedValues = CandidateCultures()
            .Select(culture =>
            {
                var parsed = decimal.TryParse(text, SupportedNumberStyles, culture, out var candidate);
                return (culture.Name, Parsed: parsed, Value: candidate);
            })
            .Where(static candidate => candidate.Parsed)
            .ToArray();
        var distinctValues = parsedValues
            .Select(static candidate => candidate.Value)
            .Distinct()
            .ToArray();
        if (distinctValues.Length == 1)
        {
            value = distinctValues[0];
            diagnostic = null;
            return true;
        }

        value = default;
        diagnostic = distinctValues.Length == 0
            ? $"Grid cell value '{text}' cannot be read as number using the UI, current, or invariant culture."
            : $"Grid cell value '{text}' is culture-ambiguous and resolves to different numbers: "
                + string.Join(
                    ", ",
                    parsedValues.Select(static candidate =>
                        $"{(candidate.Name.Length == 0 ? "Invariant" : candidate.Name)}={candidate.Value}"))
                + ". Configure the grid column culture explicitly.";
        return false;
    }

    private static bool TryConvertTypedNumber(object? rawValue, out decimal value)
    {
        switch (rawValue)
        {
            case byte number:
                value = number;
                return true;
            case sbyte number:
                value = number;
                return true;
            case short number:
                value = number;
                return true;
            case ushort number:
                value = number;
                return true;
            case int number:
                value = number;
                return true;
            case uint number:
                value = number;
                return true;
            case long number:
                value = number;
                return true;
            case ulong number:
                value = number;
                return true;
            case float number when float.IsFinite(number):
                value = (decimal)number;
                return true;
            case double number when double.IsFinite(number):
                value = (decimal)number;
                return true;
            case decimal number:
                value = number;
                return true;
            default:
                value = default;
                return false;
        }
    }

    private static IReadOnlyList<System.Globalization.CultureInfo> CandidateCultures()
    {
        return new[]
            {
                System.Globalization.CultureInfo.CurrentUICulture,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Globalization.CultureInfo.InvariantCulture
            }
            .DistinctBy(static culture => culture.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
