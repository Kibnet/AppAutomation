namespace AppAutomation.Abstractions;

internal static class GridRuntimeResolver
{
    public static bool TryResolveUniqueRowIndex(
        IGridControl grid,
        GridRowSelector rowSelector,
        out int rowIndex)
    {
        var matches = FindMatchingRowIndexes(grid, rowSelector);
        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Grid row selector '{DescribeRowSelector(rowSelector)}' matched {matches.Count} rows; expected exactly one.");
        }

        rowIndex = matches.Count == 1 ? matches[0] : -1;
        return rowIndex >= 0;
    }

    public static List<int> FindMatchingRowIndexes(IGridControl grid, GridRowSelector rowSelector)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(rowSelector);

        var metadata = RequireColumnMetadata(grid);
        var conditions = rowSelector.Conditions
            .Select(condition => (ColumnIndex: ResolveColumnIndex(metadata, condition.ColumnName), Condition: condition))
            .ToArray();
        var matches = new List<int>();
        var rows = grid.Rows;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var cells = rows[rowIndex].Cells;
            if (conditions.All(condition => condition.ColumnIndex < cells.Count
                    && string.Equals(
                        ReadRowConditionText(
                            grid.AutomationId,
                            rowSelector,
                            condition.Condition,
                            ReadCellSnapshot(grid, cells[condition.ColumnIndex], condition.ColumnIndex)),
                        condition.Condition.Value,
                        StringComparison.Ordinal)))
            {
                matches.Add(rowIndex);
            }
        }

        return matches;
    }

    public static string? ReadRowConditionText(
        string gridName,
        GridRowSelector selector,
        GridRowCondition condition,
        GridCellValueSnapshot snapshot)
    {
        var column = selector.FindColumnDefinition(condition.ColumnName);
        return column is null
            ? snapshot.DisplayText
            : GridCellValueNormalizer.Normalize(gridName, snapshot, column).DisplayText;
    }

    public static GridCellValueSnapshot ReadCellSnapshot(
        IGridControl grid,
        IGridCellControl cell,
        int columnIndex)
    {
        var snapshot = cell is IGridCellValueControl semanticCell
            ? semanticCell.ValueSnapshot
            : new GridCellValueSnapshot(cell.Value, cell.Value) { IsDisplayOnly = true };
        return grid is ConfiguredGridControl configured
            ? configured.NormalizeValueSnapshot(snapshot, columnIndex)
            : snapshot;
    }

    public static int ResolveColumnIndex(IGridControl grid, string columnName)
    {
        return ResolveColumnIndex(RequireColumnMetadata(grid), columnName);
    }

    public static int ResolveColumnIndex(IGridColumnMetadataControl metadata, string columnName)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        if (!metadata.TryGetColumnIndex(columnName, out var columnIndex))
        {
            throw new InvalidOperationException(
                $"Grid column '{columnName}' is not configured. Available columns: {string.Join(", ", metadata.ColumnNames)}.");
        }

        return columnIndex;
    }

    public static IGridColumnMetadataControl RequireColumnMetadata(IGridControl grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return grid as IGridColumnMetadataControl
            ?? throw new InvalidOperationException(
                $"Grid '{grid.AutomationId}' does not expose column metadata. Register it with WithGridColumns.");
    }

    public static string DescribeRowSelector(GridRowSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return string.Join(", ", selector.Conditions.Select(static condition => $"{condition.ColumnName}='{condition.Value}'"));
    }
}
