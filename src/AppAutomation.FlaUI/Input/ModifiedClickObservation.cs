using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AppAutomation.FlaUI.Input;

/// <summary>Observes the tagged keyboard events of one modified click on its dispatch thread.</summary>
internal sealed class ModifiedClickObservation : IDisposable
{
    private const int LowLevelKeyboardHook = 13;
    private const uint ProcessSentMessages = 0x00400000; // PM_QS_SENDMESSAGE, without removing posted messages.
    private const int InvalidHookHandle = 1404;
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(1);
    private static readonly HookProc Callback = ObserveKeyboard;
    [ThreadStatic] private static ModifiedClickObservation? _current;
    private readonly IntPtr _hook;

    internal ModifiedClickObservation(ushort key)
    {
        if (_current != null) throw new InvalidOperationException("A modified click is already observing this thread.");
        Span<byte> bytes = stackalloc byte[IntPtr.Size];
        RandomNumberGenerator.Fill(bytes);
        Tag = MemoryMarshal.Read<nuint>(bytes) | 1;
        Input = new(key, Tag);
        _hook = SetWindowsHookExW(LowLevelKeyboardHook, Callback, GetModuleHandleW(IntPtr.Zero), 0);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not observe the modified click's key release.");
        _current = this;
    }

    internal nuint Tag { get; }
    internal KeyboardEvents Input { get; }

    internal void Pump() => _ = PeekMessageW(out _, IntPtr.Zero, 0, 0, ProcessSentMessages);

    internal void ConfirmRelease(Func<bool> isPressed)
    {
        // SendInput queues the batch. Even the hook observes key-up before Windows updates key state.
        // Keep the lease until both the tagged event and the released state have been observed.
        var started = Stopwatch.GetTimestamp();
        do
        {
            Pump();
            if (Input.ReleaseAccepted && !isPressed()) return;
            if (Input.ReleaseAccepted && Input.ForeignKey)
                throw new PointerInterferenceException("Keyboard input changed during the modified click's release.");
            Thread.Sleep(1);
        } while (Stopwatch.GetElapsedTime(started) < ReleaseTimeout);
        throw new PointerCleanupException("Windows did not confirm the modified click's key release. The click was not retried.");
    }

    public void Dispose()
    {
        _current = null;
        if (!UnhookWindowsHookEx(_hook) && Marshal.GetLastWin32Error() is var error && error != InvalidHookHandle)
            throw new PointerCleanupException("Could not remove the modified click's keyboard observer.", new Win32Exception(error));
    }

    private static IntPtr ObserveKeyboard(int code, IntPtr message, IntPtr data)
    {
        var next = CallNextHookEx(IntPtr.Zero, code, message, data);
        if (code == 0 && _current is { } observation)
        {
            var input = Marshal.PtrToStructure<NativeKeyboardEvent>(data);
            observation.Input.Observe(input.Key, input.Flags, input.Extra, accepted: next == IntPtr.Zero);
        }
        return next;
    }

    internal sealed class KeyboardEvents(ushort key, nuint tag)
    {
        private const uint Injected = 0x10;
        private const uint KeyUp = 0x80;

        internal bool ForeignKey { get; private set; }
        internal bool PressAccepted { get; private set; }
        internal bool ReleaseAccepted { get; private set; }

        internal void Observe(uint virtualKey, uint flags, nuint extra, bool accepted)
        {
            if (virtualKey == key && extra == tag && (flags & Injected) != 0)
            {
                if (!accepted) return;
                if ((flags & KeyUp) == 0) PressAccepted = true;
                else ReleaseAccepted = true;
            }
            else if (accepted && virtualKey is 0x10 or 0x11 or 0x12 or 0x1b or 0x5b or 0x5c or >= 0xa0 and <= 0xa5)
            {
                ForeignKey = true;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardEvent { public uint Key, ScanCode, Flags, Time; public nuint Extra; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public nuint WParam;
        public IntPtr LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandleW(IntPtr moduleName);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookExW(int id, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out NativeMessage message, IntPtr window, uint min, uint max, uint flags);
}
