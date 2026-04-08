using AppAutomation.Recorder.Avalonia.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

namespace AppAutomation.Recorder.Avalonia;

public static class AppAutomationRecorder
{
    private static readonly Dictionary<Window, IAppAutomationRecorderSession> Sessions = new();
    private static readonly Dictionary<Window, Window> OverlayWindows = new();

    public static IAppAutomationRecorderSession Attach(Window window, AppAutomationRecorderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (Sessions.TryGetValue(window, out var existing))
        {
            return existing;
        }

        options ??= new AppAutomationRecorderOptions();
        var session = new RecorderSession(window, options);
        Sessions[window] = session;

        if (options.ShowOverlay)
        {
            AttachOverlay(window, session, options);
        }

        window.Closed += (_, _) =>
        {
            if (Sessions.Remove(window, out var current))
            {
                current.Dispose();
            }

            if (OverlayWindows.Remove(window, out var overlayWindow))
            {
                overlayWindow.Close();
            }
        };

        return session;
    }

    private static void AttachOverlay(Window owner, IAppAutomationRecorderSession session, AppAutomationRecorderOptions options)
    {
        var overlayWindow = CreateOverlayWindow(session, options);
        var overlay = (RecorderOverlay)overlayWindow.Content!;

        async Task ExportAsync()
        {
            if (!options.Overlay.EnableExportButton)
            {
                return;
            }

            if (session is RecorderSession recorderSession)
            {
                _ = await recorderSession.ExportWithDirectoryPickerAsync(async cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var folders = await overlayWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = "Export AppAutomation Recorder Output",
                        AllowMultiple = false
                    });
                    return folders.FirstOrDefault()?.Path.LocalPath;
                });
                return;
            }

            var folders = await overlayWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Export AppAutomation Recorder Output",
                AllowMultiple = false
            });
            var selectedFolder = folders.FirstOrDefault()?.Path.LocalPath;
            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                return;
            }

            _ = await session.SaveToDirectoryAsync(selectedFolder);
        }

        overlay.ExportRequested += async (_, _) => await ExportAsync();

        if (session is RecorderSession recorderSession)
        {
            recorderSession.ExportRequested += async (_, _) => await ExportAsync();
            recorderSession.OverlayToggleRequested += (_, _) => overlay.ToggleMinimized();
        }

        overlayWindow.Closed += (_, _) =>
        {
            if (OverlayWindows.TryGetValue(owner, out var registeredWindow)
                && ReferenceEquals(registeredWindow, overlayWindow))
            {
                OverlayWindows.Remove(owner);
            }
        };

        overlayWindow.Show();
        OverlayWindows[owner] = overlayWindow;
    }

    internal static Window CreateOverlayWindow(IAppAutomationRecorderSession session, AppAutomationRecorderOptions options)
    {
        var overlay = new RecorderOverlay();
        overlay.Attach(session, options);
        var configuration = GetOverlayWindowConfiguration(options);

        return new Window
        {
            CanResize = configuration.CanResize,
            SizeToContent = configuration.SizeToContent,
            Width = configuration.Width,
            Height = configuration.Height,
            MinWidth = configuration.MinWidth,
            MinHeight = configuration.MinHeight,
            ShowInTaskbar = configuration.ShowInTaskbar,
            SystemDecorations = configuration.SystemDecorations,
            Background = new SolidColorBrush(configuration.BackgroundColor),
            Content = overlay,
            Topmost = configuration.Topmost,
            Title = configuration.Title,
            RequestedThemeVariant = configuration.ThemeVariant,
            WindowStartupLocation = configuration.WindowStartupLocation
        };
    }

    internal static OverlayWindowConfiguration GetOverlayWindowConfiguration(AppAutomationRecorderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var theme = RecorderOverlay.ResolveOverlayTheme(options.OverlayTheme);
        var palette = RecorderOverlay.GetPalette(theme);

        return
        new(
            CanResize: true,
            SizeToContent: SizeToContent.Manual,
            Width: 1080,
            Height: 760,
            MinWidth: 760,
            MinHeight: 420,
            ShowInTaskbar: true,
            SystemDecorations: SystemDecorations.Full,
            BackgroundColor: palette.OverlayBackground,
            Topmost: false,
            Title: "AppAutomation Recorder",
            ThemeVariant: theme == RecorderOverlayTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light,
            WindowStartupLocation: WindowStartupLocation.CenterScreen);
    }

    internal readonly record struct OverlayWindowConfiguration(
        bool CanResize,
        SizeToContent SizeToContent,
        double Width,
        double Height,
        double MinWidth,
        double MinHeight,
        bool ShowInTaskbar,
        SystemDecorations SystemDecorations,
        Color BackgroundColor,
        bool Topmost,
        string Title,
        ThemeVariant ThemeVariant,
        WindowStartupLocation WindowStartupLocation);
}
