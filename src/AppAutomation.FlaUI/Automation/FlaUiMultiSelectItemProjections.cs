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
        var result = new List<(T Item, string Text, Rectangle? Bounds, bool? Actionable)>();
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var text = readText(candidate);
            if (!indexes.TryGetValue(text, out var matchingIndex))
            {
                indexes.Add(text, result.Count);
                result.Add((candidate, text, null, null));
                continue;
            }

            var existing = result[matchingIndex];
            var existingBounds = existing.Bounds ?? readBounds(existing.Item);
            var candidateBounds = readBounds(candidate);
            if (!HasEquivalentBounds(existingBounds, candidateBounds))
            {
                throw new InvalidOperationException(
                    $"Multi-select items container exposes duplicate item text '{text}' "
                    + $"at bounds {existingBounds} and {candidateBounds}.");
            }

            var existingActionable = existing.Actionable ?? isActionable(existing.Item);
            var candidateActionable = isActionable(candidate);
            if (!existingActionable && candidateActionable)
            {
                result[matchingIndex] = (candidate, text, candidateBounds, candidateActionable);
            }
            else
            {
                result[matchingIndex] = (existing.Item, existing.Text, existingBounds, existingActionable);
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
        return selectableTexts.Count > checkBoxTexts.Count
            || selectableTexts.All(text => !checkBoxSet.Contains(text));
    }

    internal static bool CanUseVisibleSelection(
        IReadOnlyCollection<string> requestedTexts,
        IReadOnlyCollection<string> visibleTexts,
        IReadOnlyCollection<string> selectedTexts)
    {
        var visible = visibleTexts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requestedTexts.All(visible.Contains) && selectedTexts.All(visible.Contains);
    }

    private static bool HasEquivalentBounds(Rectangle first, Rectangle second)
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
