namespace AppAutomation.Abstractions;

internal static class GridCurrentViewPositionResolver
{
    public static int? FindPosition(
        IReadOnlyList<object?> viewItems,
        IReadOnlyDictionary<object, double> visiblePositions,
        object selectedItem,
        bool hasAuthoritativeView)
    {
        if (!TryValidate(viewItems, visiblePositions, hasAuthoritativeView, out var indexes, out _)
            || !visiblePositions.ContainsKey(selectedItem))
        {
            return null;
        }

        return indexes.TryGetValue(selectedItem, out var index) ? index : null;
    }

    public static object? FindItemAtPosition(
        IReadOnlyList<object?> viewItems,
        IReadOnlyDictionary<object, double> visiblePositions,
        int position,
        bool hasAuthoritativeView)
    {
        return position >= 0
            && TryValidate(viewItems, visiblePositions, hasAuthoritativeView, out _, out var provenCount)
            && position < provenCount
            && viewItems[position] is { } item
            && visiblePositions.ContainsKey(item)
                ? item
                : null;
    }

    private static bool TryValidate(
        IReadOnlyList<object?> viewItems,
        IReadOnlyDictionary<object, double> visiblePositions,
        bool hasAuthoritativeView,
        out Dictionary<object, int> indexes,
        out int provenCount)
    {
        indexes = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        provenCount = 0;
        for (var index = 0; index < viewItems.Count; index++)
        {
            if (viewItems[index] is not { } item || !indexes.TryAdd(item, index))
            {
                return false;
            }
        }

        if (visiblePositions.Count == 0)
        {
            return false;
        }

        var orderedVisible = visiblePositions.OrderBy(static pair => pair.Value).ToArray();
        var firstIndex = -1;
        for (var index = 0; index < orderedVisible.Length; index++)
        {
            var visible = orderedVisible[index];
            if (!double.IsFinite(visible.Value)
                || !indexes.TryGetValue(visible.Key, out var viewIndex)
                || index > 0 && (visible.Value - orderedVisible[index - 1].Value < 1
                    || viewIndex != firstIndex + index))
            {
                return false;
            }

            if (index == 0)
            {
                firstIndex = viewIndex;
            }
        }

        if (!hasAuthoritativeView && firstIndex != 0)
        {
            return false;
        }

        provenCount = hasAuthoritativeView ? viewItems.Count : orderedVisible.Length;
        return true;
    }
}
