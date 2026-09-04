namespace AppAutomation.Abstractions;

/// <summary>Describes whether a stable row selector found no row, one row, or several rows.</summary>
public enum GridRowResolutionState
{
    NotFound = 0,
    Unique = 1,
    Ambiguous = 2
}
