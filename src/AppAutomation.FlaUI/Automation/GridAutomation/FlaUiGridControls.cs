using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation.GridAutomation;
using AppAutomation.FlaUI.Extensions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;
using FlaUI.Core.WindowsAPI;
using CultureInfo = System.Globalization.CultureInfo;
using DateTimeStyles = System.Globalization.DateTimeStyles;
using NumberStyles = System.Globalization.NumberStyles;

namespace AppAutomation.FlaUI.Automation;

public sealed partial class FlaUiControlResolver
{
    private sealed class FlaUiGridControl :
        FlaUiControlBase<Grid>,
        IGridUserActionControl,
        IAddressableGridControl,
        IIndexedAddressableGridControl,
        IGridColumnMetadataControl
    {
        private readonly AutomationElement _searchRoot;

        public FlaUiGridControl(AutomationElement searchRoot, Grid inner) : base(inner)
        {
            _searchRoot = searchRoot ?? throw new ArgumentNullException(nameof(searchRoot));
        }

        public IReadOnlyList<IGridRowControl> Rows =>
            Inner.Rows.Select(row => (IGridRowControl)new FlaUiGridRowControl(row)).ToArray();

        public IReadOnlyList<string> ColumnNames =>
            (TryRead(() => Inner.ColumnHeaders) ?? Array.Empty<GridHeader>())
            .Select(header => ReadAutomationElementText(header) ?? header.Name ?? string.Empty)
            .ToArray();

        public IGridRowControl? GetRowByIndex(int index)
        {
            try
            {
                var row = Inner.GetRowByIndex(index);
                return row is null ? null : new FlaUiGridRowControl(row);
            }
            catch
            {
                var rows = Inner.Rows;
                return index >= 0 && index < rows.Length
                    ? new FlaUiGridRowControl(rows[index])
                    : null;
            }
        }

        public bool TryGetColumnIndex(string columnName, out int columnIndex)
        {
            var columnNames = ColumnNames;
            var matches = columnNames
                .Select(static (name, index) => (name, index))
                .Where(candidate => string.Equals(candidate.name, columnName?.Trim(), StringComparison.Ordinal))
                .Select(static candidate => candidate.index)
                .Take(2)
                .ToArray();
            columnIndex = matches.Length == 1 ? matches[0] : -1;
            return matches.Length == 1;
        }

        public GridRowResolution ResolveRow(GridRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var columnNames = ColumnNames;
            var matches = FindMatchingRows(row, columnNames, timeoutMs, out var scannedRows);
            var description = $"grid='{AutomationId}'; selector={GridRuntimeResolver.DescribeRowSelector(row)}; matches={matches.Length}; rows={scannedRows}";
            return matches.Length switch
            {
                0 => GridRowResolution.NotFound(description),
                1 => GridRowResolution.Unique(description),
                _ => GridRowResolution.Ambiguous(matches.Length, description)
            };
        }

        public GridCellValueSnapshot ReadCell(GridCellAddress address, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(address);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var budget = Stopwatch.StartNew();
            var columnNames = ColumnNames;
            var row = ResolveUniqueRow(address.Row, columnNames, timeoutMs);
            EnsureRemainingBudget(budget, timeoutMs, "read a grid cell");
            var columnIndex = ResolveColumnIndex(address.ColumnName, columnNames);
            var cell = TryRead(() => row.Cells.ElementAtOrDefault(columnIndex))
                ?? throw new InvalidOperationException(
                    $"Grid column '{address.ColumnName}' was not found in the selected row of grid '{AutomationId}'.");
            var value = new FlaUiGridCellControl(cell).Value;
            return new GridCellValueSnapshot(value, value, GridCellValueKind.Text) { IsDisplayOnly = true };
        }

        public string CopyCell(GridCellAddress address, int timeoutMs) =>
            ReadCell(address, timeoutMs).DisplayText ?? string.Empty;

        public void EditCell(GridCellAddress address, GridCellValueEditRequest request, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(address);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var budget = Stopwatch.StartNew();
            var columnNames = ColumnNames;
            var row = ResolveUniqueRow(address.Row, columnNames, timeoutMs);
            var columnIndex = ResolveColumnIndex(address.ColumnName, columnNames);
            EditResolvedCell(row, columnIndex, request, budget, timeoutMs);
        }

        private void EditResolvedCell(
            GridRow row,
            int columnIndex,
            GridCellValueEditRequest request,
            Stopwatch budget,
            int timeoutMs)
        {
            var cell = TryRead(() => row.Cells.ElementAtOrDefault(columnIndex))
                ?? throw new InvalidOperationException(
                    $"Grid column index {columnIndex} was not found in the selected row of grid '{AutomationId}'.");
            TryRead(() =>
            {
                row.ScrollIntoView();
                return true;
            });

            TryDoubleClick(cell, out _);
            var candidates = new[] { (AutomationElement)cell }
                .Concat(FindAutomationDescendants(cell))
                .ToArray();
            switch (request.EditorKind)
            {
                case GridCellEditorKind.CheckBox:
                    SetNativeCheckBox(cell, candidates, request);
                    break;
                case GridCellEditorKind.ComboBox:
                    SelectNativeComboBox(cell, candidates, request, RemainingMilliseconds(budget, timeoutMs));
                    break;
                case GridCellEditorKind.SearchPicker:
                    SelectNativeSearchPicker(cell, candidates, request, RemainingMilliseconds(budget, timeoutMs));
                    break;
                case GridCellEditorKind.Number:
                    if (!TrySetNativeSpinner(cell, candidates, request))
                    {
                        EnterNativeGridText(cell, candidates, request);
                    }
                    break;
                case GridCellEditorKind.Text:
                    EnterNativeGridText(cell, candidates, request);
                    break;
                case GridCellEditorKind.Date:
                    SetNativeDate(cell, candidates, request, RemainingMilliseconds(budget, timeoutMs));
                    break;
                case GridCellEditorKind.Time:
                    SetNativeTime(cell, candidates, request);
                    break;
                case GridCellEditorKind.Color:
                    EnterNativeGridText(
                        cell,
                        candidates,
                        request with { Value = ColorValue.Normalize(request.Value) });
                    break;
                default:
                    throw new System.NotSupportedException(
                        $"Native grid '{AutomationId}' does not expose a standard '{request.EditorKind}' cell editor. "
                        + "Register a declarative grid definition with editor parts for this template column.");
            }

            if (request.CommitMode == GridCellEditCommitMode.Cancel)
            {
                var cancel = ResolveNativeEditorPart(cell, request.EditorParts?.CancelButton);
                if (cancel is not null)
                {
                    cancel.Click();
                }
                else
                {
                    TryFocus(cell);
                    Keyboard.Press(VirtualKeyShort.ESCAPE);
                }

                return;
            }

            var confirm = ResolveNativeEditorPart(cell, request.EditorParts?.ConfirmButton);
            if (confirm is not null)
            {
                confirm.Click();
            }
            else
            {
                Keyboard.Press(VirtualKeyShort.RETURN);
            }
        }

        public GridRowResolution ResolveRow(GridIndexedRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var matches = FindMatchingRows(row, timeoutMs, out var scannedRows);
            var description = DescribeIndexedResolution(row, matches.Length, scannedRows);
            return matches.Length switch
            {
                0 => GridRowResolution.NotFound(description),
                1 => GridRowResolution.Unique(description),
                _ => GridRowResolution.Ambiguous(matches.Length, description)
            };
        }

        public GridCellValueSnapshot ReadCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(column);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var budget = Stopwatch.StartNew();
            var resolved = ResolveUniqueRow(row, timeoutMs, out _);
            EnsureRemainingBudget(budget, timeoutMs, "read a grid cell");
            var columnIndex = ResolveVisibleColumnIndex(column);
            var cell = TryRead(() => resolved.Cells.ElementAtOrDefault(columnIndex))
                ?? throw new InvalidOperationException(
                    $"Grid source field '{column.SourceFieldName}' was not found in the selected row of grid '{AutomationId}'.");
            var value = new FlaUiGridCellControl(cell).Value;
            return new GridCellValueSnapshot(value, value, column.ValueKind) { IsDisplayOnly = true };
        }

