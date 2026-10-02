using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace AppAutomation.FlaUI.Input;

internal readonly record struct DesktopPointerTarget(Point Point, int ProcessId, IntPtr Window, string Locator, int[] RuntimeId, bool RequireEnabled)
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
        for (var current = automation.FromPoint(Point); current != null; current = current.Parent)
        {
            var currentId = current.FrameworkAutomationElement.RuntimeId.ValueOrDefault;
            if (currentId != null && RuntimeId.AsSpan().SequenceEqual(currentId))
            {
                if (current.IsOffscreen || !current.BoundingRectangle.Contains(Point) || RequireEnabled && !current.IsEnabled)
                    throw new PointerInterferenceException("The target moved or became hidden before input was sent.");
                return;
            }
        }
        throw new PointerInterferenceException("The exact pointer target was replaced or covered before input was sent.");
    }
}

internal static class DesktopPointerTargetResolver
{
    internal static DesktopPointerTarget Resolve(Func<AutomationElement> resolve, bool requireEnabled,
        Action<AutomationElement>? prepare = null, Func<AutomationElement, Point>? pointFactory = null,
        int? expectedProcessId = null, bool allowPreparation = true)
    {
        var element = resolve() ?? throw new InvalidOperationException("The pointer target resolver returned null.");
        CheckProcess(element, expectedProcessId);
        var window = FindWindow(element);
        if (allowPreparation)
        {
            // Activating an already visible owned popup can dismiss its Flyout. Keep the existing
            // foreground when this application already owns it; exact hit-testing still rejects
            // a covered target or another window in the same process.
            if (!WindowsPointerBackend.ForegroundBelongsTo(element.Properties.ProcessId.Value))
                WindowsPointerBackend.Activate(window);
            if (element.IsOffscreen && element.Patterns.ScrollItem.IsSupported)
                element.Patterns.ScrollItem.Pattern.ScrollIntoView();
            prepare?.Invoke(element);
            element = resolve() ?? throw new InvalidOperationException("The pointer target disappeared after preparation.");
        }
        CheckProcess(element, expectedProcessId);
        using var dpi = WindowsPointerBackend.PhysicalDpiScope.Enter();
        var bounds = element.BoundingRectangle;
        var point = ChoosePoint(bounds, element.IsOffscreen, element.IsEnabled, requireEnabled,
            element.TryGetClickablePoint(out var clickable) ? clickable : null,
            pointFactory?.Invoke(element));
        var process = element.Properties.ProcessId.Value;
        var runtimeId = element.FrameworkAutomationElement.RuntimeId.ValueOrDefault;
        if (runtimeId == null || runtimeId.Length == 0) throw new InvalidOperationException("The pointer target has no stable native runtime ID.");
        var target = new DesktopPointerTarget(point, process, FindWindow(element), element.Properties.AutomationId.ValueOrDefault ?? "", runtimeId.ToArray(), requireEnabled);
        target.ValidateOwner();
        var hit = element.Automation.FromPoint(point);
        for (var current = hit; current != null; current = current.Parent)
        {
            if (current.Equals(element)) return target;
        }
        throw new InvalidOperationException("The physical hit test did not reach the target or one of its descendants.");
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

    private static IntPtr FindWindow(AutomationElement element)
    {
        for (var current = element; current != null; current = current.Parent)
        {
            if (current.Properties.NativeWindowHandle.TryGetValue(out var handle) && handle != IntPtr.Zero) return handle;
        }
        throw new InvalidOperationException("The pointer target has no owning native window.");
    }
}
