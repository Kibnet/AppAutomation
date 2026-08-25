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

        var targetColumnIndex = GridRuntimeResolver.ResolveColumnIndex(grid, columnName);
        var matchingRows = GridRuntimeResolver.FindMatchingRowIndexes(grid, rowSelector);
        if (matchingRows.Count != 1)
        {
            throw new InvalidOperationException(
                $"Grid row selector matched {matchingRows.Count} rows; exactly one row is required.");
        }

        return ReadCellText(grid, matchingRows[0], targetColumnIndex);
    }
}
