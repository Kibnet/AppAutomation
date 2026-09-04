namespace AppAutomation.Abstractions;

/// <summary>Identifies one cell by a stable row selector and logical column name.</summary>
public sealed record GridCellAddress
{
    public GridCellAddress(GridRowSelector row, string columnName)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        ColumnName = GridAutomationDefinition.NormalizeRequired(columnName, nameof(columnName));
    }

    public GridRowSelector Row { get; }

    public string ColumnName { get; }
}
