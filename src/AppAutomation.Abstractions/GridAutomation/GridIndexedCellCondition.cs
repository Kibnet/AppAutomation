namespace AppAutomation.Abstractions;

/// <summary>Contains one physical-column condition used by a provider grid traversal.</summary>
public sealed record GridIndexedCellCondition
{
    public GridIndexedCellCondition(GridRuntimeColumn column, string expectedText)
    {
        Column = column ?? throw new ArgumentNullException(nameof(column));
        ExpectedText = expectedText ?? throw new ArgumentNullException(nameof(expectedText));
    }

    public GridRuntimeColumn Column { get; }

    public int ColumnIndex => Column.ColumnIndex;

    public string ExpectedText { get; }
}
