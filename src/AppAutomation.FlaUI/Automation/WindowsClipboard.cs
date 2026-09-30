using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

namespace AppAutomation.FlaUI.Automation;

internal static class WindowsClipboard
{
    private const uint UnicodeTextFormat = 13;
    private const uint MoveableMemory = 0x0002;
    private const int OpenAttemptCount = 10;
    private static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(20);

    public static Task SetTextAsync(string text, CancellationToken cancellationToken) =>
        SetTextAsync(WindowsClipboardOwnerWindow.Handle, text, WindowsClipboardNative.Instance, cancellationToken);

    internal static async Task SetTextAsync(
        IntPtr ownerWindow,
        string text,
        IWindowsClipboardNative native,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(native);
        if (ownerWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "A native owner window handle is required for a Windows clipboard write.");
        }

        for (var attempt = 1; attempt <= OpenAttemptCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (native.OpenClipboard(ownerWindow))
            {
                WriteOpenedClipboard(native, text);
                return;
            }

            if (attempt < OpenAttemptCount)
            {
                await Task.Delay(OpenRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw CreateWin32Exception(
            native,
            "The Windows clipboard could not be opened for a text write.");
    }

    private static void WriteOpenedClipboard(IWindowsClipboardNative native, string text)
    {
        Exception? writeFailure = null;
        try
        {
            WriteClipboardText(native, text);
        }
        catch (Exception exception)
        {
            writeFailure = exception;
        }

        var closeSucceeded = native.CloseClipboard();
        if (writeFailure is not null)
        {
            ExceptionDispatchInfo.Capture(writeFailure).Throw();
        }

        if (!closeSucceeded)
        {
            throw CreateWin32Exception(native, "The Windows clipboard could not be closed after a text write.");
        }
    }

    private static void WriteClipboardText(IWindowsClipboardNative native, string text)
    {
        if (!native.EmptyClipboard())
        {
            throw CreateWin32Exception(native, "The Windows clipboard could not be cleared.");
        }

        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var memory = native.GlobalAlloc(MoveableMemory, (nuint)bytes.Length);
        if (memory == IntPtr.Zero)
        {
            throw CreateWin32Exception(native, "Clipboard text memory could not be allocated.");
        }

        var clipboardOwnsMemory = false;
        try
        {
            var destination = native.GlobalLock(memory);
            if (destination == IntPtr.Zero)
            {
                throw CreateWin32Exception(native, "Clipboard text memory could not be locked.");
            }

            try
            {
                Marshal.Copy(bytes, 0, destination, bytes.Length);
            }
            finally
            {
                _ = native.GlobalUnlock(memory);
            }

            if (native.SetClipboardData(UnicodeTextFormat, memory) == IntPtr.Zero)
            {
                throw CreateWin32Exception(native, "Clipboard text could not be stored.");
            }

            clipboardOwnsMemory = true;
        }
        finally
        {
            if (!clipboardOwnsMemory)
            {
                _ = native.GlobalFree(memory);
            }
        }
    }

    private static Win32Exception CreateWin32Exception(
        IWindowsClipboardNative native,
        string message) =>
        new(native.GetLastError(), message);
}

internal interface IWindowsClipboardNative
{
    bool OpenClipboard(IntPtr owner);
    bool CloseClipboard();
    bool EmptyClipboard();
    IntPtr SetClipboardData(uint format, IntPtr memory);
    IntPtr GlobalAlloc(uint flags, nuint bytes);
    IntPtr GlobalLock(IntPtr memory);
    bool GlobalUnlock(IntPtr memory);
    IntPtr GlobalFree(IntPtr memory);
    int GetLastError();
}

internal sealed class WindowsClipboardNative : IWindowsClipboardNative
{
    public static WindowsClipboardNative Instance { get; } = new();

    private WindowsClipboardNative()
    {
    }

    public bool OpenClipboard(IntPtr owner) => NativeMethods.OpenClipboard(owner);
    public bool CloseClipboard() => NativeMethods.CloseClipboard();
    public bool EmptyClipboard() => NativeMethods.EmptyClipboard();
    public IntPtr SetClipboardData(uint format, IntPtr memory) => NativeMethods.SetClipboardData(format, memory);
    public IntPtr GlobalAlloc(uint flags, nuint bytes) => NativeMethods.GlobalAlloc(flags, bytes);
    public IntPtr GlobalLock(IntPtr memory) => NativeMethods.GlobalLock(memory);
    public bool GlobalUnlock(IntPtr memory) => NativeMethods.GlobalUnlock(memory);
    public IntPtr GlobalFree(IntPtr memory) => NativeMethods.GlobalFree(memory);
    public int GetLastError() => Marshal.GetLastWin32Error();

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetClipboardData(uint format, IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalFree(IntPtr memory);
    }
}

internal static class WindowsClipboardOwnerWindow
{
    private static readonly Lazy<IntPtr> OwnerHandle = new(
        CreateOwnerWindow,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static IntPtr Handle => OwnerHandle.Value;

    private static IntPtr CreateOwnerWindow()
    {
        using var ready = new ManualResetEventSlim();
        Exception? failure = null;
        var handle = IntPtr.Zero;
        var thread = new Thread(() =>
        {
            try
            {
                handle = NativeMethods.CreateWindowEx(
                    0,
                    "STATIC",
                    "AppAutomation Clipboard Owner",
                    0,
                    0,
                    0,
                    0,
                    0,
                    new IntPtr(-3),
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);
                if (handle == IntPtr.Zero)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "The AppAutomation clipboard owner window could not be created.");
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                ready.Set();
            }

            if (handle == IntPtr.Zero)
            {
                return;
            }

            while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                _ = NativeMethods.TranslateMessage(ref message);
                _ = NativeMethods.DispatchMessage(ref message);
            }
        })
        {
            IsBackground = true,
            Name = "AppAutomation Clipboard Owner"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return handle;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWindowEx(
            uint extendedStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll", EntryPoint = "GetMessageW")]
        internal static extern int GetMessage(out NativeMessage message, IntPtr window, uint minimum, uint maximum);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TranslateMessage(ref NativeMessage message);

        [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
        internal static extern IntPtr DispatchMessage(ref NativeMessage message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
        public uint LPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
