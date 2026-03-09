namespace AppAutomation.Session.Contracts;

public sealed class HeadlessAppLaunchOptions
{
    public Func<object>? CreateMainWindow { get; init; }

    public Func<CancellationToken, ValueTask>? BeforeLaunchAsync { get; init; }

    public Func<CancellationToken, ValueTask<object>>? CreateMainWindowAsync { get; init; }
}
