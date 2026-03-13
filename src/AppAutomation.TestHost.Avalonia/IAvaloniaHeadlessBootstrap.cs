using Avalonia.Controls;

namespace AppAutomation.TestHost.Avalonia;

public interface IAvaloniaHeadlessBootstrap
{
    ValueTask BeforeLaunchAsync(CancellationToken cancellationToken = default);

    Window CreateMainWindow();
}
