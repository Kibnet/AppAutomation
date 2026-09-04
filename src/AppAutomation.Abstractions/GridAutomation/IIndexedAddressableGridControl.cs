namespace AppAutomation.Abstractions;

/// <summary>Provider SPI for stable operations that must traverse a virtualized grid.</summary>
public interface IIndexedAddressableGridControl : IGridControl
{
    GridRowResolution ResolveRow(GridIndexedRowSelector row, int timeoutMs);

    GridCellValueSnapshot ReadCell(GridIndexedRowSelector row, GridRuntimeColumn column, int timeoutMs);

    string CopyCell(GridIndexedRowSelector row, GridRuntimeColumn column, int timeoutMs);

    void EditCell(
        GridIndexedRowSelector row,
        GridRuntimeColumn column,
        GridCellValueEditRequest request,
        int timeoutMs);

    void OpenRow(GridIndexedRowSelector row, int timeoutMs);
}
