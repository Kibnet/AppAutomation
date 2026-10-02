using System.Diagnostics;
using AppAutomation.FlaUI.Input;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

public sealed class DesktopInputCoordinatorProcessTests
{
    private const string ChildDirectoryVariable = "APPAUTOMATION_POINTER_MUTEX_CHILD";

    [Test]
    public async Task AnotherProcessCannotAcquireUntilOwnerReleases()
    {
        var directory = Path.Combine(Path.GetTempPath(), "appautomation-pointer-mutex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var lease = new DesktopInputCoordinator().Acquire(directory, timeout.Token);
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test executable path is unavailable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(DesktopInputCoordinatorProcessTests).Assembly.Location);
        start.ArgumentList.Add("--treenode-filter");
        start.ArgumentList.Add("/*/*/DesktopInputCoordinatorProcessTests/ChildLeaseEntry");
        start.ArgumentList.Add("--results-directory");
        start.ArgumentList.Add(directory);
        start.Environment[ChildDirectoryVariable] = directory;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The mutex probe child did not start.");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        try
        {
            await WaitForFileAsync(Path.Combine(directory, "ready"), child, timeout.Token);
            await Task.Delay(150, timeout.Token);
            await Assert.That(File.Exists(Path.Combine(directory, "acquired"))).IsFalse();

            lease.Dispose();
            await child.WaitForExitAsync(timeout.Token);
            File.WriteAllText(Path.Combine(directory, "child-output.log"), await output + await error);
            await Assert.That(child.ExitCode).IsEqualTo(0);
            await Assert.That(File.Exists(Path.Combine(directory, "acquired"))).IsTrue();

            using var reacquired = new DesktopInputCoordinator().Acquire(directory, timeout.Token);
            // The child wrote a button timestamp immediately before exiting. The monotonic timeline
            // must be visible in the parent under the same desktop mutex, without physical input.
            var cooldown = ((IDesktopClickTimeline)reacquired).RemainingCooldown(TimeSpan.FromSeconds(30));
            await Assert.That(cooldown).IsGreaterThan(TimeSpan.Zero);
            await Assert.That(cooldown).IsLessThanOrEqualTo(TimeSpan.FromSeconds(30));
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    [Test]
    public void ChildLeaseEntry()
    {
        var directory = Environment.GetEnvironmentVariable(ChildDirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            Skip.Test("Entry point used only by the cross-process mutex probe.");
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        File.WriteAllText(Path.Combine(directory, "ready"), Environment.ProcessId.ToString());
        using var lease = new DesktopInputCoordinator().Acquire(directory, timeout.Token);
        File.WriteAllText(Path.Combine(directory, "acquired"), Environment.ProcessId.ToString());
        ((IDesktopClickTimeline)lease).RecordButtonEvent(Environment.TickCount64);
    }

    private static async Task WaitForFileAsync(string path, Process child, CancellationToken token)
    {
        while (!File.Exists(path))
        {
            if (child.HasExited)
                throw new InvalidOperationException($"The mutex probe exited with code {child.ExitCode} before its ready signal.");
            await Task.Delay(25, token);
        }
    }
}
