using System.ComponentModel;
using System.Runtime.InteropServices;
using AppAutomation.FlaUI.Automation;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests;

public sealed class WindowsClipboardTests
{
    [Test]
    public async Task SetTextAsync_UsesConfiguredOwnerAndClosesClipboard()
    {
        using var native = new FakeWindowsClipboardNative();
        var owner = new IntPtr(42);

        await WindowsClipboard.SetTextAsync(
            owner,
            "Item 42",
            native,
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(native.Owner).IsEqualTo(owner);
            await Assert.That(native.Text).IsEqualTo("Item 42");
            await Assert.That(native.CloseCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task OwnerWindow_BelongsToAutomationProcess()
    {
        var owner = WindowsClipboardOwnerWindow.Handle;
        _ = GetWindowThreadProcessId(owner, out var processId);

        using (Assert.Multiple())
        {
            await Assert.That(owner).IsNotEqualTo(IntPtr.Zero);
            await Assert.That(processId).IsEqualTo((uint)Environment.ProcessId);
        }
    }

    [Test]
    public async Task SetTextAsync_ReportsCloseFailureAfterSuccessfulWrite()
    {
        using var native = new FakeWindowsClipboardNative { CloseSucceeds = false };

        var exception = await Assert.ThrowsAsync<Win32Exception>(() =>
            WindowsClipboard.SetTextAsync(
                new IntPtr(42),
                "Item 42",
                native,
                CancellationToken.None));

        await Assert.That(exception!.Message).Contains("could not be closed");
    }

    private sealed class FakeWindowsClipboardNative : IWindowsClipboardNative, IDisposable
    {
        private readonly List<IntPtr> _allocations = [];

        public IntPtr Owner { get; private set; }
        public string? Text { get; private set; }
        public int CloseCount { get; private set; }
        public bool CloseSucceeds { get; init; } = true;

        public bool OpenClipboard(IntPtr owner)
        {
            Owner = owner;
            return true;
        }

        public bool CloseClipboard()
        {
            CloseCount++;
            return CloseSucceeds;
        }

        public bool EmptyClipboard() => true;

        public IntPtr SetClipboardData(uint format, IntPtr memory)
        {
            Text = Marshal.PtrToStringUni(memory);
            return memory;
        }

        public IntPtr GlobalAlloc(uint flags, nuint bytes)
        {
            var memory = Marshal.AllocHGlobal(checked((int)bytes));
            _allocations.Add(memory);
            return memory;
        }

        public IntPtr GlobalLock(IntPtr memory) => memory;
        public bool GlobalUnlock(IntPtr memory) => true;

        public IntPtr GlobalFree(IntPtr memory)
        {
            if (_allocations.Remove(memory))
            {
                Marshal.FreeHGlobal(memory);
            }

            return IntPtr.Zero;
        }

        public int GetLastError() => 5;

        public void Dispose()
        {
            foreach (var memory in _allocations)
            {
                Marshal.FreeHGlobal(memory);
            }

            _allocations.Clear();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
