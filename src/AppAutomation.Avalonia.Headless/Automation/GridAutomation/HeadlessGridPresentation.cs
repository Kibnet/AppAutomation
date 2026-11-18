using System.Diagnostics;
using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation.GridAutomation;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AppAutomation.Avalonia.Headless.Automation;

public sealed partial class HeadlessControlResolver
{
    private sealed partial class HeadlessVisualGridControl
    {
        private static bool UsesObjectIdentity(GridIndexedRowSelector selector) =>
            selector.Conditions.Any(static condition => condition.Column.IsObjectKey || condition.Column.UsesObjectRowIdentity);

        private static GridRuntimeColumn WithRowIdentity(GridRuntimeColumn column, GridIndexedRowSelector selector) =>
            UsesObjectIdentity(selector) ? column with { UsesObjectRowIdentity = true } : column;

        private object?[] ReadObjectIdentityView()
        {
            Inner.Control.Dispatcher.RunJobs();
            TopLevel.GetTopLevel(Inner.Control)?.UpdateLayout();
            var view = HeadlessGridRuntimeAccess.ReadCurrentViewItems(Inner.Control);
            return view.IsAuthoritative ? view.Items : throw new NotSupportedException(
                $"Grid '{_gridDescription}' must expose an authoritative current view for object row identities.");
        }

        private void VerifyObjectRowPublication(IndexedHeadlessRow row, IReadOnlyList<GridRuntimeColumn> columns)
        {
            var properties = columns.Select(static column => column.ObjectRowKeyAutomationProperty
                    ?? (column.IsObjectKey ? column.RowIdentityAutomationProperty : null))
                .Where(static property => property.HasValue).Distinct().ToArray();
            if (properties.Length != 1)
                throw new InvalidOperationException($"Grid '{_gridDescription}' does not expose one configured object-key publication property.");
            var target = FindOpenRowTarget(row, [columns[0].CellContext.RowPath], referenceOnly: true);
            if (target is null || row.Item is null) return;
            var published = properties[0] switch
            {
                GridRowAutomationProperty.AutomationId => AutomationProperties.GetAutomationId(target),
                GridRowAutomationProperty.HelpText => AutomationProperties.GetHelpText(target),
                GridRowAutomationProperty.Name => AutomationProperties.GetName(target),
                GridRowAutomationProperty.ItemStatus => AutomationProperties.GetItemStatus(target),
                _ => throw new InvalidOperationException($"Grid '{_gridDescription}' configures an unsupported object-key publication property.")
            };
            if (!string.Equals(published, GridRowObjectKey.Get(row.Item), StringComparison.Ordinal))
                throw new InvalidOperationException($"Grid '{_gridDescription}' publishes an object row key that does not match its current row reference.");
        }

        private void RequireCurrentObjectRow(IndexedHeadlessRow row, bool required)
        {
            if (!required) return;
            var view = HeadlessGridRuntimeAccess.ReadCurrentViewItems(Inner.Control);
            if (!view.IsAuthoritative || row.Item is null
                || view.Items.Count(item => ReferenceEquals(item, row.Item)) != 1)
                throw new InvalidOperationException($"Grid '{_gridDescription}' no longer exposes the unique object row in its current view.");
        }

        private void RequireObjectEditTarget(IndexedHeadlessRow row, GridRuntimeColumn column, Control editor)
        {
            if (!column.UsesObjectRowIdentity) return;
            RequireCurrentObjectRow(row, required: true);
            var cell = HeadlessGridRuntimeAccess.FindCell(Inner.Control, row.Item!, column, _gridDescription);
            if (cell is null || !(ReferenceEquals(editor, cell) || editor.GetVisualAncestors().Contains(cell)))
                throw new InvalidOperationException($"Grid '{_gridDescription}' active editor no longer belongs to the requested object row and column.");
        }

        private GridCellValueSnapshot ReadPublishedCell(GridIndexedRowSelector selector, IndexedHeadlessRow match,
            GridRuntimeColumn column, Stopwatch operation, int timeoutMs)
        {
            var cell = FindCatalogVisualCell(match, column, Remaining())
                ?? throw new InvalidOperationException(
                    $"Grid '{_gridDescription}' does not expose the published display cell '{column.SourceFieldName}'.");
            // Revealing a cell may replace its container or change the current view. Resolve the same key again.
            match = ResolveUniqueRow(selector, Remaining());
            return HeadlessRuntime.Dispatch(() =>
            {
                _ = Remaining();
                RequireCurrentObjectRow(match, column.UsesObjectRowIdentity);
                var actual = match.Item is not null
                    ? HeadlessGridRuntimeAccess.FindCell(Inner.Control, match.Item, column, _gridDescription)
                    : cell.Control;
                if (actual is null || !actual.IsEffectivelyVisible)
                    throw new InvalidOperationException($"Grid '{_gridDescription}' lost its published display cell '{column.SourceFieldName}'.");

                string text;
                if (actual.IsSet(AutomationProperties.NameProperty))
                    text = AutomationProperties.GetName(actual) ?? string.Empty;
                else
                {
                    var peer = ControlAutomationPeer.CreatePeerForElement(actual)
                        ?? throw new NotSupportedException(
                            $"Grid '{_gridDescription}' cell '{column.SourceFieldName}' exposes no readable automation Name.");
                    text = peer.GetName();
                }

                var raw = match.Item is null ? null : ReadPropertyPath(match.Item, column.SourceFieldName);
                return GridCellValueNormalizer.Normalize(_gridDescription,
                    new GridCellValueSnapshot(text, raw, column.ValueKind)
                    {
                        ValueSource = match.Item,
                        IsDisplayOnly = match.Item is null,
                        HasAuthoritativeDisplayText = true
                    }, column);
            });

            int Remaining() => timeoutMs - (int)operation.ElapsedMilliseconds is > 0 and var remaining
                ? remaining : throw new TimeoutException($"Grid '{_gridDescription}' published cell read exceeded its timeout.");
        }
    }
}
