namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal readonly record struct HorizontalGridViewport(double? Position, double? Minimum, double? Maximum)
{
    internal bool AtBoundary(bool forward) =>
        Position is { } position && (forward ? Maximum : Minimum) is { } boundary
        && (forward ? position >= boundary - 0.001 : position <= boundary + 0.001);

    internal bool PreferForward => Position is not { } position || Minimum is not { } minimum
        || Maximum is not { } maximum || maximum - position <= position - minimum;
}

internal static class NativeGridHorizontalReveal
{
    internal static bool? DirectionFromCatalog(int targetIndex, IEnumerable<int> visibleIndexes)
    {
        int? first = null;
        int? last = null;
        foreach (var index in visibleIndexes)
        {
            // The sequence follows screen order. A gap may be a hidden column, not a frozen boundary.
            if (last is { } previous && index <= previous) return null;
            first ??= index;
            last = index;
        }

        if (targetIndex < first) return false;
        if (targetIndex > last) return true;
        return null;
    }

    internal static bool TryReveal(Func<HorizontalGridViewport> observeViewport, Func<bool?> preferredDirection,
        Func<bool> isVisible, Func<bool, bool> tryMove, Func<bool> hasBudget)
    {
        if (!hasBudget()) return false;
        if (isVisible()) return hasBudget();

        var viewport = observeViewport();
        var leftExhausted = viewport.AtBoundary(forward: false);
        var rightExhausted = viewport.AtBoundary(forward: true);
        var forward = leftExhausted || !rightExhausted && (preferredDirection() ?? viewport.PreferForward);

        for (var step = 0; step < 64 && hasBudget() && !(leftExhausted && rightExhausted); step++)
        {
            var moved = !viewport.AtBoundary(forward) && tryMove(forward);
            if (!hasBudget()) return false;
            if (isVisible()) return hasBudget();
            if (moved)
            {
                viewport = observeViewport();
                continue;
            }

            if (forward) rightExhausted = true;
            else leftExhausted = true;
            forward = !forward;
        }

        return false;
    }
}
