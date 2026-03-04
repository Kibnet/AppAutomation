using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace FlaUI.EasyUse.Helpers;

internal static class ControlTree
{
    public static IEnumerable<Control> EnumerateDescendants(Control root)
    {
        var seen = new HashSet<Control>();
        var queue = new Queue<object>();

        foreach (var child in root.GetVisualChildren())
        {
            queue.Enqueue(child);
        }

        if (root is ILogical logicalRoot)
        {
            foreach (var child in logicalRoot.LogicalChildren)
            {
                queue.Enqueue(child);
            }
        }

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
        }
    }
}