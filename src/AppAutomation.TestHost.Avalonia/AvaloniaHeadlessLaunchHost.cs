using Avalonia.Controls;
using AppAutomation.Session.Contracts;

namespace AppAutomation.TestHost.Avalonia;

public static class AvaloniaHeadlessLaunchHost
{
    public static HeadlessAppLaunchOptions Create(IAvaloniaHeadlessBootstrap bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);

        return new HeadlessAppLaunchOptions
        {
            BeforeLaunchAsync = bootstrap.BeforeLaunchAsync,
            CreateMainWindow = bootstrap.CreateMainWindow
        };
    }

    public static HeadlessAppLaunchOptions Create(
        Func<Window> createMainWindow,
        Func<CancellationToken, ValueTask>? beforeLaunchAsync = null)
    {
        ArgumentNullException.ThrowIfNull(createMainWindow);

        return new HeadlessAppLaunchOptions
        {
            BeforeLaunchAsync = beforeLaunchAsync,
            CreateMainWindow = createMainWindow
        };
    }

    public static HeadlessAppLaunchOptions Create(
        Func<CancellationToken, ValueTask<Window>> createMainWindowAsync,
        Func<CancellationToken, ValueTask>? beforeLaunchAsync = null)
    {
        ArgumentNullException.ThrowIfNull(createMainWindowAsync);

        return new HeadlessAppLaunchOptions
        {
            BeforeLaunchAsync = beforeLaunchAsync,
            CreateMainWindowAsync = async cancellationToken => await createMainWindowAsync(cancellationToken)
        };
    }
}
