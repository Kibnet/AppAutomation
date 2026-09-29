namespace AppAutomation.Abstractions;

/// <summary>
/// Provides plain-text clipboard access for an automation runtime.
/// </summary>
public interface IUiClipboardRuntime
{
    /// <summary>
    /// Replaces the current clipboard text.
    /// </summary>
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// Clipboard actions shared by all UI automation providers.
/// </summary>
public static class ClipboardPageExtensions
{
    /// <summary>
    /// Copies text to the clipboard owned by the current automation runtime.
    /// </summary>
    public static async Task<TSelf> CopyTextToClipboardAsync<TSelf>(
        this TSelf page,
        string text,
        CancellationToken cancellationToken = default)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(text);

        if (!page.Capabilities.SupportsClipboardWrite
            || page.ResolverInternal is not IUiClipboardRuntime clipboard)
        {
            throw new NotSupportedException(
                $"Runtime adapter '{page.Capabilities.AdapterId}' does not support writing text to the clipboard.");
        }

        await clipboard.SetTextAsync(text, cancellationToken).ConfigureAwait(false);
        return page;
    }
}
