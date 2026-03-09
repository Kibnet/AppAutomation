using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.Session.Contracts;
using DotnetDebug.Avalonia;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

public sealed class HeadlessLaunchOptionsTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task Launch_SupportsAsyncBootstrap_And_RepeatedSessions()
    {
        var beforeLaunchCalls = 0;
        var createWindowCalls = 0;
        object? firstWindow = null;
        object? secondWindow = null;

        for (var index = 0; index < 2; index++)
        {
            using var session = DesktopAppSession.Launch(new HeadlessAppLaunchOptions
            {
                BeforeLaunchAsync = cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    beforeLaunchCalls++;
                    return ValueTask.CompletedTask;
                },
                CreateMainWindowAsync = cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    createWindowCalls++;
                    return ValueTask.FromResult<object>(new MainWindow());
                }
            });

            var resolver = new HeadlessControlResolver(session.MainWindow);
            var tabs = resolver.Resolve<IUiControl>(new UiControlDefinition("MainTabs", UiControlType.Tab, "MainTabs"));
            await Assert.That(tabs.AutomationId).IsEqualTo("MainTabs");

            if (firstWindow is null)
            {
                firstWindow = session.MainWindow;
            }
            else
            {
                secondWindow = session.MainWindow;
            }
        }

        using (Assert.Multiple())
        {
            await Assert.That(beforeLaunchCalls).IsEqualTo(2);
            await Assert.That(createWindowCalls).IsEqualTo(2);
            await Assert.That(ReferenceEquals(firstWindow, secondWindow)).IsEqualTo(false);
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task Launch_Throws_WhenNoHeadlessFactoryConfigured()
    {
        await Assert.That(() => DesktopAppSession.Launch(new HeadlessAppLaunchOptions()))
            .Throws<InvalidOperationException>();
    }
}
