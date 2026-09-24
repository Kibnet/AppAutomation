namespace AppAutomation.Abstractions;

/// <summary>
/// Separates the catalog's complete logical schema from the provider's visible runtime column metadata.
/// </summary>
internal interface IGridLogicalColumnMetadataControl
{
    IReadOnlyList<GridColumnDefinition> LogicalColumns { get; }

    bool TryGetLogicalColumnIndex(string columnName, out int columnIndex);
}
