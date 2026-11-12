using System.Diagnostics;
using System.Runtime.InteropServices;
using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation.GridAutomation;
using AppAutomation.FlaUI.Input;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Patterns;
using FlaUI.UIA3.Converters;
using FlaUI.UIA3.Patterns;

namespace AppAutomation.FlaUI.Automation;

public sealed partial class FlaUiControlResolver
{
    private sealed partial class FlaUiVisualGridControl
    {
        public IReadOnlyList<GridRowSelector> ReadSelectedRows(GridRowSetIdentity identity, UiOperationTimeoutBudget budget)
        {
            var root = RequireRowSetRoot(budget);
            return ReadRowSet(root, FindNativeSelection(root), identity, budget).Selected.Values.ToArray();
        }

        public void SetSelectedRows(GridRowSetIdentity identity, IReadOnlyDictionary<string, GridRowSelector> expected,
            UiOperationTimeoutBudget budget)
        {
            var root = RequireRowSetRoot(budget);
            var selection = FindNativeSelection(root);
            var initial = ReadRowSet(root, selection, identity, budget);
            if (expected.Count == 0 && !initial.AllowsEmpty)
                throw new NotSupportedException($"Grid '{AutomationId}' does not allow an empty selection.");
            if (SameSelection(initial.Selected, expected)) return;

            if (selection is not null && initial.Native is not null
                && TrySetNativeSelectedRows(root, selection, initial, identity, expected, budget)) return;
            SetSelectedRowsWithGestures(root, selection, initial, identity, expected, budget);
        }

        private bool TrySetNativeSelectedRows(AutomationElement root, SelectionPattern selection, RowSetState initial,
            GridRowSetIdentity identity, IReadOnlyDictionary<string, GridRowSelector> expected, UiOperationTimeoutBudget budget)
        {
            var targets = expected.ToDictionary(static pair => pair.Key, pair => identity.ToRuntimeSelector(pair.Value),
                StringComparer.Ordinal);
            var positions = targets.Count > 0 ? ProveRowSetTargets(root, identity, expected, budget) : null;

            Dictionary<string, (GridRowSelector Key, AutomationElement Element)> current;
            try
            {
                foreach (var (key, target) in targets)
                    if (!initial.Selected.ContainsKey(key)) _ = ResolveSelectableRow(key, target);

                current = ReadCurrent();
                if (current.Count != initial.Selected.Count || !initial.Selected.Keys.All(current.ContainsKey))
                    throw new InvalidOperationException($"Grid '{AutomationId}' selection changed while locating the requested rows.");
                if (expected.Count > 0 && !HasAllIdentityColumnsVisible(identity))
                    return false; // Native writes need readable row peers throughout the operation.
                foreach (var (key, entry) in current)
                    if (!expected.ContainsKey(key)) _ = RequireSelectionItem(entry.Element, root, allowVirtualized: true);
            }
            catch (MissingSelectionItemException)
            {
                return false; // Only capability failure before the first write can choose another strategy.
            }

            // Add first so a provider that requires a selection does not reject replacing its last row.
            foreach (var (key, target) in targets)
            {
                if (current.ContainsKey(key)) continue;
                var item = ResolveSelectableRow(key, target);
                _ = budget.Remaining;
                if (!item.IsSelected.Value) item.AddToSelection();
            }

            // Adding can recreate selection peers. Read from the container again before each removal.
            foreach (var key in current.Keys.Where(key => !expected.ContainsKey(key)))
            {
                if (!ReadCurrent().TryGetValue(key, out var extra)) continue;
                _ = budget.Remaining;
                RequireSelectionItem(extra.Element, root, allowVirtualized: true).RemoveFromSelection();
            }
            return true;

            Dictionary<string, (GridRowSelector Key, AutomationElement Element)> ReadCurrent() =>
                ReadSelectedRowsCore(selection, identity, budget) is { IsComplete: true } read
                    ? read.Rows.ToDictionary(entry => GridRowSelectionSet.Key(entry.Key), StringComparer.Ordinal)
                    : throw new MissingSelectionItemException($"Grid '{AutomationId}' stopped exposing all native selected row identities.");

            ISelectionItemPattern ResolveSelectableRow(string key, GridIndexedRowSelector selector)
            {
                _ = budget.Remaining;
                if (selector.Conditions.All(static part => part.Column.RowIdentityAutomationProperty.HasValue))
                {
                    // A provider can publish SelectionItem for rows that are not currently visible.
                    var published = root.FindAllDescendants(CreateNativeRowCondition(root, selector))
                        .Where(row => BelongsToGrid(row, root)).ToArray();
                    if (published.Length > 1)
                        throw new InvalidOperationException($"Grid '{AutomationId}' exposes multiple rows for a requested stable key.");
                    if (published is [var peer] && peer.Patterns.SelectionItem.IsSupported)
                        return RequireSelectionItem(peer, root);
                }

                var row = ResolveRowSetTarget(key, selector, identity, budget,
                    HasCompositeCellIdentity(identity) ? positions : null);
                _ = budget.Remaining;
                return RequireSelectionItem(row.Element, root);
            }
        }

