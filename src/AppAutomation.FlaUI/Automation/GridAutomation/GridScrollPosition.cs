namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal sealed record GridScrollPosition(double? ScrollPercent, double? RangeValue)
{
    public bool HasMovedFrom(GridScrollPosition previous, bool forward)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return HasMoved(ScrollPercent, previous.ScrollPercent, forward)
            || HasMoved(RangeValue, previous.RangeValue, forward);
    }

    public bool CanCompareWith(GridScrollPosition previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return CanCompare(ScrollPercent, previous.ScrollPercent)
            || CanCompare(RangeValue, previous.RangeValue);
    }

    private static bool HasMoved(double? current, double? previous, bool forward)
    {
        const double tolerance = 0.001;
        if (current is not { } currentValue
            || previous is not { } previousValue
            || currentValue < 0
            || previousValue < 0)
        {
            return false;
        }

        return forward
            ? currentValue > previousValue + tolerance
            : currentValue < previousValue - tolerance;
    }

    private static bool CanCompare(double? current, double? previous)
    {
        return current is >= 0 && previous is >= 0;
    }
}
