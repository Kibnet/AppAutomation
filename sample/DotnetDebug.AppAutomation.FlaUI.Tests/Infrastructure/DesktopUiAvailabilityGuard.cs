using System.Diagnostics;
using System.Runtime.InteropServices;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;

internal static class DesktopUiAvailabilityGuard
{
    private const uint DesktopReadObjects = 0x0001;

    public static void SkipIfUnavailable()
    {
        var reason = GetUnavailableReason(Capture());
        if (reason is not null)
        {
            Skip.Test(reason);
        }
    }

    internal static string? GetUnavailableReason(DesktopUiAvailabilityProbe probe)
    {
        if (!probe.IsWindows)
        {
            return "FlaUI desktop runtime tests require Windows.";
        }

        if (!probe.IsUserInteractive)
        {
            return "FlaUI desktop runtime tests require an interactive user session.";
        }

        if (probe.ProcessSessionId <= 0)
        {
            return "FlaUI desktop runtime tests require a non-service desktop session.";
        }

        if (!probe.CanAccessInputDesktop)
        {
            return "FlaUI desktop runtime tests require access to the interactive input desktop.";
        }

        return null;
    }

    internal static DesktopUiAvailabilityProbe Capture()
    {
        return new DesktopUiAvailabilityProbe(
            OperatingSystem.IsWindows(),
            Environment.UserInteractive,
            Process.GetCurrentProcess().SessionId,
            CanAccessInputDesktop());
    }

    private static bool CanAccessInputDesktop()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var desktopHandle = OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktopHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return true;
        }
        finally
        {
            _ = CloseDesktop(desktopHandle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktopHandle);
}

internal readonly record struct DesktopUiAvailabilityProbe(
    bool IsWindows,
    bool IsUserInteractive,
    int ProcessSessionId,
    bool CanAccessInputDesktop);
