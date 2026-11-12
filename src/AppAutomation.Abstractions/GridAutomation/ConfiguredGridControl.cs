namespace AppAutomation.Abstractions;

internal class ConfiguredGridControl :
    IAddressableGridControl,
    IGridRowSelectionControl,
    IGridRowSetSelectionControl,
    IGridColumnMetadataControl,
    IGridLogicalColumnMetadataControl,
    IGridAutomationCatalogControl,
    IGridHeaderValueControl
{
    private readonly IEditableGridControl? _editableGrid;
    private readonly IGridUserActionControl? _actionGrid;
    private readonly IAddressableGridControl? _addressableGrid;
    private readonly IIndexedAddressableGridControl? _indexedGrid;
    private readonly IReadOnlyList<GridColumnDefinition> _columns;
    private readonly IReadOnlyList<string> _runtimeColumnNames;
    private readonly IReadOnlyList<string> _declaredRuntimeColumnNames;
    private readonly IReadOnlyDictionary<string, GridColumnDefinition> _columnDefinitionMap;
    private IReadOnlyList<string> RuntimeMetadataNames => Inner is IGridColumnMetadataControl metadata
        ? metadata.ColumnNames : Array.Empty<string>();
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
        _runtimeColumnNames = Array.AsReadOnly(columns
            .Select(static column => column.RuntimeColumnName ?? column.SourceFieldName)
            .ToArray());
        _columnDefinitionMap = new System.Collections.ObjectModel.ReadOnlyDictionary<string, GridColumnDefinition>(
            columns.Select((column, index) => (Column: column, RuntimeName: _runtimeColumnNames[index]))
                .ToDictionary(static entry => entry.RuntimeName,
                    static entry => entry.Column,
                    StringComparer.Ordinal));
        _declaredRuntimeColumnNames = definition.Columns.Count > 0
            ? Array.AsReadOnly(definition.Columns
                .Select(static column => column.RuntimeColumnName ?? column.SourceFieldName)
                .ToArray())
            : _runtimeColumnNames;
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

    public IReadOnlyList<string> ColumnNames => RuntimeMetadataNames;

    public IReadOnlyList<GridColumnDefinition> LogicalColumns => _columns;

    public string GridAutomationFingerprint { get; }

    public IGridRowControl? GetRowByIndex(int index) => Inner.GetRowByIndex(index);

    public string ReadHeaderText(GridHeaderSelector selector, int timeoutMs = 5000, GridHeaderContextDefinition? context = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var path = selector.Path.Select(part => selector.Kind == GridHeaderKind.Band ? part : part with
        {
            Name = Definition.FindColumn(part.Name)?.SourceFieldName ?? part.Name
        }).ToArray();
        var metadata = (context ?? Definition.HeaderContext) with
        {
            RuntimeColumnNames = _columns.GroupBy(static column => column.SourceFieldName, StringComparer.Ordinal)
                .Select(static group => (group.Key, Names: group.Select(static column => column.RuntimeColumnName ?? column.SourceFieldName)
                    .Distinct(StringComparer.Ordinal).Take(2).ToArray()))
                .Where(static group => group.Names.Length == 1)
                .ToDictionary(static group => group.Key, static group => group.Names[0], StringComparer.Ordinal)
        };
        return Inner is IGridHeaderValueControl headers
            ? headers.ReadHeaderText(new GridHeaderSelector(selector.Kind, path), timeoutMs, metadata)
            : throw new InvalidOperationException($"Grid '{AutomationId}' does not expose readable header metadata for {selector}.");
    }

    public GridHeaderSelector CaptureFirstGroup(IReadOnlyList<string> fields, int timeoutMs = 5000,
        GridHeaderContextDefinition? context = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        var sourceFields = fields.Select(field => Definition.FindColumn(field)?.SourceFieldName ?? field).ToArray();
        var captured = Inner is IGridHeaderValueControl headers
            ? headers.CaptureFirstGroup(sourceFields, timeoutMs, context ?? Definition.HeaderContext)
            : throw new NotSupportedException($"Grid '{AutomationId}' does not expose group metadata.");
        if (captured.Kind != GridHeaderKind.Group
            || !captured.Path.Select(static part => part.Name).SequenceEqual(sourceFields, StringComparer.Ordinal))
            throw new InvalidOperationException($"Grid '{AutomationId}' returned a different group field path.");
        return new GridHeaderSelector(captured.Kind, captured.Path.Select((part, index) => part with { Name = fields[index] }).ToArray());
    }

    internal GridRowSelector CaptureRowKey(int position, int timeoutMs, bool firstAvailable = false,
        IReadOnlyList<GridRowSelector>? excludingRows = null)
    {
        var identityNames = Definition.RowIdentityColumns.Count > 0
            ? Definition.RowIdentityColumns
            : _columns.Where(static column => column.IsStableIdentityCandidate)
                .Select(static column => column.LogicalName).ToArray();
        if (identityNames.Count == 0)
        {
            throw new InvalidOperationException(
                $"Grid '{AutomationId}' has no stable row identity. Configure IdentifyRowsBy(...).");
        }

        var budget = UiOperationTimeoutBudget.Start(timeoutMs, "capture grid row key");
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var attempts = 0;
        GridRowSelector? lastCandidate = null;
        GridRowResolution? lastResolution = null;
        var indexes = identityNames.Select(ResolveColumnIndex).ToArray();
        var runtimeNames = RuntimeMetadataNames;
        var columns = indexes.Select(index => MapColumn(index, runtimeNames)).ToArray();
        var excluded = (excludingRows ?? []).Select(selector =>
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (selector.Conditions.Count != identityNames.Count
                || identityNames.Any(name => !selector.Conditions.Any(condition => condition.ColumnName == name)))
                throw new ArgumentException("Excluded rows must contain the complete identity of this grid.", nameof(excludingRows));
            return (IReadOnlyList<string?>)identityNames.Select(name =>
                selector.Conditions.Single(condition => condition.ColumnName == name).Value).ToArray();
        }).ToArray();
        try
        {
            return UiWait.Until(CaptureCandidate, static candidate => candidate is not null,
                new UiWaitOptions { Timeout = budget.Remaining, PollInterval = TimeSpan.FromMilliseconds(25) },
                $"Grid '{AutomationId}' did not capture a row that remains present while its unique identity is confirmed.")!;
        }
        catch (TimeoutException error)
        {
            throw new TimeoutException($"Grid '{AutomationId}' could not confirm a current row identity; "
                + $"attempts={attempts}; candidate={(lastCandidate is null ? "<none>" : GridRuntimeResolver.DescribeRowSelector(lastCandidate))}; "
                + $"state={lastResolution?.State.ToString() ?? "<not resolved>"}; "
                + $"remaining={Math.Max(0, timeoutMs - elapsed.ElapsedMilliseconds)}ms; "
                + $"resolution={lastResolution?.Description ?? "<none>"}.", error);
        }

        GridRowSelector? CaptureCandidate()
        {
            _ = budget.Remaining;
            attempts++;
            IReadOnlyList<string?> values;
            var uniquenessProven = false;
            if (Inner is IGridRowPositionIdentityControl positioned)
            {
                var capture = firstAvailable
                    ? excluded.Length == 0
                        ? positioned.ReadFirstAvailableRowIdentity(columns, Definition.CellContext.RowPath, budget.RemainingMilliseconds)
                        : positioned.ReadFirstAvailableRowIdentity(columns, Definition.CellContext.RowPath, excluded, budget.RemainingMilliseconds)
                    : positioned.ReadRowIdentityAtPosition(position, columns, Definition.CellContext.RowPath, budget.RemainingMilliseconds);
                values = capture.Values;
                uniquenessProven = capture.IsUnique;
            }
            else
            {
                IReadOnlyList<string?> ReadIdentity(IGridRowControl row) => Enumerable.Range(0, indexes.Length).Select(keyPart =>
                {
                    var runtimeIndex = columns[keyPart].RuntimeColumnIndex;
                    if (runtimeIndex is not { } cellIndex || cellIndex >= row.Cells.Count)
                    {
                        throw new InvalidOperationException(
                            $"Grid '{AutomationId}' cannot read hidden identity column '{identityNames[keyPart]}' "
                            + "at the selected view position without provider row metadata.");
                    }

                    return GridRuntimeResolver.ReadCellSnapshot(Inner, row.Cells[cellIndex], cellIndex).DisplayText;
                }).ToArray();
                if (firstAvailable && excluded.Length > 0)
                {
                    values = Inner.Rows.Select(ReadIdentity).FirstOrDefault(candidate =>
                        !excluded.Any(previous => previous.SequenceEqual(candidate, StringComparer.Ordinal)))
                        ?? throw new InvalidOperationException($"Grid '{AutomationId}' has no other available data row.");
                }
                else
                {
                    var row = Inner.GetRowByIndex(position)
                        ?? throw new InvalidOperationException(
                            $"Grid '{AutomationId}' has no logical row at current view position {position}.");
                    values = ReadIdentity(row);
                }
            }

            if (values.Count != identityNames.Count)
            {
                throw new InvalidOperationException(
                    $"Grid '{AutomationId}' returned {values.Count} key parts at position {position}; "
                    + $"expected {identityNames.Count}.");
            }

            GridRowSelector? selector = null;
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException(
                        $"Grid '{AutomationId}' row at current view position {position} has no readable "
                        + $"stable key in '{identityNames[index]}'.");
                }

                selector = selector is null
                    ? GridRowSelector.ByCell(identityNames[index], value)
                    : selector.AndCell(identityNames[index], value);
            }

            if (!uniquenessProven)
            {
                lastCandidate = selector;
                lastResolution = null;
                var resolution = ResolveRow(selector!, budget.RemainingMilliseconds);
                lastResolution = resolution;
                if (firstAvailable && resolution.State == GridRowResolutionState.NotFound)
                {
                    _ = budget.Remaining;
                    return null;
                }
                if (resolution.State != GridRowResolutionState.Unique)
                {
                    throw new InvalidOperationException(
                        $"Grid '{AutomationId}' row at current view position {position} has a {resolution.State} "
                        + $"runtime key ({GridRuntimeResolver.DescribeRowSelector(selector!)}): {resolution.Description}");
                }
            }

            _ = budget.Remaining;
            return selector!;
        }
    }

    public bool TryGetColumnIndex(string columnName, out int columnIndex)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            columnIndex = -1;
            return false;
        }

        var normalized = columnName.Trim();
        var matches = RuntimeMetadataNames
            .Select((name, index) => (name, index))
            .Where(candidate => string.Equals(candidate.name, normalized, StringComparison.Ordinal))
            .Select(static candidate => candidate.index)
            .Take(2)
            .ToArray();
        columnIndex = matches.Length == 1 ? matches[0] : -1;
        return matches.Length == 1;
    }

    public bool TryGetLogicalColumnIndex(string columnName, out int columnIndex)
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

        if (ShouldUseIndexedGrid(row))
        {
            return _indexedGrid!.ResolveRow(MapRow(row), timeoutMs);
        }

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
        IReadOnlyList<string> runtimeNames = _indexedGrid is not null ? RuntimeMetadataNames : [];
        if (ShouldUseIndexedGrid(address.Row, columnIndex, runtimeNames))
        {
            var snapshot = _indexedGrid!.ReadCell(
                MapRow(address.Row, runtimeNames), MapColumn(columnIndex, runtimeNames), timeoutMs);
            return NormalizeValueSnapshot(snapshot, _columns[columnIndex]);
        }

        if (ShouldUseAddressableGrid(runtimeNames))
        {
            var snapshot = _addressableGrid!.ReadCell(MapAddressableAddress(address, columnIndex), timeoutMs);
            return NormalizeValueSnapshot(snapshot, _columns[columnIndex]);
        }

        if (_indexedGrid is not null)
        {
            var snapshot = _indexedGrid.ReadCell(
                MapRow(address.Row, runtimeNames), MapColumn(columnIndex, runtimeNames), timeoutMs);
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

        return GridRuntimeResolver.ReadCellSnapshot(this, row.Cells[columnIndex], columnIndex);
    }

    public string CopyCell(GridCellAddress address, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);

        var columnIndex = ResolveColumnIndex(address.ColumnName);
        var budget = UiOperationTimeoutBudget.Start(timeoutMs, "copy grid cell");
        IReadOnlyList<string> runtimeNames = _indexedGrid is not null ? RuntimeMetadataNames : [];
        if (ShouldUseIndexedGrid(address.Row, columnIndex, runtimeNames))
        {
            var mappedRow = MapRow(address.Row, runtimeNames);
            var mappedColumn = MapColumn(columnIndex, runtimeNames);
            var copied = _indexedGrid!.CopyCell(mappedRow, mappedColumn, budget.RemainingMilliseconds);
            return NormalizeCopiedValue(copied, _columns[columnIndex]);
        }

        if (ShouldUseAddressableGrid(runtimeNames))
        {
            var mappedAddress = MapAddressableAddress(address, columnIndex);
            var copied = _addressableGrid!.CopyCell(mappedAddress, budget.RemainingMilliseconds);
            return NormalizeCopiedValue(copied, _columns[columnIndex]);
        }

        if (_indexedGrid is not null)
        {
            var mappedRow = MapRow(address.Row, runtimeNames);
            var mappedColumn = MapColumn(columnIndex, runtimeNames);
            var copied = _indexedGrid.CopyCell(mappedRow, mappedColumn, budget.RemainingMilliseconds);
            return NormalizeCopiedValue(copied, _columns[columnIndex]);
        }

        if (_addressableGrid is not null)
        {
            var mappedAddress = MapAddressableAddress(address, columnIndex);
            var copied = _addressableGrid.CopyCell(mappedAddress, budget.RemainingMilliseconds);
            return NormalizeCopiedValue(copied, _columns[columnIndex]);
        }

        if (_actionGrid is null)
        {
            return ReadCell(address, timeoutMs).DisplayText ?? string.Empty;
        }

        var actionCopied = _actionGrid.CopyCell(ResolveUniqueRowIndex(address.Row), columnIndex);
        return NormalizeCopiedValue(actionCopied, _columns[columnIndex]);
    }

    public void EditCell(
        GridCellAddress address,
        GridCellValueEditRequest request,
        int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        GridCellEditRequestValidation.Validate(request);
        var columnIndex = ResolveColumnIndex(address.ColumnName);
        IReadOnlyList<string> runtimeNames = _indexedGrid is not null ? RuntimeMetadataNames : [];
        if (ShouldUseIndexedGrid(address.Row, columnIndex, runtimeNames))
        {
            _indexedGrid!.EditCell(
                MapRow(address.Row, runtimeNames),
                MapColumn(columnIndex, runtimeNames),
                request with { EditorParts = request.EditorParts ?? _columns[columnIndex].EditorParts },
                timeoutMs);
            return;
        }

        if (ShouldUseAddressableGrid(runtimeNames))
        {
            _addressableGrid!.EditCell(
                MapAddressableAddress(address, columnIndex),
                request with { EditorParts = request.EditorParts ?? _columns[columnIndex].EditorParts },
                timeoutMs);
            return;
        }

        if (_indexedGrid is not null)
        {
            _indexedGrid.EditCell(
                MapRow(address.Row, runtimeNames),
                MapColumn(columnIndex, runtimeNames),
                request with { EditorParts = request.EditorParts ?? _columns[columnIndex].EditorParts },
                timeoutMs);
            return;
        }

        if (_addressableGrid is not null)
        {
            _addressableGrid.EditCell(
                MapAddressableAddress(address, columnIndex),
                request with { EditorParts = request.EditorParts ?? _columns[columnIndex].EditorParts },
                timeoutMs);
            return;
        }

        if (_editableGrid is null)
        {
            throw new NotSupportedException(
                $"Grid '{AutomationId}' does not support cell editing.");
        }

        var rowIndex = ResolveUniqueRowIndex(address.Row);
        _editableGrid.EditCell(GridCellEditRequestValidation.CreateIndexedRequest(
            rowIndex,
            columnIndex,
            request,
            timeoutMs,
            request.EditorParts ?? _columns[columnIndex].EditorParts));
    }

    public void OpenRow(GridRowSelector row, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        if (ShouldUseIndexedGrid(row))
        {
            _indexedGrid!.OpenRow(MapRow(row), timeoutMs);
            return;
        }

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

    public void SelectRow(GridRowSelector row, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        if (!IsDeclaredIdentitySelector(row))
        {
            throw new InvalidOperationException(
                $"Grid '{AutomationId}' row selection requires exactly the stable identity columns declared by IdentifyRowsBy(...). "
                + $"Expected: [{string.Join(", ", Definition.RowIdentityColumns)}]; "
                + $"actual: [{string.Join(", ", row.Conditions.Select(static condition => condition.ColumnName))}].");
        }

        if (ShouldUseIndexedGrid(row)
            && Inner is IIndexedGridRowSelectionControl indexedSelection)
        {
            indexedSelection.SelectRow(MapRow(row), timeoutMs);
            return;
        }

        if (ShouldUseAddressableGrid()
            && Inner is IGridRowSelectionControl addressableSelection)
        {
            addressableSelection.SelectRow(MapAddressableRow(row), timeoutMs);
            return;
        }

        if (Inner is IIndexedGridRowSelectionControl indexedFallback)
        {
            indexedFallback.SelectRow(MapRow(row), timeoutMs);
            return;
        }

        if (Inner is IGridRowSelectionControl addressableFallback)
        {
            addressableFallback.SelectRow(MapAddressableRow(row), timeoutMs);
            return;
        }

        throw new NotSupportedException(
            $"Grid '{AutomationId}' does not expose provider-neutral row selection.");
    }

    public void SetSelectedRows(IReadOnlyList<GridRowSelector> rows, int timeoutMs)
    {
        var budget = UiOperationTimeoutBudget.Start(timeoutMs, "replace grid row selection");
        ValidateRowSetSelectors(rows);
        var expected = GridRowSelectionSet.Index(rows, "Requested grid selection");
        RequireRowSetRuntime().SetSelectedRows(CreateRowSetIdentity(), expected, budget);
    }

    public IReadOnlyList<GridRowSelector> ReadSelectedRows(int timeoutMs)
    {
        var budget = UiOperationTimeoutBudget.Start(timeoutMs, "read complete grid row selection");
        return RequireRowSetRuntime().ReadSelectedRows(CreateRowSetIdentity(), budget);
    }

    internal void ValidateRowSetSelectors(IReadOnlyList<GridRowSelector> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (Definition.RowIdentityColumns.Count == 0 || rows.Any(row => !IsDeclaredIdentitySelector(row)))
            throw new InvalidOperationException($"Grid '{AutomationId}' requires the exact stable columns declared by IdentifyRowsBy(...) for every selected row.");
    }

    private IGridRowSetRuntime RequireRowSetRuntime() => Inner as IGridRowSetRuntime
        ?? throw new NotSupportedException($"Grid '{AutomationId}' does not expose complete row selection.");

    private GridRowSetIdentity CreateRowSetIdentity()
    {
        if (Definition.RowIdentityColumns.Count == 0)
            throw new NotSupportedException($"Grid '{AutomationId}' needs IdentifyRowsBy(...) to read the complete selection.");
        var names = RuntimeMetadataNames;
        var columns = Definition.RowIdentityColumns.Select(name => MapColumn(ResolveColumnIndex(name), names)).ToArray();
        return new GridRowSetIdentity(Definition.RowIdentityColumns, columns, Definition.CellContext.RowPath);
    }

    private bool ShouldUseAddressableGrid(IReadOnlyList<string>? runtimeNames = null)
    {
        if (_addressableGrid is null)
        {
            return false;
        }

        return _indexedGrid is null || (runtimeNames ?? RuntimeMetadataNames).Count > 0;
    }

    private bool ShouldUseIndexedGrid(GridRowSelector row, int? targetColumnIndex = null,
        IReadOnlyList<string>? runtimeNames = null)
    {
        if (_indexedGrid is null) return false;
        var names = runtimeNames ?? RuntimeMetadataNames;
        return row.Conditions.Any(RequiresIndexedAccess)
            || targetColumnIndex is { } columnIndex && FindRuntimeColumnIndex(_columns[columnIndex], names) is null;

        bool RequiresIndexedAccess(GridRowCondition condition)
        {
            var column = _columns[ResolveColumnIndex(condition.ColumnName)];
            return column.RowIdentityAutomationProperty is not null || FindRuntimeColumnIndex(column, names) is null;
        }
    }

    protected int ResolveColumnIndex(string columnName)
    {
        if (TryGetLogicalColumnIndex(columnName, out var columnIndex))
        {
            return columnIndex;
        }

        throw new InvalidOperationException(
            $"Grid logical column '{columnName}' is not configured. Available logical columns: "
            + string.Join(", ", _columns.Select(static column => column.LogicalName)) + ".");
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

    private GridIndexedRowSelector MapRow(GridRowSelector row, IReadOnlyList<string>? runtimeNames = null)
    {
        var names = runtimeNames ?? RuntimeMetadataNames;
        var mapped = new GridIndexedRowSelector(
            row.Conditions.Select(condition => new GridIndexedCellCondition(
                MapColumn(ResolveColumnIndex(condition.ColumnName), names),
                condition.Value)));

        return IsDeclaredIdentitySelector(row)
            ? mapped.WithDeclaredUniqueIdentity()
            : mapped;
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

        mapped = mapped.WithColumnDefinitions(row.Conditions.ToDictionary(
            condition => _runtimeColumnNames[ResolveColumnIndex(condition.ColumnName)],
            condition => _columns[ResolveColumnIndex(condition.ColumnName)],
            StringComparer.Ordinal));

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
        var row = MapAddressableRow(address.Row);
        return new GridCellAddress(
            row.WithColumnDefinitions(_columnDefinitionMap),
            _runtimeColumnNames[columnIndex]);
    }

    private GridRuntimeColumn MapColumn(int columnIndex, IReadOnlyList<string> runtimeNames)
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
            column.EditorParts)
        {
            CellContext = Definition.CellContext,
            DeclaredRuntimeColumnNames = _declaredRuntimeColumnNames,
            RuntimeColumnIndex = FindRuntimeColumnIndex(column, runtimeNames),
            RuntimeColumnName = column.RuntimeColumnName ?? column.SourceFieldName,
            RowIdentityAutomationProperty = column.RowIdentityAutomationProperty,
            BooleanTrueDisplayText = column.BooleanTrueDisplayText,
            BooleanFalseDisplayText = column.BooleanFalseDisplayText
        };
    }

    private static int? FindRuntimeColumnIndex(GridColumnDefinition column, IReadOnlyList<string> runtimeNames)
    {
        var runtimeName = string.IsNullOrWhiteSpace(column.RuntimeColumnName) ? column.SourceFieldName : column.RuntimeColumnName;
        var matches = runtimeNames
            .Select((name, index) => (name, index))
            .Where(candidate => string.Equals(candidate.name, runtimeName, StringComparison.Ordinal))
            .Select(static candidate => candidate.index)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
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

    private string NormalizeCopiedValue(string copied, GridColumnDefinition column)
    {
        if (!string.IsNullOrWhiteSpace(column.DisplayValuePath))
        {
            return copied;
        }

        return NormalizeValueSnapshot(
                new GridCellValueSnapshot(copied, copied, column.ValueKind ?? GridCellValueKind.Text),
                column)
            .DisplayText ?? string.Empty;
    }

    internal GridCellValueSnapshot NormalizeValueSnapshot(GridCellValueSnapshot snapshot, int columnIndex) =>
        NormalizeValueSnapshot(snapshot, _columns[columnIndex]);
}
