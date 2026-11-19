using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Internal.AutomationModel;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AppAutomation.Avalonia.Headless.Automation;

// The original definition, not a guessed locator or a retained/recycled native container.
internal sealed record HeadlessPointerIntent(Control SearchRoot, Func<AutomationElement> Resolve,
    UiInputPreparationContext? Context)
{
    internal HeadlessPointerIntent Select(Func<AutomationElement, AutomationElement> select) =>
        this with { Resolve = () => select(Resolve()) };
}

internal sealed class HeadlessPointerOcclusion(Control target, TopLevel owner, Point point, Visual? hit)
    : InvalidOperationException($"The pointer target '{target.GetType().Name}' is occluded at {point}: hit={hit?.GetType().Name ?? "<none>"}.")
{
    internal Control Target { get; } = target;
    internal TopLevel Owner { get; } = owner;
    internal Point Point { get; } = point;
}

internal sealed class HeadlessPointerPreparation(HeadlessPointerIntent? intent, Func<TimeSpan> remaining)
{
    private readonly UiInputPreparationContext? _context = UiInputPreparationContext.ForOperation(intent?.Context);
    private readonly HashSet<Control> _dismissed = new(ReferenceEqualityComparer.Instance);

    // Must be called outside the UI dispatch, and only before the first original Down attempt.
    internal void Recover(HeadlessPointerOcclusion occlusion, Func<Control>? resolveOriginalTarget = null,
        Func<Control, TopLevel, Point>? resolvePoint = null)
    {
        if (intent is null || _context is null || _context.Notifications.Count == 0) throw occlusion;
        _ = remaining();
        // Prove that the original locator is still resolvable before causing an auxiliary side effect.
        var original = intent.Resolve();
        var originalTarget = resolveOriginalTarget?.Invoke() ?? original.Control;
        HeadlessRuntime.Session.Dispatch<bool>(async () =>
        {
            _ = remaining();
            if (!ReferenceEquals(TopLevel.GetTopLevel(original.Control), occlusion.Owner)
                || !ReferenceEquals(TopLevel.GetTopLevel(intent.SearchRoot), occlusion.Owner)) throw occlusion;
            // A layout/move handler can replace or recycle an attached target. Re-prepare its new point
            // before deciding whether the toast at the old point is still an obstacle to this intent.
            var currentPoint = resolvePoint is not null ? resolvePoint(originalTarget, occlusion.Owner)
                : originalTarget.TranslatePoint(new Point(originalTarget.Bounds.Width / 2, originalTarget.Bounds.Height / 2), occlusion.Owner);
            if (!ReferenceEquals(originalTarget, occlusion.Target) || currentPoint != occlusion.Point) return true;
            var hit = occlusion.Owner.InputHitTest(occlusion.Point) as Visual;
            var candidates = new List<(Control Root, UiRegisteredNotification Registration)>();
            foreach (var registration in _context.Notifications)
            {
                foreach (var root in HeadlessControlResolver.FindNotificationRoots(intent.SearchRoot, registration.RootDefinition))
                {
                    if (Contains(root, hit) && !Contains(root, occlusion.Target)
                        && ReferenceEquals(TopLevel.GetTopLevel(root), occlusion.Owner))
                        candidates.Add((root, registration));
                }
            }
            candidates = candidates.Distinct().ToList();
            if (candidates.Count == 0 && Contains(occlusion.Target, hit)) return true;
            if (candidates.Count != 1) throw occlusion;
            var (blocker, configuration) = candidates[0];
            if (configuration.Parts.DismissButtonLocator is not { Length: > 0 } locator
                || _dismissed.Contains(blocker)) throw occlusion;
            Control dismiss;
            try
            {
                dismiss = HeadlessControlResolver.FindNotificationPart(blocker, configuration.RootDefinition,
                    configuration.Parts, locator);
            }
            catch (UiControlResolutionException error)
            {
                throw new InvalidOperationException("The blocking notification does not expose a unique dismiss part.", error);
            }
            if (!dismiss.IsEffectivelyEnabled || !dismiss.IsEffectivelyVisible) throw occlusion;
            var bounds = VisibleBounds(dismiss, occlusion.Owner);
            if (bounds.Width <= 0 || bounds.Height <= 0) throw occlusion;
            RequireHit(dismiss, occlusion.Owner, bounds.Center);

            var gone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void CheckGone()
            {
                if (!blocker.IsEffectivelyVisible || !ReferenceEquals(TopLevel.GetTopLevel(blocker), occlusion.Owner))
                    gone.TrySetResult();
                else if (!Contains(blocker, occlusion.Owner.InputHitTest(occlusion.Point) as Visual))
                    gone.TrySetResult();
            }
            void Detached(object? sender, VisualTreeAttachmentEventArgs args) => CheckGone();
            void Changed(object? sender, AvaloniaPropertyChangedEventArgs args) => CheckGone();
            void Layout(object? sender, EventArgs args) => CheckGone();
            blocker.DetachedFromVisualTree += Detached;
            blocker.PropertyChanged += Changed;
            occlusion.Owner.LayoutUpdated += Layout;
            try
            {
                CheckGone();
                if (!gone.Task.IsCompleted)
                {
                    _ = remaining();
                    _dismissed.Add(blocker);
                    // Use the configured part's ordinary invoke handler; never remove a visual/history entry here.
                    AutomationElement.InvokeOnUi(dismiss);
                    CheckGone();
                }
                // Async dispatch pumps the UI queue while the standard handler finishes closing/animating.
                await gone.Task.WaitAsync(remaining());
                _ = remaining();
            }
            finally
            {
                blocker.DetachedFromVisualTree -= Detached;
                blocker.PropertyChanged -= Changed;
                occlusion.Owner.LayoutUpdated -= Layout;
            }
            return true;
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    internal static void RequireHit(Control target, TopLevel owner, Point point)
    {
        if (!target.IsEffectivelyVisible || !target.IsEffectivelyEnabled
            || !ReferenceEquals(TopLevel.GetTopLevel(target), owner))
            throw new InvalidOperationException("The pointer target is disabled, hidden or detached from its owning window.");
        var hit = owner.InputHitTest(point) as Visual;
        if (!Contains(target, hit))
            throw new HeadlessPointerOcclusion(target, owner, point, hit);
    }

    internal void RequireOriginalHit(Control target, TopLevel owner, Point point)
    {
        RequireHit(target, owner, point);
        if (intent is null || _context is not { Notifications.Count: > 0 }) return;
        var hit = owner.InputHitTest(point) as Visual;
        if (hit is null) return;
        // Ordinary descendants remain cheap. Only a declared root on the hit's ancestor path
        // warrants the full scoped proof, including the case where the target contains the toast.
        var path = hit.GetVisualAncestors().Prepend(hit).TakeWhile(visual => !ReferenceEquals(visual, target))
            .OfType<Control>().ToArray();
        foreach (var registration in _context.Notifications)
        {
            var definition = registration.RootDefinition;
            var possible = HeadlessControlResolver.FindVisibleNotificationMatches(path,
                definition.LocatorValue, definition.LocatorKind, definition.FallbackToName);
            if (possible.Length == 0) continue;
            var registered = HeadlessControlResolver.FindNotificationRoots(intent.SearchRoot, definition);
            if (possible.Any(root => registered.Contains(root)))
                throw new HeadlessPointerOcclusion(target, owner, point, hit);
        }
    }

    private static bool Contains(Visual root, Visual? candidate) => candidate is not null
        && (ReferenceEquals(candidate, root) || candidate.GetVisualAncestors().Contains(root));

    internal static Rect VisibleBounds(Control target, TopLevel owner)
    {
        var visible = new Rect(owner.ClientSize);
        foreach (var visual in target.GetVisualAncestors().Prepend(target))
        {
            if (visual.TransformToVisual(owner) is not { } transform)
                throw new InvalidOperationException("The pointer target is detached from its owning window.");
            if (ReferenceEquals(visual, target) || visual.ClipToBounds)
                visible = visible.Intersect(new Rect(visual.Bounds.Size).TransformToAABB(transform));
            if (visual.Clip is { } clip) visible = visible.Intersect(clip.Bounds.TransformToAABB(transform));
            if (ReferenceEquals(visual, owner)) break;
        }
        return visible;
    }
}
