namespace AppAutomation.Abstractions;

/// <summary>
/// Exposes stable names for a grid's ordered columns.
/// </summary>
public interface IGridColumnMetadataControl
{
    /// <summary>
    /// Gets the stable column names in their runtime order.
    /// </summary>
    IReadOnlyList<string> ColumnNames { get; }

    /// <summary>
    /// Resolves a stable column name to its zero-based runtime index.
    /// </summary>
    bool TryGetColumnIndex(string columnName, out int columnIndex);
}

public static partial class UiControlResolverExtensions
{
    /// <summary>
    /// Adds stable ordered column metadata to one grid property.
    /// </summary>
    public static IUiControlResolver WithGridColumns(
        this IUiControlResolver innerResolver,
        string propertyName,
        IReadOnlyList<string> columnNames)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(columnNames);

        return innerResolver.WithAdapters(new GridColumnMetadataAdapter(propertyName, columnNames));
    }
}

/// <summary>
/// Adds configured column metadata while preserving the source grid's runtime capabilities.
/// </summary>
public sealed class GridColumnMetadataAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly string[] _columnNames;

    /// <summary>
    /// Initializes column metadata for one logical grid property.
    /// </summary>
    public GridColumnMetadataAdapter(string propertyName, IReadOnlyList<string> columnNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(columnNames);

        _propertyName = propertyName.Trim();
        _columnNames = ValidateColumnNames(columnNames);
    }

    /// <inheritdoc />
    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);

        return IsGridType(requestedType)
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        var grid = innerResolver.Resolve<IGridControl>(definition);
        return grid switch
        {
            IGridUserActionControl actionGrid when grid is IEditableGridControl editableGrid =>
                new ActionEditableGridWithColumns(actionGrid, editableGrid, _columnNames),
            IGridUserActionControl actionGrid => new ActionGridWithColumns(actionGrid, _columnNames),
            IEditableGridControl editableGrid => new EditableGridWithColumns(editableGrid, _columnNames),
            _ => new GridWithColumns(grid, _columnNames)
        };
    }

    private static bool IsGridType(Type requestedType)
    {
        return requestedType == typeof(IGridControl)
            || requestedType == typeof(IGridUserActionControl)
            || requestedType == typeof(IEditableGridControl);
    }

    private static string[] ValidateColumnNames(IReadOnlyList<string> columnNames)
    {
        if (columnNames.Count == 0)
        {
            throw new ArgumentException("At least one grid column name is required.", nameof(columnNames));
        }

        var result = new string[columnNames.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < columnNames.Count; index++)
        {
            var columnName = columnNames[index];
            if (string.IsNullOrWhiteSpace(columnName))
            {
                throw new ArgumentException("Grid column names cannot be empty.", nameof(columnNames));
            }

            var normalized = columnName.Trim();
            if (!seen.Add(normalized))
            {
                throw new ArgumentException($"Grid column name '{normalized}' is duplicated.", nameof(columnNames));
            }

            result[index] = normalized;
        }

        return result;
    }

    private class GridWithColumns : IGridControl, IGridColumnMetadataControl
    {
        protected readonly IGridControl Inner;
        private readonly Dictionary<string, int> _columnIndexes;

        public GridWithColumns(IGridControl inner, string[] columnNames)
        {
            Inner = inner;
            ColumnNames = Array.AsReadOnly(columnNames);
            _columnIndexes = columnNames
                .Select(static (name, index) => new KeyValuePair<string, int>(name, index))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        }

        public string AutomationId => Inner.AutomationId;

        public string Name => Inner.Name;

        public bool IsEnabled => Inner.IsEnabled;

        public IReadOnlyList<IGridRowControl> Rows => Inner.Rows;

        public IReadOnlyList<string> ColumnNames { get; }

        public IGridRowControl? GetRowByIndex(int index) => Inner.GetRowByIndex(index);

        public bool TryGetColumnIndex(string columnName, out int columnIndex)
        {
            if (string.IsNullOrWhiteSpace(columnName))
            {
                columnIndex = -1;
                return false;
            }

            return _columnIndexes.TryGetValue(columnName.Trim(), out columnIndex);
        }
    }

    private sealed class ActionGridWithColumns : GridWithColumns, IGridUserActionControl
    {
        private readonly IGridUserActionControl _actionGrid;

        public ActionGridWithColumns(IGridUserActionControl inner, string[] columnNames)
            : base(inner, columnNames)
        {
            _actionGrid = inner;
        }

        public void OpenRow(int rowIndex) => _actionGrid.OpenRow(rowIndex);

        public void SortByColumn(string columnName) => _actionGrid.SortByColumn(columnName);

        public void ScrollToEnd() => _actionGrid.ScrollToEnd();

        public string CopyCell(int rowIndex, int columnIndex) => _actionGrid.CopyCell(rowIndex, columnIndex);

        public void Export() => _actionGrid.Export();
    }

    private sealed class EditableGridWithColumns : GridWithColumns, IEditableGridControl
    {
        private readonly IEditableGridControl _editableGrid;

        public EditableGridWithColumns(IEditableGridControl inner, string[] columnNames)
            : base(inner, columnNames)
        {
            _editableGrid = inner;
        }

        public void EditCell(GridCellEditRequest request) => _editableGrid.EditCell(request);
    }

    private sealed class ActionEditableGridWithColumns : GridWithColumns, IGridUserActionControl, IEditableGridControl
    {
        private readonly IGridUserActionControl _actionGrid;
        private readonly IEditableGridControl _editableGrid;

        public ActionEditableGridWithColumns(
            IGridUserActionControl actionGrid,
            IEditableGridControl editableGrid,
            string[] columnNames)
            : base(actionGrid, columnNames)
        {
            _actionGrid = actionGrid;
            _editableGrid = editableGrid;
        }

        public void OpenRow(int rowIndex) => _actionGrid.OpenRow(rowIndex);

        public void SortByColumn(string columnName) => _actionGrid.SortByColumn(columnName);

        public void ScrollToEnd() => _actionGrid.ScrollToEnd();

        public string CopyCell(int rowIndex, int columnIndex) => _actionGrid.CopyCell(rowIndex, columnIndex);

        public void Export() => _actionGrid.Export();

        public void EditCell(GridCellEditRequest request) => _editableGrid.EditCell(request);
    }
}
