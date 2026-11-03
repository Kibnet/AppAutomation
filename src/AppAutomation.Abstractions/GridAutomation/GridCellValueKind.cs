namespace AppAutomation.Abstractions;

/// <summary>Represents the semantic kind of a grid cell value.</summary>
public enum GridCellValueKind
{
    Text = 0,
    Number = 1,
    Date = 2,
    Time = 3,
    Boolean = 4,
    Selection = 5,
    Reference = 6,
    Color = 7,
    /// <summary>A wall-clock date and time, without timezone conversion.</summary>
    DateTime = 8
}
