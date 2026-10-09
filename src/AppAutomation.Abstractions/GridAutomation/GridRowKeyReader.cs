namespace AppAutomation.Abstractions;

/// <summary>Reads every configured identity part from one logical row in the current grid view.</summary>
public interface IGridRowPositionIdentityControl
{
    GridRowPositionIdentity ReadRowIdentityAtPosition(
        int position,
        IReadOnlyList<GridRuntimeColumn> identityColumns,
        string rowPath,
        int timeoutMs);
}

/// <summary>
/// Identity parts captured from one view position, with an optional uniqueness proof from the same provider scan.
/// </summary>
public sealed record GridRowPositionIdentity(IReadOnlyList<string?> Values, bool IsUnique);

/// <summary>Bootstraps a stable selector from a deliberately chosen view position.</summary>
public static class GridRowKeyReader
{
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
