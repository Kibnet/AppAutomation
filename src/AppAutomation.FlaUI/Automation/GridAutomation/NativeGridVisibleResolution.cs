namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal static class NativeGridVisibleResolution
{
    public static bool TryResolve<TSnapshot, TRow>(
        bool hasDeclaredUniqueIdentity,
        IReadOnlyList<TSnapshot> snapshots,
        IReadOnlyList<TRow> visibleRows,
        Func<TSnapshot, bool> snapshotMatches,
        Func<TRow, bool> liveRowMatches,
        out TSnapshot[] matchingSnapshots,
        out TRow? liveMatchingRow)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(visibleRows);
        ArgumentNullException.ThrowIfNull(snapshotMatches);
        ArgumentNullException.ThrowIfNull(liveRowMatches);

        matchingSnapshots = snapshots.Where(snapshotMatches).ToArray();
        liveMatchingRow = default;
        if (!hasDeclaredUniqueIdentity || matchingSnapshots.Length == 0)
        {
            return false;
        }

        if (matchingSnapshots.Length == 1)
        {
            liveMatchingRow = visibleRows.FirstOrDefault(liveRowMatches);
        }

        return true;
    }
}
