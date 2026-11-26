using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.Recorder.Avalonia;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.TestHost;
using DotnetDebug.Avalonia;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

[NotInParallel("DesktopUi")]
public sealed class NamedItemSurfaceHeadlessTests
{
    [Test]
    public async Task RecordsOneDoubleClickEvenWhenTheHandlerDetachesTheSurface()
    {
        using var session = Launch();
        // Authoring discovery intentionally ignores bin/ and obj/ even under a nested target.
        var artifacts = Directory.CreateTempSubdirectory("appautomation-named-item-");
        await File.WriteAllTextAsync(Path.Combine(artifacts.FullName, "CaptureTargets.cs"), """
            using AppAutomation.Abstractions;
            namespace NamedItemCapture;
            public partial class CapturePage(IUiControlResolver resolver) : UiPage(resolver) { }
            public partial class CaptureScenarios
            {
                private CapturePage Page => throw new System.NotImplementedException();
            }
            """);

        var capture = HeadlessRuntime.Session.Dispatch(async () =>
        {
            var window = session.MainWindow;
            var surface = FindSurface(window);
            using var recorder = (RecorderSession)AppAutomationRecorder.Attach(window, new()
            {
                ShowOverlay = false,
                AuthoringProjectDirectory = artifacts.FullName,
                PageNamespace = "NamedItemCapture",
                PageClassName = "CapturePage",
                ScenarioNamespace = "NamedItemCapture",
                ScenarioClassName = "CaptureScenarios"
            });
            recorder.Start();
            var laser = ControlAutomationPeer.CreatePeerForElement(surface).GetChildren()
                .Single(peer => peer.GetName() == "Laser 02");
            var point = laser.GetBoundingRectangle().Center;

            // Drawn workload at the right of the same line is not a named hit area.
            Click(point.WithX(point.X + 160));
            Click(point.WithX(point.X + 160));
            var outside = recorder.StepJournal.Count;
            Click(point);
            var single = recorder.StepJournal.Count;
            Click(point);
            recorder.Stop();
            var step = recorder.StepJournal.Single();
            await UiWait.UntilAsync(() => recorder.IsBusy, static busy => !busy);
            var draft = Directory.EnumerateFiles(artifacts.FullName, "CaptureScenarios*.g.cs.autosave",
                SearchOption.AllDirectories).SingleOrDefault()
                ?? throw new InvalidOperationException($"Recorder did not write the named-item draft: {recorder.LatestStatus}");
            if (!RecorderAutosaveStateSerializer.TryRead(draft, out var restored, out var error))
                throw new InvalidOperationException(error ?? "Cannot read the recorded named-item draft.");
            var save = await recorder.SaveAsync();
            return (outside, single, step, restored: restored!.Steps.Single(), save);

            void Click(Point p)
            {
                window.MouseMove(p);
                window.MouseDown(p, MouseButton.Left);
                window.MouseUp(p, MouseButton.Left);
            }
        }, CancellationToken.None).GetAwaiter().GetResult();

        const string command = "Page.DoubleClickNamedItem(static page => page.EquipmentSurface, \"Laser 02\");";
        await Assert.That(capture.outside).IsEqualTo(0);
        await Assert.That(capture.single).IsEqualTo(0);
        await Assert.That(capture.step.CanPersist).IsTrue();
        await Assert.That(capture.step.Preview.Trim()).IsEqualTo(command);
        await Assert.That(capture.restored.ActionKind).IsEqualTo(RecordedActionKind.DoubleClickNamedItem);
        await Assert.That(capture.restored.StringValue).IsEqualTo("Laser 02");
        await Assert.That(capture.save.Success).IsTrue().Because(capture.save.Message);
        await Assert.That(await File.ReadAllTextAsync(capture.save.ScenarioFilePath!)).Contains(command);
        await Assert.That(await File.ReadAllTextAsync(capture.save.PageFilePath!)).Contains("UiControlType.NamedItemSurface");
        await Assert.That(CreatePage(session).OpenedEquipment.Text).IsEqualTo("line-laser:1");
    }

    [Test]
    public async Task ReplaysTheNamedItemAfterReorderingAndReveal()
    {
        using var session = Launch();
        await CreatePage(session).VerifyReplayAfterReorder();
    }

    [Test]
    public async Task RefusesIncompleteAndAmbiguousItemSetsBeforeInput()
    {
        using var session = Launch();
        await CreatePage(session).VerifyInvalidTargetsDoNotOpenAnItem();
    }

    [Test]
    public async Task RefusesTheSecondPressWhenTheItemSetChangesDuringTheFirst()
    {
        using var session = Launch();
        var page = CreatePage(session);
        var presses = 0;
        HeadlessRuntime.Dispatch(() =>
        {
            var surface = FindSurface(session.MainWindow);
            surface.PointerPressed += (_, _) =>
            {
                presses++;
                AutomationProperties.SetItemStatus(surface, NamedItemSurfaceMetadata.Format(4, 100));
            };
        });

        await Assert.That(() => page.DoubleClickNamedItem(static p => p.EquipmentSurface, "Laser 02"))
            .Throws<InvalidOperationException>();
        await Assert.That(HeadlessRuntime.Dispatch(() => presses)).IsEqualTo(1);
        await Assert.That(page.OpenedEquipment.Text).IsEqualTo("None");
    }

    private static Control FindSurface(Window window) => window.GetVisualDescendants().OfType<Control>()
        .Single(control => AutomationProperties.GetAutomationId(control) == "EquipmentSurface");

    private static NamedItemSurfacePage CreatePage(DesktopAppSession session) =>
        new(new HeadlessControlResolver(session.MainWindow));

    private static DesktopAppSession Launch()
    {
        const string variable = NamedItemSurfaceFixture.EnvironmentVariable;
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "1");
        try
        {
            var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
            try
            {
                HeadlessRuntime.Dispatch(() =>
                {
                    session.MainWindow.Show();
                    session.MainWindow.Dispatcher.RunJobs();
                    session.MainWindow.UpdateLayout();
                });
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); }
    }
}
