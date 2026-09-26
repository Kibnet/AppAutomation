using Avalonia.Controls;
using Avalonia.Headless;

namespace AppAutomation.Avalonia.Headless.Session;

internal static class HeadlessScreenshotCapture
{
    internal static bool IsRenderingBackendAvailable(Window window)
    {
        return window.Dispatcher.CheckAccess()
            ? IsRenderingBackendAvailableOnUiThread(window)
            : HeadlessRuntime.Dispatch(() => IsRenderingBackendAvailableOnUiThread(window));
    }

    internal static bool IsWindowVisible(Window window) => window.Dispatcher.CheckAccess()
        ? window.IsVisible
        : HeadlessRuntime.Dispatch(() => window.IsVisible);

    internal static CapturedScreenshot Capture(Window window, string filePath)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (window.Dispatcher.CheckAccess())
        {
            return CaptureOnUiThread(window, filePath);
        }

        return HeadlessRuntime.Dispatch(() => CaptureOnUiThread(window, filePath));
    }

    private static CapturedScreenshot CaptureOnUiThread(Window window, string filePath)
    {
        if (!window.IsVisible)
        {
            throw new InvalidOperationException("The headless window must be shown before capturing a screenshot.");
        }

        using var frame = CaptureFrame(window);

        var path = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var created = false;
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                frame.Save(stream);
                stream.Flush(flushToDisk: true);
            }

            return new CapturedScreenshot(path, frame.PixelSize.Width, frame.PixelSize.Height);
        }
        catch
        {
            if (created)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Preserve the original capture failure.
                }
                catch (UnauthorizedAccessException)
                {
                    // Preserve the original capture failure.
                }
            }

            throw;
        }
    }

    private static bool IsRenderingBackendAvailableOnUiThread(Window window)
    {
        try
        {
            // The non-rendering headless backend rejects this read even before Show().
            // A null frame with Skia means rendering is available but has not run yet.
            using var frame = window.GetLastRenderedFrame();
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static global::Avalonia.Media.Imaging.WriteableBitmap CaptureFrame(Window window)
    {
        try
        {
            return window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("The headless window has no rendered frame to capture.");
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidOperationException(
                "Headless drawing has no pixels. Configure the test AppBuilder with .UseSkia() "
                + "and .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).",
                ex);
        }
    }
}

internal sealed record CapturedScreenshot(string Path, int Width, int Height);
