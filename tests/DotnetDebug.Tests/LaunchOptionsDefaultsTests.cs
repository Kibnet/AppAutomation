using EasyUse.Session.Contracts;
using TUnit.Assertions;
using TUnit.Core;

public class LaunchOptionsDefaultsTests
{
    [Test]
    public async Task DesktopProjectLaunchOptions_DefaultsRemainStable()
    {
        var options = new DesktopProjectLaunchOptions
        {
            ProjectRelativePath = "src/DotnetDebug.Avalonia/DotnetDebug.Avalonia.csproj",
            TargetFramework = "net9.0"
        };

        using (Assert.Multiple())
        {
            await Assert.That(options.BuildConfiguration).IsEqualTo("Debug");
            await Assert.That(options.SolutionFileName).IsEqualTo("DotnetDebug.sln");
            await Assert.That(options.ExecutableName).IsNull();
            await Assert.That(options.BuildBeforeLaunch).IsEqualTo(true);
            await Assert.That(options.BuildOncePerProcess).IsEqualTo(true);
            await Assert.That(options.BuildTimeout).IsEqualTo(TimeSpan.FromMinutes(2));
            await Assert.That(options.MainWindowTimeout).IsEqualTo(TimeSpan.FromSeconds(20));
            await Assert.That(options.PollInterval).IsEqualTo(TimeSpan.FromMilliseconds(200));
        }
    }

    [Test]
    public async Task DesktopAppLaunchOptions_DefaultsRemainStable()
    {
        var options = new DesktopAppLaunchOptions
        {
            ExecutablePath = "DotnetDebug.Avalonia.exe"
        };

        using (Assert.Multiple())
        {
            await Assert.That(options.WorkingDirectory).IsNull();
            await Assert.That(options.MainWindowTimeout).IsEqualTo(TimeSpan.FromSeconds(20));
            await Assert.That(options.PollInterval).IsEqualTo(TimeSpan.FromMilliseconds(200));
        }
    }
}
