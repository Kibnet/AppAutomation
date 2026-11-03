using System.Collections;
using System.Globalization;

namespace AppAutomation.Abstractions;

internal sealed record GridHeaderNode(object Source, double Top, string? Text,
    string? RuntimeAddress = null, string? RuntimeText = null);
internal sealed record GridHeaderMetadata(object Source, GridHeaderSelector? Selector, string? Text, string? Error);

/// <summary>Общий разбор публичных метаданных: visual handles не переживают один обход.</summary>
internal static class GridHeaderMetadataReader
{
    private static readonly string[] ColumnFieldPaths = ["FieldName", "SortMemberPath", "Binding.Path"];
    private static readonly string[] BandNamePaths = ["BandName", "Name", "Header"];

    public static bool IsHeader(object source, GridHeaderContextDefinition context)
    {
        var role = Read(source, "HeaderKind")?.ToString() ?? source.GetType().Name;
        if (role.EndsWith("Panel", StringComparison.Ordinal) || role.EndsWith("Presenter", StringComparison.Ordinal)
            || role.Contains("Headers", StringComparison.Ordinal)) return false;
        return role.Contains("ColumnHeader", StringComparison.Ordinal)
            || role.Contains("BandHeader", StringComparison.Ordinal)
            || role.Contains("GroupRow", StringComparison.Ordinal)
            || role.Contains("RowGroupHeader", StringComparison.Ordinal)
            || role is "Column" or "Band" or "Group"
            || GridPropertyValueReader.TryReadPath(source, context.GroupValuePath, out _);
    }

