using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

public sealed class DesktopUiAvailabilityGuardTests
{
    [Test]
    public async Task GetUnavailableReason_ReturnsWindowsRequirement_ForNonWindowsProbe()
    {
        var reason = DesktopUiAvailabilityGuard.GetUnavailableReason(new DesktopUiAvailabilityProbe(
            IsWindows: false,
            IsUserInteractive: true,
            ProcessSessionId: 1,
            CanAccessInputDesktop: true));

        await Assert.That(reason).IsEqualTo("FlaUI desktop runtime tests require Windows.");
    }

    [Test]
    public async Task GetUnavailableReason_ReturnsInteractiveRequirement_ForNonInteractiveProbe()
    {
        var reason = DesktopUiAvailabilityGuard.GetUnavailableReason(new DesktopUiAvailabilityProbe(
            IsWindows: true,
            IsUserInteractive: false,
            ProcessSessionId: 1,
            CanAccessInputDesktop: true));

        await Assert.That(reason).IsEqualTo("FlaUI desktop runtime tests require an interactive user session.");
    }

    [Test]
    public async Task GetUnavailableReason_ReturnsDesktopSessionRequirement_ForServiceSession()
    {
        var reason = DesktopUiAvailabilityGuard.GetUnavailableReason(new DesktopUiAvailabilityProbe(
            IsWindows: true,
            IsUserInteractive: true,
            ProcessSessionId: 0,
            CanAccessInputDesktop: true));

        await Assert.That(reason).IsEqualTo("FlaUI desktop runtime tests require a non-service desktop session.");
    }

    [Test]
    public async Task GetUnavailableReason_ReturnsInputDesktopRequirement_WhenDesktopCannotBeOpened()
    {
        var reason = DesktopUiAvailabilityGuard.GetUnavailableReason(new DesktopUiAvailabilityProbe(
            IsWindows: true,
            IsUserInteractive: true,
            ProcessSessionId: 1,
            CanAccessInputDesktop: false));

        await Assert.That(reason).IsEqualTo("FlaUI desktop runtime tests require access to the interactive input desktop.");
    }

    [Test]
    public async Task GetUnavailableReason_ReturnsNull_WhenDesktopUiIsAvailable()
    {
        var reason = DesktopUiAvailabilityGuard.GetUnavailableReason(new DesktopUiAvailabilityProbe(
            IsWindows: true,
            IsUserInteractive: true,
            ProcessSessionId: 1,
            CanAccessInputDesktop: true));

        await Assert.That(reason).IsNull();
    }
}
