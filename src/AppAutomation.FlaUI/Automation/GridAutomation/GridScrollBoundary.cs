namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal static class GridScrollBoundary
{
    public static bool AreHorizontalScrollButtons(
        System.Drawing.Rectangle backward,
        System.Drawing.Rectangle forward)
    {
        if (backward.Width <= 0 || backward.Height <= 0
            || forward.Width <= 0 || forward.Height <= 0)
        {
            return false;
        }

        var centerDistance = Math.Abs(
            (backward.Top + backward.Bottom) / 2.0
            - (forward.Top + forward.Bottom) / 2.0);
        var horizontalGap = Math.Max(backward.Left, forward.Left)
            - Math.Min(backward.Right, forward.Right);
        return centerDistance <= Math.Min(backward.Height, forward.Height) / 2.0
            && horizontalGap >= 0;
    }

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
