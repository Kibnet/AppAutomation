using System.Globalization;

namespace AppAutomation.Abstractions;

internal static class GridCellEditRequestValidation
{
    public static void Validate(GridCellEditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.RowIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ColumnIndex);
        Validate(request.Value, request.EditorKind, request.NumericInput);
    }

    public static void Validate(GridCellValueEditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.Value, request.EditorKind, request.NumericInput);
    }

    public static GridCellEditRequest CreateIndexedRequest(
        int rowIndex,
        int columnIndex,
        GridCellValueEditRequest request,
        int timeoutMs,
        GridCellEditorParts? editorParts = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        Validate(request);
        return new GridCellEditRequest(
            rowIndex,
            columnIndex,
            request.Value,
            request.EditorKind,
            request.CommitMode,
            request.SearchText)
        {
            TimeoutMs = timeoutMs,
            EditorParts = editorParts ?? request.EditorParts,
            NumericInput = request.NumericInput
        };
    }

    public static void Validate(
        string value,
        GridCellEditorKind editorKind,
        GridNumericInput? numericInput)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (numericInput is null)
        {
            return;
        }

        if (editorKind != GridCellEditorKind.Number)
        {
            throw new ArgumentException(
                "A physical numeric input can only be used with a Number grid editor.",
                nameof(numericInput));
        }

        if (!GridNumericText.TryParse(value, CultureInfo.InvariantCulture, out var canonicalValue)
            || !canonicalValue.Equals(numericInput.CanonicalValue))
        {
            throw new ArgumentException(
                $"Physical numeric input represents '{numericInput.CanonicalValue.ToString("G17", CultureInfo.InvariantCulture)}' "
                + $"but the request value is '{value}'.",
                nameof(numericInput));
        }
    }
}
