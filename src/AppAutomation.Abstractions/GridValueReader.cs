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

        return GridRuntimeResolver.ReadCellSnapshot(grid, row.Cells[columnIndex], columnIndex).DisplayText
            ?? string.Empty;
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

        var columnIndex = GridRuntimeResolver.ResolveColumnIndex(grid, columnName);
        if (!GridRuntimeResolver.TryResolveUniqueRowIndex(grid, rowSelector, out var rowIndex))
        {
            throw new InvalidOperationException("Grid row selector matched 0 rows; exactly one row is required.");
        }

        var row = grid.GetRowByIndex(rowIndex)
            ?? throw new InvalidOperationException($"Grid row {rowIndex} disappeared while reading '{columnName}'.");
        if (columnIndex >= row.Cells.Count)
        {
            throw new InvalidOperationException($"Grid column '{columnName}' does not exist in row {rowIndex}.");
        }

        return GridRuntimeResolver.ReadCellSnapshot(grid, row.Cells[columnIndex], columnIndex);
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

        return GridValueConversion.TryConvertDate(snapshot, out var value, out var diagnostic)
            ? value
            : throw new InvalidOperationException(diagnostic);
    }

    private static TimeSpan? ConvertTime(GridCellValueSnapshot snapshot)
    {
        if (snapshot.IsNull)
        {
            return null;
        }

        return GridValueConversion.TryConvertTime(snapshot, out var value, out var diagnostic)
            ? value
            : throw new InvalidOperationException(diagnostic);
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

        return TryConvertText(
            snapshot,
            "number",
            static (string text, System.Globalization.CultureInfo culture, out decimal parsed) =>
                decimal.TryParse(text, SupportedNumberStyles, culture, out parsed)
                || TryExtractNumberToken(text, culture, out parsed),
            out value,
            out diagnostic);
    }

    public static bool TryConvertDate(
        GridCellValueSnapshot snapshot,
        out DateTime value,
        out string? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        DateTime? typedValue = snapshot.RawValue switch
        {
            DateTime date => date.Date,
            DateTimeOffset date => date.Date,
            DateOnly date => date.ToDateTime(TimeOnly.MinValue),
            _ => null
        };
        if (typedValue.HasValue)
        {
            value = typedValue.Value;
            diagnostic = null;
            return true;
        }

        return TryConvertText(
            snapshot,
            "date",
            static (string text, System.Globalization.CultureInfo culture, out DateTime parsed) =>
            {
                var success = DateTime.TryParse(
                    text, culture, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out parsed);
                parsed = parsed.Date;
                return success;
            },
            out value,
            out diagnostic);
    }

    public static bool TryConvertTime(
        GridCellValueSnapshot snapshot,
        out TimeSpan value,
        out string? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        TimeSpan? typedValue = snapshot.RawValue switch
        {
            TimeSpan time => time,
            TimeOnly time => time.ToTimeSpan(),
            DateTime date => date.TimeOfDay,
            DateTimeOffset date => date.TimeOfDay,
            _ => null
        };
        if (typedValue.HasValue)
        {
            value = typedValue.Value;
            diagnostic = null;
            return true;
        }

        return TryConvertText(
            snapshot,
            "time",
            static (string text, System.Globalization.CultureInfo culture, out TimeSpan parsed) =>
                TimeSpan.TryParse(text, culture, out parsed),
            out value,
            out diagnostic);
    }

    private static bool TryConvertText<TValue>(
        GridCellValueSnapshot snapshot,
        string valueKind,
        TryParseValue<TValue> tryParse,
        out TValue value,
        out string? diagnostic)
        where TValue : struct
    {
        var text = snapshot.DisplayText;
        if (string.IsNullOrWhiteSpace(text))
        {
            value = default;
            diagnostic = $"Grid cell value '{text ?? "<null>"}' cannot be read as {valueKind}.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.CultureName))
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(snapshot.CultureName);
            if (tryParse(text, culture, out value))
            {
                diagnostic = null;
                return true;
            }

            diagnostic =
                $"Grid cell value '{text}' cannot be read as {valueKind} using configured culture '{culture.Name}'.";
            return false;
        }

        var parsedValues = CandidateCultures()
            .Select(culture =>
            {
                var parsed = tryParse(text, culture, out var candidate);
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
            ? $"Grid cell value '{text}' cannot be read as {valueKind} using the UI, current, or invariant culture."
            : $"Grid cell value '{text}' is culture-ambiguous and resolves to different {valueKind} values: "
                + string.Join(
                    ", ",
                    parsedValues.Select(static candidate =>
                        $"{(candidate.Name.Length == 0 ? "Invariant" : candidate.Name)}={candidate.Value}"))
                + ". Configure the grid column culture explicitly.";
        return false;
    }

    private delegate bool TryParseValue<TValue>(
        string text,
        System.Globalization.CultureInfo culture,
        out TValue value);

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

    private static bool TryExtractNumberToken(
        string text,
        System.Globalization.CultureInfo culture,
        out decimal value)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(
                text,
                @"[+-]?\d[\d\s\u00A0\u202F.,']*")
            .Select(static match => match.Value.Trim())
            .Where(static token => token.Length > 0)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            value = default;
            return false;
        }

        var groupSeparator = culture.NumberFormat.NumberGroupSeparator;
        var normalized = new string(matches[0]
                .SelectMany(character => char.IsWhiteSpace(character) || character is '\u00A0' or '\u202F' or '\''
                    ? groupSeparator
                    : character.ToString())
                .ToArray())
            .Trim();
        return decimal.TryParse(normalized, SupportedNumberStyles, culture, out value);
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
