namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal static class NativeGridRowNormalizer
{
    public static bool HasContinuousObservation(
        IReadOnlyList<NativeGridRowSnapshot>? previous,
        IReadOnlyList<NativeGridRowSnapshot> current,
        Func<NativeGridRowSnapshot, NativeGridRowSnapshot, bool> representSameRow)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(representSameRow);
        if (!HasCompactRowBands(current))
        {
            return false;
        }

        if (previous is null)
        {
            return true;
        }

        for (var overlap = Math.Min(previous.Count, current.Count); overlap > 0; overlap--)
        {
            if (Enumerable.Range(0, overlap).All(index =>
                    representSameRow(previous[previous.Count - overlap + index], current[index])))
            {
                return true;
            }
        }

        return false;
    }

    public static NativeGridRowSnapshot? AtScannedPosition(
        IReadOnlyList<NativeGridRowSnapshot> rows,
        int position,
        bool positionOrderProven)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        return positionOrderProven && position < rows.Count ? rows[position] : null;
    }

    public static NativeGridRowSnapshot? AtVisibleStartPosition(
        IReadOnlyList<NativeGridRowSnapshot> rows,
        int position,
        bool atStart)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        if (!atStart || position >= rows.Count || rows.Count == 0)
        {
            return null;
        }

        var observation = rows[0].ObservationIndex;
        if (rows.Any(row => row.ObservationIndex != observation
                || row.Bounds.Width <= 0
                || row.Bounds.Height <= 0))
        {
            return null;
        }

        var ordered = rows.OrderBy(static row => row.Bounds.Top).ToArray();
        if (!HasCompactRowBands(ordered))
        {
            return null;
        }

        return ordered[position];
    }

    private static bool HasCompactRowBands(IReadOnlyList<NativeGridRowSnapshot> rows)
    {
        if (rows.Count == 0 || rows.Any(static row => row.Bounds.Width <= 0 || row.Bounds.Height <= 0))
        {
            return false;
        }

        for (var index = 1; index < rows.Count; index++)
        {
            var previous = rows[index - 1].Bounds;
            var current = rows[index].Bounds;
            var smallerHeight = Math.Min(previous.Height, current.Height);
            var gap = current.Top - previous.Bottom;
            if (current.Top - previous.Top < 1
                || gap < -2
                || gap > Math.Max(2, smallerHeight / 2))
            {
                return false;
            }
        }

        return true;
    }

    public static void Append(
        List<NativeGridRowSnapshot> destination,
        IEnumerable<NativeGridRowSnapshot> observedRows,
        Func<NativeGridRowSnapshot, NativeGridRowSnapshot, bool> representSameRow)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(observedRows);
        ArgumentNullException.ThrowIfNull(representSameRow);

        foreach (var row in observedRows)
        {
            var existingIndex = destination.FindIndex(existing => representSameRow(row, existing));
            if (existingIndex < 0)
            {
                destination.Add(row);
            }
            else
            {
                destination[existingIndex] = row;
            }
        }
    }
}
