namespace AppAutomation.Abstractions;

/// <summary>Describes optional input, popup and lifecycle parts of one cell editor.</summary>
public sealed record GridCellEditorParts(
    GridRelativeLocator? Input = null,
    GridRelativeLocator? Results = null,
    GridRelativeLocator? OpenButton = null,
    GridRelativeLocator? ConfirmButton = null,
    GridRelativeLocator? CancelButton = null,
    GridRelativeLocator? CommitTarget = null,
    bool UseKeyboardInput = false)
{
    public GridCellEditorParts(
        GridRelativeLocator? input,
        GridRelativeLocator? results,
        GridRelativeLocator? openButton,
        GridRelativeLocator? confirmButton,
        GridRelativeLocator? cancelButton)
        : this(input, results, openButton, confirmButton, cancelButton, null, false)
    {
    }

    public void Deconstruct(
        out GridRelativeLocator? input,
        out GridRelativeLocator? results,
        out GridRelativeLocator? openButton,
        out GridRelativeLocator? confirmButton,
        out GridRelativeLocator? cancelButton)
    {
        input = Input;
        results = Results;
        openButton = OpenButton;
        confirmButton = ConfirmButton;
        cancelButton = CancelButton;
    }
}
