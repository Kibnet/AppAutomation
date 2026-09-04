namespace AppAutomation.Abstractions;

/// <summary>Describes structural DataContext paths used to read a cell without a provider reference.</summary>
public sealed record GridCellContextDefinition
{
    public GridCellContextDefinition(
        string rowPath,
        string fieldNamePath,
        string valuePath)
    {
        RowPath = GridPropertyPath.Normalize(rowPath, nameof(rowPath));
        FieldNamePath = GridPropertyPath.Normalize(fieldNamePath, nameof(fieldNamePath));
        ValuePath = GridPropertyPath.Normalize(valuePath, nameof(valuePath));
    }

    public string RowPath { get; }

    public string FieldNamePath { get; }

    public string ValuePath { get; }

    public static GridCellContextDefinition Default { get; } = new("Row", "Column.FieldName", "Value");

    public static GridCellContextDefinition Structural(
        string rowPath,
        string fieldNamePath,
        string valuePath)
    {
        return new GridCellContextDefinition(rowPath, fieldNamePath, valuePath);
    }
}
