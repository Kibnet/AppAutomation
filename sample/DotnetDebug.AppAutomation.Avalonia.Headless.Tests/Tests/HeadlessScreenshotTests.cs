using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TestHost.Avalonia;
using System.Security.Cryptography;
using SkiaSharp;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

public sealed class HeadlessScreenshotTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task CaptureScreenshot_SavesCurrentRenderedFrame_WithoutOverwriting()
    {
        Border? content = null;
        using var session = DesktopAppSession.Launch(AvaloniaHeadlessLaunchHost.Create(
            () =>
            {
                content = new Border { Background = Brushes.Red };
                return new Window { Width = 120, Height = 90, Content = content };
            }));
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts", "headless-screenshots", "проверка кадра", Guid.NewGuid().ToString("N"));
        HeadlessRuntime.Dispatch(session.MainWindow.Show);
        try
        {
            var first = session.CaptureScreenshot(Path.Combine(directory, "state-a.png"));
            HeadlessRuntime.Dispatch(() => content!.Background = Brushes.Blue);
            var second = HeadlessRuntime.Dispatch(() => session.CaptureScreenshot(Path.Combine(directory, "state-b.png")));

            using var firstImage = new Bitmap(first);
            using var secondImage = new Bitmap(second);
            using var firstPixels = SKBitmap.Decode(first);
            using var secondPixels = SKBitmap.Decode(second);
            var red = firstPixels.GetPixel(60, 45);
            var blue = secondPixels.GetPixel(60, 45);
            using (Assert.Multiple())
            {
                await Assert.That(firstImage.PixelSize.Width).IsEqualTo(120);
                await Assert.That(firstImage.PixelSize.Height).IsEqualTo(90);
                await Assert.That(secondImage.PixelSize.Width).IsEqualTo(120);
                await Assert.That(File.ReadAllBytes(first).AsSpan(0, 8).ToArray()).IsEquivalentTo(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                await Assert.That(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(first))))
                    .IsNotEqualTo(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(second))));
                await Assert.That(red.Red > 240 && red.Green < 15 && red.Blue < 15).IsTrue();
                await Assert.That(blue.Blue > 240 && blue.Red < 15 && blue.Green < 15).IsTrue();
                await Assert.That(() => session.CaptureScreenshot(first)).Throws<IOException>();
                await Assert.That(() => session.CaptureScreenshot(Path.Combine(first, "child.png")))
                    .Throws<IOException>();
            }

            Console.WriteLine($"Headless screenshot A: {first}");
            Console.WriteLine($"Headless screenshot B: {second}");
        }
        finally
        {
            HeadlessRuntime.Dispatch(session.MainWindow.Close);
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task CaptureScreenshot_RequiresShownWindowAndLiveSession()
    {
        var session = DesktopAppSession.Launch(AvaloniaHeadlessLaunchHost.Create(
            () => new Window { Width = 80, Height = 60, Content = new Border { Background = Brushes.Green } }));
        var path = Path.Combine(AppContext.BaseDirectory, "artifacts", "headless-screenshots", Guid.NewGuid().ToString("N"), "hidden.png");
        await Assert.That(HeadlessRuntime.Dispatch(() => session.MainWindow.GetLastRenderedFrame() is null)).IsTrue();
        var resolver = new HeadlessControlResolver(session.MainWindow);
        await Assert.That(resolver.Capabilities.SupportsScreenshots).IsFalse();
        await Assert.That(() => session.CaptureScreenshot(path)).Throws<InvalidOperationException>();
        await Assert.That(File.Exists(path)).IsFalse();
        HeadlessRuntime.Dispatch(session.MainWindow.Show);
        await Assert.That(resolver.Capabilities.SupportsScreenshots).IsTrue();
        HeadlessRuntime.Dispatch(session.MainWindow.Close);
        await Assert.That(() => session.CaptureScreenshot(path)).Throws<InvalidOperationException>();
        session.Dispose();
        await Assert.That(() => session.CaptureScreenshot(path)).Throws<ObjectDisposedException>();
    }
}
