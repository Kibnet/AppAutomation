using AvaloniaWindow = Avalonia.Controls.Window;
using AppAutomation.Session.Contracts;

namespace AppAutomation.Avalonia.Headless.Session;

public sealed class DesktopAppSession : IDisposable
{
    private readonly AvaloniaWindow _nativeWindow;
    private readonly Action? _disposeCallback;
    private bool _disposed;

    private DesktopAppSession(AvaloniaWindow nativeWindow, Action? disposeCallback)
    {
        _nativeWindow = nativeWindow;
        _disposeCallback = disposeCallback;
        MainWindow = nativeWindow;
    }

    public AvaloniaWindow MainWindow { get; }

    public static DesktopAppSession Launch(HeadlessAppLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.CreateMainWindow is null && options.CreateMainWindowAsync is null)
        {
            throw new InvalidOperationException(
                "Headless launch options must configure CreateMainWindow or CreateMainWindowAsync.");
        }

        try
        {
            if (options.BeforeLaunchAsync is not null)
            {
                options.BeforeLaunchAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }

            var window = HeadlessRuntime.Dispatch(() =>
            {
                var created = options.CreateMainWindowAsync is not null
                    ? options.CreateMainWindowAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult()
                    : options.CreateMainWindow?.Invoke();

                return created as AvaloniaWindow
                    ?? throw new InvalidOperationException("Headless launch factory must return Avalonia.Controls.Window.");
            });

            return new DesktopAppSession(window, options.DisposeCallback);
        }
        catch
        {
            options.DisposeCallback?.Invoke();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _disposeCallback?.Invoke();
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }
}
