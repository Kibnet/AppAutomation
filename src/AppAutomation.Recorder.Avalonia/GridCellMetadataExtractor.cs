using System.Collections;
using System.Reflection;
using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace AppAutomation.Recorder.Avalonia;

internal sealed record RecorderGridCellMetadata(
    object Row,
    int RowIndex,
    string SourceFieldName,
    object? RawValue,
    string? DisplayText,
    Control VisualRowOwner,
    Control CellOwner,
    Control EditorRoot,
    IReadOnlyList<string> InspectedContexts);

internal sealed record RecorderNativeGridColumn(
    int Index,
    object Column,
    string LogicalName,
    string SourceFieldName,
    GridCellEditorKind? EditorKind,
    GridCellValueKind? ValueKind,
    bool IsStableIdentityCandidate);

internal static class GridCellMetadataExtractor
{
    public static bool TryExtract(
        Control source,
        Control gridRoot,
        GridAutomationDefinition? definition,
        IReadOnlyList<object?> items,
        Func<Control, string?> displayTextReader,
        out RecorderGridCellMetadata metadata,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(gridRoot);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(displayTextReader);

        var context = definition?.CellContext ?? GridCellContextDefinition.Default;
        var rows = new List<(object Row, int RowIndex, Control Owner)>();
        var fields = new List<string>();
        var rawValues = new List<object?>();
        var inspected = new List<string>();
        RecorderNativeGridColumn? nativeColumn = null;
        string? missingNativeSourceCaption = null;
        var visualPath = EnumeratePath(source, gridRoot).ToArray();

        foreach (var current in visualPath)
        {
            var dataContext = current.DataContext;
            inspected.Add($"{current.GetType().Name}:{dataContext?.GetType().FullName ?? "<null>"}");
            if (dataContext is null)
            {
                continue;
            }

            if (TryFindItem(items, dataContext, out var directIndex, out var directRow))
            {
                AddDistinctRow(rows, directRow, directIndex, current);
            }

            if (TryReadPath(dataContext, context.RowPath, out var contextRowValue)
                && contextRowValue is not null
                && TryFindItem(items, contextRowValue, out var rowIndex, out var row))
            {
                AddDistinctRow(rows, row, rowIndex, current);
            }

            if (TryReadPath(dataContext, context.FieldNamePath, out var fieldValue)
                && fieldValue is not null
                && fieldValue is not Control
                && (fieldValue is string || fieldValue is not IEnumerable)
                && fieldValue.ToString() is { } fieldText
                && !string.IsNullOrWhiteSpace(fieldText))
            {
                AddDistinct(fields, fieldText.Trim());
            }

            if (TryReadPath(dataContext, context.ValuePath, out var rawValue))
            {
                rawValues.Add(rawValue);
            }

            if (nativeColumn is null
                && IsNativeDataGrid(gridRoot)
                && TryReadProperty(current, "Column", out var columnObject)
                && columnObject is not null)
            {
                nativeColumn = ReadNativeColumns(gridRoot)
                    .FirstOrDefault(candidate => ReferenceEquals(candidate.Column, columnObject));
                if (nativeColumn is not null)
                {
                    if (string.IsNullOrWhiteSpace(nativeColumn.SourceFieldName))
                    {
                        missingNativeSourceCaption = nativeColumn.LogicalName;
                    }
                    else
                    {
                        AddDistinct(fields, nativeColumn.SourceFieldName);
                    }
                }
            }
        }

        if (IsNativeDataGrid(gridRoot) && nativeColumn is null)
        {
            nativeColumn = TryResolveNativeColumnFromSource(source, gridRoot);
            if (nativeColumn is not null)
            {
                if (string.IsNullOrWhiteSpace(nativeColumn.SourceFieldName))
                {
                    missingNativeSourceCaption = nativeColumn.LogicalName;
                }
                else
                {
                    AddDistinct(fields, nativeColumn.SourceFieldName);
                }
            }
        }

        if (fields.Count == 0
            && rows.Count == 1
            && definition is not null
            && TryResolveConfiguredVisualColumn(
                source,
                gridRoot,
                rows[0].Row,
                definition,
                out var configuredVisualColumn))
        {
            AddDistinct(fields, configuredVisualColumn.SourceFieldName);
        }

        if (rows.Count != 1 || fields.Count != 1)
        {
            metadata = null!;
            error = BuildFailure(
                gridRoot,
                source,
                rows,
                fields,
                inspected,
                rows.Count == 0
                    ? $"row metadata '{context.RowPath}' was not found"
                    : rows.Count > 1
                        ? "multiple row objects were found in the selected visual path"
                        : fields.Count == 0 && missingNativeSourceCaption is not null
                            ? $"native column '{missingNativeSourceCaption}' exposes only a visual caption and has no stable SortMemberPath or Binding.Path; "
                                + "register this column explicitly with GridColumnDefinition.Map(...).FromField(...)"
                        : fields.Count == 0
                            ? $"column metadata '{context.FieldNamePath}' was not found"
                            : "multiple source fields were found in the selected visual path");
            return false;
        }

        var selectedRow = rows[0];
        var sourceField = fields[0];
        var cellOwner = ResolveCellOwner(
            visualPath,
            selectedRow.Row,
            sourceField,
            context) ?? selectedRow.Owner;
        var cellOwnerIndex = Array.IndexOf(visualPath, cellOwner);
        var editorRoot = cellOwnerIndex > 0
            ? visualPath[cellOwnerIndex - 1]
            : cellOwner;
        var column = definition?.FindColumnBySourceField(sourceField);
        var raw = rawValues.Count > 0
            ? rawValues[0]
            : TryReadPath(
                selectedRow.Row,
                column?.DisplayValuePath ?? column?.SourceFieldName ?? sourceField,
                out var displayedRowValue)
                ? displayedRowValue
                : null;

        metadata = new RecorderGridCellMetadata(
            selectedRow.Row,
            selectedRow.RowIndex,
            sourceField,
            raw,
            displayTextReader(source)?.Trim(),
            selectedRow.Owner,
            cellOwner,
            editorRoot,
            inspected);
        error = string.Empty;
        return true;
    }

