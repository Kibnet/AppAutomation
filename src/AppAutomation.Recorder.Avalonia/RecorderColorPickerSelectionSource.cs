using AppAutomation.Abstractions;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

/// <summary>
/// Reports committed selections made by color pickers whose popup is not exposed through
/// standard Avalonia list or combo-box controls.
/// </summary>
public interface IRecorderColorPickerSelectionSource
{
    /// <summary>
    /// Occurs synchronously when the consumer control commits a color selection.
    /// </summary>
    event EventHandler<RecorderColorPickerSelectionConfirmedEventArgs>? SelectionConfirmed;
}

/// <summary>
/// A provider-neutral event broker for custom color picker controls.
/// </summary>
public sealed class RecorderColorPickerSelectionSource : IRecorderColorPickerSelectionSource
{
    public event EventHandler<RecorderColorPickerSelectionConfirmedEventArgs>? SelectionConfirmed;

    /// <summary>
    /// Reports a committed color using the stable logical picker root.
    /// </summary>
    public void ConfirmSelection(Control logicalRoot, string color)
    {
        ArgumentNullException.ThrowIfNull(logicalRoot);
        var canonical = ColorValue.Normalize(color);
        SelectionConfirmed?.Invoke(
            this,
            new RecorderColorPickerSelectionConfirmedEventArgs(logicalRoot, canonical));
    }
}

/// <summary>
/// Contains an immutable snapshot of a committed custom color selection.
/// </summary>
public sealed class RecorderColorPickerSelectionConfirmedEventArgs : EventArgs
{
    public RecorderColorPickerSelectionConfirmedEventArgs(Control logicalRoot, string color)
    {
        ArgumentNullException.ThrowIfNull(logicalRoot);
        LogicalRoot = logicalRoot;
        Color = ColorValue.Normalize(color);
    }

    public Control LogicalRoot { get; }

    public string Color { get; }
}
