namespace AppAutomation.Abstractions;

/// <summary>Optionally exposes a typed and null-aware value for one runtime grid cell.</summary>
public interface IGridCellValueControl : IGridCellControl
{
    GridCellValueSnapshot ValueSnapshot { get; }
}
