using System.Collections;
using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using static AppAutomation.Recorder.Avalonia.RecorderCaptureControlReader;
using static AppAutomation.Recorder.Avalonia.RecorderSemanticValueModel;

namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderGridCaptureResolver
{
    private static readonly string[] GridRowContextPropertyNames = ["Row", "RowData", "DataItem", "Item"];
    private static readonly string[] GridColumnContextPropertyNames = ["FieldName", "ColumnName", "PropertyName"];
    private static readonly string[] NestedGridColumnContextPropertyNames = ["FieldName", "ColumnName", "PropertyName", "Name"];
    private static readonly string[] GridCellValueContextPropertyNames = ["Value", "CellValue", "DisplayValue"];

    private readonly Dictionary<Control, (
        RecorderGridHint Hint, GridAutomationDefinition Definition, IReadOnlyList<RecorderNativeGridColumn> Columns)>
        _nativeGridDefinitions = new(ReferenceEqualityComparer.Instance);

    private readonly AppAutomationRecorderOptions _options;
    private readonly RecorderSelectorResolver _selectorResolver;
    private readonly RecorderCaptureControlReader _controlReader;
    private readonly Func<Control?>? _validationRootProvider;

    internal RecorderGridCaptureResolver(AppAutomationRecorderOptions options,
        RecorderSelectorResolver selectorResolver, RecorderCaptureControlReader controlReader,
        Func<Control?>? validationRootProvider)
    {
        _options = options;
        _selectorResolver = selectorResolver;
        _controlReader = controlReader;
        _validationRootProvider = validationRootProvider;
    }

    internal bool IsConfiguredGridRoot(Control source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _options.GridAutomation.Any(definition =>
            TryGetLocator(source, definition.CaptureLocatorKind, out var locatorValue)
            && string.Equals(definition.CaptureLocatorValue, locatorValue, StringComparison.Ordinal));
    }

    internal IReadOnlyList<Control> ReadMaterializedGridCellControls(Control source) =>
        IsConfiguredGridRoot(source)
            ? GridCellMetadataExtractor.ReadMaterializedCellControls(source)
            : Array.Empty<Control>();

    internal IReadOnlyList<Control> ReadConfiguredGridRoots()
    {
        var roots = new List<Control>();
        var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        foreach (var definition in _options.GridAutomation)
        {
            if (_controlReader.TryFindControl(definition.CaptureLocatorValue, definition.CaptureLocatorKind, out var grid)
                && visited.Add(grid))
            {
                roots.Add(grid);
            }
        }
        return roots;
    }

    internal bool TryResolveGridHint(Control source, out RecorderGridHint hint, out Control gridSource)
    {
        foreach (var current in EnumerateRelatedControls(source))
        {
            if (current is Window)
            {
                continue;
            }

            foreach (var candidate in _options.EnumerateGridHints())
            {
                if (TryGetLocator(current, candidate.SourceLocatorKind, out var locatorValue)
                    && string.Equals(candidate.SourceLocatorValue.Trim(), locatorValue, StringComparison.Ordinal))
                {
                    hint = candidate;
                    gridSource = current;
                    return true;
                }
            }

            if (GridCellMetadataExtractor.IsNativeDataGrid(current)
                && TryCreateNativeGridDefinition(current, out hint, out _))
            {
                gridSource = current;
                return true;
            }
        }

        hint = null!;
        gridSource = null!;
        return false;
    }

    internal GridComboSelectionContextResolution TryResolveGridComboSelectionContext(Control source)
    {
        var matchingGrids = FindRelatedGridHints(source).Take(2).ToArray();
        if (matchingGrids.Length == 0)
        {
            return new GridComboSelectionContextResolution(false, null, null);
        }

        if (matchingGrids.Length > 1)
        {
            return new GridComboSelectionContextResolution(
                true,
                null,
                "Selection editor belongs to multiple configured grids; the logical grid locator is ambiguous.");
        }

        var (hint, gridSource) = matchingGrids[0];
        if (!TryReadItemsSource(gridSource, out var items) || items.Count == 0)
        {
            return new GridComboSelectionContextResolution(
                true,
                null,
                $"Grid '{hint.TargetLocatorValue}' does not expose a non-empty ItemsSource for selection capture.");
        }

        var definition = FindGridDefinition(hint);
        if (definition is not null)
        {
            if (!GridCellMetadataExtractor.TryExtract(
                    source,
                    gridSource,
                    definition,
                    items,
                    ExtractTextValue,
                    out var metadata,
                    out var metadataError))
            {
                return new GridComboSelectionContextResolution(true, null, metadataError);
            }

            var logicalColumnName = definition.FindColumnBySourceField(metadata.SourceFieldName)?.LogicalName
                ?? metadata.SourceFieldName;
            var configuredColumnIndex = FindColumnIndex(hint.ColumnPropertyNames, logicalColumnName);
            return new GridComboSelectionContextResolution(
                true,
                new GridComboSelectionContext(
                    source,
                    gridSource,
                    metadata.CellOwner,
                    metadata.EditorRoot,
                    hint,
                    metadata.RowIndex,
                    configuredColumnIndex,
                    logicalColumnName,
                    metadata.Row),
                null);
        }

        if (source is not (ComboBox or ListBox))
        {
            return new GridComboSelectionContextResolution(false, null, null);
        }

        var currentCellText = TryReadGridCellContextText(source, out var contextText)
            ? contextText
            : null;
        if (!TryResolveGridCell(
                source,
                gridSource,
                hint,
                items,
                currentCellText,
                out var rowIndex,
                out var columnIndex,
                out _))
        {
            var missingParts = new List<string>(2);
            if (!TryResolveGridRow(source, gridSource, items, out _, out _))
            {
                missingParts.Add("row");
            }

            if (!TryResolveGridColumnIndex(
                    source,
                    gridSource,
                    hint,
                    out _)
                && string.IsNullOrWhiteSpace(currentCellText))
            {
                missingParts.Add("column");
            }

            var missing = missingParts.Count == 0
                ? "row or column"
                : string.Join(" and ", missingParts);
            return new GridComboSelectionContextResolution(
                true,
                null,
                $"Grid '{hint.TargetLocatorValue}' selection capture could not resolve an unambiguous {missing} context.");
        }

        return new GridComboSelectionContextResolution(
            true,
            new GridComboSelectionContext(source, gridSource, source, source, hint, rowIndex, columnIndex),
            null);
    }

    internal IEnumerable<(RecorderGridHint Hint, Control GridSource)> FindRelatedGridHints(Control source)
    {
        var matchedHints = new HashSet<RecorderGridHint>(ReferenceEqualityComparer.Instance);
        var relatedControls = EnumerateRelatedControls(source).ToArray();
        foreach (var current in relatedControls)
        {
            if (current is Window)
            {
                continue;
            }

            foreach (var hint in _options.EnumerateGridHints())
            {
                if (matchedHints.Contains(hint)
                    || !TryGetLocator(current, hint.SourceLocatorKind, out var locatorValue)
                    || !string.Equals(hint.SourceLocatorValue.Trim(), locatorValue, StringComparison.Ordinal))
                {
                    continue;
                }

                matchedHints.Add(hint);
                yield return (hint, current);
            }
        }

        if (matchedHints.Count > 0)
        {
            yield break;
        }

        if (TryResolveGridHint(source, out var nativeHint, out var nativeGrid)
            && FindGridDefinition(nativeHint) is not null)
        {
            yield return (nativeHint, nativeGrid);
            yield break;
        }

        foreach (var hint in _options.EnumerateGridHints())
        {
            if (!_controlReader.TryFindControl(
                    hint.SourceLocatorValue,
                    hint.SourceLocatorKind,
                    out var gridSource)
                || !TryReadItemsSource(gridSource, out var items)
                || !TryResolveGridRow(source, gridSource, items, out _, out _))
            {
                continue;
            }

            yield return (hint, gridSource);
        }
    }

    internal RecorderGridSearchPickerContext? ResolveGridSearchPickerContext(
        Control searchInput,
        RecorderGridSearchPickerHint hint)
    {
        if (!TryResolveGridSearchPickerGridSource(hint, out var gridHint, out var gridSource))
        {
            return null;
        }

        var usesObjectKey = FindGridDefinition(gridHint)?.Columns.Any(static column => column.IsObjectKey) == true;
        IReadOnlyList<object?> items;
        if (usesObjectKey)
        {
            if (!NativeGridSelectionAccess.TryReadCurrentView(gridSource, out var currentView))
                return null;
            items = currentView;
        }
        else
        {
            TryReadItemsSource(gridSource, out items);
        }

        object? rowItem;
        if (items.Count == 0
            || !TryResolveGridRow(searchInput, gridSource, items, out var rowIndex, out rowItem, referenceOnly: usesObjectKey))
        {
            if (usesObjectKey || !TryResolveGridRowIndexFromAutomationId(searchInput, out rowIndex))
                return null;
            rowItem = items.ElementAtOrDefault(rowIndex);
        }

        int columnIndex;
        if (hint.ColumnIndex is >= 0)
        {
            columnIndex = hint.ColumnIndex.Value;
        }
        else if (!string.IsNullOrWhiteSpace(hint.ColumnName))
        {
            columnIndex = FindColumnIndex(gridHint.ColumnPropertyNames, hint.ColumnName);
        }
        else if (!TryResolveGridColumnIndex(searchInput, gridSource, gridHint, out columnIndex)
            && !TryResolveGridColumnIndexFromAutomationId(searchInput, out columnIndex))
        {
            return null;
        }

        return columnIndex < 0 ? null : new(gridHint, gridSource, rowIndex, columnIndex, rowItem, usesObjectKey);
    }

    private bool TryResolveGridSearchPickerGridSource(
        RecorderGridSearchPickerHint hint,
        out RecorderGridHint gridHint,
        out Control gridSource)
    {
        foreach (var candidate in _options.EnumerateGridHints())
        {
            if (candidate.TargetLocatorKind == hint.TargetGridLocatorKind
                && string.Equals(candidate.TargetLocatorValue.Trim(), hint.TargetGridLocatorValue.Trim(), StringComparison.Ordinal)
                && _controlReader.TryFindControl(candidate.SourceLocatorValue, candidate.SourceLocatorKind, out gridSource))
            {
                gridHint = candidate;
                return true;
            }
        }

        gridHint = null!;
        gridSource = null!;
        return false;
    }

    internal static int FindColumnIndex(IReadOnlyList<string> columnNames, string columnName)
    {
        for (var i = 0; i < columnNames.Count; i++)
        {
            if (string.Equals(columnNames[i], columnName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    internal bool TryResolveGridRowIndex(Control source, RecorderGridActionHint hint, out int rowIndex)
    {
        if (TryResolveGridHint(source, out var objectHint, out var objectGrid)
            && FindGridDefinition(objectHint)?.Columns.Any(static column => column.IsObjectKey) == true)
        {
            rowIndex = -1;
            return NativeGridSelectionAccess.TryReadCurrentView(objectGrid, out var currentView)
                && TryResolveGridRow(source, objectGrid, currentView, out rowIndex, out _, referenceOnly: true);
        }
        if (hint.RowIndex is >= 0)
        {
            rowIndex = hint.RowIndex.Value;
            return true;
        }

        if (TryResolveGridHint(source, out _, out var gridSource)
            && TryReadItemsSource(gridSource, out var items)
            && TryResolveGridRow(source, gridSource, items, out rowIndex, out _))
        {
            return true;
        }

        return TryResolveGridRowIndexFromAutomationId(source, out rowIndex);
    }

    internal bool TryResolveGridCellIndexes(
        Control source,
        RecorderGridActionHint hint,
        out int rowIndex,
        out int columnIndex)
    {
        rowIndex = hint.RowIndex is >= 0 ? hint.RowIndex.Value : -1;
        columnIndex = hint.ColumnIndex is >= 0 ? hint.ColumnIndex.Value : -1;
        if (TryResolveGridHint(source, out var objectHint, out var objectGrid)
            && FindGridDefinition(objectHint)?.Columns.Any(static column => column.IsObjectKey) == true)
            return NativeGridSelectionAccess.TryReadCurrentView(objectGrid, out var currentView)
                && TryResolveGridCell(source, objectGrid, objectHint, currentView, out rowIndex, out columnIndex, out _);
        if (rowIndex >= 0 && columnIndex >= 0)
        {
            return true;
        }

        if (TryResolveGridHint(source, out var gridHint, out var gridSource)
            && TryReadItemsSource(gridSource, out var items)
            && TryResolveGridCell(source, gridSource, gridHint, items, out var resolvedRowIndex, out var resolvedColumnIndex, out _))
        {
            if (rowIndex < 0)
            {
                rowIndex = resolvedRowIndex;
            }

            if (columnIndex < 0)
            {
                columnIndex = resolvedColumnIndex;
            }

            return rowIndex >= 0 && columnIndex >= 0;
        }

        TryResolveGridRowIndexFromAutomationId(source, out var parsedRowIndex);
        TryResolveGridColumnIndexFromAutomationId(source, out var parsedColumnIndex);
        if (rowIndex < 0)
        {
            rowIndex = parsedRowIndex;
        }

        if (columnIndex < 0)
        {
            columnIndex = parsedColumnIndex;
        }

        return rowIndex >= 0 && columnIndex >= 0;
    }

    internal bool TryResolveGridCell(
        Control source,
        Control gridSource,
        RecorderGridHint hint,
        IReadOnlyList<object?> items,
        out int rowIndex,
        out int columnIndex,
        out string cellValue)
    {
        return TryResolveGridCell(
            source,
            gridSource,
            hint,
            items,
            observedTextOverride: null,
            out rowIndex,
            out columnIndex,
            out cellValue);
    }

    internal bool TryResolveGridCell(
        Control source,
        Control gridSource,
        RecorderGridHint hint,
        IReadOnlyList<object?> items,
        string? observedTextOverride,
        out int rowIndex,
        out int columnIndex,
        out string cellValue)
    {
        rowIndex = -1;
        columnIndex = -1;
        cellValue = string.Empty;

        if (hint.ColumnPropertyNames.Count == 0)
        {
            return false;
        }

        var definition = FindGridDefinition(hint);
        if (GridCellMetadataExtractor.TryExtract(
                source,
                gridSource,
                definition,
                items,
                ExtractTextValue,
                out var metadata,
                out _))
        {
            var logicalColumnName = definition?
                    .FindColumnBySourceField(metadata.SourceFieldName)?
                    .LogicalName
                ?? metadata.SourceFieldName;
            columnIndex = FindColumnIndex(hint.ColumnPropertyNames, logicalColumnName);
            if (columnIndex >= 0)
            {
                rowIndex = metadata.RowIndex;
                if (definition?.FindColumn(logicalColumnName)?.ReadsDisplayedText == true)
                {
                    cellValue = metadata.DisplayText!;
                    return true;
                }
                if (TryReadDisplayedGridCellValue(
                        gridSource,
                        metadata.Row,
                        rowIndex,
                        columnIndex,
                        logicalColumnName,
                        out cellValue))
                {
                    return true;
                }

                var column = definition?.FindColumn(logicalColumnName);
                if (TryFormatGridValue(metadata.RawValue, column, out cellValue))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(metadata.DisplayText))
                {
                    cellValue = metadata.DisplayText;
                    return true;
                }
            }

        }

        if (definition?.Columns.Any(static column => column.IsObjectKey || column.ReadsDisplayedText) == true)
            return false;

        if (!TryResolveGridRow(source, gridSource, items, out rowIndex, out var rowItem))
        {
            return false;
        }

        var observedText = string.IsNullOrWhiteSpace(observedTextOverride)
            ? ExtractTextValue(source)?.Trim()
            : observedTextOverride.Trim();
        if (TryResolveGridColumnIndex(
            source,
            gridSource,
            hint,
            out columnIndex))
        {
            if (TryReadDisplayedGridCellValue(
                    gridSource,
                    rowItem,
                    rowIndex,
                    columnIndex,
                    hint.ColumnPropertyNames[columnIndex],
                    out cellValue))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(observedText))
            {
                cellValue = observedText;
                return true;
            }

            return false;
        }

        if (string.IsNullOrWhiteSpace(observedText))
        {
            return false;
        }

        var matchedColumnIndex = -1;
        var matchedValue = string.Empty;
        var matchedColumnCount = 0;
        for (var candidateColumnIndex = 0; candidateColumnIndex < hint.ColumnPropertyNames.Count; candidateColumnIndex++)
        {
            if (!TryReadDisplayedGridCellValue(
                    gridSource,
                    rowItem,
                    rowIndex,
                    candidateColumnIndex,
                    out var candidateValue)
                || !string.Equals(candidateValue, observedText, StringComparison.Ordinal))
            {
                continue;
            }

            matchedColumnIndex = candidateColumnIndex;
            matchedValue = candidateValue;
            matchedColumnCount++;
        }

        if (matchedColumnCount == 1)
        {
            columnIndex = matchedColumnIndex;
            cellValue = matchedValue;
            return true;
        }

        return false;
    }

    internal static bool TryResolveGridRow(
        Control source,
        Control gridSource,
        IReadOnlyList<object?> items,
        out int rowIndex,
        out object item,
        bool referenceOnly = false)
    {
        foreach (var current in EnumerateRelatedControls(source))
        {
            if (ReferenceEquals(current, gridSource))
            {
                continue;
            }

            var dataContext = current.DataContext;
            if (dataContext is null)
            {
                continue;
            }

            if (TryFindItemIndex(items, dataContext, out rowIndex, out item, referenceOnly))
            {
                return true;
            }

            foreach (var propertyName in GridRowContextPropertyNames)
            {
                if (GridPropertyValueReader.TryReadProperty(dataContext, propertyName, out var rowCandidate)
                    && rowCandidate is not null
                    && TryFindItemIndex(items, rowCandidate, out rowIndex, out item, referenceOnly))
                {
                    return true;
                }
            }

        }

        rowIndex = -1;
        item = null!;
        return false;
    }

    internal bool TryResolveGridColumnIndex(
        Control source,
        Control gridSource,
        RecorderGridHint hint,
        out int columnIndex)
    {
        var columnNames = hint.ColumnPropertyNames;
        foreach (var current in EnumerateRelatedControls(source))
        {
            if (ReferenceEquals(current, gridSource))
            {
                continue;
            }

            if (TryParseVisualGridIndex(AutomationProperties.GetAutomationId(current), "_Cell", out var candidate)
                && candidate >= 0
                && candidate < columnNames.Count)
            {
                columnIndex = candidate;
                return true;
            }

            if (TryReadGridColumnContextName(current.DataContext, out var contextColumnName)
                && TryMatchGridColumnName(hint, contextColumnName, out columnIndex))
            {
                return true;
            }
        }

        columnIndex = -1;
        return false;
    }

    internal GridAutomationDefinition? FindGridDefinition(RecorderGridHint hint)
    {
        var configured = _options.FindGridDefinition(hint);
        if (configured is not null)
        {
            return configured;
        }

        foreach (var candidate in _nativeGridDefinitions.Values)
        {
            if (ReferenceEquals(candidate.Hint, hint))
            {
                return candidate.Definition;
            }
        }

        return null;
    }

    internal bool TryCreateNativeGridDefinition(
        Control grid,
        out RecorderGridHint hint,
        out GridAutomationDefinition definition)
    {
        var nativeColumns = GridCellMetadataExtractor.ReadNativeColumns(grid);
        if (_nativeGridDefinitions.TryGetValue(grid, out var cached) && cached.Columns.SequenceEqual(nativeColumns))
        {
            hint = cached.Hint;
            definition = cached.Definition;
            return true;
        }

        var columns = nativeColumns
            .Where(static metadata => !string.IsNullOrWhiteSpace(metadata.SourceFieldName))
            .Select(static metadata => CreateNativeColumnDefinition(metadata))
            .ToArray();
        if (columns.Length == 0
            || _selectorResolver.Resolve(grid, UiControlType.Grid) is not { Success: true, Control: { } locator })
        {
            _nativeGridDefinitions.Remove(grid);
            hint = null!;
            definition = null!;
            return false;
        }

        definition = GridAutomationDefinition.ByLocators(
                locator.ProposedPropertyName,
                locator.LocatorValue,
                locator.LocatorKind,
                locator.LocatorValue,
                locator.LocatorKind,
                locator.FallbackToName)
            .WithColumns(columns);
        hint = new RecorderGridHint(
            locator.LocatorValue,
            locator.LocatorValue,
            columns.Select(static column => column.LogicalName).ToArray(),
            locator.LocatorKind,
            locator.LocatorKind,
            locator.FallbackToName);
        _nativeGridDefinitions[grid] = (hint, definition, nativeColumns);
        return true;
    }

    internal static GridColumnDefinition CreateNativeColumnDefinition(RecorderNativeGridColumn metadata)
    {
        var column = GridColumnDefinition.Map(metadata.LogicalName)
            .FromField(metadata.SourceFieldName);
        if (metadata.EditorKind is { } editorKind)
        {
            column = column.EditWith(editorKind);
        }

        if (metadata.ValueKind is { } valueKind)
        {
            column = column.AsValue(valueKind);
        }

        return metadata.IsStableIdentityCandidate
            ? column.AsStableIdentityCandidate()
            : column;
    }

    internal static bool TryReadGridColumnContextName(object? dataContext, out string columnName)
    {
        columnName = string.Empty;
        if (dataContext is null)
        {
            return false;
        }

        foreach (var propertyName in GridColumnContextPropertyNames)
        {
            if (GridPropertyValueReader.TryReadProperty(dataContext, propertyName, out var directValue)
                && directValue is not null
                && directValue is not Control
                && (directValue is string || directValue is not IEnumerable)
                && directValue.ToString() is { } directText
                && !string.IsNullOrWhiteSpace(directText))
            {
                columnName = directText.Trim();
                return true;
            }
        }

        if (!GridPropertyValueReader.TryReadProperty(dataContext, "Column", out var column) || column is null)
        {
            return false;
        }

        foreach (var propertyName in NestedGridColumnContextPropertyNames)
        {
            if (GridPropertyValueReader.TryReadProperty(column, propertyName, out var nestedValue)
                && nestedValue?.ToString() is { } nestedText
                && !string.IsNullOrWhiteSpace(nestedText))
            {
                columnName = nestedText.Trim();
                return true;
            }
        }

        return false;
    }

    internal bool TryMatchGridColumnName(
        RecorderGridHint hint,
        string contextColumnName,
        out int columnIndex)
    {
        var columnNames = hint.ColumnPropertyNames;
        var definition = FindGridDefinition(hint);
        var logicalColumnName = definition?.FindColumnBySourceField(contextColumnName)?.LogicalName
            ?? contextColumnName;
        columnIndex = FindColumnIndex(columnNames, logicalColumnName);
        if (columnIndex >= 0)
        {
            return true;
        }

        var suffixMatches = columnNames
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .Where(candidate =>
                contextColumnName.EndsWith(candidate.Name, StringComparison.OrdinalIgnoreCase)
                || candidate.Name.EndsWith(contextColumnName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (suffixMatches.Length != 1)
        {
            return false;
        }

        columnIndex = suffixMatches[0].Index;
        return true;
    }

    internal static bool TryReadGridCellContextText(Control source, out string value)
    {
        foreach (var current in EnumerateRelatedControls(source))
        {
            var dataContext = current.DataContext;
            if (dataContext is null)
            {
                continue;
            }

            foreach (var propertyName in GridCellValueContextPropertyNames)
            {
                if (!GridPropertyValueReader.TryReadProperty(dataContext, propertyName, out var cellValue))
                {
                    continue;
                }

                var displayText = ExtractSelectionText(cellValue)?.Trim();
                if (!string.IsNullOrWhiteSpace(displayText))
                {
                    value = displayText;
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }

    internal static bool TryParseVisualGridIndex(string? automationId, string marker, out int index)
    {
        index = -1;
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return false;
        }

        var markerIndex = automationId.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var digitStart = markerIndex + marker.Length;
        var digitEnd = digitStart;
        while (digitEnd < automationId.Length && char.IsDigit(automationId[digitEnd]))
        {
            digitEnd++;
        }

        return digitEnd > digitStart
            && int.TryParse(
                automationId[digitStart..digitEnd],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out index);
    }

    internal static bool TryResolveGridRowIndexFromAutomationId(Control source, out int rowIndex)
    {
        foreach (var current in EnumerateRelatedControls(source))
        {
            if (TryParseVisualGridIndex(AutomationProperties.GetAutomationId(current), "_Row", out rowIndex))
            {
                return true;
            }
        }

        rowIndex = -1;
        return false;
    }

    internal static bool TryResolveGridColumnIndexFromAutomationId(Control source, out int columnIndex)
    {
        foreach (var current in EnumerateRelatedControls(source))
        {
            if (TryParseVisualGridIndex(AutomationProperties.GetAutomationId(current), "_Cell", out columnIndex))
            {
                return true;
            }
        }

        columnIndex = -1;
        return false;
    }

    internal static bool TryReadItemsSource(Control control, out IReadOnlyList<object?> items)
    {
        var itemsSourceProperty = control.GetType().GetProperty("ItemsSource");
        var itemsValue = itemsSourceProperty?.GetValue(control);
        if (itemsValue is null)
        {
            var itemsProperty = control.GetType().GetProperty("Items");
            itemsValue = itemsProperty?.GetValue(control);
        }

        if (itemsValue is IEnumerable enumerable and not string)
        {
            items = enumerable.Cast<object?>().ToArray();
            return true;
        }

        items = Array.Empty<object?>();
        return false;
    }

    internal static bool TryFindItemIndex(
        IReadOnlyList<object?> items,
        object dataContext,
        out int rowIndex,
        out object item,
        bool referenceOnly = false)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var candidate = items[i];
            if (ReferenceEquals(candidate, dataContext))
            {
                rowIndex = i;
                item = candidate!;
                return true;
            }
        }

        var equalIndex = -1;
        if (!referenceOnly)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not { } candidate || !candidate.Equals(dataContext)) continue;
                if (equalIndex >= 0)
                {
                    equalIndex = -1;
                    break;
                }
                equalIndex = i;
            }
        }

        rowIndex = equalIndex;
        item = equalIndex >= 0 ? items[equalIndex]! : null!;
        return equalIndex >= 0;
    }

    internal RecorderGridRowCaptureResult CaptureRow(
        Control source,
        string? configuredTargetColumnName,
        string targetGridLocatorValue,
        UiLocatorKind targetGridLocatorKind,
        int rowIndex,
        int? columnIndex,
        bool excludeTargetColumnFromIdentity)
    {
        var matchingHints = _options.EnumerateGridHints()
            .Concat(_nativeGridDefinitions.Values.Select(static entry => entry.Hint))
            .Where(hint => hint.TargetLocatorKind == targetGridLocatorKind
                && string.Equals(
                    hint.TargetLocatorValue.Trim(),
                    targetGridLocatorValue.Trim(),
                    StringComparison.Ordinal))
            .ToArray();
        if (matchingHints.Length == 0)
        {
            return new RecorderGridRowCaptureResult(null, null);
        }

        if (matchingHints.Length != 1)
        {
            return new RecorderGridRowCaptureResult(null,
                $"Grid '{targetGridLocatorValue}' has multiple RecorderGridHint registrations; named row capture requires exactly one.");
        }

        var hint = matchingHints[0];
        var definition = FindGridDefinition(hint);
        if (definition is not null)
        {
            return CaptureCatalogRow(
                source,
                configuredTargetColumnName,
                hint,
                definition,
                rowIndex,
                columnIndex,
                excludeTargetColumnFromIdentity);
        }

        if (hint.RowIdentityColumnPropertyNames is not { Count: > 0 })
        {
            return new RecorderGridRowCaptureResult(null, null);
        }

        if (!TryValidateGridIdentityHint(hint, out var identityColumns, out var configurationError))
        {
            return new RecorderGridRowCaptureResult(null, configurationError);
        }

        if (!_controlReader.TryFindControl(hint.SourceLocatorValue, hint.SourceLocatorKind, out var gridSource)
            || !TryReadItemsSource(gridSource, out var items))
        {
            return new RecorderGridRowCaptureResult(null,
                $"Grid '{targetGridLocatorValue}' does not expose the configured ItemsSource for named row capture.");
        }

        if (rowIndex < 0 || rowIndex >= items.Count || items[rowIndex] is not { } item)
        {
            return new RecorderGridRowCaptureResult(null,
                $"Grid '{targetGridLocatorValue}' row index {rowIndex} is outside the current ItemsSource.");
        }

        string? targetColumnName = null;
        if (columnIndex is { } resolvedColumnIndex)
        {
            if (resolvedColumnIndex < 0 || resolvedColumnIndex >= hint.ColumnPropertyNames.Count)
            {
                return new RecorderGridRowCaptureResult(null,
                    $"Grid '{targetGridLocatorValue}' column index {resolvedColumnIndex} is outside ColumnPropertyNames.");
            }

            targetColumnName = hint.ColumnPropertyNames[resolvedColumnIndex].Trim();
        }

        var effectiveIdentityColumns = excludeTargetColumnFromIdentity && targetColumnName is not null
            ? identityColumns
                .Where(columnName => !string.Equals(columnName, targetColumnName, StringComparison.Ordinal))
                .ToArray()
            : identityColumns;
        if (effectiveIdentityColumns.Count == 0)
        {
            return new RecorderGridRowCaptureResult(null,
                $"Grid '{targetGridLocatorValue}' row identity is empty after excluding target column '{targetColumnName}'.");
        }

        var displayedGrid = _controlReader.TryFindControl(
                targetGridLocatorValue,
                targetGridLocatorKind,
                out var targetGrid)
            ? targetGrid
            : gridSource;
        if (!TryReadGridIdentity(
                displayedGrid,
                item,
                rowIndex,
                hint,
                effectiveIdentityColumns,
                out var conditions,
                out var readError))
        {
            return new RecorderGridRowCaptureResult(null, readError);
        }

        if (!TryReadGridModelIdentity(hint, item, effectiveIdentityColumns, out var modelIdentity, out readError))
        {
            return new RecorderGridRowCaptureResult(null, readError);
        }

        var matchingRows = 0;
        foreach (var candidate in items)
        {
            if (!TryReadGridModelIdentity(
                    hint,
                    candidate,
                    effectiveIdentityColumns,
                    out var candidateIdentity,
                    out readError))
            {
                return new RecorderGridRowCaptureResult(null, readError);
            }

            if (candidateIdentity.SequenceEqual(modelIdentity, StringComparer.Ordinal))
            {
                matchingRows++;
            }
        }

        if (matchingRows != 1)
        {
            return new RecorderGridRowCaptureResult(null,
                $"Grid '{targetGridLocatorValue}' configured row identity matches {matchingRows} rows; named row capture requires exactly one.");
        }

        return new RecorderGridRowCaptureResult(
            new RecorderGridRowCapture(conditions, targetColumnName), null);
    }

    internal RecorderGridRowCaptureResult CaptureCatalogRow(
        Control source,
        string? configuredTargetColumnName,
        RecorderGridHint hint,
        GridAutomationDefinition definition,
        int rowIndex,
        int? columnIndex,
        bool excludeTargetColumnFromIdentity,
        IReadOnlyList<object?>? captureItems = null)
    {
        var items = captureItems;
        if (!_controlReader.TryFindControl(hint.SourceLocatorValue, hint.SourceLocatorKind, out var gridSource)
            || items is null && !TryReadItemsSource(gridSource, out items))
        {
            return new RecorderGridRowCaptureResult(null,
                $"Grid '{definition.PagePropertyName}' does not expose the configured ItemsSource for stable row capture.");
        }

        var usesObjectIdentity = definition.Columns.Any(static column => column.IsObjectKey);
        if (usesObjectIdentity)
        {
            if (!NativeGridSelectionAccess.TryReadCurrentView(gridSource, out var currentView))
                return new RecorderGridRowCaptureResult(null, $"Grid '{definition.PagePropertyName}' requires an authoritative current view for object-key recording.");
            items = currentView;
        }

        if (rowIndex < 0 || rowIndex >= items.Count || items[rowIndex] is not { } item)
        {
            return new RecorderGridRowCaptureResult(null,
                $"Grid '{definition.PagePropertyName}' row {rowIndex} is no longer present in the current ItemsSource.");
        }

        var targetColumnName = string.IsNullOrWhiteSpace(configuredTargetColumnName)
            ? null
            : configuredTargetColumnName.Trim();
        if (targetColumnName is null && columnIndex is { } resolvedColumnIndex)
        {
            if (resolvedColumnIndex < 0 || resolvedColumnIndex >= hint.ColumnPropertyNames.Count)
            {
                return new RecorderGridRowCaptureResult(null,
                    $"Grid '{definition.PagePropertyName}' did not expose the selected logical column. "
                    + "Configure the column with GridColumnDefinition.Map(...).FromField(...).");
            }

            targetColumnName = hint.ColumnPropertyNames[resolvedColumnIndex].Trim();
        }

        var usesAutomaticIdentity = definition.RowIdentityColumns.Count == 0;
        var identityColumns = usesAutomaticIdentity
            ? definition.Columns
                .Where(static column => column.IsStableIdentityCandidate)
                .Select(static column => column.LogicalName)
                .ToArray()
            : definition.RowIdentityColumns.ToArray();
        var effectiveIdentityColumns = excludeTargetColumnFromIdentity && targetColumnName is not null
            ? identityColumns
                .Where(columnName => !string.Equals(columnName, targetColumnName, StringComparison.Ordinal))
                .ToArray()
            : identityColumns;
        if (effectiveIdentityColumns.Length == 0)
        {
            return new RecorderGridRowCaptureResult(null,
                $"Grid '{definition.PagePropertyName}' has no stable row identity for column '{targetColumnName}'. "
                + "Configure IdentifyRowsBy(...) or expose a native row-model key with KeyAttribute; index-based capture is not used.");
        }

        if (usesAutomaticIdentity
            && !TryValidateAutomaticGridIdentityValues(
                definition,
                items,
                effectiveIdentityColumns,
                out var automaticIdentityError))
        {
            return new RecorderGridRowCaptureResult(null, automaticIdentityError);
        }

        if (!TryReadGridModelIdentity(
                hint,
                item,
                effectiveIdentityColumns,
                out var identityValues,
                out var readError))
        {
            return new RecorderGridRowCaptureResult(null, readError);
        }

        if (usesObjectIdentity && !TryValidateObjectRowPublication(source, gridSource, item, definition, out readError))
            return new RecorderGridRowCaptureResult(null, readError);

        var matchingRows = 0;
        foreach (var candidate in items)
        {
            if (!TryReadGridModelIdentity(
                    hint,
                    candidate,
                    effectiveIdentityColumns,
                    out var candidateValues,
                    out readError))
            {
                return new RecorderGridRowCaptureResult(null, readError);
            }

            if (candidateValues.SequenceEqual(identityValues, StringComparer.Ordinal))
            {
                matchingRows++;
            }
        }

        if (matchingRows != 1)
        {
            return new RecorderGridRowCaptureResult(null,
                $"Grid '{definition.PagePropertyName}' stable row identity matched {matchingRows} rows; exactly one is required. "
                + "Add another column to IdentifyRowsBy(...).");
        }

        var capturedViewPosition = TryResolveCurrentGridViewPosition(
            gridSource,
            definition,
            items,
            item);
        var conditions = effectiveIdentityColumns
            .Select((columnName, index) => new RecordedGridRowCondition(columnName, identityValues[index])
            {
                IsObjectKey = definition.FindColumn(columnName)?.IsObjectKey == true
            })
            .ToArray();
        return new RecorderGridRowCaptureResult(
            new RecorderGridRowCapture(conditions, targetColumnName, capturedViewPosition, IsCatalog: true), null);
    }

    internal static int? TryResolveCurrentGridViewPosition(
        Control gridSource,
        GridAutomationDefinition definition,
        IReadOnlyList<object?> items,
        object selectedRow)
    {
        IReadOnlyList<object?> viewItems = items;
        var hasAuthoritativeView = false;
        try
        {
            var collectionView = gridSource.GetType().GetProperty("CollectionView")?.GetValue(gridSource);
            if (collectionView is IEnumerable currentView and not string)
            {
                viewItems = currentView.Cast<object?>().ToArray();
                hasAuthoritativeView = true;
            }
            else if (gridSource is ItemsControl itemsControl)
            {
                viewItems = itemsControl.ItemsView.Cast<object?>().ToArray();
                hasAuthoritativeView = true;
            }
        }
        catch
        {
            return null;
        }

        var itemIndexes = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not { } item || !itemIndexes.TryAdd(item, index))
            {
                return null;
            }
        }

        var positions = new Dictionary<object, double>(ReferenceEqualityComparer.Instance);
        foreach (var candidate in EnumerateDescendantControls(gridSource))
        {
            if (!IsVisibleForCapture(candidate)
                || candidate.Opacity <= 0.01
                || candidate.Bounds.Width <= 0
                || candidate.Bounds.Height <= 0
                || candidate.DataContext is not { } context)
            {
                continue;
            }

            var row = itemIndexes.ContainsKey(context)
                ? context
                : GridPropertyValueReader.TryReadPath(
                    context,
                    definition.CellContext.RowPath,
                    out var contextRow)
                    ? contextRow
                    : null;
            if (row is null || !itemIndexes.ContainsKey(row))
            {
                continue;
            }

            var point = candidate.TranslatePoint(new Point(0, 0), gridSource);
            if (point is null || point.Value.Y < 0 || point.Value.Y >= gridSource.Bounds.Height)
            {
                continue;
            }

            if (!positions.TryGetValue(row, out var currentY) || point.Value.Y < currentY)
            {
                positions[row] = point.Value.Y;
            }
        }

        return GridCurrentViewPositionResolver.FindPosition(
            viewItems, positions, selectedRow, hasAuthoritativeView);
    }

    internal static bool TryValidateAutomaticGridIdentityValues(
        GridAutomationDefinition definition,
        IReadOnlyList<object?> items,
        IReadOnlyList<string> identityColumns,
        out string error)
    {
        foreach (var item in items)
        {
            if (item is null)
            {
                error = $"Grid '{definition.PagePropertyName}' contains a null row and cannot prove an automatic stable identity.";
                return false;
            }

            foreach (var columnName in identityColumns)
            {
                var column = definition.FindColumn(columnName);
                var path = column?.DisplayValuePath ?? column?.SourceFieldName ?? columnName;
                if (!GridPropertyValueReader.TryReadPath(item, path, out var value) || !IsStableGridIdentityScalar(value))
                {
                    error = $"Grid '{definition.PagePropertyName}' column '{columnName}' is not a readable scalar stable identity. "
                        + "Configure IdentifyRowsBy(...) with one or more stable columns.";
                    return false;
                }
            }
        }

        error = string.Empty;
        return true;
    }

    internal static bool IsStableGridIdentityScalar(object? value)
    {
        if (value is null)
        {
            return false;
        }

        var type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        return type.IsEnum
            || type.IsPrimitive
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Guid);
    }

    internal static bool TryValidateGridIdentityHint(
        RecorderGridHint hint,
        out IReadOnlyList<string> identityColumns,
        out string error)
    {
        identityColumns = Array.Empty<string>();
        var columns = hint.ColumnPropertyNames.Select(static name => name?.Trim() ?? string.Empty).ToArray();
        if (columns.Length == 0
            || columns.Any(string.IsNullOrWhiteSpace)
            || columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
        {
            error = $"Grid '{hint.TargetLocatorValue}' ColumnPropertyNames must be non-empty and distinct for named row capture.";
            return false;
        }

        var identities = (hint.RowIdentityColumnPropertyNames ?? Array.Empty<string>())
            .Select(static name => name?.Trim() ?? string.Empty)
            .ToArray();
        if (identities.Length == 0
            || identities.Any(string.IsNullOrWhiteSpace)
            || identities.Distinct(StringComparer.Ordinal).Count() != identities.Length
            || identities.Any(identity => !columns.Contains(identity, StringComparer.Ordinal)))
        {
            error = $"Grid '{hint.TargetLocatorValue}' RowIdentityColumnPropertyNames must be non-empty, distinct, and contained in ColumnPropertyNames.";
            return false;
        }

        identityColumns = identities;
        error = string.Empty;
        return true;
    }

    internal static bool TryReadGridIdentity(
        Control gridSource,
        object rowItem,
        int rowIndex,
        RecorderGridHint hint,
        IReadOnlyList<string> identityColumns,
        out IReadOnlyList<RecordedGridRowCondition> conditions,
        out string error)
    {
        var result = new RecordedGridRowCondition[identityColumns.Count];
        for (var index = 0; index < identityColumns.Count; index++)
        {
            var columnName = identityColumns[index];
            var columnIndex = hint.ColumnPropertyNames
                .Select(static candidate => candidate.Trim())
                .ToList()
                .FindIndex(candidate => string.Equals(candidate, columnName, StringComparison.Ordinal));
            if (columnIndex < 0
                || !TryReadDisplayedGridCellValue(gridSource, rowItem, rowIndex, columnIndex, out var value))
            {
                conditions = Array.Empty<RecordedGridRowCondition>();
                error = $"Grid '{hint.TargetLocatorValue}' row {rowIndex} does not expose visible identity cell '{columnName}'.";
                return false;
            }

            result[index] = new RecordedGridRowCondition(columnName, value);
        }

        conditions = result;
        error = string.Empty;
        return true;
    }

    internal bool TryReadGridModelIdentity(
        RecorderGridHint hint,
        object? item,
        IReadOnlyList<string> identityColumns,
        out IReadOnlyList<string> values,
        out string error)
    {
        if (item is null)
        {
            values = [];
            error = $"Grid '{hint.TargetLocatorValue}' contains a row without a readable source item.";
            return false;
        }

        var definition = FindGridDefinition(hint);
        var result = new string[identityColumns.Count];
        for (var index = 0; index < identityColumns.Count; index++)
        {
            var columnName = identityColumns[index];
            var column = definition?.FindColumn(columnName);
            if (column?.IsObjectKey == true)
            {
                try { result[index] = GridRowObjectKey.Get(item); }
                catch (ArgumentException exception)
                {
                    values = [];
                    error = $"Grid '{hint.TargetLocatorValue}' cannot use this object row: {exception.Message}";
                    return false;
                }
                continue;
            }
            var valuePath = column?.DisplayValuePath ?? column?.SourceFieldName ?? columnName;
            if (!GridPropertyValueReader.TryReadPath(item, valuePath, out var rawValue)
                || !TryFormatGridValue(rawValue, column, out result[index]))
            {
                values = Array.Empty<string>();
                error = $"Grid row type '{item.GetType().FullName}' does not expose readable identity path '{valuePath}' for logical column '{columnName}'.";
                return false;
            }
        }

        values = result;
        error = string.Empty;
        return true;
    }

    internal static bool TryValidateObjectRowPublication(Control source, Control grid, object row,
        GridAutomationDefinition definition, out string error)
    {
        var key = GridRowObjectKey.Get(row);
        var property = GetObjectRowPublicationProperty(definition);
        var published = EnumerateRelatedControls(source)
            .TakeWhile(control => !ReferenceEquals(control, grid))
            .Where(control => ReferenceEquals(control.DataContext, row))
            .Select(control => control.GetValue(property))
            .Where(static value => value is not null)
            .ToArray();
        if (published.Length > 0 && published.All(value => string.Equals(value, key, StringComparison.Ordinal)))
        {
            error = string.Empty;
            return true;
        }
        error = $"Grid '{definition.PagePropertyName}' object row must publish its current GridRowObjectKey in the actual row {property.Name}; "
            + (published.Length == 0 ? "no row key was published." : "the published row key does not match the current row object (possibly a recycled container).");
        return false;
    }

    internal static AttachedProperty<string?> GetObjectRowPublicationProperty(GridAutomationDefinition definition) =>
        definition.Columns.Single(static column => column.IsObjectKey).RowIdentityAutomationProperty switch
        {
            GridRowAutomationProperty.AutomationId => AutomationProperties.AutomationIdProperty,
            GridRowAutomationProperty.Name => AutomationProperties.NameProperty,
            GridRowAutomationProperty.HelpText => AutomationProperties.HelpTextProperty,
            GridRowAutomationProperty.ItemStatus => AutomationProperties.ItemStatusProperty,
            _ => throw new InvalidOperationException($"Grid '{definition.PagePropertyName}' configures an unsupported object-key publication property.")
        };

    internal static bool TryFormatGridValue(
        object? value,
        GridColumnDefinition? column,
        out string text)
    {
        if (value is null)
        {
            text = string.Empty;
            return true;
        }

        try
        {
            text = GridCellValueNormalizer.FormatValue(value, column) ?? string.Empty;
            return true;
        }
        catch (FormatException)
        {
            text = string.Empty;
            return false;
        }
    }

    internal static bool TryReadDisplayedGridCellValue(
        Control gridSource,
        object? rowItem,
        int rowIndex,
        int columnIndex,
        out string value)
    {
        return TryReadDisplayedGridCellValue(
            gridSource,
            rowItem,
            rowIndex,
            columnIndex,
            columnName: null,
            out value);
    }

    internal static bool TryReadDisplayedGridCellValue(
        Control gridSource,
        object? rowItem,
        int rowIndex,
        int columnIndex,
        string? columnName,
        out string value)
    {
        foreach (var candidate in EnumerateDescendantControls(gridSource))
        {
            var automationId = AutomationProperties.GetAutomationId(candidate);
            var matchesIndexedColumn = TryParseVisualGridIndex(automationId, "_Cell", out var candidateColumnIndex)
                && candidateColumnIndex == columnIndex;
            var matchesNamedColumn = !string.IsNullOrWhiteSpace(columnName)
                && !string.IsNullOrWhiteSpace(automationId)
                && automationId.EndsWith($"_{columnName.Trim()}Cell", StringComparison.OrdinalIgnoreCase);
            if (!matchesIndexedColumn && !matchesNamedColumn)
            {
                continue;
            }

            if (rowItem is not null)
            {
                if (!ReferenceEquals(candidate.DataContext, rowItem))
                {
                    continue;
                }
            }
            else if (!TryParseVisualGridIndex(automationId, "_Row", out var candidateRowIndex)
                     || candidateRowIndex != rowIndex)
            {
                continue;
            }

            if (!IsVisibleForCapture(candidate)) continue;

            var displayedValue = ExtractTextValue(candidate);
            if (displayedValue is not null)
            {
                value = displayedValue.Trim();
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    internal static GridCellEditorKind? InferGridEditorKind(Control source)
    {
        foreach (var candidate in EnumerateRelatedControls(source))
        {
            switch (candidate)
            {
                case CheckBox:
                    return GridCellEditorKind.CheckBox;
                case NumericUpDown:
                    return GridCellEditorKind.Number;
                case DatePicker or Calendar:
                    return GridCellEditorKind.Date;
                case TimePicker:
                    return GridCellEditorKind.Time;
                case ComboBox or ListBox:
                    return GridCellEditorKind.ComboBox;
                case TextBox:
                    return GridCellEditorKind.Text;
            }
        }

        return null;
    }

    internal bool IsCatalogGridEditor(Control source, GridCellEditorKind editorKind)
    {
        return TryGetCatalogGridEditorKind(source, out var resolved) && resolved == editorKind;
    }

    internal bool TryGetCatalogGridEditorKind(Control source, out GridCellEditorKind editorKind)
    {
        editorKind = default;
        if (!TryResolveGridHint(source, out var hint, out var gridSource)
            || FindGridDefinition(hint) is not { } definition
            || !TryReadItemsSource(gridSource, out var items)
            || !GridCellMetadataExtractor.TryExtract(
                source,
                gridSource,
                definition,
                items,
                ExtractTextValue,
                out var metadata,
                out _))
        {
            return false;
        }

        var column = definition.FindColumnBySourceField(metadata.SourceFieldName);
        var resolved = column?.EditorKind ?? InferGridEditorKind(source);
        if (resolved is null)
        {
            return false;
        }

        editorKind = resolved.Value;
        return true;
    }

    internal bool TryResolveGridSearchPickerSemanticValue(
        Control source,
        bool requireLiteral,
        out SemanticValueCandidate candidate,
        out string error)
    {
        candidate = null!;
        error = string.Empty;
        var matchingHints = _options.GridSearchPickerHints
            .Where(hint => IsGridSearchPickerPart(source, hint))
            .ToArray();
        if (matchingHints.Length == 0)
        {
            return false;
        }

        if (matchingHints.Length != 1)
        {
            error = $"Value source matches {matchingHints.Length} grid SearchPicker hints; configure unique source and part locators.";
            return false;
        }

        var hint = matchingHints[0];
        if (ResolveGridSearchPickerContext(source, hint) is not { } context)
        {
            error = "Grid SearchPicker does not expose a resolvable row and column context.";
            return false;
        }

        var (gridHint, gridSource, rowIndex, columnIndex, rowItem, _) = context;
        if (columnIndex >= gridHint.ColumnPropertyNames.Count)
        {
            error = "Grid SearchPicker column context is outside the configured grid columns.";
            return false;
        }

        var displayedGrid = _controlReader.TryFindControl(hint.TargetGridLocatorValue, hint.TargetGridLocatorKind, out var targetGrid)
            ? targetGrid
            : null;
        string? displayedValue = null;
        if (rowItem is not null && displayedGrid is not null
            && TryReadDisplayedGridCellValue(
                displayedGrid,
                rowItem,
                rowIndex,
                columnIndex,
                gridHint.ColumnPropertyNames[columnIndex],
                out var value))
        {
            displayedValue = value;
        }

        if (requireLiteral && displayedValue is null)
        {
            error = "Grid SearchPicker does not expose a committed displayed value for a literal assertion.";
            return false;
        }

        candidate = new SemanticValueCandidate(
            CreateCompositeDescriptor(
                hint.TargetGridLocatorValue,
                UiControlType.Grid,
                hint.TargetGridLocatorKind,
                hint.TargetFallbackToName,
                displayedGrid ?? gridSource,
                warning: null),
            RecorderValueKind.GridCellText,
            RecorderValueAccessorKind.GridCellText,
            StringValue: displayedValue,
            GridContext: new GridValueContext(rowIndex, columnIndex, null, null));
        return true;
    }

    internal bool TryResolveGridEditorSemanticValue(
        Control source,
        bool requireLiteral,
        out SemanticValueCandidate candidate,
        out string error)
    {
        candidate = null!;
        error = string.Empty;
        if (!TryResolveGridEditHint(source, out var hint))
        {
            return false;
        }

        if (hint.RowIndex < 0 || hint.ColumnIndex < 0)
        {
            error = "Grid editor does not expose a resolvable row and column context.";
            return false;
        }

        string? displayedValue = null;
        if (_controlReader.TryFindControl(hint.ValueLocatorValue, hint.ValueLocatorKind, out var valueControl))
        {
            displayedValue = ExtractTextValue(valueControl)?.Trim();
            if (displayedValue is null
                && hint.EditorKind == GridCellEditorKind.Number
                && RecorderNumericValueReader.TryReadNumericValue(valueControl, out var numericValue))
            {
                displayedValue = numericValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        if (requireLiteral && displayedValue is null)
        {
            error = "Grid editor does not expose a committed displayed value for a literal assertion.";
            return false;
        }

        var descriptorSource = _controlReader.TryFindControl(
                hint.TargetGridLocatorValue,
                hint.TargetGridLocatorKind,
                out var targetGrid)
            ? targetGrid
            : source;
        candidate = new SemanticValueCandidate(
            CreateCompositeDescriptor(
                hint.TargetGridLocatorValue,
                UiControlType.Grid,
                hint.TargetGridLocatorKind,
                hint.TargetFallbackToName,
                descriptorSource,
                warning: null),
            RecorderValueKind.GridCellText,
            RecorderValueAccessorKind.GridCellText,
            StringValue: displayedValue,
            GridContext: new GridValueContext(hint.RowIndex, hint.ColumnIndex, null, null));
        return true;
    }

    internal bool TryResolveGridSemanticValue(
        Control source,
        bool requireLiteral,
        out SemanticValueCandidate candidate,
        out string error)
    {
        candidate = null!;
        error = string.Empty;
        if (!TryResolveGridHint(source, out var hint, out var gridSource))
        {
            return false;
        }

        var locator = _selectorResolver.Resolve(gridSource, UiControlType.Grid);
        if (!locator.Success || locator.Control is null)
        {
            error = locator.Message;
            return false;
        }

        if (!TryReadItemsSource(gridSource, out var items))
        {
            error = "Configured grid does not expose an ItemsSource for stable cell capture.";
            return false;
        }

        var definition = FindGridDefinition(hint);
        if (definition is not null)
        {
            if (!GridCellMetadataExtractor.TryExtract(
                source,
                gridSource,
                definition,
                items,
                ExtractTextValue,
                out var metadata,
                out error))
            {
                return false;
            }

            var configuredColumn = definition.FindColumnBySourceField(metadata.SourceFieldName);
            var logicalColumnName = configuredColumn?.LogicalName ?? metadata.SourceFieldName;
            var configuredColumnIndex = FindColumnIndex(hint.ColumnPropertyNames, logicalColumnName);
            var cellValue = metadata.DisplayText;
            if (configuredColumn?.ReadsDisplayedText != true && string.IsNullOrWhiteSpace(cellValue)
                && !TryFormatGridValue(metadata.RawValue, configuredColumn, out cellValue))
            {
                error = $"Grid column '{logicalColumnName}' does not expose readable display text. "
                    + "Configure DisplayValueFrom(...) or FormatWith(...).";
                return false;
            }

            if (!TryCreateGridSemanticValue(
                    locator.Control,
                    metadata.RowIndex,
                    configuredColumnIndex,
                    logicalColumnName,
                    cellValue ?? string.Empty,
                    metadata.RawValue,
                    configuredColumn,
                    out candidate,
                    out error))
            {
                return false;
            }

            if (requireLiteral && !HasLiteral(candidate))
            {
                error = $"Grid column '{logicalColumnName}' does not expose a current value for a literal assertion.";
                return false;
            }

            return true;
        }

        if (!TryResolveGridCell(source, gridSource, hint, items, out var rowIndex, out var legacyColumnIndex, out var legacyCellValue))
        {
            error = "Select a concrete grid cell before capturing its value.";
            return false;
        }

        candidate = new SemanticValueCandidate(
            locator.Control,
            RecorderValueKind.GridCellText,
            RecorderValueAccessorKind.GridCellText,
            StringValue: legacyCellValue,
            GridContext: new GridValueContext(rowIndex, legacyColumnIndex, null, null));
        return true;
    }

    internal static bool TryCreateGridSemanticValue(
        RecordedControlDescriptor control,
        int rowIndex,
        int columnIndex,
        string logicalColumnName,
        string displayText,
        object? rawValue,
        GridColumnDefinition? column,
        out SemanticValueCandidate candidate,
        out string error)
    {
        var valueKind = column?.ValueKind ?? RecorderGridValueConversion.InferGridCellValueKind(rawValue, column?.EditorKind);
        var recorderKind = valueKind switch
        {
            GridCellValueKind.Number => RecorderValueKind.Number,
            GridCellValueKind.Date => RecorderValueKind.Date,
            GridCellValueKind.DateTime => RecorderValueKind.DateTime,
            GridCellValueKind.Time => RecorderValueKind.Time,
            GridCellValueKind.Boolean => RecorderValueKind.Boolean,
            GridCellValueKind.Color => RecorderValueKind.Color,
            _ => RecorderValueKind.Text
        };
        var gridContext = new GridValueContext(rowIndex, columnIndex, null, logicalColumnName);
        candidate = recorderKind switch
        {
            RecorderValueKind.Number when RecorderGridValueConversion.TryConvertGridNumber(rawValue, displayText, column, out var number) =>
                new SemanticValueCandidate(
                    control,
                    recorderKind,
                    RecorderValueAccessorKind.GridCellValue,
                    DoubleValue: number,
                    GridContext: gridContext),
            RecorderValueKind.Date when RecorderGridValueConversion.TryConvertGridDate(rawValue, displayText, column, out var date) =>
                new SemanticValueCandidate(
                    control,
                    recorderKind,
                    RecorderValueAccessorKind.GridCellValue,
                    DateValue: date,
                    GridContext: gridContext),
            RecorderValueKind.DateTime when RecorderGridValueConversion.TryConvertGridDateTime(rawValue, displayText, column, out var dateTime) =>
                new SemanticValueCandidate(
                    control,
                    recorderKind,
                    RecorderValueAccessorKind.GridCellValue,
                    DateValue: dateTime,
                    GridContext: gridContext),
            RecorderValueKind.Time when RecorderGridValueConversion.TryConvertGridTime(rawValue, displayText, column, out var time) =>
                new SemanticValueCandidate(
                    control,
                    recorderKind,
                    RecorderValueAccessorKind.GridCellValue,
                    TimeValue: time,
                    GridContext: gridContext),
            RecorderValueKind.Boolean when RecorderGridValueConversion.TryConvertGridBoolean(rawValue, displayText, out var boolean) =>
                new SemanticValueCandidate(
                    control,
                    recorderKind,
                    RecorderValueAccessorKind.GridCellValue,
                    BoolValue: boolean,
                    GridContext: gridContext),
            RecorderValueKind.Color => new SemanticValueCandidate(
                control,
                recorderKind,
                RecorderValueAccessorKind.GridCellValue,
                StringValue: displayText,
                GridContext: gridContext),
            _ when recorderKind == RecorderValueKind.Text => new SemanticValueCandidate(
                control,
                recorderKind,
                RecorderValueAccessorKind.GridCellValue,
                StringValue: displayText,
                GridContext: gridContext),
            _ => null!
        };

        if (candidate is not null)
        {
            candidate = candidate with { DisplayText = displayText };
            error = string.Empty;
            return true;
        }

        error = $"Grid column '{column?.LogicalName ?? "<unknown>"}' is configured as {valueKind}, "
            + $"but value '{displayText}' cannot be converted. Configure DisplayValueFrom(...), FormatWith(...), or AsValue(...).";
        return false;
    }


    internal static bool IsGridSearchPickerPart(Control source, RecorderGridSearchPickerHint hint)
    {
        var relatedControls = EnumerateRelatedControls(source).ToArray();
        var matchesSource = relatedControls.Any(current =>
            HasExactLocator(current, hint.SourceLocatorKind, hint.SourceLocatorValue));
        var matchesPart = relatedControls.Any(current =>
            HasExactLocator(current, hint.Parts.LocatorKind, hint.Parts.SearchInputLocator)
            || HasExactLocator(current, hint.Parts.LocatorKind, hint.Parts.ResultsLocator)
            || (!string.IsNullOrWhiteSpace(hint.Parts.ApplyButtonLocator)
                && HasExactLocator(current, hint.Parts.LocatorKind, hint.Parts.ApplyButtonLocator!))
            || (!string.IsNullOrWhiteSpace(hint.Parts.ExpandButtonLocator)
                && HasExactLocator(current, hint.Parts.LocatorKind, hint.Parts.ExpandButtonLocator!)));
        return matchesSource && (matchesPart || HasExactLocator(source, hint.SourceLocatorKind, hint.SourceLocatorValue));
    }

    internal bool TryResolveGridEditHint(Control source, out RecorderGridEditHint hint)
    {
        foreach (var current in EnumerateRelatedControls(source))
        {
            foreach (var candidate in _options.GridEditHints)
            {
                if (TryGetLocator(current, candidate.SourceLocatorKind, out var locatorValue)
                    && string.Equals(candidate.SourceLocatorValue.Trim(), locatorValue, StringComparison.Ordinal))
                {
                    hint = candidate;
                    return true;
                }
            }
        }

        hint = null!;
        return false;
    }

    internal RecorderGridCellEditResolution ResolveCellEdit(Control? source)
    {
        if (source is null || !TryResolveGridHint(source, out var hint, out var gridSource))
            return new(false, null, "Control is not an editor inside a configured grid cell.");

        var definition = FindGridDefinition(hint);
        if (definition is null)
            return new(false, null, "Control is not an editor inside a GridAutomationCatalog grid.");

        if (!TryReadItemsSource(gridSource, out var items))
            return new(true, null,
                $"Grid '{definition.PagePropertyName}' does not expose an ItemsSource for cell action capture.");

        if (!GridCellMetadataExtractor.TryExtract(source, gridSource, definition, items, ExtractTextValue,
                out var metadata, out var metadataError))
            return new(true, null, metadataError);

        var column = definition.FindColumnBySourceField(metadata.SourceFieldName);
        var logicalColumnName = column?.LogicalName ?? metadata.SourceFieldName;
        var editorKind = column?.EditorKind ?? InferGridEditorKind(source);
        if (editorKind is null)
            return new(true, null,
                $"Grid '{definition.PagePropertyName}' column '{logicalColumnName}' does not expose a supported editor. "
                + "Configure EditWith(...) when its editor is not a standard Avalonia control.");

        return new(true, new RecorderGridCellEditContext(source, gridSource, definition, metadata,
            column, logicalColumnName, editorKind.Value), null);
    }

}

internal sealed record RecorderGridRowCapture(
    IReadOnlyList<RecordedGridRowCondition> Conditions,
    string? ColumnName,
    int? ViewPosition = null,
    bool IsCatalog = false);

internal sealed record RecorderGridRowCaptureResult(RecorderGridRowCapture? Capture, string? Error);

internal sealed record RecorderGridSearchPickerContext(
    RecorderGridHint GridHint,
    Control GridSource,
    int RowIndex,
    int ColumnIndex,
    object? RowItem,
    bool UsesObjectKey);

internal sealed record RecorderGridCellEditContext(
    Control Source,
    Control GridSource,
    GridAutomationDefinition Definition,
    RecorderGridCellMetadata Metadata,
    GridColumnDefinition? Column,
    string LogicalColumnName,
    GridCellEditorKind EditorKind);

internal sealed record RecorderGridCellEditResolution(
    bool Handled,
    RecorderGridCellEditContext? Context,
    string? Error);
