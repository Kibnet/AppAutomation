using AppAutomation.Abstractions;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderStepFactory
{
    internal Control? FindMultipleSelectionGrid(Control? source)
    {
        if (source is null || !TryResolveGridHint(source, out var hint, out var grid)
            || _options.FindGridDefinition(hint) is null
            || HasMoreSpecificGridInteraction(source, grid)
            || !NativeGridSelectionAccess.SupportsMultiple(grid))
            return null;

        return grid;
    }

    internal static bool IsMultipleSelectionRow(Control source, Control grid) =>
        (NativeGridSelectionAccess.TryReadCurrentView(grid, out var view)
            ? TryResolveGridRow(source, grid, view, out _, out _)
            : TryReadItemsSource(grid, out var sourceItems) && TryResolveGridRow(source, grid, sourceItems, out _, out _));

    internal StepCreationResult CreateGridRowSetStep(Control grid, IReadOnlyList<object?> selected)
    {
        if (!TryResolveGridHint(grid, out var hint, out var source) || !ReferenceEquals(grid, source)
            || _options.FindGridDefinition(hint) is not { } definition || definition.RowIdentityColumns.Count == 0)
            return StepCreationResult.Unsupported("Multiple grid row selection requires stable identity columns declared by IdentifyRowsBy(...).");
        object?[] items = [];
        if (selected.Count > 0 && !NativeGridSelectionAccess.TryReadCurrentView(grid, out items))
            return StepCreationResult.Unsupported($"Grid '{definition.PagePropertyName}' does not expose an authoritative current view for stable selection capture.");

        var columns = definition.RowIdentityColumns;
        var rows = new List<RecordedGridSelectionRow>(selected.Count);
        var selectedByKey = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var item in selected)
        {
            if (item is null)
                return StepCreationResult.Unsupported("The selected grid row has no readable source item.");
            if (!TryReadGridModelIdentity(hint, item, columns, out var values, out var error))
                return StepCreationResult.Unsupported(error);
            if (!selectedByKey.TryAdd(Key(values), item))
                return StepCreationResult.Unsupported("More than one selected grid row has the same stable key.");
            rows.Add(new RecordedGridSelectionRow(columns.Select((name, index) =>
                new RecordedGridRowCondition(name, values[index])).ToArray()));
        }

        if (rows.Count > 0)
        {
            var resolved = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item is null)
                    return StepCreationResult.Unsupported("The grid contains a row without a readable source item.");
                if (!TryReadGridModelIdentity(hint, item, columns, out var values, out var error))
                    return StepCreationResult.Unsupported(error);
                var key = Key(values);
                if (selectedByKey.TryGetValue(key, out var selectedItem)
                    && (!ReferenceEquals(item, selectedItem) && !(item.GetType().IsValueType && item.Equals(selectedItem))
                        || !resolved.Add(key)))
                    return StepCreationResult.Unsupported($"Grid '{definition.PagePropertyName}' cannot identify every selected row uniquely.");
            }

            if (resolved.Count != rows.Count)
                return StepCreationResult.Unsupported($"Grid '{definition.PagePropertyName}' cannot identify every selected row uniquely.");
        }

        var descriptor = new RecordedControlDescriptor(
            definition.PagePropertyName, UiControlType.Grid, definition.RuntimeLocatorValue,
            definition.RuntimeLocatorKind, definition.RuntimeFallbackToName, grid.GetType().FullName ?? grid.GetType().Name,
            Warning: null);
        return CreateStep(grid, new RecordedStep(RecordedActionKind.SelectGridRows, descriptor) { GridSelectedRows = rows });

        string Key(IReadOnlyList<string> values) => GridRowSelectionSet.Key(columns.Select((name, index) =>
            new GridRowCondition(name, values[index])));
    }
}
