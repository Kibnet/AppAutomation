using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using AppAutomation.Session.Contracts;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;

namespace AppAutomation.FlaUI.Session;

internal static class DesktopWindowPlacementService
{
    private const int MonitorInfoPrimary = 0x00000001;
    private const int ShowWindowRestore = 9;
    private const uint SetWindowPosNoZOrder = 0x0004;
    private const uint SetWindowPosNoActivate = 0x0010;
    private const uint SetWindowPosNoOwnerZOrder = 0x0200;
    private const int BoundsTolerance = 3;
    private const int MonitorDeviceNameLength = 32;

    public static void Apply(
        Application application,
        Window mainWindow,
        DesktopAppLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(options);

        if (options.WindowPlacement is null)
        {
            return;
        }

        try
        {
            var monitors = EnumerateMonitors();
            var windowHandle = ResolveWindowHandle(application, mainWindow, options.WindowPlacement, monitors);
            var currentWindowBounds = GetNativeWindowRectangle(windowHandle);
            var placement = ResolvePlacement(options.WindowPlacement, currentWindowBounds, monitors);

            RestoreWindow(mainWindow, windowHandle);
            ApplyWindowRectangle(windowHandle, placement.TargetBounds, options.WindowPlacement, monitors);
            WaitForWindowRectangle(windowHandle, placement.TargetBounds, options, monitors);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or PlatformNotSupportedException)
        {
            throw new InvalidOperationException(
                $"Desktop window placement failed. ExecutablePath={options.ExecutablePath}, ProcessId={application.ProcessId}.{Environment.NewLine}{ex.Message}",
                ex);
        }
    }

    internal static DesktopWindowPlacementResult ResolvePlacement(
        DesktopWindowPlacement placement,
        Rectangle currentWindowBounds,
        IReadOnlyList<DesktopMonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(monitors);

        var sortedMonitors = SortMonitors(monitors);
        if (sortedMonitors.Count == 0)
        {
            throw CreatePlacementException("No desktop monitors were reported by Windows.", placement, sortedMonitors);
        }

        ValidatePlacement(placement, sortedMonitors);

        var monitor = ResolveMonitor(placement, sortedMonitors, out var usedFallback);
        var placementArea = placement.UseWorkingArea ? monitor.WorkingArea : monitor.Bounds;
        if (placementArea.Width <= 0 || placementArea.Height <= 0)
        {
            throw CreatePlacementException(
                $"Selected monitor area is invalid: {FormatRectangle(placementArea)}.",
                placement,
                sortedMonitors);
        }

        var targetSize = ResolveTargetSize(placement, currentWindowBounds, placementArea, sortedMonitors);
        var targetBounds = CalculateTargetBounds(placement, placementArea, targetSize);

        if (!Contains(placementArea, targetBounds))
        {
            throw CreatePlacementException(
                $"Requested placement is outside the selected monitor area. Target={FormatRectangle(targetBounds)}, Area={FormatRectangle(placementArea)}.",
                placement,
                sortedMonitors);
        }

        return new DesktopWindowPlacementResult(targetBounds, monitor, placementArea, usedFallback);
    }

    internal static IReadOnlyList<DesktopMonitorInfo> SortMonitors(IEnumerable<DesktopMonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        return monitors
            .OrderByDescending(static monitor => monitor.IsPrimary)
            .ThenBy(static monitor => monitor.Bounds.Top)
            .ThenBy(static monitor => monitor.Bounds.Left)
            .ThenBy(static monitor => monitor.Bounds.Bottom)
            .ThenBy(static monitor => monitor.Bounds.Right)
            .ToArray();
    }

    internal static Rectangle GetNativeWindowRectangle(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);

        if (mainWindow.Properties.NativeWindowHandle.TryGetValue(out var nativeHandle)
            && nativeHandle != IntPtr.Zero)
        {
            return GetNativeWindowRectangle(nativeHandle);
        }

