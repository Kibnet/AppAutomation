using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AppAutomation.FlaUI.Automation;

internal static class WindowsClipboard
{
    private const uint UnicodeTextFormat = 13;
    private const uint MoveableMemory = 0x0002;
    private const int OpenAttemptCount = 10;
    private static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(20);

    public static async Task SetTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (var attempt = 1; attempt <= OpenAttemptCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    WriteOpenedClipboard(text);
                    return;
                }
                finally
                {
                    _ = CloseClipboard();
                }
            }

            if (attempt < OpenAttemptCount)
            {
                await Task.Delay(OpenRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new Win32Exception(
            Marshal.GetLastWin32Error(),
            "The Windows clipboard could not be opened for a text write.");
    }

    private static void WriteOpenedClipboard(string text)
    {
        if (!EmptyClipboard())
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The Windows clipboard could not be cleared.");
        }

        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var memory = GlobalAlloc(MoveableMemory, (nuint)bytes.Length);
        if (memory == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Clipboard text memory could not be allocated.");
        }

        var clipboardOwnsMemory = false;
        try
        {
            var destination = GlobalLock(memory);
            if (destination == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Clipboard text memory could not be locked.");
            }

            try
            {
                Marshal.Copy(bytes, 0, destination, bytes.Length);
            }
            finally
            {
                _ = GlobalUnlock(memory);
            }

            if (SetClipboardData(UnicodeTextFormat, memory) == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Clipboard text could not be stored.");
            }

            clipboardOwnsMemory = true;
        }
        finally
        {
            if (!clipboardOwnsMemory)
            {
                _ = GlobalFree(memory);
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
