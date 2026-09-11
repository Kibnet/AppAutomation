namespace AppAutomation.Abstractions;

/// <summary>Contains a null-aware semantic and displayed value for one grid cell.</summary>
public sealed record GridCellValueSnapshot(
    string? DisplayText,
    object? RawValue = null,
    GridCellValueKind ValueKind = GridCellValueKind.Text)
{
    public object? ValueSource { get; init; }

    public string? CultureName { get; init; }

    /// <summary>
    /// Indicates that the provider already read the displayed UI value and has no model source.
    /// Declarative display projection and formatting must not be applied again; typed conversion
    /// still uses the configured value kind and culture.
    /// </summary>
    public bool IsDisplayOnly { get; init; }

    public bool IsNull => RawValue is null && DisplayText is null;
}
