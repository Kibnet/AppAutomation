namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal static class NativeGridTraversal
{
    public static TRow[] Scan<TRow>(
        Func<TRow[]> readRows,
        Action<IReadOnlyList<TRow>> observeRows,
        Func<IReadOnlyList<TRow>, string> createSignature,
        Func<string, IReadOnlyList<TRow>, bool> moveForward)
    {
        ArgumentNullException.ThrowIfNull(readRows);
        ArgumentNullException.ThrowIfNull(observeRows);
        ArgumentNullException.ThrowIfNull(createSignature);
        ArgumentNullException.ThrowIfNull(moveForward);

        TRow[] visibleRows;
        do
        {
            visibleRows = readRows();
            observeRows(visibleRows);
            var previousSignature = createSignature(visibleRows);
            if (moveForward(previousSignature, visibleRows))
            {
                continue;
            }

            var finalRows = readRows();
            if (!string.Equals(
                    previousSignature,
                    createSignature(finalRows),
                    StringComparison.Ordinal))
            {
                visibleRows = finalRows;
                observeRows(visibleRows);
            }

            break;
        }
        while (true);

        return visibleRows;
    }
}
