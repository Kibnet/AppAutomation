namespace AppAutomation.Abstractions;

/// <summary>Reads every configured identity part from one logical row in the current grid view.</summary>
public interface IGridRowPositionIdentityControl
{
    GridRowPositionIdentity ReadRowIdentityAtPosition(
        int position,
        IReadOnlyList<GridRuntimeColumn> identityColumns,
        string rowPath,
        int timeoutMs);

    GridRowPositionIdentity ReadFirstAvailableRowIdentity(
        IReadOnlyList<GridRuntimeColumn> identityColumns, string rowPath, int timeoutMs) =>
        throw new NotSupportedException("The grid provider does not support capturing an available row.");

    GridRowPositionIdentity ReadFirstAvailableRowIdentity(
        IReadOnlyList<GridRuntimeColumn> identityColumns, string rowPath,
        IReadOnlyList<IReadOnlyList<string?>> excludedIdentities, int timeoutMs) =>
        excludedIdentities.Count == 0
            ? ReadFirstAvailableRowIdentity(identityColumns, rowPath, timeoutMs)
            : throw new NotSupportedException("The grid provider does not support capturing a different available row.");
}

/// <summary>
/// Identity parts captured from one view position, with an optional uniqueness proof from the same provider scan.
/// </summary>
public sealed record GridRowPositionIdentity(IReadOnlyList<string?> Values, bool IsUnique);

/// <summary>Captures stable selectors from the current grid view.</summary>
public static class GridRowKeyReader
{
    /// <summary>Chooses any available data row once; subsequent operations use its captured stable identity.</summary>
    public static GridRowSelector CaptureFirst(IGridControl grid, int timeoutMs = 5000)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        return grid is ConfiguredGridControl configured
            ? configured.CaptureRowKey(0, timeoutMs, firstAvailable: true)
            : throw new InvalidOperationException($"Grid '{grid.AutomationId}' needs a GridAutomationCatalog registration to capture a stable row key.");
    }

    /// <summary>Captures an available row different from previously captured rows of the same grid.</summary>
    public static GridRowSelector CaptureFirst(IGridControl grid, IReadOnlyList<GridRowSelector> excludingRows, int timeoutMs = 5000)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(excludingRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        return grid is ConfiguredGridControl configured
            ? configured.CaptureRowKey(0, timeoutMs, firstAvailable: true, excludingRows)
            : throw new InvalidOperationException($"Grid '{grid.AutomationId}' needs a GridAutomationCatalog registration to capture a stable row key.");
    }

    public static GridRowSelector Capture(IGridControl grid, int position, int timeoutMs = 5000)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        if (grid is not ConfiguredGridControl configured)
        {
            throw new InvalidOperationException(
                $"Grid '{grid.AutomationId}' needs a GridAutomationCatalog registration to capture a stable row key.");
        }

        return configured.CaptureRowKey(position, timeoutMs);
    }
}
