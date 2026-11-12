using System.Text.Json;

namespace AppAutomation.Abstractions;

/// <summary>Publishes a grid's complete UI selection in the grid root's UIA ItemStatus.</summary>
public static class GridRowSelectionValue
{
    private const string Prefix = "AppAutomation.GridSelection/";
    private const string Version = Prefix + "1:";

    /// <summary>
    /// Encodes actual selected identities, including virtualized rows. An optional ordered view
    /// lists the current displayed row order only when every entry is selectable by a continuous Shift range.
    /// Use the same logical identity columns as the grid catalog. Republish on view or selection changes.
    /// </summary>
    public static string ToAutomationValue(IEnumerable<GridRowSelector> selectedRows,
        IEnumerable<GridRowSelector>? orderedView = null, bool allowsEmpty = true)
    {
        ArgumentNullException.ThrowIfNull(selectedRows);
        return Version + JsonSerializer.Serialize(new Payload(Encode(selectedRows),
            orderedView is null ? null : Encode(orderedView), allowsEmpty));
    }

    internal static Snapshot? Read(string? value, GridRowSetIdentity identity)
    {
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        if (!value.StartsWith(Version, StringComparison.Ordinal))
            throw new InvalidOperationException("Unsupported UIA grid selection snapshot version.");
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(value[Version.Length..]);
            if (payload?.Selected is null || payload.AllowsEmpty is null)
                throw new InvalidOperationException("The UIA grid selection snapshot must specify the full selection and whether it may be empty.");
            var selected = GridRowSelectionSet.Index(payload.Selected.Select(Decode), "Published grid selection");
            var view = payload.View is null ? null : GridRowSelectionSet.Index(payload.View.Select(Decode), "Published grid view");
            return new Snapshot(selected, view?.Keys.ToArray(), payload.AllowsEmpty.Value);
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("Invalid UIA grid selection snapshot.", error);
        }

        GridRowSelector Decode(GridRowCondition[] conditions)
        {
            if (conditions is null || conditions.Length != identity.LogicalColumns.Count
                || conditions.Any(part => part is null || part.Value is null || !identity.LogicalColumns.Contains(part.ColumnName))
                || conditions.Select(static part => part.ColumnName).Distinct(StringComparer.Ordinal).Count() != conditions.Length)
                throw new InvalidOperationException("A published grid row does not use the configured stable identity columns.");
            var row = GridRowSelector.ByCell(conditions[0].ColumnName, conditions[0].Value);
            foreach (var part in conditions.Skip(1)) row = row.AndCell(part.ColumnName, part.Value);
            return row;
        }
    }

    private static GridRowCondition[][] Encode(IEnumerable<GridRowSelector> rows) =>
        rows.Select(row => row.Conditions.ToArray()).ToArray();

    private sealed record Payload(GridRowCondition[][] Selected, GridRowCondition[][]? View, bool? AllowsEmpty);

    internal sealed record Snapshot(IReadOnlyDictionary<string, GridRowSelector> Selected, string[]? OrderedView, bool AllowsEmpty);
}
