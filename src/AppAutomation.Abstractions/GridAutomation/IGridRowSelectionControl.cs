namespace AppAutomation.Abstractions;

/// <summary>Optional capability for selecting one logical grid row by a stable selector.</summary>
public interface IGridRowSelectionControl : IGridControl
{
    /// <summary>Selects the uniquely matching row and confirms the resulting selection state.</summary>
    void SelectRow(GridRowSelector row, int timeoutMs);
}

/// <summary>Provider SPI for selecting one row while traversing a virtualized grid.</summary>
public interface IIndexedGridRowSelectionControl : IGridControl
{
    /// <summary>Selects the uniquely matching runtime row and confirms the resulting selection state.</summary>
    void SelectRow(GridIndexedRowSelector row, int timeoutMs);
}