        private AutomationElement RequireRowSetRoot(UiOperationTimeoutBudget budget)
        {
            _ = budget.Remaining;
            return FindGridRoot(budget) ?? throw new InvalidOperationException($"Grid '{AutomationId}' is unavailable.");
        }

        private static SelectionPattern? FindNativeSelection(AutomationElement root) =>
            root.Patterns.Selection.PatternOrDefault is SelectionPattern selection
                && selection.CanSelectMultiple.TryGetValue(out var multiple) && multiple ? selection : null;

        private RowSetState ReadRowSet(AutomationElement root, SelectionPattern? selection, GridRowSetIdentity identity,
            UiOperationTimeoutBudget budget)
        {
            _ = budget.Remaining;
            if (!root.Equals(RequireRowSetRoot(budget)))
                throw new InvalidOperationException($"Grid '{AutomationId}' was replaced during selection.");
            var publication = GridRowSelectionValue.Read(root.Properties.ItemStatus.ValueOrDefault, identity);
            var native = selection is null ? null : ReadSelectedRowsCore(selection, identity, budget);
            if (native is not null)
            {
                var selected = GridRowSelectionSet.Index(native.Rows.Select(static entry => entry.Key), $"Grid '{AutomationId}' selection");
                if (publication is not null && (native.Count != publication.Selected.Count
                    || !selected.Keys.All(publication.Selected.ContainsKey)))
                    throw new InvalidOperationException($"Grid '{AutomationId}' reports conflicting UIA selection snapshots.");
                var allowsEmpty = selection!.IsSelectionRequired.TryGetValue(out var required)
                    ? !required : publication?.AllowsEmpty == true;
                if (native.IsComplete)
                    return new RowSetState(selected, publication?.OrderedView, allowsEmpty, native.Rows,
                        HasPublication: publication is not null);
                if (publication is not null)
                    return new RowSetState(publication.Selected, publication.OrderedView, allowsEmpty, null, HasPublication: true);
            }
            return publication is not null
                ? new RowSetState(publication.Selected, publication.OrderedView, publication.AllowsEmpty, null, HasPublication: true)
                : throw new NotSupportedException($"Grid '{AutomationId}' does not expose complete UI Automation multiple selection "
                    + "through Selection or a GridRowSelectionValue publication on its root.");
        }

        private bool HasAllIdentityColumnsVisible(GridRowSetIdentity identity)
        {
            var cellColumnCount = identity.RuntimeColumns.Count(static column => column.RowIdentityAutomationProperty is null);
            return cellColumnCount == 0
                || ReadVisibleIdentityCellIndexes(identity, ReadNativeColumnHeaders()).Count == cellColumnCount;
        }

        private NativeSelectionRead? ReadSelectedRowsCore(
            SelectionPattern selection, GridRowSetIdentity identity, UiOperationTimeoutBudget budget)
        {
            _ = budget.Remaining;
            // The generic UIA property may be absent even when the Selection provider implements GetSelection.
            // FlaUI converts an absent property array to an empty array; do not accept that as an empty selection.
            var nativeSelection = selection.NativePattern.GetCurrentSelection();
            if (nativeSelection is null) return null;
            var selected = AutomationElementConverter.NativeArrayToManaged(selection.Automation, nativeSelection);
            if (selected.Length == 0) return new NativeSelectionRead(0, []);

            var headers = identity.RuntimeColumns.Any(static column => column.RowIdentityAutomationProperty is null)
                ? ReadNativeColumnHeaders()
                : [];
            var indexes = ReadVisibleIdentityCellIndexes(identity, headers);
            var readable = new List<(GridRowSelector Key, AutomationElement Element)>(selected.Length);
            foreach (var element in selected)
            {
                _ = budget.Remaining;
                var cells = indexes.Count > 0 ? ResolveNativeRowCells(element, headers, indexes.Values) : [];
                var key = identity.TryRead(column => ReadRowIdentityValue(element, cells, indexes, column));
                if (key is not null) readable.Add((key, element));
            }
            return new NativeSelectionRead(selected.Length, readable.ToArray());
        }

