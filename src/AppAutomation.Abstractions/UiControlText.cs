namespace AppAutomation.Abstractions;

/// <summary>
/// Reads user-visible text from controls that explicitly expose a readable text capability.
/// </summary>
public static class UiControlText
{
    /// <summary>
    /// Reads the visible text exposed by <paramref name="control"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The control does not implement <see cref="IReadableTextControl"/>.
    /// </exception>
    public static string Read(IUiControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control is IReadableTextControl readable
            ? readable.Text
            : throw new InvalidOperationException(
                $"Control '{control.AutomationId}' does not expose readable visible text in adapter contract.");
    }
}
