using System.Globalization;
using System.Linq.Expressions;

namespace AppAutomation.Abstractions;

/// <summary>A surface that publishes a complete set of named double-click targets.</summary>
public interface INamedItemSurfaceControl : IUiControl
{
    /// <summary>Reveals the unique exact match, then sends one double click to its current hit area.</summary>
    void DoubleClickItem(string itemName, int timeoutMs);
}

/// <summary>Describes the complete logical item set in the surface's automation ItemStatus.</summary>
public static class NamedItemSurfaceMetadata
{
    private const string Prefix = "AppAutomation.NamedItems/1:";

    /// <summary>
    /// Publishes the logical count, including offscreen items, and the current collection revision.
    /// Change the revision whenever item identities or names change, not on viewport movement.
    /// </summary>
    public static string Format(int itemCount, long revision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        return FormattableString.Invariant($"{Prefix}{itemCount}:{revision}");
    }

    internal static bool IsPublished(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    internal static (int Count, long Revision) Read(string? value, string surface)
    {
        if (IsPublished(value))
        {
            var fields = value.AsSpan(Prefix.Length);
            var separator = fields.IndexOf(':');
            if (separator > 0
                && int.TryParse(fields[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                && long.TryParse(fields[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var revision))
                return (count, revision);
        }

        throw new InvalidOperationException($"Named surface '{surface}' does not publish valid item metadata.");
    }
}

internal sealed record NamedItemSurfaceItem<T>(string Key, string Name, T Element);

internal sealed class NamedItemSurfaceSnapshot<T>
{
    private readonly string _surface;

    public NamedItemSurfaceSnapshot(
        string surface, string? metadata, IEnumerable<NamedItemSurfaceItem<T>> items)
    {
        _surface = surface;
        var (count, revision) = NamedItemSurfaceMetadata.Read(metadata, surface);
        Revision = revision;
        var byKey = new Dictionary<string, NamedItemSurfaceItem<T>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidOperationException($"Named surface '{surface}' publishes an item without a key or name.");

            var normalized = item with { Name = item.Name.Trim() };
            if (!byKey.TryAdd(item.Key, normalized) && byKey[item.Key] != normalized)
                throw new InvalidOperationException($"Named surface '{surface}' publishes conflicting items with key '{item.Key}'.");
        }

        if (byKey.Count != count)
            throw new InvalidOperationException(
                $"Named surface '{surface}' publishes an incomplete item set: expected {count}, read {byKey.Count}.");

        Items = byKey.Values.ToArray();
    }

    public long Revision { get; }
    public IReadOnlyList<NamedItemSurfaceItem<T>> Items { get; }

    public NamedItemSurfaceItem<T> Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var expected = name.Trim();
        var matches = Items.Where(item => string.Equals(item.Name, expected, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Named surface '{_surface}' has no item named '{name}'."),
            _ => throw new InvalidOperationException($"Item name '{name}' is ambiguous in named surface '{_surface}'.")
        };
    }

    public NamedItemSurfaceItem<T> Rebind(NamedItemSurfaceItem<T> original, long revision)
    {
        if (revision != Revision)
            throw new InvalidOperationException($"Named surface '{_surface}' changed its item set during the gesture.");
        var current = Find(original.Name);
        if (!string.Equals(current.Key, original.Key, StringComparison.Ordinal))
            throw new InvalidOperationException($"Named item '{original.Name}' changed identity in surface '{_surface}'.");
        return current;
    }
}

public static partial class UiPageExtensions
{
    /// <summary>Double-clicks the unique item with the specified exact name in the selected surface.</summary>
    public static TSelf DoubleClickNamedItem<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, INamedItemSurfaceControl>> selector,
        string itemName,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
        var budget = UiOperationTimeoutBudget.Start(timeoutMs, "named item double click");
        Resolve(selector, page).DoubleClickItem(itemName, budget.RemainingMilliseconds);
        return page;
    }
}
