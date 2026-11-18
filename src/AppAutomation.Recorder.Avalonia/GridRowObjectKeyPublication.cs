using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

/// <summary>Publishes the current data-row object's reference-lifetime key in the real row's ItemStatus.</summary>
public sealed class GridRowObjectKeyPublication : AvaloniaObject
{
    private static readonly AttachedProperty<string?> PublishedKeyProperty =
        AvaloniaProperty.RegisterAttached<GridRowObjectKeyPublication, Control, string?>("PublishedKey");

    /// <summary>Enables object-key publication only on explicitly opted-in data-row controls.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<GridRowObjectKeyPublication, Control, bool>(
            "IsEnabled", coerce: static (owner, enabled) =>
            {
                if (enabled) EnsureAvailable((Control)owner);
                return enabled;
            });

    static GridRowObjectKeyPublication() =>
        IsEnabledProperty.Changed.AddClassHandler<Control>(static (control, _) => OnEnabledChanged(control));

    private GridRowObjectKeyPublication() { }

    public static bool GetIsEnabled(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(Control control, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(IsEnabledProperty, enabled);
    }

    private static void OnEnabledChanged(Control control)
    {
        control.DataContextChanged -= OnRowUpdated;
        control.LayoutUpdated -= OnRowUpdated;
        if (!GetIsEnabled(control))
        {
            ClearOwnedValue(control);
            return;
        }

        control.DataContextChanged += OnRowUpdated;
        control.LayoutUpdated += OnRowUpdated;
        PublishCurrentRow(control);
    }

    private static void OnRowUpdated(object? sender, EventArgs args)
    {
        if (sender is Control control && GetIsEnabled(control)) PublishCurrentRow(control);
    }

    private static void PublishCurrentRow(Control control)
    {
        try
        {
            EnsureAvailable(control);
            if (control.DataContext is not { } row)
            {
                ClearOwnedValue(control);
                return;
            }

            // Equality-based DataContext notifications can be suppressed during recycling. A layout
            // pass reads the actual current reference, without retaining the previous row object.
            var key = GridRowObjectKey.Get(row);
            if (string.Equals(control.GetValue(PublishedKeyProperty), key, StringComparison.Ordinal)) return;
            control.SetValue(PublishedKeyProperty, key);
            AutomationProperties.SetItemStatus(control, key);
        }
        catch
        {
            // Preserve foreign metadata and avoid repeating an integration error on every layout.
            SetIsEnabled(control, false);
            throw;
        }
    }

    private static void EnsureAvailable(Control control)
    {
        var owned = control.GetValue(PublishedKeyProperty);
        var actual = AutomationProperties.GetItemStatus(control);
        if (owned is null ? control.IsSet(AutomationProperties.ItemStatusProperty)
            : !string.Equals(actual, owned, StringComparison.Ordinal))
            throw new InvalidOperationException("GridRowObjectKeyPublication requires exclusive ownership of the data row ItemStatus. "
                + "Remove the existing ItemStatus binding/metadata or disable object-key publication; foreign metadata is not overwritten.");
    }

    private static void ClearOwnedValue(Control control)
    {
        var owned = control.GetValue(PublishedKeyProperty);
        control.ClearValue(PublishedKeyProperty);
        if (owned is not null && string.Equals(AutomationProperties.GetItemStatus(control), owned, StringComparison.Ordinal))
            control.ClearValue(AutomationProperties.ItemStatusProperty);
    }
}
