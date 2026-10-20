using System.Drawing;
using System.Globalization;
using FlaUI.Core.Definitions;

namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal readonly record struct NativeGridCellCandidate(Rectangle Bounds, bool IsText);

internal static class NativeGridCellMatcher
{
    public static bool IsEmptyDateProjection(
        string? name,
        string? value,
        ControlType controlType,
        bool hasTextPattern,
        int descendantCount)
    {
        // Some grid providers publish a row ordinal as the Name of an otherwise empty date cell.
        return controlType == ControlType.Group
            && name is { Length: > 0 }
            && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            && string.IsNullOrWhiteSpace(value)
            && !hasTextPattern
            && descendantCount == 0;
    }

    public static int SelectBySourceField(
        string sourceFieldName,
        string? geometricallySelectedAutomationId,
        IReadOnlyList<string?> candidateAutomationIds,
        bool selectedIdBelongsToDifferentSourceField)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFieldName);
        ArgumentNullException.ThrowIfNull(candidateAutomationIds);
        var matches = candidateAutomationIds
            .Select(static (id, index) => (Id: id, Index: index))
            .Where(candidate => string.Equals(candidate.Id, sourceFieldName, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Source field '{sourceFieldName}' has multiple UIA cell projections in the same row.");
        }

        if (matches.Length == 1)
        {
            return matches[0].Index;
        }

        if (selectedIdBelongsToDifferentSourceField)
        {
            throw new InvalidOperationException(
                $"Source field '{sourceFieldName}' has no matching UIA cell; geometry selected "
                + $"'{geometricallySelectedAutomationId}' instead.");
        }

        return -1;
    }

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
