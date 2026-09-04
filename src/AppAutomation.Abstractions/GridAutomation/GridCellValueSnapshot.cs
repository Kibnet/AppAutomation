namespace AppAutomation.Abstractions;

/// <summary>Contains a null-aware semantic and displayed value for one grid cell.</summary>
public sealed record GridCellValueSnapshot(
    string? DisplayText,
    object? RawValue = null,
    GridCellValueKind ValueKind = GridCellValueKind.Text)
{
    public object? ValueSource { get; init; }

    public string? CultureName { get; init; }

    public bool IsNull => RawValue is null && DisplayText is null;
}
