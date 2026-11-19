using System.Drawing;
using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Input;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using ElementNotAvailableException = FlaUI.Core.Exceptions.ElementNotAvailableException;

namespace AppAutomation.FlaUI.Automation;

public sealed partial class FlaUiControlResolver
{
    internal bool IsRegisteredPointerBlocker(UiInputPreparationContext context, AutomationElement target, Point point) =>
        FindPointerBlocker(context, target, point) is not null;

    internal bool DismissPointerBlocker(UiInputPreparationContext context, AutomationElement target,
        PointerOcclusionException obstruction, DesktopPointerController.PointerOperation operation)
    {
        operation.Check();
        var currentId = target.FrameworkAutomationElement.RuntimeId.ValueOrDefault;
        if (currentId is null || !currentId.AsSpan().SequenceEqual(obstruction.RuntimeId)
            || target.IsOffscreen || !target.IsEnabled || !target.BoundingRectangle.Contains(obstruction.Point))
            throw new PointerInterferenceException("The original target changed before notification preparation.");
        var blocker = FindPointerBlocker(context, target, obstruction.Point);
        if (blocker is null) return false;

        var (registration, root) = blocker.Value;
        if (string.IsNullOrWhiteSpace(registration.Parts.DismissButtonLocator))
            throw new NotSupportedException("The blocking registered notification has no configured dismiss button.");
        var runtimeId = root.FrameworkAutomationElement.RuntimeId.ValueOrDefault;
        if (runtimeId is not { Length: > 0 })
            throw new InvalidOperationException("The blocking notification does not expose native instance identity.");
        var identity = $"{root.Properties.ProcessId.Value}:{string.Join(',', runtimeId)}";
        var originalProcess = target.Properties.ProcessId.Value;
        var originalWindow = WindowsPointerBackend.RootWindow(DesktopPointerTargetResolver.FindWindow(target));
        using var changed = new AutoResetEvent(false);
        void Signal() { try { changed.Set(); } catch (ObjectDisposedException) { } }
        var eventRoot = FindNotificationEventWindow(root, originalWindow);
        using var structure = eventRoot.RegisterStructureChangedEvent(TreeScope.Subtree, (_, _, _) => Signal());
        using var property = eventRoot.RegisterPropertyChangedEvent(TreeScope.Subtree, (_, _, _) => Signal(),
            eventRoot.Automation.PropertyLibrary.Element.IsOffscreen, eventRoot.Automation.PropertyLibrary.Element.BoundingRectangle);

        operation.Check();
        if (!StillBlocking()) return true;
        AutomationElement? close = null;
        var closeTarget = DesktopPointerTargetResolver.Resolve(() => close = FindNotificationPart(root,
                registration.RootDefinition, registration.Parts, registration.Parts.DismissButtonLocator!),
            requireEnabled: true, expectedProcessId: target.Properties.ProcessId.Value, allowPreparation: false,
            check: operation.Check);
        var dismissButton = close ?? throw new InvalidOperationException("The notification dismiss part disappeared during preparation.");
        if (WindowsPointerBackend.RootWindow(closeTarget.Window) != originalWindow)
            throw new PointerInterferenceException("The notification dismiss part belongs to another native window.");
        operation.Check();
        ValidateClose();
        if (operation.BeginDismiss(identity))
        {
            operation.Log("notification-dismiss", detail: "scoped registered blocker");
            if (dismissButton.Patterns.Invoke.IsSupported)
            {
                var invoke = dismissButton.Patterns.Invoke.Pattern;
                ValidateClose();
                operation.Check();
                invoke.Invoke();
            }
            else
            {
                operation.ClickAsync(closeTarget.Point, new PointerClickOptions(), ValidateClose,
                    auxiliary: true).GetAwaiter().GetResult();
            }
        }

        while (StillBlocking())
        {
            operation.Check();
            var remaining = operation.Remaining;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException("The blocking notification did not leave the original pointer target before its deadline.");
            var signaled = WaitHandle.WaitAny([changed, operation.Token.WaitHandle], remaining);
            operation.Check();
            if (signaled == WaitHandle.WaitTimeout)
                throw new TimeoutException("The blocking notification did not leave the original pointer target before its deadline.");
        }
        operation.Check();
        return true;

        void ValidateClose()
        {
            operation.Check();
            if (WindowsPointerBackend.RootWindow(WindowsPointerBackend.WindowAt(closeTarget.Point)) != originalWindow)
                throw new PointerInterferenceException("The notification dismiss part is covered by another native window.");
            closeTarget.ValidateExact();
        }

        bool StillBlocking()
        {
            operation.Check();
            ValidatePointerOwner(originalProcess, obstruction.Point);
            try
            {
                if (!IsAttachedAndAvailable(root) || root.IsOffscreen) return false;
                return IsWithin(target.Automation.FromPoint(obstruction.Point), root);
            }
            catch (ElementNotAvailableException) { return false; }
        }
    }