    private static Control? ResolveCellOwner(
        IReadOnlyList<Control> visualPath,
        object row,
        string sourceField,
        GridCellContextDefinition context)
    {
        Control? outermost = null;
        foreach (var control in visualPath)
        {
            var dataContext = control.DataContext;
            if (dataContext is null
                || !TryReadPath(dataContext, context.RowPath, out var contextRow)
                || contextRow is null
                || !(ReferenceEquals(contextRow, row) || contextRow.Equals(row))
                || !TryReadPath(dataContext, context.FieldNamePath, out var field)
                || !string.Equals(field?.ToString()?.Trim(), sourceField, StringComparison.Ordinal))
            {
                continue;
            }

            outermost = control;
        }

        return outermost;
    }

    public static bool IsNativeDataGrid(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        for (var type = control.GetType(); type is not null; type = type.BaseType)
        {
            if (string.Equals(type.FullName, "Avalonia.Controls.DataGrid", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<RecorderNativeGridColumn> ReadNativeColumns(Control grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!TryReadProperty(grid, "Columns", out var columnsValue)
            || columnsValue is not IEnumerable columnsEnumerable)
        {
            return Array.Empty<RecorderNativeGridColumn>();
        }

        var columns = columnsEnumerable.Cast<object?>().Where(static column => column is not null).Cast<object>().ToArray();
        var stableIdentityPaths = ReadStableIdentityPaths(grid);
        var result = new List<RecorderNativeGridColumn>(columns.Length);
        for (var index = 0; index < columns.Length; index++)
        {
            var column = columns[index];
            if (!ReadBooleanProperty(column, "IsVisible", defaultValue: true))
            {
                continue;
            }

            var sourceField = ReadColumnSourceField(column);
            var logicalName = ReadColumnCaption(column) ?? sourceField;
            if (string.IsNullOrWhiteSpace(logicalName))
            {
                continue;
            }

            var editorKind = InferNativeEditorKind(column);
            GridCellValueKind? valueKind = editorKind switch
            {
                GridCellEditorKind.Number => GridCellValueKind.Number,
                GridCellEditorKind.Date => GridCellValueKind.Date,
                GridCellEditorKind.Time => GridCellValueKind.Time,
                GridCellEditorKind.CheckBox => GridCellValueKind.Boolean,
                GridCellEditorKind.ComboBox or GridCellEditorKind.SearchPicker => GridCellValueKind.Selection,
                GridCellEditorKind.Color => GridCellValueKind.Color,
                GridCellEditorKind.Text => GridCellValueKind.Text,
                _ => null
            };
            result.Add(new RecorderNativeGridColumn(
                index,
                column,
                logicalName.Trim(),
                sourceField.Trim(),
                editorKind,
                valueKind,
                stableIdentityPaths.Contains(sourceField.Trim())));
        }

        return result;
    }

    public static bool TryReadPath(object source, string propertyPath, out object? value)
    {
        return GridPropertyValueReader.TryReadPath(source, propertyPath, out value);
    }

    private static IEnumerable<Control> EnumeratePath(Control source, Control gridRoot)
    {
        var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        Control? current = source;
        while (current is not null && visited.Add(current))
        {
            yield return current;
            if (ReferenceEquals(current, gridRoot))
            {
                yield break;
            }

            current = current.GetVisualParent() as Control
                ?? current.GetLogicalParent() as Control;
        }
    }

    private static bool TryReadProperty(object source, string propertyName, out object? value)
    {
        return GridPropertyValueReader.TryReadProperty(source, propertyName, out value);
    }

    private static RecorderNativeGridColumn? TryResolveNativeColumnFromSource(
        Control source,
        Control grid)
    {
        var columns = ReadNativeColumns(grid);
        foreach (var current in EnumeratePath(source, grid))
        {
            if (!TryReadProperty(current, "Column", out var value) || value is null)
            {
                continue;
            }

            var match = columns.FirstOrDefault(candidate => ReferenceEquals(candidate.Column, value));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static bool TryResolveConfiguredVisualColumn(
        Control source,
        Control gridRoot,
        object row,
        GridAutomationDefinition definition,
        out GridColumnDefinition column)
    {
        column = null!;
        GridColumnDefinition? outermostMatch = null;
        foreach (var current in EnumeratePath(source, gridRoot))
        {
            if (current.GetVisualParent() is not global::Avalonia.Controls.Grid owner
                || !ReferenceEquals(current.DataContext, row)
                || !ReferenceEquals(owner.DataContext, row))
            {
                continue;
            }

            var columnIndex = global::Avalonia.Controls.Grid.GetColumn(current);
            if (columnIndex >= 0 && columnIndex < definition.Columns.Count)
            {
                // Nested editors commonly contain their own zero-based layout grids. The
                // outermost row grid is the one whose column position describes the cell.
                outermostMatch = definition.Columns[columnIndex];
            }
        }

        if (outermostMatch is null)
        {
            return false;
        }

        column = outermostMatch;
        return true;
    }

    private static string? ReadColumnCaption(object column)
    {
        TryReadProperty(column, "Header", out var header);
        return header switch
        {
            null => null,
            string text when !string.IsNullOrWhiteSpace(text) => text,
            TextBlock textBlock when !string.IsNullOrWhiteSpace(textBlock.Text) => textBlock.Text,
            Label label when label.Content is not null => label.Content.ToString(),
            ContentControl contentControl when contentControl.Content is not null => contentControl.Content.ToString(),
            Control control when !string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)) =>
                AutomationProperties.GetName(control),
            _ => header.ToString()
        };
    }

    private static string ReadColumnSourceField(object column)
    {
        if (TryReadProperty(column, "SortMemberPath", out var sortMemberPathValue)
            && sortMemberPathValue is string sortMemberPath
            && !string.IsNullOrWhiteSpace(sortMemberPath))
        {
            return sortMemberPath.Trim();
        }

        if (TryReadProperty(column, "Binding", out var binding)
            && binding is not null
            && TryReadProperty(binding, "Path", out var pathValue))
        {
            var bindingPath = pathValue?.ToString();
            if (!string.IsNullOrWhiteSpace(bindingPath))
            {
                return bindingPath.Trim();
            }
        }

        return string.Empty;
    }

    private static GridCellEditorKind? InferNativeEditorKind(object column)
    {
        var typeName = column.GetType().Name;
        if (typeName.Contains("CheckBox", StringComparison.OrdinalIgnoreCase))
        {
            return GridCellEditorKind.CheckBox;
        }

        if (typeName.Contains("ComboBox", StringComparison.OrdinalIgnoreCase))
        {
            return GridCellEditorKind.ComboBox;
        }

        if (typeName.Contains("Text", StringComparison.OrdinalIgnoreCase))
        {
            return GridCellEditorKind.Text;
        }

        return null;
    }

    private static bool ReadBooleanProperty(object source, string propertyName, bool defaultValue)
    {
        return TryReadProperty(source, propertyName, out var value) && value is bool result
            ? result
            : defaultValue;
    }

    private static HashSet<string> ReadStableIdentityPaths(Control grid)
    {
        if (!TryReadProperty(grid, "ItemsSource", out var itemsSource)
            || itemsSource is not IEnumerable enumerable)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var row = enumerable.Cast<object?>().FirstOrDefault(static item => item is not null);
        if (row is null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return row.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property =>
                property.GetIndexParameters().Length == 0
                && property.GetMethod is { IsPublic: true }
                && property.CustomAttributes.Any(static attribute => string.Equals(
                    attribute.AttributeType.FullName,
                    "System.ComponentModel.DataAnnotations.KeyAttribute",
                    StringComparison.Ordinal)))
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool TryFindItem(
        IReadOnlyList<object?> items,
        object candidate,
        out int rowIndex,
        out object row)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not { } item || !ReferenceEquals(item, candidate))
            {
                continue;
            }

            rowIndex = index;
            row = item;
            return true;
        }

        var equivalentIndexes = items
            .Select(static (item, index) => (Item: item, Index: index))
            .Where(entry => entry.Item is not null && entry.Item.Equals(candidate))
            .Take(2)
            .ToArray();
        if (equivalentIndexes.Length == 1)
        {
            rowIndex = equivalentIndexes[0].Index;
            row = equivalentIndexes[0].Item!;
            return true;
        }

        rowIndex = -1;
        row = null!;
        return false;
    }

    private static void AddDistinctRow(
        List<(object Row, int RowIndex, Control Owner)> rows,
        object row,
        int rowIndex,
        Control owner)
    {
        if (rows.Any(candidate =>
                candidate.RowIndex == rowIndex
                && (ReferenceEquals(candidate.Row, row) || candidate.Row.Equals(row))))
        {
            return;
        }

        rows.Add((row, rowIndex, owner));
    }

    private static void AddDistinct(List<string> values, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!values.Contains(value, StringComparer.Ordinal))
        {
            values.Add(value);
        }
    }

    private static string BuildFailure(
        Control gridRoot,
        Control source,
        List<(object Row, int RowIndex, Control Owner)> rows,
        List<string> fields,
        List<string> inspected,
        string reason)
    {
        return $"Grid cell resolution failed: grid='{GetSafeId(gridRoot)}'; "
            + $"selectedType='{source.GetType().FullName}'; "
            + $"selectedDataContext='{source.DataContext?.GetType().FullName ?? "<null>"}'; "
            + $"rows={rows.Count}; fields=[{string.Join(", ", fields)}]; "
            + $"contexts=[{string.Join(" -> ", inspected)}]; reason={reason}. "
            + "Configure GridAutomationDefinition.WithCellContext(...) and IdentifyRowsBy(...) when the grid uses another metadata shape.";
    }

    private static string GetSafeId(Control control)
    {
        return global::Avalonia.Automation.AutomationProperties.GetAutomationId(control)
            ?? control.Name
            ?? control.GetType().Name;
    }
}
