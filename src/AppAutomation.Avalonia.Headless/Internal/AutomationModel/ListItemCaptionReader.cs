using System.Collections.Specialized;
using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AppAutomation.Avalonia.Headless.Internal.AutomationModel;

internal static class ListItemCaptionReader
{
    public static T ReadSnapshot<T>(global::Avalonia.Controls.ListBox list, Func<Func<int, string?>, T> read,
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

        list.Items.CollectionChanged += OnItemsChanged;
        try
        {
            while (true)
            {
                list.Dispatcher.RunJobs();
                changed = false;

                try
                {
                    var result = read(index => ReadCore(list, index, EnsureUnchanged));
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
            list.Items.CollectionChanged -= OnItemsChanged;
        }
    }

    private static string? ReadCore(global::Avalonia.Controls.ListBox list, int index, Action ensureUnchanged)
    {
        ensureUnchanged();
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

        var captionRoot = container ?? item as Control;
        if (captionRoot is not null)
        {
            var controls = captionRoot.GetVisualDescendants().OfType<Control>()
                .Prepend(captionRoot).Where(static control => control.IsVisible).ToArray();
            var accessibleName = controls.Select(AutomationProperties.GetName)
                .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name));
            if (accessibleName is not null)
            {
                return accessibleName;
            }

            var text = controls.OfType<global::Avalonia.Controls.TextBlock>()
                .Select(static block => block.Text ?? string.Empty).ToArray();
            if (text.Length > 0)
            {
                var caption = string.Join(" ", text.Where(static value => !string.IsNullOrWhiteSpace(value)));
                if (list.ItemTemplate is not null || HasDisplayString(item) || caption != item.GetType().FullName)
                {
                    return caption;
                }
            }
        }

        if (list.ItemTemplate is null && item is not Control && HasDisplayString(item))
        {
            return item.ToString();
        }

        ensureUnchanged();
        throw new UiControlResolutionException(UiControlResolutionFailure.Detached,
            $"List '{AutomationProperties.GetAutomationId(list)}' item at index {index} does not expose a readable UI caption. " +
            "Expose the item template text or AutomationProperties.Name on its real container.");
    }

    private sealed class ItemsChangedDuringReadException : Exception;

    private static bool HasDisplayString(object item)
    {
        var declaringType = item.GetType().GetMethod(nameof(ToString), Type.EmptyTypes)?.DeclaringType;
        return declaringType is not null && declaringType != typeof(object) && declaringType != typeof(ValueType);
    }
}
