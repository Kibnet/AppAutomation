using System.Collections;
using System.Reflection;
using AppAutomation.Abstractions;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace AppAutomation.Avalonia.Headless.Automation.GridAutomation;

internal static class HeadlessGridRuntimeAccess
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    private static readonly string[] CancelEditingMethods = ["CancelEditing", "CloseEditor", "HideEditor"];

    public static object?[] ReadSourceItems(Control grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var source = grid is ItemsControl itemsControl
            ? itemsControl.ItemsSource
            : GridPropertyValueReader.TryReadProperty(grid, "ItemsSource", out var itemsSource)
                ? itemsSource
                : null;
        return source is IEnumerable values && source is not string
            ? values.Cast<object?>().ToArray()
            : Array.Empty<object?>();
    }

    public static string[] ReadColumnNames(Control grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid is DataGrid dataGrid)
        {
            return dataGrid.Columns
                .Select(ReadDataGridColumnName)
                .ToArray();
        }

        if (!GridPropertyValueReader.TryReadProperty(grid, "Columns", out var columns)
            || columns is not IEnumerable values
            || columns is string)
        {
            return Array.Empty<string>();
        }

        return values.Cast<object?>()
            .Select(static column =>
            {
                if (column is null)
                {
                    return string.Empty;
                }

                if (GridPropertyValueReader.TryReadProperty(column, "Header", out var header)
                    && MenuPathValue.TryGetVisibleCaption(header) is { } headerText
                    && !string.IsNullOrWhiteSpace(headerText))
                {
                    return headerText.Trim();
                }

                if (GridPropertyValueReader.TryReadProperty(column!, "FieldName", out var fieldName)
                    && Convert.ToString(fieldName, System.Globalization.CultureInfo.InvariantCulture) is { } sourceName
                    && !string.IsNullOrWhiteSpace(sourceName))
                {
                    return sourceName.Trim();
                }

                return null;
            })
            .Select(static name => name ?? string.Empty)
            .ToArray();
    }

    public static Control? FindCell(
        Control grid,
        object row,
        GridRuntimeColumn column,
        string gridDescription)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(column);

        var all = new[] { grid }
            .Concat(grid.GetVisualDescendants().OfType<Control>())
            .ToArray();
        var matches = all
            .Where(static candidate => candidate.IsEffectivelyVisible)
            .Where(candidate => IsCellContextMatch(candidate.DataContext, row, column))
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        var roots = matches
            .Where(candidate => !HasMatchingVisualAncestor(candidate, matches))
            .Take(2)
            .ToArray();
        if (roots.Length != 1)
        {
            throw new InvalidOperationException(
                $"Grid '{gridDescription}' exposes {roots.Length} visible cells for row "
                + $"'{row.GetType().FullName}' and source field '{column.SourceFieldName}'; expected exactly one.");
        }

        return roots[0];
    }

    public static bool ScrollIntoView(
        Control grid,
        object row,
        int rowIndex,
        GridRuntimeColumn column)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(column);
        if (grid is DataGrid dataGrid)
        {
            var dataGridColumn = FindDataGridColumn(dataGrid, column);
            dataGrid.ScrollIntoView(row, dataGridColumn);
            dataGrid.Dispatcher.RunJobs();
            dataGrid.UpdateLayout();
            return true;
        }

        var providerColumn = FindProviderColumn(grid, column);
        var providerRowIndex = ResolveProviderRowIndex(grid, row) ?? rowIndex;
        foreach (var method in FindMethods(grid, "ScrollIntoView"))
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                continue;
            }

            object firstArgument;
            if (parameters[0].ParameterType.IsInstanceOfType(row))
            {
                firstArgument = row;
            }
            else if (parameters[0].ParameterType == typeof(int))
            {
                if (providerRowIndex < 0)
                {
                    continue;
                }

                firstArgument = providerRowIndex;
            }
            else
            {
                continue;
            }

            var arguments = CreateArguments(parameters, firstArgument, providerColumn);
            if (arguments is null)
            {
                continue;
            }

            method.Invoke(grid, arguments);
            grid.Dispatcher.RunJobs();
            if (grid is global::Avalonia.Controls.Primitives.TemplatedControl templatedGrid)
            {
                templatedGrid.ApplyTemplate();
            }
            grid.UpdateLayout();
            return true;
        }

        return false;
    }

    public static HeadlessGridEditorActivation ActivateEditor(
        Control grid,
        object row,
        int rowIndex,
        GridRuntimeColumn column)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(column);

        if (grid is DataGrid dataGrid)
        {
            return ActivateDataGridEditor(dataGrid, row, column);
        }

        var providerColumn = FindProviderColumn(grid, column);
        var providerRowIndex = ResolveProviderRowIndex(grid, row) ?? rowIndex;
        if (providerRowIndex < 0)
        {
            throw new InvalidOperationException(
                "Grid runtime could not resolve the requested source item to a visible row index.");
        }

        var focusedItemAssigned = SetPropertyIfCompatible(grid, "FocusedItem", row);
        var focusedRowAssigned = SetPropertyIfCompatible(grid, "FocusedRowIndex", providerRowIndex);
        var focusedColumnAssigned = providerColumn is null;
        if (providerColumn is not null)
        {
            focusedColumnAssigned = SetPropertyIfCompatible(grid, "FocusedColumn", providerColumn);
        }

        _ = grid.Focus();
        var showEditorInvoked = InvokeParameterless(grid, "ShowEditor");
        var activeEditor = ReadActiveEditor(grid);
        var failureReason = activeEditor is null
            ? $"FocusedItem assigned: {focusedItemAssigned}; focused row assigned: {focusedRowAssigned}; "
              + $"focused column assigned: {focusedColumnAssigned}; ShowEditor invoked: {showEditorInvoked}."
            : null;
        return new HeadlessGridEditorActivation(
            activeEditor is not null,
            activeEditor,
            failureReason,
            InvocationAttempted: showEditorInvoked);
    }

    public static Control? ReadActiveEditor(Control grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid is DataGrid dataGrid)
        {
            return FindDataGridEditor(dataGrid, dataGrid.SelectedItem);
        }

        return GridPropertyValueReader.TryReadProperty(grid, "ActiveEditor", out var activeEditor)
            ? activeEditor as Control
            : null;
    }

    public static bool FinishEditing(Control grid, GridCellEditCommitMode commitMode)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid is DataGrid dataGrid)
        {
            return commitMode == GridCellEditCommitMode.Cancel
                ? dataGrid.CancelEdit()
                : dataGrid.CommitEdit();
        }

        if (commitMode == GridCellEditCommitMode.Cancel)
        {
            var activeEditor = ReadActiveEditor(grid);
            return activeEditor is not null
                && CancelEditingMethods.Any(methodName => InvokeParameterless(grid, methodName))
                && ReadActiveEditor(grid) is null;
        }

        // Composite editors first post their typed value into the grid transaction;
        // the grid then commits that transaction to the row model.
        var posted = InvokeParameterlessIfPresent(grid, "PostEditor");
        if (posted.Found && !posted.Succeeded)
        {
            return false;
        }

        var committed = InvokeParameterlessIfPresent(grid, "CommitEditing");
        return (posted.Found || committed.Found)
            && posted.Succeeded
            && committed.Succeeded;
    }

    public static bool TrySetNumericEditorValue(Control editor, string value)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!decimal.TryParse(
                value,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number))
        {
            return false;
        }

        var properties = editor.GetType().GetProperties(PublicInstance);
        foreach (var propertyName in new[] { "Value", "EditorValue" })
        {
            var matches = properties
                .Where(property =>
                    string.Equals(property.Name, propertyName, StringComparison.Ordinal)
                    && property.GetIndexParameters().Length == 0
                    && property.SetMethod is { IsPublic: true })
                .Take(2)
                .ToArray();
            if (matches.Length > 1)
            {
                return false;
            }
            if (matches.Length == 0
                || !TryConvertNumericValue(number, matches[0].PropertyType, out var converted))
            {
                continue;
            }

            matches[0].SetValue(editor, converted);
            var actual = matches[0].GetValue(editor);
            try
            {
                return actual is not null
                    && Convert.ToDecimal(actual, System.Globalization.CultureInfo.InvariantCulture) == number;
            }
            catch (InvalidCastException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        return false;
    }

    public static bool SelectRow(Control grid, object row)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(row);
        if (grid is DataGrid dataGrid)
        {
            dataGrid.SelectedItem = row;
            return ReferenceEquals(dataGrid.SelectedItem, row) || Equals(dataGrid.SelectedItem, row);
        }

        return SetPropertyIfCompatible(grid, "SelectedItem", row)
            || SetPropertyIfCompatible(grid, "FocusedItem", row);
    }

    private static bool IsCellContextMatch(object? context, object row, GridRuntimeColumn column)
    {
        if (context is null
            || !GridPropertyValueReader.TryReadPath(context, column.CellContext.RowPath, out var candidateRow)
            || !GridPropertyValueReader.TryReadPath(context, column.CellContext.FieldNamePath, out var fieldName))
        {
            return false;
        }

        return (ReferenceEquals(candidateRow, row) || Equals(candidateRow, row))
            && string.Equals(
                Convert.ToString(fieldName, System.Globalization.CultureInfo.InvariantCulture),
                column.SourceFieldName,
                StringComparison.Ordinal);
    }

    private static bool HasMatchingVisualAncestor(Control candidate, IReadOnlyCollection<Control> matches)
    {
        for (var parent = candidate.GetVisualParent() as Control;
             parent is not null;
             parent = parent.GetVisualParent() as Control)
        {
            if (matches.Contains(parent))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadDataGridColumnName(DataGridColumn column)
    {
        var header = MenuPathValue.TryGetVisibleCaption(column.Header);
        if (!string.IsNullOrWhiteSpace(header))
        {
            return header.Trim();
        }

        return string.IsNullOrWhiteSpace(column.SortMemberPath)
            ? string.Empty
            : column.SortMemberPath.Trim();
    }

    private static object? FindProviderColumn(Control grid, GridRuntimeColumn column)
    {
        if (grid is DataGrid dataGrid)
        {
            return FindDataGridColumn(dataGrid, column);
        }

        if (!GridPropertyValueReader.TryReadProperty(grid, "Columns", out var columns)
            || columns is not IEnumerable values
            || columns is string)
        {
            return null;
        }

        var matches = values.Cast<object?>()
            .Where(static candidate => candidate is not null)
            .Where(candidate =>
                GridPropertyValueReader.TryReadProperty(candidate!, "FieldName", out var fieldName)
                && string.Equals(
                    Convert.ToString(fieldName, System.Globalization.CultureInfo.InvariantCulture),
                    column.SourceFieldName,
                    StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Grid column source field '{column.SourceFieldName}' is ambiguous in the runtime Columns collection.");
        }

        if (matches.Length == 0)
        {
            throw new InvalidOperationException(
                $"Grid runtime Columns collection does not expose source field '{column.SourceFieldName}'.");
        }

        return matches[0];
    }

    private static HeadlessGridEditorActivation ActivateDataGridEditor(
        DataGrid dataGrid,
        object row,
        GridRuntimeColumn column)
    {
        var dataGridColumn = FindDataGridColumn(dataGrid, column);
        dataGrid.SelectedItem = row;
        dataGrid.CurrentColumn = dataGridColumn;
        dataGrid.ScrollIntoView(row, dataGridColumn);
        dataGrid.UpdateLayout();
        _ = dataGrid.Focus();
        var started = false;
        try
        {
            started = dataGrid.BeginEdit();
            dataGrid.Dispatcher.RunJobs();
            dataGrid.UpdateLayout();
            var editor = FindDataGridEditor(dataGrid, row);
            return new HeadlessGridEditorActivation(
                started,
                editor,
                editor is null
                    ? $"Native DataGrid BeginEdit returned {started}, but no active editor was materialized. "
                      + $"Observed controls: {DescribeDataGridControls(dataGrid, row)}."
                    : null,
                InvocationAttempted: true);
        }
        catch
        {
            if (started)
            {
                _ = dataGrid.CancelEdit();
            }

            throw;
        }
    }

    private static DataGridColumn FindDataGridColumn(DataGrid dataGrid, GridRuntimeColumn column)
    {
        if (column.RuntimeColumnIndex is { } runtimeColumnIndex
            && runtimeColumnIndex >= 0
            && runtimeColumnIndex < dataGrid.Columns.Count)
        {
            return dataGrid.Columns[runtimeColumnIndex];
        }

        var bindingMatches = dataGrid.Columns
            .Where(candidate => string.Equals(
                ReadDataGridBindingPath(candidate),
                column.SourceFieldName,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (bindingMatches.Length == 1)
        {
            return bindingMatches[0];
        }
        if (bindingMatches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Grid column binding path '{column.SourceFieldName}' is ambiguous in the runtime Columns collection.");
        }

        var matches = dataGrid.Columns
            .Where(candidate => string.Equals(
                candidate.SortMemberPath,
                column.SourceFieldName,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(matches.Length == 0
                ? $"Grid runtime Columns collection does not expose source field '{column.SourceFieldName}' by runtime index, binding path, or sort metadata."
                : $"Grid column source field '{column.SourceFieldName}' is ambiguous in the runtime Columns collection.");
        }

        return matches[0];
    }

    private static string? ReadDataGridBindingPath(DataGridColumn column)
    {
        return column is DataGridBoundColumn
            {
                Binding: global::Avalonia.Data.Binding binding
            }
            ? binding.Path
            : null;
    }

    private static Control? FindDataGridEditor(DataGrid dataGrid, object? row)
    {
        if (row is null)
        {
            return null;
        }

        var activeCell = FindCurrentDataGridCell(dataGrid, row);
        if (activeCell is null)
        {
            return null;
        }

        var cellControls = activeCell.GetVisualDescendants()
            .OfType<Control>()
            .Concat(activeCell.GetLogicalDescendants().OfType<Control>())
            .Prepend(activeCell)
            .Distinct()
            .ToArray();
        var semanticRoots = cellControls
            .Where(HasWritableSemanticValue)
            .Where(candidate => !candidate.GetVisualAncestors()
                .OfType<Control>()
                .Any(ancestor => cellControls.Contains(ancestor) && HasWritableSemanticValue(ancestor)))
            .OrderByDescending(static candidate => candidate.IsKeyboardFocusWithin)
            .Take(2)
            .ToArray();
        if (semanticRoots.Length == 1 || semanticRoots.FirstOrDefault()?.IsKeyboardFocusWithin == true)
        {
            return semanticRoots[0];
        }

        var builtInEditors = cellControls
            .Where(IsBuiltInEditor)
            .OrderByDescending(static candidate => candidate.IsKeyboardFocusWithin)
            .Take(2)
            .ToArray();
        return builtInEditors.Length switch
        {
            0 => null,
            1 => builtInEditors[0],
            _ when builtInEditors[0].IsKeyboardFocusWithin => builtInEditors[0],
            _ => null
        };
    }

    private static DataGridCell? FindCurrentDataGridCell(DataGrid dataGrid, object row)
    {
        var currentColumn = dataGrid.CurrentColumn;
        if (currentColumn is null || !currentColumn.IsVisible)
        {
            return null;
        }

        var sourceColumnIndex = dataGrid.Columns.IndexOf(currentColumn);
        if (sourceColumnIndex < 0)
        {
            return null;
        }

        var matchingRows = dataGrid.GetVisualDescendants()
            .OfType<DataGridRow>()
            .Where(candidate => ReferenceEquals(candidate.DataContext, row) || Equals(candidate.DataContext, row))
            .Take(2)
            .ToArray();
        if (matchingRows.Length != 1)
        {
            return null;
        }

        var cells = matchingRows[0].GetVisualDescendants()
            .OfType<DataGridCell>()
            .ToArray();
        return sourceColumnIndex < cells.Length
            ? cells[sourceColumnIndex]
            : null;
    }

    private static bool HasWritableSemanticValue(Control control)
    {
        return control.GetType()
            .GetProperties(PublicInstance)
            .Any(static property =>
                property.Name is "Value" or "EditorValue"
                && property.GetIndexParameters().Length == 0
                && property.SetMethod is { IsPublic: true });
    }

    private static bool IsBuiltInEditor(Control candidate) => candidate is TextBox
        or ComboBox
        or CheckBox
        or DatePicker
        or TimePicker;

    private static string DescribeDataGridControls(DataGrid dataGrid, object row)
    {
        var controls = dataGrid.GetVisualDescendants()
            .OfType<Control>()
            .Concat(dataGrid.GetLogicalDescendants().OfType<Control>())
            .Distinct()
            .Where(candidate => IsBuiltInEditor(candidate) || HasWritableSemanticValue(candidate))
            .Take(12)
            .Select(candidate =>
                $"{candidate.GetType().Name}[context="
                + (ReferenceEquals(candidate.DataContext, row)
                    ? "target"
                    : candidate.DataContext?.GetType().Name ?? "null")
                + $", focused={candidate.IsKeyboardFocusWithin}]")
            .ToArray();
        return controls.Length == 0 ? "<none>" : string.Join(", ", controls);
    }

    private static int? ResolveProviderRowIndex(Control grid, object row)
    {
        var methods = FindMethods(grid, "FindRowByItem")
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == 1
                    && parameters[0].ParameterType.IsInstanceOfType(row)
                    && method.ReturnType == typeof(int);
            })
            .Take(2)
            .ToArray();
        if (methods.Length > 1)
        {
            throw new InvalidOperationException(
                "Grid runtime exposes multiple compatible FindRowByItem methods.");
        }

        return methods.Length == 1
            ? (int?)methods[0].Invoke(grid, [row])
            : null;
    }

    private static bool SetPropertyIfCompatible(object target, string propertyName, object value)
    {
        var properties = target.GetType()
            .GetProperties(PublicInstance)
            .Where(property =>
                string.Equals(property.Name, propertyName, StringComparison.Ordinal)
                && property.GetIndexParameters().Length == 0
                && property.SetMethod is { IsPublic: true }
                && property.PropertyType.IsInstanceOfType(value))
            .Take(2)
            .ToArray();
        if (properties.Length != 1)
        {
            return false;
        }

        properties[0].SetValue(target, value);
        return ReferenceEquals(properties[0].GetValue(target), value)
            || Equals(properties[0].GetValue(target), value);
    }

    private static bool SetPropertyIfCompatible(object target, string propertyName, int value)
    {
        var properties = target.GetType()
            .GetProperties(PublicInstance)
            .Where(property =>
                string.Equals(property.Name, propertyName, StringComparison.Ordinal)
                && property.GetIndexParameters().Length == 0
                && property.SetMethod is { IsPublic: true }
                && property.PropertyType == typeof(int))
            .Take(2)
            .ToArray();
        if (properties.Length != 1)
        {
            return false;
        }

        properties[0].SetValue(target, value);
        return Equals(properties[0].GetValue(target), value);
    }

    private static bool TryConvertNumericValue(decimal value, Type propertyType, out object? converted)
    {
        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (targetType == typeof(object))
        {
            converted = value;
            return true;
        }

        if (targetType != typeof(byte)
            && targetType != typeof(sbyte)
            && targetType != typeof(short)
            && targetType != typeof(ushort)
            && targetType != typeof(int)
            && targetType != typeof(uint)
            && targetType != typeof(long)
            && targetType != typeof(ulong)
            && targetType != typeof(float)
            && targetType != typeof(double)
            && targetType != typeof(decimal))
        {
            converted = null;
            return false;
        }

        try
        {
            converted = Convert.ChangeType(
                value,
                targetType,
                System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (InvalidCastException)
        {
            converted = null;
            return false;
        }
        catch (FormatException)
        {
            converted = null;
            return false;
        }
        catch (OverflowException)
        {
            converted = null;
            return false;
        }
    }

    private static bool InvokeParameterless(object target, string methodName)
    {
        var methods = FindMethods(target, methodName)
            .Where(static method => method.GetParameters().Length == 0)
            .Take(2)
            .ToArray();
        if (methods.Length != 1)
        {
            return false;
        }

        var result = methods[0].Invoke(target, null);
        return methods[0].ReturnType != typeof(bool) || result is true;
    }

    private static InvocationResult InvokeParameterlessIfPresent(object target, string methodName)
    {
        var methods = FindMethods(target, methodName)
            .Where(static method => method.GetParameters().Length == 0)
            .Take(2)
            .ToArray();
        if (methods.Length == 0)
        {
            return new InvocationResult(Found: false, Succeeded: true);
        }

        if (methods.Length > 1)
        {
            return new InvocationResult(Found: true, Succeeded: false);
        }

        var result = methods[0].Invoke(target, null);
        return new InvocationResult(
            Found: true,
            Succeeded: methods[0].ReturnType != typeof(bool) || result is true);
    }

    private static IEnumerable<MethodInfo> FindMethods(object target, string methodName) =>
        target.GetType()
            .GetMethods(PublicInstance)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal));

    private static object?[]? CreateArguments(
        ParameterInfo[] parameters,
        object firstArgument,
        object? secondArgument)
    {
        var arguments = new object?[parameters.Length];
        arguments[0] = firstArgument;
        for (var index = 1; index < parameters.Length; index++)
        {
            if (index == 1
                && secondArgument is not null
                && parameters[index].ParameterType.IsInstanceOfType(secondArgument))
            {
                arguments[index] = secondArgument;
                continue;
            }

            if (!parameters[index].HasDefaultValue)
            {
                return null;
            }

            arguments[index] = parameters[index].DefaultValue;
        }

        return arguments;
    }
}

internal sealed record HeadlessGridEditorActivation(
    bool Started,
    Control? ActiveEditor,
    string? FailureReason = null,
    bool InvocationAttempted = false);

internal readonly record struct InvocationResult(bool Found, bool Succeeded);
