using System.Linq.Expressions;

namespace AppAutomation.Abstractions;

public static partial class UiPageExtensions
{
    /// <summary>Replaces and verifies the full selection of stable rows, regardless of their order.</summary>
    public static TSelf SelectGridRows<TSelf>(
        this TSelf page, Expression<Func<TSelf, IGridControl>> selector, IReadOnlyList<GridRowSelector> rows,
        int timeoutMs = 5000) where TSelf : UiPage =>
        ExecuteGridRowSet(page, selector, rows, timeoutMs, replace: true);

    /// <summary>Verifies the full selection without selecting or deselecting any rows.</summary>
    public static TSelf WaitUntilSelectedGridRowsEqual<TSelf>(
        this TSelf page, Expression<Func<TSelf, IGridControl>> selector, IReadOnlyList<GridRowSelector> rows,
        int timeoutMs = 5000) where TSelf : UiPage =>
        ExecuteGridRowSet(page, selector, rows, timeoutMs, replace: false);

    private static TSelf ExecuteGridRowSet<TSelf>(
        TSelf page, Expression<Func<TSelf, IGridControl>> selector, IReadOnlyList<GridRowSelector> rows,
        int timeoutMs, bool replace) where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(rows);
        var operation = replace ? nameof(SelectGridRows) : nameof(WaitUntilSelectedGridRowsEqual);
        var budget = UiOperationTimeoutBudget.Start(timeoutMs, operation);
        var grid = Resolve(selector, page);
        if (grid is not IGridRowSetSelectionControl selection)
            throw new NotSupportedException($"Grid '{grid.AutomationId}' does not expose complete row selection.");
        var expected = GridRowSelectionSet.Index(rows, "Requested grid selection");
        if (replace)
            selection.SetSelectedRows(rows, budget.RemainingMilliseconds);
        else if (selection is ConfiguredGridControl configured)
            configured.ValidateRowSetSelectors(rows);
        GridRowSelectionSet.WaitForExactSelection(selection, expected, budget);
        return page;
    }
}
