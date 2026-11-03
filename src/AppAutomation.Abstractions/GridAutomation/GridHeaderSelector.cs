using System.Text.Json.Serialization;

namespace AppAutomation.Abstractions;

/// <summary>Вид содержательного заголовка таблицы.</summary>
public enum GridHeaderKind { Column, Group, Band }

/// <summary>Часть полного пути: поле/полоса и типизированный ключ группы.</summary>
public sealed record GridHeaderPathPart(string Name, string? Value = null, GridCellValueKind ValueKind = GridCellValueKind.Text);

/// <summary>Адрес заголовка без зависимости от визуального индекса.</summary>
public sealed class GridHeaderSelector : IEquatable<GridHeaderSelector>
{
    internal const string AutomationValuePrefix = "AppAutomation.GridHeader/1:";

    [JsonConstructor]
    public GridHeaderSelector(GridHeaderKind kind, IReadOnlyList<GridHeaderPathPart> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Enum.IsDefined(kind) || path.Count == 0 || kind == GridHeaderKind.Column && path.Count != 1)
            throw new ArgumentException("A header requires a valid kind and a nonempty complete path.", nameof(path));
        foreach (var part in path)
        {
            ArgumentNullException.ThrowIfNull(part);
            ArgumentException.ThrowIfNullOrWhiteSpace(part.Name);
            if (!Enum.IsDefined(part.ValueKind))
                throw new ArgumentException("The group key has an unsupported value kind.", nameof(path));
            if (kind != GridHeaderKind.Group && (part.Value is not null || part.ValueKind != GridCellValueKind.Text))
                throw new ArgumentException("Only a group header can contain a typed value key.", nameof(path));
        }
        Kind = kind;
        Path = Array.AsReadOnly(path.ToArray());
    }

    public GridHeaderKind Kind { get; }
    public IReadOnlyList<GridHeaderPathPart> Path { get; }

    /// <summary>Формат семантических метаданных реального UIA-заголовка, не отдельного псевдоэлемента.</summary>
    public string ToAutomationValue() => AutomationValuePrefix + System.Text.Json.JsonSerializer.Serialize(this);

    public static GridHeaderSelector? FromAutomationValue(string? value)
    {
        if (value is null || !value.StartsWith(AutomationValuePrefix, StringComparison.Ordinal)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<GridHeaderSelector>(value[AutomationValuePrefix.Length..])
            ?? throw new InvalidOperationException("The grid header address is empty.");
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
        { throw new InvalidOperationException("The grid header UIA address is invalid.", ex); }
    }
    public static GridHeaderSelector Column(string name) => new(GridHeaderKind.Column, [new(name)]);
    public static GridHeaderSelector Band(string name) => new(GridHeaderKind.Band, [new(name)]);
    public static GridHeaderSelector Group(string field, string? value, GridCellValueKind valueKind = GridCellValueKind.Text) =>
        new(GridHeaderKind.Group, [new(field, value, valueKind)]);
    public GridHeaderSelector ThenGroup(string field, string? value, GridCellValueKind valueKind = GridCellValueKind.Text) =>
        Kind == GridHeaderKind.Group ? new(Kind, [.. Path, new(field, value, valueKind)])
            : throw new InvalidOperationException("Only a group path can contain another group.");
    public GridHeaderSelector ThenBand(string name) => Kind == GridHeaderKind.Band
        ? new(Kind, [.. Path, new(name)]) : throw new InvalidOperationException("Only a band path can contain another band.");
    public bool Equals(GridHeaderSelector? other) => other is not null && Kind == other.Kind && Path.SequenceEqual(other.Path);
    public override bool Equals(object? obj) => obj is GridHeaderSelector other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        foreach (var part in Path) hash.Add(part);
        return hash.ToHashCode();
    }
    public override string ToString() => $"{Kind}: " + string.Join(" / ", Path.Select(p =>
        Kind == GridHeaderKind.Group ? $"{p.Name}={p.Value ?? "<null>"} ({p.ValueKind})" : p.Name));
}

/// <summary>Возможность читать реальные заголовки через общий адрес.</summary>
public interface IGridHeaderValueControl : IGridControl
{
    string ReadHeaderText(GridHeaderSelector selector, int timeoutMs = 5000, GridHeaderContextDefinition? context = null);

    GridHeaderSelector CaptureFirstGroup(IReadOnlyList<string> fields, int timeoutMs = 5000,
        GridHeaderContextDefinition? context = null) =>
        throw new NotSupportedException($"Grid '{AutomationId}' does not support capturing an available group.");
}

/// <summary>Captures an available group's complete address without recording its business values.</summary>
public static class GridHeaderKeyReader
{
    public static GridHeaderSelector CaptureFirst(IGridControl grid, IReadOnlyList<string> fields, int timeoutMs = 5000)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count == 0 || fields.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A complete, nonempty group field path is required.", nameof(fields));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        var fieldPath = fields.ToArray();
        var captured = grid is IGridHeaderValueControl headers
            ? headers.CaptureFirstGroup(fieldPath, timeoutMs)
            : throw new NotSupportedException($"Grid '{grid.AutomationId}' does not expose group metadata.");
        if (captured.Kind != GridHeaderKind.Group
            || !captured.Path.Select(static part => part.Name).SequenceEqual(fieldPath, StringComparer.Ordinal))
            throw new InvalidOperationException($"Grid '{grid.AutomationId}' returned a different group field path.");
        return captured;
    }
}
