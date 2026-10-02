using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using AppAutomation.Abstractions;

namespace AppAutomation.FlaUI.Input;

[Flags]
internal enum PointerButtons { None = 0, Left = 1, Right = 2, Middle = 4, X1 = 8, X2 = 16 }

// The guard includes Escape as well as modifiers: Escape cancels an OLE drag even when all mouse
// coordinates and buttons match. These are observed user keys; the pointer runtime never releases them.
internal readonly record struct PointerState(Point Position, PointerButtons Buttons, int Modifiers);

internal interface IPointerBackend
{
    string DesktopKey { get; }
    TimeSpan DoubleClickTime { get; }
    Size DragThreshold { get; }
    void EnsureDesktopAvailable();
    PointerState ReadState();
    bool IsOnMonitor(Point point);
    void SetPosition(Point point);
    void MoveWithInput(Point point) => SetPosition(point);
    void Down(PointerButton button);
    void Up(PointerButton button);
    void Wheel(double amount, bool horizontal);
}

/// <summary>All coordinates crossing this boundary are physical screen pixels.</summary>
internal sealed class WindowsPointerBackend : IPointerBackend
{
    internal const int EscapeInputFlag = 16;
    // SessionId on a new Process instance may enumerate the process table; the current process never
    // changes Windows sessions, so do this once rather than on every input-state check.
    private static readonly int CurrentSessionId = Process.GetCurrentProcess().SessionId;
    public string DesktopKey => $"{CurrentSessionId}:{ObjectName(GetProcessWindowStation())}:{ObjectName(GetThreadDesktop(GetCurrentThreadId()))}";
    public TimeSpan DoubleClickTime => TimeSpan.FromMilliseconds(GetDoubleClickTime());
    public Size DragThreshold
    {
        get { using var dpi = PhysicalDpiScope.Enter(); return new Size(GetSystemMetrics(68), GetSystemMetrics(69)); }
    }