        private static string? ReadRowIdentityValue(AutomationElement row, IReadOnlyList<AutomationElement> cells,
            IReadOnlyDictionary<GridRuntimeColumn, int> indexes, GridRuntimeColumn column) =>
            column.RowIdentityAutomationProperty is { } property
                ? FlaUiGridRowAutomationValueReader.Read(row, property)
                : indexes.TryGetValue(column, out var index) && index < cells.Count && cells[index] is { } cell
                    ? ReadGridCellText(cell, preserveEmpty: true) : null;

        private Dictionary<GridRuntimeColumn, int> ReadVisibleIdentityCellIndexes(
            GridRowSetIdentity identity, NativeGridColumnHeader[] headers)
        {
            var names = headers.Select(static header => header.Name ?? string.Empty).ToArray();
            return identity.RuntimeColumns.Where(static column => column.RowIdentityAutomationProperty is null)
                .Where(column => column.RuntimeColumnName is { } name
                    ? names.Contains(name, StringComparer.Ordinal) : column.RuntimeColumnIndex.HasValue)
                .ToDictionary(column => column, column => ResolveRuntimeColumnIndex(column, names));
        }

        private ISelectionItemPattern RequireSelectionItem(AutomationElement element, AutomationElement root,
            bool allowVirtualized = false)
        {
            try
            {
                if (element.Properties.IsEnabled.TryGetValue(out var enabled) && !enabled)
                    throw new InvalidOperationException($"Grid '{AutomationId}' contains a disabled row required for selection.");
            }
            catch (COMException error) when (allowVirtualized && error.HResult == unchecked((int)0x80040201))
            {
                // An already-selected virtual row can expose SelectionItem but not IsEnabled.
                // Still require the pattern and verify that this grid owns it before removal.
            }
            var item = element.Patterns.SelectionItem.PatternOrDefault;
            if (item is null || !item.SelectionContainer.TryGetValue(out var owner)
                || owner is null || !root.Equals(owner))
                throw new MissingSelectionItemException($"Grid '{AutomationId}' does not expose a selection item owned by the current grid.");
            return item;
        }

        private sealed class MissingSelectionItemException(string message) : NotSupportedException(message);

        private sealed record RowSetState(IReadOnlyDictionary<string, GridRowSelector> Selected,
            string[]? OrderedView, bool AllowsEmpty, (GridRowSelector Key, AutomationElement Element)[]? Native,
            bool HasPublication);

        private sealed record NativeSelectionRead(int Count, (GridRowSelector Key, AutomationElement Element)[] Rows)
        {
            public bool IsComplete => Count == Rows.Length;
        }

        private static bool SameSelection(IReadOnlyDictionary<string, GridRowSelector> left,
            IReadOnlyDictionary<string, GridRowSelector> right) => left.Count == right.Count && left.Keys.All(right.ContainsKey);

