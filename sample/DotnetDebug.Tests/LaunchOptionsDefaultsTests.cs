using System.IO;
using AppAutomation.Session.Contracts;
using DotnetDebug.AppAutomation.TestHost;
using TUnit.Assertions;
using TUnit.Core;

public class LaunchOptionsDefaultsTests
{
    [Test]
    public async Task DotnetDebugHeadlessLaunchOptions_ExposeMainWindowFactory()
    {
        var options = DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions();

        using (Assert.Multiple())
        {
            await Assert.That(options.CreateMainWindow is not null).IsEqualTo(true);
            await Assert.That(options.BeforeLaunchAsync is null).IsEqualTo(true);
            await Assert.That(options.CreateMainWindowAsync is null).IsEqualTo(true);
        }
    }

    [Test]
    public async Task DesktopAppLaunchOptions_DefaultsRemainStable()
    {
        var options = new DesktopAppLaunchOptions
        {
            ExecutablePath = "DotnetDebug.Avalonia.exe"
        };

        using (Assert.Multiple())
        {
            await Assert.That(options.WorkingDirectory).IsNull();
            await Assert.That(options.Arguments.Count).IsEqualTo(0);
            await Assert.That(options.EnvironmentVariables.Count).IsEqualTo(0);
            await Assert.That(options.MainWindowTimeout).IsEqualTo(TimeSpan.FromSeconds(20));
            await Assert.That(options.PollInterval).IsEqualTo(TimeSpan.FromMilliseconds(200));
            await Assert.That(options.WindowPlacement).IsNull();
        }
    }

    [Test]
    public async Task DotnetDebugDesktopLaunchOptions_ResolveCurrentRepositoryLayout()
    {
        var options = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(
            buildConfiguration: "Release",
            buildBeforeLaunch: false);

        using (Assert.Multiple())
        {
            await Assert.That(Path.GetFileName(options.ExecutablePath)).IsEqualTo("DotnetDebug.Avalonia.exe");
            await Assert.That(File.Exists(options.ExecutablePath)).IsEqualTo(true);
            await Assert.That(Directory.Exists(options.WorkingDirectory!)).IsEqualTo(true);
            await Assert.That(options.WindowPlacement).IsNotNull();
            await Assert.That(options.WindowPlacement!.Monitor).IsEqualTo(DesktopMonitorSelector.LastAvailable);
            await Assert.That(options.WindowPlacement.Anchor).IsEqualTo(DesktopWindowAnchor.Center);
        }
    }

    [Test]
    public async Task DotnetDebugDesktopLaunchOptions_UseIsolatedBuildOutput_OnlyWhenBuilding()
    {
        var buildOptions = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(
            buildConfiguration: "Debug",
            buildBeforeLaunch: true,
            buildOncePerProcess: false);
        var noBuildOptions = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(
            buildConfiguration: "Release",
            buildBeforeLaunch: false);

        using (Assert.Multiple())
        {
            await Assert.That(buildOptions.DisposeCallback is not null).IsEqualTo(true);
            await Assert.That(buildOptions.ExecutablePath.Contains("AppAutomationDesktopBuild-", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(noBuildOptions.ExecutablePath.Contains("AppAutomationDesktopBuild-", StringComparison.Ordinal)).IsEqualTo(false);
        }

        buildOptions.DisposeCallback!.Invoke();
    }
}
