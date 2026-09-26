using Avalonia;
using Avalonia.Headless;
using DotnetDebug.Avalonia;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Infrastructure;

public sealed class RenderedHeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
