using System.Drawing;

namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal static class GridCellClickGeometry
{
    internal static bool? HorizontalRevealDirection(Rectangle target, Rectangle viewport)
    {
        if (target.Width <= 0 || viewport.Width <= 0) return null;
        if (target.Right <= viewport.Left) return false;
        if (target.Left >= viewport.Right) return true;
        return null;
    }

    internal static Point CenterOfVisibleIntersection(
        Rectangle cellBounds,
        IEnumerable<Rectangle> ancestorBounds)
    {
        ArgumentNullException.ThrowIfNull(ancestorBounds);
        if (cellBounds.Width <= 0 || cellBounds.Height <= 0)
        {
            throw new InvalidOperationException(
                "The grid cell does not expose positive bounds for pointer input.");
        }

        var visible = cellBounds;
        foreach (var clip in ancestorBounds)
        {
            if (clip.Width <= 0 || clip.Height <= 0)
            {
                continue;
            }

            visible = Rectangle.Intersect(visible, clip);
            if (visible.Width <= 0 || visible.Height <= 0)
            {
                throw new InvalidOperationException(
                    "The grid cell has no visible area inside its viewport and owning window.");
            }
        }

        return new Point(
            checked(visible.Left + visible.Width / 2),
            checked(visible.Top + visible.Height / 2));
    }
}
