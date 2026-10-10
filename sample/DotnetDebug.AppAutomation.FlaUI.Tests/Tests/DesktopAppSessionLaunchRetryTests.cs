using System.Runtime.InteropServices;
using AppAutomation.FlaUI.Session;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

public sealed class DesktopAppSessionLaunchRetryTests
{
    [Test]
    public async Task WaitForMainWindow_RetriesTransientUiaTimeout()
    {
        var expected = new object();
        var attempts = 0;
        var transient = new TimeoutException(
            "UIA Timeout",
            new COMException("Operation timed out.", unchecked((int)0x80131505)));

        var actual = DesktopAppSession.WaitForMainWindow(
            () => ++attempts == 1 ? throw transient : expected,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1));

        using (Assert.Multiple())
        {
            await Assert.That(actual).IsSameReferenceAs(expected);
            await Assert.That(attempts).IsEqualTo(2);
        }
    }

    [Test]
    public async Task WaitForMainWindow_ReportsLastUiaTimeoutAfterDeadline()
    {
        var transient = new TimeoutException("UIA Timeout");
        var failure = Assert.Throws<TimeoutException>(() =>
            DesktopAppSession.WaitForMainWindow<object>(
                () => throw transient,
                TimeSpan.FromMilliseconds(30),
                TimeSpan.FromMilliseconds(1)));

        await Assert.That(failure.InnerException).IsSameReferenceAs(transient);
    }

    [Test]
    public async Task WaitForMainWindow_RetriesDirectComTimeout()
    {
        var expected = new object();
        var attempts = 0;
        var transient = new COMException("Operation timed out.", unchecked((int)0x80131505));

        var actual = DesktopAppSession.WaitForMainWindow(
            () => ++attempts == 1 ? throw transient : expected,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1));

        using (Assert.Multiple())
        {
            await Assert.That(actual).IsSameReferenceAs(expected);
            await Assert.That(attempts).IsEqualTo(2);
        }
    }

    [Test]
    public async Task WaitForMainWindow_DoesNotRetryOtherComFailures()
    {
        var attempts = 0;
        var failure = new COMException("Unrelated UI Automation failure.", unchecked((int)0x80004005));

        var observed = Assert.Throws<COMException>(() =>
            DesktopAppSession.WaitForMainWindow<object>(
                () =>
                {
                    attempts++;
                    throw failure;
                },
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(1)));

        using (Assert.Multiple())
        {
            await Assert.That(observed).IsSameReferenceAs(failure);
            await Assert.That(attempts).IsEqualTo(1);
        }
    }
}