        throw new InvalidOperationException("Could not resolve the native window handle.");
    }

    internal static IReadOnlyList<DesktopMonitorInfo> EnumerateMonitors()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Desktop window placement is supported only on Windows.");
        }

        var monitors = new List<DesktopMonitorInfo>();
        var succeeded = EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitorHandle, _, _, _) =>
            {
                var monitorInfo = new MonitorInfoEx
                {
                    Size = Marshal.SizeOf<MonitorInfoEx>()
                };

                if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to query monitor information.");
                }

                monitors.Add(new DesktopMonitorInfo(
                    string.IsNullOrWhiteSpace(monitorInfo.DeviceName) ? "<unknown>" : monitorInfo.DeviceName,
                    monitorInfo.Monitor.ToRectangle(),
                    monitorInfo.WorkArea.ToRectangle(),
                    (monitorInfo.Flags & MonitorInfoPrimary) != 0));
                return true;
            },
            IntPtr.Zero);

        if (!succeeded)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to enumerate desktop monitors.");
        }

        return SortMonitors(monitors);
    }

    private static void ValidatePlacement(
        DesktopWindowPlacement placement,
        IReadOnlyList<DesktopMonitorInfo> monitors)
    {
        if (placement.Monitor is null)
        {
            throw CreatePlacementException("Monitor selector is required.", placement, monitors);
        }

        if (placement.Offset is null)
        {
            throw CreatePlacementException("Window offset is required.", placement, monitors);
        }

        if (!Enum.IsDefined(placement.Anchor))
        {
            throw CreatePlacementException($"Unknown window anchor value '{(int)placement.Anchor}'.", placement, monitors);
        }

        if (!Enum.IsDefined(placement.UnavailableBehavior))
        {
            throw CreatePlacementException(
                $"Unknown monitor unavailable behavior value '{(int)placement.UnavailableBehavior}'.",
                placement,
                monitors);
        }
    }

    private static DesktopMonitorInfo ResolveMonitor(
        DesktopWindowPlacement placement,
        IReadOnlyList<DesktopMonitorInfo> monitors,
        out bool usedFallback)
    {
        usedFallback = false;

        var selector = placement.Monitor;
        var monitor = TryResolveMonitor(selector, monitors);
        if (monitor is not null)
        {
            return monitor.Value;
        }

        if (placement.UnavailableBehavior != DesktopWindowPlacementUnavailableBehavior.UsePrimaryMonitor)
        {
            throw CreatePlacementException($"Requested monitor '{selector}' was not found.", placement, monitors);
        }

        var primary = monitors.FirstOrDefault(static candidate => candidate.IsPrimary);
        if (primary.DeviceName is null)
        {
            throw CreatePlacementException(
                $"Requested monitor '{selector}' was not found and no primary monitor is available for fallback.",
                placement,
                monitors);
        }

        usedFallback = true;
        return primary;
    }

    private static DesktopMonitorInfo? TryResolveMonitor(
        DesktopMonitorSelector selector,
        IReadOnlyList<DesktopMonitorInfo> monitors)
    {
        if (selector.IsPrimary)
        {
            foreach (var monitor in monitors)
            {
                if (monitor.IsPrimary)
                {
                    return monitor;
                }
            }

            return null;
        }

        if (selector.IsLastAvailable)
        {
            return monitors[^1];
        }

        if (selector.Index is { } index)
        {
            return index < monitors.Count ? monitors[index] : null;
        }

        if (selector.DeviceName is { Length: > 0 } deviceName)
        {
            foreach (var monitor in monitors)
            {
                if (string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return monitor;
                }
            }
        }

        return null;
    }

    private static Size ResolveTargetSize(
        DesktopWindowPlacement placement,
        Rectangle currentWindowBounds,
        Rectangle placementArea,
        IReadOnlyList<DesktopMonitorInfo> monitors)
    {
        var targetSize = placement.Size is null
            ? new Size(currentWindowBounds.Width, currentWindowBounds.Height)
            : new Size(placement.Size.Width, placement.Size.Height);

        if (targetSize.Width <= 0 || targetSize.Height <= 0)
        {
            throw CreatePlacementException(
                $"Current window size is invalid and no explicit size was provided. Current={FormatRectangle(currentWindowBounds)}.",
                placement,
                monitors);
        }

        if (targetSize.Width > placementArea.Width || targetSize.Height > placementArea.Height)
        {
            throw CreatePlacementException(
                $"Requested window size {targetSize.Width}x{targetSize.Height} exceeds selected monitor area {placementArea.Width}x{placementArea.Height}.",
                placement,
                monitors);
        }

        return targetSize;
    }

    private static Rectangle CalculateTargetBounds(
        DesktopWindowPlacement placement,
        Rectangle placementArea,
        Size targetSize)
    {
        var offset = placement.Offset;
        var targetX = placement.Anchor switch
        {
            DesktopWindowAnchor.Center => placementArea.Left + (placementArea.Width - targetSize.Width) / 2 + offset.X,
            DesktopWindowAnchor.TopLeft or DesktopWindowAnchor.BottomLeft => placementArea.Left + offset.X,
            DesktopWindowAnchor.TopRight or DesktopWindowAnchor.BottomRight => placementArea.Right - targetSize.Width - offset.X,
            _ => throw new ArgumentOutOfRangeException(nameof(placement), "Unknown window anchor.")
        };
        var targetY = placement.Anchor switch
        {
            DesktopWindowAnchor.Center => placementArea.Top + (placementArea.Height - targetSize.Height) / 2 + offset.Y,
            DesktopWindowAnchor.TopLeft or DesktopWindowAnchor.TopRight => placementArea.Top + offset.Y,
            DesktopWindowAnchor.BottomLeft or DesktopWindowAnchor.BottomRight => placementArea.Bottom - targetSize.Height - offset.Y,
            _ => throw new ArgumentOutOfRangeException(nameof(placement), "Unknown window anchor.")
        };

        return new Rectangle(targetX, targetY, targetSize.Width, targetSize.Height);
    }

    private static IntPtr ResolveWindowHandle(
        Application application,
        Window mainWindow,
        DesktopWindowPlacement placement,
        IReadOnlyList<DesktopMonitorInfo> monitors)
    {
        var handle = application.MainWindowHandle;
        if (handle != IntPtr.Zero)
        {
            return handle;
        }

        try
        {
            if (mainWindow.Properties.NativeWindowHandle.TryGetValue(out var nativeHandle)
                && nativeHandle != IntPtr.Zero)
            {
                return nativeHandle;
            }
        }
        catch
        {
            // The error below includes enough launch context; native handle lookup is a best-effort fallback.
        }

        throw CreatePlacementException(
            $"Could not resolve a native window handle. ProcessId={application.ProcessId}, Executable={application.Name}.",
            placement,
            monitors);
    }

    private static void RestoreWindow(Window mainWindow, IntPtr windowHandle)
    {
        try
        {
            if (mainWindow.Patterns.Window.TryGetPattern(out var windowPattern)
                && windowPattern is not null)
            {
                windowPattern.SetWindowVisualState(WindowVisualState.Normal);
                return;
            }
        }
        catch
        {
            // Fall back to Win32 restore below.
        }

        _ = ShowWindow(windowHandle, ShowWindowRestore);
    }

    private static void ApplyWindowRectangle(
        IntPtr windowHandle,
        Rectangle targetBounds,
        DesktopWindowPlacement placement,
        IReadOnlyList<DesktopMonitorInfo> monitors)
    {
        var succeeded = SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            targetBounds.X,
            targetBounds.Y,
            targetBounds.Width,
            targetBounds.Height,
            SetWindowPosNoZOrder | SetWindowPosNoOwnerZOrder | SetWindowPosNoActivate);

        if (!succeeded)
        {
            throw CreatePlacementException(
                $"SetWindowPos failed. NativeError={Marshal.GetLastWin32Error()}, Target={FormatRectangle(targetBounds)}.",
                placement,
                monitors);
        }
    }

    private static void WaitForWindowRectangle(
        IntPtr windowHandle,
        Rectangle targetBounds,
        DesktopAppLaunchOptions options,
        IReadOnlyList<DesktopMonitorInfo> monitors)
    {
        var timeout = options.MainWindowTimeout;
        var pollInterval = options.PollInterval;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            Rectangle observedBounds;
            try
            {
                observedBounds = GetNativeWindowRectangle(windowHandle);
            }
            catch
            {
                Thread.Sleep(pollInterval);
                continue;
            }

            if (IsWithinTolerance(observedBounds, targetBounds))
            {
                return;
            }

            Thread.Sleep(pollInterval);
        }

        throw CreatePlacementException(
            $"Window bounds did not reach requested placement within {timeout.TotalSeconds:0.###} seconds. Target={FormatRectangle(targetBounds)}, Actual={FormatRectangle(GetNativeWindowRectangle(windowHandle))}.",
            options.WindowPlacement!,
            monitors);
    }

    private static Rectangle GetNativeWindowRectangle(IntPtr windowHandle)
    {
        if (!GetWindowRect(windowHandle, out var nativeRect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to query native window rectangle.");
        }

        return nativeRect.ToRectangle();
    }

    private static bool Contains(Rectangle outer, Rectangle inner)
    {
        return inner.Left >= outer.Left
            && inner.Top >= outer.Top
            && inner.Right <= outer.Right
            && inner.Bottom <= outer.Bottom;
    }

    private static bool IsWithinTolerance(Rectangle actual, Rectangle expected)
    {
        return Math.Abs(actual.Left - expected.Left) <= BoundsTolerance
            && Math.Abs(actual.Top - expected.Top) <= BoundsTolerance
            && Math.Abs(actual.Width - expected.Width) <= BoundsTolerance
            && Math.Abs(actual.Height - expected.Height) <= BoundsTolerance;
    }

    private static InvalidOperationException CreatePlacementException(
        string message,
        DesktopWindowPlacement placement,
        IReadOnlyList<DesktopMonitorInfo> monitors)
    {
        return new InvalidOperationException(
            $"{message}{Environment.NewLine}" +
            $"Requested placement: {FormatPlacement(placement)}{Environment.NewLine}" +
            $"Available monitors:{Environment.NewLine}{FormatMonitors(monitors)}");
    }

    private static string FormatPlacement(DesktopWindowPlacement placement)
    {
        var size = placement.Size is null ? "<current>" : $"{placement.Size.Width}x{placement.Size.Height}";
        var offset = placement.Offset is null ? "<null>" : $"{placement.Offset.X},{placement.Offset.Y}";
        return $"Monitor={placement.Monitor}, Size={size}, Anchor={placement.Anchor}, Offset={offset}, UseWorkingArea={placement.UseWorkingArea}, UnavailableBehavior={placement.UnavailableBehavior}";
    }

    private static string FormatMonitors(IReadOnlyList<DesktopMonitorInfo> monitors)
    {
        if (monitors.Count == 0)
        {
            return "<none>";
        }

        return string.Join(
            Environment.NewLine,
            monitors.Select((monitor, index) =>
                $"  [{index}] Primary={monitor.IsPrimary}, DeviceName={monitor.DeviceName}, Bounds={FormatRectangle(monitor.Bounds)}, WorkingArea={FormatRectangle(monitor.WorkingArea)}"));
    }

    private static string FormatRectangle(Rectangle rectangle)
    {
        return $"X={rectangle.X}, Y={rectangle.Y}, Width={rectangle.Width}, Height={rectangle.Height}";
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private delegate bool MonitorEnumProc(
        IntPtr hMonitor,
        IntPtr hdcMonitor,
        IntPtr lprcMonitor,
        IntPtr dwData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public int Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MonitorDeviceNameLength)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        private readonly int _left;
        private readonly int _top;
        private readonly int _right;
        private readonly int _bottom;

        public Rectangle ToRectangle()
        {
            return Rectangle.FromLTRB(_left, _top, _right, _bottom);
        }
    }
}

internal readonly record struct DesktopMonitorInfo(
    string DeviceName,
    Rectangle Bounds,
    Rectangle WorkingArea,
    bool IsPrimary);

internal readonly record struct DesktopWindowPlacementResult(
    Rectangle TargetBounds,
    DesktopMonitorInfo Monitor,
    Rectangle PlacementArea,
    bool UsedFallback);