        private void SetSelectedRowsWithGestures(AutomationElement root, SelectionPattern? selection, RowSetState initial,
            GridRowSetIdentity identity, IReadOnlyDictionary<string, GridRowSelector> expected, UiOperationTimeoutBudget budget)
        {
            var affected = initial.Selected.Concat(expected).DistinctBy(static pair => pair.Key)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
            IReadOnlyDictionary<string, int> viewPositions;
            if (initial.OrderedView is { } order)
            {
                viewPositions = order.Select((key, index) => (key, index))
                    .ToDictionary(static entry => entry.key, static entry => entry.index, StringComparer.Ordinal);
                if (!affected.Keys.All(viewPositions.ContainsKey))
                    throw new InvalidOperationException($"Grid '{AutomationId}' does not contain all requested rows in its current view.");
            }
            else
            {
                viewPositions = ProveRowSetTargets(root, identity, affected, budget);
            }

            var projected = initial.Selected;
            var targets = affected.ToDictionary(static pair => pair.Key, pair => identity.ToRuntimeSelector(pair.Value),
                StringComparer.Ordinal);
            var range = FindRange(initial.OrderedView, expected);
            var additions = expected.Keys.Where(key => !initial.Selected.ContainsKey(key)).ToArray();
            var removals = initial.Selected.Keys.Where(key => !expected.ContainsKey(key))
                .OrderBy(key => viewPositions[key]).ToArray();

            DesktopPointer.WithSelectionClicks(root, budget.Remaining, click =>
            {
                IEnumerable<string> preflight = range is null ? additions.Concat(removals) : expected.Keys;
                foreach (var key in preflight.OrderBy(key => viewPositions[key]))
                {
                    if (range is { } interval && key != interval.Start && key != interval.End)
                    {
                        if (!ResolveTarget(key).IsEnabled)
                            throw new InvalidOperationException($"Grid '{AutomationId}' contains a disabled row in the requested range.");
                        continue;
                    }
                    ClickTarget(key, PointerModifier.None, validateOnly: true);
                }
                var preparedState = ReadStableState();
                if (!SameSelection(preparedState.Selected, projected))
                    throw new InvalidOperationException($"Grid '{AutomationId}' selection changed during gesture preparation.");
                if (expected.Count > 0 && !preparedState.HasPublication && !HasAllIdentityColumnsVisible(identity))
                    throw new NotSupportedException($"Grid '{AutomationId}' cannot confirm the complete selection without scrolling. "
                        + "Expose a complete GridRowSelectionValue publication for identity columns in separate viewports.");

                if (range is { } bounds)
                {
                    var anchor = new Dictionary<string, GridRowSelector>(StringComparer.Ordinal) { [bounds.Start] = expected[bounds.Start] };
                    // A singleton selection alone does not prove where Shift is anchored.
                    Send(bounds.Start, PointerModifier.None, anchor);
                    Send(bounds.End, PointerModifier.Shift, expected);
                    return;
                }

                // Add before removing, so a grid that requires one selected row never has to go through empty.
                foreach (var key in additions)
                {
                    var next = new Dictionary<string, GridRowSelector>(projected, StringComparer.Ordinal) { [key] = expected[key] };
                    Send(key, projected.Count == 0 ? PointerModifier.None : PointerModifier.Control, next);
                }
                foreach (var key in removals)
                {
                    var next = new Dictionary<string, GridRowSelector>(projected, StringComparer.Ordinal);
                    next.Remove(key);
                    Send(key, PointerModifier.Control, next);
                }

                void Send(string key, PointerModifier modifier, IReadOnlyDictionary<string, GridRowSelector> next)
                {
                    ClickTarget(key, modifier);
                    UiWait.Until(ReadStableState, snapshot => SameSelection(snapshot.Selected, next),
                        new UiWaitOptions { Timeout = budget.Remaining, PollInterval = TimeSpan.FromMilliseconds(25) },
                        $"Grid '{AutomationId}' did not reach the expected full selection after {modifier} click.");
                    projected = next;
                }

                void ClickTarget(string key, PointerModifier modifier, bool validateOnly = false)
                {
                    var selector = targets[key];
                    AutomationElement? target = null;
                    GridIndexedCellCondition[]? visibleConditions = null;
                    click(() =>
                    {
                        target = ResolveTarget(key);
                        if (!validateOnly)
                            visibleConditions = VerifyPreparedRow(target, selector);
                        return target;
                    }, element => GridRowSelection.GetVisibleGridRowPoint(element, root, requireExactHit: true), modifier, () =>
                    {
                        if (!SameSelection(ReadStableState().Selected, projected))
                            throw new InvalidOperationException($"Grid '{AutomationId}' selection changed before the next click.");
                        VerifyPreparedRow(target!, selector, visibleConditions);
                    }, validateOnly);
                }
            });

            AutomationElement ResolveTarget(string key)
            {
                var selector = targets[key];
                return ResolveRowSetTarget(key, selector, identity, budget,
                    initial.OrderedView is not null || HasCompositeCellIdentity(identity) ? viewPositions : null).Element;
            }

            GridIndexedCellCondition[] VerifyPreparedRow(AutomationElement row, GridIndexedRowSelector selector,
                GridIndexedCellCondition[]? required = null)
            {
                _ = budget.Remaining;
                if (!BelongsToGrid(row, root))
                    throw new InvalidOperationException($"Grid '{AutomationId}' no longer owns the prepared row.");

                var headers = identity.RuntimeColumns.Any(static column => column.RowIdentityAutomationProperty is null)
                    ? ReadNativeColumnHeaders() : [];
                var indexes = ReadVisibleIdentityCellIndexes(identity, headers);
                var conditions = required ?? selector.Conditions.Where(condition =>
                    condition.Column.RowIdentityAutomationProperty is not null || indexes.ContainsKey(condition.Column)).ToArray();
                var cells = indexes.Count > 0 ? ResolveNativeRowCells(row, headers, indexes.Values) : [];
                if (conditions.Length == 0 || conditions.Any(condition =>
                    !string.Equals(ReadRowIdentityValue(row, cells, indexes, condition.Column), condition.ExpectedText,
                        StringComparison.Ordinal)))
                    throw new InvalidOperationException($"Grid '{AutomationId}' row identity changed before the next click.");
                return conditions;
            }

            RowSetState ReadStableState()
            {
                var state = ReadRowSet(root, selection, identity, budget);
                if (initial.OrderedView is { } published && (state.OrderedView is null
                    || !published.SequenceEqual(state.OrderedView, StringComparer.Ordinal)))
                    throw new InvalidOperationException($"Grid '{AutomationId}' view changed during selection.");
                return state;
            }
        }

