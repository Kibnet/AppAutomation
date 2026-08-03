using System.Globalization;
using System.Linq.Expressions;

namespace AppAutomation.Abstractions;

public static partial class UiPageExtensions
{
    /// <summary>
    /// Waits until the grid contains at least one row matching the stable selector.
    /// </summary>
    public static TSelf WaitUntilGridContainsRow<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(rowSelector);
        var grid = Resolve(selector, page);

        WaitUntil(
            page,
            selector,
            () => FindMatchingRowIndexes(grid, rowSelector).Count > 0,
            timeoutMs,
            $"Grid '{grid.AutomationId}' did not contain a row matching the stable selector.",
            expectedValue: DescribeRowSelector(rowSelector),
            lastObservedValueFactory: () => DescribeRowMatches(grid, rowSelector));
        return page;
    }

    /// <summary>
    /// Waits until a named cell in the uniquely selected row equals the expected value.
    /// </summary>
    public static TSelf WaitUntilGridCellEquals<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        string columnName,
        string expectedValue,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(rowSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentNullException.ThrowIfNull(expectedValue);

        var grid = Resolve(selector, page);
        var columnIndex = ResolveColumnIndex(grid, columnName);
        WaitUntil(
            page,
            selector,
            () => TryResolveUniqueRowIndex(grid, rowSelector, out var rowIndex)
                && string.Equals(TryReadGridCellValue(grid, rowIndex, columnIndex), expectedValue, StringComparison.Ordinal),
            timeoutMs,
            $"Grid '{grid.AutomationId}' named cell '{columnName}' did not reach expected value.",
            expectedValue: expectedValue,
            lastObservedValueFactory: () => TryReadNamedGridCellValue(grid, rowSelector, columnIndex));
        return page;
    }

    /// <summary>
    /// Opens the uniquely selected row using its current runtime index.
    /// </summary>
    public static TSelf OpenGridRow<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var rowIndex = WaitForUniqueRowIndex(page, selector, rowSelector, timeoutMs, nameof(OpenGridRow));
        return OpenGridRow(page, selector, rowIndex, timeoutMs);
    }

    /// <summary>
    /// Copies a named cell from the uniquely selected row.
    /// </summary>
    public static TSelf CopyGridCell<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        string columnName,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var grid = Resolve(selector, page);
        var columnIndex = ResolveColumnIndex(grid, columnName);
        var rowIndex = WaitForUniqueRowIndex(page, selector, rowSelector, timeoutMs, nameof(CopyGridCell));
        return CopyGridCell(page, selector, rowIndex, columnIndex, timeoutMs);
    }

    /// <summary>
    /// Edits a named cell in the uniquely selected row.
    /// </summary>
    public static TSelf EditGridCell<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        string columnName,
        string value,
        GridCellEditorKind editorKind = GridCellEditorKind.Text,
        GridCellEditCommitMode commitMode = GridCellEditCommitMode.Commit,
        string? searchText = null,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(value);
        var grid = Resolve(selector, page);
        var columnIndex = ResolveColumnIndex(grid, columnName);
        var rowIndex = WaitForUniqueRowIndex(page, selector, rowSelector, timeoutMs, nameof(EditGridCell));
        var request = new GridCellEditRequest(
            rowIndex,
            columnIndex,
            value,
            editorKind,
            commitMode,
            searchText)
        {
            TimeoutMs = timeoutMs
        };
        return ExecuteGridCellEdit(
            page,
            selector,
            request,
            timeoutMs,
            nameof(EditGridCell),
            candidate => TryReadNamedGridCellValue(candidate, rowSelector, columnIndex));
    }

    /// <summary>
    /// Edits a named cell with a text value in the uniquely selected row.
    /// </summary>
    public static TSelf EditGridCellText<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        string columnName,
        string value,
        GridCellEditCommitMode commitMode = GridCellEditCommitMode.Commit,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        return EditGridCell(page, selector, rowSelector, columnName, value, GridCellEditorKind.Text, commitMode, timeoutMs: timeoutMs);
    }

    /// <summary>
    /// Edits a named cell with an invariant-culture numeric value in the uniquely selected row.
    /// </summary>
    public static TSelf EditGridCellNumber<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        string columnName,
        double value,
        GridCellEditCommitMode commitMode = GridCellEditCommitMode.Commit,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        return EditGridCell(
            page,
            selector,
            rowSelector,
            columnName,
            value.ToString("G17", CultureInfo.InvariantCulture),
            GridCellEditorKind.Number,
            commitMode,
            timeoutMs: timeoutMs);
    }

    /// <summary>
    /// Edits a named cell with a date value in the uniquely selected row.
    /// </summary>
    public static TSelf EditGridCellDate<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        string columnName,
        DateTime value,
        GridCellEditCommitMode commitMode = GridCellEditCommitMode.Commit,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        return EditGridCell(
            page,
            selector,
            rowSelector,
            columnName,
            value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            GridCellEditorKind.Date,
            commitMode,
            timeoutMs: timeoutMs);
    }

    /// <summary>
    /// Selects a combo-box item in a named cell of the uniquely selected row.
    /// </summary>
    public static TSelf SelectGridCellComboItem<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        string columnName,
        string itemText,
        GridCellEditCommitMode commitMode = GridCellEditCommitMode.Commit,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemText);
        return EditGridCell(page, selector, rowSelector, columnName, itemText, GridCellEditorKind.ComboBox, commitMode, timeoutMs: timeoutMs);
    }

    /// <summary>
    /// Searches and selects an item in a named grid-cell search picker.
    /// </summary>
    public static TSelf SearchAndSelectGridCell<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        string columnName,
        string searchText,
        string itemText,
        GridCellEditCommitMode commitMode = GridCellEditCommitMode.Commit,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchText);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemText);
        return EditGridCell(
            page,
            selector,
            rowSelector,
            columnName,
            itemText,
            GridCellEditorKind.SearchPicker,
            commitMode,
            searchText,
            timeoutMs);
    }

    private static int WaitForUniqueRowIndex<TSelf>(
        TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        int timeoutMs,
        string operationName)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(rowSelector);
        var grid = Resolve(selector, page);
        var rowIndex = -1;
        WaitUntil(
            page,
            selector,
            () => TryResolveUniqueRowIndex(grid, rowSelector, out rowIndex),
            timeoutMs,
            $"Grid '{grid.AutomationId}' did not contain exactly one row matching the stable selector.",
            expectedValue: DescribeRowSelector(rowSelector),
            lastObservedValueFactory: () => DescribeRowMatches(grid, rowSelector),
            operationName);
        return rowIndex;
    }

    private static bool TryResolveUniqueRowIndex(IGridControl grid, GridRowSelector rowSelector, out int rowIndex)
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

    private static List<int> FindMatchingRowIndexes(IGridControl grid, GridRowSelector rowSelector)
    {
        var metadata = RequireColumnMetadata(grid);
        var conditions = rowSelector.Conditions
            .Select(condition => (ColumnIndex: ResolveColumnIndex(metadata, condition.ColumnName), condition.Value))
            .ToArray();
        var matches = new List<int>();
        var rows = grid.Rows;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var cells = rows[rowIndex].Cells;
            if (conditions.All(condition => condition.ColumnIndex < cells.Count
                    && string.Equals(cells[condition.ColumnIndex].Value, condition.Value, StringComparison.Ordinal)))
            {
                matches.Add(rowIndex);
            }
        }

        return matches;
    }

    private static int ResolveColumnIndex(IGridControl grid, string columnName)
    {
        return ResolveColumnIndex(RequireColumnMetadata(grid), columnName);
    }

    private static int ResolveColumnIndex(IGridColumnMetadataControl metadata, string columnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        if (!metadata.TryGetColumnIndex(columnName, out var columnIndex))
        {
            throw new InvalidOperationException(
                $"Grid column '{columnName}' is not configured. Available columns: {string.Join(", ", metadata.ColumnNames)}.");
        }

        return columnIndex;
    }

    private static IGridColumnMetadataControl RequireColumnMetadata(IGridControl grid)
    {
        return grid as IGridColumnMetadataControl
            ?? throw new InvalidOperationException(
                $"Grid '{grid.AutomationId}' does not expose column metadata. Register it with WithGridColumns.");
    }

    private static string DescribeRowSelector(GridRowSelector selector)
    {
        return string.Join(", ", selector.Conditions.Select(static condition => $"{condition.ColumnName}='{condition.Value}'"));
    }

    private static string DescribeRowMatches(IGridControl grid, GridRowSelector selector)
    {
        return $"matches={FindMatchingRowIndexes(grid, selector).Count}; rows={grid.Rows.Count}";
    }

    private static string? TryReadNamedGridCellValue(
        IGridControl grid,
        GridRowSelector rowSelector,
        int columnIndex)
    {
        var matches = FindMatchingRowIndexes(grid, rowSelector);
        return matches.Count switch
        {
            0 => $"<missing row; rows={grid.Rows.Count}>",
            1 => TryReadGridCellValue(grid, matches[0], columnIndex),
            _ => $"<ambiguous row; matches={matches.Count}>"
        };
    }
}
