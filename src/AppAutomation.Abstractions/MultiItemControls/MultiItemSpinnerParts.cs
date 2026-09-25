namespace AppAutomation.Abstractions;

/// <summary>Describes the real parts of a Spinner repeated inside collection items.</summary>
public sealed record MultiItemSpinnerParts
{
    public MultiItemSpinnerParts(
        MultiItemRelativeLocator root,
        MultiItemRelativeLocator input,
        MultiItemRelativeLocator? commitTarget = null,
        bool useKeyboardInput = true)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Input = input ?? throw new ArgumentNullException(nameof(input));
        CommitTarget = commitTarget;
        UseKeyboardInput = useKeyboardInput;

        if (Root.Scope != MultiItemRelativeLocatorScope.ItemRoot)
        {
            throw new ArgumentException("Spinner root must be resolved from ItemRoot.", nameof(root));
        }

        if (Input.Scope != MultiItemRelativeLocatorScope.ControlRoot)
        {
            throw new ArgumentException("Spinner input must be resolved from ControlRoot.", nameof(input));
        }

        if (CommitTarget is not null
            && CommitTarget.Scope == MultiItemRelativeLocatorScope.ControlRoot)
        {
            throw new ArgumentException(
                "Commit target cannot use ControlRoot because the active editor may be replaced during commit.",
                nameof(commitTarget));
        }
    }

    public MultiItemRelativeLocator Root { get; }

    public MultiItemRelativeLocator Input { get; }

    public MultiItemRelativeLocator? CommitTarget { get; }

    public bool UseKeyboardInput { get; }
}
