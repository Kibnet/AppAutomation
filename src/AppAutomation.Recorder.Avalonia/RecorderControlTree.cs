using System.Collections.Concurrent;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace AppAutomation.Recorder.Avalonia;

internal static class RecorderControlTree
{
    private static readonly string[] DetachedContentPropertyNames = ["PopupContent", "Child", "Content"];
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> DetachedContentProperties = new();

    public static IEnumerable<Control> EnumerateRelatedControls(
        Control? control,
        bool includePopupPlacementTarget = false)
    {
        if (control is null)
        {
            yield break;
        }

        var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<Control>();
        queue.Enqueue(control);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;

            if (current.GetVisualParent() is Control visualParent)
            {
                queue.Enqueue(visualParent);
            }

            if (current is ILogical { LogicalParent: Control logicalParent })
            {
                queue.Enqueue(logicalParent);
            }

            if (current is StyledElement { TemplatedParent: Control templatedParent })
            {
                queue.Enqueue(templatedParent);
            }

            if (includePopupPlacementTarget
                && current is Popup { PlacementTarget: Control placementTarget })
            {
                queue.Enqueue(placementTarget);
            }
        }
    }

    public static IEnumerable<Control> EnumerateReachableControls(
        Control root,
        bool includeLogicalDescendants = true)
    {
        var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var pendingRoots = new Queue<Control>();
        pendingRoots.Enqueue(root);
        while (pendingRoots.Count > 0)
        {
            var subtreeRoot = pendingRoots.Dequeue();
            var descendants = subtreeRoot.GetVisualDescendants().OfType<Control>();
            if (includeLogicalDescendants)
            {
                descendants = descendants.Concat(subtreeRoot.GetLogicalDescendants().OfType<Control>());
            }

            foreach (var candidate in descendants.Prepend(subtreeRoot))
            {
                if (!visited.Add(candidate))
                {
                    continue;
                }

                yield return candidate;
                foreach (var detachedRoot in EnumerateDetachedContentRoots(candidate))
                {
                    if (!visited.Contains(detachedRoot))
                    {
                        pendingRoots.Enqueue(detachedRoot);
                    }
                }
            }
        }
    }

    public static HashSet<Control> CollectObservableControls(
        Control root,
        Func<Control, bool> isObservable)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(isObservable);

        var controls = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        CollectObservableControls(root, controls, visited, isObservable);
        return controls;
    }

    private static void CollectObservableControls(
        Control root,
        ISet<Control> controls,
        ISet<Control> visited,
        Func<Control, bool> isObservable)
    {
        foreach (var control in root.GetVisualDescendants().OfType<Control>().Prepend(root))
        {
            if (!visited.Add(control))
            {
                continue;
            }

            if (isObservable(control))
            {
                controls.Add(control);
            }

            foreach (var detachedRoot in EnumerateDetachedContentRoots(control))
            {
                CollectObservableControls(detachedRoot, controls, visited, isObservable);
            }

            if (control.ContextMenu is { } contextMenu)
            {
                CollectMenuItems(contextMenu.Items.OfType<MenuItem>(), controls, visited);
            }

            if (control.ContextFlyout is MenuFlyout menuFlyout)
            {
                CollectMenuItems(menuFlyout.Items.OfType<MenuItem>(), controls, visited);
            }
        }

        foreach (var menuItem in root.GetLogicalDescendants().OfType<MenuItem>())
        {
            if (visited.Add(menuItem))
            {
                controls.Add(menuItem);
            }
        }

        var menus = root.GetVisualDescendants().OfType<Menu>();
        if (root is Menu rootMenu)
        {
            menus = menus.Prepend(rootMenu);
        }

        foreach (var menu in menus)
        {
            CollectMenuItems(menu.Items.OfType<MenuItem>(), controls, visited);
        }
    }

    private static void CollectMenuItems(
        IEnumerable<MenuItem> items,
        ISet<Control> controls,
        ISet<Control> visited)
    {
        foreach (var item in items)
        {
            if (visited.Add(item))
            {
                controls.Add(item);
            }

            CollectMenuItems(item.Items.OfType<MenuItem>(), controls, visited);
        }
    }

    public static IEnumerable<Control> EnumerateDetachedContentRoots(Control control)
    {
        if (control is Popup { IsOpen: false })
        {
            yield break;
        }

        foreach (var flyout in EnumerateAssociatedFlyouts(control))
        {
            if (flyout is Flyout { IsOpen: true, Content: Control contentRoot })
            {
                yield return contentRoot;
            }
        }

        foreach (var property in GetDetachedContentProperties(control.GetType()))
        {
            if (property.GetValue(control) is Control contentRoot)
            {
                yield return contentRoot;
            }
        }
    }

    public static IEnumerable<FlyoutBase> EnumerateAssociatedFlyouts(Control control)
    {
        if (control.ContextFlyout is { } contextFlyout)
        {
            yield return contextFlyout;
        }

        if (control is Button { Flyout: { } buttonFlyout })
        {
            yield return buttonFlyout;
        }

        if (control is SplitButton { Flyout: { } splitButtonFlyout })
        {
            yield return splitButtonFlyout;
        }

        if (control.GetValue(FlyoutBase.AttachedFlyoutProperty) is { } attachedFlyout)
        {
            yield return attachedFlyout;
        }
    }

    public static bool ExposesDetachedPopupContent(Control control) =>
        GetDetachedContentProperties(control.GetType())
            .Any(static property => property.Name == "PopupContent");

    private static PropertyInfo[] GetDetachedContentProperties(Type type) =>
        DetachedContentProperties.GetOrAdd(type, static currentType =>
        {
            var readableProperties = currentType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
                .ToArray();
            return DetachedContentPropertyNames
                .Select(name => readableProperties.FirstOrDefault(property => property.Name == name))
                .OfType<PropertyInfo>()
                .ToArray();
        });
}
