using System.Collections;
using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation.GridAutomation;
using AppAutomation.Avalonia.Headless.Session;

namespace AppAutomation.Avalonia.Headless.Automation;

public sealed partial class HeadlessControlResolver
{
    private sealed partial class HeadlessVisualGridControl
    {
        public IReadOnlyList<GridRowSelector> ReadSelectedRows(GridRowSetIdentity identity, UiOperationTimeoutBudget budget) =>
            HeadlessRuntime.Dispatch(() =>
            {
                _ = budget.Remaining;
                var selected = RequireSelection().Cast<object>().ToArray();
                RequireCurrentObjectSelectionItems(selected, identity, budget);
                return selected.Select(item => ReadSelectedKey(item, identity)).ToArray();
            });

        public void SetSelectedRows(GridRowSetIdentity identity, IReadOnlyDictionary<string, GridRowSelector> expected,
            UiOperationTimeoutBudget budget)
        {
            HeadlessRuntime.Dispatch(() =>
            {
                _ = budget.Remaining;
                var objectIdentity = identity.RuntimeColumns.Any(static column => column.IsObjectKey || column.UsesObjectRowIdentity);
                var selected = WritableSelection().Cast<object>().ToArray();
                RequireCurrentObjectSelectionItems(selected, identity, budget);
                var current = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var item in selected)
                {
                    if (!current.TryAdd(GridRowSelectionSet.Key(ReadSelectedKey(item, identity)), item))
                        throw new InvalidOperationException($"Grid '{_gridDescription}' exposes duplicate selected row identities.");
                }

                var targets = new Dictionary<string, object>(StringComparer.Ordinal);
                if (expected.Count > 0)
                {
                    var (items, authoritative) = HeadlessGridRuntimeAccess.ReadCurrentViewItems(Inner.Control);
                    if (!authoritative)
                        throw new NotSupportedException($"Grid '{_gridDescription}' does not expose an authoritative current view.");

                    foreach (var item in items.OfType<object>())
                    {
                        _ = budget.Remaining;
                        var key = GridRowSelectionSet.Key(ReadSelectedKey(item, identity));
                        if (expected.ContainsKey(key) && !targets.TryAdd(key, item))
                            throw new InvalidOperationException($"Grid '{_gridDescription}' has more than one row for a requested stable key.");
                    }

                    foreach (var (key, row) in expected)
                    {
                        if (!targets.ContainsKey(key))
                            throw new InvalidOperationException($"Grid '{_gridDescription}' does not contain {GridRuntimeResolver.DescribeRowSelector(row)} in its current view.");
                    }
                }

                foreach (var (key, item) in current)
                {
                    if (targets.TryGetValue(key, out var target) && ReferenceEquals(item, target)) continue;
                    _ = budget.Remaining;
                    var selection = WritableSelection();
                    RequireCurrentObjectSelectionItems(selection.Cast<object>().Concat(targets.Values), identity, budget);
                    if (objectIdentity)
                    {
                        var index = Enumerable.Range(0, selection.Count).FirstOrDefault(
                            i => ReferenceEquals(selection[i], item), -1);
                        if (index >= 0) selection.RemoveAt(index);
                    }
                    else selection.Remove(item);
                }
                foreach (var (key, item) in targets)
                {
                    _ = budget.Remaining;
                    var selection = WritableSelection();
                    if (objectIdentity
                        ? selection.Cast<object>().Any(candidate => ReferenceEquals(candidate, item))
                        : current.TryGetValue(key, out var existing) && ReferenceEquals(existing, item)) continue;
                    RequireCurrentObjectSelectionItems(selection.Cast<object>().Concat(targets.Values), identity, budget);
                    selection.Add(item);
                }
                RequireCurrentObjectSelectionItems(WritableSelection().Cast<object>(), identity, budget);
            });
        }

        private void RequireCurrentObjectSelectionItems(IEnumerable<object> selected,
            GridRowSetIdentity identity, UiOperationTimeoutBudget budget)
        {
            if (!identity.RuntimeColumns.Any(static column => column.IsObjectKey || column.UsesObjectRowIdentity)) return;
            _ = budget.Remaining;
            var view = HeadlessGridRuntimeAccess.ReadCurrentViewItems(Inner.Control);
            if (!view.IsAuthoritative)
                throw new NotSupportedException($"Grid '{_gridDescription}' does not expose an authoritative current view for object row selection.");
            var occurrences = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            foreach (var candidate in view.Items)
            {
                _ = budget.Remaining;
                if (candidate is not null)
                    occurrences[candidate] = occurrences.GetValueOrDefault(candidate) + 1;
            }
            foreach (var item in selected)
            {
                _ = budget.Remaining;
                if (occurrences.GetValueOrDefault(item) != 1)
                    throw new InvalidOperationException($"Grid '{_gridDescription}' does not expose a selected or requested object row exactly once in its current view.");
            }
        }

        private IEnumerable RequireSelection() =>
            NativeGridSelectionAccess.SupportsMultiple(Inner.Control)
                && NativeGridSelectionAccess.ReadItems(Inner.Control) is { } selection
                ? selection
                : throw new NotSupportedException($"Grid '{_gridDescription}' does not expose a complete multiple selection.");

        private IList WritableSelection() => RequireSelection() is IList { IsReadOnly: false, IsFixedSize: false } items
            ? items
            : throw new NotSupportedException($"Grid '{_gridDescription}' does not expose writable multiple selection.");

        private GridRowSelector ReadSelectedKey(object item, GridRowSetIdentity identity) => identity.Read(column =>
        {
            if (item is null) return null;
            if (column.IsObjectKey) return GridRowObjectKey.Get(item);
            var row = item;
            if (!GridPropertyValueReader.TryReadPath(row, column.SourceFieldName, out var raw))
            {
                if (!GridPropertyValueReader.TryReadPath(item, identity.RowPath, out var nested)
                    || nested is null || !GridPropertyValueReader.TryReadPath(nested, column.SourceFieldName, out raw))
                    return null;
                row = nested;
            }
            return GridCellValueNormalizer.Normalize(_gridDescription, new GridCellValueSnapshot(
                GridCellDisplayFormatter.Format(raw, cultureName: column.CultureName), raw, column.ValueKind)
                { ValueSource = row }, column).DisplayText;
        });
    }
}