        private NativeFlaUiRow RevealOrderedRow(string key,
            IReadOnlyDictionary<string, int> view, GridRowSetIdentity identity, UiOperationTimeoutBudget budget,
            RowSetProjection[]? firstProjection)
        {
            var scroll = FindGridScrollState();
            var range = scroll.RangeValuePattern;
            var minimum = scroll.ScrollPattern is not null ? 0 : range?.Minimum.Value ?? 0;
            var maximum = scroll.ScrollPattern is not null ? 100 : range?.Maximum.Value ?? 0;
            var stopwatch = Stopwatch.StartNew();
            var timeoutMs = budget.RemainingMilliseconds;
            var targetIndex = view[key];
            var previousRequest = double.NaN;
            var alreadyChecked = firstProjection is not null;
            while (true)
            {
                _ = budget.Remaining;
                var visible = firstProjection ?? ReadRowSetProjection(identity, budget);
                firstProjection = null;
                if (!alreadyChecked && FindProjectedTarget(visible, key) is { } match) return match;
                alreadyChecked = false;
                if (maximum <= minimum) break;
                var position = ReadGridScrollPosition(scroll);
                var current = position.ScrollPercent ?? position.RangeValue;
                if (current is null || !double.IsFinite(current.Value)) break;
                var visiblePositions = visible.Select(static row => row.Key).OfType<string>()
                    .Where(view.ContainsKey).Select(item => view[item]).ToArray();
                if (visiblePositions.Length == 0) break;
                if (targetIndex < visiblePositions.Min()) maximum = Math.Min(maximum, current.Value);
                else if (targetIndex > visiblePositions.Max()) minimum = Math.Max(minimum, current.Value);
                else break; // A gap in the visible key projection is not permission to click by position.

                var requested = (minimum + maximum) / 2;
                if (requested == current.Value || requested == previousRequest) break;
                previousRequest = requested;
                var next = scroll.ScrollPattern is not null ? new GridScrollPosition(requested, null)
                    : new GridScrollPosition(null, requested);
                _ = TryRestoreGridScrollPosition(scroll, next, stopwatch, timeoutMs, allowUserInput: false);
                scroll = FindGridScrollState();
            }
            throw new InvalidOperationException($"Grid '{AutomationId}' could not reveal the requested row in its current view.");
        }

        private NativeFlaUiRow ResolveRowSetTarget(string key, GridIndexedRowSelector selector,
            GridRowSetIdentity identity, UiOperationTimeoutBudget budget, IReadOnlyDictionary<string, int>? view)
        {
            RowSetProjection[]? projection = null;
            if (identity.RuntimeColumns.All(static column => column.RowIdentityAutomationProperty.HasValue))
            {
                if (TryResolveVisibleNativeRow(selector, forSelection: true) is { } row) return row;
            }
            else
            {
                projection = ReadRowSetProjection(identity, budget);
                if (FindProjectedTarget(projection, key) is { } row) return row;
            }

            return view is not null
                ? RevealOrderedRow(key, view, identity, budget, projection)
                : ResolveUniqueNativeRow(selector, Stopwatch.StartNew(), budget.RemainingMilliseconds, allowUserInput: false);
        }

