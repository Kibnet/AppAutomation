using AppAutomation.TestHost.Avalonia;
using DotnetDebug.Avalonia;
using System.Reflection;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.Tests;

public sealed class AvaloniaTestHostHelpersTests
{
    [Test]
    public async Task AvaloniaHeadlessLaunchHost_CreateSyncFactory_MapsToHeadlessOptions()
    {
        var options = AvaloniaHeadlessLaunchHost.Create(static () => new MainWindow());

        using (Assert.Multiple())
        {
            await Assert.That(options.CreateMainWindow is not null).IsEqualTo(true);
            await Assert.That(options.CreateMainWindowAsync is null).IsEqualTo(true);
            await Assert.That(options.BeforeLaunchAsync is null).IsEqualTo(true);
        }
    }

    [Test]
    public async Task TemporaryDirectory_WritesFiles_And_CleansUpByDefault()
    {
        string fullPath;
        string filePath;

        using (var tempDirectory = TemporaryDirectory.Create("AppAutomationTests"))
        {
            fullPath = tempDirectory.FullPath;
            filePath = tempDirectory.WriteTextFile("settings\\Settings.json", "{ }");

            using (Assert.Multiple())
            {
                await Assert.That(Directory.Exists(fullPath)).IsEqualTo(true);
                await Assert.That(File.Exists(filePath)).IsEqualTo(true);
            }
        }

        await Assert.That(Directory.Exists(fullPath)).IsEqualTo(false);
    }

    [Test]
    public async Task BuildConfigurationDefaults_ForAssembly_UsesAssemblyConfiguration()
    {
        var expected = typeof(AvaloniaTestHostHelpersTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?
            .Configuration;
        var configuration = BuildConfigurationDefaults.ForAssembly(typeof(AvaloniaTestHostHelpersTests).Assembly);

        await Assert.That(configuration).IsEqualTo(expected);
    }

    [Test]
    public async Task RecorderDiagnostics_DefaultsToEnabledUnlessExplicitlyDisabled()
    {
        using (Assert.Multiple())
        {
            await Assert.That(App.ShouldWriteRecorderDiagnosticsToFile(null)).IsEqualTo(true);
            await Assert.That(App.ShouldWriteRecorderDiagnosticsToFile(string.Empty)).IsEqualTo(true);
            await Assert.That(App.ShouldWriteRecorderDiagnosticsToFile("1")).IsEqualTo(true);
            await Assert.That(App.ShouldWriteRecorderDiagnosticsToFile("0")).IsEqualTo(false);
            await Assert.That(App.ShouldWriteRecorderDiagnosticsToFile("false")).IsEqualTo(false);
        }
    }
}
