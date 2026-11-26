using System.Drawing;
using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Input;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace AppAutomation.FlaUI.Automation;

public sealed partial class FlaUiControlResolver
{
    private sealed class FlaUiNamedItemSurfaceControl(AutomationElement surface, Func<AutomationElement> resolve)
        : FlaUiControlBase<AutomationElement>(surface), INamedItemSurfaceControl
    {
        public void DoubleClickItem(string itemName, int timeoutMs)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
            using var preparation = EnterPointerPreparation();
            var budget = UiOperationTimeoutBudget.Start(timeoutMs, $"double click on named item '{itemName}'");
            var root = resolve();
            if (!root.IsEnabled || root.IsOffscreen)
                throw new InvalidOperationException($"Named surface '{AutomationId}' is not visible and enabled.");

            var rootId = root.Properties.RuntimeId.Value;
            var window = DesktopPointerTargetResolver.FindWindow(root);
            var processId = root.Properties.ProcessId.Value;
            var metadata = root.Properties.ItemStatus.ValueOrDefault;
            var initial = new NamedItemSurfaceSnapshot<AutomationElement>(AutomationId, metadata,
                root.FindAllChildren(static conditions => conditions.ByControlType(ControlType.ListItem))
                    .Select(peer =>
                    {
                        _ = budget.Remaining;
                        return new NamedItemSurfaceItem<AutomationElement>(peer.AutomationId, peer.Name, peer);
                    }));
            RequireSamePublication(root);
            var original = initial.Find(itemName);
            if (!original.Element.IsEnabled)
                throw new InvalidOperationException($"Named item '{itemName}' is disabled.");

            if (VisibleBounds(root, original.Element).IsEmpty)
            {
                if (!original.Element.Patterns.ScrollItem.TryGetPattern(out var scroll))
                    throw new InvalidOperationException($"Named item '{itemName}' does not support scrolling into view.");
                _ = budget.Remaining;
                scroll.ScrollIntoView();
            }

            Point point = default;
            DesktopPointer.ClickAt(ResolveCurrentSurface,
                resolvePoint: current =>
                {
                    var bounds = VisibleBounds(current, ResolveItem(current));
                    if (bounds.IsEmpty)
                        throw new InvalidOperationException($"Named item '{itemName}' did not become visible.");
                    point = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                    return point;
                },
                options: new PointerClickOptions
                {
                    ClickCount = 2,
                    Timeout = budget.Remaining,
                    RejectInteractiveDescendantHit = true
                },
                initialTarget: root,
                validateBeforePress: () =>
                {
                    using var dpi = WindowsPointerBackend.PhysicalDpiScope.Enter();
                    var current = ResolveCurrentSurface();
                    var item = ResolveItem(current);
                    var hit = current.Automation.FromPoint(point);
                    if (!VisibleBounds(current, item).Contains(point)
                        || hit is null || !hit.Equals(current) && !hit.Equals(item))
                        throw new InvalidOperationException($"The click area of named item '{itemName}' moved or is occluded.");
                    _ = budget.Remaining;
                });

            AutomationElement ResolveCurrentSurface()
            {
                _ = budget.Remaining;
                var current = resolve();
                RequireSamePublication(current);
                return current;
            }

            void RequireSamePublication(AutomationElement current)
            {
                _ = budget.Remaining;
                if (rootId is not { Length: > 0 } || !rootId.AsSpan().SequenceEqual(current.Properties.RuntimeId.Value)
                    || current.Properties.ProcessId.Value != processId
                    || DesktopPointerTargetResolver.FindWindow(current) != window
                    || !current.IsEnabled || current.IsOffscreen
                    || !string.Equals(metadata, current.Properties.ItemStatus.ValueOrDefault, StringComparison.Ordinal))
                    throw new InvalidOperationException("The named surface changed owner or item set during the gesture.");
            }

            AutomationElement ResolveItem(AutomationElement current)
            {
                // The full set was checked before reveal. While the published revision is unchanged,
                // only the target's live projection and geometry need to be re-read before each press.
                var matches = current.FindAllChildren(conditions => conditions.ByAutomationId(original.Key)).Distinct().ToArray();
                if (matches.Length != 1 || matches[0].ControlType != ControlType.ListItem
                    || !string.Equals(matches[0].Name.Trim(), original.Name, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Named item '{itemName}' changed identity during the gesture.");
                RequireSamePublication(current);
                return matches[0];
            }
        }

        private static Rectangle VisibleBounds(AutomationElement root, AutomationElement item)
        {
            if (!item.IsEnabled || item.IsOffscreen)
                return Rectangle.Empty;

            var bounds = Rectangle.Intersect(root.BoundingRectangle, item.BoundingRectangle);
            return bounds.Width > 0 && bounds.Height > 0 ? bounds : Rectangle.Empty;
        }
    }
}
