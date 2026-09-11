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

        if (grid is IAddressableGridControl addressableGrid)
        {
            var budget = UiOperationTimeoutBudget.Start(timeoutMs, nameof(WaitUntilGridContainsRow));
            WaitUntil(
                page,
                selector,
                () => addressableGrid.ResolveRow(rowSelector, budget.RemainingMilliseconds).State
                    == GridRowResolutionState.Unique,
                budget.RemainingMilliseconds,
                $"Grid '{grid.AutomationId}' did not contain exactly one row matching the stable selector.",
                expectedValue: GridRuntimeResolver.DescribeRowSelector(rowSelector),
                lastObservedValueFactory: () =>
                    addressableGrid.ResolveRow(rowSelector, budget.RemainingMilliseconds).Description,
                nameof(WaitUntilGridContainsRow));
            return page;
        }

        WaitUntil(
            page,
            selector,
            () => GridRuntimeResolver.FindMatchingRowIndexes(grid, rowSelector).Count > 0,
            timeoutMs,
            $"Grid '{grid.AutomationId}' did not contain a row matching the stable selector.",
            expectedValue: GridRuntimeResolver.DescribeRowSelector(rowSelector),
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
        if (grid is IAddressableGridControl addressableGrid)
        {
            var budget = UiOperationTimeoutBudget.Start(timeoutMs, nameof(WaitUntilGridCellEquals));
            var address = new GridCellAddress(rowSelector, columnName);
            string? lastObserved = null;
            WaitUntil(
                page,
                selector,
                () => TryReadAddressableCellForPolling(
                        addressableGrid,
                        address,
                        budget,
                        out var snapshot,
                        out lastObserved)
                    && string.Equals(snapshot!.DisplayText, expectedValue, StringComparison.Ordinal),
                budget.RemainingMilliseconds,
                $"Grid '{grid.AutomationId}' named cell '{columnName}' did not reach expected value.",
                expectedValue: expectedValue,
                lastObservedValueFactory: () => lastObserved,
                nameof(WaitUntilGridCellEquals));
            return page;
        }

        var columnIndex = GridRuntimeResolver.ResolveColumnIndex(grid, columnName);
        WaitUntil(
            page,
            selector,
            () => GridRuntimeResolver.TryResolveUniqueRowIndex(grid, rowSelector, out var rowIndex)
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
        ArgumentNullException.ThrowIfNull(rowSelector);
        var grid = Resolve(selector, page);
        if (grid is IAddressableGridControl addressableGrid)
        {
            return ExecuteAddressableGridAction(
                page,
                selector,
                timeoutMs,
                nameof(OpenGridRow),
                budget => addressableGrid.OpenRow(rowSelector, budget.RemainingMilliseconds),
                budget => addressableGrid.ResolveRow(rowSelector, budget.RemainingMilliseconds).Description);
        }

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
        ArgumentNullException.ThrowIfNull(rowSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        var grid = Resolve(selector, page);
        if (grid is IAddressableGridControl addressableGrid)
        {
            var address = new GridCellAddress(rowSelector, columnName);
            return ExecuteAddressableGridAction(
                page,
                selector,
                timeoutMs,
                nameof(CopyGridCell),
                budget => addressableGrid.CopyCell(address, budget.RemainingMilliseconds),
                budget => addressableGrid.ReadCell(address, budget.RemainingMilliseconds).DisplayText);
        }

        var columnIndex = GridRuntimeResolver.ResolveColumnIndex(grid, columnName);
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
        ArgumentNullException.ThrowIfNull(rowSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentNullException.ThrowIfNull(value);
        var grid = Resolve(selector, page);
        if (grid is IAddressableGridControl addressableGrid)
        {
            var address = new GridCellAddress(rowSelector, columnName);
            var addressRequest = new GridCellValueEditRequest(
                value,
                editorKind,
                commitMode,
                searchText);
            return ExecuteAddressableGridEdit(
                page,
                selector,
                addressableGrid,
                address,
                addressRequest,
                timeoutMs);
        }

        var columnIndex = GridRuntimeResolver.ResolveColumnIndex(grid, columnName);
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
    /// Edits a named cell with an invariant time-of-day value in the uniquely selected row.
    /// </summary>
    public static TSelf EditGridCellTime<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        string columnName,
        TimeSpan value,
        GridCellEditCommitMode commitMode = GridCellEditCommitMode.Commit,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Grid time value must be within one day.");
        }

        return EditGridCell(
            page,
            selector,
            rowSelector,
            columnName,
            value.ToString("c", CultureInfo.InvariantCulture),
            GridCellEditorKind.Time,
            commitMode,
            timeoutMs: timeoutMs);
    }

    /// <summary>
    /// Selects a color in a named grid cell.
    /// </summary>
    public static TSelf EditGridCellColor<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        string columnName,
        string color,
        GridCellEditCommitMode commitMode = GridCellEditCommitMode.Commit,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        return EditGridCell(
            page,
            selector,
            rowSelector,
            columnName,
            ColorValue.Normalize(color),
            GridCellEditorKind.Color,
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

    /// <summary>
    /// Sets a boolean value in a named check-box cell of the uniquely selected row.
    /// </summary>
    public static TSelf SetGridCellChecked<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        GridRowSelector rowSelector,
        string columnName,
        bool isChecked,
        GridCellEditCommitMode commitMode = GridCellEditCommitMode.Commit,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        return EditGridCell(
            page,
            selector,
            rowSelector,
            columnName,
            isChecked.ToString(CultureInfo.InvariantCulture),
            GridCellEditorKind.CheckBox,
            commitMode,
            timeoutMs: timeoutMs);
    }

    private static TSelf ExecuteAddressableGridEdit<TSelf>(
        TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        IAddressableGridControl grid,
        GridCellAddress address,
        GridCellValueEditRequest request,
        int timeoutMs)
        where TSelf : UiPage
    {
        var actionName = nameof(EditGridCell);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        var budget = UiOperationTimeoutBudget.Start(timeoutMs, actionName);
        GridCellValueSnapshot? originalValue = null;
        try
        {
            originalValue = grid.ReadCell(address, budget.RemainingMilliseconds);
            grid.EditCell(address, request, budget.RemainingMilliseconds);
        }
        catch (Exception ex) when (ex is not UiOperationException and not OperationCanceledException)
        {
            throw CreateUiOperationException(
                page,
                selector,
                timeout,
                startedAtUtc,
                $"Grid '{grid.AutomationId}' failed to edit named cell '{address.ColumnName}'.",
                expectedValue: DescribeGridCellEditRequest(address, request),
                lastObservedValueFactory: () => TryDescribeAddressableCell(grid, address, budget),
                actionName,
                ex);
        }

        var expectedValue = request.CommitMode == GridCellEditCommitMode.Commit
            ? request.Value
            : originalValue?.DisplayText;
        string? lastObserved = null;
        WaitUntil(
            page,
            selector,
            () => TryReadAddressableCellForPolling(
                    grid,
                    address,
                    budget,
                    out var snapshot,
                    out lastObserved)
                && MatchesExpectedGridValue(snapshot!, request, originalValue),
            budget.RemainingMilliseconds,
            $"Grid '{grid.AutomationId}' named cell '{address.ColumnName}' did not reach expected edit result.",
            expectedValue,
            () => lastObserved,
            actionName);
        return page;
    }

    private static bool TryReadAddressableCellForPolling(
        IAddressableGridControl grid,
        GridCellAddress address,
        UiOperationTimeoutBudget budget,
        out GridCellValueSnapshot? snapshot,
        out string? lastObserved)
    {
        snapshot = null;
        lastObserved = null;
        try
        {
            snapshot = grid.ReadCell(address, budget.RemainingMilliseconds);
            lastObserved = snapshot.DisplayText;
            return true;
        }
        catch (Exception ex) when (IsTransientGridProviderFailure(ex))
        {
            lastObserved = $"<transient {ex.GetType().Name}: {ex.Message}>";
            return false;
        }
    }

    private static bool IsTransientGridProviderFailure(Exception exception)
    {
        if (exception is TimeoutException)
        {
            return true;
        }

        var typeName = exception.GetType().Name;
        if (typeName is "ElementNotAvailableException" or "StaleElementReferenceException")
        {
            return true;
        }

        return exception is InvalidOperationException
            && exception.Message.Contains("matched 0 rows", StringComparison.OrdinalIgnoreCase);
    }

    private static TSelf ExecuteAddressableGridAction<TSelf>(
        TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        int timeoutMs,
        string actionName,
        Action<UiOperationTimeoutBudget> action,
        Func<UiOperationTimeoutBudget, string?> lastObservedValueFactory)
        where TSelf : UiPage
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        var budget = UiOperationTimeoutBudget.Start(timeoutMs, actionName);
        try
        {
            action(budget);
            return page;
        }
        catch (Exception ex) when (ex is not UiOperationException and not OperationCanceledException)
        {
            throw CreateUiOperationException(
                page,
                selector,
                timeout,
                startedAtUtc,
                $"Grid action '{actionName}' failed for a stable grid address.",
                expectedValue: actionName,
                () => lastObservedValueFactory(budget),
                actionName,
                ex);
        }
    }

    private static bool MatchesExpectedGridValue(
        GridCellValueSnapshot actual,
        GridCellValueEditRequest request,
        GridCellValueSnapshot? original)
    {
        if (request.CommitMode == GridCellEditCommitMode.Cancel)
        {
            return actual.IsNull == (original?.IsNull ?? true)
                && string.Equals(actual.DisplayText, original?.DisplayText, StringComparison.Ordinal);
        }

        return request.EditorKind switch
        {
            GridCellEditorKind.Number =>
                decimal.TryParse(request.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var expectedNumber)
                && TryConvertGridNumber(actual, out var actualNumber)
                && actualNumber == expectedNumber,
            GridCellEditorKind.Date =>
                DateTime.TryParseExact(request.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expectedDate)
                && TryConvertGridDate(actual.RawValue, actual.DisplayText, out var actualDate)
                && actualDate.Date == expectedDate.Date,
            GridCellEditorKind.Time =>
                TimeSpan.TryParse(request.Value, CultureInfo.InvariantCulture, out var expectedTime)
                && TryConvertGridTime(actual.RawValue, actual.DisplayText, out var actualTime)
                && actualTime == expectedTime,
            GridCellEditorKind.CheckBox =>
                bool.TryParse(request.Value, out var expectedChecked)
                && TryConvertGridBoolean(actual.RawValue, actual.DisplayText, out var actualChecked)
                && actualChecked == expectedChecked,
            _ => string.Equals(actual.DisplayText, request.Value, StringComparison.Ordinal)
        };
    }

    private static bool TryConvertGridNumber(GridCellValueSnapshot snapshot, out decimal value)
    {
        if (GridValueConversion.TryConvertNumber(snapshot, out value, out var diagnostic))
        {
            return true;
        }

        throw new InvalidOperationException(diagnostic);
    }

    private static bool TryConvertGridDate(object? rawValue, string? displayText, out DateTime value)
    {
        switch (rawValue)
        {
            case DateTime date:
                value = date;
                return true;
            case DateTimeOffset offset:
                value = offset.Date;
                return true;
            case DateOnly dateOnly:
                value = dateOnly.ToDateTime(TimeOnly.MinValue);
                return true;
        }

        return DateTime.TryParse(displayText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value)
            || DateTime.TryParse(displayText, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out value);
    }

    private static bool TryConvertGridTime(object? rawValue, string? displayText, out TimeSpan value)
    {
        switch (rawValue)
        {
            case TimeSpan time:
                value = time;
                return true;
            case TimeOnly timeOnly:
                value = timeOnly.ToTimeSpan();
                return true;
            case DateTime dateTime:
                value = dateTime.TimeOfDay;
                return true;
            case DateTimeOffset dateTimeOffset:
                value = dateTimeOffset.TimeOfDay;
                return true;
        }

        return TimeSpan.TryParse(displayText, CultureInfo.InvariantCulture, out value)
            || TimeSpan.TryParse(displayText, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryConvertGridBoolean(object? rawValue, string? displayText, out bool value)
    {
        if (rawValue is bool boolean)
        {
            value = boolean;
            return true;
        }

        return bool.TryParse(rawValue?.ToString(), out value)
            || bool.TryParse(displayText, out value);
    }

    private static string DescribeGridCellEditRequest(
        GridCellAddress address,
        GridCellValueEditRequest request)
    {
        return $"{request.EditorKind}/{request.CommitMode}: "
            + $"{GridRuntimeResolver.DescribeRowSelector(address.Row)}; "
            + $"column='{address.ColumnName}'; value='{request.Value}'";
    }

    private static string? TryDescribeAddressableCell(
        IAddressableGridControl grid,
        GridCellAddress address,
        UiOperationTimeoutBudget budget)
    {
        try
        {
            return grid.ReadCell(address, budget.RemainingMilliseconds).DisplayText;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"<{ex.GetType().Name}: {ex.Message}>";
        }
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
            () => GridRuntimeResolver.TryResolveUniqueRowIndex(grid, rowSelector, out rowIndex),
            timeoutMs,
            $"Grid '{grid.AutomationId}' did not contain exactly one row matching the stable selector.",
            expectedValue: GridRuntimeResolver.DescribeRowSelector(rowSelector),
            lastObservedValueFactory: () => DescribeRowMatches(grid, rowSelector),
            operationName);
        return rowIndex;
    }

    private static string DescribeRowMatches(IGridControl grid, GridRowSelector selector)
    {
        return $"matches={GridRuntimeResolver.FindMatchingRowIndexes(grid, selector).Count}; rows={grid.Rows.Count}";
    }

    private static string? TryReadNamedGridCellValue(
        IGridControl grid,
        GridRowSelector rowSelector,
        int columnIndex)
    {
        var matches = GridRuntimeResolver.FindMatchingRowIndexes(grid, rowSelector);
        return matches.Count switch
        {
            0 => $"<missing row; rows={grid.Rows.Count}>",
            1 => TryReadGridCellValue(grid, matches[0], columnIndex),
            _ => $"<ambiguous row; matches={matches.Count}>"
        };
    }
}