    public void EnsureDesktopAvailable()
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive || CurrentSessionId == 0)
            throw new InvalidOperationException("Pointer input requires an interactive Windows desktop.");
        var desktop = OpenInputDesktop(0, false, 0x0001);
        if (desktop == IntPtr.Zero) throw NativeError("The input desktop is unavailable");
        try
        {
            if (!string.Equals(ObjectName(desktop), ObjectName(GetThreadDesktop(GetCurrentThreadId())), StringComparison.Ordinal))
                throw new InvalidOperationException("The automation desktop is not the active input desktop.");
        }
        finally { CloseDesktop(desktop); }
    }

    public PointerState ReadState()
    {
        using var dpi = PhysicalDpiScope.Enter();
        if (!GetPhysicalCursorPos(out var point)) throw NativeError("GetPhysicalCursorPos failed");
        var buttons = PointerButtons.None;
        var swapped = GetSystemMetrics(23) != 0;
        if (Pressed(swapped ? 2 : 1)) buttons |= PointerButtons.Left;
        if (Pressed(swapped ? 1 : 2)) buttons |= PointerButtons.Right;
        if (Pressed(4)) buttons |= PointerButtons.Middle;
        if (Pressed(5)) buttons |= PointerButtons.X1;
        if (Pressed(6)) buttons |= PointerButtons.X2;
        var modifiers = (Pressed(0x10) ? 1 : 0) | (Pressed(0x11) ? 2 : 0) | (Pressed(0x12) ? 4 : 0)
            | (Pressed(0x5b) || Pressed(0x5c) ? 8 : 0) | (Pressed(0x1b) ? EscapeInputFlag : 0);
        return new PointerState(new Point(point.X, point.Y), buttons, modifiers);
    }

    public bool IsOnMonitor(Point point)
    {
        using var dpi = PhysicalDpiScope.Enter();
        var contains = false;
        MonitorEnum callback = (IntPtr monitor, IntPtr hdc, ref NativeRect bounds, IntPtr data) =>
        {
            contains |= point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom;
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero)) throw NativeError("Monitor enumeration failed");
        GC.KeepAlive(callback);
        return contains;
    }

    public void SetPosition(Point point)
    {
        using var dpi = PhysicalDpiScope.Enter();
        if (!SetPhysicalCursorPos(point.X, point.Y)) throw NativeError("SetPhysicalCursorPos failed");
    }

    public void MoveWithInput(Point point)
    {
        using var dpi = PhysicalDpiScope.Enter();
        var desktop = new Rectangle(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
        var normalized = NormalizeAbsolutePoint(point, desktop);
        // A cursor-position setter alone does not represent a mouse motion packet to OLE drag loops.
        // MOVE | MOVE_NOCOALESCE | VIRTUALDESK | ABSOLUTE preserves event delivery and physical axes.
        Send(0xE001, x: normalized.X, y: normalized.Y);
    }

    internal static Point NormalizeAbsolutePoint(Point point, Rectangle desktop)
    {
        if (desktop.Width <= 0 || desktop.Height <= 0 || !desktop.Contains(point))
            throw new InvalidOperationException("The physical drag point is outside the virtual desktop bounds.");
        // Select the middle of the requested physical pixel's normalized interval, avoiding edge
        // rounding that can land one pixel short on large or negatively positioned monitors.
        return new Point(
            (int)Math.Clamp((((long)point.X - desktop.Left) * 65536 + 32768) / desktop.Width, 0, 65535),
            (int)Math.Clamp((((long)point.Y - desktop.Top) * 65536 + 32768) / desktop.Height, 0, 65535));
    }

    public void Down(PointerButton button) => Send(ButtonFlag(SwapButtonIfNeeded(button), false));
    public void Up(PointerButton button) => Send(ButtonFlag(SwapButtonIfNeeded(button), true));
    public void Wheel(double amount, bool horizontal)
    {
        if (!double.IsFinite(amount) || amount > int.MaxValue / 120d || amount < int.MinValue / 120d)
            throw new ArgumentOutOfRangeException(nameof(amount));
        Send(horizontal ? 0x1000u : 0x0800u, unchecked((uint)(int)Math.Round(amount * 120)));
    }

    internal static PointerButtons Mask(PointerButton button) => button switch
    {
        PointerButton.Left => PointerButtons.Left,
        PointerButton.Right => PointerButtons.Right,
        PointerButton.Middle => PointerButtons.Middle,
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    internal static IntPtr WindowAt(Point point)
    {
        using var dpi = PhysicalDpiScope.Enter();
        return WindowFromPoint(new NativePoint { X = point.X, Y = point.Y });
    }

    internal static int WindowProcess(IntPtr window)
    {
        _ = GetWindowThreadProcessId(window, out var process);
        return (int)process;
    }

    internal static bool ForegroundBelongsTo(int process) => WindowProcess(GetForegroundWindow()) == process;
    internal static void Activate(IntPtr handle)
    {
        if (handle != IntPtr.Zero) _ = SetForegroundWindow(GetAncestor(handle, 2));
    }

    private static bool Pressed(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static PointerButton SwapButtonIfNeeded(PointerButton button) => GetSystemMetrics(23) == 0 ? button : button switch
    {
        PointerButton.Left => PointerButton.Right,
        PointerButton.Right => PointerButton.Left,
        _ => button
    };
    private static uint ButtonFlag(PointerButton button, bool up) => button switch
    {
        PointerButton.Left => up ? 0x0004u : 0x0002u,
        PointerButton.Right => up ? 0x0010u : 0x0008u,
        PointerButton.Middle => up ? 0x0040u : 0x0020u,
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    private static void Send(uint flags, uint data = 0, int x = 0, int y = 0)
    {
        var input = new NativeInput { Type = 0, Mouse = new NativeMouseInput { X = x, Y = y, Flags = flags, Data = data } };
        if (SendInput(1, [input], Marshal.SizeOf<NativeInput>()) != 1) throw NativeError("SendInput did not inject the pointer event");
    }

    private static Win32Exception NativeError(string message) => new(Marshal.GetLastWin32Error(), message);
    private static string ObjectName(IntPtr handle)
    {
        var buffer = new StringBuilder(256);
        if (!GetUserObjectInformation(handle, 2, buffer, buffer.Capacity * 2, out _)) throw NativeError("Desktop identity is unavailable");
        return buffer.ToString();
    }

    internal sealed class PhysicalDpiScope : IDisposable
    {
        private readonly IntPtr _previous;
        private PhysicalDpiScope(IntPtr previous) => _previous = previous;
        public static PhysicalDpiScope Enter()
        {
            try { return new PhysicalDpiScope(SetThreadDpiAwarenessContext(new IntPtr(-4))); }
            catch (EntryPointNotFoundException) { return new PhysicalDpiScope(IntPtr.Zero); }
        }
        public void Dispose() { if (_previous != IntPtr.Zero) _ = SetThreadDpiAwarenessContext(_previous); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMouseInput { public int X; public int Y; public uint Data; public uint Flags; public uint Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public uint Type; public NativeMouseInput Mouse; }
    private delegate bool MonitorEnum(IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetPhysicalCursorPos(out NativePoint point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetPhysicalCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder result, int length, out int needed);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
