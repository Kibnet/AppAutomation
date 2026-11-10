using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace AppAutomation.FlaUI.Input;

internal enum PointerTargetRecheck
{
    Passed,
    ListMissing,
    ListChanged,
    ListUnavailable,
    ListOffscreen,
    ListDisabled,
    ListBoundsEmpty,
    ItemUnavailable,
    RawParentChain,
    CaptionMissing,
    CaptionMismatch
}

internal sealed class PointerTargetRecheckException(string message, PointerTargetRecheck check, Exception? inner)
    : InvalidOperationException(message, inner)
{
    internal PointerTargetRecheck Check { get; } = check;
    internal bool AfterPreparation { get; private set; }
    internal bool ActivationRequested { get; private set; }
    internal bool ScrollIntoViewRequested { get; private set; }

    internal void RecordPreparation(bool activationRequested, bool scrollIntoViewRequested)
    {
        AfterPreparation = true;
        ActivationRequested = activationRequested;
        ScrollIntoViewRequested = scrollIntoViewRequested;
    }
}

internal readonly record struct DesktopPointerTarget(Point Point, int ProcessId, IntPtr Window, string Locator, int[] RuntimeId,
    bool RequireEnabled, bool RejectInteractiveDescendantHit = false)
{
    internal void ValidateOwner() => ValidateOwner(Point);
    internal void ValidateOwner(Point point)
    {
        if (!WindowsPointerBackend.ForegroundBelongsTo(ProcessId)
            || WindowsPointerBackend.WindowProcess(WindowsPointerBackend.WindowAt(point)) != ProcessId)
            throw new PointerInterferenceException("The pointer target is covered by another application or no longer owns the foreground window.");
    }

    internal void ValidateExact()
    {
        ValidateOwner();
        using var dpi = WindowsPointerBackend.PhysicalDpiScope.Enter();
        // Async continuations carry only physical coordinates, process/window IDs and runtime IDs.
        // Every late UIA hit test gets a local COM lifetime on the thread that performs it.
        using var automation = new UIA3Automation();
        var runtimeId = RuntimeId;
        var hit = DesktopPointerTargetResolver.FindHitTarget(automation.FromPoint(Point), candidate =>
        {
            var candidateId = candidate.FrameworkAutomationElement.RuntimeId.ValueOrDefault;
            return candidateId != null && runtimeId.AsSpan().SequenceEqual(candidateId);
        }, RejectInteractiveDescendantHit);
        if (hit is null)
            throw new PointerInterferenceException("The exact pointer target was replaced or covered before input was sent.");
        if (hit.IsOffscreen || !hit.BoundingRectangle.Contains(Point) || RequireEnabled && !hit.IsEnabled)
            throw new PointerInterferenceException("The target moved or became hidden before input was sent.");
    }
}