    public static IReadOnlyList<GridHeaderMetadata> Read(
        IReadOnlyList<GridHeaderNode> nodes, GridHeaderContextDefinition context, object? grid = null)
    {
        var result = new List<GridHeaderMetadata>();
        var viewPaths = ReadCollectionViewGroupPaths(grid);
        var bandPaths = ReadBandPaths(grid);
        var groupPath = new List<GridHeaderPathPart>();
        double? previousGroupTop = null;
        foreach (var node in nodes.OrderBy(static n => n.Top))
        {
            try
            {
                if (GridHeaderSelector.FromAutomationValue(node.RuntimeAddress) is { } address)
                {
                    result.Add(new(node.Source, address, node.RuntimeText, null));
                    groupPath.Clear();
                    previousGroupTop = null;
                    continue;
                }
            }
            catch (InvalidOperationException ex)
            {
                result.Add(new(node.Source, null, null, ex.Message));
                groupPath.Clear();
                previousGroupTop = null;
                continue;
            }
            var headerData = Read(node.Source, "Band") ?? Read(node.Source, "Column")
                ?? Read(node.Source, "DataContext.Column") ?? Read(node.Source, "DataContext");
            if (headerData is not null && bandPaths.TryGetValue(headerData, out var bandPath))
            {
                result.Add(new(node.Source, new(GridHeaderKind.Band, bandPath),
                    node.Text ?? Read(headerData, "Header") as string, null));
                continue;
            }
            if (Read(node.Source, "DataContext") is { } group && viewPaths.TryGetValue(group, out var viewPath))
            {
                result.Add(new(node.Source, new(GridHeaderKind.Group, viewPath),
                    Read(node.Source, context.GroupTextPath) as string ?? node.Text, null));
                continue;
            }
            if (TryGroup(node.Source, context, out var part, out var text, out var level))
            {
                string? error = null;
                GridHeaderSelector? selector = null;
                if (context.ParentGroupPath is { Length: > 0 } parentPath)
                {
                    var path = new List<GridHeaderPathPart> { part! };
                    var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { node.Source };
                    var current = node.Source;
                    while (Read(current, parentPath) is { } parent)
                    {
                        if (!visited.Add(parent) || !TryGroup(parent, context, out var parentPart, out _, out _))
                        {
                            error = "The group parent path is cyclic or does not expose group metadata.";
                            break;
                        }
                        path.Insert(0, parentPart!);
                        current = parent;
                    }
                    if (error is null && path.Count != level + 1)
                        error = "The complete parent group path is unavailable.";
                    if (error is null) selector = new(GridHeaderKind.Group, path);
                }
                else if (level < 0 || level > groupPath.Count || !double.IsFinite(node.Top)
                    || previousGroupTop == node.Top)
                {
                    groupPath.Clear();
                    error = "The complete ordered parent group path is unavailable; publish parent group metadata.";
                }
                else
                {
                    groupPath.RemoveRange(level, groupPath.Count - level);
                    groupPath.Add(part!);
                    selector = new(GridHeaderKind.Group, groupPath);
                }
                previousGroupTop = node.Top;
                result.Add(new(node.Source, selector, text ?? node.Text, error));
                continue;
            }

            var role = Read(node.Source, "HeaderKind")?.ToString() ?? node.Source.GetType().Name;
            if (role.Contains("GroupRow", StringComparison.Ordinal) || role.Contains("RowGroupHeader", StringComparison.Ordinal)
                || role == nameof(GridHeaderKind.Group))
            {
                result.Add(new(node.Source, null, node.Text, "The group header does not expose its field, scalar key and level."));
            }
            else if (role.Contains("ColumnHeader", StringComparison.Ordinal) || role == nameof(GridHeaderKind.Column))
            {
                var column = Read(node.Source, "Column") ?? Read(node.Source, "DataContext.Column")
                    ?? Read(node.Source, "DataContext");
                var field = column is null ? null : ReadName(column, ColumnFieldPaths);
                if (field is null && grid is not null && Read(grid, "Columns") is IEnumerable columns)
                {
                    var caption = node.Text ?? Read(node.Source, "Content") as string;
                    var matches = caption is null ? [] : columns.Cast<object>()
                        .Where(c => string.Equals(Read(c, "Header") as string, caption, StringComparison.Ordinal)).Take(2).ToArray();
                    if (matches.Length == 1) field = ReadName(matches[0], ColumnFieldPaths);
                }
                if (!string.IsNullOrWhiteSpace(field))
                    result.Add(new(node.Source, GridHeaderSelector.Column(field), node.Text ?? Read(column!, "Header") as string, null));
                else
                    result.Add(new(node.Source, null, null, "The column header does not expose its source field."));
            }
            else if (role.Contains("BandHeader", StringComparison.Ordinal) || role == nameof(GridHeaderKind.Band))
            {
                var band = Read(node.Source, "Band") ?? Read(node.Source, "DataContext");
                var path = new List<GridHeaderPathPart>();
                var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
                while (band is not null && visited.Add(band))
                {
                    var name = ReadName(band, BandNamePaths);
                    if (string.IsNullOrWhiteSpace(name)) break;
                    path.Insert(0, new(name));
                    band = Read(band, "ParentBand");
                }
                result.Add(path.Count > 0 && band is null
                    ? new(node.Source, new(GridHeaderKind.Band, path), node.Text ?? path[^1].Name, null)
                    : new(node.Source, null, null, "The complete band header path is unavailable."));
            }
        }
        return result;
    }

    public static GridHeaderSelector? FirstGroup(IReadOnlyList<GridHeaderMetadata> headers, IReadOnlyList<string> fields, string grid)
    {
        var first = headers.FirstOrDefault(header => header.Selector is { Kind: GridHeaderKind.Group } selector
            && selector.Path.Select(static part => part.Name).SequenceEqual(fields, StringComparer.Ordinal));
        if (first?.Selector is not { } address)
        {
            return null;
        }
        _ = ResolveText(headers, address, grid);
        return address;
    }

    public static string ResolveText(IReadOnlyList<GridHeaderMetadata> headers, GridHeaderSelector selector, string grid)
    {
        var matches = headers.Where(h => selector.Equals(h.Selector)).Take(2).ToArray();
        if (matches.Length != 1)
        {
            var detail = headers.FirstOrDefault(static h => h.Error is not null)?.Error;
            throw new InvalidOperationException($"Grid '{grid}' header '{selector}' matched {matches.Length} readable headers; exactly one is required. "
                + (detail ?? "The header is not materialized or its complete metadata is not published."));
        }
        return matches[0].Text ?? throw new InvalidOperationException($"Grid '{grid}' header '{selector}' does not expose displayed text.");
    }

