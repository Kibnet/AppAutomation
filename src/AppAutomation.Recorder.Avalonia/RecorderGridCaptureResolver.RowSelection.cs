using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using static AppAutomation.Recorder.Avalonia.RecorderCaptureControlReader;

namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderGridCaptureResolver
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

    internal RecorderGridRowSetCaptureResult CaptureRowSet(Control grid, IReadOnlyList<object?> selected)
    {
        if (!TryResolveGridHint(grid, out var hint, out var source) || !ReferenceEquals(grid, source)
            || _options.FindGridDefinition(hint) is not { } definition || definition.RowIdentityColumns.Count == 0)
            return new RecorderGridRowSetCaptureResult(null, "Multiple grid row selection requires stable identity columns declared by IdentifyRowsBy(...).");
        object?[] items = [];
        if (selected.Count > 0 && !NativeGridSelectionAccess.TryReadCurrentView(grid, out items))
            return new RecorderGridRowSetCaptureResult(null, $"Grid '{definition.PagePropertyName}' does not expose an authoritative current view for stable selection capture.");

        var columns = definition.RowIdentityColumns;
        var publication = definition.Columns.Any(static column => column.IsObjectKey)
            ? GetObjectRowPublicationProperty(definition) : null;
        var materializedRows = publication is not null && selected.Count > 0
            ? ReadMaterializedRowOwners(grid, selected)
            : null;
        var hasVirtualizedSelection = false;
        var rows = new List<RecordedGridSelectionRow>(selected.Count);
        var selectedByKey = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var item in selected)
        {
            if (item is null)
                return new RecorderGridRowSetCaptureResult(null, "The selected grid row has no readable source item.");
            if (!TryReadGridModelIdentity(hint, item, columns, out var values, out var error))
                return new RecorderGridRowSetCaptureResult(null, error);
            if (publication is not null && materializedRows is not null)
            {
                var hasRowOwner = false;
                foreach (var rowOwner in materializedRows[item])
                {
                    hasRowOwner = true;
                    if (!string.Equals(rowOwner.GetValue(publication), values[0], StringComparison.Ordinal))
                        return new RecorderGridRowSetCaptureResult(null,
                            $"Grid '{definition.PagePropertyName}' selected object row does not publish its current key in an actual row {publication.Name}.");
                }
                hasVirtualizedSelection |= !hasRowOwner;
            }
            if (!selectedByKey.TryAdd(Key(values), item))
                return new RecorderGridRowSetCaptureResult(null, "More than one selected grid row has the same stable key.");
            rows.Add(new RecordedGridSelectionRow(columns.Select((name, index) =>
                new RecordedGridRowCondition(name, values[index])
                {
                    IsObjectKey = definition.FindColumn(name)?.IsObjectKey == true
                }).ToArray()));
        }

        if (hasVirtualizedSelection)
        {
            try
            {
                // No row container to verify: require the same complete publication that FlaUI reads.
                var published = GridRowSelectionValue.ReadObjectKey(AutomationProperties.GetItemStatus(grid), columns[0]);
                if (published is null || published.Selected.Count != selectedByKey.Count
                    || !selectedByKey.Keys.All(published.Selected.ContainsKey))
                    return new RecorderGridRowSetCaptureResult(null,
                        $"Grid '{definition.PagePropertyName}' needs a GridRowSelectionValue publication matching its complete selection to record virtualized object rows.");
            }
            catch (InvalidOperationException error)
            {
                return new RecorderGridRowSetCaptureResult(null, $"Grid '{definition.PagePropertyName}': {error.Message}");
            }
        }

        if (rows.Count > 0)
        {
            var resolved = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item is null)
                    return new RecorderGridRowSetCaptureResult(null, "The grid contains a row without a readable source item.");
                if (!TryReadGridModelIdentity(hint, item, columns, out var values, out var error))
                    return new RecorderGridRowSetCaptureResult(null, error);
                var key = Key(values);
                if (selectedByKey.TryGetValue(key, out var selectedItem)
                    && (!ReferenceEquals(item, selectedItem) && !(item.GetType().IsValueType && item.Equals(selectedItem))
                        || !resolved.Add(key)))
                    return new RecorderGridRowSetCaptureResult(null, $"Grid '{definition.PagePropertyName}' cannot identify every selected row uniquely.");
            }

            if (resolved.Count != rows.Count)
                return new RecorderGridRowSetCaptureResult(null, $"Grid '{definition.PagePropertyName}' cannot identify every selected row uniquely.");
        }

        var descriptor = new RecordedControlDescriptor(
            definition.PagePropertyName, UiControlType.Grid, definition.RuntimeLocatorValue,
            definition.RuntimeLocatorKind, definition.RuntimeFallbackToName, grid.GetType().FullName ?? grid.GetType().Name,
            Warning: null);
        return new RecorderGridRowSetCaptureResult(new RecorderGridRowSetCapture(descriptor, rows), null);

        string Key(IReadOnlyList<string> values) => GridRowSelectionSet.Key(columns.Select((name, index) =>
            new GridRowCondition(name, values[index])));
    }

    private ILookup<object, Control> ReadMaterializedRowOwners(Control grid, IReadOnlyList<object?> selected)
    {
        var selectedItems = new HashSet<object?>(selected, ReferenceEqualityComparer.Instance);
        return EnumerateDescendantControls(grid)
            .Where(control => !ReferenceEquals(control, grid)
                && control.DataContext is { } item && selectedItems.Contains(item)
                && (GridRowObjectKeyPublication.GetIsEnabled(control)
                    || AutomationProperties.GetControlTypeOverride(control) == AutomationControlType.DataItem
                    || ControlAutomationPeer.CreatePeerForElement(control)?.GetAutomationControlType() == AutomationControlType.DataItem)
                && TryResolveGridHint(control, out _, out var ownerGrid) && ReferenceEquals(ownerGrid, grid))
            .ToLookup(static control => control.DataContext!, ReferenceEqualityComparer.Instance);
    }

    internal RecorderGridRowGestureResolution ResolveRowGesture(Control? source)
    {
        if (source is null
            || !TryResolveGridHint(source, out var hint, out var gridSource)
            || _options.FindGridDefinition(hint) is not { } definition)
        {
            return new(false, null, null);
        }

        if (HasMoreSpecificGridInteraction(source, gridSource))
        {
            return new(false, null, null);
        }

        if (definition.RowIdentityColumns.Count == 0)
        {
            return new(true, null,
                $"Grid '{definition.PagePropertyName}' cannot record row selection without a declared stable identity. "
                + "Configure IdentifyRowsBy(...); visual row indexes are not persisted.");
        }

        var usesObjectIdentity = definition.Columns.Any(static column => column.IsObjectKey);
        IReadOnlyList<object?> items;
        if (NativeGridSelectionAccess.TryReadCurrentView(gridSource, out var view)) items = view;
        else if (usesObjectIdentity)
            return new(true, null,
                $"Grid '{definition.PagePropertyName}' requires an authoritative current view for object-key recording.");
        else _ = TryReadItemsSource(gridSource, out items);
        if (!TryResolveGridRow(source, gridSource, items, out var rowIndex, out _,
                referenceOnly: usesObjectIdentity))
        {
            return new(true, null,
                $"Grid '{definition.PagePropertyName}' could not resolve the clicked visual element to a source row.");
        }

        return new(true, new RecorderGridRowGestureContext(source, gridSource, hint, definition, rowIndex, items), null);
    }

    internal static bool HasMoreSpecificGridInteraction(Control source, Control gridSource)
    {
        foreach (var current in EnumerateRelatedControls(source))
        {
            if (ReferenceEquals(current, gridSource))
            {
                return false;
            }

            if (current is Button
                or ToggleButton
                or TextBox
                or ComboBox
                or ListBox
                or NumericUpDown
                or DatePicker
                or TimePicker
                or Calendar
                or Slider
                or MenuItem)
            {
                return true;
            }
        }

        return false;
    }

}

internal sealed record RecorderGridRowSetCapture(
    RecordedControlDescriptor Control,
    IReadOnlyList<RecordedGridSelectionRow> Rows);

internal sealed record RecorderGridRowSetCaptureResult(RecorderGridRowSetCapture? Capture, string? Error);

internal sealed record RecorderGridRowGestureContext(
    Control Source,
    Control GridSource,
    RecorderGridHint Hint,
    GridAutomationDefinition Definition,
    int RowIndex,
    IReadOnlyList<object?> Items);

internal sealed record RecorderGridRowGestureResolution(
    bool Handled,
    RecorderGridRowGestureContext? Context,
    string? Error);
