namespace AppAutomation.Abstractions;

/// <summary>Represents the outcome of resolving a stable row selector.</summary>
public sealed record GridRowResolution(GridRowResolutionState State, int MatchCount, string Description)
{
    public static GridRowResolution NotFound(string description) =>
        new(GridRowResolutionState.NotFound, 0, description);

    public static GridRowResolution Unique(string description) =>
        new(GridRowResolutionState.Unique, 1, description);

    public static GridRowResolution Ambiguous(int matchCount, string description) =>
        new(GridRowResolutionState.Ambiguous, matchCount, description);
}
