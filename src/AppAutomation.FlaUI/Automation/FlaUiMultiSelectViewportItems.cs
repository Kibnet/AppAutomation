using System.Drawing;

namespace AppAutomation.FlaUI.Automation;

internal sealed class FlaUiMultiSelectViewportItems
{
    private const int GeometryTolerance = 2;

    internal readonly record struct Observation(string Text, bool? IsChecked, Rectangle Bounds, int PositionInSet = 0);

    internal sealed record Viewport(double Position, Rectangle Bounds, Observation[] Rows)
    {
        internal int FirstItemIndex { get; init; }

        internal bool Matches(Viewport other, bool compareSelection = false)
        {
            if (Rows.Length != other.Rows.Length)
                return false;
            for (var index = 0; index < Rows.Length; index++)
            {
                var row = Rows[index];
                var candidate = other.Rows[index];
                if (!IsSameRowAtRest(row, candidate) || compareSelection && row.IsChecked != candidate.IsChecked)
                    return false;
            }
            return true;
        }

        internal bool ContainsUnmovedRowsOf(Viewport other)
        {
            var index = 0;
            foreach (var row in Rows)
            {
                if (index == other.Rows.Length)
                    break;
                var candidate = other.Rows[index];
                if (row.Bounds == candidate.Bounds && HasSameCaption(row, candidate) && PositionsAgree(row, candidate))
                    index++;
            }
            return index == other.Rows.Length;
        }

        internal Viewport Include(Viewport sample)
        {
            // A boundary read can temporarily omit a realized row. Keep its slot until the viewport changes.
            var rows = Rows.ToList();
            var sharesRow = false;
            foreach (var observation in sample.Rows)
            {
                var index = Array.FindIndex(Rows, row => IsSameRowAtRest(row, observation));
                if (index < 0)
                    rows.Add(observation);
                else
                {
                    sharesRow = true;
                    rows[index] = rows[index] with
                    {
                        IsChecked = observation.IsChecked,
                        PositionInSet = Math.Max(rows[index].PositionInSet, observation.PositionInSet)
                    };
                }
            }

            return sharesRow || Rows.Length == 0
                ? this with { Rows = rows.OrderBy(static row => row.Bounds.Top).ThenBy(static row => row.Bounds.Left).ToArray() }
                : this;
        }

        internal bool HasKnownGap => HasConsecutivePositions(Rows) == false;
    }

    internal sealed class Item(Viewport viewport, int index)
    {
        internal Viewport Viewport { get; private set; } = viewport;
        internal int Index { get; private set; } = index;
        internal Observation State => Viewport.Rows[Index];
        internal bool? IsChecked { get; set; } = viewport.Rows[index].IsChecked;

        internal void Observe(Viewport next, int nextIndex)
        {
            var row = next.Rows[nextIndex];
            IsChecked = row.IsChecked;
            // Prefer a viewport where more of the checkbox can actually be clicked.
            if (Rectangle.Intersect(next.Bounds, row.Bounds).Height >= Rectangle.Intersect(Viewport.Bounds, State.Bounds).Height)
            {
                Viewport = next;
                Index = nextIndex;
            }
        }
    }

    private readonly List<Item> _items = [];

    internal IReadOnlyList<Item> Items => _items;

    internal Viewport Append(Viewport viewport, int overlap)
    {
        var start = _items.Count - overlap;
        viewport = viewport with { FirstItemIndex = start };
        for (var index = 0; index < viewport.Rows.Length; index++)
        {
            if (index < overlap)
                _items[start + index].Observe(viewport, index);
            else
                _items.Add(new Item(viewport, index));
        }
        return viewport;
    }

