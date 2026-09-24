namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal static class GridScrollBoundary
{
    public static bool IsReached(
        bool? patternScrollable,
        double? patternPercent,
        double? rangeMinimum,
        double? rangeMaximum,
        double? rangeValue,
        bool forward)
    {
        if (patternScrollable != false && patternPercent is >= 0 and { } percent)
        {
            return forward ? percent >= 100 : percent <= 0;
        }

        if (rangeMinimum is { } minimum
            && rangeMaximum is { } maximum
            && rangeValue is { } value)
        {
            return maximum <= minimum || (forward ? value >= maximum : value <= minimum);
        }

        return patternScrollable == false;
    }
}
