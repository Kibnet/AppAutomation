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
                    column.RuntimeColumnName is not null
                        ? string.Equals(column.RuntimeColumnName, runtimeName, StringComparison.Ordinal)
                        : string.Equals(column.LogicalName, runtimeName, StringComparison.Ordinal)
                          || string.Equals(column.SourceFieldName, runtimeName, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Grid '{definition.PagePropertyName}' runtime column '{runtimeName}' matches multiple configured "
                    + $"logical/source/runtime columns: {string.Join(", ", matches.Select(static column => $"{column.LogicalName}->{column.SourceFieldName}->{column.RuntimeColumnName}"))}.");
            }

            var configured = matches.SingleOrDefault();
            if (configured is null)
            {
                var strictMismatch = remaining.FirstOrDefault(column =>
                    column.RuntimeColumnName is not null
                    && (string.Equals(column.LogicalName, runtimeName, StringComparison.Ordinal)
                        || string.Equals(column.SourceFieldName, runtimeName, StringComparison.Ordinal)));
                if (strictMismatch is not null)
                {
                    throw new InvalidOperationException(
                        $"Grid '{definition.PagePropertyName}' explicitly maps logical column "
                        + $"'{strictMismatch.LogicalName}' to runtime column '{strictMismatch.RuntimeColumnName}', "
                        + $"but the provider exposed '{runtimeName}' instead.");
                }

                merged.Add(GridColumnDefinition.Auto(runtimeName));
                continue;
            }

            merged.Add(configured.BindRuntimeColumn(runtimeName));
            remaining.Remove(configured);
        }

        // Runtime metadata normally contains only the columns from the current user layout.
        // Keep configured hidden columns in the logical schema and resolve their provider access
        // only when an operation actually addresses one of them.
        merged.AddRange(remaining);

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
