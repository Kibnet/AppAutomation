using DotnetDebug.Avalonia;
using AppAutomation.Session.Contracts;
using AppAutomation.TestHost.Avalonia;

namespace DotnetDebug.AppAutomation.TestHost;

public static class DotnetDebugAppLaunchHost
{
    private static readonly AvaloniaDesktopAppDescriptor DesktopApp = new(
        solutionFileNames:
        [
            "AppAutomation.sln"
        ],
        desktopProjectRelativePaths:
        [
            "sample\\DotnetDebug.Avalonia\\DotnetDebug.Avalonia.csproj"
        ],
        desktopTargetFramework: "net10.0",
        executableName: "DotnetDebug.Avalonia.exe");

    public static DesktopAppLaunchOptions CreateDesktopLaunchOptions(
        string? buildConfiguration = null,
        bool buildBeforeLaunch = true,
        bool buildOncePerProcess = true,
        TimeSpan? buildTimeout = null,
        TimeSpan? mainWindowTimeout = null,
        TimeSpan? pollInterval = null)
    {
        return AvaloniaDesktopLaunchHost.CreateLaunchOptions(
            DesktopApp,
            new AvaloniaDesktopLaunchOptions
            {
                BuildConfiguration = buildConfiguration ?? BuildConfigurationDefaults.ForAssembly(typeof(DotnetDebugAppLaunchHost).Assembly),
                BuildBeforeLaunch = buildBeforeLaunch,
                BuildOncePerProcess = buildOncePerProcess,
                BuildTimeout = buildTimeout ?? TimeSpan.FromMinutes(5),
                MainWindowTimeout = mainWindowTimeout ?? TimeSpan.FromSeconds(20),
                PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(200),
                UseIsolatedBuildOutput = buildBeforeLaunch
            });
    }

    public static HeadlessAppLaunchOptions CreateHeadlessLaunchOptions()
    {
        return AvaloniaHeadlessLaunchHost.Create(static () => new MainWindow());
    }
}
