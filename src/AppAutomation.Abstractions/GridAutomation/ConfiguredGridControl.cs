namespace AppAutomation.Abstractions;

internal class ConfiguredGridControl :
    IAddressableGridControl,
    IGridColumnMetadataControl,
    IGridAutomationCatalogControl
{
    private readonly IEditableGridControl? _editableGrid;
    private readonly IGridUserActionControl? _actionGrid;
    private readonly IAddressableGridControl? _addressableGrid;
    private readonly IIndexedAddressableGridControl? _indexedGrid;
    private readonly IReadOnlyList<GridColumnDefinition> _columns;
    private readonly IReadOnlyList<string> _runtimeColumnNames;
    private readonly Dictionary<string, int> _columnIndexes;

    public ConfiguredGridControl(
        IGridControl inner,
        IEditableGridControl? editableGrid,
        IGridUserActionControl? actionGrid,
        GridAutomationDefinition definition,
        IReadOnlyList<GridColumnDefinition> columns,
        string catalogFingerprint)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _editableGrid = editableGrid;
        _actionGrid = actionGrid;
        _addressableGrid = inner as IAddressableGridControl;
        _indexedGrid = inner as IIndexedAddressableGridControl;
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _columns = columns ?? throw new ArgumentNullException(nameof(columns));
        GridAutomationFingerprint = catalogFingerprint;
        ColumnNames = Array.AsReadOnly(columns.Select(static column => column.LogicalName).ToArray());
        _runtimeColumnNames = inner is IGridColumnMetadataControl metadata
            && metadata.ColumnNames.Count == columns.Count
                ? Array.AsReadOnly(metadata.ColumnNames.ToArray())
                : ColumnNames;
        _columnIndexes = columns
            .Select(static (column, index) => (column.LogicalName, index))
            .ToDictionary(static item => item.LogicalName, static item => item.index, StringComparer.Ordinal);
    }

    protected IGridControl Inner { get; }

    protected GridAutomationDefinition Definition { get; }

    public string AutomationId => Inner.AutomationId;

    public string Name => Inner.Name;

    public bool IsEnabled => Inner.IsEnabled;

    public IReadOnlyList<IGridRowControl> Rows => Inner.Rows;

    public IReadOnlyList<string> ColumnNames { get; }

    public string GridAutomationFingerprint { get; }

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

    public GridRowResolution ResolveRow(GridRowSelector row, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);

        if (ShouldUseAddressableGrid())
        {
            return _addressableGrid!.ResolveRow(MapAddressableRow(row), timeoutMs);
        }

        if (_indexedGrid is not null)
        {
            return _indexedGrid.ResolveRow(MapRow(row), timeoutMs);
        }

        if (_addressableGrid is not null)
        {
            return _addressableGrid.ResolveRow(MapAddressableRow(row), timeoutMs);
        }

        var matches = GridRuntimeResolver.FindMatchingRowIndexes(this, row);
        var description =
            $"selector={GridRuntimeResolver.DescribeRowSelector(row)}; matches={matches.Count}; rows={Rows.Count}";
        return matches.Count switch
        {
            0 => GridRowResolution.NotFound(description),
            1 => GridRowResolution.Unique(description),
            _ => GridRowResolution.Ambiguous(matches.Count, description)
        };
    }

    public GridCellValueSnapshot ReadCell(GridCellAddress address, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);

        var columnIndex = ResolveColumnIndex(address.ColumnName);
        if (ShouldUseAddressableGrid())
        {
            var snapshot = _addressableGrid!.ReadCell(MapAddressableAddress(address, columnIndex), timeoutMs);
            return NormalizeValueSnapshot(snapshot, _columns[columnIndex]);
        }

        if (_indexedGrid is not null)
        {
            var snapshot = _indexedGrid.ReadCell(MapRow(address.Row), MapColumn(columnIndex), timeoutMs);
            return NormalizeValueSnapshot(snapshot, _columns[columnIndex]);
        }

        if (_addressableGrid is not null)
        {
            var snapshot = _addressableGrid.ReadCell(MapAddressableAddress(address, columnIndex), timeoutMs);
            return NormalizeValueSnapshot(snapshot, _columns[columnIndex]);
        }

        var rowIndex = ResolveUniqueRowIndex(address.Row);
        var row = Inner.GetRowByIndex(rowIndex)
            ?? throw new InvalidOperationException(
                $"Grid '{AutomationId}' row {rowIndex} disappeared while reading '{address.ColumnName}'.");
        if (columnIndex >= row.Cells.Count)
        {
            throw new InvalidOperationException(
                $"Grid '{AutomationId}' column '{address.ColumnName}' resolved to {columnIndex}, "
                + $"but row {rowIndex} exposes only {row.Cells.Count} cells.");
        }

        var cell = row.Cells[columnIndex];
        if (cell is IGridCellValueControl semanticCell)
        {
            return semanticCell.ValueSnapshot;
        }

        return GridCellValueNormalizer.Create(cell.Value, _columns[columnIndex]);
    }

    public string CopyCell(GridCellAddress address, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);

        var columnIndex = ResolveColumnIndex(address.ColumnName);
        if (ShouldUseAddressableGrid())
        {
            return _addressableGrid!.CopyCell(MapAddressableAddress(address, columnIndex), timeoutMs);
        }

        if (_indexedGrid is not null)
        {
            return _indexedGrid.CopyCell(MapRow(address.Row), MapColumn(columnIndex), timeoutMs);
        }

        if (_addressableGrid is not null)
        {
            return _addressableGrid.CopyCell(MapAddressableAddress(address, columnIndex), timeoutMs);
        }

        if (_actionGrid is null)
        {
            return ReadCell(address, timeoutMs).DisplayText ?? string.Empty;
        }

        return _actionGrid.CopyCell(ResolveUniqueRowIndex(address.Row), columnIndex);
    }

    public void EditCell(
        GridCellAddress address,
        GridCellValueEditRequest request,
        int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        if (ShouldUseAddressableGrid())
        {
            var addressableColumn = ResolveColumnIndex(address.ColumnName);
            _addressableGrid!.EditCell(
                MapAddressableAddress(address, addressableColumn),
                request with { EditorParts = request.EditorParts ?? _columns[addressableColumn].EditorParts },
                timeoutMs);
            return;
        }

        if (_indexedGrid is not null)
        {
            var indexedColumn = ResolveColumnIndex(address.ColumnName);
            _indexedGrid.EditCell(
                MapRow(address.Row),
                MapColumn(indexedColumn),
                request with { EditorParts = request.EditorParts ?? _columns[indexedColumn].EditorParts },
                timeoutMs);
            return;
        }

        if (_addressableGrid is not null)
        {
            var addressableColumn = ResolveColumnIndex(address.ColumnName);
            _addressableGrid.EditCell(
                MapAddressableAddress(address, addressableColumn),
                request with { EditorParts = request.EditorParts ?? _columns[addressableColumn].EditorParts },
                timeoutMs);
            return;
        }

        if (_editableGrid is null)
        {
            throw new NotSupportedException(
                $"Grid '{AutomationId}' does not support cell editing.");
        }

        var (rowIndex, columnIndex) = ResolveIndexes(address);
        _editableGrid.EditCell(
            new GridCellEditRequest(
                rowIndex,
                columnIndex,
                request.Value,
                request.EditorKind,
                request.CommitMode,
                request.SearchText)
            {
                TimeoutMs = timeoutMs,
                EditorParts = request.EditorParts ?? _columns[columnIndex].EditorParts
            });
    }

    public void OpenRow(GridRowSelector row, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        if (ShouldUseAddressableGrid())
        {
            _addressableGrid!.OpenRow(MapAddressableRow(row), timeoutMs);
            return;
        }

        if (_indexedGrid is not null)
        {
            _indexedGrid.OpenRow(MapRow(row), timeoutMs);
            return;
        }

        if (_addressableGrid is not null)
        {
            _addressableGrid.OpenRow(MapAddressableRow(row), timeoutMs);
            return;
        }

        if (_actionGrid is null)
        {
            throw new NotSupportedException(
                $"Grid '{AutomationId}' does not support row activation.");
        }

        _actionGrid.OpenRow(ResolveUniqueRowIndex(row));
    }

    private bool ShouldUseAddressableGrid()
    {
        if (_addressableGrid is null)
        {
            return false;
        }

        return _indexedGrid is null
            || Inner is IGridColumnMetadataControl { ColumnNames.Count: > 0 };
    }

    protected int ResolveColumnIndex(string columnName)
    {
        return GridRuntimeResolver.ResolveColumnIndex((IGridColumnMetadataControl)this, columnName);
    }

    protected int ResolveUniqueRowIndex(GridRowSelector row)
    {
        var matches = GridRuntimeResolver.FindMatchingRowIndexes(this, row);
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"Grid row selector '{GridRuntimeResolver.DescribeRowSelector(row)}' matched "
                + $"{matches.Count} rows in grid '{AutomationId}'; expected exactly one.");
        }

        return matches[0];
    }

    private GridIndexedRowSelector MapRow(GridRowSelector row)
    {
        return new GridIndexedRowSelector(
            row.Conditions.Select(condition => new GridIndexedCellCondition(
                MapColumn(ResolveColumnIndex(condition.ColumnName)),
                condition.Value)));
    }

    private GridRowSelector MapAddressableRow(GridRowSelector row)
    {
        var first = row.Conditions[0];
        var mapped = GridRowSelector.ByCell(
            _runtimeColumnNames[ResolveColumnIndex(first.ColumnName)],
            first.Value);
        foreach (var condition in row.Conditions.Skip(1))
        {
            mapped = mapped.AndCell(
                _runtimeColumnNames[ResolveColumnIndex(condition.ColumnName)],
                condition.Value);
        }

        return IsDeclaredIdentitySelector(row)
            ? mapped.WithDeclaredUniqueIdentity()
            : mapped;
    }

    private bool IsDeclaredIdentitySelector(GridRowSelector row)
    {
        return Definition.RowIdentityColumns.Count > 0
            && row.Conditions.Count == Definition.RowIdentityColumns.Count
            && row.Conditions.All(condition => Definition.RowIdentityColumns.Contains(
                condition.ColumnName,
                StringComparer.Ordinal));
    }

    private GridCellAddress MapAddressableAddress(GridCellAddress address, int columnIndex)
    {
        return new GridCellAddress(
            MapAddressableRow(address.Row),
            _runtimeColumnNames[columnIndex]);
    }

    private GridRuntimeColumn MapColumn(int columnIndex)
    {
        var column = _columns[columnIndex];
        return new GridRuntimeColumn(
            columnIndex,
            column.SourceFieldName,
            column.DisplayValuePath,
            column.FormatString,
            column.CultureName,
            column.ValueKind ?? GridCellValueNormalizer.InferValueKind(column.EditorKind),
            column.EditorKind,
            column.EditorParts);
    }

    private (int RowIndex, int ColumnIndex) ResolveIndexes(GridCellAddress address)
    {
        return (ResolveUniqueRowIndex(address.Row), ResolveColumnIndex(address.ColumnName));
    }

    private GridCellValueSnapshot NormalizeValueSnapshot(
        GridCellValueSnapshot snapshot,
        GridColumnDefinition column)
    {
        return GridCellValueNormalizer.Normalize(
            Definition.PagePropertyName,
            snapshot,
            column);
    }
}
