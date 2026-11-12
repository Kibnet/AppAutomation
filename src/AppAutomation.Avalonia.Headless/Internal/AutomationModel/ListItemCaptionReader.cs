using System.Collections.Specialized;
using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;

namespace AppAutomation.Avalonia.Headless.Internal.AutomationModel;

internal static class ListItemCaptionReader
{
    public static T ReadSnapshot<T>(ItemsControl itemsControl, Func<Func<int, string?>, T> read,
        int timeoutMs = 5000)
    {
        var budget = UiOperationTimeoutBudget.Start(timeoutMs, "list snapshot read");
        var changed = false;
        void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args) => changed = true;
        void EnsureUnchanged()
        {
            if (changed)
                throw new ItemsChangedDuringReadException();
        }
        string? ReadAt(int index)
        {
            EnsureUnchanged();
            return itemsControl is global::Avalonia.Controls.ComboBox combo
                ? ReadComboBoxCaption(combo, index)
                : ReadListBoxCaption(itemsControl, index, EnsureUnchanged);
        }

        itemsControl.Items.CollectionChanged += OnItemsChanged;
        try
        {
            while (true)
            {
                itemsControl.Dispatcher.RunJobs();
                changed = false;

                try
                {
                    var result = read(ReadAt);
                    EnsureUnchanged();
                    return result;
                }
                catch (ItemsChangedDuringReadException)
                {
                    // Retry only a proven collection change, never an unreadable caption.
                    _ = budget.Remaining;
                }
            }
        }
        finally
        {
            itemsControl.Items.CollectionChanged -= OnItemsChanged;
        }
    }

    private static string? ReadListBoxCaption(ItemsControl list, int index, Action ensureUnchanged)
    {
        var item = list.Items[index];
        if (item is null)
        {
            return null;
        }

        var container = list.ContainerFromIndex(index);
        if (container is null && (list.ItemTemplate is not null || item is Control))
        {
            list.ScrollIntoView(index);
            list.UpdateLayout();
            list.Dispatcher.RunJobs();
            list.UpdateLayout();
            ensureUnchanged();
            container = list.ContainerFromIndex(index);
        }

        ensureUnchanged();
        return ReadItemCaption(item, list.ItemTemplate, container: container) ?? throw UnreadableCaption(list, index);
    }

    private static string? ReadComboBoxCaption(global::Avalonia.Controls.ComboBox combo, int index)
    {
        if (combo.Items[index] is not { } item) return null;
        var binding = item is Control ? null : combo.DisplayMemberBinding;
        var template = binding is null && item is not Control ? combo.FindDataTemplate(item, combo.ItemTemplate) : null;
        return ReadItemCaption(item, template, container: combo.ContainerFromIndex(index), display: binding is null ? null : ReadBinding)
            ?? throw UnreadableCaption(combo, index);

        string? ReadBinding(object value) => new global::Avalonia.Controls.TextBlock
        {
            DataContext = value,
            [!global::Avalonia.Controls.TextBlock.TextProperty] = binding!
        }.Text;
    }

    internal static string? ReadItemCaption(object item, IDataTemplate? template = null, string? displayMember = null,
        Control? container = null, Func<object, string?>? display = null)
    {
        var contentHost = item as ContentControl;
        var content = contentHost is null ? item : contentHost.Content;
        var ignoredText = content is not null && !HasDisplayString(content) ? content.GetType().FullName : null;
        if ((container ?? item as Control) is { } root && ReadVisualCaption(root, ignoredText) is { } visible)
            return visible;
        if (content is null) return null;
        if (content is Control visual) return ReadVisualCaption(visual);

        // A self-container may not have been templated yet. Render its content template in a
        // detached presenter instead of opening the popup or reparenting an existing control.
        var effectiveTemplate = contentHost?.ContentTemplate ?? (item is Control ? null : template);
        if (effectiveTemplate is not null) return ReadTemplatedCaption(content, effectiveTemplate, ignoredText);
        if (display is not null) return display(content);
        if (!string.IsNullOrWhiteSpace(displayMember) && contentHost is null)
            return GridPropertyValueReader.TryReadPath(item, displayMember, out var value) ? ReadScalarCaption(value) : null;
        return ReadScalarCaption(content);
    }

    private static string? ReadScalarCaption(object? value) => value is not null && HasDisplayString(value) ? value.ToString() : null;

    private static string? ReadTemplatedCaption(object item, IDataTemplate template, string? ignoredText)
    {
        var presenter = new ContentPresenter { Content = item, ContentTemplate = template };
        presenter.UpdateChild();
        return ReadVisualCaption(presenter, ignoredText);
    }

    internal static string? ReadVisualCaption(Control root, string? ignoredText = null)
    {
        var controls = EnumerateShown(root).OfType<Control>().ToArray();
        var accessibleName = controls.Select(AutomationProperties.GetName)
            .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name));
        if (accessibleName is not null) return accessibleName;

        var text = controls.OfType<global::Avalonia.Controls.TextBlock>().Select(static block => block.Text).ToArray();
        if (text.Length == 0) return null;
        var caption = string.Join(" ", text.Where(static value => !string.IsNullOrWhiteSpace(value)));
        return caption == ignoredText ? null : caption;
    }

    private static IEnumerable<Visual> EnumerateShown(Visual visual)
    {
        if (!visual.IsVisible) yield break;
        yield return visual;
        foreach (var child in visual.GetVisualChildren())
        foreach (var descendant in EnumerateShown(child))
            yield return descendant;
    }

    private static UiControlResolutionException UnreadableCaption(ItemsControl items, int index) =>
        new(UiControlResolutionFailure.Detached,
            $"List '{AutomationProperties.GetAutomationId(items)}' item at index {index} does not expose a readable UI caption. " +
            "Expose the item template text or AutomationProperties.Name on its real container.");

    private sealed class ItemsChangedDuringReadException : Exception;

    private static bool HasDisplayString(object item)
    {
        var declaringType = item.GetType().GetMethod(nameof(ToString), Type.EmptyTypes)?.DeclaringType;
        return declaringType is not null && declaringType != typeof(object) && declaringType != typeof(ValueType);
    }
}
