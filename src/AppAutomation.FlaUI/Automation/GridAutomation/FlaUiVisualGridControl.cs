using AppAutomation.FlaUI.Input;
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
    internal static bool IsEmptyNativeDateCellProjection(
        string? name,
        string? value,
        ControlType controlType,
        bool hasTextPattern,
        int descendantCount)
    {
        // Some grid providers publish a row ordinal as the Name of an otherwise empty date cell.
        return controlType == ControlType.Group
            && name is { Length: > 0 }
            && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            && string.IsNullOrWhiteSpace(value)
            && !hasTextPattern
            && descendantCount == 0;
    }

    private sealed class FlaUiVisualGridControl :
        IGridUserActionControl,
        IEditableGridControl,
        IIndexedAddressableGridControl,
        IAddressableGridControl,
        IGridRowSelectionControl,
        IIndexedGridRowSelectionControl,
        IGridColumnMetadataControl
    {
        private readonly Window _searchRoot;
        private readonly IGridControl? _fallback;
        private AutomationElement? _gridRoot;
        private NativeFlaUiRow[]? _prefetchedNativeRows;
        private int[] _nativeSignatureColumnIndexes = [];
        private GridRowAutomationProperty[] _nativeSignatureRowProperties = [];

        public FlaUiVisualGridControl(
            Window searchRoot,
            string automationId,
            IGridControl? fallback = null)
        {
            _searchRoot = searchRoot ?? throw new ArgumentNullException(nameof(searchRoot));
            _fallback = fallback;
            AutomationId = string.IsNullOrWhiteSpace(automationId)
                ? throw new ArgumentException("Automation id cannot be empty.", nameof(automationId))
                : automationId;
        }

        public string AutomationId { get; }

        public string Name => AutomationId;

        public bool IsEnabled => TryRead(() => FindGridRoot()?.IsEnabled)
            ?? _fallback?.IsEnabled
            ?? false;

        public IReadOnlyList<IGridRowControl> Rows =>
            ReadVisualRows();

        public IReadOnlyList<string> ColumnNames => ReadColumnNames();

        public IGridRowControl? GetRowByIndex(int index)
        {
            var row = ReadRows()
                .FirstOrDefault(candidate =>
                    ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row") == index);
            if (row is not null)
            {
                return new FlaUiVisualGridRowControl(row);
            }

            var cellRow = ReadCellRows()
                .FirstOrDefault(candidate => candidate.RowIndex == index);
            if (cellRow is not null && cellRow.Cells.Count > 0)
            {
                return new FlaUiVisualGridCellBackedRowControl(cellRow.Cells);
            }

            return _fallback?.GetRowByIndex(index);
        }

        public bool TryGetColumnIndex(string columnName, out int columnIndex)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
            var matches = ColumnNames
                .Select((name, index) => (name, index))
                .Where(candidate => string.Equals(
                    candidate.name,
                    columnName.Trim(),
                    StringComparison.Ordinal))
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
            if (!HasNativeDataRows())
            {
                return ResolveRow(MapRow(row), timeoutMs);
            }

            var scan = ScanNativeRows(row, Stopwatch.StartNew(), timeoutMs);
            var description = DescribeNativeResolution(row, scan.MatchingRows.Count, scan.Rows);
            return scan.MatchingRows.Count switch
            {
                0 => GridRowResolution.NotFound(description),
                1 => GridRowResolution.Unique(description),
                _ => GridRowResolution.Ambiguous(scan.MatchingRows.Count, description)
            };
        }

        public GridCellValueSnapshot ReadCell(GridCellAddress address, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(address);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            if (HasNativeDataRows())
            {
                var stopwatch = Stopwatch.StartNew();
                EnsureNativeColumnVisible(address.ColumnName, stopwatch, timeoutMs);
                var row = ResolveUniqueNativeRow(address.Row, stopwatch, timeoutMs);
                var columnIndex = ResolveNamedColumnIndex(address.ColumnName);
                var displayText = ReadVisualGridCellText(row.Cells[columnIndex]);
                return new GridCellValueSnapshot(
                    displayText,
                    displayText,
                    GridCellValueKind.Text)
                { IsDisplayOnly = true };
            }

            return ReadCell(
                MapRow(address.Row),
                CreateRuntimeColumn(address.ColumnName),
                timeoutMs);
        }

        public string CopyCell(GridCellAddress address, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(address);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            if (HasNativeDataRows())
            {
                var stopwatch = Stopwatch.StartNew();
                EnsureNativeColumnVisible(address.ColumnName, stopwatch, timeoutMs);
                var row = ResolveUniqueNativeRow(address.Row, stopwatch, timeoutMs);
                var columnIndex = ResolveNamedColumnIndex(address.ColumnName);
                var cell = row.Cells[columnIndex];
                if (TryRead(() => cell.Patterns.Value.IsSupported))
                {
                    var value = TryRead(() => cell.Patterns.Value.Pattern.Value);
                    if (value is not null)
                    {
                        return value;
                    }
                }

                return ReadVisualGridCellText(cell) ?? string.Empty;
            }

            return CopyCell(
                MapRow(address.Row),
                CreateRuntimeColumn(address.ColumnName),
                timeoutMs);
        }

        public void EditCell(
            GridCellAddress address,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(address);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            if (HasNativeDataRows())
            {
                var stopwatch = Stopwatch.StartNew();
                EnsureNativeColumnVisible(address.ColumnName, stopwatch, timeoutMs);
                var columnIndex = ResolveNamedColumnIndex(address.ColumnName);
                var selectorColumnIndexes = address.Row.Conditions
                    .Select(condition => ResolveNamedColumnIndex(condition.ColumnName))
                    .Distinct()
                    .ToArray();
                var row = TryResolveVisibleNativeRow(
                        selectorColumnIndexes,
                        Array.Empty<GridRowAutomationProperty>(),
                        candidate => NativeRowMatches(candidate, address.Row),
                        candidate => NativeRowMatches(candidate, address.Row),
                        out var visibleRow)
                    ? visibleRow!
                    : ResolveUniqueNativeRow(address.Row, stopwatch, timeoutMs);
                var nativeRemaining = RemainingGridMilliseconds(stopwatch, timeoutMs);
                EditResolvedCell(
                    row.Cells[columnIndex],
                    timeout => TryResolveVisibleNativeRow(
                            selectorColumnIndexes,
                            Array.Empty<GridRowAutomationProperty>(),
                            candidate => NativeRowMatches(candidate, address.Row),
                            candidate => NativeRowMatches(candidate, address.Row),
                            out var refreshedVisibleRow)
                        ? refreshedVisibleRow!.Cells[columnIndex]
                        : ResolveUniqueNativeRow(
                            address.Row,
                            Stopwatch.StartNew(),
                            timeout).Cells[columnIndex],
                    GridCellEditRequestValidation.CreateIndexedRequest(
                        0,
                        columnIndex,
                        request,
                        nativeRemaining));
                return;
            }

            var column = CreateRuntimeColumn(
                address.ColumnName,
                request.EditorKind,
                request.EditorParts);
            EditCell(MapRow(address.Row), column, request, timeoutMs);
        }

        public void OpenRow(GridRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            if (HasNativeDataRows())
            {
                var stopwatch = Stopwatch.StartNew();
                var target = ResolveUniqueNativeRow(row, stopwatch, timeoutMs).Element;
                if (TryDoubleClick(target, out var exception))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Grid '{AutomationId}' stable row could not be opened by double-click.",
                    exception);
            }

            OpenRow(MapRow(row), timeoutMs);
        }

        public void SelectRow(GridRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var stopwatch = Stopwatch.StartNew();
            if (HasNativeDataRows())
            {
                var target = ResolveNativeRowForSelection(row, stopwatch, timeoutMs).Element;
                SelectAndConfirmGridRow(
                    target,
                    FindGridRoot(),
                    RemainingGridMilliseconds(stopwatch, timeoutMs),
                    $"Grid '{AutomationId}' row '{GridRuntimeResolver.DescribeRowSelector(row)}'",
                    () => TryResolveVisibleNativeRow(row, forSelection: true)?.Element,
                    () => FindGridRoot());
                return;
            }

            SelectRow(MapRow(row), timeoutMs);
        }

        private NativeFlaUiRow ResolveNativeRowForSelection(
            GridRowSelector selector,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var selectorColumnIndexes = selector.Conditions
                .Select(condition => ResolveNamedColumnIndex(condition.ColumnName))
                .Distinct()
                .ToArray();
            var scroll = FindGridScrollState();

            NativeFlaUiRow? TryResolveCurrentViewport(IReadOnlyList<NativeFlaUiRow> visibleRows)
            {
                var snapshots = new List<NativeGridRowSnapshot>();
                AppendNativeRows(
                    snapshots,
                    visibleRows,
                    ReadGridScrollPosition(FindGridScrollState()),
                    selectorColumnIndexes,
                    Array.Empty<GridRowAutomationProperty>());
                if (!NativeGridVisibleResolution.TryResolve(
                        hasDeclaredUniqueIdentity: true,
                        snapshots,
                        visibleRows,
                        candidate => NativeRowMatches(candidate, selector),
                        candidate => NativeRowMatches(candidate, selector),
                        out var visibleMatches,
                        out var liveVisibleMatch))
                {
                    return null;
                }

                if (visibleMatches.Length != 1 || liveVisibleMatch is null)
                {
                    throw new InvalidOperationException(
                        $"Grid '{AutomationId}' visible stable selector matched {visibleMatches.Length} logical rows; expected exactly one.");
                }

                return liveVisibleMatch;
            }

            var currentRows = TakePrefetchedNativeRows();
            if (TryResolveCurrentViewport(currentRows) is { } currentMatch)
            {
                return currentMatch;
            }

            MoveGridScrollToStart(scroll, stopwatch, timeoutMs);
            scroll = FindGridScrollState();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                var visibleRows = TakePrefetchedNativeRows();
                if (TryResolveCurrentViewport(visibleRows) is { } match)
                {
                    return match;
                }

                var signature = CreateNativeRowSignature(visibleRows);
                if (!MoveGridScrollForward(
                        scroll,
                        stopwatch,
                        timeoutMs,
                        signature,
                        EstimateNativeScrollIncrement(visibleRows)))
                {
                    break;
                }
            }

            throw new InvalidOperationException(
                $"Grid '{AutomationId}' did not materialize stable row '{GridRuntimeResolver.DescribeRowSelector(selector)}' within the operation timeout.");
        }

        private bool HasNativeDataRows()
        {
            if (ReadNativeColumnHeaders().Length == 0)
            {
                return false;
            }

            _prefetchedNativeRows ??= ReadNativeDataRows();
            return true;
        }

        private bool TryResolveVisibleNativeRow(
            IReadOnlyList<int> selectorColumnIndexes,
            IReadOnlyList<GridRowAutomationProperty> selectorRowProperties,
            Func<NativeGridRowSnapshot, bool> snapshotMatches,
            Func<NativeFlaUiRow, bool> liveRowMatches,
            out NativeFlaUiRow? matchingRow,
            bool forSelection = false)
        {
            matchingRow = null;
            var visibleRows = forSelection && selectorColumnIndexes.Count == 0
                ? ReadNativeDataRows(includeCells: false)
                : TakePrefetchedNativeRows();
            var snapshots = new List<NativeGridRowSnapshot>();
            AppendNativeRows(
                snapshots,
                visibleRows,
                forSelection
                    ? new GridScrollPosition(null, null)
                    : ReadGridScrollPosition(FindGridScrollState()),
                selectorColumnIndexes,
                selectorRowProperties);
            if (!NativeGridVisibleResolution.TryResolve(
                    hasDeclaredUniqueIdentity: true,
                    snapshots,
                    visibleRows,
                    snapshotMatches,
                    liveRowMatches,
                    out var visibleMatches,
                    out var liveVisibleMatch))
            {
                return false;
            }

            if (visibleMatches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Grid '{AutomationId}' visible stable selector matched {visibleMatches.Length} logical rows; "
                    + "expected exactly one before reading the edited cell.");
            }

            if (liveVisibleMatch is null)
            {
                return false;
            }

            matchingRow = liveVisibleMatch;
            return true;
        }

        private NativeFlaUiRow? TryResolveVisibleNativeRow(GridRowSelector selector, bool forSelection = false)
        {
            var selectorColumnIndexes = selector.Conditions
                .Select(condition => ResolveNamedColumnIndex(condition.ColumnName))
                .Distinct()
                .ToArray();
            return TryResolveVisibleNativeRow(
                selectorColumnIndexes,
                Array.Empty<GridRowAutomationProperty>(),
                candidate => NativeRowMatches(candidate, selector),
                candidate => NativeRowMatches(candidate, selector),
                out var matchingRow,
                forSelection)
                ? matchingRow
                : null;
        }

        private NativeFlaUiRow? TryResolveVisibleNativeRow(GridIndexedRowSelector selector, bool forSelection = false)
        {
            var selectorColumnIndexes = selector.Conditions
                .Where(static condition => condition.Column.RowIdentityAutomationProperty is null)
                .Select(condition => ResolveRuntimeColumnIndex(condition.Column))
                .Distinct()
                .ToArray();
            var selectorRowProperties = selector.Conditions
                .Select(static condition => condition.Column.RowIdentityAutomationProperty)
                .Where(static property => property is not null)
                .Select(static property => property!.Value)
                .Distinct()
                .ToArray();
            return TryResolveVisibleNativeRow(
                selectorColumnIndexes,
                selectorRowProperties,
                candidate => NativeRowMatches(candidate, selector),
                candidate => NativeRowMatches(candidate, selector),
                out var matchingRow,
                forSelection)
                ? matchingRow
                : null;
        }

        private NativeFlaUiRow ResolveUniqueNativeRow(
            GridRowSelector selector,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var scan = ScanNativeRows(selector, stopwatch, timeoutMs);
            if (scan.MatchingRows.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Grid row selector matched {scan.MatchingRows.Count} rows in grid '{AutomationId}'; expected exactly one. "
                    + DescribeNativeResolution(selector, scan.MatchingRows.Count, scan.Rows));
            }

            if (scan.LiveMatchingRow is { } liveMatch
                && TryRead(() => liveMatch.Element.IsAvailable)
                && NativeRowMatches(liveMatch, selector))
            {
                return liveMatch;
            }

            var scroll = FindGridScrollState();
            if (TryRestoreGridScrollPosition(
                    scroll,
                    scan.MatchingRows[0].ScrollPosition,
                    stopwatch,
                    timeoutMs))
            {
                var restoredMatch = TakePrefetchedNativeRows()
                    .FirstOrDefault(row => row.IsVisible && NativeRowMatches(row, selector));
                if (restoredMatch is not null)
                {
                    return restoredMatch;
                }
            }

            MoveGridScrollToStart(scroll, stopwatch, timeoutMs);
            do
            {
                var visibleRows = TakePrefetchedNativeRows();
                var match = visibleRows.FirstOrDefault(row => NativeRowMatches(row, selector));
                if (match is not null)
                {
                    return match;
                }

                var previousSignature = CreateNativeRowSignature(visibleRows);
                if (!MoveGridScrollForward(
                        scroll,
                        stopwatch,
                        timeoutMs,
                        previousSignature,
                        EstimateNativeScrollIncrement(visibleRows)))
                {
                    var finalRows = ReadNativeDataRows();
                    if (!string.Equals(
                            previousSignature,
                            CreateNativeRowSignature(finalRows),
                            StringComparison.Ordinal))
                    {
                        match = finalRows.FirstOrDefault(row => NativeRowMatches(row, selector));
                        if (match is not null)
                        {
                            return match;
                        }
                    }

                    break;
                }
            }
            while (true);

            throw new InvalidOperationException(
                $"Grid '{AutomationId}' stable row disappeared during bounded traversal.");
        }

        private NativeFlaUiRow ResolveUniqueNativeRow(
            GridIndexedRowSelector selector,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var scan = ScanNativeRows(selector, stopwatch, timeoutMs);
            if (scan.MatchingRows.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Grid row selector matched {scan.MatchingRows.Count} rows in grid '{AutomationId}'; expected exactly one. "
                    + DescribeIndexedNativeResolution(selector, scan.MatchingRows.Count, scan.Rows));
            }

            if (scan.LiveMatchingRow is { } liveMatch
                && TryRead(() => liveMatch.Element.IsAvailable)
                && NativeRowMatches(liveMatch, selector))
            {
                return liveMatch;
            }

            var target = scan.MatchingRows[0];
            var scroll = FindGridScrollState();
            if (TryRestoreGridScrollPosition(scroll, target.ScrollPosition, stopwatch, timeoutMs))
            {
                var restoredMatch = TakePrefetchedNativeRows()
                    .FirstOrDefault(row => row.IsVisible && NativeRowMatches(row, selector));
                if (restoredMatch is not null)
                {
                    return restoredMatch;
                }
            }

            MoveGridScrollToStart(scroll, stopwatch, timeoutMs);
            do
            {
                var visibleRows = TakePrefetchedNativeRows();
                var match = visibleRows.FirstOrDefault(row => NativeRowMatches(row, selector));
                if (match is not null)
                {
                    return match;
                }

                var previousSignature = CreateNativeRowSignature(visibleRows);
                if (!MoveGridScrollForward(
                        scroll,
                        stopwatch,
                        timeoutMs,
                        previousSignature,
                        EstimateNativeScrollIncrement(visibleRows)))
                {
                    break;
                }
            }
            while (true);

            throw new InvalidOperationException(
                $"Grid '{AutomationId}' stable row disappeared during bounded traversal.");
        }

        private NativeFlaUiRow ResolveIndexedNativeRowForColumn(
            GridIndexedRowSelector selector,
            GridRuntimeColumn targetColumn,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var visibleSelectorColumns = selector.Conditions
                .Where(static condition => condition.Column.RowIdentityAutomationProperty is null)
                .Select(static condition => condition.Column)
                .ToArray();
            if (visibleSelectorColumns.Length > 1 && selector.HasDeclaredUniqueIdentity)
            {
                return ResolveCompositeNativeRowForColumn(selector, targetColumn, stopwatch, timeoutMs);
            }

            if (visibleSelectorColumns.Length != 1)
            {
                EnsureNativeColumnVisible(targetColumn, stopwatch, timeoutMs);
                return ResolveUniqueNativeRow(selector, stopwatch, timeoutMs);
            }

            EnsureNativeColumnVisible(visibleSelectorColumns[0], stopwatch, timeoutMs);
            var identifiedRow = ResolveUniqueNativeRow(selector, stopwatch, timeoutMs);
            var originalScroll = ReadGridScrollPosition(FindGridScrollState());
            var originalAnchor = CaptureNativeRowAnchor(identifiedRow);
            EnsureNativeColumnVisible(targetColumn, stopwatch, timeoutMs);
            return RebindNativeRowAfterHorizontalScroll(originalAnchor, originalScroll);
        }

        private NativeFlaUiRow ResolveCompositeNativeRowForColumn(
            GridIndexedRowSelector selector,
            GridRuntimeColumn targetColumn,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var visibleRows = WaitForNativeDataRows(stopwatch, timeoutMs);
            if (TryResolveCompositeVisibleRow(selector, targetColumn, visibleRows, stopwatch, timeoutMs)
                is { } visibleMatch)
            {
                return visibleMatch;
            }

            var scroll = FindGridScrollState();
            MoveGridScrollToStart(scroll, stopwatch, timeoutMs);
            do
            {
                visibleRows = TakePrefetchedNativeRows();
                if (TryResolveCompositeVisibleRow(selector, targetColumn, visibleRows, stopwatch, timeoutMs)
                    is { } match)
                {
                    return match;
                }

                visibleRows = ReadNativeDataRows();
                var signature = CreateNativeRowSignature(visibleRows);
                if (!MoveGridScrollForward(
                        scroll, stopwatch, timeoutMs, signature, EstimateNativeScrollIncrement(visibleRows)))
                {
                    break;
                }
            }
            while (true);

            throw new InvalidOperationException(
                $"Grid '{AutomationId}' did not expose a row matching its configured composite identity.");
        }

        private NativeFlaUiRow? TryResolveCompositeVisibleRow(
            GridIndexedRowSelector selector,
            GridRuntimeColumn targetColumn,
            IReadOnlyList<NativeFlaUiRow> visibleRows,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var originalScroll = ReadGridScrollPosition(FindGridScrollState());
            var candidates = visibleRows.Where(static row => row.IsVisible)
                .Select(row => (Row: row, Anchor: CaptureNativeRowAnchor(row)))
                .ToArray();
            if (candidates.Length == 0)
            {
                return null;
            }
            foreach (var condition in selector.Conditions)
            {
                if (condition.Column.RowIdentityAutomationProperty is { } rowProperty)
                {
                    candidates = candidates.Where(candidate => string.Equals(
                        FlaUiGridRowAutomationValueReader.Read(candidate.Row.Element, rowProperty),
                        condition.ExpectedText, StringComparison.Ordinal)).ToArray();
                    continue;
                }

                EnsureNativeColumnVisible(condition.Column, stopwatch, timeoutMs);
                var currentRows = ReadNativeDataRows();
                var currentScroll = ReadGridScrollPosition(FindGridScrollState());
                var columnIndex = ResolveRuntimeColumnIndex(condition.Column);
                candidates = candidates.Select(candidate => (
                        Row: RebindNativeRowAfterHorizontalScroll(
                            candidate.Anchor, originalScroll, currentRows, currentScroll),
                        candidate.Anchor))
                    .Where(candidate => string.Equals(candidate.Row.GetCellText(columnIndex),
                        condition.ExpectedText, StringComparison.Ordinal))
                    .ToArray();
                if (candidates.Length == 0)
                {
                    return null;
                }
            }

            if (candidates.Length == 0)
            {
                return null;
            }
            var logicalRows = new List<NativeGridRowSnapshot>();
            NativeGridRowNormalizer.Append(logicalRows, candidates.Select(candidate =>
                new NativeGridRowSnapshot([], candidate.Anchor.Index, candidate.Anchor.Bounds, originalScroll)
                {
                    StableValues = selector.Conditions.Select(static condition => (string?)condition.ExpectedText).ToArray(),
                    RuntimeId = candidate.Anchor.RuntimeId is { Length: > 0 } runtimeId
                        ? string.Join(',', runtimeId)
                        : null
                }).ToArray(), HasSameNativeRow);
            if (logicalRows.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Grid '{AutomationId}' composite stable selector matched {logicalRows.Count} visible rows; "
                    + "expected exactly one.");
            }

            EnsureNativeColumnVisible(targetColumn, stopwatch, timeoutMs);
            return RebindNativeRowAfterHorizontalScroll(candidates[0].Anchor, originalScroll);
        }

        private static (System.Drawing.Rectangle Bounds, int? Index, int[]? RuntimeId) CaptureNativeRowAnchor(
            NativeFlaUiRow row) =>
            (TryRead(() => row.Element.BoundingRectangle),
                ReadNativeGridRowIndex(row),
                TryRead(() => row.Element.FrameworkAutomationElement.RuntimeId.ValueOrDefault));

        private NativeFlaUiRow RebindNativeRowAfterHorizontalScroll(
            (System.Drawing.Rectangle Bounds, int? Index, int[]? RuntimeId) originalAnchor,
            GridScrollPosition originalScroll,
            IReadOnlyList<NativeFlaUiRow>? observedRows = null,
            GridScrollPosition? observedScroll = null)
        {
            var visibleRows = observedRows ?? ReadNativeDataRows();
            var currentScroll = observedScroll ?? ReadGridScrollPosition(FindGridScrollState());
            if ((originalScroll.ScrollPercent is { } originalPercent
                    && currentScroll.ScrollPercent is { } currentPercent
                    && Math.Abs(originalPercent - currentPercent) > 0.001)
                || (originalScroll.RangeValue is { } originalRange
                    && currentScroll.RangeValue is { } currentRange
                    && Math.Abs(originalRange - currentRange) > 0.001))
            {
                throw new InvalidOperationException(
                    $"Grid '{AutomationId}' changed its vertical position while materializing the target column; "
                    + "the previously identified row cannot be reused safely.");
            }

            var (originalBounds, originalIndex, originalRuntimeId) = originalAnchor;
            if (originalRuntimeId is { Length: > 0 })
            {
                var sameRuntime = visibleRows.FirstOrDefault(candidate =>
                    originalRuntimeId.SequenceEqual(
                        TryRead(() => candidate.Element.FrameworkAutomationElement.RuntimeId.ValueOrDefault)
                        ?? [])
                    && (originalIndex is null
                        || ReadNativeGridRowIndex(candidate) is not { } candidateIndex
                        || originalIndex == candidateIndex));
                if (sameRuntime is not null)
                {
                    return sameRuntime;
                }
            }

            var sameRowArea = visibleRows.Where(candidate =>
            {
                var bounds = TryRead(() => candidate.Element.BoundingRectangle);
                var candidateIndex = ReadNativeGridRowIndex(candidate);
                return originalBounds.Height > 0
                    && Math.Abs(bounds.Top - originalBounds.Top) <= 3
                    && Math.Abs(bounds.Bottom - originalBounds.Bottom) <= 3
                    && (originalIndex is null || candidateIndex is null || originalIndex == candidateIndex);
            }).ToArray();
            if (sameRowArea.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Grid '{AutomationId}' could not rebind the identified row after horizontal column navigation.");
            }

            return sameRowArea[0];
        }

        private NativeGridScan ScanNativeRows(
            GridRowSelector selector,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var selectorColumnIndexes = selector.Conditions
                .Select(condition => ResolveNamedColumnIndex(condition.ColumnName))
                .Distinct()
                .ToArray();
            return ScanNativeRows(
                selectorColumnIndexes,
                Array.Empty<GridRowAutomationProperty>(),
                row => NativeRowMatches(row, selector),
                row => NativeRowMatches(row, selector),
                stopwatch,
                timeoutMs,
                selector.HasDeclaredUniqueIdentity);
        }

        private NativeGridScan ScanNativeRows(
            GridIndexedRowSelector selector,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var selectorColumnIndexes = selector.Conditions
                .Where(static condition => condition.Column.RowIdentityAutomationProperty is null)
                .Select(condition => ResolveRuntimeColumnIndex(condition.Column))
                .Distinct()
                .ToArray();
            var selectorRowProperties = selector.Conditions
                .Select(static condition => condition.Column.RowIdentityAutomationProperty)
                .Where(static property => property is not null)
                .Select(static property => property!.Value)
                .Distinct()
                .ToArray();
            return ScanNativeRows(
                selectorColumnIndexes,
                selectorRowProperties,
                row => NativeRowMatches(row, selector),
                row => NativeRowMatches(row, selector),
                stopwatch,
                timeoutMs,
                selector.HasDeclaredUniqueIdentity);
        }

        private NativeGridScan ScanNativeRows(
            IReadOnlyList<int> selectorColumnIndexes,
            IReadOnlyList<GridRowAutomationProperty> selectorRowProperties,
            Func<NativeGridRowSnapshot, bool> snapshotMatches,
            Func<NativeFlaUiRow, bool> liveRowMatches,
            Stopwatch stopwatch,
            int timeoutMs,
            bool hasDeclaredUniqueIdentity)
        {
            var rows = new List<NativeGridRowSnapshot>();
            var visibleRows = WaitForNativeDataRows(stopwatch, timeoutMs);
            _nativeSignatureColumnIndexes = selectorColumnIndexes.Distinct().ToArray();
            _nativeSignatureRowProperties = selectorRowProperties.Distinct().ToArray();
            var scroll = FindGridScrollState();
            if (hasDeclaredUniqueIdentity)
            {
                AppendNativeRows(
                    rows,
                    visibleRows,
                    ReadGridScrollPosition(scroll),
                    selectorColumnIndexes,
                    selectorRowProperties);
                if (NativeGridVisibleResolution.TryResolve(
                        hasDeclaredUniqueIdentity,
                        rows,
                        visibleRows,
                        snapshotMatches,
                        liveRowMatches,
                        out var visibleMatches,
                        out var liveVisibleMatch))
                {
                    return new NativeGridScan(rows, visibleMatches, liveVisibleMatch);
                }

                rows.Clear();
            }

            if (visibleRows.Length == 0 && stopwatch.ElapsedMilliseconds >= timeoutMs)
            {
                return new NativeGridScan(rows, Array.Empty<NativeGridRowSnapshot>(), null);
            }

            MoveGridScrollToStart(scroll, stopwatch, timeoutMs);
            scroll = FindGridScrollState();
            visibleRows = NativeGridTraversal.Scan(
                TakePrefetchedNativeRows,
                observed => AppendNativeRows(
                    rows,
                    observed,
                    ReadGridScrollPosition(scroll),
                    selectorColumnIndexes,
                    selectorRowProperties),
                observed => CreateNativeRowSignature(observed),
                (previousSignature, observed) => MoveGridScrollForward(
                    scroll,
                    stopwatch,
                    timeoutMs,
                    previousSignature,
                    EstimateNativeScrollIncrement(observed)));

            var matchingRows = rows.Where(snapshotMatches).ToArray();
            return new NativeGridScan(
                rows,
                matchingRows,
                matchingRows.Length == 1
                    ? visibleRows.FirstOrDefault(row => row.IsVisible && liveRowMatches(row))
                    : null);
        }

        private void AppendNativeRows(
            List<NativeGridRowSnapshot> accumulated,
            IReadOnlyList<NativeFlaUiRow> visibleRows,
            GridScrollPosition scrollPosition,
            IReadOnlyList<int> valueColumnIndexes,
            IReadOnlyList<GridRowAutomationProperty>? rowAutomationProperties = null)
        {
            if (visibleRows.Count == 0)
            {
                return;
            }

            var observationIndex = accumulated.Count == 0 ? 0 : accumulated.Max(static row => row.ObservationIndex) + 1;
            var rawSnapshots = visibleRows.Where(static row => row.IsVisible).Select(row =>
            {
                if (valueColumnIndexes.Any(index => index < 0 || index >= row.Cells.Count))
                {
                    throw new InvalidOperationException($"Grid '{AutomationId}' row does not expose the selector's columns.");
                }
                var rowIndex = ReadNativeGridRowIndex(row);
                var snapshotColumnIndexes = rowIndex is null
                    ? Enumerable.Range(0, row.Cells.Count)
                    : valueColumnIndexes;
                var cellTexts = Enumerable.Repeat(string.Empty, row.Cells.Count).ToArray();
                foreach (var index in snapshotColumnIndexes)
                {
                    cellTexts[index] = row.GetCellText(index);
                }
                var rowAutomationValues = (rowAutomationProperties ?? Array.Empty<GridRowAutomationProperty>())
                    .ToDictionary(
                        static property => property,
                        property => FlaUiGridRowAutomationValueReader.Read(row.Element, property));

                return new NativeGridRowSnapshot(
                    cellTexts,
                    rowIndex,
                    TryRead(() => row.Element.BoundingRectangle),
                    scrollPosition)
                {
                    RuntimeId = ReadNativeRowRuntimeId(row.Element),
                    ObservationIndex = observationIndex,
                    RowAutomationValues = rowAutomationValues,
                    StableValues = valueColumnIndexes
                        .Select(index => (string?)cellTexts[index])
                        .Concat((rowAutomationProperties ?? Array.Empty<GridRowAutomationProperty>())
                            .Select(property => rowAutomationValues[property]))
                        .ToArray()
                };
            }).ToArray();
            var viewportSignature = CreateNativeSnapshotSignature(rawSnapshots);
            rawSnapshots = rawSnapshots
                .Select(snapshot => snapshot with { ViewportSignature = viewportSignature })
                .ToArray();
            var snapshots = new List<NativeGridRowSnapshot>();
            NativeGridRowNormalizer.Append(snapshots, rawSnapshots, HasSameNativeRow);
            NativeGridRowNormalizer.Append(accumulated, snapshots, HasSameNativeRow);
        }

        private static string CreateNativeSnapshotSignature(
            IEnumerable<NativeGridRowSnapshot> rows)
        {
            return string.Join(
                "\u001e",
                rows.Select(static row => string.Join(
                    "\u001f",
                    row.RuntimeId,
                    row.RowIndex,
                    row.Bounds,
                    string.Join("\u001d", row.StableValues))));
        }

        private bool HasSameNativeRow(NativeGridRowSnapshot first, NativeGridRowSnapshot second)
        {
            try
            {
                return first.HasSameStableRow(second);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"Grid '{AutomationId}' could not establish row identity: {exception.Message} " +
                    $"First={DescribeNativeRow(first)}; second={DescribeNativeRow(second)}.", exception);
            }
        }

        private static int? ReadNativeGridRowIndex(NativeFlaUiRow row)
        {
            if (row.Cells.Count > 0)
            {
                var gridItem = TryRead(() => row.Cells[0].Patterns.GridItem.PatternOrDefault);
                if (gridItem is not null)
                {
                    var index = TryRead(() => (int?)gridItem.Row.Value);
                    return index >= 0 ? index : null;
                }
            }

            return null;
        }

        private bool NativeRowMatches(NativeFlaUiRow row, GridRowSelector selector)
        {
            return selector.Conditions.All(condition =>
            {
                if (!TryGetColumnIndex(condition.ColumnName, out var columnIndex)
                    || columnIndex >= row.Cells.Count)
                {
                    return false;
                }

                return string.Equals(
                    GridRuntimeResolver.ReadRowConditionText(AutomationId, selector, condition,
                        new GridCellValueSnapshot(row.GetCellText(columnIndex)) { IsDisplayOnly = true }),
                    condition.Value,
                    StringComparison.Ordinal);
            });
        }

        private bool NativeRowMatches(NativeGridRowSnapshot row, GridRowSelector selector)
        {
            return selector.Conditions.All(condition =>
            {
                if (!TryGetColumnIndex(condition.ColumnName, out var columnIndex)
                    || columnIndex >= row.CellTexts.Count)
                {
                    return false;
                }

                return string.Equals(
                    GridRuntimeResolver.ReadRowConditionText(AutomationId, selector, condition,
                        new GridCellValueSnapshot(row.CellTexts[columnIndex]) { IsDisplayOnly = true }),
                    condition.Value,
                    StringComparison.Ordinal);
            });
        }

        private bool NativeRowMatches(NativeFlaUiRow row, GridIndexedRowSelector selector)
        {
            return selector.Conditions.All(condition =>
            {
                var actual = condition.Column.RowIdentityAutomationProperty is { } rowProperty
                    ? FlaUiGridRowAutomationValueReader.Read(row.Element, rowProperty)
                    : row.GetCellText(ResolveRuntimeColumnIndex(condition.Column));
                return string.Equals(actual, condition.ExpectedText, StringComparison.Ordinal);
            });
        }

        private bool NativeRowMatches(NativeGridRowSnapshot row, GridIndexedRowSelector selector)
        {
            return selector.Conditions.All(condition =>
            {
                if (condition.Column.RowIdentityAutomationProperty is { } rowProperty)
                {
                    return row.RowAutomationValues.TryGetValue(rowProperty, out var value)
                        && string.Equals(value, condition.ExpectedText, StringComparison.Ordinal);
                }

                var columnIndex = ResolveRuntimeColumnIndex(condition.Column);
                return columnIndex < row.CellTexts.Count
                    && string.Equals(row.CellTexts[columnIndex], condition.ExpectedText, StringComparison.Ordinal);
            });
        }

        private int ResolveRuntimeColumnIndex(GridRuntimeColumn column)
        {
            if (column.RuntimeColumnName is { } runtimeName)
            {
                return ResolveNamedColumnIndex(runtimeName);
            }
            if (column.RuntimeColumnIndex is { } runtimeColumnIndex)
            {
                return runtimeColumnIndex;
            }

            throw new InvalidOperationException(
                $"Grid source field '{column.SourceFieldName}' is hidden in the current runtime layout. "
                + "A hidden target column cannot be read or edited through native UI Automation; "
                + "make it visible or expose a provider-neutral addressable grid implementation.");
        }

        private string DescribeNativeResolution(
            GridRowSelector selector,
            int matchCount,
            IReadOnlyList<NativeGridRowSnapshot> discoveredRows)
        {
            var root = FindGridRoot();
            var scroll = FindGridScrollState();
            var scrollDescription = scroll.ScrollPattern is not null
                ? $"ScrollPattern(percent={TryRead(() => scroll.ScrollPattern.VerticalScrollPercent.ValueOrDefault)})"
                : scroll.RangeValuePattern is not null
                    ? $"RangeValuePattern(min={TryRead(() => scroll.RangeValuePattern.Minimum.ValueOrDefault)},"
                      + $"value={TryRead(() => scroll.RangeValuePattern.Value.ValueOrDefault)},"
                      + $"max={TryRead(() => scroll.RangeValuePattern.Maximum.ValueOrDefault)},"
                      + $"small={TryRead(() => scroll.RangeValuePattern.SmallChange.ValueOrDefault)},"
                      + $"large={TryRead(() => scroll.RangeValuePattern.LargeChange.ValueOrDefault)})"
                    : $"buttons(back={DescribeScrollButton(scroll.BackwardButton)},forward={DescribeScrollButton(scroll.ForwardButton)})";
            var matchingRows = string.Join(
                " | ",
                discoveredRows.Where(row => NativeRowMatches(row, selector)).Select(DescribeNativeRow));
            return $"grid='{AutomationId}'; selector={GridRuntimeResolver.DescribeRowSelector(selector)}; "
                   + $"matches={matchCount}; scannedRows={discoveredRows.Count}; "
                   + $"firstRow={DescribeNativeRow(discoveredRows.Count == 0 ? null : discoveredRows[0])}; "
                   + $"lastRow={DescribeNativeRow(discoveredRows.Count == 0 ? null : discoveredRows[^1])}; "
                   + $"matchingRows={matchingRows}; "
                   + $"columns={string.Join(", ", ColumnNames)}; "
                   + $"patterns=(grid={TryRead(() => root?.Patterns.Grid.IsSupported) == true},"
                   + $"itemContainer={TryRead(() => root?.Patterns.ItemContainer.IsSupported) == true},"
                   + $"scroll={TryRead(() => root?.Patterns.Scroll.IsSupported) == true}); "
                   + $"scroll={scrollDescription}";
        }

        private string DescribeIndexedNativeResolution(
            GridIndexedRowSelector selector,
            int matchCount,
            IReadOnlyList<NativeGridRowSnapshot> discoveredRows)
        {
            var conditions = string.Join(
                ", ",
                selector.Conditions.Select(static condition =>
                    condition.Column.RowIdentityAutomationProperty is { } property
                        ? $"row.{property}='{condition.ExpectedText}'"
                        : $"column[{condition.Column.RuntimeColumnIndex?.ToString(CultureInfo.InvariantCulture) ?? "hidden"}]='{condition.ExpectedText}'"));
            return $"grid='{AutomationId}'; selector={conditions}; matches={matchCount}; "
                   + $"scannedRows={discoveredRows.Count}; columns={string.Join(", ", ColumnNames)}";
        }

        private static string DescribeNativeRow(NativeGridRowSnapshot? row)
        {
            return row is null
                ? "<none>"
                : $"[{string.Join(", ", row.CellTexts)}]"
                  + $"@index={row.RowIndex?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}"
                  + $"/bounds={row.Bounds}"
                  + $"/runtimeId={row.RuntimeId ?? "<unknown>"}"
                  + $"/scroll={row.ScrollPosition}";
        }

        private static string DescribeScrollButton(AutomationElement? button)
        {
            return button is null
                ? "missing"
                : $"{TryRead(() => button.AutomationId)}/enabled={TryRead(() => button.IsEnabled)}/invoke={TryRead(() => button.Patterns.Invoke.IsSupported)}";
        }

        private GridIndexedRowSelector MapRow(GridRowSelector row)
        {
            ArgumentNullException.ThrowIfNull(row);
            var mapped = new GridIndexedRowSelector(
                row.Conditions.Select(condition => new GridIndexedCellCondition(
                    CreateRuntimeColumn(condition.ColumnName, configuration: row.FindColumnDefinition(condition.ColumnName)),
                    condition.Value)));

            return row.HasDeclaredUniqueIdentity
                ? mapped.WithDeclaredUniqueIdentity()
                : mapped;
        }

        private GridRuntimeColumn CreateRuntimeColumn(
            string columnName,
            GridCellEditorKind? editorKind = null,
            GridCellEditorParts? editorParts = null,
            GridColumnDefinition? configuration = null)
        {
            return new GridRuntimeColumn(
                ResolveNamedColumnIndex(columnName),
                columnName,
                displayValuePath: configuration?.DisplayValuePath,
                formatString: configuration?.FormatString,
                cultureName: configuration?.CultureName,
                configuration?.ValueKind ?? GridCellValueNormalizer.InferValueKind(configuration?.EditorKind),
                editorKind,
                editorParts);
        }

        private int ResolveNamedColumnIndex(string columnName)
        {
            var names = ColumnNames;
            var matches = names.Select((name, index) => (name, index))
                .Where(candidate => string.Equals(candidate.name, columnName?.Trim(), StringComparison.Ordinal))
                .Take(2).ToArray();
            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Grid column '{columnName}' is ambiguous in visual grid '{AutomationId}' ({matches.Length} matches).");
            }

            if (matches.Length == 1)
            {
                return matches[0].index;
            }

            throw new InvalidOperationException(
                $"Grid column '{columnName}' was not found in visual grid '{AutomationId}'. "
                + $"Available columns: {string.Join(", ", names)}.");
        }

        public GridRowResolution ResolveRow(GridIndexedRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            if (HasNativeDataRows())
            {
                var scan = ScanNativeRows(row, Stopwatch.StartNew(), timeoutMs);
                var nativeDescription = DescribeIndexedNativeResolution(row, scan.MatchingRows.Count, scan.Rows);
                return scan.MatchingRows.Count switch
                {
                    0 => GridRowResolution.NotFound(nativeDescription),
                    1 => GridRowResolution.Unique(nativeDescription),
                    _ => GridRowResolution.Ambiguous(scan.MatchingRows.Count, nativeDescription)
                };
            }

            var matches = FindMatchingRowIndexes(row, timeoutMs);
            var description = DescribeIndexedResolution(row, matches.Length);
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
            var stopwatch = Stopwatch.StartNew();
            if (HasNativeDataRows())
            {
                var nativeRow = ResolveIndexedNativeRowForColumn(row, column, stopwatch, timeoutMs);
                var nativeRuntimeColumnIndex = ResolveRuntimeColumnIndex(column);
                var nativeCell = nativeRow.Cells[nativeRuntimeColumnIndex];
                var nativeDisplayText = ReadVisualGridCellText(nativeCell);
                if (column.ValueKind == GridCellValueKind.Date
                    && IsEmptyNativeDateCellProjection(
                        nativeDisplayText,
                        TryRead(() => nativeCell.Patterns.Value.PatternOrDefault?.Value.Value),
                        TryRead(() => nativeCell.ControlType),
                        TryRead(() => nativeCell.Patterns.Text.IsSupported),
                        FindAutomationDescendants(nativeCell).Length))
                {
                    nativeDisplayText = null;
                }
                return GridCellValueNormalizer.Normalize(AutomationId,
                    new GridCellValueSnapshot(nativeDisplayText, nativeDisplayText, column.ValueKind)
                    {
                        IsDisplayOnly = true
                    },
                    column);
            }

            var rowIndex = ResolveUniqueRowIndex(row, timeoutMs);
            var runtimeColumnIndex = column.RuntimeColumnIndex ?? column.ColumnIndex;
            var cell = FindVisualCellWithTraversal(
                    rowIndex,
                    runtimeColumnIndex,
                    RemainingGridMilliseconds(stopwatch, timeoutMs))
                ?? throw new InvalidOperationException(
                    $"Grid '{AutomationId}' row {rowIndex} no longer exposes column {runtimeColumnIndex}.");
            var displayText = ReadVisualGridCellText(cell);
            return GridCellValueNormalizer.Normalize(AutomationId,
                new GridCellValueSnapshot(displayText, displayText, column.ValueKind) { IsDisplayOnly = true },
                column);
        }

        public string CopyCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(column);
            if (HasNativeDataRows())
            {
                var stopwatch = Stopwatch.StartNew();
                var nativeRow = ResolveIndexedNativeRowForColumn(row, column, stopwatch, timeoutMs);
                var nativeRuntimeColumnIndex = ResolveRuntimeColumnIndex(column);
                var nativeCell = nativeRow.Cells[nativeRuntimeColumnIndex];
                if (TryRead(() => nativeCell.Patterns.Value.IsSupported))
                {
                    var value = TryRead(() => nativeCell.Patterns.Value.Pattern.Value);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                return ReadVisualGridCellText(nativeCell) ?? string.Empty;
            }

            var rowIndex = ResolveUniqueRowIndex(row, timeoutMs);
            var runtimeColumnIndex = column.RuntimeColumnIndex ?? column.ColumnIndex;
            var cell = FindVisualCellWithTraversal(rowIndex, runtimeColumnIndex, timeoutMs)
                ?? throw new InvalidOperationException(
                    $"Grid '{AutomationId}' row {rowIndex} no longer exposes column {runtimeColumnIndex}.");
            if (TryRead(() => cell.Patterns.Value.IsSupported))
            {
                var value = TryRead(() => cell.Patterns.Value.Pattern.Value);
                if (value is not null)
                {
                    return value;
                }
            }

            return ReadVisualGridCellText(cell) ?? string.Empty;
        }

        public void EditCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(column);
            ArgumentNullException.ThrowIfNull(request);
            var stopwatch = Stopwatch.StartNew();
            if (HasNativeDataRows())
            {
                var nativeRow = ResolveIndexedNativeRowForColumn(row, column, stopwatch, timeoutMs);
                var nativeRuntimeColumnIndex = ResolveRuntimeColumnIndex(column);
                var nativeRemaining = RemainingGridMilliseconds(stopwatch, timeoutMs);
                var editScroll = ReadGridScrollPosition(FindGridScrollState());
                var editAnchor = CaptureNativeRowAnchor(nativeRow);
                EditResolvedCell(
                    nativeRow.Cells[nativeRuntimeColumnIndex],
                    _ => RebindNativeRowAfterHorizontalScroll(editAnchor, editScroll).Cells[nativeRuntimeColumnIndex],
                    GridCellEditRequestValidation.CreateIndexedRequest(
                        0,
                        nativeRuntimeColumnIndex,
                        request,
                        nativeRemaining,
                        request.EditorParts ?? column.EditorParts));
                return;
            }

            var rowIndex = ResolveUniqueRowIndex(row, timeoutMs);
            var runtimeColumnIndex = column.RuntimeColumnIndex ?? column.ColumnIndex;
            var cell = FindVisualCellWithTraversal(
                    rowIndex,
                    runtimeColumnIndex,
                    RemainingGridMilliseconds(stopwatch, timeoutMs))
                ?? throw new InvalidOperationException(
                    $"Grid '{AutomationId}' row {rowIndex} no longer exposes column {runtimeColumnIndex}.");
            var remaining = RemainingGridMilliseconds(stopwatch, timeoutMs);
            EditResolvedCell(
                cell,
                timeout => FindVisualCellWithTraversal(rowIndex, runtimeColumnIndex, timeout),
                GridCellEditRequestValidation.CreateIndexedRequest(
                    rowIndex,
                    runtimeColumnIndex,
                    request,
                    remaining,
                    request.EditorParts ?? column.EditorParts));
        }

        public void OpenRow(GridIndexedRowSelector row, int timeoutMs)
        {
            var stopwatch = Stopwatch.StartNew();
            if (HasNativeDataRows())
            {
                var nativeTarget = ResolveUniqueNativeRow(row, stopwatch, timeoutMs).Element;
                if (TryDoubleClick(nativeTarget, out var nativeException))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Grid '{AutomationId}' stable row could not be opened by double-click.",
                    nativeException);
            }

            var rowIndex = ResolveUniqueRowIndex(row, timeoutMs);
            var target = FindVisualCellWithTraversal(
                    rowIndex,
                    0,
                    RemainingGridMilliseconds(stopwatch, timeoutMs))
                ?? ReadRows().FirstOrDefault(candidate =>
                    ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row") == rowIndex);
            if (target is null)
            {
                throw new InvalidOperationException(
                    $"Grid '{AutomationId}' stable row was resolved but is not visible after bounded traversal.");
            }

            if (TryDoubleClick(target, out var exception))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Grid '{AutomationId}' stable row could not be opened by double-click.",
                exception);
        }

        public void SelectRow(GridIndexedRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var stopwatch = Stopwatch.StartNew();
            AutomationElement target;
            Func<AutomationElement?> refreshTarget;
            Func<AutomationElement, bool>? isCurrentRowMatch = null;
            if (HasNativeDataRows())
            {
                target = ResolveUniqueNativeRow(row, stopwatch, timeoutMs).Element;
                refreshTarget = () => TryResolveVisibleNativeRow(row, forSelection: true)?.Element;
                if (row.Conditions.All(static condition => condition.Column.RowIdentityAutomationProperty is not null))
                {
                    isCurrentRowMatch = candidate => NativeRowMatches(
                        new NativeFlaUiRow(candidate, Array.Empty<AutomationElement>()), row);
                }
            }
            else
            {
                var rowIndex = ResolveUniqueRowIndex(row, timeoutMs);
                target = FindVisualCellWithTraversal(
                        rowIndex,
                        0,
                        RemainingGridMilliseconds(stopwatch, timeoutMs))
                    ?? ReadRows().FirstOrDefault(candidate =>
                        ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row") == rowIndex)
                    ?? throw new InvalidOperationException(
                        $"Grid '{AutomationId}' stable row was resolved but is not visible after bounded traversal.");
                refreshTarget = () => ReadRows().FirstOrDefault(candidate =>
                    ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row") == rowIndex);
            }

            SelectAndConfirmGridRow(
                target,
                FindGridRoot(),
                RemainingGridMilliseconds(stopwatch, timeoutMs),
                $"Grid '{AutomationId}' stable row",
                refreshTarget,
                () => FindGridRoot(),
                isCurrentRowMatch);
        }

        public void EditCell(GridCellEditRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.TimeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Grid edit timeout must be positive.");
            }

            var cell = FindVisualCellWithTraversal(request.RowIndex, request.ColumnIndex, request.TimeoutMs)
                ?? throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] was not found in grid '{AutomationId}'.");

            EditResolvedCell(
                cell,
                timeout => FindVisualCellWithTraversal(request.RowIndex, request.ColumnIndex, timeout),
                request);
        }

        private void EditResolvedCell(
            AutomationElement cell,
            Func<int, AutomationElement?> resolveCurrentCell,
            GridCellEditRequest request)
        {
            var stopwatch = Stopwatch.StartNew();
            var activeCell = cell;
            AutomationElement? activeEditor = null;
            if (RequiresActivatedGridCell(request.EditorKind))
            {
                if (!TryActivateSelectedGridCell(
                        activeCell,
                        request.EditorKind,
                        out var activationException))
                {
                    throw new InvalidOperationException(
                        $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' "
                        + $"could not be activated before resolving its '{request.EditorKind}' editor.",
                        activationException);
                }

            }

            activeEditor = ResolveMaterializedCellEditor(activeCell, request);
            if (activeEditor is null)
            {
                if (!RequiresActivatedGridCell(request.EditorKind)
                    && !TryDoubleClick(activeCell, out var activationException))
                {
                    throw new InvalidOperationException(
                        $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' "
                        + $"could not be activated before resolving its '{request.EditorKind}' editor.",
                        activationException);
                }

                var materialized = WaitForMaterializedCellEditor(
                        activeCell,
                        resolveCurrentCell,
                        request,
                        stopwatch)
                    ?? throw CreateMissingCellEditorException(cell, request);
                activeCell = materialized.Cell;
                activeEditor = materialized.Editor;
            }

            var activeRequest = request with
            {
                TimeoutMs = RemainingGridMilliseconds(stopwatch, request.TimeoutMs)
            };
            activeEditor = EnsureMaterializedCellEditorVisible(
                activeCell,
                activeEditor,
                activeRequest);
            if (activeRequest.EditorKind == GridCellEditorKind.SearchPicker)
            {
                EditSearchPickerCell(activeCell, activeEditor, activeRequest);
            }
            else if (activeRequest.EditorKind == GridCellEditorKind.Time)
            {
                EditTimeCell(activeCell, activeEditor, activeRequest);
            }
            else if (activeRequest.EditorKind == GridCellEditorKind.Date)
            {
                EditDateCell(activeCell, activeRequest);
            }
            else if (activeRequest.EditorKind == GridCellEditorKind.Color)
            {
                EditColorCell(activeCell, activeEditor, activeRequest);
            }
            else if (activeRequest.EditorKind == GridCellEditorKind.ComboBox)
            {
                EditComboBoxCell(activeCell, activeEditor, activeRequest);
            }
            else if (activeRequest.EditorKind == GridCellEditorKind.CheckBox)
            {
                EditCheckBoxCell(activeCell, activeEditor, activeRequest);
            }
            else if (activeRequest.EditorKind is GridCellEditorKind.Text or GridCellEditorKind.Number)
            {
                EditTextOrNumberCell(activeCell, activeEditor, activeRequest);
            }
            else if (_fallback is IEditableGridControl editableFallback)
            {
                editableFallback.EditCell(request);
                return;
            }
            else
            {
                throw new System.NotSupportedException(
                    $"Visual grid '{AutomationId}' does not support '{request.EditorKind}' cell editing in the FlaUI adapter.");
            }

            var confirmationCell = TryResolveCurrentCell(
                    resolveCurrentCell,
                    stopwatch,
                    request.TimeoutMs)
                ?? activeCell;
            var confirmationRequest = activeRequest with
            {
                TimeoutMs = RemainingGridMilliseconds(stopwatch, request.TimeoutMs)
            };
            if (confirmationRequest.CommitMode == GridCellEditCommitMode.Cancel)
            {
                CancelCellEdit(confirmationCell, confirmationRequest);
                return;
            }

            ConfirmCellEdit(confirmationCell, confirmationRequest);
        }

        private static bool RequiresActivatedGridCell(GridCellEditorKind editorKind) =>
            editorKind is GridCellEditorKind.Text
                or GridCellEditorKind.Number
                or GridCellEditorKind.ComboBox
                or GridCellEditorKind.SearchPicker
                or GridCellEditorKind.Date
                or GridCellEditorKind.Time
                or GridCellEditorKind.Color;

        private AutomationElement EnsureMaterializedCellEditorVisible(
            AutomationElement cell,
            AutomationElement editor,
            GridCellEditRequest request)
        {
            if (HasVisibleBounds(editor))
            {
                return editor;
            }

            if (request.EditorKind is GridCellEditorKind.Number or GridCellEditorKind.SearchPicker)
            {
                TryActivateSelectedGridCell(cell, request.EditorKind, out _);
            }
            else
            {
                TryDoubleClick(cell, out _);
            }
            var stopwatch = Stopwatch.StartNew();
            do
            {
                var refreshed = ResolveMaterializedCellEditor(cell, request);
                if (refreshed is not null)
                {
                    return refreshed;
                }

                var remaining = request.TimeoutMs - (int)stopwatch.ElapsedMilliseconds;
                if (remaining > 0)
                {
                    Thread.Sleep(Math.Min(50, remaining));
                }
            }
            while (stopwatch.ElapsedMilliseconds < request.TimeoutMs);

            throw CreateMissingCellEditorException(cell, request);
        }

        private MaterializedCellEditor? WaitForMaterializedCellEditor(
            AutomationElement initialCell,
            Func<int, AutomationElement?> resolveCurrentCell,
            GridCellEditRequest request,
            Stopwatch stopwatch)
        {
            var nextActivationAttemptAt = stopwatch.ElapsedMilliseconds + 250;
            var nextCellResolutionAt = stopwatch.ElapsedMilliseconds + 100;
            var selectedCellActivationAttempted = false;
            var nativeCellActivationAttempted = false;
            var current = initialCell;
            do
            {
                var editor = ResolveMaterializedCellEditor(current, request);
                if (editor is not null)
                {
                    return new MaterializedCellEditor(current, editor);
                }

                if ((!TryRead(() => current.IsAvailable)
                        || stopwatch.ElapsedMilliseconds >= nextCellResolutionAt)
                    && TryResolveCurrentCell(
                        resolveCurrentCell,
                        stopwatch,
                        request.TimeoutMs) is { } refreshedCell)
                {
                    current = refreshedCell;
                    nextCellResolutionAt = stopwatch.ElapsedMilliseconds + 100;
                }

                if (stopwatch.ElapsedMilliseconds >= nextActivationAttemptAt)
                {
                    if (request.EditorKind is GridCellEditorKind.Number or GridCellEditorKind.SearchPicker)
                    {
                        TryActivateSelectedGridCell(current, request.EditorKind, out _);
                    }
                    else if (!selectedCellActivationAttempted)
                    {
                        TryActivateSelectedGridCell(current, request.EditorKind, out _);
                        selectedCellActivationAttempted = true;
                    }
                    else if (!nativeCellActivationAttempted)
                    {
                        PrepareForPhysicalGridInput(current);
                        _ = TrySendDoubleClickToContainingWindow(
                            current,
                            useBoundsCenter: true);
                        nativeCellActivationAttempted = true;
                    }
                    else
                    {
                        TryDoubleClick(current, out _);
                    }

                    nextActivationAttemptAt = stopwatch.ElapsedMilliseconds + 500;
                }

                var remaining = request.TimeoutMs - (int)stopwatch.ElapsedMilliseconds;
                if (remaining > 0)
                {
                    Thread.Sleep(Math.Min(50, remaining));
                }
            }
            while (stopwatch.ElapsedMilliseconds < request.TimeoutMs);

            return null;
        }

        private bool TryActivateSelectedGridCell(
            AutomationElement cell,
            GridCellEditorKind editorKind,
            out Exception? exception)
        {
            try
            {
                var row = EnumerateSelfAndParents(cell)
                    .FirstOrDefault(candidate =>
                        TryRead(() => candidate.ControlType) == ControlType.DataItem
                        && TryRead(() => candidate.Patterns.SelectionItem.IsSupported));
                if (row is not null
                    && !TryRead(() => row.Patterns.SelectionItem.Pattern.IsSelected.Value))
                {
                    TryRead(() =>
                    {
                        row.Patterns.SelectionItem.Pattern.Select();
                        return true;
                    });
                }

                DesktopPointer.ClickPrepared(() => cell, PrepareForPhysicalGridInput, GetBoundsCenter);
                if (row is not null)
                {
                    var selectionWait = Stopwatch.StartNew();
                    while (selectionWait.ElapsedMilliseconds < 250
                           && !TryRead(() => row.Patterns.SelectionItem.Pattern.IsSelected.Value))
                    {
                        Thread.Sleep(10);
                    }
                }

                if (editorKind is GridCellEditorKind.Number
                    or GridCellEditorKind.SearchPicker
                    or GridCellEditorKind.ComboBox)
                {
                    exception = null;
                    return true;
                }

                DesktopPointer.ClickPrepared(() => cell, PrepareForPhysicalGridInput, GetBoundsCenter);
                exception = null;
                return true;
            }
            catch (Exception activationException) when (!DesktopPointer.IsTerminalFailure(activationException))
            {
                exception = activationException;
                return false;
            }
        }

        private static AutomationElement? TryResolveCurrentCell(
            Func<int, AutomationElement?> resolveCurrentCell,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
            return remaining <= 0 ? null : resolveCurrentCell(remaining);
        }

        private AutomationElement? ResolveMaterializedCellEditor(
            AutomationElement cell,
            GridCellEditRequest request)
        {
            if (request.EditorParts?.Input is { } configuredInput)
            {
                var configuredEditor = ResolveEditorPart(cell, configuredInput);
                return configuredEditor is not null && HasVisibleBounds(configuredEditor)
                    ? configuredEditor
                    : null;
            }

            var candidates = new[] { cell }
                .Concat(FindAutomationDescendants(cell))
                .Where(HasVisibleBounds)
                .ToArray();
            return request.EditorKind switch
            {
                GridCellEditorKind.SearchPicker => candidates.FirstOrDefault(candidate =>
                    TryRead(() => candidate.ControlType) == ControlType.Edit),
                GridCellEditorKind.Text or GridCellEditorKind.Color => candidates.FirstOrDefault(candidate =>
                    TryRead(() => candidate.ControlType) == ControlType.Edit),
                GridCellEditorKind.Number => candidates.FirstOrDefault(candidate =>
                        TryRead(() => candidate.ControlType) == ControlType.Spinner)
                    ?? candidates.FirstOrDefault(candidate =>
                        TryRead(() => candidate.ControlType) == ControlType.Edit),
                GridCellEditorKind.ComboBox => candidates.FirstOrDefault(candidate =>
                    TryRead(() => candidate.ControlType) == ControlType.ComboBox),
                GridCellEditorKind.CheckBox => candidates.FirstOrDefault(candidate =>
                    TryRead(() => candidate.ControlType) == ControlType.CheckBox),
                GridCellEditorKind.Date => candidates.FirstOrDefault(candidate =>
                    TryRead(() => candidate.ControlType) is ControlType.Edit or ControlType.Calendar),
                GridCellEditorKind.Time => candidates.FirstOrDefault(candidate =>
                    TryRead(() => candidate.ControlType) == ControlType.Edit),
                _ => null
            };
        }

        private InvalidOperationException CreateMissingCellEditorException(
            AutomationElement originalCell,
            GridCellEditRequest request)
        {
            var configuredParts = request.EditorParts is null
                ? "none"
                : string.Join(
                    ", ",
                    new[]
                    {
                        DescribeGridEditorPart("input", request.EditorParts.Input),
                        DescribeGridEditorPart("results", request.EditorParts.Results),
                        DescribeGridEditorPart("open", request.EditorParts.OpenButton),
                        DescribeGridEditorPart("confirm", request.EditorParts.ConfirmButton),
                        DescribeGridEditorPart("cancel", request.EditorParts.CancelButton),
                        DescribeGridEditorPart("commit target", request.EditorParts.CommitTarget),
                        request.EditorParts.UseKeyboardInput ? "input=keyboard" : null
                    }.Where(static part => part is not null));
            var observedTypes = new[] { originalCell }
                .Concat(FindAutomationDescendants(originalCell))
                .Select(candidate => $"{TryRead(() => candidate.ControlType)}:{TryRead(() => candidate.AutomationId)}")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var cellContext = string.Join(
                " -> ",
                EnumerateSelfAndParents(originalCell)
                    .Take(5)
                    .Select(candidate =>
                        $"{TryRead(() => candidate.ControlType)}"
                        + $"[id='{TryRead(() => candidate.AutomationId)}',"
                        + $"name='{TryRead(() => candidate.Name)}',"
                        + $"bounds={TryRead(() => candidate.BoundingRectangle)}]"));
            var configuredInputObservations = request.EditorParts?.Input is
            { LocatorKind: UiLocatorKind.AutomationId } input
                && FindGridRoot() is { } gridRoot
                    ? FindAutomationDescendants(gridRoot)
                        .Where(candidate => string.Equals(
                            TryRead(() => candidate.AutomationId),
                            input.LocatorValue,
                            StringComparison.Ordinal))
                        .Take(3)
                        .Select(candidate => DescribeGridComboElement("configured input", candidate))
                        .ToArray()
                    : Array.Empty<string>();
            return new InvalidOperationException(
                $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' "
                + $"did not materialize its '{request.EditorKind}' editor after activation. "
                + $"Configured parts: {configuredParts}. Observed controls: "
                + (observedTypes.Length == 0 ? "none" : string.Join(", ", observedTypes)) + ". "
                + $"Cell context: {cellContext}. Configured input observations: "
                + (configuredInputObservations.Length == 0
                    ? "none"
                    : string.Join(", ", configuredInputObservations)) + ".");
        }

        private static IEnumerable<AutomationElement> EnumerateSelfAndParents(AutomationElement element)
        {
            for (var current = element; current is not null; current = TryRead(() => current.Parent))
            {
                yield return current;
            }
        }

        private static string? DescribeGridEditorPart(string name, GridRelativeLocator? locator) =>
            locator is null
                ? null
                : $"{name}={locator.Scope}:{locator.LocatorKind}:{locator.LocatorValue}";

        public void OpenRow(int rowIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);

            var candidates = FindOpenRowTargets(rowIndex);
            Exception? lastException = null;
            foreach (var candidate in candidates)
            {
                if (TryDoubleClick(candidate, out lastException))
                {
                    return;
                }
            }

            if (_fallback is IGridUserActionControl actionFallback)
            {
                actionFallback.OpenRow(rowIndex);
                return;
            }

            var detail = lastException is null
                ? $"Visual grid row {rowIndex} was not found in grid '{AutomationId}'."
                : $"Visual grid row {rowIndex} in grid '{AutomationId}' could not be opened by double-click.";
            throw new InvalidOperationException(detail, lastException);
        }

        private AutomationElement? FindVisualCell(int rowIndex, int columnIndex)
        {
            var expectedAutomationId = $"{AutomationId}_Row{rowIndex}_Cell{columnIndex}";
            return FindAutomationDescendants(_searchRoot)
                .FirstOrDefault(candidate =>
                    string.Equals(
                        TryRead(() => candidate.AutomationId),
                        expectedAutomationId,
                        StringComparison.Ordinal));
        }

        private AutomationElement? FindVisualCellWithTraversal(int rowIndex, int columnIndex, int timeoutMs)
        {
            var current = FindVisualCell(rowIndex, columnIndex);
            if (current is not null)
            {
                return current;
            }

            var stopwatch = Stopwatch.StartNew();
            var scroll = FindGridScrollPattern();
            if (scroll is null)
            {
                return null;
            }

            MoveGridScrollToStart(scroll, stopwatch, timeoutMs);
            do
            {
                current = FindVisualCell(rowIndex, columnIndex);
                if (current is not null)
                {
                    return current;
                }
            }
            while (stopwatch.ElapsedMilliseconds < timeoutMs && ScrollGridForward(scroll));

            return FindVisualCell(rowIndex, columnIndex);
        }

        private void EditSearchPickerCell(
            AutomationElement cell,
            AutomationElement materializedEditor,
            GridCellEditRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SearchText))
            {
                throw new ArgumentException(
                    "Search text cannot be empty for a search-picker grid edit.",
                    nameof(request));
            }

            var editorElements = new[] { cell }
                .Concat(FindAutomationDescendants(cell))
                .ToArray();
            var searchInput = materializedEditor
                ?? ResolveEditorPart(cell, request.EditorParts?.Input)
                ?? editorElements
                .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Edit);
            if (searchInput is null)
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose a ServerSearchComboBox input.");
            }

            var configuredResults = request.EditorParts?.Results;
            var inputAutomationId = TryRead(() => searchInput.AutomationId);
            var editorAutomationId = TryGetEditorAutomationId(inputAutomationId);
            var resultsAutomationId = configuredResults?.LocatorKind == UiLocatorKind.AutomationId
                ? configuredResults.LocatorValue
                : editorAutomationId is null
                    ? null
                    : $"{editorAutomationId}_Results";
            ExecuteFlaUiSearchPickerSelection(
                searchInput,
                wait => configuredResults is
                {
                    Scope: GridRelativeLocatorScope.DetachedPopup,
                    LocatorKind: UiLocatorKind.AutomationId
                }
                        ? WaitForProcessElementByAutomationId(configuredResults.LocatorValue, wait)
                        : configuredResults is null
                            ? resultsAutomationId is null
                                ? null
                                : WaitForProcessElementByAutomationId(resultsAutomationId, wait)
                            : WaitForEditorPart(cell, configuredResults, wait),
                () => ResolveEditorPart(cell, request.EditorParts?.OpenButton)
                    ?? (editorAutomationId is null
                        ? null
                        : FindProcessElementByAutomationId($"{editorAutomationId}_OpenButton")),
                request.SearchText,
                request.Value,
                request.TimeoutMs,
                $"visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}'");
        }

        private void SelectGridSearchPickerListItem(
            ListBox results,
            string itemText,
            TimeSpan timeout)
        {
            IReadOnlyList<AutomationElement> ReadCandidates()
            {
                var directChildren = TryRead(results.FindAllChildren)
                    ?? Array.Empty<AutomationElement>();
                if (directChildren.Length > 0)
                {
                    return directChildren
                        .Where(HasVisibleBounds)
                        .ToArray();
                }

                var items = TryRead(() => results.Items.Cast<AutomationElement>().ToArray())
                    ?? Array.Empty<AutomationElement>();
                if (items.Length > 0)
                {
                    return items
                        .Where(HasVisibleBounds)
                        .ToArray();
                }

                return FindAutomationDescendants(results)
                    .Where(HasVisibleBounds)
                    .ToArray();
            }

            VirtualizedExactItemSelector.Select(
                itemText,
                timeout,
                ReadCandidates,
                ReadAutomationElementText,
                ResolveComboItemProjection,
                ReadAutomationElementText,
                GetVisibleGridPopupItemIdentity,
                candidate =>
                {
                    if (!HasVisibleBounds(candidate))
                    {
                        TryScrollIntoView(candidate);
                    }
                },
                candidate =>
                {
                    if (!TryRead(() => candidate.Patterns.SelectionItem.IsSupported))
                    {
                        return false;
                    }

                    return TryRead(() =>
                    {
                        candidate.Patterns.SelectionItem.Pattern.Select();
                        return true;
                    });
                },
                candidate =>
                {
                    _ = TryRead(() =>
                    {
                        _searchRoot.SetForeground();
                        return true;
                    });
                    DesktopPointer.ClickFallback(candidate, "grid popup candidate fallback");
                });
        }

        private static string GetVisibleGridPopupItemIdentity(AutomationElement candidate)
        {
            return string.Join(
                '|',
                TryRead(() => candidate.ControlType),
                TryRead(() => candidate.AutomationId),
                TryRead(() => candidate.BoundingRectangle));
        }

        private void EditTextOrNumberCell(
            AutomationElement cell,
            AutomationElement materializedEditor,
            GridCellEditRequest request)
        {
            var explicitInput = ResolveEditorPart(cell, request.EditorParts?.Input)
                ?? (TryRead(() => materializedEditor.ControlType) == ControlType.Edit
                    ? materializedEditor
                    : null);
            if (request.EditorKind == GridCellEditorKind.Number
                && request.NumericInput is null
                && request.EditorParts?.UseKeyboardInput != true)
            {
                var spinner = TryRead(() => materializedEditor.ControlType) == ControlType.Spinner
                    ? materializedEditor
                    : new[] { cell }.Concat(FindAutomationDescendants(cell))
                        .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Spinner);
                if (spinner is not null
                    && double.TryParse(request.Value, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    new FlaUiSpinnerControl(spinner.AsSpinner()).Value = number;
                    return;
                }
            }

            var input = explicitInput
                ?? new[] { cell }.Concat(FindAutomationDescendants(cell))
                    .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Edit);
            if (input is null || TryRead(() => input.ControlType) != ControlType.Edit)
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose a writable text editor.");
            }

            if (request.NumericInput is not null
                || request.EditorParts?.UseKeyboardInput == true)
            {
                EnterGridTextWithKeyboard(input, request);
                return;
            }

            new FlaUiTextBoxControl(input.AsTextBox()).Enter(
                request.EditorKind == GridCellEditorKind.Number
                    ? request.NumericInput?.Text ?? request.Value
                    : request.Value);
        }

        private void EnterGridTextWithKeyboard(
            AutomationElement input,
            GridCellEditRequest request)
        {
            var stopwatch = Stopwatch.StartNew();
            _ = TryRead(() =>
            {
                DesktopPointer.ClickPrepared(
                    () => input,
                    PrepareForPhysicalGridInput,
                    options: new PointerClickOptions
                    {
                        Timeout = TimeSpan.FromMilliseconds(RemainingGridMilliseconds(stopwatch, request.TimeoutMs))
                    });
                return true;
            });
            _ = UiWait.Until(
                () => TryRead(() => input.Properties.HasKeyboardFocus.Value) == true,
                static hasFocus => hasFocus,
                new UiWaitOptions
                {
                    Timeout = TimeSpan.FromMilliseconds(RemainingGridMilliseconds(stopwatch, request.TimeoutMs)),
                    PollInterval = TimeSpan.FromMilliseconds(25)
                },
                $"Grid editor input '{TryRead(() => input.AutomationId)}' did not receive keyboard focus before entering its value.");
            _ = RemainingGridMilliseconds(stopwatch, request.TimeoutMs);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            var inputText = request.EditorKind == GridCellEditorKind.Number
                ? request.NumericInput?.Text ?? request.Value
                : request.Value;
            Keyboard.Type(inputText);

            string? lastActual = null;
            do
            {
                lastActual = TryRead(() => input.AsTextBox().Text)
                    ?? ReadAutomationElementVisibleText(input);
                if (GridEditorTextMatchesRequest(lastActual, request))
                {
                    return;
                }

                var remaining = request.TimeoutMs - (int)stopwatch.ElapsedMilliseconds;
                if (remaining > 0)
                {
                    Thread.Sleep(Math.Min(25, remaining));
                }
            }
            while (stopwatch.ElapsedMilliseconds < request.TimeoutMs);

            throw new InvalidOperationException(
                $"Grid editor input '{TryRead(() => input.AutomationId)}' did not receive keyboard value '{inputText}'. "
                + $"Last observed text: '{lastActual ?? "<unavailable>"}'.");
        }

        private static bool GridEditorTextMatchesRequest(
            string? actual,
            GridCellEditRequest request)
        {
            if (string.Equals(actual, request.Value, StringComparison.Ordinal))
            {
                return true;
            }

            if (request.EditorKind != GridCellEditorKind.Number || actual is null)
            {
                return false;
            }

            return GridNumericText.TryParse(
                    actual,
                    request.NumericInput?.CultureName,
                    out var actualNumber,
                    out _)
                && GridNumericText.TryParse(request.Value, CultureInfo.InvariantCulture, out var expectedNumber)
                && actualNumber.Equals(expectedNumber);
        }

        private void EditDateCell(AutomationElement cell, GridCellEditRequest request)
        {
            var date = ParseGridDate(request.Value);
            var calendar = ResolveEditorPart(cell, request.EditorParts?.Results)
                ?? new[] { cell }.Concat(FindAutomationDescendants(cell))
                    .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Calendar);
            if (calendar is null)
            {
                if (ResolveEditorPart(cell, request.EditorParts?.OpenButton) is { } openButton)
                {
                    new FlaUiButtonControl(openButton.AsButton()).Invoke();
                }
                calendar = request.EditorParts?.Results is { } results
                    ? WaitForEditorPart(cell, results, TimeSpan.FromMilliseconds(request.TimeoutMs))
                    : null;
            }

            if (calendar is null || TryRead(() => calendar.ControlType) != ControlType.Calendar)
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose a calendar editor.");
            }

            new FlaUiCalendarControl(calendar.AsCalendar()).SelectDate(date);
        }

        private void EditTimeCell(
            AutomationElement cell,
            AutomationElement materializedEditor,
            GridCellEditRequest request)
        {
            var time = ParseGridTime(request.Value);
            var input = materializedEditor
                ?? ResolveEditorPart(cell, request.EditorParts?.Input);
            if (input is not null && TryRead(() => input.ControlType) == ControlType.Edit)
            {
                new FlaUiTextBoxControl(input.AsTextBox()).Enter(time.ToString("c", CultureInfo.InvariantCulture));
                Keyboard.Type(VirtualKeyShort.RETURN);
                return;
            }

            new FlaUiTimePickerControl(materializedEditor ?? cell).SelectedTime = time;
        }

        private void EditColorCell(
            AutomationElement cell,
            AutomationElement materializedEditor,
            GridCellEditRequest request)
        {
            var expected = ColorValue.Normalize(request.Value);
            var editor = materializedEditor
                ?? new[] { cell }
                .Concat(FindAutomationDescendants(cell))
                .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Edit);
            if (editor is null)
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose an editable color-value surface.");
            }

            new FlaUiTextBoxControl(editor.AsTextBox()).Enter(expected);
        }

        private void EditComboBoxCell(
            AutomationElement cell,
            AutomationElement materializedEditor,
            GridCellEditRequest request)
        {
            var editor = materializedEditor
                ?? ResolveEditorPart(cell, request.EditorParts?.Input)
                ?? new[] { cell }
                .Concat(FindAutomationDescendants(cell))
                .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.ComboBox);
            if (editor is null)
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose a ComboBox editor.");
            }

            var stopwatch = Stopwatch.StartNew();
            if (request.EditorParts?.Results is null
                && TryRead(() => editor.ControlType) == ControlType.ComboBox)
            {
                new FlaUiComboBoxControl(editor.AsComboBox()).SelectItem(
                    request.Value, TimeSpan.FromMilliseconds(RemainingGridMilliseconds(stopwatch, request.TimeoutMs)));
                return;
            }

            var results = ResolveEditorPart(cell, request.EditorParts?.Results);
            if (results is null || !HasVisibleBounds(results))
            {
                var open = ResolveEditorPart(cell, request.EditorParts?.OpenButton);
                if (open is null)
                {
                    throw new InvalidOperationException(
                        $"Combo-box editor in grid '{AutomationId}' requires a visible configured Results surface "
                        + $"or OpenButton. Input type: {TryRead(() => editor.ControlType)}; parts: {request.EditorParts}.");
                }
                new FlaUiButtonControl(open.AsButton()).Invoke();
            }
            results = UiWait.Until(
                () => ResolveEditorPart(cell, request.EditorParts?.Results),
                candidate => candidate is not null && HasVisibleBounds(candidate),
                new UiWaitOptions
                {
                    Timeout = TimeSpan.FromMilliseconds(RemainingGridMilliseconds(stopwatch, request.TimeoutMs)),
                    PollInterval = TimeSpan.FromMilliseconds(25)
                },
                $"Configured combo-box Results in grid '{AutomationId}' did not appear.");
            var remaining = TimeSpan.FromMilliseconds(RemainingGridMilliseconds(stopwatch, request.TimeoutMs));
            if (TryRead(() => results!.ControlType) == ControlType.List)
            {
                new FlaUiListBoxControl(results!.AsListBox()).SelectItem(request.Value, remaining);
            }
            else if (TryRead(() => results!.ControlType) == ControlType.ComboBox)
            {
                new FlaUiComboBoxControl(results!.AsComboBox()).SelectItem(request.Value, remaining);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Configured combo-box Results in grid '{AutomationId}' expose '{TryRead(() => results!.ControlType)}', not ListBox or ComboBox.");
            }
        }

        private static string DescribeGridComboElement(string role, AutomationElement element) =>
            $"{role}: {TryRead(() => element.ControlType)} id='{TryRead(() => element.AutomationId)}' "
            + $"name='{TryRead(() => element.Name)}' bounds={TryRead(() => element.BoundingRectangle)} "
            + $"offscreen={TryRead(() => element.IsOffscreen)}";

        private void ConfirmCellEdit(AutomationElement cell, GridCellEditRequest request)
        {
            var confirm = ResolveEditorPart(cell, request.EditorParts?.ConfirmButton);
            if (confirm is not null)
            {
                new FlaUiButtonControl(confirm.AsButton()).Invoke();
                return;
            }

            if (request.EditorParts?.CommitTarget is { } commitTargetLocator)
            {
                var commitTarget = ResolveEditorPart(cell, commitTargetLocator)
                    ?? throw new InvalidOperationException(
                        $"Grid editor commit target '{commitTargetLocator.LocatorKind}:{commitTargetLocator.LocatorValue}' "
                        + $"was not found within scope '{commitTargetLocator.Scope}' of the active row in grid '{AutomationId}'.");
                var activeInput = ResolveEditorPart(cell, request.EditorParts.Input);
                if (activeInput is not null)
                {
                    TryFocus(commitTarget);
                    var focusStopwatch = Stopwatch.StartNew();
                    var maximumFocusWait = Math.Min(500, request.TimeoutMs);
                    do
                    {
                        if (!TryRead(() => activeInput.IsAvailable)
                            || !HasVisibleBounds(activeInput))
                        {
                            return;
                        }

                        var remainingFocusWait = maximumFocusWait - (int)focusStopwatch.ElapsedMilliseconds;
                        if (remainingFocusWait > 0)
                        {
                            Thread.Sleep(Math.Min(25, remainingFocusWait));
                        }
                    }
                    while (focusStopwatch.ElapsedMilliseconds < maximumFocusWait);
                }

                DesktopPointer.ClickPrepared(
                    () => ResolveEditorPart(cell, commitTargetLocator) ?? commitTarget,
                    PrepareForPhysicalGridInput);
                return;
            }

            if (request.EditorKind == GridCellEditorKind.ComboBox
                && request.EditorParts?.Results is not null)
            {
                // Exact selection may update only the active composite editor. Keep its current
                // focus and let the grid commit the pending value before resolving the display cell.
                Keyboard.Type(VirtualKeyShort.RETURN);
                return;
            }

            if (request.EditorKind is GridCellEditorKind.Text
                or GridCellEditorKind.Number
                or GridCellEditorKind.Color)
            {
                TryFocus(ResolveEditorPart(cell, request.EditorParts?.Input) ?? cell);
                Keyboard.Type(VirtualKeyShort.RETURN);
            }
        }

        private void CancelCellEdit(AutomationElement cell, GridCellEditRequest request)
        {
            var cancel = ResolveEditorPart(cell, request.EditorParts?.CancelButton);
            if (cancel is not null)
            {
                new FlaUiButtonControl(cancel.AsButton()).Invoke();
                return;
            }

            TryFocus(cell);
            Keyboard.Type(VirtualKeyShort.ESCAPE);
        }

        private void EditCheckBoxCell(
            AutomationElement cell,
            AutomationElement materializedEditor,
            GridCellEditRequest request)
        {
            if (!bool.TryParse(request.Value, out var expected))
            {
                throw new InvalidOperationException(
                    $"Grid check-box value '{request.Value}' is not a Boolean value.");
            }

            var editor = materializedEditor
                ?? ResolveEditorPart(cell, request.EditorParts?.Input)
                ?? new[] { cell }
                    .Concat(FindAutomationDescendants(cell))
                    .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.CheckBox);
            if (editor is null || TryRead(() => editor.ControlType) != ControlType.CheckBox)
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose a CheckBox editor.");
            }

            var checkBox = new FlaUiCheckBoxControl(editor.AsCheckBox());
            if (checkBox.IsChecked != expected)
            {
                TryScrollIntoView(editor);
                TryFocus(editor);
                DesktopPointer.ClickFallback(editor, "grid editor activation fallback");
            }
        }

        private AutomationElement? ResolveEditorPart(AutomationElement cell, GridRelativeLocator? locator)
        {
            return ResolveGridEditorPart(
                _searchRoot,
                cell,
                FindGridRoot(),
                locator,
                CreateAmbiguousEditorPartException);
        }

        private InvalidOperationException CreateAmbiguousEditorPartException(
            GridRelativeLocator locator)
        {
            return new InvalidOperationException(
                $"Grid editor part '{locator.LocatorKind}:{locator.LocatorValue}' is ambiguous "
                + $"within scope '{locator.Scope}' of the active cell in grid '{AutomationId}'.");
        }

        private AutomationElement? WaitForEditorPart(
            AutomationElement cell,
            GridRelativeLocator locator,
            TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            do
            {
                var result = ResolveEditorPart(cell, locator);
                if (result is not null
                    && TryRead(() => result.IsAvailable)
                    && (HasVisibleBounds(result)
                        || (locator.Scope == GridRelativeLocatorScope.DetachedPopup
                            && HasUsableGridPopupSurface(result))))
                {
                    return result;
                }

                Thread.Sleep(50);
            }
            while (stopwatch.Elapsed < timeout);

            return null;
        }

        private static DateTime ParseGridDate(string value)
        {
            if (DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var exact))
            {
                return exact.Date;
            }

            throw new InvalidOperationException(
                $"Grid date value '{value}' is not a valid invariant date.");
        }

        private static TimeSpan ParseGridTime(string value)
        {
            if (TimeSpan.TryParseExact(value, "c", CultureInfo.InvariantCulture, out var time)
                && time >= TimeSpan.Zero
                && time < TimeSpan.FromDays(1))
            {
                return time;
            }

            throw new InvalidOperationException($"Grid time value '{value}' is not a valid invariant time of day.");
        }

        private AutomationElement? WaitForProcessElementByAutomationId(string automationId, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            do
            {
                var element = FindProcessElementByAutomationId(automationId);
                if (element is not null && TryRead(() => element.IsAvailable))
                {
                    return element;
                }

                Thread.Sleep(50);
            }
            while (stopwatch.Elapsed < timeout);

            return null;
        }

        private AutomationElement? FindProcessElementByAutomationId(string automationId)
        {
            var condition = _searchRoot.Automation.ConditionFactory.ByAutomationId(automationId);
            var local = TryRead(() => _searchRoot.FindFirstDescendant(condition));
            if (local is not null && TryRead(() => local.IsAvailable))
            {
                return local;
            }

            var processId = TryRead(() => _searchRoot.FrameworkAutomationElement.ProcessId.ValueOrDefault);
            var desktop = TryRead(() => _searchRoot.Automation.GetDesktop());
            if (processId <= 0 || desktop is null)
            {
                return null;
            }

            var processRoots = TryRead(() => desktop.FindAllChildren(factory => factory.ByProcessId(processId)))
                ?? Array.Empty<AutomationElement>();
            foreach (var root in processRoots)
            {
                if (root is null || !TryRead(() => root.IsAvailable))
                {
                    continue;
                }

                var match = TryRead(() => root.FindFirstDescendant(condition));
                if (match is not null && TryRead(() => match.IsAvailable))
                {
                    return match;
                }
            }

            return null;
        }

        private static string? TryGetEditorAutomationId(string? inputAutomationId)
        {
            const string inputSuffix = "_Input";
            return !string.IsNullOrWhiteSpace(inputAutomationId)
                   && inputAutomationId.EndsWith(inputSuffix, StringComparison.Ordinal)
                ? inputAutomationId[..^inputSuffix.Length]
                : null;
        }

        public void SortByColumn(string columnName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

            if (_fallback is IGridUserActionControl actionFallback)
            {
                actionFallback.SortByColumn(columnName);
                return;
            }

            ThrowUnsupportedUserAction(nameof(SortByColumn));
        }

        public void ScrollToEnd()
        {
            if (_fallback is IGridUserActionControl actionFallback)
            {
                actionFallback.ScrollToEnd();
                return;
            }

            ThrowUnsupportedUserAction(nameof(ScrollToEnd));
        }

        public string CopyCell(int rowIndex, int columnIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);

            if (_fallback is IGridUserActionControl actionFallback)
            {
                return actionFallback.CopyCell(rowIndex, columnIndex);
            }

            ThrowUnsupportedUserAction(nameof(CopyCell));
            return string.Empty;
        }

        public void Export()
        {
            if (_fallback is IGridUserActionControl actionFallback)
            {
                actionFallback.Export();
                return;
            }

            ThrowUnsupportedUserAction(nameof(Export));
        }

        private AutomationElement[] FindOpenRowTargets(int rowIndex)
        {
            var targets = new List<AutomationElement>();
            var row = ReadRows()
                .FirstOrDefault(candidate =>
                    ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row") == rowIndex);
            if (row is not null)
            {
                targets.Add(row);
            }

            var cellRow = ReadCellRows()
                .FirstOrDefault(candidate => candidate.RowIndex == rowIndex);
            if (cellRow is not null)
            {
                targets.AddRange(cellRow.Cells);
            }

            return targets.ToArray();
        }

        private void ThrowUnsupportedUserAction(string actionName)
        {
            throw new System.NotSupportedException(
                $"Visual grid '{AutomationId}' does not support user action '{actionName}' in the FlaUI adapter.");
        }

        private int ResolveUniqueRowIndex(GridIndexedRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var matches = FindMatchingRowIndexes(row, timeoutMs);
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Grid row selector matched {matches.Length} rows in grid '{AutomationId}'; expected exactly one. "
                    + DescribeIndexedResolution(row, matches.Length));
            }

            return matches[0];
        }

        private int[] FindMatchingRowIndexes(GridIndexedRowSelector selector, int timeoutMs)
        {
            var matches = new HashSet<int>();
            var stopwatch = Stopwatch.StartNew();
            var horizontalScroll = FindGridHorizontalScrollPattern();
            if (horizontalScroll is not null)
            {
                MoveGridHorizontalScrollToStart(horizontalScroll, stopwatch, timeoutMs);
            }

            var scroll = FindGridScrollPattern();
            if (scroll is not null)
            {
                MoveGridScrollToStart(scroll, stopwatch, timeoutMs);
            }

            do
            {
                foreach (var row in ReadCurrentIndexedRows())
                {
                    if (selector.Conditions.All(condition =>
                            condition.ColumnIndex < row.Cells.Count
                            && string.Equals(
                                GridCellValueNormalizer.Normalize(AutomationId,
                                    new GridCellValueSnapshot(ReadVisualGridCellText(row.Cells[condition.ColumnIndex]))
                                    { IsDisplayOnly = true }, condition.Column).DisplayText,
                                condition.ExpectedText,
                                StringComparison.Ordinal)))
                    {
                        matches.Add(row.RowIndex);
                    }
                }
            }
            while (scroll is not null
                   && stopwatch.ElapsedMilliseconds < timeoutMs
                   && ScrollGridForward(scroll));

            return matches.OrderBy(static index => index).ToArray();
        }

        private IndexedFlaUiRow[] ReadCurrentIndexedRows()
        {
            var gridItemRows = ReadGridItemRows();
            if (gridItemRows.Length > 0)
            {
                return gridItemRows;
            }

            var cellRows = ReadCellRows();
            if (cellRows.Length > 0)
            {
                return cellRows
                    .Select(static row => new IndexedFlaUiRow(row.RowIndex, row.Cells))
                    .ToArray();
            }

            return ReadRows()
                .Select(row => new IndexedFlaUiRow(
                    ParseVisualGridIndex(TryRead(() => row.AutomationId), "_Row"),
                    new FlaUiVisualGridRowControl(row)
                        .ReadAutomationCells()))
                .Where(static row => row.RowIndex != int.MaxValue && row.Cells.Count > 0)
                .ToArray();
        }

        private IReadOnlyList<string> ReadColumnNames()
        {
            var headers = ReadNativeColumnHeaders();
            if (headers.Length > 0)
            {
                return headers.Select(static candidate => candidate.Name!).ToArray();
            }
            if (_fallback is IGridColumnMetadataControl metadata
                && metadata.ColumnNames.Count > 0)
            {
                return metadata.ColumnNames;
            }

            return Array.Empty<string>();
        }

        private NativeGridColumnHeader[] ReadNativeColumnHeaders()
        {
            var root = FindGridRoot();
            if (root is null)
            {
                return Array.Empty<NativeGridColumnHeader>();
            }

            var headerCondition = root.Automation.ConditionFactory.ByControlType(ControlType.HeaderItem);
            var rootBounds = TryRead(() => root.BoundingRectangle);
            return (TryRead(() => root.FindAllDescendants(headerCondition))
                    ?? Array.Empty<AutomationElement>())
                .Select(candidate => new NativeGridColumnHeader(
                    ReadVisualGridCellText(candidate),
                    TryRead(() => candidate.BoundingRectangle)))
                .Where(static candidate =>
                    !string.IsNullOrWhiteSpace(candidate.Name)
                    && candidate.Bounds.Width > 0)
                .Where(candidate => System.Drawing.Rectangle.Intersect(candidate.Bounds, rootBounds).Width > 0)
                .OrderBy(static candidate => candidate.Bounds.Left)
                .Select(static candidate => candidate with { Name = candidate.Name!.Trim() })
                .ToArray();
        }

        private void EnsureNativeColumnVisible(GridRuntimeColumn column, Stopwatch stopwatch, int timeoutMs)
        {
            if (column.RuntimeColumnName is { } caption)
            {
                var declaredIndexes = column.DeclaredRuntimeColumnNames
                    .Select((name, index) => (name, index))
                    .Where(candidate => string.Equals(candidate.name, caption, StringComparison.Ordinal))
                    .Take(2)
                    .ToArray();
                EnsureNativeColumnVisible(
                    caption,
                    stopwatch,
                    timeoutMs,
                    declaredIndexes.Length == 1 ? declaredIndexes[0].index : null,
                    column.DeclaredRuntimeColumnNames);
            }
        }

        private void EnsureNativeColumnVisible(
            string caption,
            Stopwatch stopwatch,
            int timeoutMs,
            int? configuredColumnIndex = null,
            IReadOnlyList<string>? declaredRuntimeColumnNames = null)
        {
            bool IsVisible() => ReadNativeColumnHeaders().Any(header =>
                string.Equals(header.Name, caption, StringComparison.Ordinal));
            bool WaitForTargetHeader(int maximumWaitMilliseconds)
            {
                var deadline = Math.Min(timeoutMs, stopwatch.ElapsedMilliseconds + maximumWaitMilliseconds);
                while (stopwatch.ElapsedMilliseconds < deadline)
                {
                    if (IsVisible())
                    {
                        return true;
                    }

                    Thread.Sleep((int)Math.Max(1, Math.Min(25, deadline - stopwatch.ElapsedMilliseconds)));
                }

                return IsVisible();
            }
            bool WaitForTargetOrMovement(string previousSignature, double? previousPosition, int maximumWaitMilliseconds)
            {
                var deadline = Math.Min(
                    timeoutMs,
                    stopwatch.ElapsedMilliseconds + maximumWaitMilliseconds);
                do
                {
                    if (IsVisible())
                    {
                        return true;
                    }

                    var currentSignature = Signature();
                    var currentPosition = Position();
                    if (!string.Equals(previousSignature, currentSignature, StringComparison.Ordinal)
                        || HasHorizontalPositionChanged(previousPosition, currentPosition))
                    {
                        return true;
                    }

                    var remaining = deadline - stopwatch.ElapsedMilliseconds;
                    if (remaining <= 0)
                    {
                        return false;
                    }

                    Thread.Sleep((int)Math.Min(25, remaining));
                }
                while (stopwatch.ElapsedMilliseconds < deadline);

                return false;
            }
            if (IsVisible())
            {
                return;
            }

            // Keep the row snapshot captured by HasNativeDataRows when the target column is already
            // visible. Re-reading that row while a composite editor is rebuilding its template can
            // replace a usable snapshot with a transient projection. A real horizontal move still
            // invalidates the snapshot before resolving the row again.
            _prefetchedNativeRows = null;

            var root = FindGridRoot();
            var namedHeader = root is null ? null
                : (TryRead(() => root.FindAllDescendants(root.Automation.ConditionFactory.ByControlType(ControlType.HeaderItem))) ?? [])
                    .FirstOrDefault(header => string.Equals(ReadVisualGridCellText(header)?.Trim(), caption, StringComparison.Ordinal));
            var scroll = FindGridHorizontalScrollPattern();
            AutomationElement? FindCurrentHorizontalScrollBar()
            {
                var currentRoot = FindGridRoot();
                return currentRoot is null ? null : FindAutomationDescendants(currentRoot)
                    .Where(candidate => TryRead(() => candidate.ControlType) == ControlType.ScrollBar
                        && TryRead(() => candidate.BoundingRectangle) is var bounds && bounds.Width > bounds.Height)
                    .FirstOrDefault(HasVisibleBounds);
            }
            IRangeValuePattern? FindCurrentHorizontalRange()
            {
                var currentScrollBar = FindCurrentHorizontalScrollBar();
                return currentScrollBar is null
                    ? null
                    : TryRead(() => currentScrollBar.Patterns.RangeValue.PatternOrDefault);
            }

            string Signature() => string.Join("|", ReadNativeColumnHeaders().Select(header => $"{header.Name}@{header.Bounds}"));
            double? Position()
            {
                if (scroll?.HorizontallyScrollable.ValueOrDefault == true)
                {
                    var percent = TryRead(() => scroll.HorizontalScrollPercent.ValueOrDefault);
                    if (percent >= 0)
                    {
                        return percent;
                    }
                }

                var currentRange = FindCurrentHorizontalRange();
                return currentRange is null ? null : TryRead(() => currentRange.Value.ValueOrDefault);
            }
            bool AtBoundary(bool forward)
            {
                if (scroll?.HorizontallyScrollable.ValueOrDefault == true)
                {
                    var percent = TryRead(() => scroll.HorizontalScrollPercent.ValueOrDefault);
                    if (percent >= 0)
                    {
                        return forward ? percent >= 99.999 : percent <= 0.001;
                    }
                }

                var currentRange = FindCurrentHorizontalRange();
                if (currentRange is not null)
                {
                    var value = TryRead(() => currentRange.Value.ValueOrDefault);
                    var boundary = forward
                        ? TryRead(() => currentRange.Maximum.ValueOrDefault)
                        : TryRead(() => currentRange.Minimum.ValueOrDefault);
                    return forward ? value >= boundary - 0.001 : value <= boundary + 0.001;
                }

                return false;
            }
            bool MoveWithScrollPattern(bool forward) => TryRead(() =>
            {
                if (scroll?.HorizontallyScrollable.ValueOrDefault != true)
                {
                    return false;
                }

                scroll.Scroll(
                    forward ? ScrollAmount.LargeIncrement : ScrollAmount.LargeDecrement,
                    ScrollAmount.NoAmount);
                return true;
            });
            bool MoveWithScrollBarButton(bool forward) => TryRead(() =>
            {
                var scrollBar = FindCurrentHorizontalScrollBar();
                if (scrollBar is null)
                {
                    return false;
                }

                var buttons = FindAutomationDescendants(scrollBar)
                    .Where(candidate => TryRead(() => candidate.ControlType) == ControlType.Button
                        && HasVisibleBounds(candidate))
                    .OrderBy(candidate => TryRead(() => candidate.BoundingRectangle).Left)
                    .ToArray();
                if (buttons.Length == 0)
                {
                    return false;
                }

                var button = forward ? buttons[^1] : buttons[0];
                new FlaUiButtonControl(button.AsButton()).Invoke();
                return true;
            });
            bool PageScrollBar(bool forward) => TryRead(() =>
            {
                var scrollBar = FindCurrentHorizontalScrollBar();
                if (scrollBar is null)
                {
                    return false;
                }

                var thumb = FindAutomationDescendants(scrollBar)
                    .FirstOrDefault(candidate =>
                        TryRead(() => candidate.ControlType) == ControlType.Thumb
                        && HasVisibleBounds(candidate));
                if (thumb is null)
                {
                    return false;
                }

                var scrollBounds = TryRead(() => scrollBar.BoundingRectangle);
                var thumbBounds = TryRead(() => thumb.BoundingRectangle);
                var trackStart = scrollBounds.Left + scrollBounds.Height + 1;
                var trackEnd = scrollBounds.Right - scrollBounds.Height - 1;
                var pagePoint = forward
                    ? Math.Min(trackEnd, thumbBounds.Right + Math.Max(2, thumbBounds.Width / 2))
                    : Math.Max(trackStart, thumbBounds.Left - Math.Max(2, thumbBounds.Width / 2));
                if (forward ? pagePoint <= thumbBounds.Right : pagePoint >= thumbBounds.Left)
                {
                    return false;
                }

                DesktopPointer.ClickPrepared(
                    () => FindCurrentHorizontalScrollBar() ?? scrollBar,
                    element => PrepareForPhysicalGridInput(element),
                    element => new System.Drawing.Point(
                        pagePoint,
                        TryRead(() => element.BoundingRectangle).Top + scrollBounds.Height / 2),
                    options: new PointerClickOptions
                    {
                        Timeout = TimeSpan.FromMilliseconds(RemainingGridMilliseconds(stopwatch, timeoutMs))
                    });
                return true;
            });
            bool DragScrollBarToBoundary(bool forward)
            {
                try
                {
                    var scrollBar = FindCurrentHorizontalScrollBar();
                    if (scrollBar is null)
                    {
                        return false;
                    }

                    var thumb = FindAutomationDescendants(scrollBar)
                        .FirstOrDefault(candidate =>
                            TryRead(() => candidate.ControlType) == ControlType.Thumb
                            && HasVisibleBounds(candidate));
                    if (thumb is null)
                    {
                        return false;
                    }

                    var scrollBounds = TryRead(() => scrollBar.BoundingRectangle);
                    var thumbBounds = TryRead(() => thumb.BoundingRectangle);
                    var start = new System.Drawing.Point(
                        thumbBounds.Left + thumbBounds.Width / 2,
                        thumbBounds.Top + thumbBounds.Height / 2);
                    var endX = forward
                        ? scrollBounds.Right - scrollBounds.Height - thumbBounds.Width / 2 - 1
                        : scrollBounds.Left + scrollBounds.Height + thumbBounds.Width / 2 + 1;
                    if (forward ? endX <= start.X : endX >= start.X)
                    {
                        return false;
                    }

                    DesktopPointer.Drag(
                        thumb,
                        start,
                        new System.Drawing.Point(endX, start.Y),
                        prepare: () => PrepareForPhysicalGridInput(thumb),
                        timeout: TimeSpan.FromMilliseconds(RemainingGridMilliseconds(stopwatch, timeoutMs)));
                    return true;
                }
                catch (Exception exception) when (!DesktopPointer.IsTerminalFailure(exception))
                {
                    return false;
                }
            }
            var configuredIndexesByName = declaredRuntimeColumnNames?
                .Select((name, index) => (name, index))
                .GroupBy(static candidate => candidate.name, StringComparer.Ordinal)
                .Where(static group => group.Count() == 1)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Single().index,
                    StringComparer.Ordinal);
            bool MoveToConfiguredPosition()
            {
                if (configuredColumnIndex is not { } targetIndex
                    || declaredRuntimeColumnNames is not { Count: > 1 })
                {
                    return false;
                }

                return TryRead(() =>
                {
                    var scrollBar = FindCurrentHorizontalScrollBar();
                    if (scrollBar is null)
                    {
                        return false;
                    }

                    var visibleHeaderCount = Math.Max(1, ReadNativeColumnHeaders().Length);
                    var targetIsInStartViewport = targetIndex < visibleHeaderCount;
                    var targetIsInEndViewport = targetIndex >= declaredRuntimeColumnNames.Count - visibleHeaderCount;
                    var direction = ResolveConfiguredDirection();
                    if (direction == false && targetIsInStartViewport)
                    {
                        return DragScrollBarToBoundary(forward: false);
                    }

                    if (direction == true && targetIsInEndViewport)
                    {
                        return DragScrollBarToBoundary(forward: true);
                    }

                    return false;
                });
            }

            if (namedHeader is not null)
            {
                var previousSignature = Signature();
                var previousPosition = Position();
                TryScrollIntoView(namedHeader);
                if (WaitForTargetOrMovement(previousSignature, previousPosition, 300)
                    && IsVisible())
                {
                    return;
                }
            }

            if (configuredColumnIndex is not null
                && declaredRuntimeColumnNames is { Count: > 1 })
            {
                if (MoveToConfiguredPosition())
                {
                    if (WaitForTargetHeader(700))
                    {
                        return;
                    }
                }
            }

            bool? ResolveConfiguredDirection()
            {
                if (configuredColumnIndex is not { } targetIndex
                    || configuredIndexesByName is not { Count: > 0 })
                {
                    return null;
                }

                var visibleIndexes = ReadNativeColumnHeaders()
                    .Select(header => header.Name is not null
                        && configuredIndexesByName.TryGetValue(header.Name, out var index)
                            ? (int?)index
                            : null)
                    .Where(static index => index.HasValue)
                    .Select(static index => index!.Value)
                    .ToArray();
                if (visibleIndexes.Length == 0)
                {
                    return null;
                }

                var orderedIndexes = visibleIndexes.Distinct().Order().ToArray();
                var scrollableRangeStart = 0;
                for (var index = 1; index < orderedIndexes.Length; index++)
                {
                    if (orderedIndexes[index] > orderedIndexes[index - 1] + 1)
                    {
                        scrollableRangeStart = index;
                    }
                }

                var currentScrollableMinimum = orderedIndexes[scrollableRangeStart];
                var currentScrollableMaximum = orderedIndexes[^1];
                if (targetIndex < currentScrollableMinimum)
                {
                    return false;
                }

                if (targetIndex > currentScrollableMaximum)
                {
                    return true;
                }

                return null;
            }

            bool ResolveGeometryDirection()
            {
                if (namedHeader is not null && root is not null)
                {
                    var targetBounds = TryRead(() => namedHeader.BoundingRectangle);
                    var rootBounds = TryRead(() => root.BoundingRectangle);
                    if (targetBounds.Width > 0 && targetBounds.Right <= rootBounds.Left)
                    {
                        return false;
                    }

                    if (targetBounds.Width > 0 && targetBounds.Left >= rootBounds.Right)
                    {
                        return true;
                    }
                }

                return !AtBoundary(forward: true);
            }

            var configuredDirection = ResolveConfiguredDirection();
            var moveForward = configuredDirection ?? ResolveGeometryDirection();
            var directionWasInferred = configuredDirection.HasValue;
            var directionChanges = 0;
            for (var step = 0; step < 64 && stopwatch.ElapsedMilliseconds < timeoutMs; step++)
            {
                if (IsVisible())
                {
                    return;
                }

                var currentConfiguredDirection = ResolveConfiguredDirection();
                if (currentConfiguredDirection.HasValue
                    && currentConfiguredDirection.Value != moveForward
                    && directionChanges == 0)
                {
                    moveForward = currentConfiguredDirection.Value;
                    directionWasInferred = true;
                    directionChanges++;
                }

                if (AtBoundary(moveForward))
                {
                    if (directionWasInferred || directionChanges > 0)
                    {
                        break;
                    }

                    moveForward = !moveForward;
                    directionChanges++;
                    continue;
                }

                var before = Signature();
                var beforePosition = Position();
                _prefetchedNativeRows = null;
                var moved = MoveWithScrollPattern(moveForward)
                    && WaitForTargetOrMovement(before, beforePosition, 300);
                if (!moved)
                {
                    before = Signature();
                    beforePosition = Position();
                    moved = PageScrollBar(moveForward)
                        && WaitForTargetOrMovement(before, beforePosition, 300);
                }
                if (!moved)
                {
                    before = Signature();
                    beforePosition = Position();
                    moved = MoveWithScrollBarButton(moveForward)
                        && WaitForTargetOrMovement(before, beforePosition, 300);
                }

                if (!moved)
                {
                    if (directionWasInferred || directionChanges > 0)
                    {
                        break;
                    }

                    moveForward = !moveForward;
                    directionChanges++;
                }
            }

            if (IsVisible())
            {
                return;
            }
            var finalRange = FindCurrentHorizontalRange();
            throw new InvalidOperationException(
                $"Grid '{AutomationId}' did not expose runtime column '{caption}' after horizontal materialization. "
                + $"The column may be hidden or unavailable through UI Automation. Current headers: {string.Join(", ", ColumnNames)}. "
                + $"Horizontal scroll: available={scroll?.HorizontallyScrollable.ValueOrDefault}, percent={scroll?.HorizontalScrollPercent.ValueOrDefault}; "
                + $"range={finalRange?.Value.ValueOrDefault}/{finalRange?.Maximum.ValueOrDefault}; "
                + $"scrollbars={string.Join("; ", root is null ? [] : FindAutomationDescendants(root).Where(item => TryRead(() => item.ControlType) == ControlType.ScrollBar).Select(item => DescribeGridComboElement("scrollbar", item)))}.");
        }

        private static bool HasHorizontalPositionChanged(double? previous, double? current)
        {
            return previous.HasValue
                && current.HasValue
                && Math.Abs(previous.Value - current.Value) >= 0.001;
        }

        private NativeFlaUiRow[] ReadNativeDataRows(bool includeCells = true)
        {
            var root = FindGridRoot();
            var headers = ReadNativeColumnHeaders();
            if (root is null || headers.Length == 0)
            {
                return Array.Empty<NativeFlaUiRow>();
            }

            var dataItemCondition = root.Automation.ConditionFactory.ByControlType(ControlType.DataItem);
            var rootBounds = TryRead(() => root.BoundingRectangle);
            var rowElements = TryRead(() => root.FindAllDescendants(dataItemCondition))
                ?? Array.Empty<AutomationElement>();
            return rowElements
                .Select(candidate => new
                {
                    Element = candidate,
                    Bounds = TryRead(() => candidate.BoundingRectangle),
                    IsOffscreen = TryRead(() => candidate.IsOffscreen)
                })
                .Where(static candidate =>
                    candidate.Bounds.Width > 0
                    && candidate.Bounds.Height > 0
                    && !candidate.IsOffscreen)
                .Where(candidate =>
                {
                    var intersection = System.Drawing.Rectangle.Intersect(candidate.Bounds, rootBounds);
                    return intersection.Width > 0 && intersection.Height > 0;
                })
                .OrderBy(static candidate => candidate.Bounds.Top)
                .Select(row => new NativeFlaUiRow(
                    row.Element,
                    includeCells ? ResolveNativeRowCells(row.Element, headers) : Array.Empty<AutomationElement>()))
                .Where(row => !includeCells || row.Cells.Count == headers.Length)
                .ToArray();
        }

        private static IReadOnlyList<AutomationElement> ResolveNativeRowCells(
            AutomationElement row,
            IReadOnlyList<NativeGridColumnHeader> headers)
        {
            var children = TryRead(() => row.FindAllChildren()) ?? Array.Empty<AutomationElement>();
            var rowBounds = TryRead(() => row.BoundingRectangle);
            var candidates = children
                .Select(candidate => new
                {
                    Element = candidate,
                    Bounds = TryRead(() => candidate.BoundingRectangle),
                    IsOffscreen = TryRead(() => candidate.IsOffscreen)
                })
                .Where(static candidate =>
                    candidate.Bounds.Width > 0
                    && candidate.Bounds.Height > 0
                    && !candidate.IsOffscreen)
                .ToArray();
            var cells = new List<AutomationElement>(headers.Count);
            foreach (var header in headers)
            {
                var cell = candidates
                    .Where(candidate =>
                        OverlapWidth(candidate.Bounds, header.Bounds) > 0
                        && System.Drawing.Rectangle.Intersect(candidate.Bounds, rowBounds).Height > 0)
                    .OrderByDescending(candidate =>
                        TryRead(() => candidate.Element.ControlType) != ControlType.Text)
                    .ThenByDescending(candidate => OverlapWidth(candidate.Bounds, header.Bounds))
                    .ThenBy(candidate => Math.Abs(candidate.Bounds.Width - header.Bounds.Width))
                    .ThenByDescending(static candidate => candidate.Bounds.Width)
                    .Select(static candidate => candidate.Element)
                    .FirstOrDefault();
                if (cell is null)
                {
                    return Array.Empty<AutomationElement>();
                }

                cells.Add(cell);
            }

            return cells;
        }

        private static int OverlapWidth(
            System.Drawing.Rectangle left,
            System.Drawing.Rectangle right)
        {
            return Math.Max(0, Math.Min(left.Right, right.Right) - Math.Max(left.Left, right.Left));
        }

        private static bool HasVisibleBounds(AutomationElement element)
        {
            var bounds = TryRead(() => element.BoundingRectangle);
            return bounds.Width > 0
                && bounds.Height > 0
                && !TryRead(() => element.IsOffscreen);
        }

        private IndexedFlaUiRow[] ReadGridItemRows()
        {
            var root = FindGridRoot();
            if (root is null)
            {
                return Array.Empty<IndexedFlaUiRow>();
            }

            return FindAutomationDescendants(root)
                .Select(candidate => new
                {
                    Element = candidate,
                    Pattern = TryRead(() => candidate.Patterns.GridItem.PatternOrDefault)
                })
                .Where(static candidate =>
                    candidate.Pattern is not null
                    && candidate.Pattern.Row.ValueOrDefault >= 0
                    && candidate.Pattern.Column.ValueOrDefault >= 0)
                .GroupBy(static candidate => candidate.Pattern!.Row.ValueOrDefault)
                .OrderBy(static group => group.Key)
                .Select(static group => new IndexedFlaUiRow(
                    group.Key,
                    group
                        .OrderBy(static candidate => candidate.Pattern!.Column.ValueOrDefault)
                        .Select(static candidate => candidate.Element)
                        .ToArray()))
                .Where(static row => row.Cells.Count > 0)
                .ToArray();
        }

        private AutomationElement? FindGridRoot()
        {
            if (_gridRoot is not null && TryRead(() => _gridRoot.IsAvailable))
            {
                return _gridRoot;
            }

            _gridRoot = new[] { _searchRoot }
                .Concat(FindAutomationDescendants(_searchRoot))
                .FirstOrDefault(candidate => string.Equals(
                    TryRead(() => candidate.AutomationId),
                    AutomationId,
                    StringComparison.Ordinal));
            return _gridRoot;
        }

        private IScrollPattern? FindGridScrollPattern()
        {
            var root = FindGridRoot();
            if (root is null)
            {
                return null;
            }

            var directPatterns = new[] { root }
                .Concat(FindAutomationDescendants(root))
                .Select(static candidate => TryRead(() => candidate.Patterns.Scroll.PatternOrDefault))
                .Where(static pattern => pattern is not null).ToArray();
            var direct = directPatterns.FirstOrDefault(static pattern => pattern!.VerticallyScrollable.ValueOrDefault);
            if (direct is not null)
            {
                return direct;
            }

            var rootBounds = TryRead(() => root.BoundingRectangle);
            for (var ancestor = TryRead(() => root.Parent);
                 ancestor is not null && TryRead(() => ancestor.ControlType) != ControlType.Window;
                 ancestor = TryRead(() => ancestor.Parent))
            {
                var ancestorBounds = TryRead(() => ancestor.BoundingRectangle);
                if (rootBounds.Width <= 0
                    || rootBounds.Height <= 0
                    || ancestorBounds.Width <= 0
                    || ancestorBounds.Height <= 0
                    || System.Drawing.Rectangle.Intersect(rootBounds, ancestorBounds) is not { Width: > 0, Height: > 0 })
                {
                    break;
                }

                var pattern = TryRead(() => ancestor.Patterns.Scroll.PatternOrDefault);
                if (pattern?.VerticallyScrollable.ValueOrDefault == true)
                {
                    return pattern;
                }
            }

            return null;
        }

        private IScrollPattern? FindGridHorizontalScrollPattern()
        {
            var root = FindGridRoot();
            if (root is null)
            {
                return null;
            }

            var directPatterns = new[] { root }
                .Concat(FindAutomationDescendants(root))
                .Select(static candidate => TryRead(() => candidate.Patterns.Scroll.PatternOrDefault))
                .Where(static pattern => pattern is not null)
                .ToArray();
            var direct = directPatterns.FirstOrDefault(static pattern => pattern!.HorizontallyScrollable.ValueOrDefault);
            if (direct is not null)
            {
                return direct;
            }

            var rootBounds = TryRead(() => root.BoundingRectangle);
            for (var ancestor = TryRead(() => root.Parent);
                 ancestor is not null && TryRead(() => ancestor.ControlType) != ControlType.Window;
                 ancestor = TryRead(() => ancestor.Parent))
            {
                var ancestorBounds = TryRead(() => ancestor.BoundingRectangle);
                if (rootBounds.Width <= 0
                    || rootBounds.Height <= 0
                    || ancestorBounds.Width <= 0
                    || ancestorBounds.Height <= 0
                    || System.Drawing.Rectangle.Intersect(rootBounds, ancestorBounds) is not { Width: > 0, Height: > 0 })
                {
                    break;
                }

                var pattern = TryRead(() => ancestor.Patterns.Scroll.PatternOrDefault);
                if (pattern?.HorizontallyScrollable.ValueOrDefault == true)
                {
                    return pattern;
                }
            }

            return directPatterns.FirstOrDefault();
        }

        private IRangeValuePattern? FindGridScrollBarRange()
        {
            var root = FindGridRoot();
            if (root is null)
            {
                return null;
            }

            return FindAutomationDescendants(root)
                .Where(static candidate =>
                    TryRead(() => candidate.ControlType) == ControlType.ScrollBar)
                .Where(static candidate =>
                {
                    var bounds = TryRead(() => candidate.BoundingRectangle);
                    return bounds.Height > bounds.Width;
                })
                .Select(static candidate =>
                    TryRead(() => candidate.Patterns.RangeValue.PatternOrDefault))
                .FirstOrDefault(static range =>
                    range is not null
                    && range.IsReadOnly.ValueOrDefault == false
                    && range.Maximum.ValueOrDefault >= range.Minimum.ValueOrDefault);
        }

        private GridScrollState FindGridScrollState()
        {
            var scrollBar = FindGridScrollBar();
            return new GridScrollState(
                FindGridScrollPattern(),
                FindGridScrollBarRange(),
                FindGridScrollButton("PART_PageUpButton")
                    ?? FindGridScrollButton("PART_LineUpButton"),
                FindGridScrollButton("PART_PageDownButton")
                    ?? FindGridScrollButton("PART_LineDownButton"),
                FindGridRoot(),
                scrollBar,
                scrollBar is null
                    ? null
                    : FindAutomationDescendants(scrollBar)
                        .FirstOrDefault(static candidate =>
                            TryRead(() => candidate.ControlType) == ControlType.Thumb
                            && HasVisibleBounds(candidate)));
        }

        private AutomationElement? FindGridScrollBar()
        {
            var root = FindGridRoot();
            if (root is null)
            {
                return null;
            }

            return FindAutomationDescendants(root)
                .FirstOrDefault(static candidate =>
                {
                    if (TryRead(() => candidate.ControlType) != ControlType.ScrollBar)
                    {
                        return false;
                    }

                    var bounds = TryRead(() => candidate.BoundingRectangle);
                    return bounds.Width > 0 && bounds.Height > bounds.Width;
                });
        }

        private AutomationElement? FindGridScrollButton(string automationId)
        {
            var root = FindGridRoot();
            if (root is null)
            {
                return null;
            }

            return FindAutomationDescendants(root)
                .FirstOrDefault(candidate =>
                    string.Equals(
                        TryRead(() => candidate.AutomationId),
                        automationId,
                        StringComparison.Ordinal)
                    && TryRead(() => candidate.ControlType) == ControlType.Button
                    && HasVisibleBounds(candidate));
        }

        private void MoveGridScrollToStart(
            GridScrollState scroll,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            if (IsGridScrollAtBoundary(scroll, forward: false))
            {
                return;
            }

            _prefetchedNativeRows = null;
            var reachedStartWithoutVisibleRowChange = false;
            if (scroll.ScrollPattern is not null)
            {
                var previousRows = ReadVisibleNativeRowSignature();
                var previousPosition = ReadGridScrollPosition(scroll);
                MoveGridScrollToStart(scroll.ScrollPattern, stopwatch, timeoutMs);
                _ = WaitForGridScrollProgress(
                    previousPosition,
                    previousRows,
                    stopwatch,
                    timeoutMs,
                    forward: false,
                    requireBoundary: true,
                    maximumWaitMilliseconds: 300);
                if (_prefetchedNativeRows is not null)
                {
                    return;
                }

                reachedStartWithoutVisibleRowChange = IsGridScrollAtBoundary(
                    FindGridScrollState(),
                    forward: false);
            }

            if (!reachedStartWithoutVisibleRowChange && scroll.RangeValuePattern is not null)
            {
                var previous = ReadVisibleNativeRowSignature();
                var previousPosition = ReadGridScrollPosition(scroll);
                var minimum = TryRead(() => scroll.RangeValuePattern.Minimum.ValueOrDefault);
                var moved = TryRead(() =>
                {
                    scroll.RangeValuePattern.SetValue(minimum);
                    return true;
                });
                if (moved
                    && WaitForGridScrollProgress(
                        previousPosition,
                        previous,
                        stopwatch,
                        timeoutMs,
                        forward: false,
                        requireBoundary: true,
                        maximumWaitMilliseconds: 300))
                {
                    if (_prefetchedNativeRows is not null)
                    {
                        return;
                    }

                    reachedStartWithoutVisibleRowChange = IsGridScrollAtBoundary(
                        FindGridScrollState(),
                        forward: false);
                }

                if (!reachedStartWithoutVisibleRowChange)
                {
                    var refreshedScroll = FindGridScrollState();
                    var refreshedRange = refreshedScroll.RangeValuePattern;
                    previousPosition = ReadGridScrollPosition(refreshedScroll);
                    if (refreshedRange is not null
                        && TryRead(() =>
                        {
                            refreshedRange.SetValue(
                                TryRead(() => refreshedRange.Minimum.ValueOrDefault));
                            return true;
                        })
                        && WaitForGridScrollProgress(
                            previousPosition,
                            previous,
                            stopwatch,
                            timeoutMs,
                            forward: false,
                            requireBoundary: true,
                            maximumWaitMilliseconds: 300))
                    {
                        if (_prefetchedNativeRows is not null)
                        {
                            return;
                        }

                        reachedStartWithoutVisibleRowChange = IsGridScrollAtBoundary(
                            FindGridScrollState(),
                            forward: false);
                    }
                }
            }

            var beforeKeyboardReset = ReadVisibleNativeRowSignature();
            var beforeKeyboardPosition = ReadGridScrollPosition(FindGridScrollState());
            if (TryMoveNativeGridWithKeyboard(forward: false)
                && WaitForGridScrollProgress(
                    beforeKeyboardPosition,
                    beforeKeyboardReset,
                    stopwatch,
                    timeoutMs,
                    forward: false,
                    requireBoundary: true,
                    maximumWaitMilliseconds: 1000))
            {
                return;
            }

            if (reachedStartWithoutVisibleRowChange)
            {
                return;
            }

            var beforeThumbReset = ReadVisibleNativeRowSignature();
            var beforeThumbPosition = ReadGridScrollPosition(FindGridScrollState());
            if (TryDragGridThumbToStart(scroll)
                && WaitForGridScrollProgress(
                    beforeThumbPosition,
                    beforeThumbReset,
                    stopwatch,
                    timeoutMs,
                    forward: false,
                    requireBoundary: true,
                    maximumWaitMilliseconds: 1000))
            {
                return;
            }

            while (scroll.BackwardButton is not null || scroll.Root is not null)
            {
                scroll = FindGridScrollState();
                if (IsGridScrollAtBoundary(scroll, forward: false))
                {
                    return;
                }
                _ = RemainingGridMilliseconds(stopwatch, timeoutMs);
                var previous = ReadVisibleNativeRowSignature();
                var previousPosition = ReadGridScrollPosition(scroll);
                var changed = scroll.BackwardButton is not null
                    && TryClickGridScrollButton(scroll.BackwardButton)
                    && WaitForGridScrollProgress(
                        previousPosition,
                        previous,
                        stopwatch,
                        timeoutMs,
                        forward: false);
                if (!changed
                    && scroll.Root is not null
                    && TryScrollGridWithWheel(scroll.Root, 3))
                {
                    changed = WaitForGridScrollProgress(
                        previousPosition,
                        previous,
                        stopwatch,
                        timeoutMs,
                        forward: false);
                }
                if (!changed
                    && scroll.Root is not null
                    && TrySendGridMouseWheel(scroll.Root, 3))
                {
                    changed = WaitForGridScrollProgress(
                        previousPosition,
                        previous,
                        stopwatch,
                        timeoutMs,
                        forward: false);
                }

                if (!changed
                    && TryPageGridScrollBar(scroll, forward: false))
                {
                    changed = WaitForGridScrollProgress(
                        previousPosition,
                        previous,
                        stopwatch,
                        timeoutMs,
                        forward: false);
                }

                if (!changed && scroll.RangeValuePattern is not null)
                {
                    var minimum = TryRead(() => scroll.RangeValuePattern.Minimum.ValueOrDefault);
                    var current = TryRead(() => scroll.RangeValuePattern.Value.ValueOrDefault);
                    changed = current > minimum
                        && TryRead(() =>
                        {
                            scroll.RangeValuePattern.SetValue(minimum);
                            return true;
                        })
                        && WaitForGridScrollProgress(
                            previousPosition,
                            previous,
                            stopwatch,
                            timeoutMs,
                            forward: false);
                }

                if (!changed)
                {
                    return;
                }
            }
        }

        private bool WaitForGridScrollProgress(
            GridScrollPosition previousPosition,
            string previousVisibleRows,
            Stopwatch stopwatch,
            int timeoutMs,
            bool forward,
            bool requireBoundary = false,
            int maximumWaitMilliseconds = 300)
        {
            var waitDeadline = Math.Min(
                timeoutMs,
                stopwatch.ElapsedMilliseconds + maximumWaitMilliseconds);
            long? boundaryReachedAt = null;
            while (stopwatch.ElapsedMilliseconds < waitDeadline)
            {
                var remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    return false;
                }

                Thread.Sleep(Math.Min(25, remaining));
                var currentScroll = FindGridScrollState();
                var currentPosition = ReadGridScrollPosition(currentScroll);
                var isAtBoundary = IsGridScrollAtBoundary(currentScroll, forward);
                if (isAtBoundary)
                {
                    boundaryReachedAt ??= stopwatch.ElapsedMilliseconds;
                }
                else if (!requireBoundary && currentPosition.HasMovedFrom(previousPosition, forward))
                {
                    return true;
                }

                var rows = ReadNativeDataRows();
                if (!string.Equals(
                        previousVisibleRows,
                        CreateNativeRowSignature(rows),
                        StringComparison.Ordinal))
                {
                    _prefetchedNativeRows = rows;
                    if (isAtBoundary
                        || !requireBoundary
                        || !currentPosition.CanCompareWith(previousPosition))
                    {
                        return true;
                    }
                }

                if (boundaryReachedAt is { } reachedAt
                    && stopwatch.ElapsedMilliseconds - reachedAt >= maximumWaitMilliseconds)
                {
                    return true;
                }
            }

            return boundaryReachedAt is not null;
        }

        private bool MoveGridScrollForward(
            GridScrollState scroll,
            Stopwatch stopwatch,
            int timeoutMs,
            string? previousSignature = null,
            double? rangeIncrement = null)
        {
            if (stopwatch.ElapsedMilliseconds >= timeoutMs || IsGridScrollAtBoundary(scroll, forward: true))
            {
                return false;
            }

            if (scroll.ScrollPattern is not null)
            {
                return ScrollGridForward(scroll.ScrollPattern);
            }

            var previous = previousSignature ?? ReadVisibleNativeRowSignature();
            if (scroll.RangeValuePattern is not null
                && TryMoveGridRangeForward(
                    scroll.RangeValuePattern,
                    rangeIncrement,
                    previous,
                    stopwatch,
                    timeoutMs))
            {
                return true;
            }

            if (scroll.ForwardButton is not null
                && TryClickGridScrollButton(scroll.ForwardButton)
                && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs))
            {
                return true;
            }

            if (TryMoveNativeGridWithKeyboard(forward: true)
                && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs))
            {
                return true;
            }

            if (TryPageGridScrollBar(scroll, forward: true)
                && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs))
            {
                return true;
            }

            if (TryDragGridThumbForward(scroll)
                && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs))
            {
                return true;
            }

            if (scroll.Root is not null
                && TryScrollGridWithWheel(scroll.Root, -3)
                && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs))
            {
                return true;
            }

            if (scroll.Root is not null
                && TrySendGridMouseWheel(scroll.Root, -3)
                && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs))
            {
                return true;
            }

            return false;
        }

        private static bool IsGridScrollAtBoundary(GridScrollState scroll, bool forward)
        {
            var pattern = scroll.ScrollPattern;
            var range = scroll.RangeValuePattern;
            return GridScrollBoundary.IsReached(
                pattern is null
                    ? null
                    : TryRead(() => (bool?)pattern.VerticallyScrollable.Value),
                pattern is null
                    ? null
                    : TryRead(() => (double?)pattern.VerticalScrollPercent.Value),
                range is null ? null : TryRead(() => (double?)range.Minimum.Value),
                range is null ? null : TryRead(() => (double?)range.Maximum.Value),
                range is null ? null : TryRead(() => (double?)range.Value.Value),
                forward);
        }

        private bool TryMoveGridRangeForward(
            IRangeValuePattern rangeValue,
            double? requestedIncrement,
            string previousSignature,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var current = TryRead(() => rangeValue.Value.ValueOrDefault);
            var minimum = TryRead(() => rangeValue.Minimum.ValueOrDefault);
            var maximum = TryRead(() => rangeValue.Maximum.ValueOrDefault);
            var range = maximum - minimum;
            var tolerance = Math.Max(0.001, range / 10_000);
            if (current >= maximum - tolerance)
            {
                return false;
            }

            var largeChange = TryRead(() => rangeValue.LargeChange.ValueOrDefault);
            var smallChange = TryRead(() => rangeValue.SmallChange.ValueOrDefault);
            var increment = Math.Max(
                1,
                requestedIncrement ?? Math.Max(largeChange, Math.Max(smallChange, range / 10)));
            var next = Math.Min(maximum, current + increment);
            var moved = TryRead(() =>
            {
                rangeValue.SetValue(next);
                return true;
            });
            if (!moved)
            {
                return false;
            }

            return WaitForVisibleNativeRowsToChange(
                previousSignature,
                stopwatch,
                timeoutMs,
                maximumWaitMilliseconds: 500);
        }

        private static double? EstimateNativeScrollIncrement(IReadOnlyList<NativeFlaUiRow> rows)
        {
            var bounds = rows
                .Where(static row => row.IsVisible)
                .Select(static row => TryRead(() => row.Element.BoundingRectangle))
                .Where(static bounds => bounds.Height > 0)
                .OrderBy(static bounds => bounds.Top)
                .ToArray();
            if (bounds.Length == 0)
            {
                return null;
            }

            var visibleExtent = bounds[^1].Bottom - bounds[0].Top;
            var overlap = bounds.Min(static bounds => bounds.Height);
            return Math.Max(1, visibleExtent - overlap);
        }

        private static GridScrollPosition ReadGridScrollPosition(GridScrollState scroll)
        {
            return new GridScrollPosition(
                scroll.ScrollPattern is null
                    ? null
                    : TryRead(() => scroll.ScrollPattern.VerticalScrollPercent.ValueOrDefault),
                scroll.RangeValuePattern is null
                    ? null
                    : TryRead(() => scroll.RangeValuePattern.Value.ValueOrDefault));
        }

        private bool TryRestoreGridScrollPosition(
            GridScrollState scroll,
            GridScrollPosition position,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            if (stopwatch.ElapsedMilliseconds >= timeoutMs)
            {
                return false;
            }

            var previous = ReadVisibleNativeRowSignature();
            if (position.RangeValue is { } targetRange
                && scroll.RangeValuePattern is not null)
            {
                var minimum = TryRead(() => scroll.RangeValuePattern.Minimum.ValueOrDefault);
                if (Math.Abs(targetRange - minimum) <= 0.001
                    && TryMoveNativeGridWithKeyboard(forward: false)
                    && WaitForVisibleNativeRowsToChange(
                        previous,
                        stopwatch,
                        timeoutMs,
                        maximumWaitMilliseconds: 1000))
                {
                    return true;
                }
            }

            var restored = false;
            if (scroll.ScrollPattern is not null && position.ScrollPercent is { } scrollPercent)
            {
                restored = TryRead(() =>
                {
                    scroll.ScrollPattern.SetScrollPercent(-1, scrollPercent);
                    return true;
                });
            }
            else if (scroll.RangeValuePattern is not null && position.RangeValue is { } rangeValue)
            {
                restored = TryRead(() =>
                {
                    scroll.RangeValuePattern.SetValue(rangeValue);
                    return true;
                });
            }

            if (!restored)
            {
                return false;
            }

            return WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs);
        }

        private bool TryDragGridThumbToStart(GridScrollState scroll)
        {
            if (scroll.ScrollBar is null || scroll.Thumb is null)
            {
                return false;
            }

            var scrollBounds = TryRead(() => scroll.ScrollBar.BoundingRectangle);
            var thumbBounds = TryRead(() => scroll.Thumb.BoundingRectangle);
            var targetCenterY = scrollBounds.Top + scrollBounds.Width + thumbBounds.Height / 2;
            if (thumbBounds.Top <= scrollBounds.Top + scrollBounds.Width + 1)
            {
                return false;
            }

            return TryDragGridThumb(scroll.Thumb, targetCenterY);
        }

        private bool TryDragGridThumbForward(GridScrollState scroll)
        {
            if (scroll.ScrollBar is null || scroll.Thumb is null)
            {
                return false;
            }

            var scrollBounds = TryRead(() => scroll.ScrollBar.BoundingRectangle);
            var thumbBounds = TryRead(() => scroll.Thumb.BoundingRectangle);
            var maximumTop = scrollBounds.Bottom - scrollBounds.Width - thumbBounds.Height;
            if (thumbBounds.Top >= maximumTop - 1)
            {
                return false;
            }

            var delta = Math.Max(1, Math.Min(3, thumbBounds.Height / 4));
            return TryDragGridThumb(
                scroll.Thumb,
                Math.Min(maximumTop + thumbBounds.Height / 2, thumbBounds.Top + thumbBounds.Height / 2 + delta));
        }

        private bool TryDragGridThumb(AutomationElement thumb, int targetCenterY)
        {
            if (!TryRead(() => thumb.IsAvailable) || !HasVisibleBounds(thumb))
            {
                return false;
            }

            return TryRead(() =>
            {
                var bounds = TryRead(() => thumb.BoundingRectangle);
                var start = new System.Drawing.Point(
                    bounds.Left + bounds.Width / 2,
                    bounds.Top + bounds.Height / 2);
                DesktopPointer.Drag(thumb, start, new System.Drawing.Point(start.X, targetCenterY),
                    prepare: () => PrepareForPhysicalGridInput(thumb));
                return true;
            });
        }

        private bool TryPageGridScrollBar(GridScrollState scroll, bool forward)
        {
            if (scroll.ScrollBar is null || scroll.Thumb is null)
            {
                return false;
            }

            var scrollBounds = TryRead(() => scroll.ScrollBar.BoundingRectangle);
            var thumbBounds = TryRead(() => scroll.Thumb.BoundingRectangle);
            var trackStart = scrollBounds.Top + scrollBounds.Width;
            var trackEnd = scrollBounds.Bottom - scrollBounds.Width;
            var availableStart = forward ? thumbBounds.Bottom + 2 : trackStart + 1;
            var availableEnd = forward ? trackEnd - 1 : thumbBounds.Top - 2;
            if (availableStart > availableEnd)
            {
                return false;
            }

            return TryRead(() =>
            {
                DesktopPointer.ClickAt(scroll.ScrollBar, new System.Drawing.Point(
                    scrollBounds.Left + scrollBounds.Width / 2,
                    availableStart + (availableEnd - availableStart) / 2),
                    prepare: () => PrepareForPhysicalGridInput(scroll.ScrollBar));
                return true;
            });
        }

        private string ReadVisibleNativeRowSignature()
        {
            return CreateNativeRowSignature(ReadNativeDataRows());
        }

        private string CreateNativeRowSignature(IEnumerable<NativeFlaUiRow> rows)
        {
            return string.Join(
                "\u001e",
                rows.Where(static row => row.IsVisible).Select(row =>
                {
                    var rowIndex = ReadNativeGridRowIndex(row);
                    var signatureColumnIndexes = rowIndex is null
                        ? Enumerable.Range(0, row.Cells.Count)
                        : _nativeSignatureColumnIndexes;
                    return string.Join("\u001f",
                        ReadNativeRowRuntimeId(row.Element),
                        rowIndex,
                        TryRead(() => row.Element.BoundingRectangle),
                        string.Join(
                            "\u001d",
                            _nativeSignatureRowProperties.Select(property =>
                                FlaUiGridRowAutomationValueReader.Read(row.Element, property))),
                        string.Join(
                            "\u001d",
                            signatureColumnIndexes
                                .Where(index => index >= 0 && index < row.Cells.Count)
                                .Select(row.GetCellText)));
                }));
        }

        private static string? ReadNativeRowRuntimeId(AutomationElement element)
        {
            var runtimeId = TryRead(() => element.FrameworkAutomationElement.RuntimeId.ValueOrDefault);
            return runtimeId is { Length: > 0 } ? string.Join(',', runtimeId) : null;
        }

        private NativeFlaUiRow[] TakePrefetchedNativeRows()
        {
            var rows = _prefetchedNativeRows;
            _prefetchedNativeRows = null;
            return rows ?? ReadNativeDataRows();
        }

        private NativeFlaUiRow[] WaitForNativeDataRows(Stopwatch stopwatch, int timeoutMs)
        {
            var firstRead = true;
            return NativeGridTraversal.WaitForRows(
                () =>
                {
                    if (firstRead)
                    {
                        firstRead = false;
                        return TakePrefetchedNativeRows();
                    }

                    return ReadNativeDataRows();
                },
                () => stopwatch.ElapsedMilliseconds < timeoutMs,
                () =>
                {
                    var remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
                    if (remaining > 0)
                    {
                        Thread.Sleep(Math.Min(25, remaining));
                    }
                });
        }

        private bool WaitForVisibleNativeRowsToChange(
            string previous,
            Stopwatch stopwatch,
            int timeoutMs,
            int maximumWaitMilliseconds = 300)
        {
            var waitDeadline = Math.Min(
                timeoutMs,
                stopwatch.ElapsedMilliseconds + maximumWaitMilliseconds);
            while (stopwatch.ElapsedMilliseconds < waitDeadline)
            {
                var remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    return false;
                }

                Thread.Sleep(Math.Min(25, remaining));
                var rows = ReadNativeDataRows();
                if (!string.Equals(previous, CreateNativeRowSignature(rows), StringComparison.Ordinal))
                {
                    _prefetchedNativeRows = rows;
                    return true;
                }
            }

            return false;
        }

        private static bool TryClickGridScrollButton(AutomationElement button)
        {
            if (!TryRead(() => button.IsAvailable)
                || !TryRead(() => button.IsEnabled))
            {
                return false;
            }

            var typedButton = button.AsButton();
            if (typedButton.Patterns.Invoke.IsSupported)
            {
                // An Invoke failure may follow delivery. Do not replay it as wheel/keyboard input.
                typedButton.Invoke();
            }
            else
            {
                DesktopPointer.ClickFallback(typedButton, "grid scroll button lacks Invoke");
            }

            return true;
        }

        private bool TryScrollGridWithWheel(AutomationElement root, double lines)
        {
            if (!TryRead(() => root.IsAvailable) || !HasVisibleBounds(root))
            {
                return false;
            }

            return TryRead(() =>
            {
                var bounds = TryRead(() => root.BoundingRectangle);
                DesktopPointer.Scroll(root, new System.Drawing.Point(
                    bounds.Left + bounds.Width / 2,
                    bounds.Top + bounds.Height * 3 / 4), lines,
                    prepare: () => PrepareForPhysicalGridInput(root));
                return true;
            });
        }

        private static bool TrySendGridMouseWheel(AutomationElement root, int wheelNotches)
        {
            if (!TryRead(() => root.IsAvailable) || !HasVisibleBounds(root) || wheelNotches == 0)
            {
                return false;
            }

            var windowHandle = FindNativeWindowHandle(root);
            if (windowHandle == IntPtr.Zero)
            {
                return false;
            }

            return TryRead(() =>
            {
                var bounds = TryRead(() => root.BoundingRectangle);
                var screenPoint = new System.Drawing.Point(
                    bounds.Left + bounds.Width / 2,
                    bounds.Top + bounds.Height * 3 / 4);
                var packedPoint = new IntPtr(
                    (screenPoint.X & 0xFFFF) | ((screenPoint.Y & 0xFFFF) << 16));
                var wheelDelta = wheelNotches * 120;
                SendMessage(
                    windowHandle,
                    WindowMessageMouseWheel,
                    new IntPtr(wheelDelta << 16),
                    packedPoint);
                return true;
            });
        }

        private bool TryMoveNativeGridWithKeyboard(bool forward)
        {
            var rows = ReadNativeDataRows();
            var target = forward ? rows.LastOrDefault() : rows.FirstOrDefault();
            if (target is null)
            {
                return false;
            }

            return TryRead(() =>
            {
                if (target.Element.Patterns.SelectionItem.IsSupported)
                {
                    target.Element.Patterns.SelectionItem.Pattern.Select();
                }

                DesktopPointer.ClickPrepared(() => target.Element, PrepareForPhysicalGridInput);
                if (forward)
                {
                    Keyboard.Type(VirtualKeyShort.NEXT);
                }
                else
                {
                    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.HOME);
                }

                return true;
            });
        }

        private void PrepareForPhysicalGridInput(AutomationElement target)
        {
            var window = EnumerateAncestorsAndSelf(target)
                .FirstOrDefault(static candidate =>
                    TryRead(() => candidate.ControlType) == ControlType.Window);
            _ = TryRead(() =>
            {
                (window ?? _searchRoot).SetForeground();
                return true;
            });
            if (window is not null)
            {
                TryFocus(window);
            }

            TryFocus(target);
        }

        private static IEnumerable<AutomationElement> EnumerateAncestorsAndSelf(
            AutomationElement element)
        {
            for (var current = element; current is not null; current = TryRead(() => current.Parent))
            {
                yield return current;
            }
        }

        private static int RemainingGridMilliseconds(Stopwatch stopwatch, int timeoutMs)
        {
            var remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                throw new TimeoutException(
                    "The grid operation exceeded its timeout while resolving a stable row.");
            }

            return remaining;
        }

        private static bool TryMoveGridScrollTowardStart(IScrollPattern scroll)
        {
            var position = TryRead(() => scroll.VerticalScrollPercent.ValueOrDefault);
            if (position <= 0)
            {
                return false;
            }

            return TryRead(() =>
            {
                scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeDecrement);
                return true;
            });
        }

        private static void MoveGridScrollToStart(
            IScrollPattern scroll,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            const int maximumWaitMilliseconds = 300;
            var deadline = Math.Min(
                timeoutMs,
                stopwatch.ElapsedMilliseconds + maximumWaitMilliseconds);
            var previous = TryRead(() => scroll.VerticalScrollPercent.ValueOrDefault);
            var absoluteResetRequested = previous > 0 && TryRead(() =>
            {
                scroll.SetScrollPercent(-1, 0);
                return true;
            });
            while (previous > 0 && stopwatch.ElapsedMilliseconds < deadline)
            {
                if (!absoluteResetRequested && !TryMoveGridScrollTowardStart(scroll))
                {
                    return;
                }

                var remaining = (int)Math.Min(
                    timeoutMs - stopwatch.ElapsedMilliseconds,
                    deadline - stopwatch.ElapsedMilliseconds);
                if (remaining <= 0)
                {
                    return;
                }

                Thread.Sleep(Math.Min(25, remaining));
                var current = TryRead(() => scroll.VerticalScrollPercent.ValueOrDefault);
                if (current <= 0 || current >= previous)
                {
                    return;
                }

                previous = current;
            }
        }

        private static void MoveGridHorizontalScrollToStart(
            IScrollPattern scroll,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            const int maximumWaitMilliseconds = 300;
            var deadline = Math.Min(
                timeoutMs,
                stopwatch.ElapsedMilliseconds + maximumWaitMilliseconds);
            var previous = TryRead(() => scroll.HorizontalScrollPercent.ValueOrDefault);
            var absoluteResetRequested = previous > 0 && TryRead(() =>
            {
                scroll.SetScrollPercent(0, -1);
                return true;
            });
            while (previous > 0 && stopwatch.ElapsedMilliseconds < deadline)
            {
                if (!absoluteResetRequested)
                {
                    var moved = TryRead(() =>
                    {
                        scroll.Scroll(ScrollAmount.LargeDecrement, ScrollAmount.NoAmount);
                        return true;
                    });
                    if (!moved)
                    {
                        return;
                    }
                }

                var remaining = (int)Math.Min(
                    timeoutMs - stopwatch.ElapsedMilliseconds,
                    deadline - stopwatch.ElapsedMilliseconds);
                if (remaining <= 0)
                {
                    return;
                }

                Thread.Sleep(Math.Min(25, remaining));
                var current = TryRead(() => scroll.HorizontalScrollPercent.ValueOrDefault);
                if (current <= 0 || current >= previous)
                {
                    return;
                }

                previous = current;
            }
        }

        private static bool ScrollGridForward(IScrollPattern scroll)
        {
            var previous = TryRead(() => scroll.VerticalScrollPercent.ValueOrDefault);
            if (previous < 0 || previous >= 100)
            {
                return false;
            }

            var scrolled = TryRead(() =>
            {
                scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
                return true;
            });
            if (!scrolled)
            {
                return false;
            }

            Thread.Sleep(25);
            return TryRead(() => scroll.VerticalScrollPercent.ValueOrDefault) > previous;
        }

        private string DescribeIndexedResolution(GridIndexedRowSelector selector, int matchCount)
        {
            var conditions = string.Join(", ", selector.Conditions.Select(static condition =>
                $"column[{condition.ColumnIndex}]='{condition.ExpectedText}'"));
            return $"grid='{AutomationId}'; selector={conditions}; matches={matchCount}";
        }


        private IReadOnlyList<IGridRowControl> ReadVisualRows()
        {
            var rows = ReadRows()
                .Select(row => (IGridRowControl)new FlaUiVisualGridRowControl(row))
                .ToArray();
            if (rows.Length > 0)
            {
                return rows;
            }

            var cellRows = ReadCellRows()
                .Select(row => (IGridRowControl)new FlaUiVisualGridCellBackedRowControl(row.Cells))
                .ToArray();
            if (cellRows.Length > 0)
            {
                return cellRows;
            }

            return _fallback?.Rows ?? Array.Empty<IGridRowControl>();
        }

        private AutomationElement[] ReadRows()
        {
            var rowPrefix = $"{AutomationId}_Row";
            return FindAutomationDescendants(_searchRoot)
                .Where(candidate => IsVisualGridRow(candidate, rowPrefix))
                .OrderBy(candidate => ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row"))
                .ToArray();
        }

        private VisualGridCellRow[] ReadCellRows()
        {
            var cellPrefix = $"{AutomationId}_Row";
            return FindAutomationDescendants(_searchRoot)
                .Select(static candidate => new VisualGridCellCandidate(
                    candidate,
                    TryRead(() => candidate.AutomationId),
                    RowIndex: ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row"),
                    ColumnIndex: ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Cell")))
                .Where(candidate =>
                    candidate.AutomationId?.StartsWith(cellPrefix, StringComparison.Ordinal) == true
                    && candidate.RowIndex != int.MaxValue
                    && candidate.ColumnIndex != int.MaxValue
                    && HasExactVisualGridIndexSuffix(candidate.AutomationId, "_Cell"))
                .GroupBy(static candidate => candidate.RowIndex)
                .OrderBy(static group => group.Key)
                .Select(static group => new VisualGridCellRow(
                    group.Key,
                    group
                        .OrderBy(static candidate => candidate.ColumnIndex)
                        .Select(static candidate => candidate.Element)
                        .ToArray()))
                .ToArray();
        }

        private sealed record NativeGridColumnHeader(
            string? Name,
            System.Drawing.Rectangle Bounds);

        private sealed class NativeFlaUiRow
        {
            private readonly string?[] _cellTexts;
            private readonly bool[] _readCells;

            public NativeFlaUiRow(
                AutomationElement element,
                IReadOnlyList<AutomationElement> cells)
            {
                Element = element;
                Cells = cells;
                _cellTexts = new string?[cells.Count];
                _readCells = new bool[cells.Count];
            }

            public AutomationElement Element { get; }

            public IReadOnlyList<AutomationElement> Cells { get; }

            public bool IsVisible => HasVisibleBounds(Element);

            public string GetCellText(int columnIndex)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
                if (columnIndex >= Cells.Count)
                {
                    return string.Empty;
                }

                if (!_readCells[columnIndex])
                {
                    _cellTexts[columnIndex] = ReadGridCellText(Cells[columnIndex]) ?? string.Empty;
                    _readCells[columnIndex] = true;
                }

                return _cellTexts[columnIndex] ?? string.Empty;
            }
        }

        private sealed record NativeGridScan(
            IReadOnlyList<NativeGridRowSnapshot> Rows,
            IReadOnlyList<NativeGridRowSnapshot> MatchingRows,
            NativeFlaUiRow? LiveMatchingRow);

        private sealed record GridScrollState(
            IScrollPattern? ScrollPattern,
            IRangeValuePattern? RangeValuePattern,
            AutomationElement? BackwardButton,
            AutomationElement? ForwardButton,
            AutomationElement? Root,
            AutomationElement? ScrollBar,
            AutomationElement? Thumb);

        private sealed record MaterializedCellEditor(
            AutomationElement Cell,
            AutomationElement Editor);

        private sealed record IndexedFlaUiRow(int RowIndex, IReadOnlyList<AutomationElement> Cells);
    }

    private sealed record VisualGridCellCandidate(
        AutomationElement Element,
        string? AutomationId,
        int RowIndex,
        int ColumnIndex);

    private sealed record VisualGridCellRow(
        int RowIndex,
        IReadOnlyList<AutomationElement> Cells);

    private static void ExecuteFlaUiSearchPickerSelection(
        AutomationElement input,
        Func<TimeSpan, AutomationElement?> resolveResults,
        Func<AutomationElement?> resolveOpenButton,
        string searchText,
        string itemText,
        int timeoutMs,
        string targetDescription)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(resolveResults);
        ArgumentNullException.ThrowIfNull(resolveOpenButton);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchText);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);

        var stopwatch = Stopwatch.StartNew();
        TimeSpan Remaining()
        {
            var remaining = TimeSpan.FromMilliseconds(timeoutMs) - stopwatch.Elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        TimeSpan BoundedPopupWait(TimeSpan maximum)
        {
            var remaining = Remaining();
            if (remaining <= TimeSpan.Zero || maximum <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            var reserve = TimeSpan.FromMilliseconds(Math.Min(300, remaining.TotalMilliseconds / 3));
            var available = remaining - reserve;
            return available <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(Math.Max(1, remaining.TotalMilliseconds / 2))
                : available < maximum ? available : maximum;
        }

        new FlaUiTextBoxControl(input.AsTextBox()).Enter(searchText);
        var inputEnteredAt = stopwatch.Elapsed;

        var initialWait = BoundedPopupWait(TimeSpan.FromMilliseconds(500));

        var results = resolveResults(initialWait);
        var initialResultsResolvedAt = stopwatch.Elapsed;
        TimeSpan? openInvokedAt = null;
        if (results is null && Remaining() > TimeSpan.Zero)
        {
            var openButton = resolveOpenButton();
            if (openButton is not null)
            {
                new FlaUiButtonControl(openButton.AsButton()).Invoke();
                openInvokedAt = stopwatch.Elapsed;
            }

            results = resolveResults(BoundedPopupWait(TimeSpan.FromSeconds(1)));
        }
        var resultsResolvedAt = stopwatch.Elapsed;

        if (results is null)
        {
            throw new InvalidOperationException(
                $"Search picker in {targetDescription} does not expose configured results.");
        }

        var selectionTimeout = Remaining();
        if (selectionTimeout <= TimeSpan.Zero)
        {
            throw new TimeoutException(
                $"Search picker in {targetDescription} exhausted its timeout before selecting '{itemText}'. "
                + $"Stages: input={inputEnteredAt.TotalMilliseconds:0}ms; "
                + $"initial-results={initialResultsResolvedAt.TotalMilliseconds:0}ms; "
                + $"open={(openInvokedAt?.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) ?? "not-invoked")}ms; "
                + $"results={resultsResolvedAt.TotalMilliseconds:0}ms; timeout={timeoutMs}ms.");
        }

        if (TryRead(() => results.ControlType) == ControlType.List)
        {
            new FlaUiListBoxControl(results.AsListBox(), () => resolveResults(TimeSpan.Zero))
                .SelectItem(itemText, selectionTimeout);
            return;
        }

        if (TryRead(() => results.ControlType) == ControlType.ComboBox)
        {
            new FlaUiComboBoxControl(results.AsComboBox()).SelectItem(itemText, selectionTimeout);
            return;
        }

        throw new InvalidOperationException(
            $"Search picker results in {targetDescription} are neither a ListBox nor a ComboBox.");
    }

    private static bool TryDoubleClick(AutomationElement element, out Exception? exception)
    {
        try
        {
            DesktopPointer.ClickPrepared(() => element, target =>
            {
                TryScrollIntoView(target);
                TryFocus(target);
            }, options: new PointerClickOptions { ClickCount = 2 });
            exception = null;
            return true;
        }
        catch (Exception mouseException) when (!DesktopPointer.IsTerminalFailure(mouseException))
        {
            try
            {
                if (TrySendDoubleClickToContainingWindow(element))
                {
                    exception = null;
                    return true;
                }
            }
            catch (Exception nativeException) when (!DesktopPointer.IsTerminalFailure(nativeException))
            {
                exception = new AggregateException(
                    "The standard and native double-click paths both failed.",
                    mouseException,
                    nativeException);
                return false;
            }

            exception = mouseException;
            return false;
        }
    }

    internal static void SelectAndConfirmGridRow(
        AutomationElement target,
        AutomationElement? selectionContainer,
        int timeoutMs,
        string description,
        Func<AutomationElement?> refreshTarget,
        Func<AutomationElement?>? refreshSelectionContainer = null,
        Func<AutomationElement, bool>? isCurrentRowMatch = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(refreshTarget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        var stopwatch = Stopwatch.StartNew();
        var currentTarget = target;
        var targetBounds = TryRead(() => currentTarget.BoundingRectangle);
        var targetIsVisible = TryRead(() => currentTarget.IsAvailable)
            && !TryRead(() => currentTarget.IsOffscreen)
            && targetBounds.Width > 0
            && targetBounds.Height > 0;
        if (!targetIsVisible)
        {
            TryScrollIntoView(currentTarget);
        }
        if (!targetIsVisible || isCurrentRowMatch is null || !isCurrentRowMatch(currentTarget))
        {
            currentTarget = WaitForFreshGridRowTarget(refreshTarget, stopwatch, timeoutMs, description);
            selectionContainer = refreshSelectionContainer?.Invoke() ?? selectionContainer;
        }
        var selectable = FindSelectableGridRowElement(currentTarget);
        System.Drawing.Point? completedClickPoint = null;

        TimeSpan RemainingPointerTimeout()
        {
            var remaining = TimeSpan.FromMilliseconds(timeoutMs) - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException($"{description} exhausted its selection timeout before clicking.");
            return remaining;
        }

        AutomationElement ResolvePhysicalTarget(bool preferTextChild)
        {
            _ = RemainingPointerTimeout();
            currentTarget = refreshTarget()
                ?? throw new InvalidOperationException($"{description} disappeared before clicking.");
            if (refreshSelectionContainer is not null)
                selectionContainer = refreshSelectionContainer();
            return preferTextChild
                ? FindGridRowClickTarget(currentTarget) ?? currentTarget
                : FindSelectableGridRowElement(currentTarget) ?? currentTarget;
        }

        if (!TryConfirmGridRowSelection(
                currentTarget,
                selectable,
                selectionContainer,
                completedClickPoint: null,
                out _))
        {
            if (selectable is not null)
            {
                // A provider can apply Select before throwing; do not replay it as a click.
                selectable.Patterns.SelectionItem.Pattern.Select();
            }
            else
            {
                try
                {
                    System.Drawing.Point clickPoint = default;
                    DesktopPointer.ClickPrepared(
                        () => ResolvePhysicalTarget(preferTextChild: true),
                        element => { TryScrollIntoView(element); TryFocus(element); },
                        element => clickPoint = element.TryGetClickablePoint(out var point) ? point : GetBoundsCenter(element),
                        options: new PointerClickOptions { Timeout = RemainingPointerTimeout() });
                    completedClickPoint = clickPoint;
                }
                catch (Exception pointerFallbackException) when (!DesktopPointer.IsTerminalFailure(pointerFallbackException)
                    && pointerFallbackException is not TimeoutException
                    && stopwatch.ElapsedMilliseconds < timeoutMs)
                {
                    currentTarget = WaitForFreshGridRowTarget(refreshTarget, stopwatch, timeoutMs, description);
                    selectable = FindSelectableGridRowElement(currentTarget);
                    System.Drawing.Point clickPoint = default;
                    DesktopPointer.ClickPrepared(
                        () => ResolvePhysicalTarget(preferTextChild: false),
                        element => { TryScrollIntoView(element); TryFocus(element); },
                        element => clickPoint = GetVisibleGridRowPoint(element, selectionContainer),
                        options: new PointerClickOptions { Timeout = RemainingPointerTimeout() });
                    completedClickPoint = clickPoint;
                }
            }
        }

        var selectionStateWasReadable = false;
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (isCurrentRowMatch is null || !isCurrentRowMatch(currentTarget))
            {
                var refreshedTarget = refreshTarget();
                if (refreshedTarget is null)
                {
                    Thread.Sleep(Math.Max(
                        1,
                        Math.Min(25, timeoutMs - (int)stopwatch.ElapsedMilliseconds)));
                    continue;
                }

                currentTarget = refreshedTarget;
                selectionContainer = refreshSelectionContainer?.Invoke() ?? selectionContainer;
                selectable = FindSelectableGridRowElement(currentTarget);
            }
            if (TryConfirmGridRowSelection(
                    currentTarget,
                    selectable,
                    selectionContainer,
                    completedClickPoint,
                    out var currentStateWasReadable))
            {
                return;
            }
            selectionStateWasReadable |= currentStateWasReadable;

            Thread.Sleep(Math.Max(
                1,
                Math.Min(25, timeoutMs - (int)stopwatch.ElapsedMilliseconds)));
        }

        if (!selectionStateWasReadable)
        {
            throw new System.NotSupportedException(
                $"{description} exposes neither a readable SelectionItem pattern nor a readable grid selection; selection cannot be confirmed.");
        }

        throw new InvalidOperationException(
            $"{description} did not confirm its selected state within {timeoutMs} ms. "
            + DescribeGridSelectionEvidence(currentTarget, selectionContainer));
    }

    private static AutomationElement WaitForFreshGridRowTarget(
        Func<AutomationElement?> resolveTarget,
        Stopwatch stopwatch,
        int timeoutMs,
        string description)
    {
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            var target = resolveTarget();
            if (target is not null)
            {
                return target;
            }

            Thread.Sleep(Math.Max(
                1,
                Math.Min(25, timeoutMs - (int)stopwatch.ElapsedMilliseconds)));
        }

        throw new TimeoutException(
            $"{description} was not materialized after scrolling within {timeoutMs} ms.");
    }

    private static AutomationElement? FindSelectableGridRowElement(AutomationElement target)
    {
        for (var current = target; current is not null; current = TryRead(() => current.Parent))
        {
            if (TryRead(() => current.Patterns.SelectionItem.IsSupported))
            {
                return current;
            }
        }

        return null;
    }

    private static AutomationElement? FindGridRowClickTarget(AutomationElement row)
    {
        var rowBounds = TryRead(() => row.BoundingRectangle);
        return FindAutomationDescendants(row)
                .Where(candidate => TryRead(() => candidate.ControlType) == ControlType.Text)
                .FirstOrDefault(candidate =>
                {
                    var bounds = TryRead(() => candidate.BoundingRectangle);
                    return bounds.Width > 0
                        && bounds.Height > 0
                        && System.Drawing.Rectangle.Intersect(rowBounds, bounds) is { Width: > 0, Height: > 0 };
                })
            ?? row;
    }

    private static string DescribeGridSelectionEvidence(
        AutomationElement target,
        AutomationElement? selectionContainer)
    {
        var focused = TryRead(() => target.Automation.FocusedElement());
        var selectionSupported = selectionContainer is not null
            && TryRead(() => selectionContainer.Patterns.Selection.IsSupported);
        var selectedElements = selectionSupported
            ? TryRead(() => selectionContainer!.Patterns.Selection.Pattern.Selection.Value)
            : null;
        return $"targetType={TryRead(() => target.ControlType)}; "
            + $"targetBounds={TryRead(() => target.BoundingRectangle)}; "
            + $"focusedType={TryRead(() => focused?.ControlType)}; "
            + $"focusedBounds={TryRead(() => focused?.BoundingRectangle)}; "
            + $"containerType={TryRead(() => selectionContainer?.ControlType)}; "
            + $"selectionSupported={selectionSupported}; "
            + $"selectedCount={selectedElements?.Length.ToString(CultureInfo.InvariantCulture) ?? "<unreadable>"}; "
            + $"selected={string.Join(", ", (selectedElements ?? [])
                .Select(selected => $"{TryRead(() => selected.ItemStatus)}/{TryRead(() => selected.Name)}"))}.";
    }

    private static System.Drawing.Point GetVisibleGridRowPoint(
        AutomationElement row,
        AutomationElement? selectionContainer)
    {
        var rowBounds = TryRead(() => row.BoundingRectangle);
        var visibleBounds = selectionContainer is null
            ? rowBounds
            : System.Drawing.Rectangle.Intersect(
                rowBounds,
                TryRead(() => selectionContainer.BoundingRectangle));
        if (visibleBounds.Width <= 0 || visibleBounds.Height <= 0)
        {
            return row.TryGetClickablePoint(out var point) ? point : GetBoundsCenter(row);
        }

        return new System.Drawing.Point(
            visibleBounds.Left + Math.Min(64, Math.Max(1, visibleBounds.Width / 2)),
            visibleBounds.Top + Math.Max(1, visibleBounds.Height / 2));
    }

    private static bool TryConfirmGridRowSelection(
        AutomationElement target,
        AutomationElement? selectable,
        AutomationElement? selectionContainer,
        System.Drawing.Point? completedClickPoint,
        out bool selectionStateWasReadable)
    {
        bool? selectionItemIsSelected = null;
        if (selectable is not null
            && TryRead(() => selectable.Patterns.SelectionItem.IsSupported))
        {
            selectionItemIsSelected = TryRead(() =>
                (bool?)selectable.Patterns.SelectionItem.Pattern.IsSelected.Value);
        }

        AutomationElement[]? selectedElements = null;
        if (selectionContainer is not null
            && TryRead(() => selectionContainer.Patterns.Selection.IsSupported))
        {
            selectedElements = TryRead(() => selectionContainer.Patterns.Selection.Pattern.Selection.Value);
        }

        if (selectedElements is null && selectionContainer is not null)
        {
            selectedElements = TryRead(() => selectionContainer
                .AsGrid()
                .SelectedItems
                .Select(static row => (AutomationElement)row)
                .ToArray());
        }

        var targetIsSelected = selectedElements?.Any(selected =>
            RepresentsSameSelectedGridRow(selected, target)) == true;
        var focusedElement = TryRead(() => target.Automation.FocusedElement());
        var focusRepresentsTarget = focusedElement is not null
            && RepresentsFocusedGridRow(focusedElement, target, selectionContainer);
        // Click returns the cursor before selection is observed, so use the delivered point.
        var containerFocusConfirmsClick = completedClickPoint.HasValue
            && focusedElement is not null
            && selectionContainer is not null
            && IsSameAutomationElement(focusedElement, selectionContainer)
            && IsPointWithinGridRow(target, selectionContainer, completedClickPoint.Value);

        selectionStateWasReadable = selectionItemIsSelected.HasValue
            || selectedElements is not null
            || focusedElement is not null;
        return selectionItemIsSelected == true
            || (selectedElements is not null && targetIsSelected)
            || focusRepresentsTarget
            || containerFocusConfirmsClick;
    }

    private static bool IsPointWithinGridRow(
        AutomationElement target,
        AutomationElement selectionContainer,
        System.Drawing.Point point)
    {
        using var dpi = WindowsPointerBackend.PhysicalDpiScope.Enter();
        var targetBounds = TryRead(() => target.BoundingRectangle);
        var containerBounds = TryRead(() => selectionContainer.BoundingRectangle);
        if (targetBounds.Width <= 0
            || targetBounds.Height <= 0
            || containerBounds.Width <= 0
            || containerBounds.Height <= 0)
        {
            return false;
        }

        var visibleTargetBounds = System.Drawing.Rectangle.Intersect(targetBounds, containerBounds);
        return visibleTargetBounds.Width > 0
            && visibleTargetBounds.Height > 0
            && visibleTargetBounds.Contains(point);
    }

    private static bool RepresentsFocusedGridRow(
        AutomationElement focusedElement,
        AutomationElement target,
        AutomationElement? selectionContainer)
    {
        if (selectionContainer is not null
            && IsSameAutomationElement(focusedElement, selectionContainer))
        {
            return false;
        }

        if (IsSameAutomationElement(focusedElement, target)
            || IsAutomationAncestor(target, focusedElement))
        {
            return true;
        }

        var focusedBounds = TryRead(() => focusedElement.BoundingRectangle);
        var targetBounds = TryRead(() => target.BoundingRectangle);
        if (focusedBounds.Width <= 0
            || focusedBounds.Height <= 0
            || targetBounds.Width <= 0
            || targetBounds.Height <= 0
            || focusedBounds.Height > targetBounds.Height * 2)
        {
            return false;
        }

        var overlap = System.Drawing.Rectangle.Intersect(focusedBounds, targetBounds);
        return overlap.Width > 0
            && overlap.Height > 0
            && overlap.Height * 2 >= Math.Min(focusedBounds.Height, targetBounds.Height);
    }

    private static bool RepresentsSameSelectedGridRow(
        AutomationElement selected,
        AutomationElement target)
    {
        if (IsSameAutomationElement(selected, target)
            || IsAutomationAncestor(target, selected)
            || IsAutomationAncestor(selected, target))
        {
            return true;
        }

        var selectedStableValue = TryRead(() => selected.ItemStatus);
        var targetStableValue = TryRead(() => target.ItemStatus);
        if (string.IsNullOrWhiteSpace(selectedStableValue)
            || !string.Equals(selectedStableValue, targetStableValue, StringComparison.Ordinal))
        {
            return false;
        }

        var selectedBounds = TryRead(() => selected.BoundingRectangle);
        var targetBounds = TryRead(() => target.BoundingRectangle);
        if (selectedBounds.Height <= 0 || targetBounds.Height <= 0)
        {
            return false;
        }

        const int roundingTolerance = 1;
        var verticalOverlap = Math.Min(selectedBounds.Bottom, targetBounds.Bottom) + roundingTolerance
            - (Math.Max(selectedBounds.Top, targetBounds.Top) - roundingTolerance);
        return verticalOverlap > 0
            && verticalOverlap * 2 >= Math.Min(selectedBounds.Height, targetBounds.Height);
    }

    private static bool IsAutomationAncestor(
        AutomationElement expectedAncestor,
        AutomationElement element)
    {
        var expectedIdentity = GetAutomationElementIdentity(expectedAncestor);
        for (var current = element; current is not null; current = TryRead(() => current.Parent))
        {
            if (string.Equals(
                    GetAutomationElementIdentity(current),
                    expectedIdentity,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSameAutomationElement(AutomationElement left, AutomationElement right) =>
        string.Equals(
            GetAutomationElementIdentity(left),
            GetAutomationElementIdentity(right),
            StringComparison.Ordinal);

    private static System.Drawing.Point GetBoundsCenter(AutomationElement element)
    {
        var bounds = element.BoundingRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException(
                "The automation element does not expose visible bounds for mouse input.");
        }

        return new System.Drawing.Point(
            bounds.Left + bounds.Width / 2,
            bounds.Top + bounds.Height / 2);
    }

    private static void TryScrollIntoView(AutomationElement element)
    {
        try
        {
            if (element.Patterns.ScrollItem.IsSupported)
            {
                element.Patterns.ScrollItem.Pattern.ScrollIntoView();
            }
        }
        catch (Exception pointerFallbackException) when (!DesktopPointer.IsTerminalFailure(pointerFallbackException))
        {
            // Some visual bridge elements expose no scroll pattern; double-click can still work.
        }
    }

    private static void TryFocus(AutomationElement element)
    {
        try
        {
            element.Focus();
        }
        catch (Exception pointerFallbackException) when (!DesktopPointer.IsTerminalFailure(pointerFallbackException))
        {
            // Focus is best-effort before the mouse gesture.
        }
    }

    private sealed class FlaUiVisualGridRowControl : IGridRowControl
    {
        private readonly AutomationElement _inner;

        public FlaUiVisualGridRowControl(AutomationElement inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<IGridCellControl> Cells =>
            ReadCells().Select(cell => (IGridCellControl)new FlaUiVisualGridCellControl(cell)).ToArray();

        internal AutomationElement[] ReadAutomationCells() => ReadCells();

        private AutomationElement[] ReadCells()
        {
            var rowAutomationId = TryRead(() => _inner.AutomationId);
            if (string.IsNullOrWhiteSpace(rowAutomationId))
            {
                return Array.Empty<AutomationElement>();
            }

            var cellPrefix = $"{rowAutomationId}_Cell";
            return FindAutomationDescendants(_inner)
                .Where(candidate => IsVisualGridCell(candidate, cellPrefix))
                .OrderBy(candidate => ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Cell"))
                .ToArray();
        }
    }

    private sealed class FlaUiVisualGridCellBackedRowControl : IGridRowControl
    {
        private readonly IReadOnlyList<AutomationElement> _cells;

        public FlaUiVisualGridCellBackedRowControl(IReadOnlyList<AutomationElement> cells)
        {
            _cells = cells ?? throw new ArgumentNullException(nameof(cells));
        }

        public IReadOnlyList<IGridCellControl> Cells =>
            _cells.Select(cell => (IGridCellControl)new FlaUiVisualGridCellControl(cell)).ToArray();
    }

    private sealed class FlaUiVisualGridCellControl : IGridCellValueControl
    {
        private readonly AutomationElement _inner;

        public FlaUiVisualGridCellControl(AutomationElement inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Value => ReadVisualGridCellText(_inner) ?? string.Empty;

        public GridCellValueSnapshot ValueSnapshot => new(Value, Value) { IsDisplayOnly = true };
    }
}
