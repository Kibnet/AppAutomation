namespace AppAutomation.Abstractions;

/// <summary>Represents one logical collection of repeated controls addressed by stable item keys.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Collection is the public domain term for the logical repeated-control surface.")]
public interface IMultiItemControlCollection : IUiControlAvailability
{
    /// <summary>Resolves a Spinner in the uniquely identified collection item.</summary>
    ISpinnerControl ResolveSpinner(string itemKey, int timeoutMs);
}

/// <summary>Provider contract used by the shared multi-item adapter.</summary>
public interface IMultiItemControlRuntimeResolver
{
    /// <summary>Reads the current availability and enabled state of the configured collection root.</summary>
    MultiItemControlState ReadMultiItemCollectionState(MultiItemControlDefinition definition);

    /// <summary>Resolves the Spinner nested in the uniquely keyed item within the supplied timeout.</summary>
    ISpinnerControl ResolveMultiItemSpinner(
        MultiItemControlDefinition definition,
        string itemKey,
        int timeoutMs);
}

/// <summary>Represents the observable runtime state of a configured repeated-control collection.</summary>
/// <param name="IsAvailable">Whether the collection root currently exists and is visible.</param>
/// <param name="IsEnabled">Whether the available collection root is effectively enabled.</param>
public readonly record struct MultiItemControlState(bool IsAvailable, bool IsEnabled);
