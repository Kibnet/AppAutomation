using System.Collections.ObjectModel;

namespace AppAutomation.Abstractions;

internal static class GridAutomationColumnResolver
{
    public static IReadOnlyList<GridColumnDefinition> Resolve(
        IGridControl grid,
        GridAutomationDefinition definition)
    {
        if (grid is IGridColumnMetadataControl metadata && metadata.ColumnNames.Count > 0)
        {
            return MergeRuntimeAndConfiguredColumns(metadata.ColumnNames, definition);
        }

        if (definition.Columns.Count > 0)
        {
            return definition.Columns;
        }

        throw new InvalidOperationException(
            $"Grid '{definition.PagePropertyName}' does not expose native column metadata. "
            + "Register its columns in GridAutomationDefinition or provide a runtime grid metadata adapter.");
    }

    private static ReadOnlyCollection<GridColumnDefinition> MergeRuntimeAndConfiguredColumns(
        IReadOnlyList<string> runtimeColumnNames,
        GridAutomationDefinition definition)
    {
        var remaining = definition.Columns.ToList();
        var merged = new List<GridColumnDefinition>(runtimeColumnNames.Count + remaining.Count);
        foreach (var runtimeName in runtimeColumnNames)
        {
            var matches = remaining
                .Where(column =>
                    string.Equals(column.LogicalName, runtimeName, StringComparison.Ordinal)
                    || string.Equals(column.SourceFieldName, runtimeName, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Grid '{definition.PagePropertyName}' runtime column '{runtimeName}' matches multiple configured "
                    + $"logical/source columns: {string.Join(", ", matches.Select(static column => $"{column.LogicalName}->{column.SourceFieldName}"))}.");
            }

            var configured = matches.SingleOrDefault();
            if (configured is null)
            {
                merged.Add(GridColumnDefinition.Auto(runtimeName));
                continue;
            }

            merged.Add(configured);
            remaining.Remove(configured);
        }

        if (remaining.Count > 0)
        {
            throw new InvalidOperationException(
                $"Grid '{definition.PagePropertyName}' does not expose configured source columns: "
                + $"{string.Join(", ", remaining.Select(static column => column.SourceFieldName))}.");
        }

        var duplicateLogicalName = merged
            .GroupBy(static column => column.LogicalName, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateLogicalName is not null)
        {
            throw new InvalidOperationException(
                $"Grid '{definition.PagePropertyName}' resolves logical column "
                + $"'{duplicateLogicalName.Key}' more than once.");
        }

        return Array.AsReadOnly(merged.ToArray());
    }
}