internal static class DesktopPointerTargetResolver
{
    internal static DesktopPointerTarget Resolve(Func<AutomationElement> resolve, bool requireEnabled,
        Action<AutomationElement>? prepare = null, Func<AutomationElement, Point>? pointFactory = null,
        int? expectedProcessId = null, bool allowPreparation = true, bool activateWindow = true,
        PointerPhaseTrace? profile = null, bool rejectInteractiveDescendantHit = false)
    {
        profile?.Mark("resolve-first");
        var element = resolve() ?? throw new InvalidOperationException("The pointer target resolver returned null.");
        profile?.Mark("check-process-first");
        CheckProcess(element, expectedProcessId);
        profile?.Mark("find-window-first");
        var window = FindWindow(element);
        if (allowPreparation)
        {
            // Activating an already visible owned popup can dismiss its Flyout. Keep the existing
            // foreground when this application already owns it; exact hit-testing still rejects
            // a covered target or another window in the same process.
            profile?.Mark("foreground-check");
            var activationRequested = !WindowsPointerBackend.ForegroundBelongsTo(element.Properties.ProcessId.Value);
            if (activationRequested)
            {
                if (!activateWindow)
                    throw new PointerInterferenceException("The popup owner lost the foreground before the click.");
                profile?.Mark("foreground-activate");
                WindowsPointerBackend.Activate(window);
            }
            profile?.Mark("scroll-check");
            var scrollIntoViewRequested = element.IsOffscreen && element.Patterns.ScrollItem.IsSupported;
            if (scrollIntoViewRequested)
            {
                profile?.Mark("scroll-into-view");
                element.Patterns.ScrollItem.Pattern.ScrollIntoView();
            }
            profile?.Mark("prepare");
            prepare?.Invoke(element);
            profile?.Mark("resolve-second", $"activated={activationRequested}, scrolled={scrollIntoViewRequested}");
            try
            {
                element = resolve() ?? throw new InvalidOperationException("The pointer target disappeared after preparation.");
            }
            catch (PointerTargetRecheckException exception)
            {
                exception.RecordPreparation(activationRequested, scrollIntoViewRequested);
                throw;
            }
        }
        profile?.Mark("check-process-final");
        CheckProcess(element, expectedProcessId);
        profile?.Mark("target-point");
        using var dpi = WindowsPointerBackend.PhysicalDpiScope.Enter();
        var bounds = element.BoundingRectangle;
        var point = ChoosePoint(bounds, element.IsOffscreen, element.IsEnabled, requireEnabled,
            element.TryGetClickablePoint(out var clickable) ? clickable : null,
            pointFactory?.Invoke(element));
        profile?.Mark("target-runtime-id");
        var process = element.Properties.ProcessId.Value;
        var runtimeId = element.FrameworkAutomationElement.RuntimeId.ValueOrDefault;
        if (runtimeId == null || runtimeId.Length == 0) throw new InvalidOperationException("The pointer target has no stable native runtime ID.");
        profile?.Mark("find-window-final");
        var finalWindow = FindWindow(element);
        profile?.Mark("target-metadata");
        var target = new DesktopPointerTarget(point, process, finalWindow, element.Properties.AutomationId.ValueOrDefault ?? "",
            runtimeId.ToArray(), requireEnabled, rejectInteractiveDescendantHit);
        profile?.Mark("hit-test");
        target.ValidateOwner();
        if (FindHitTarget(element.Automation.FromPoint(point), element.Equals, rejectInteractiveDescendantHit) is not null)
            return target;
        throw new InvalidOperationException("The physical hit test did not reach the target or one of its descendants.");
    }

    internal static AutomationElement? FindHitTarget(AutomationElement? hit, Predicate<AutomationElement> matches,
        bool rejectInteractiveDescendantHit)
    {
        var interactiveDescendant = false;
        for (var current = hit; current is not null; current = current.Parent)
        {
            if (matches(current))
            {
                if (interactiveDescendant)
                    throw new PointerInterferenceException("The pointer would activate a command inside the target instead of the target itself.");
                return current;
            }

            if (rejectInteractiveDescendantHit && current.ControlType is ControlType.Button or ControlType.SplitButton
                or ControlType.MenuItem or ControlType.Hyperlink or ControlType.CheckBox or ControlType.RadioButton
                or ControlType.ComboBox or ControlType.Edit or ControlType.TabItem or ControlType.ScrollBar)
                interactiveDescendant = true;
        }
        return null;
    }

    internal static Point ChoosePoint(Rectangle bounds, bool offscreen, bool enabled, bool requireEnabled,
        Point? clickable = null, Point? explicitPoint = null)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || offscreen)
            throw new InvalidOperationException("The pointer target does not have visible, positive bounds.");
        if (requireEnabled && !enabled) throw new InvalidOperationException("The pointer target is disabled.");
        var point = explicitPoint ?? (clickable is { } candidate && bounds.Contains(candidate) ? candidate
            : new Point(checked(bounds.Left + bounds.Width / 2), checked(bounds.Top + bounds.Height / 2)));
        if (!bounds.Contains(point)) throw new InvalidOperationException("The pointer target point lies outside its fresh element bounds.");
        return point;
    }

    private static void CheckProcess(AutomationElement element, int? expected)
    {
        if (expected.HasValue && element.Properties.ProcessId.Value != expected.Value)
            throw new InvalidOperationException("The pointer target changed application after session registration.");
    }

    internal static string DescribeForeground(AutomationElement target, int expectedProcessId)
    {
        var targetWindow = IntPtr.Zero;
        try { targetWindow = FindWindow(target); }
        catch (Exception error) when (!DesktopPointer.IsTerminalFailure(error)) { }
        return WindowsPointerBackend.DescribeForeground(targetWindow, expectedProcessId);
    }

    private static IntPtr FindWindow(AutomationElement element)
    {
        for (var current = element; current != null; current = current.Parent)
        {
            if (current.Properties.NativeWindowHandle.TryGetValue(out var handle) && handle != IntPtr.Zero) return handle;
        }
        throw new InvalidOperationException("The pointer target has no owning native window.");
    }
}
