using System.Drawing;

namespace AppAutomation.FlaUI.Automation;

internal static class FlaUiMultiSelectItemProjections
{
    internal static T[] Normalize<T>(
        IEnumerable<T> candidates,
        Func<T, string> readText,
        Func<T, Rectangle> readBounds,
        Func<T, bool> isActionable)
    {
        var result = new List<(T Item, Rectangle? Bounds, bool? Actionable)>();
        var indexes = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var text = readText(candidate);
            if (!indexes.TryGetValue(text, out var matchingIndexes))
            {
                indexes.Add(text, [result.Count]);
                result.Add((candidate, null, null));
                continue;
            }

            var candidateBounds = readBounds(candidate);
            var merged = false;
            foreach (var index in matchingIndexes)
            {
                var existing = result[index];
                var existingBounds = existing.Bounds ?? readBounds(existing.Item);
                if (!HasEquivalentBounds(existingBounds, candidateBounds))
                {
                    result[index] = (existing.Item, existingBounds, existing.Actionable);
                    continue;
                }

                var existingActionable = existing.Actionable ?? isActionable(existing.Item);
                result[index] = !existingActionable && isActionable(candidate)
                    ? (candidate, existingBounds, true)
                    : (existing.Item, existingBounds, existingActionable);
                merged = true;
                break;
            }

            if (!merged)
            {
                matchingIndexes.Add(result.Count);
                result.Add((candidate, candidateBounds, null));
            }
        }

        return result.Select(static entry => entry.Item).ToArray();
    }

    internal static bool PreferSelectableItems(
        IReadOnlyCollection<string> checkBoxTexts,
        IReadOnlyCollection<string> selectableTexts)
    {
        if (selectableTexts.Count == 0)
        {
            return false;
        }

        if (checkBoxTexts.Count == 0)
        {
            return true;
        }

        var checkBoxSet = checkBoxTexts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return selectableTexts.All(text => !checkBoxSet.Contains(text));
    }

    internal static bool CanUseVisibleSelection(
        IReadOnlyCollection<string> requestedTexts,
        IReadOnlyCollection<string> visibleTexts,
        IReadOnlyCollection<string> selectedTexts)
    {
        var visible = visibleTexts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requestedTexts.All(visible.Contains) && selectedTexts.All(visible.Contains);
    }

    internal static void ValidateRequestedItems(IEnumerable<string> availableTexts, IReadOnlySet<string> requestedTexts)
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in availableTexts)
        {
            if (requestedTexts.Contains(text) && !matched.Add(text))
                throw new InvalidOperationException($"Multi-select item '{text}' is ambiguous: more than one matching item.");
        }

        if (matched.Count != requestedTexts.Count)
        {
            var missing = requestedTexts.Where(text => !matched.Contains(text));
            throw new InvalidOperationException($"Multi-select items were not found: [{string.Join(", ", missing)}].");
        }
    }

    internal static bool HasEquivalentBounds(Rectangle first, Rectangle second)
    {
        if (first.Width <= 0 || first.Height <= 0 || second.Width <= 0 || second.Height <= 0)
        {
            return false;
        }

        var intersection = Rectangle.Intersect(first, second);
        if (intersection.Width <= 0 || intersection.Height <= 0)
        {
            return false;
        }

        var smallerArea = Math.Min((long)first.Width * first.Height, (long)second.Width * second.Height);
        return (long)intersection.Width * intersection.Height * 10 >= smallerArea * 9
            && Math.Max(first.Height, second.Height) <= Math.Min(first.Height, second.Height) * 2L;
    }
}