        public string CopyCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            int timeoutMs) => ReadCell(row, column, timeoutMs).DisplayText ?? string.Empty;

        public void EditCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(column);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var budget = Stopwatch.StartNew();
            var resolved = ResolveUniqueRow(row, timeoutMs, out _);
            EditResolvedCell(
                resolved,
                ResolveVisibleColumnIndex(column),
                request,
                budget,
                timeoutMs);
        }

        public void OpenRow(GridIndexedRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var resolved = ResolveUniqueRow(row, timeoutMs, out var scannedRows);
            TryRead(() =>
            {
                resolved.ScrollIntoView();
                return true;
            });
            if (!TryDoubleClick(resolved, out var exception))
            {
                throw new InvalidOperationException(
                    $"Grid indexed row '{DescribeIndexedResolution(row, 1, scannedRows)}' could not be opened by double-click.",
                    exception);
            }
        }

        public void OpenRow(GridRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var columnNames = ColumnNames;
            var resolved = ResolveUniqueRow(row, columnNames, timeoutMs);
            TryRead(() =>
            {
                resolved.ScrollIntoView();
                return true;
            });
            if (!TryDoubleClick(resolved, out var exception))
            {
                throw new InvalidOperationException(
                    $"Grid row '{GridRuntimeResolver.DescribeRowSelector(row)}' could not be opened in grid '{AutomationId}'.",
                    exception);
            }
        }

        private int[] FindMatchingRows(
            GridRowSelector selector,
            IReadOnlyList<string> columnNames,
            int timeoutMs,
            out int scannedRows)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var columns = selector.Conditions
                .Select(condition => (Index: ResolveColumnIndex(condition.ColumnName, columnNames), condition.Value))
                .ToArray();
            var stopwatch = Stopwatch.StartNew();
            var candidates = TryRead(() => Inner.GetRowsByValue(columns[0].Index, columns[0].Value, 0))
                ?? Array.Empty<GridRow>();
            scannedRows = candidates.Length;
            var matches = new HashSet<int>();
            foreach (var row in candidates)
            {
                EnsureRemainingBudget(stopwatch, timeoutMs, "resolve a stable grid row");
                var snapshot = ReadGridRowSnapshot(row);
                var cells = snapshot.Cells;
                if (!columns.All(condition =>
                        condition.Index < cells.Length
                        && string.Equals(
                            new FlaUiGridCellControl(cells[condition.Index]).Value,
                            condition.Value,
                            StringComparison.Ordinal)))
                {
                    continue;
                }

                var rowIndex = snapshot.RowIndex;
                if (rowIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Grid '{AutomationId}' returned a matching row without a GridItem row index; "
                        + "the virtualized row cannot be re-resolved safely.");
                }

                matches.Add(rowIndex);
            }

            return matches.ToArray();
        }

        private int[] FindMatchingRows(
            GridIndexedRowSelector selector,
            int timeoutMs,
            out int scannedRows)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var visibleSeed = selector.Conditions.FirstOrDefault(static condition =>
                condition.Column.RowIdentityAutomationProperty is null
                && condition.Column.RuntimeColumnIndex is not null);
            var candidates = visibleSeed is null
                ? TryRead(() => Inner.Rows) ?? Array.Empty<GridRow>()
                : TryRead(() => Inner.GetRowsByValue(
                    visibleSeed.Column.RuntimeColumnIndex!.Value,
                    visibleSeed.ExpectedText,
                    0)) ?? Array.Empty<GridRow>();
            scannedRows = candidates.Length;
            foreach (var property in selector.Conditions
                         .Select(static condition => condition.Column.RowIdentityAutomationProperty)
                         .Where(static property => property is not null)
                         .Select(static property => property!.Value)
                         .Distinct())
            {
                if (candidates.Length > 0
                    && candidates.All(candidate =>
                        FlaUiGridRowAutomationValueReader.Read(candidate, property) is null))
                {
                    throw new InvalidOperationException(
                        $"Grid '{AutomationId}' does not expose configured stable row identity metadata "
                        + $"'{property}' on its runtime rows.");
                }
            }
            var stopwatch = Stopwatch.StartNew();
            var matches = new HashSet<int>();
            foreach (var candidate in candidates)
            {
                EnsureRemainingBudget(stopwatch, timeoutMs, "resolve a stable grid row");
                var snapshot = ReadGridRowSnapshot(candidate);
                if (!IndexedRowMatches(candidate, snapshot.Cells, selector))
                {
                    continue;
                }

                var rowIndex = snapshot.RowIndex;
                if (rowIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Grid '{AutomationId}' returned a matching row without a GridItem row index; "
                        + "the virtualized row cannot be re-resolved safely.");
                }

                matches.Add(rowIndex);
            }

            return matches.ToArray();
        }

        private GridRow ResolveUniqueRow(
            GridRowSelector selector,
            IReadOnlyList<string> columnNames,
            int timeoutMs)
        {
            var stopwatch = Stopwatch.StartNew();
            var matches = FindMatchingRows(selector, columnNames, timeoutMs, out _);
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Grid row selector '{GridRuntimeResolver.DescribeRowSelector(selector)}' matched {matches.Length} rows in grid '{AutomationId}'; expected exactly one.");
            }

            return ResolveCurrentGridRow(matches[0], selector, columnNames, stopwatch, timeoutMs);
        }

        private GridRow ResolveUniqueRow(
            GridIndexedRowSelector selector,
            int timeoutMs,
            out int scannedRows)
        {
            var stopwatch = Stopwatch.StartNew();
            var matches = FindMatchingRows(selector, timeoutMs, out scannedRows);
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Grid indexed row selector matched {matches.Length} rows in grid '{AutomationId}'; expected exactly one. "
                    + DescribeIndexedResolution(selector, matches.Length, scannedRows));
            }

            return ResolveCurrentGridRow(matches[0], selector, stopwatch, timeoutMs, scannedRows);
        }

        private GridRow ResolveCurrentGridRow(
            int rowIndex,
            GridRowSelector selector,
            IReadOnlyList<string> columnNames,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var columns = selector.Conditions
                .Select(condition => (Index: ResolveColumnIndex(condition.ColumnName, columnNames), condition.Value))
                .ToArray();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                var row = TryRead(() => Inner.GetRowByIndex(rowIndex));
                if (row is not null)
                {
                    TryRead(() =>
                    {
                        row.ScrollIntoView();
                        return true;
                    });
                    var cells = TryRead(() => row.Cells) ?? Array.Empty<GridCell>();
                    if (columns.All(condition =>
                            condition.Index < cells.Length
                            && string.Equals(
                                new FlaUiGridCellControl(cells[condition.Index]).Value,
                                condition.Value,
                                StringComparison.Ordinal)))
                    {
                        return row;
                    }
                }

                Thread.Sleep(25);
            }

            throw new TimeoutException(
                $"Grid row selector '{GridRuntimeResolver.DescribeRowSelector(selector)}' was unique at row {rowIndex} "
                + $"but could not be re-resolved in grid '{AutomationId}' within the operation timeout.");
        }

        private GridRow ResolveCurrentGridRow(
            int rowIndex,
            GridIndexedRowSelector selector,
            Stopwatch stopwatch,
            int timeoutMs,
            int scannedRows)
        {
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                var row = TryRead(() => Inner.GetRowByIndex(rowIndex));
                if (row is not null)
                {
                    TryRead(() =>
                    {
                        row.ScrollIntoView();
                        return true;
                    });
                    var cells = TryRead(() => row.Cells) ?? Array.Empty<GridCell>();
                    if (IndexedRowMatches(row, cells, selector))
                    {
                        return row;
                    }
                }

                Thread.Sleep(25);
            }

            throw new TimeoutException(
                $"Grid indexed row selector was unique at row {rowIndex} but could not be re-resolved "
                + $"in grid '{AutomationId}' within the operation timeout. "
                + DescribeIndexedResolution(selector, 1, scannedRows));
        }

        private static bool IndexedRowMatches(
            GridRow row,
            GridCell[] cells,
            GridIndexedRowSelector selector)
        {
            return selector.Conditions.All(condition =>
            {
                var actual = condition.Column.RowIdentityAutomationProperty is { } property
                    ? FlaUiGridRowAutomationValueReader.Read(row, property)
                    : condition.Column.RuntimeColumnIndex is { } runtimeColumnIndex
                      && runtimeColumnIndex < cells.Length
                        ? new FlaUiGridCellControl(cells[runtimeColumnIndex]).Value
                        : null;
                return string.Equals(actual, condition.ExpectedText, StringComparison.Ordinal);
            });
        }

        private static int ResolveVisibleColumnIndex(GridRuntimeColumn column)
        {
            return column.RuntimeColumnIndex
                ?? throw new InvalidOperationException(
                    $"Grid source field '{column.SourceFieldName}' is hidden in the current runtime layout. "
                    + "A hidden target column cannot be read or edited through UI Automation; make it visible.");
        }

        private string DescribeIndexedResolution(
            GridIndexedRowSelector selector,
            int matchCount,
            int scannedRows)
        {
            var conditions = string.Join(
                ", ",
                selector.Conditions.Select(static condition =>
                    condition.Column.RowIdentityAutomationProperty is { } property
                        ? $"row.{property}='{condition.ExpectedText}'"
                        : $"column[{condition.Column.RuntimeColumnIndex?.ToString(CultureInfo.InvariantCulture) ?? "hidden"}]='{condition.ExpectedText}'"));
            return $"grid='{AutomationId}'; selector={conditions}; matches={matchCount}; rows={scannedRows}";
        }

        private static int ReadGridRowIndex(GridRow row)
        {
            var cells = TryRead(() => row.Cells) ?? Array.Empty<GridCell>();
            return ReadGridRowIndex(cells);
        }

        private static GridRowSnapshot ReadGridRowSnapshot(GridRow row)
        {
            var cells = TryRead(() => row.Cells) ?? Array.Empty<GridCell>();
            return new GridRowSnapshot(cells, ReadGridRowIndex(cells));
        }

        private static int ReadGridRowIndex(GridCell[] cells)
        {
            return cells.Length == 0
                ? -1
                : TryRead(() => cells[0].Patterns.GridItem.PatternOrDefault?.Row.ValueOrDefault ?? -1);
        }

        private sealed record GridRowSnapshot(GridCell[] Cells, int RowIndex);

        private static int RemainingMilliseconds(Stopwatch stopwatch, int timeoutMs)
        {
            var remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                throw new TimeoutException("The grid operation exceeded its timeout while resolving the stable row.");
            }

            return remaining;
        }

        private static void EnsureRemainingBudget(Stopwatch stopwatch, int timeoutMs, string operation)
        {
            if (stopwatch.ElapsedMilliseconds >= timeoutMs)
            {
                throw new TimeoutException($"The grid operation exceeded its timeout before it could {operation}.");
            }
        }

        private int ResolveColumnIndex(
            string columnName,
            IReadOnlyList<string> columnNames)
        {
            var normalizedColumnName = columnName?.Trim();
            var matches = columnNames
                .Select(static (candidate, index) => (candidate, index))
                .Where(candidate => string.Equals(
                    candidate.candidate,
                    normalizedColumnName,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Grid column '{columnName}' is ambiguous in grid '{AutomationId}' ({matches.Length} matches).");
            }

            if (matches.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Grid column '{columnName}' was not found. Available columns: {string.Join(", ", columnNames)}.");
            }

            return matches[0].index;
        }

        private void EnterNativeGridText(
            AutomationElement cell,
            IEnumerable<AutomationElement> candidates,
            GridCellValueEditRequest request)
        {
            var input = ResolveNativeEditorPart(cell, request.EditorParts?.Input)
                ?? candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Edit)
                ?? throw new InvalidOperationException("The active grid cell does not expose a writable text editor.");
            new FlaUiTextBoxControl(input.AsTextBox()).Enter(request.Value);
        }

        private bool TrySetNativeSpinner(
            AutomationElement cell,
            IEnumerable<AutomationElement> candidates,
            GridCellValueEditRequest request)
        {
            var configured = ResolveNativeEditorPart(cell, request.EditorParts?.Input);
            var spinner = configured is not null && TryRead(() => configured.ControlType) == ControlType.Spinner
                ? configured
                : candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Spinner);
            if (spinner is null || !double.TryParse(request.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            new FlaUiSpinnerControl(spinner.AsSpinner()).Value = number;
            return true;
        }

        private void SelectNativeComboBox(
            AutomationElement cell,
            IEnumerable<AutomationElement> candidates,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            var configured = ResolveNativeEditorPart(cell, request.EditorParts?.Input);
            var combo = configured is not null && TryRead(() => configured.ControlType) == ControlType.ComboBox
                ? configured
                : candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.ComboBox)
                ?? throw new InvalidOperationException("The active grid cell does not expose a ComboBox editor.");
            new FlaUiComboBoxControl(combo.AsComboBox()).SelectItem(request.Value, TimeSpan.FromMilliseconds(timeoutMs));
        }

        private void SetNativeCheckBox(
            AutomationElement cell,
            IEnumerable<AutomationElement> candidates,
            GridCellValueEditRequest request)
        {
            if (!bool.TryParse(request.Value, out var expected))
            {
                throw new InvalidOperationException($"Grid check-box value '{request.Value}' is not Boolean.");
            }

            var configured = ResolveNativeEditorPart(cell, request.EditorParts?.Input);
            var element = configured is not null && TryRead(() => configured.ControlType) == ControlType.CheckBox
                ? configured
                : candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.CheckBox)
                ?? throw new InvalidOperationException("The active grid cell does not expose a CheckBox editor.");
            var checkBox = new FlaUiCheckBoxControl(element.AsCheckBox());
            if (checkBox.IsChecked != expected)
            {
                checkBox.IsChecked = expected;
            }
        }

        private void SelectNativeSearchPicker(
            AutomationElement cell,
            IReadOnlyList<AutomationElement> candidates,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            if (string.IsNullOrWhiteSpace(request.SearchText))
            {
                throw new ArgumentException("Search text cannot be empty for a search-picker grid edit.", nameof(request));
            }

            var input = ResolveNativeEditorPart(cell, request.EditorParts?.Input)
                ?? candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Edit)
                ?? throw new InvalidOperationException("The active grid cell does not expose a search input.");
            ExecuteFlaUiSearchPickerSelection(
                input,
                wait => request.EditorParts?.Results is { } resultsLocator
                    ? WaitForNativeEditorPart(cell, resultsLocator, wait)
                    : null,
                () => ResolveNativeEditorPart(cell, request.EditorParts?.OpenButton),
                request.SearchText,
                request.Value,
                timeoutMs,
                "the active grid cell");
        }

        private void SetNativeDate(
            AutomationElement cell,
            IReadOnlyList<AutomationElement> candidates,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            if (!DateTime.TryParseExact(request.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw new InvalidOperationException($"Grid date value '{request.Value}' is not a valid invariant date.");
            }

            var calendar = ResolveNativeEditorPart(cell, request.EditorParts?.Results)
                ?? candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Calendar);
            if (calendar is null)
            {
                ResolveNativeEditorPart(cell, request.EditorParts?.OpenButton)?.Click();
                calendar = request.EditorParts?.Results is { } locator
                    ? WaitForNativeEditorPart(cell, locator, TimeSpan.FromMilliseconds(timeoutMs))
                    : null;
            }

            if (calendar is null || TryRead(() => calendar.ControlType) != ControlType.Calendar)
            {
                throw new InvalidOperationException("The active grid cell does not expose a calendar editor.");
            }

            new FlaUiCalendarControl(calendar.AsCalendar()).SelectDate(date.Date);
        }

        private void SetNativeTime(
            AutomationElement cell,
            IReadOnlyList<AutomationElement> candidates,
            GridCellValueEditRequest request)
        {
            if (!TimeSpan.TryParseExact(request.Value, "c", CultureInfo.InvariantCulture, out var time)
                || time < TimeSpan.Zero
                || time >= TimeSpan.FromDays(1))
            {
                throw new InvalidOperationException($"Grid time value '{request.Value}' is not a valid invariant time of day.");
            }

            var input = ResolveNativeEditorPart(cell, request.EditorParts?.Input)
                ?? candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Edit);
            if (input is null || TryRead(() => input.ControlType) != ControlType.Edit)
            {
                throw new InvalidOperationException("The active grid cell does not expose a time input.");
            }

            new FlaUiTextBoxControl(input.AsTextBox()).Enter(time.ToString("c", CultureInfo.InvariantCulture));
        }

        private AutomationElement? ResolveNativeEditorPart(
            AutomationElement cell,
            GridRelativeLocator? locator)
        {
            return ResolveGridEditorPart(
                _searchRoot,
                cell,
                Inner,
                locator,
                CreateNativeAmbiguousEditorPartException);
        }

        private InvalidOperationException CreateNativeAmbiguousEditorPartException(
            GridRelativeLocator locator)
        {
            return new InvalidOperationException(
                $"Grid editor part '{locator.LocatorKind}:{locator.LocatorValue}' is ambiguous "
                + $"within scope '{locator.Scope}' of the active cell in grid '{AutomationId}'.");
        }

        private AutomationElement? WaitForNativeEditorPart(
            AutomationElement cell,
            GridRelativeLocator locator,
            TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            do
            {
                var resolved = ResolveNativeEditorPart(cell, locator);
                if (resolved is not null && TryRead(() => resolved.IsAvailable))
                {
                    return resolved;
                }

                Thread.Sleep(50);
            }
            while (stopwatch.Elapsed < timeout);

            return null;
        }

        public void OpenRow(int rowIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);

            Exception? rowException = null;
            var row = TryRead(() => Inner.GetRowByIndex(rowIndex));
            if (row is not null && TryDoubleClick(row, out rowException))
            {
                return;
            }

            var rows = TryRead(() => Inner.Rows) ?? Array.Empty<GridRow>();
            if (rowIndex >= rows.Length)
            {
                throw new InvalidOperationException($"Grid row {rowIndex} was not found in grid '{AutomationId}'.");
            }

            if (TryDoubleClick(rows[rowIndex], out var indexedRowException))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Grid row {rowIndex} in grid '{AutomationId}' could not be opened by double-click.",
                indexedRowException ?? rowException);
        }

        public void SortByColumn(string columnName)
        {
            ThrowUnsupportedUserAction(nameof(SortByColumn));
        }

        public void ScrollToEnd()
        {
            ThrowUnsupportedUserAction(nameof(ScrollToEnd));
        }

        public string CopyCell(int rowIndex, int columnIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);

            return GetRowByIndex(rowIndex)?.Cells.ElementAtOrDefault(columnIndex)?.Value
                   ?? string.Empty;
        }

        public void Export()
        {
            ThrowUnsupportedUserAction(nameof(Export));
        }

        private void ThrowUnsupportedUserAction(string actionName)
        {
            throw new System.NotSupportedException(
                $"Grid '{AutomationId}' does not support user action '{actionName}' in the FlaUI adapter.");
        }
    }


    private sealed class FlaUiDataGridViewControl : FlaUiControlBase<DataGridView>, IGridControl
    {
        public FlaUiDataGridViewControl(DataGridView inner) : base(inner)
        {
        }

        public IReadOnlyList<IGridRowControl> Rows =>
            Inner.Rows.Select(row => (IGridRowControl)new FlaUiObjectGridRowControl(row)).ToArray();

        public IGridRowControl? GetRowByIndex(int index)
        {
            var rows = Inner.Rows;
            if (index < 0 || index >= rows.Length)
            {
                return null;
            }

            return new FlaUiObjectGridRowControl(rows[index]);
        }
    }

    private sealed class FlaUiGridRowControl : IGridRowControl
    {
        private readonly GridRow _inner;

        public FlaUiGridRowControl(GridRow inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<IGridCellControl> Cells =>
            _inner.Cells.Select(cell => (IGridCellControl)new FlaUiGridCellControl(cell)).ToArray();
    }

    private sealed class FlaUiGridCellControl : IGridCellValueControl
    {
        private readonly GridCell _inner;

        public FlaUiGridCellControl(GridCell inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Value =>
            ReadAutomationElementText(_inner)
            ?? ReadObjectText(TryRead(() => _inner.Value))
            ?? string.Empty;

        public GridCellValueSnapshot ValueSnapshot => new(Value, Value) { IsDisplayOnly = true };
    }

    private sealed class FlaUiObjectGridRowControl : IGridRowControl
    {
        private readonly object _inner;

        public FlaUiObjectGridRowControl(object inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<IGridCellControl> Cells
        {
            get
            {
                var cellsProperty = _inner.GetType().GetProperty("Cells");
                if (cellsProperty?.GetValue(_inner) is not System.Collections.IEnumerable cells)
                {
                    return Array.Empty<IGridCellControl>();
                }

                var result = new List<IGridCellControl>();
                foreach (var cell in cells)
                {
                    if (cell is not null)
                    {
                        result.Add(new FlaUiObjectGridCellControl(cell));
                    }
                }

                return result;
            }
        }
    }

    private sealed class FlaUiObjectGridCellControl : IGridCellValueControl
    {
        private readonly object _inner;

        public FlaUiObjectGridCellControl(object inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public GridCellValueSnapshot ValueSnapshot => new(Value, Value) { IsDisplayOnly = true };

        public string Value
        {
            get
            {
                var valueProperty = _inner.GetType().GetProperty("Value");
                return ReadObjectText(valueProperty?.GetValue(_inner))
                    ?? _inner.ToString()
                    ?? string.Empty;
            }
        }
    }
}
