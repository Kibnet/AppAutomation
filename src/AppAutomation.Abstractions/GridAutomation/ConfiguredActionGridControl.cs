namespace AppAutomation.Abstractions;

internal sealed class ConfiguredActionGridControl : ConfiguredGridControl, IGridUserActionControl
{
    private readonly IGridUserActionControl _inner;

    public ConfiguredActionGridControl(
        IGridControl grid,
        IGridUserActionControl inner,
        GridAutomationDefinition definition,
        IReadOnlyList<GridColumnDefinition> columns,
        string catalogFingerprint)
        : base(grid, editableGrid: null, inner, definition, columns, catalogFingerprint)
    {
        _inner = inner;
    }

    public void OpenRow(int rowIndex) => _inner.OpenRow(rowIndex);

    public void SortByColumn(string columnName) => _inner.SortByColumn(columnName);

    public void ScrollToEnd() => _inner.ScrollToEnd();

    public string CopyCell(int rowIndex, int columnIndex) => _inner.CopyCell(rowIndex, columnIndex);

    public void Export() => _inner.Export();
}
