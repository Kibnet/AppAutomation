namespace AppAutomation.Abstractions;

/// <summary>Identifies one provider row using physical column indexes.</summary>
public sealed record GridIndexedRowSelector
{
    public GridIndexedRowSelector(IEnumerable<GridIndexedCellCondition> conditions)
        : this(conditions, hasDeclaredUniqueIdentity: false)
    {
    }

    private GridIndexedRowSelector(
        IEnumerable<GridIndexedCellCondition> conditions,
        bool hasDeclaredUniqueIdentity)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        var materialized = conditions.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("At least one indexed grid row condition is required.", nameof(conditions));
        }

        if (materialized.Any(static condition => condition.Column.ColumnIndex < 0))
        {
            throw new ArgumentException("Indexed grid row columns cannot be negative.", nameof(conditions));
        }

        if (materialized.Select(static condition => condition.Column.ColumnIndex).Distinct().Count() != materialized.Length)
        {
            throw new ArgumentException("Indexed grid row columns must be distinct.", nameof(conditions));
        }

        Conditions = Array.AsReadOnly(materialized);
        HasDeclaredUniqueIdentity = hasDeclaredUniqueIdentity;
    }

    public IReadOnlyList<GridIndexedCellCondition> Conditions { get; }

    internal bool HasDeclaredUniqueIdentity { get; }

    internal GridIndexedRowSelector WithDeclaredUniqueIdentity()
    {
        return HasDeclaredUniqueIdentity
            ? this
            : new GridIndexedRowSelector(Conditions, hasDeclaredUniqueIdentity: true);
    }
}