        private static bool HasCompositeCellIdentity(GridRowSetIdentity identity) =>
            identity.RuntimeColumns.Count(static column => column.RowIdentityAutomationProperty is null) > 1;

        private NativeFlaUiRow? FindProjectedTarget(IEnumerable<RowSetProjection> rows, string key)
        {
            var matches = rows.Where(row => string.Equals(row.Key, key, StringComparison.Ordinal)).Take(2).ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException($"Grid '{AutomationId}' exposes multiple rows for a requested stable key.");
            return matches.SingleOrDefault()?.Row;
        }

        private sealed record RowSetProjection(NativeFlaUiRow Row, GridRowSelector? Identity)
        {
            public string? Key { get; } = Identity is null ? null : GridRowSelectionSet.Key(Identity);
        }

        private RowSetProjection[] ReadRowSetProjection(GridRowSetIdentity identity, UiOperationTimeoutBudget budget)
        {
            _ = budget.Remaining;
            var cellColumns = identity.RuntimeColumns.Where(static column => column.RowIdentityAutomationProperty is null).ToArray();
            var headers = cellColumns.Length > 0 ? ReadNativeColumnHeaders() : [];
            var indexes = ReadVisibleIdentityCellIndexes(identity, headers);
            if (indexes.Count == cellColumns.Length)
            {
                var rows = ReadNativeDataRows(includeCells: indexes.Count > 0, includeIncompleteRows: true,
                    requiredCellIndexes: indexes.Values.ToArray(), knownHeaders: headers);
                return rows.Select(row => new RowSetProjection(row, ReadKey(column =>
                    ReadRowIdentityValue(row.Element, row.Cells, indexes, column)))).ToArray();
            }

            var initialRows = ReadNativeDataRows(includeCells: false);
            if (initialRows.Length == 0) return [];
            var scroll = ReadGridScrollPosition(FindGridScrollState(includeNavigationControls: false));
            var anchors = initialRows.Select(CaptureNativeRowAnchor).ToArray();
            var latest = initialRows;
            var columnValues = new Dictionary<GridRuntimeColumn, string?[]>();
            var stopwatch = Stopwatch.StartNew();
            var timeoutMs = budget.RemainingMilliseconds;
            foreach (var column in cellColumns)
            {
                _ = budget.Remaining;
                if (!indexes.TryGetValue(column, out var index))
                {
                    EnsureNativeColumnVisible(column, stopwatch, timeoutMs, allowUserInput: false);
                    headers = ReadNativeColumnHeaders();
                    indexes = ReadVisibleIdentityCellIndexes(identity, headers);
                    if (!indexes.TryGetValue(column, out index))
                        throw new InvalidOperationException($"Grid '{AutomationId}' does not expose identity column '{column.RuntimeColumnName ?? column.SourceFieldName}'.");
                }
                var rows = ReadNativeDataRows(includeIncompleteRows: true, requiredCellIndexes: [index], knownHeaders: headers);
                var currentScroll = ReadGridScrollPosition(FindGridScrollState(includeNavigationControls: false));
                latest = anchors.Select(anchor => RebindNativeRowAfterHorizontalScroll(anchor, scroll, rows, currentScroll,
                    requireUnique: true)).ToArray();
                if (latest.Distinct(ReferenceEqualityComparer.Instance).Count() != latest.Length)
                    throw new InvalidOperationException($"Grid '{AutomationId}' could not associate each identity cell with a distinct row.");
                columnValues.Add(column, latest.Select(row => ReadCell(row, index)).ToArray());
            }

            return latest.Select((row, index) => new RowSetProjection(row, ReadKey(column =>
                column.RowIdentityAutomationProperty is { } property
                    ? FlaUiGridRowAutomationValueReader.Read(row.Element, property)
                    : columnValues[column][index]))).ToArray();

            GridRowSelector? ReadKey(Func<GridRuntimeColumn, string?> read)
            {
                _ = budget.Remaining;
                return identity.TryRead(read);
            }

            static string? ReadCell(NativeFlaUiRow row, int index) =>
                row.HasCell(index) ? ReadGridCellText(row.Cells[index], preserveEmpty: true) : null;
        }

