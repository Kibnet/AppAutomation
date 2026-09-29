using System.Runtime.InteropServices;
using AppAutomation.FlaUI.Automation;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests;

public sealed class WindowsClipboardTests
{
    private const uint UnicodeTextFormat = 13;

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task SetTextAsync_WritesUnicodeTextToSystemClipboard()
    {
        var expected = $"AppAutomation-{Guid.NewGuid():N}";

        try
        {
            await WindowsClipboard.SetTextAsync(expected, CancellationToken.None);

            await Assert.That(await ReadTextAsync()).IsEqualTo(expected);
        }
        finally
        {
            if (string.Equals(await ReadTextAsync(), expected, StringComparison.Ordinal))
            {
                Clear();
            }
        }
    }

    private static async Task<string?> ReadTextAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    if (!IsClipboardFormatAvailable(UnicodeTextFormat))
                    {
                        return null;
                    }

                    var memory = GetClipboardData(UnicodeTextFormat);
                    var pointer = memory == IntPtr.Zero ? IntPtr.Zero : GlobalLock(memory);
                    if (pointer == IntPtr.Zero)
                    {
                        return null;
                    }

                    try
                    {
                        return Marshal.PtrToStringUni(pointer);
                    }
                    finally
                    {
                        _ = GlobalUnlock(memory);
                    }
                }
                finally
                {
                    _ = CloseClipboard();
                }
            }

            await Task.Delay(20);
        }

        return null;
    }

    private static void Clear()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return;
        }

        try
        {
            _ = EmptyClipboard();
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);
}
