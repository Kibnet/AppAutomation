namespace AppAutomation.Abstractions;

/// <summary>
/// Identifies a grid row by one or more exact cell values.
/// </summary>
public sealed class GridRowSelector
{
    private readonly IReadOnlyList<GridRowCondition> _conditions;
    private readonly IReadOnlyDictionary<string, GridColumnDefinition>? _columnDefinitions;

    private GridRowSelector(
        GridRowCondition[] conditions,
        bool hasDeclaredUniqueIdentity = false,
        IReadOnlyDictionary<string, GridColumnDefinition>? columnDefinitions = null)
    {
        _conditions = Array.AsReadOnly(conditions);
        HasDeclaredUniqueIdentity = hasDeclaredUniqueIdentity;
        _columnDefinitions = columnDefinitions;
    }

    /// <summary>
    /// Gets the ordered row conditions.
    /// </summary>
    public IReadOnlyList<GridRowCondition> Conditions => _conditions;

    internal bool HasDeclaredUniqueIdentity { get; }

    /// <summary>
    /// Starts a selector with an exact cell condition.
    /// </summary>
    public static GridRowSelector ByCell(string columnName, string value)
    {
        return new GridRowSelector([CreateCondition(columnName, value)]);
    }

    /// <summary>
    /// Adds another exact cell condition and returns a new selector.
    /// </summary>
    public GridRowSelector AndCell(string columnName, string value)
    {
        var condition = CreateCondition(columnName, value);
        if (_conditions.Any(existing => string.Equals(existing.ColumnName, condition.ColumnName, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"Grid row selector already contains column '{condition.ColumnName}'.",
                nameof(columnName));
        }

        return new GridRowSelector([.. _conditions, condition], HasDeclaredUniqueIdentity, _columnDefinitions);
    }

    internal GridRowSelector WithDeclaredUniqueIdentity()
    {
        return HasDeclaredUniqueIdentity
            ? this
            : new GridRowSelector(_conditions.ToArray(), hasDeclaredUniqueIdentity: true, _columnDefinitions);
    }

    internal GridRowSelector WithColumnDefinitions(IReadOnlyDictionary<string, GridColumnDefinition> definitions)
    {
        return new GridRowSelector(
            _conditions.ToArray(),
            HasDeclaredUniqueIdentity,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, GridColumnDefinition>(
                definitions.ToDictionary(static entry => entry.Key, static entry => entry.Value, StringComparer.Ordinal)));
    }

    internal GridColumnDefinition? FindColumnDefinition(string columnName) =>
        _columnDefinitions is not null && _columnDefinitions.TryGetValue(columnName, out var definition)
            ? definition
            : null;

    private static GridRowCondition CreateCondition(string columnName, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentNullException.ThrowIfNull(value);
        return new GridRowCondition(columnName.Trim(), value);
    }
}

/// <summary>
/// Describes one exact cell condition in a <see cref="GridRowSelector"/>.
/// </summary>
public sealed record GridRowCondition(string ColumnName, string Value);
