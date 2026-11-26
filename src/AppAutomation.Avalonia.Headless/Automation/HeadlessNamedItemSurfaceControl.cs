using System.Diagnostics;
using System.Runtime.CompilerServices;
using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Internal.AutomationModel;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AppAutomation.Avalonia.Headless.Automation;

public sealed partial class HeadlessControlResolver
{
    private sealed class HeadlessNamedItemSurfaceControl(AutomationElement surface, UiControlDefinition definition)
        : HeadlessControlBase<AutomationElement>(surface), INamedItemSurfaceControl
    {
        private static readonly ConditionalWeakTable<TopLevel, LastNamedPress> LastPressByWindow = new();

        public void DoubleClickItem(string itemName, int timeoutMs)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
            var budget = UiOperationTimeoutBudget.Start(timeoutMs, $"double click on named item '{itemName}'");
            HeadlessRuntime.Dispatch(() => DoubleClick(itemName, budget));
        }

        private void DoubleClick(string itemName, UiOperationTimeoutBudget budget)
        {
            var root = NativeControl;
            var owner = TopLevel.GetTopLevel(root)
                ?? throw new InvalidOperationException("The named surface has no owning window.");
            if (Inner.PointerIntent is { } intent && !ReferenceEquals(TopLevel.GetTopLevel(intent.SearchRoot), owner))
                throw new InvalidOperationException("The named surface is not in the resolved window.");
            var locator = new UiControlScope(definition.LocatorValue, definition.LocatorKind, definition.FallbackToName);
            var scope = FindScope();
            if (definition.Scope is not null && scope is null)
                throw new InvalidOperationException("The named surface is not attached to its declared visual scope.");

            var initial = NamedItemSurfacePeers.Read(root, CheckBudget);
            var original = initial.Find(itemName);
            var preparation = new HeadlessPointerPreparation(Inner.PointerIntent, () => budget.Remaining);

            void CheckBudget() => _ = budget.Remaining;

            void RequireSameSurface()
            {
                CheckBudget();
                // Already on the session dispatcher. Check the original visual ancestry without
                // re-entering the queue through PointerIntent.Resolve.
                if (!MatchesLocator(root, locator) || !ReferenceEquals(FindScope(), scope)
                    || !ReferenceEquals(TopLevel.GetTopLevel(root), owner)
                    || !root.IsEffectivelyEnabled || !root.IsEffectivelyVisible)
                    throw new InvalidOperationException("The named surface changed owner or became unavailable during the gesture.");
            }

            Control? FindScope() => definition.Scope is { } expected
                ? root.GetVisualAncestors().OfType<Control>().FirstOrDefault(control => MatchesLocator(control, expected))
                : null;

            RequireSameSurface();
            if (!original.Element.IsEnabled())
                throw new InvalidOperationException($"Named item '{itemName}' is disabled.");

            var bounds = VisibleBounds(original.Element);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                original.Element.BringIntoView();
                owner.Dispatcher.RunJobs();
                owner.UpdateLayout();
                RequireSameSurface();
                bounds = VisibleBounds(NamedItemSurfacePeers.Read(root, CheckBudget).Rebind(original, initial.Revision).Element);
            }

            if (bounds.Width <= 0 || bounds.Height <= 0)
                throw new InvalidOperationException($"Named item '{itemName}' did not become visible.");

            var point = bounds.Center;
            var lastPress = LastPressByWindow.GetValue(owner, static _ => new LastNamedPress());
            WaitForSeparateGesture(owner, point, lastPress, budget);
            owner.MouseMove(point);
            for (var press = 0; press < 2; press++)
            {
                RequireSameSurface();
                var current = NamedItemSurfacePeers.Read(root, CheckBudget);
                var target = current.Rebind(original, initial.Revision);
                var clip = HeadlessPointerPreparation.VisibleBounds(root, owner);
                if (!ContainsPoint(NamedItemSurfacePeers.VisibleBounds(target.Element).Intersect(clip), point)
                    || current.Items.Any(other => other.Key != target.Key
                        && ContainsPoint(NamedItemSurfacePeers.VisibleBounds(other.Element).Intersect(clip), point))
                    || !NamedItemSurfacePeers.OwnsHit(root, target.Element, owner.InputHitTest(point)))
                    throw new InvalidOperationException($"The click area of named item '{itemName}' moved or is occluded.");

                preparation.RequireOriginalHit(root, owner, point);
                CheckBudget();
                try { owner.MouseDown(point, MouseButton.Left); }
                finally
                {
                    lastPress.Position = point;
                    lastPress.Timestamp = Stopwatch.GetTimestamp();
                    owner.MouseUp(point, MouseButton.Left);
                }
            }
            owner.Dispatcher.RunJobs();

            Rect VisibleBounds(AutomationPeer peer) =>
                NamedItemSurfacePeers.VisibleBounds(peer).Intersect(HeadlessPointerPreparation.VisibleBounds(root, owner));
        }

        private static void WaitForSeparateGesture(
            TopLevel owner, Point point, LastNamedPress lastPress, UiOperationTimeoutBudget budget)
        {
            if (lastPress.Timestamp is not { } timestamp || owner.GetPlatformSettings() is not { } settings)
                return;

            var size = settings.GetDoubleTapSize(PointerType.Mouse);
            if (Math.Abs(lastPress.Position.X - point.X) > size.Width / 2
                || Math.Abs(lastPress.Position.Y - point.Y) > size.Height / 2)
                return;

            // Avalonia uses millisecond timestamps and resets the count only past the threshold.
            var remaining = settings.GetDoubleTapTime(PointerType.Mouse) - Stopwatch.GetElapsedTime(timestamp);
            var delay = TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds) + 1);
            if (delay <= TimeSpan.Zero)
                return;
            if (delay >= budget.Remaining)
                throw new TimeoutException("The named item double click timed out waiting for a separate gesture.");
            Thread.Sleep(delay);
        }

        private static bool ContainsPoint(Rect bounds, Point point) =>
            bounds.Width > 0 && bounds.Height > 0 && bounds.Contains(point);

        private sealed class LastNamedPress
        {
            public Point Position { get; set; }
            public long? Timestamp { get; set; }
        }
    }
}
