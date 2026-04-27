using System.Collections;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace AppAutomation.Avalonia.Headless.Internal.AutomationModel;

internal static class ControlTree
{
    public static IEnumerable<Control> EnumerateDescendants(Control root)
    {
        var seen = new HashSet<Control>();
        var queue = new Queue<object>();

        EnqueueChildren(root, queue);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is not Control control)
            {
                continue;
            }

            if (!seen.Add(control))
            {
                continue;
            }

            yield return control;

            EnqueueChildren(control, queue);
        }
    }

    private static void EnqueueChildren(Control control, Queue<object> queue)
    {
        foreach (var visualChild in control.GetVisualChildren())
        {
            queue.Enqueue(visualChild);
        }

        if (control is ILogical logical)
        {
            foreach (var logicalChild in logical.LogicalChildren)
            {
                queue.Enqueue(logicalChild);
            }
        }

        switch (control)
        {
            case ContentControl { Content: not null } contentControl:
                queue.Enqueue(contentControl.Content);
                break;
            case Decorator { Child: not null } decorator:
                queue.Enqueue(decorator.Child);
                break;
        }

        EnqueuePropertyValue(control, "Root", queue);
        EnqueuePropertyValue(control, "Content", queue);
        EnqueueEnumerablePropertyValues(control, "Items", queue);
    }

    private static void EnqueuePropertyValue(Control control, string propertyName, Queue<object> queue)
    {
        var property = control.GetType().GetProperty(propertyName);
        var value = property?.GetValue(control);
        if (value is not null)
        {
            queue.Enqueue(value);
        }
    }

    private static void EnqueueEnumerablePropertyValues(Control control, string propertyName, Queue<object> queue)
    {
        var property = control.GetType().GetProperty(propertyName);
        if (property?.GetValue(control) is not IEnumerable values)
        {
            return;
        }

        foreach (var value in values)
        {
            if (value is not null)
            {
                queue.Enqueue(value);
            }
        }
    }
}
