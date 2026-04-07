using AppAutomation.Recorder.Avalonia.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

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
        var overlay = new RecorderOverlay();
        overlay.Attach(session, options);

        var overlayWindow = new Window
        {
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Background = Brushes.Transparent,
            Content = overlay,
            Topmost = true,
            Title = "AppAutomation Recorder"
        };

        async Task ExportAsync()
        {
            if (!options.Overlay.EnableExportButton)
            {
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
        overlay.MinimizeRequested += (_, _) => RepositionOverlay();
        overlay.RestoreRequested += (_, _) => RepositionOverlay();

        if (session is RecorderSession recorderSession)
        {
            recorderSession.ExportRequested += async (_, _) => await ExportAsync();
            recorderSession.OverlayToggleRequested += (_, _) =>
            {
                overlay.ToggleMinimized();
                RepositionOverlay();
            };
        }

        void RepositionOverlay()
        {
            if (owner.WindowState == WindowState.Minimized)
            {
                overlayWindow.IsVisible = false;
                return;
            }

            overlayWindow.IsVisible = true;
            var x = owner.Position.X + Math.Max(0, (int)((owner.Width - overlayWindow.Width) / 2));
            var y = owner.Position.Y;
            overlayWindow.Position = new PixelPoint(x, y);
        }

        owner.PositionChanged += (_, _) => RepositionOverlay();
        owner.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.Property.Name, nameof(Window.WindowState), StringComparison.Ordinal))
            {
                RepositionOverlay();
            }
        };
        owner.Opened += (_, _) => RepositionOverlay();
        owner.Activated += (_, _) => RepositionOverlay();

        overlayWindow.Show();
        RepositionOverlay();
        OverlayWindows[owner] = overlayWindow;
    }
}
