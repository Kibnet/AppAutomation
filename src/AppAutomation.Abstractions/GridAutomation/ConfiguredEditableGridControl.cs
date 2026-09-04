namespace AppAutomation.Abstractions;

internal sealed class ConfiguredEditableGridControl : ConfiguredGridControl, IEditableGridControl
{
    private readonly IEditableGridControl _inner;

    public ConfiguredEditableGridControl(
        IGridControl grid,
        IEditableGridControl inner,
        GridAutomationDefinition definition,
        IReadOnlyList<GridColumnDefinition> columns,
        string catalogFingerprint)
        : base(grid, inner, actionGrid: null, definition, columns, catalogFingerprint)
    {
        _inner = inner;
    }

    public void EditCell(GridCellEditRequest request) => _inner.EditCell(request);
}
