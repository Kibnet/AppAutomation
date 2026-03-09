using System.Diagnostics;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

public sealed class DesktopAppLaunchProcessStartInfoTests
{
    [Test]
    public async Task CreateProcessStartInfo_MapsArgumentsAndEnvironmentVariables()
    {
        const string environmentKey = "APP_AUTOMATION_TEST_FLAG";
        var options = new DesktopAppLaunchOptions
        {
            ExecutablePath = @"C:\Apps\Demo.exe",
            WorkingDirectory = @"C:\Apps",
            Arguments = ["--seed", "42"],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                [environmentKey] = "enabled"
            }
        };

        var startInfo = DesktopAppSession.CreateProcessStartInfo(options, options.ExecutablePath, options.WorkingDirectory);

        using (Assert.Multiple())
        {
            await Assert.That(startInfo.FileName).IsEqualTo(options.ExecutablePath);
            await Assert.That(startInfo.WorkingDirectory).IsEqualTo(options.WorkingDirectory);
            await Assert.That(startInfo.UseShellExecute).IsEqualTo(false);
            await Assert.That(startInfo.ArgumentList.Count).IsEqualTo(2);
            await Assert.That(startInfo.ArgumentList[0]).IsEqualTo("--seed");
            await Assert.That(startInfo.ArgumentList[1]).IsEqualTo("42");
            await Assert.That(startInfo.Environment[environmentKey]).IsEqualTo("enabled");
        }
    }

    [Test]
    public async Task CreateProcessStartInfo_RejectsEmptyEnvironmentVariableNames()
    {
        var options = new DesktopAppLaunchOptions
        {
            ExecutablePath = @"C:\Apps\Demo.exe",
            EnvironmentVariables = new Dictionary<string, string?>
            {
                [string.Empty] = "invalid"
            }
        };

        await Assert.That(() => DesktopAppSession.CreateProcessStartInfo(options, options.ExecutablePath))
            .Throws<ArgumentException>();
    }
}
