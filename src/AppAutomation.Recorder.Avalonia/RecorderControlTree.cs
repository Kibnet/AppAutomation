using System.Collections.Concurrent;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace AppAutomation.Recorder.Avalonia;

internal static class RecorderControlTree
{
    private static readonly string[] DetachedContentPropertyNames = ["PopupContent", "Child", "Content"];
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> DetachedContentProperties = new();

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
