using System.Drawing;

namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal readonly record struct NativeGridCellCandidate(Rectangle Bounds, bool IsText);

internal static class NativeGridCellMatcher
{
    public static int?[]? Match(
        IReadOnlyList<Rectangle> headers,
        IReadOnlyList<NativeGridCellCandidate> candidates,
        Rectangle rowBounds,
        IReadOnlyCollection<int>? requiredIndexes)
    {
        var matches = new int?[headers.Count];
        for (var headerIndex = 0; headerIndex < headers.Count; headerIndex++)
        {
            if (requiredIndexes is not null && !requiredIndexes.Contains(headerIndex))
            {
                continue;
            }

            var header = headers[headerIndex];
            var selected = Enumerable.Range(0, candidates.Count)
                .Where(index => OverlapWidth(candidates[index].Bounds, header) > 0
                    && Rectangle.Intersect(candidates[index].Bounds, rowBounds).Height > 0)
                .OrderBy(index => candidates[index].IsText)
                .ThenByDescending(index => OverlapWidth(candidates[index].Bounds, header))
                .ThenBy(index => Math.Abs(candidates[index].Bounds.Width - header.Width))
                .ThenByDescending(index => candidates[index].Bounds.Width)
                .Select(static index => (int?)index)
                .FirstOrDefault();
            if (selected is null)
            {
                return null;
            }

            matches[headerIndex] = selected;
        }

        return matches;
    }

    public static int OverlapWidth(Rectangle left, Rectangle right) =>
        Math.Max(0, Math.Min(left.Right, right.Right) - Math.Max(left.Left, right.Left));
}