    internal Viewport WithCurrentSelection(Viewport viewport)
    {
        var rows = new Observation[viewport.Rows.Length];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = viewport.Rows[index] with { IsChecked = _items[viewport.FirstItemIndex + index].IsChecked };
        }
        return viewport with { Rows = rows };
    }

    internal (Item Item, bool ShouldBeChecked)[] PlanChanges(IReadOnlySet<string> requested)
    {
        FlaUiMultiSelectItemProjections.ValidateRequestedItems(_items.Select(static item => item.State.Text), requested);
        return _items.Select(item => (Item: item, ShouldBeChecked: requested.Contains(item.State.Text)))
            .Where(static change => change.Item.IsChecked != change.ShouldBeChecked)
            .ToArray();
    }

    internal static int? FindOverlap(Viewport earlier, Viewport later, double? expectedShift)
    {
        var previous = earlier.Rows;
        var current = later.Rows;
        if (previous.Length == 0 || current.Length == 0)
            return previous.Length == current.Length ? 0 : null;
        if (earlier.ContainsUnmovedRowsOf(later) || later.ContainsUnmovedRowsOf(earlier))
            return null;

        var previousPositions = HasConsecutivePositions(previous);
        var currentPositions = HasConsecutivePositions(current);
        if (previousPositions == false || currentPositions == false)
            return null;

        if (previousPositions == true && currentPositions == true)
        {
            var overlap = (long)previous[^1].PositionInSet - current[0].PositionInSet + 1;
            if (overlap < 0 || overlap > Math.Min(previous.Length, current.Length))
                return null;
            if (overlap == 0)
                return 0; // The published positions account for every intervening row.

            var moved = false;
            for (var index = 0; index < overlap; index++)
            {
                var oldRow = previous[previous.Length - (int)overlap + index];
                var newRow = current[index];
                if (!HasSameCaption(oldRow, newRow))
                    return null;
                moved |= oldRow.Bounds.Bottom > newRow.Bounds.Bottom || oldRow.Bounds.Top > newRow.Bounds.Top;
            }
            return moved ? (int)overlap : null;
        }

        if (expectedShift > earlier.Bounds.Height + GeometryTolerance)
            return null; // No geometric overlap is possible across this jump; only ordinals could bridge it.

        int? match = null;
        for (var count = 1; count <= Math.Min(previous.Length, current.Length); count++)
        {
            var first = previous.Length - count;
            if (!HasSameCaption(previous[first], current[0]) || !HasSameCaption(previous[^1], current[count - 1]))
                continue;

            // A clipped row can retain its top edge or lose part of its height. Try both exposed edges.
            var shiftFromTop = previous[first].Bounds.Top - current[0].Bounds.Top;
            var shiftFromBottom = previous[^1].Bounds.Bottom - current[count - 1].Bounds.Bottom;
            if (!MatchesGeometry(earlier, later, count, expectedShift, shiftFromTop)
                && (shiftFromTop == shiftFromBottom || !MatchesGeometry(earlier, later, count, expectedShift, shiftFromBottom)))
                continue;

            if (match is not null)
                return null; // Two possible alignments cannot establish which logical rows survived.
            match = count;
        }

        return match;
    }

    private static bool MatchesGeometry(Viewport earlier, Viewport later, int count, double? expectedShift, int shift)
    {
        if (shift <= 0)
            return false;
        var common = Rectangle.Intersect(Translate(earlier.Bounds, -shift), later.Bounds);
        if (common.Height <= GeometryTolerance || common.Width <= 0)
            return false;

        var previous = earlier.Rows;
        var current = later.Rows;
        var start = previous.Length - count;
        var hasIndexedAnchor = false;
        var hasDistinctAnchor = false;
        for (var index = 0; index < count; index++)
        {
            var oldRow = previous[start + index];
            var newRow = current[index];
            var oldBounds = Rectangle.Intersect(Translate(oldRow.Bounds, -shift), common);
            var newBounds = Rectangle.Intersect(newRow.Bounds, common);
            if (!HasSameCaption(oldRow, newRow) || !PositionsAgree(oldRow, newRow)
                || !FlaUiMultiSelectItemProjections.HasEquivalentBounds(oldBounds, newBounds))
                return false;
            hasIndexedAnchor |= oldRow.PositionInSet > 0 && oldRow.PositionInSet == newRow.PositionInSet;
            hasDistinctAnchor |= !HasSameCaption(current[0], newRow);
        }

        // A scrollbar may report its new position while a subset of the old rows is still on screen.
        // Check the entire shared span, not just the matching captions at its edges.
        if (start > 0 && CenterY(Translate(previous[start - 1].Bounds, -shift)) > common.Top + GeometryTolerance
            || count < current.Length && CenterY(current[count].Bounds) < common.Bottom - GeometryTolerance)
            return false;

        // RangeValue has provider-defined units. Without a reported pixel displacement or an
        // ordinal, a lone equal caption is not evidence of overlap (even if its UIA ID is reused).
        return hasIndexedAnchor || hasDistinctAnchor
            || expectedShift is { } expected && Math.Abs(shift - expected) <= GeometryTolerance;
    }

    // A non-increasing position sequence can describe different UIA sets, not one list.
    private static bool? HasConsecutivePositions(Observation[] rows)
    {
        if (rows.Length == 0 || rows[0].PositionInSet <= 0)
            return null;
        var consecutive = true;
        for (var index = 1; index < rows.Length; index++)
        {
            var difference = (long)rows[index].PositionInSet - rows[index - 1].PositionInSet;
            if (difference <= 0)
                return null;
            consecutive &= difference == 1;
        }
        return consecutive;
    }

    private static bool HasSameCaption(Observation first, Observation second) =>
        string.Equals(first.Text, second.Text, StringComparison.OrdinalIgnoreCase);

    private static bool PositionsAgree(Observation first, Observation second) =>
        first.PositionInSet <= 0 || second.PositionInSet <= 0 || first.PositionInSet == second.PositionInSet;

    private static bool IsSameRowAtRest(Observation first, Observation second) =>
        HasSameCaption(first, second) && PositionsAgree(first, second)
        && FlaUiMultiSelectItemProjections.HasEquivalentBounds(first.Bounds, second.Bounds);

    private static int CenterY(Rectangle bounds) => bounds.Top + bounds.Height / 2;

    private static Rectangle Translate(Rectangle bounds, int offset)
    {
        bounds.Offset(0, offset);
        return bounds;
    }
}
