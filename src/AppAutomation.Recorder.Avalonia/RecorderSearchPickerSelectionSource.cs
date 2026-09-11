using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

/// <summary>
/// Reports confirmed selections made by search-picker controls whose results are not exposed
/// as a standard Avalonia <see cref="ComboBox"/> or <see cref="ListBox"/>.
/// </summary>
public interface IRecorderSearchPickerSelectionSource
{
    /// <summary>
    /// Occurs synchronously after the consumer control has committed an object selection.
    /// </summary>
    event EventHandler<RecorderSearchPickerSelectionConfirmedEventArgs>? SelectionConfirmed;
}

/// <summary>
/// A provider-neutral event broker for reporting confirmed selections to Recorder.
/// </summary>
public sealed class RecorderSearchPickerSelectionSource : IRecorderSearchPickerSelectionSource
{
    /// <inheritdoc />
    public event EventHandler<RecorderSearchPickerSelectionConfirmedEventArgs>? SelectionConfirmed;

    /// <summary>
    /// Reports a confirmed selection synchronously on the UI thread, before popup cleanup,
    /// using the live search input, results root, and selected display text.
    /// </summary>
    /// <param name="searchInput">The text input associated with the logical search picker.</param>
    /// <param name="resultsRoot">The current popup results root.</param>
    /// <param name="selectedValue">The display text of the committed selection.</param>
    public void ConfirmSelection(TextBox searchInput, Control resultsRoot, string selectedValue)
    {
        ArgumentNullException.ThrowIfNull(searchInput);
        ArgumentNullException.ThrowIfNull(resultsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedValue);

        SelectionConfirmed?.Invoke(
            this,
            new RecorderSearchPickerSelectionConfirmedEventArgs(
                searchInput,
                resultsRoot,
                selectedValue.Trim()));
    }
}

/// <summary>
/// Contains an immutable snapshot of a committed search-picker selection.
/// </summary>
public sealed class RecorderSearchPickerSelectionConfirmedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a confirmed selection snapshot.
    /// </summary>
    public RecorderSearchPickerSelectionConfirmedEventArgs(
        TextBox searchInput,
        Control resultsRoot,
        string selectedValue)
    {
        ArgumentNullException.ThrowIfNull(searchInput);
        ArgumentNullException.ThrowIfNull(resultsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedValue);

        SearchInput = searchInput;
        ResultsRoot = resultsRoot;
        SelectedValue = selectedValue.Trim();
    }

    /// <summary>
    /// Gets the text input associated with the logical search picker.
    /// </summary>
    public TextBox SearchInput { get; }

    /// <summary>
    /// Gets the popup results root captured when the selection was committed.
    /// </summary>
    public Control ResultsRoot { get; }

    /// <summary>
    /// Gets the selected display text captured when the selection was committed.
    /// </summary>
    public string SelectedValue { get; }
}
