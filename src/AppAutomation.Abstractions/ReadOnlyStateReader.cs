namespace AppAutomation.Abstractions;

/// <summary>
/// Reads a control's confirmed editability without inferring it from enabled state.
/// </summary>
public static class ReadOnlyStateReader
{
    /// <summary>
    /// Returns the current read-only state, or fails if the provider cannot observe it.
    /// </summary>
    public static bool Read(IUiControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control is IReadOnlyStateControl readable && readable.TryGetIsReadOnly(out var isReadOnly))
        {
            return isReadOnly;
        }

        throw new InvalidOperationException(
            $"Control '{control.AutomationId}' ({control.GetType().Name}) does not expose a readable read-only state.");
    }
}
