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

    public static HeadlessAppLaunchOptions Create<TPayload>(
        IAvaloniaHeadlessBootstrap bootstrap,
        AutomationLaunchScenario<TPayload> scenario)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);

        return Create(bootstrap.CreateMainWindow, scenario, bootstrap.BeforeLaunchAsync);
    }

    public static HeadlessAppLaunchOptions Create<TPayload>(
        Func<Window> createMainWindow,
        AutomationLaunchScenario<TPayload> scenario,
        Func<CancellationToken, ValueTask>? beforeLaunchAsync = null)
    {
        ArgumentNullException.ThrowIfNull(createMainWindow);

        var scenarioTransport = AutomationLaunchScenarioTransport.Create(scenario);
        return new HeadlessAppLaunchOptions
        {
            BeforeLaunchAsync = async cancellationToken =>
            {
                using var ambientOverride = scenarioTransport.PushAmbientOverride();
                if (beforeLaunchAsync is not null)
                {
                    await beforeLaunchAsync(cancellationToken);
                }
            },
            CreateMainWindow = () =>
            {
                using var ambientOverride = scenarioTransport.PushAmbientOverride();
                return createMainWindow();
            },
            DisposeCallback = scenarioTransport.Dispose
        };
    }

    public static HeadlessAppLaunchOptions Create<TPayload>(
        Func<CancellationToken, ValueTask<Window>> createMainWindowAsync,
        AutomationLaunchScenario<TPayload> scenario,
        Func<CancellationToken, ValueTask>? beforeLaunchAsync = null)
    {
        ArgumentNullException.ThrowIfNull(createMainWindowAsync);

        var scenarioTransport = AutomationLaunchScenarioTransport.Create(scenario);
        return new HeadlessAppLaunchOptions
        {
            BeforeLaunchAsync = async cancellationToken =>
            {
                using var ambientOverride = scenarioTransport.PushAmbientOverride();
                if (beforeLaunchAsync is not null)
                {
                    await beforeLaunchAsync(cancellationToken);
                }
            },
            CreateMainWindowAsync = async cancellationToken =>
            {
                using var ambientOverride = scenarioTransport.PushAmbientOverride();
                return await createMainWindowAsync(cancellationToken);
            },
            DisposeCallback = scenarioTransport.Dispose
        };
    }
}
