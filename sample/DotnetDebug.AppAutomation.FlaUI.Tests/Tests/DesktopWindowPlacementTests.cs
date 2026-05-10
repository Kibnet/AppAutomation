using System.Drawing;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using DotnetDebug.AppAutomation.TestHost;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

public sealed class DesktopWindowPlacementTests
{
    private const int PlacementSmokeWidth = 800;
    private const int PlacementSmokeHeight = 600;

    [Test]
    public async Task DesktopWindowPlacement_RejectsInvalidPublicValues()
    {
        using (Assert.Multiple())
        {
            await Assert.That(() => DesktopMonitorSelector.FromIndex(-1))
                .Throws<ArgumentOutOfRangeException>();
            await Assert.That(() => DesktopMonitorSelector.FromDeviceName("   "))
                .Throws<ArgumentException>();
            await Assert.That(() => new DesktopWindowSize(0, 600))
                .Throws<ArgumentOutOfRangeException>();
            await Assert.That(() => new DesktopWindowSize(800, 0))
                .Throws<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task SortMonitors_PutsPrimaryFirst_ThenVirtualCoordinates()
    {
        var sorted = DesktopWindowPlacementService.SortMonitors(
        [
            CreateMonitor("DISPLAY3", new Rectangle(2000, 0, 1000, 800), isPrimary: false),
            CreateMonitor("DISPLAY2", new Rectangle(-1200, 0, 1200, 900), isPrimary: false),
            CreateMonitor("DISPLAY1", new Rectangle(0, 0, 1600, 900), isPrimary: true)
        ]);

        using (Assert.Multiple())
        {
            await Assert.That(sorted[0].DeviceName).IsEqualTo("DISPLAY1");
            await Assert.That(sorted[1].DeviceName).IsEqualTo("DISPLAY2");
            await Assert.That(sorted[2].DeviceName).IsEqualTo("DISPLAY3");
        }
    }

    [Test]
    public async Task ResolvePlacement_CentersInSelectedWorkingArea()
    {
        var placement = DesktopWindowPlacement.Centered(
            DesktopMonitorSelector.FromIndex(1),
            width: 800,
            height: 600);

        var result = DesktopWindowPlacementService.ResolvePlacement(
            placement,
            currentWindowBounds: new Rectangle(0, 0, 300, 200),
            CreateMonitors());

        using (Assert.Multiple())
        {
            await Assert.That(result.Monitor.DeviceName).IsEqualTo("DISPLAY2");
            await Assert.That(result.PlacementArea).IsEqualTo(new Rectangle(1920, 0, 1280, 860));
            await Assert.That(result.TargetBounds).IsEqualTo(new Rectangle(2160, 130, 800, 600));
        }
    }

    [Test]
    public async Task ResolvePlacement_RejectsOffsetCausedOutOfAreaRectangle()
    {
        var placement = new DesktopWindowPlacement
        {
            Monitor = DesktopMonitorSelector.Primary,
            Size = new DesktopWindowSize(800, 600),
            Anchor = DesktopWindowAnchor.TopLeft,
            Offset = new DesktopWindowOffset(-1, 0)
        };

        var exception = CapturePlacementException(placement);

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.Message).Contains("outside the selected monitor area");
            await Assert.That(exception.Message).Contains("Available monitors");
        }
    }

