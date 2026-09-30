using System.Linq.Expressions;

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
/// Represents a text control that can receive the current clipboard text through a real paste interaction.
/// </summary>
public interface IClipboardPasteTarget : ITextBoxControl
{
    /// <summary>
    /// Replaces the control text through the provider's real paste interaction.
    /// </summary>
    Task PasteFromClipboardAsync(int timeoutMs, CancellationToken cancellationToken = default);
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

        if (!page.Capabilities.SupportsClipboardText
            || page.ResolverInternal is not IUiClipboardRuntime clipboard)
        {
            throw new NotSupportedException(
                $"Runtime adapter '{page.Capabilities.AdapterId}' does not support writing text to the clipboard.");
        }

        await clipboard.SetTextAsync(text, cancellationToken).ConfigureAwait(false);
        return page;
    }

    /// <summary>
    /// Pastes the current clipboard text into a text box and verifies its resulting value.
    /// </summary>
    public static async Task<TSelf> PasteTextFromClipboardAsync<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ITextBoxControl>> selector,
        string expectedText,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(expectedText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        cancellationToken.ThrowIfCancellationRequested();

        if (!page.Capabilities.SupportsClipboardText
            || page.ResolverInternal is not IUiClipboardRuntime)
        {
            throw new NotSupportedException(
                $"Runtime adapter '{page.Capabilities.AdapterId}' does not support pasting text from the clipboard.");
        }

        var budget = UiOperationTimeoutBudget.Start(timeoutMs, "clipboard-paste");
        var resolveTarget = selector.Compile();
        var target = resolveTarget(page)
            ?? throw new InvalidOperationException("Selector returned null.");
        if (target is not IClipboardPasteTarget pasteTarget)
        {
            throw new NotSupportedException(
                $"Text control '{target.AutomationId}' does not support a real clipboard paste interaction.");
        }

        await pasteTarget
            .PasteFromClipboardAsync(budget.RemainingMilliseconds, cancellationToken)
            .ConfigureAwait(false);

        var result = await UiWait.TryUntilAsync(
                () => resolveTarget(page).Text,
                actual => string.Equals(actual, expectedText, StringComparison.Ordinal),
                new UiWaitOptions
                {
                    Timeout = budget.Remaining,
                    PollInterval = TimeSpan.FromMilliseconds(50)
                },
                cancellationToken,
                page.LoggerInternal)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new TimeoutException(
                $"Text control '{target.AutomationId}' did not reach the expected clipboard value "
                + $"'{expectedText}'. Last observed value: '{result.Value}'.");
        }

        return page;
    }
}
