namespace AppAutomation.Abstractions;

/// <summary>Provides stable-address operations for configured grids.</summary>
public interface IAddressableGridControl : IGridControl
{
    GridRowResolution ResolveRow(GridRowSelector row, int timeoutMs);

    GridCellValueSnapshot ReadCell(GridCellAddress address, int timeoutMs);

    string CopyCell(GridCellAddress address, int timeoutMs);

    void EditCell(GridCellAddress address, GridCellValueEditRequest request, int timeoutMs);

    void OpenRow(GridRowSelector row, int timeoutMs);
}