    private static bool TryGroup(object source, GridHeaderContextDefinition context,
        out GridHeaderPathPart? part, out string? text, out int level)
    {
        part = null;
        text = null;
        level = -1;
        var hasValue = GridPropertyValueReader.TryReadPath(source, context.GroupValuePath, out var value);
        var field = Read(source, context.GroupFieldPath) as string;
        if (!hasValue)
        {
            // Стандартный group header публикует ключ через collection-view group.
            hasValue = GridPropertyValueReader.TryReadPath(source, "DataContext.Key", out value);
            field = Read(source, "PropertyName") as string;
        }
        if (!hasValue || string.IsNullOrWhiteSpace(field)) return false;
        var rawLevel = Read(source, context.GroupLevelPath) ?? Read(source, "Sublevel");
        if (rawLevel is int index) level = index;
        text = Read(source, context.GroupTextPath) as string ?? Read(source, "PropertyValue") as string;
        return TryCreatePart(field, value, out part);
    }

    private static bool TryCreatePart(string field, object? value, out GridHeaderPathPart? part)
    {
        part = null;
        var kind = value switch
        {
            bool => GridCellValueKind.Boolean,
            DateTime or DateTimeOffset => GridCellValueKind.Date,
            TimeSpan => GridCellValueKind.Time,
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => GridCellValueKind.Number,
            _ => GridCellValueKind.Text
        };
        var key = value switch
        {
            null => null,
            string s => s,
            bool b => b ? "true" : "false",
            DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan t => t.ToString("c", CultureInfo.InvariantCulture),
            Guid g => g.ToString("D"),
            Enum e => e.ToString(),
            IFormattable f when kind == GridCellValueKind.Number => f.ToString(null, CultureInfo.InvariantCulture),
            _ => null
        };
        if (value is not null && key is null) return false;
        part = new(field, key, kind);
        return true;
    }

    private static Dictionary<object, IReadOnlyList<GridHeaderPathPart>> ReadCollectionViewGroupPaths(object? grid)
    {
        var result = new Dictionary<object, IReadOnlyList<GridHeaderPathPart>>(ReferenceEqualityComparer.Instance);
        if (grid is null) return result;
        var view = Read(grid, "CollectionView") ?? Read(grid, "ItemsSource");
        if (view is null || Read(view, "Groups") is not IEnumerable groups
            || Read(view, "GroupDescriptions") is not IEnumerable descriptions) return result;
        var fields = descriptions.Cast<object>().Select(d => Read(d, "PropertyName") as string).ToArray();
        void Visit(object group, IReadOnlyList<GridHeaderPathPart> parent)
        {
            if (parent.Count >= fields.Length || fields[parent.Count] is not { Length: > 0 } field || result.ContainsKey(group)
                || !GridPropertyValueReader.TryReadProperty(group, "Key", out var key)
                || !TryCreatePart(field, key, out var part)) return;
            var path = parent.Append(part!).ToArray();
            result.Add(group, path);
            if (Read(group, "IsBottomLevel") is false && Read(group, "Items") is IEnumerable children)
                foreach (var child in children.Cast<object>()) Visit(child, path);
        }
        foreach (var group in groups.Cast<object>()) Visit(group, []);
        return result;
    }

    private static Dictionary<object, IReadOnlyList<GridHeaderPathPart>> ReadBandPaths(object? grid)
    {
        var result = new Dictionary<object, IReadOnlyList<GridHeaderPathPart>>(ReferenceEqualityComparer.Instance);
        if (grid is null || Read(grid, "Bands") is not IEnumerable bands) return result;
        void Visit(object band, IReadOnlyList<GridHeaderPathPart> parent)
        {
            var name = ReadName(band, BandNamePaths);
            if (string.IsNullOrWhiteSpace(name) || result.ContainsKey(band)) return;
            var path = parent.Append(new GridHeaderPathPart(name)).ToArray();
            result.Add(band, path);
            if (Read(band, "Bands") is IEnumerable children)
                foreach (var child in children.Cast<object>()) Visit(child, path);
        }
        foreach (var band in bands.Cast<object>()) Visit(band, []);
        return result;
    }

    private static string? ReadName(object source, IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
            if (Read(source, path) is string name && !string.IsNullOrWhiteSpace(name)) return name.Trim();
        return null;
    }

    private static object? Read(object source, string path) =>
        GridPropertyValueReader.TryReadPath(source, path, out var value) ? value : null;
}
