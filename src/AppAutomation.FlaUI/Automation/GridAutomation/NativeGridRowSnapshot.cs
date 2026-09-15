using System.Drawing;
using AppAutomation.Abstractions;

namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal sealed record NativeGridRowSnapshot(
    IReadOnlyList<string> CellTexts,
    int? RowIndex,
    Rectangle Bounds,
    GridScrollPosition ScrollPosition)
{
    public Dictionary<GridRowAutomationProperty, string?> RowAutomationValues { get; init; } = new();

    public IReadOnlyList<string?> StableValues { get; init; } = Array.Empty<string?>();

    public string? RuntimeId { get; init; }

    public string ViewportSignature { get; init; } = string.Empty;

    public int ObservationIndex { get; init; }

    public bool HasSameStableRow(NativeGridRowSnapshot other)
    {
        if (RowIndex is { } rowIndex && other.RowIndex is { } otherRowIndex)
        {
            return rowIndex == otherRowIndex;
        }

        var samePosition = HasSameScrollPosition(other);
        if (HasMatchingConfiguredStableValues(other)
            && (ObservationIndex == other.ObservationIndex || samePosition == true)
            && HasCompatibleRowBand(Bounds, other.Bounds))
        {
            return true;
        }

        if (HasSameObservationProjection(other))
        {
            return true;
        }

        if (HasMatchingProjectionContent(other)
            && samePosition == true
            && HasEquivalentProjectionBounds(Bounds, other.Bounds))
        {
            return true;
        }

        if (string.IsNullOrEmpty(RuntimeId) || string.IsNullOrEmpty(other.RuntimeId))
        {
            throw new InvalidOperationException("Native row identity is unavailable: expose a UIA RuntimeId or native row index.");
        }

        if (!string.Equals(RuntimeId, other.RuntimeId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!CellTexts.SequenceEqual(other.CellTexts, StringComparer.Ordinal)
            || RowAutomationValues.Count != other.RowAutomationValues.Count
            || RowAutomationValues.Any(pair => !other.RowAutomationValues.TryGetValue(pair.Key, out var value)
                || !string.Equals(pair.Value, value, StringComparison.Ordinal)))
        {
            return false;
        }


        if (ObservationIndex == other.ObservationIndex
            && Bounds.Width > 0
            && Bounds.Height > 0
            && other.Bounds.Width > 0
            && other.Bounds.Height > 0
            && !HasEquivalentProjectionBounds(Bounds, other.Bounds))
        {
            return false;
        }

        var sameViewport = samePosition != false && ViewportSignature.Length > 0
            && string.Equals(ViewportSignature, other.ViewportSignature, StringComparison.Ordinal);
        if (Bounds.Width > 0 && Bounds.Height > 0 && Bounds == other.Bounds && (sameViewport || samePosition == true))
        {
            return true;
        }

        var scrollDelta = ScrollPosition.RangeValue is { } currentRange && other.ScrollPosition.RangeValue is { } priorRange
            ? currentRange - priorRange
            : ScrollPosition.ScrollPercent is { } currentPercent && other.ScrollPosition.ScrollPercent is { } priorPercent
                ? currentPercent - priorPercent
                : (double?)null;
        if (Math.Abs(ObservationIndex - other.ObservationIndex) == 1 && scrollDelta is { } delta && Math.Abs(delta) > 0.001
            && Bounds.Width > 0 && Bounds.Height > 0 && Bounds.Size == other.Bounds.Size && Bounds.Left == other.Bounds.Left
            && Bounds.Top != other.Bounds.Top)
        {
            // An overlapping row moves against the scroll direction. A recycled container re-enters from the other edge.
            // Only compare consecutive observations; neither RangeValue units nor a caption alone establish row identity.
            return Math.Sign(Bounds.Top - other.Bounds.Top) != Math.Sign(delta);
        }

        throw new InvalidOperationException(
            $"Native row identity '{RuntimeId}' cannot distinguish viewport overlap from a recycled row. " +
            "Expose native row index metadata; matching cell text alone does not establish row identity.");
    }

    private bool HasSameObservationProjection(NativeGridRowSnapshot other)
    {
        if (ObservationIndex != other.ObservationIndex
            || !HasMatchingProjectionContent(other))
        {
            return false;
        }

        return HasEquivalentProjectionBounds(Bounds, other.Bounds);
    }

    private bool HasMatchingProjectionContent(NativeGridRowSnapshot other)
    {
        return HasMatchingStableValues(other)
            && CellTexts.SequenceEqual(other.CellTexts, StringComparer.Ordinal);
    }

    private bool HasMatchingConfiguredStableValues(NativeGridRowSnapshot other)
    {
        return StableValues.Count > 0
            && StableValues.Any(static value => !string.IsNullOrWhiteSpace(value))
            && other.StableValues.Count > 0
            && other.StableValues.Any(static value => !string.IsNullOrWhiteSpace(value))
            && StableValues.SequenceEqual(other.StableValues, StringComparer.Ordinal);
    }

    private bool? HasSameScrollPosition(NativeGridRowSnapshot other)
    {
        return ScrollPosition.RangeValue is { } range && other.ScrollPosition.RangeValue is { } otherRange
            ? Math.Abs(range - otherRange) <= 0.001
            : ScrollPosition.ScrollPercent is { } percent && other.ScrollPosition.ScrollPercent is { } otherPercent
                ? Math.Abs(percent - otherPercent) <= 0.001
                : null;
    }

    private bool HasMatchingStableValues(NativeGridRowSnapshot other)
    {
        if (StableValues.Count > 0 || other.StableValues.Count > 0)
        {
            return StableValues.Count > 0
                && StableValues.Any(static value => !string.IsNullOrWhiteSpace(value))
                && other.StableValues.Any(static value => !string.IsNullOrWhiteSpace(value))
                && StableValues.SequenceEqual(other.StableValues, StringComparer.Ordinal);
        }

        return RowAutomationValues.Count > 0
            && RowAutomationValues.Values.Any(static value => !string.IsNullOrWhiteSpace(value))
            && other.RowAutomationValues.Values.Any(static value => !string.IsNullOrWhiteSpace(value))
            && RowAutomationValues.Count == other.RowAutomationValues.Count
            && RowAutomationValues.All(pair =>
                other.RowAutomationValues.TryGetValue(pair.Key, out var value)
                && string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    private static bool HasEquivalentProjectionBounds(Rectangle first, Rectangle second)
    {
        if (first.Width <= 0 || first.Height <= 0 || second.Width <= 0 || second.Height <= 0)
        {
            return false;
        }

        const int roundingTolerance = 1;
        var inflatedFirst = first;
        inflatedFirst.Inflate(roundingTolerance, roundingTolerance);
        var inflatedSecond = second;
        inflatedSecond.Inflate(roundingTolerance, roundingTolerance);
        var intersection = Rectangle.Intersect(inflatedFirst, inflatedSecond);
        return intersection.Width >= Math.Min(first.Width, second.Width) / 2
            && intersection.Height >= Math.Min(first.Height, second.Height) / 2;
    }

    private static bool HasCompatibleRowBand(Rectangle first, Rectangle second)
    {
        if (first.Height <= 0 || second.Height <= 0)
        {
            return false;
        }

        const int roundingTolerance = 1;
        var overlap = Math.Min(first.Bottom, second.Bottom) + roundingTolerance
            - (Math.Max(first.Top, second.Top) - roundingTolerance);
        return overlap > 0 && overlap * 2 >= Math.Min(first.Height, second.Height);
    }
}
