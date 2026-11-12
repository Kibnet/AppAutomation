using System.Collections;
using System.Reflection;

namespace AppAutomation.Abstractions;

// Accesses the control's public selection surface, never its DataContext or binding source.
internal static class NativeGridSelectionAccess
{
    internal static bool SupportsMultiple(object control) =>
        GridPropertyValueReader.TryReadProperty(control, "SelectionMode", out var mode)
        && mode?.ToString() is "Multiple" or "Extended";

    internal static IEnumerable? ReadItems(object control) =>
        GridPropertyValueReader.TryReadProperty(control, "SelectedItems", out var items)
            && items is IEnumerable selection and not string ? selection : null;

    internal static bool TryReadCurrentView(object control, out object?[] items)
    {
        foreach (var property in new[] { "CollectionView", "ItemsView" })
        {
            if (GridPropertyValueReader.TryReadProperty(control, property, out var view)
                && view is IEnumerable sequence and not string)
            {
                items = sequence.Cast<object?>().ToArray();
                return true;
            }
        }

        if (GridPropertyValueReader.TryReadProperty(control, "VisibleRowCount", out var countValue)
            && countValue is int count && count >= 0
            && control.GetType().GetMethod("GetSourceItemByVisibleRowIndex", BindingFlags.Public | BindingFlags.Instance,
                binder: null, types: [typeof(int)], modifiers: null) is { } getItem)
        {
            items = Enumerable.Range(0, count).Select(index => getItem.Invoke(control, [index]))
                .Where(static item => item is not null).ToArray();
            return true;
        }

        items = [];
        return false;
    }
}
