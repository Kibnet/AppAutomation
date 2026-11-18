using System.Diagnostics;
using AppAutomation.Abstractions;
using FlaUI.Core.AutomationElements;

namespace AppAutomation.FlaUI.Automation;

public sealed partial class FlaUiControlResolver
{
    private static bool UsesObjectRowIdentity(GridIndexedRowSelector row) =>
        row.Conditions.Any(static condition => condition.Column.IsObjectKey || condition.Column.UsesObjectRowIdentity);

    private static GridCellValueSnapshot ReadPublishedCellPresentation(
        AutomationElement cell, GridRuntimeColumn column, string gridId)
    {
        if (!cell.Properties.Name.TryGetValue(out var displayText) || displayText is null)
            throw new InvalidOperationException(
                $"Grid '{gridId}' cell '{column.SourceFieldName}' does not expose its configured UI Automation Name display source.");

        return GridCellValueNormalizer.Normalize(gridId,
            new GridCellValueSnapshot(displayText, displayText, column.ValueKind)
            {
                IsDisplayOnly = true,
                HasAuthoritativeDisplayText = true
            }, column);
    }

    private sealed partial class FlaUiVisualGridControl
    {
        private GridRowResolution ReadObjectRowResolution(
            GridIndexedRowSelector row, Stopwatch stopwatch, int timeoutMs)
        {
            var columns = row.Conditions.Select(static condition => condition.Column).ToArray();
            if (columns is not [{ IsObjectKey: true, RowIdentityAutomationProperty: not null }])
                throw new InvalidOperationException(
                    $"Grid '{AutomationId}' object row identity must use its sole configured row metadata key.");

            var identity = new GridRowSetIdentity(columns.Select(static column => column.SourceFieldName).ToArray(),
                columns, columns[0].CellContext.RowPath);
            var key = identity.Read(column => row.Conditions.Single(condition => condition.Column == column).ExpectedText);
            var encodedKey = GridRowSelectionSet.Key(key);
            var budget = UiOperationTimeoutBudget.Start(RemainingGridMilliseconds(stopwatch, timeoutMs), "grid object identity");
            var root = RequireRowSetRoot(budget);
            var publication = GridRowSelectionValue.Read(root.Properties.ItemStatus.ValueOrDefault, identity);
            var description = $"grid='{AutomationId}'; object selector={GridRuntimeResolver.DescribeRowSelector(key)}; current view";
            if (publication?.OrderedView is { } view)
                return view.Contains(encodedKey, StringComparer.Ordinal)
                    ? GridRowResolution.Unique(description)
                    : GridRowResolution.NotFound(description);

            // Reuse the complete row-set traversal rather than treating a single visible peer
            // or a declared identity as proof that this reference occurs once in the current view.
            var positions = ProveRowSetTargets(root, identity,
                new Dictionary<string, GridRowSelector>(StringComparer.Ordinal), budget, out var ambiguous);
            if (ambiguous.TryGetValue(encodedKey, out var matches))
                return GridRowResolution.Ambiguous(matches, description);
            return positions.ContainsKey(encodedKey)
                ? GridRowResolution.Unique(description)
                : GridRowResolution.NotFound(description);
        }

        private void RequireObjectRowInCurrentView(GridIndexedRowSelector row, Stopwatch stopwatch, int timeoutMs)
        {
            if (!UsesObjectRowIdentity(row)) return;
            var resolution = ReadObjectRowResolution(row, stopwatch, timeoutMs);
            if (resolution.State != GridRowResolutionState.Unique)
                throw new InvalidOperationException(
                    $"Grid object row selector matched {resolution.MatchCount} rows; expected exactly one. {resolution.Description}");
        }

        private void RejectObjectIdentityCellFallback(GridIndexedRowSelector row)
        {
            if (UsesObjectRowIdentity(row))
                throw new NotSupportedException(
                    $"Grid '{AutomationId}' does not expose data row peers with the configured object identity metadata; positional cell fallback is not allowed.");
        }

        private NativeFlaUiRow ResolveCurrentObjectRow(GridIndexedRowSelector row, Stopwatch stopwatch, int timeoutMs)
        {
            RequireObjectRowInCurrentView(row, stopwatch, timeoutMs);
            if (TryResolveProvenObjectRow(row) is { } visible) return visible;

            // The full current view has proved this key unique. Locating that key must not
            // infer cross-viewport identity again from recycled UIA RuntimeIds or row geometry.
            _nativeSignatureColumnIndexes = [];
            _nativeSignatureRowProperties = row.Conditions
                .Select(static condition => condition.Column.RowIdentityAutomationProperty!.Value)
                .Distinct().ToArray();
            _prefetchedNativeRows = null;
            _ = LocateProvenUniqueNativeRow(row, stopwatch, timeoutMs, metadataOnly: true);
            var targetPosition = ReadGridScrollPosition(FindGridScrollState());
            RequireObjectRowInCurrentView(row, stopwatch, timeoutMs);
            if (TryResolveProvenObjectRow(row) is { } current) return current;

            // A traversal-based proof can move the viewport itself. Restore its target
            // position, then read the live key again instead of retaining a recycled peer.
            if (TryRestoreGridScrollPosition(FindGridScrollState(), targetPosition, stopwatch, timeoutMs)
                && TryResolveProvenObjectRow(row) is { } restored)
                return restored;
            throw new InvalidOperationException(
                $"Grid '{AutomationId}' object row disappeared while refreshing its current-view identity proof.");
        }

        private NativeFlaUiRow? TryResolveProvenObjectRow(GridIndexedRowSelector row)
        {
            var matches = ReadNativeDataRows(includeCells: false, metadataSelector: row)
                .Where(candidate => NativeRowMatches(candidate, row)).Take(2).ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException(
                    $"Grid '{AutomationId}' exposes multiple rows for the proved object key in the current viewport.");
            return matches.SingleOrDefault();
        }
    }
}
