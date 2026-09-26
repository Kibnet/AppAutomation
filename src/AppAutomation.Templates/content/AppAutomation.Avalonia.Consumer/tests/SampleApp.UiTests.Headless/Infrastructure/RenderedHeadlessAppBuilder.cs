using Avalonia;
using Avalonia.Headless;
using SampleApp.AppAutomation.TestHost;

namespace SampleApp.UiTests.Headless.Infrastructure;

public sealed class RenderedHeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure(
            () => (Application)Activator.CreateInstance(SampleAppAppLaunchHost.AvaloniaAppType)!)
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
