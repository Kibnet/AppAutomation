namespace AppAutomation.Abstractions;

/// <summary>Describes optional input, popup and lifecycle parts of one cell editor.</summary>
public sealed record GridCellEditorParts(
    GridRelativeLocator? Input = null,
    GridRelativeLocator? Results = null,
    GridRelativeLocator? OpenButton = null,
    GridRelativeLocator? ConfirmButton = null,
    GridRelativeLocator? CancelButton = null);