        private static (string Start, string End)? FindRange(string[]? order,
            IReadOnlyDictionary<string, GridRowSelector> expected)
        {
            if (order is null || expected.Count < 2) return null;
            var positions = order.Select((key, index) => (key, index)).Where(pair => expected.ContainsKey(pair.key))
                .Select(static pair => pair.index).ToArray();
            if (positions.Length != expected.Count || positions[^1] - positions[0] + 1 != expected.Count) return null;
            var first = order[positions[0]];
            var last = order[positions[^1]];
            return expected.Keys.First() == last ? (last, first) : (first, last);
        }

        private IReadOnlyDictionary<string, int> ProveRowSetTargets(AutomationElement root, GridRowSetIdentity identity,
            IReadOnlyDictionary<string, GridRowSelector> expected, UiOperationTimeoutBudget budget)
        {
            var stopwatch = Stopwatch.StartNew();
            var timeoutMs = budget.RemainingMilliseconds;
            var visible = ReadRowSetProjection(identity, budget);
            var rowCount = TryRead(() => root.Patterns.Grid.PatternOrDefault?.RowCount.Value);
            var rows = new List<NativeGridRowSnapshot>();
            if (rowCount is { } count && count == visible.Length)
            {
                Observe(visible, new GridScrollPosition(null, null));
                if (rows.Count != count)
                    throw new InvalidOperationException($"Grid '{AutomationId}' did not expose a distinct row for every grid position.");
            }
            else
            {
                var scroll = FindGridScrollState();
                MoveGridScrollToStart(scroll, stopwatch, timeoutMs, allowUserInput: false);
                scroll = FindGridScrollState();
                var continuous = IsGridScrollAtBoundary(scroll, forward: false);
                NativeGridRowSnapshot[]? previous = null;
                NativeGridTraversal.Scan(() => ReadRowSetProjection(identity, budget), observed =>
                    {
                        var current = Observe(observed, ReadGridScrollPosition(scroll));
                        continuous &= NativeGridRowNormalizer.HasContinuousObservation(previous, current, HasSameNativeRow);
                        previous = current;
                    },
                    observed => CreateNativeRowSignature(observed.Select(static row => row.Row)),
                    (_, observed) => MoveGridScrollForward(scroll, stopwatch, timeoutMs,
                        rangeIncrement: EstimateNativeScrollIncrement(observed.Select(static row => row.Row).ToArray()),
                        allowUserInput: false));
                if (!continuous || !IsGridScrollAtBoundary(FindGridScrollState(), forward: true)
                    || rowCount is { } total && rows.Count != total)
                    throw new InvalidOperationException($"Grid '{AutomationId}' could not prove uniqueness across its current view.");
            }

            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < rows.Count; index++)
            {
                _ = budget.Remaining;
                var key = rows[index].StableValues[0]!;
                if (positions.TryAdd(key, index)) continue;
                if (expected.ContainsKey(key))
                    throw new InvalidOperationException($"Grid '{AutomationId}' has more than one row for a requested stable key.");
                ambiguous.Add(key);
            }
            foreach (var key in ambiguous) positions.Remove(key);

            foreach (var (key, row) in expected)
                if (!positions.ContainsKey(key))
                    throw new InvalidOperationException($"Grid '{AutomationId}' does not contain {GridRuntimeResolver.DescribeRowSelector(row)} in its current view.");
            return positions;

            NativeGridRowSnapshot[] Observe(IReadOnlyList<RowSetProjection> observed, GridScrollPosition position)
            {
                var observation = rows.Count == 0 ? 0 : rows.Max(static row => row.ObservationIndex) + 1;
                var snapshots = observed.Select(projected =>
                {
                    var key = projected.Key
                        ?? throw new InvalidOperationException($"Grid '{AutomationId}' has a row without a readable complete stable key.");
                    var row = projected.Row;
                    return new NativeGridRowSnapshot([], ReadNativeGridRowIndex(row), row.Element.BoundingRectangle, position)
                    {
                        RuntimeId = ReadNativeRowRuntimeId(row.Element),
                        StableValues = [key],
                        ObservationIndex = observation
                    };
                }).ToArray();
                return AppendNativeObservation(rows, snapshots);
            }
        }
    }
}
