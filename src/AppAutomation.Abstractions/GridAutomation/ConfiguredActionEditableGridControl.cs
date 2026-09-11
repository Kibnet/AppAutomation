namespace AppAutomation.Abstractions;

internal sealed class ConfiguredActionEditableGridControl :
    ConfiguredGridControl,
    IGridUserActionControl,
    IEditableGridControl
{
    private readonly IGridUserActionControl _actionGrid;
    private readonly IEditableGridControl _editableGrid;

    public ConfiguredActionEditableGridControl(
        IGridControl grid,
        IGridUserActionControl actionGrid,
        IEditableGridControl editableGrid,
        GridAutomationDefinition definition,
        IReadOnlyList<GridColumnDefinition> columns,
        string catalogFingerprint)
        : base(grid, editableGrid, actionGrid, definition, columns, catalogFingerprint)
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
