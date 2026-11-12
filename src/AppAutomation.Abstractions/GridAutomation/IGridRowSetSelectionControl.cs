using System.Globalization;
using System.Text;

namespace AppAutomation.Abstractions;

/// <summary>Optional grid capability for replacing and reading the complete selected row set.</summary>
public interface IGridRowSetSelectionControl : IGridControl
{
    /// <summary>Replaces the selection with the specified stable rows.</summary>
    void SetSelectedRows(IReadOnlyList<GridRowSelector> rows, int timeoutMs);

    /// <summary>Reads every selected row, including those outside the viewport; fails if the complete set is unavailable.</summary>
    IReadOnlyList<GridRowSelector> ReadSelectedRows(int timeoutMs);
}

internal interface IGridRowSetRuntime
{
    void SetSelectedRows(GridRowSetIdentity identity, IReadOnlyDictionary<string, GridRowSelector> expected,
        UiOperationTimeoutBudget budget);

    IReadOnlyList<GridRowSelector> ReadSelectedRows(GridRowSetIdentity identity, UiOperationTimeoutBudget budget);
}

internal sealed record GridRowSetIdentity(
    IReadOnlyList<string> LogicalColumns, IReadOnlyList<GridRuntimeColumn> RuntimeColumns, string RowPath)
{
    internal GridRowSelector Read(Func<GridRuntimeColumn, string?> read) =>
        ReadCore(read, requireComplete: true)!;

    internal GridRowSelector? TryRead(Func<GridRuntimeColumn, string?> read) =>
        ReadCore(read, requireComplete: false);

    private GridRowSelector? ReadCore(Func<GridRuntimeColumn, string?> read, bool requireComplete)
    {
        GridRowSelector? selector = null;
        for (var index = 0; index < LogicalColumns.Count; index++)
        {
            var name = LogicalColumns[index];
            var value = read(RuntimeColumns[index]);
            if (value is null)
            {
                if (!requireComplete) return null;
                throw new InvalidOperationException($"A selected grid row does not expose stable identity column '{name}'.");
            }
            selector = selector is null ? GridRowSelector.ByCell(name, value) : selector.AndCell(name, value);
        }
        return selector;
    }

    internal GridIndexedRowSelector ToRuntimeSelector(GridRowSelector selector)
    {
        var values = selector.Conditions.ToDictionary(static part => part.ColumnName, static part => part.Value, StringComparer.Ordinal);
        return new GridIndexedRowSelector(RuntimeColumns.Select((column, index) =>
            new GridIndexedCellCondition(column, values[LogicalColumns[index]]))).WithDeclaredUniqueIdentity();
    }
}

internal static class GridRowSelectionSet
{
    internal static string Key(GridRowSelector row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Key(row.Conditions);
    }

    internal static string Key(IEnumerable<GridRowCondition> conditions)
    {
        var key = new StringBuilder();
        foreach (var condition in conditions.OrderBy(static part => part.ColumnName, StringComparer.Ordinal))
        {
            Append(condition.ColumnName);
            Append(condition.Value);
        }
        return key.ToString();

        void Append(string text) => key.Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text);
    }

    internal static Dictionary<string, GridRowSelector> Index(IEnumerable<GridRowSelector> rows, string description)
    {
        var result = new Dictionary<string, GridRowSelector>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!result.TryAdd(Key(row), row))
                throw new InvalidOperationException($"{description} contains duplicate row {GridRuntimeResolver.DescribeRowSelector(row)}.");
        }
        return result;
    }

    internal static void WaitForExactSelection(
        IGridRowSetSelectionControl grid, IReadOnlyDictionary<string, GridRowSelector> expected,
        UiOperationTimeoutBudget budget)
    {
        Dictionary<string, GridRowSelector>? actual = null;
        TimeoutException? timeout = null;
        try
        {
            var observed = UiWait.TryUntil(
                () =>
                {
                    var snapshot = Index(grid.ReadSelectedRows(budget.RemainingMilliseconds), $"Grid '{grid.AutomationId}' selection");
                    _ = budget.Remaining;
                    return actual = snapshot;
                },
                IsExact,
                new UiWaitOptions { Timeout = budget.Remaining, PollInterval = TimeSpan.FromMilliseconds(25) });
            if (observed.Success) return;
        }
        catch (TimeoutException error) when (actual is not null && !IsExact(actual))
        {
            timeout = error;
        }

        var missing = expected.Where(pair => !actual!.ContainsKey(pair.Key)).Select(static pair => pair.Value);
        var extra = actual!.Where(pair => !expected.ContainsKey(pair.Key)).Select(static pair => pair.Value);
        throw new TimeoutException($"Grid '{grid.AutomationId}' did not reach the exact selection. "
            + $"Missing: [{string.Join("; ", missing.Select(GridRuntimeResolver.DescribeRowSelector))}]; "
            + $"extra: [{string.Join("; ", extra.Select(GridRuntimeResolver.DescribeRowSelector))}].", timeout);

        bool IsExact(IReadOnlyDictionary<string, GridRowSelector> snapshot) =>
            snapshot.Count == expected.Count && expected.Keys.All(snapshot.ContainsKey);
    }
}