    private (UiRegisteredNotification Registration, AutomationElement Root)? FindPointerBlocker(
        UiInputPreparationContext context, AutomationElement target, Point point)
    {
        ValidatePointerOwner(target, point);
        var hit = target.Automation.FromPoint(point);
        if (hit is null) return null;
        var hitPath = new List<AutomationElement>();
        for (var current = hit; current is not null && !current.Equals(target); current = current.Parent)
            hitPath.Add(current);
        if (hitPath.Count == 0) return null;
        var targetWindow = WindowsPointerBackend.RootWindow(DesktopPointerTargetResolver.FindWindow(target));
        if (WindowsPointerBackend.RootWindow(WindowsPointerBackend.WindowAt(point)) != targetWindow) return null;
        var matches = new List<(UiRegisteredNotification Registration, AutomationElement Root)>();
        foreach (var registration in context.Notifications)
        {
            var definition = registration.RootDefinition;
            var primary = new UiControlScope(definition.LocatorValue, definition.LocatorKind, FallbackToName: false);
            var roots = hitPath.Where(candidate => MatchesLocator(candidate, primary)).ToArray();
            if (roots.Length == 0 && definition.FallbackToName && definition.LocatorKind != UiLocatorKind.Name)
            {
                var fallback = new UiControlScope(definition.LocatorValue, UiLocatorKind.Name, FallbackToName: false);
                roots = hitPath.Where(candidate => MatchesLocator(candidate, fallback)).ToArray();
                if (roots.Length > 0)
                {
                    var searchRoots = definition.Scope is null ? GetProcessSearchRoots() : FindScopeRoots(definition.Scope);
                    if (FindVisibleNotificationMatches(searchRoots, definition.LocatorValue,
                            definition.LocatorKind, fallbackToName: false, includeRoot: true).Length > 0)
                        continue;
                }
            }
            foreach (var root in roots)
            {
                if (root.Properties.ProcessId.Value != target.Properties.ProcessId.Value
                    || WindowsPointerBackend.RootWindow(DesktopPointerTargetResolver.FindWindow(root)) != targetWindow
                    || !IsAttachedAndAvailable(root) || root.IsOffscreen
                    || definition.Scope is { } scope && !HasNotificationScope(root, scope)
                    || IsWithin(target, root)) continue;
                if (!matches.Any(candidate => candidate.Root.Equals(root) && candidate.Registration == registration))
                    matches.Add((registration, root));
            }
        }
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException("The actual pointer blocker matches more than one registered notification root or dismiss configuration.")
        };
    }

    private AutomationElement FindNotificationEventWindow(AutomationElement root, IntPtr window)
    {
        if (_window.Properties.NativeWindowHandle.ValueOrDefault == window) return _window;
        for (var current = root.Parent; current is not null; current = current.Parent)
            if (current.Properties.NativeWindowHandle.ValueOrDefault == window) return current;
        throw new NotSupportedException("The blocking notification has no stable owning-window event source.");
    }

    private static bool HasNotificationScope(AutomationElement root, UiControlScope scope)
    {
        for (var current = root; current is not null; current = current.Parent)
            if (MatchesLocator(current, scope)) return true;
        return false;
    }

    private static void ValidatePointerOwner(AutomationElement target, Point point)
        => ValidatePointerOwner(target.Properties.ProcessId.Value, point);

    private static void ValidatePointerOwner(int process, Point point)
    {
        if (!WindowsPointerBackend.ForegroundBelongsTo(process)
            || WindowsPointerBackend.WindowProcess(WindowsPointerBackend.WindowAt(point)) != process)
            throw new PointerInterferenceException("Notification preparation cannot recover foreign foreground or window interference.");
    }

    private static bool IsWithin(AutomationElement? element, AutomationElement root)
    {
        for (var current = element; current is not null; current = current.Parent)
            if (current.Equals(root)) return true;
        return false;
    }
}