    [Test]
    public async Task ResolvePlacement_RejectsExplicitOversizedGeometry()
    {
        var placement = new DesktopWindowPlacement
        {
            Monitor = DesktopMonitorSelector.Primary,
            Size = new DesktopWindowSize(2000, 600)
        };

        var exception = CapturePlacementException(placement);

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.Message).Contains("exceeds selected monitor area");
        }
    }

    [Test]
    public async Task ResolvePlacement_FallsBackToPrimary_WhenRequestedMonitorIsUnavailableAndFallbackIsEnabled()
    {
        var placement = new DesktopWindowPlacement
        {
            Monitor = DesktopMonitorSelector.FromIndex(42),
            Size = new DesktopWindowSize(800, 600),
            UnavailableBehavior = DesktopWindowPlacementUnavailableBehavior.UsePrimaryMonitor
        };

        var result = DesktopWindowPlacementService.ResolvePlacement(
            placement,
            currentWindowBounds: new Rectangle(0, 0, 300, 200),
            CreateMonitors());

        using (Assert.Multiple())
        {
            await Assert.That(result.UsedFallback).IsEqualTo(true);
            await Assert.That(result.Monitor.IsPrimary).IsEqualTo(true);
            await Assert.That(result.Monitor.DeviceName).IsEqualTo("DISPLAY1");
        }
    }

    [Test]
    public async Task ResolvePlacement_UsesLastAvailableMonitor()
    {
        var placement = new DesktopWindowPlacement
        {
            Monitor = DesktopMonitorSelector.LastAvailable,
            Size = new DesktopWindowSize(800, 600)
        };

        var result = DesktopWindowPlacementService.ResolvePlacement(
            placement,
            currentWindowBounds: new Rectangle(0, 0, 300, 200),
            CreateMonitors());

        await Assert.That(result.Monitor.DeviceName).IsEqualTo("DISPLAY2");
    }

    [Test]
    public async Task ResolvePlacement_RejectsUnknownEnumValues()
    {
        var placement = new DesktopWindowPlacement
        {
            Monitor = DesktopMonitorSelector.Primary,
            Size = new DesktopWindowSize(800, 600),
            Anchor = (DesktopWindowAnchor)999
        };

        var exception = CapturePlacementException(placement);

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.Message).Contains("Unknown window anchor");
        }
    }

    [Test]
    public async Task DesktopAppSession_Launch_AppliesLastAvailableMonitorPlacement()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        var placement = DesktopWindowPlacement.Centered(
            DesktopMonitorSelector.LastAvailable,
            width: PlacementSmokeWidth,
            height: PlacementSmokeHeight);
        SkipIfPlacementCannotFit(placement);

        using var session = DesktopAppSession.Launch(
            DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(
                buildBeforeLaunch: false,
                windowPlacement: placement));

        var bounds = DesktopWindowPlacementService.GetNativeWindowRectangle(session.MainWindow);

        using (Assert.Multiple())
        {
            await Assert.That(Math.Abs(bounds.Width - PlacementSmokeWidth) <= 3).IsEqualTo(true);
            await Assert.That(Math.Abs(bounds.Height - PlacementSmokeHeight) <= 3).IsEqualTo(true);
        }
    }

    [Test]
    public async Task DesktopAppSession_Launch_CleansUp_WhenPlacementFailsAfterProcessStart()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        var callbackCalled = false;
        var baseOptions = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(
            buildBeforeLaunch: false,
            windowPlacement: DesktopWindowPlacement.Centered(
                DesktopMonitorSelector.LastAvailable,
                width: 100_000,
                height: 100_000));
        var options = new DesktopAppLaunchOptions
        {
            ExecutablePath = baseOptions.ExecutablePath,
            WorkingDirectory = baseOptions.WorkingDirectory,
            Arguments = baseOptions.Arguments,
            EnvironmentVariables = baseOptions.EnvironmentVariables,
            MainWindowTimeout = baseOptions.MainWindowTimeout,
            PollInterval = baseOptions.PollInterval,
            WindowPlacement = baseOptions.WindowPlacement,
            DisposeCallback = () =>
            {
                callbackCalled = true;
                baseOptions.DisposeCallback?.Invoke();
            }
        };

        var exception = await CaptureLaunchExceptionAsync(options);

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.Message).Contains("exceeds selected monitor area");
            await Assert.That(callbackCalled).IsEqualTo(true);
        }
    }

    private static InvalidOperationException? CapturePlacementException(DesktopWindowPlacement placement)
    {
        try
        {
            _ = DesktopWindowPlacementService.ResolvePlacement(
                placement,
                currentWindowBounds: new Rectangle(0, 0, 300, 200),
                CreateMonitors());
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    private static async Task<Exception?> CaptureLaunchExceptionAsync(DesktopAppLaunchOptions options)
    {
        try
        {
            using var _ = DesktopAppSession.Launch(options);
            return null;
        }
        catch (Exception ex)
        {
            await Task.Yield();
            return ex;
        }
    }

    private static void SkipIfPlacementCannotFit(DesktopWindowPlacement placement)
    {
        try
        {
            _ = DesktopWindowPlacementService.ResolvePlacement(
                placement,
                currentWindowBounds: new Rectangle(0, 0, PlacementSmokeWidth, PlacementSmokeHeight),
                DesktopWindowPlacementService.EnumerateMonitors());
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("exceeds selected monitor area", StringComparison.Ordinal)
                                                || ex.Message.Contains("outside the selected monitor area", StringComparison.Ordinal))
        {
            Skip.Test($"FlaUI placement smoke requires a target working area that fits {PlacementSmokeWidth}x{PlacementSmokeHeight}. {ex.Message}");
        }
    }

    private static DesktopMonitorInfo[] CreateMonitors()
    {
        return
        [
            CreateMonitor("DISPLAY2", new Rectangle(1920, 0, 1280, 900), workingArea: new Rectangle(1920, 0, 1280, 860)),
            CreateMonitor("DISPLAY1", new Rectangle(0, 0, 1600, 900), workingArea: new Rectangle(0, 0, 1600, 860), isPrimary: true)
        ];
    }

    private static DesktopMonitorInfo CreateMonitor(
        string deviceName,
        Rectangle bounds,
        Rectangle? workingArea = null,
        bool isPrimary = false)
    {
        return new DesktopMonitorInfo(deviceName, bounds, workingArea ?? bounds, isPrimary);
    }
}
