using System.Diagnostics;
using System.Reflection;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.UI;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderStepDragDropTests
{
    [Test]
    [Arguments(0, 3, "BCDA")]
    [Arguments(3, 0, "DABC")]
    [Arguments(1, 2, "ACBD")]
    [Arguments(2, 1, "ACBD")]
    public async Task MoveToFinalIndexAndUndoPreserveIdentity(int source, int target, string expected)
    {
        using var session = CreateSession();
        var steps = AddSteps(session, 4);
        var moved = session.MoveStepToIndex(steps[source].StepId, target);
        var movedOrder = Order(session);
        var undone = session.UndoStepMove();

        using (Assert.Multiple())
        {
            await Assert.That(moved).IsTrue();
            await Assert.That(movedOrder).IsEqualTo(expected);
            await Assert.That(undone).IsTrue();
            await Assert.That(Order(session)).IsEqualTo("ABCD");
            await Assert.That(session.StepJournal.Select(entry => entry.StepId)).IsEquivalentTo(
                steps.Select(step => step.StepId));
            await Assert.That(session.CanUndoStepMove).IsFalse();
        }
    }

    [Test]
    public async Task UndoKeepsAppendedStepsEditedPayloadAndIgnoredState()
    {
        using var session = CreateSession();
        var steps = AddSteps(session, 3);
        session.MoveStepToIndex(steps[0].StepId, 2);
        var fourth = Step("D");
        session.AddRecordedStepForTesting(fourth);
        session.MoveStepToIndex(fourth.StepId, 0);
        session.TryCreateStepEditDraft(steps[0].StepId, out var draft, out _);
        var edited = session.ApplyStepEdit(draft! with { StringValue = "edited after drag" });
        session.SetStepIgnored(steps[1].StepId, true);
        session.RetryStepValidation(steps[2].StepId);
        var firstUndo = session.UndoStepMove();
        var firstOrder = Order(session);
        var secondUndo = session.UndoStepMove();

        using (Assert.Multiple())
        {
            await Assert.That(edited.Success).IsTrue();
            await Assert.That(firstUndo).IsTrue();
            await Assert.That(firstOrder).IsEqualTo("BCAD");
            await Assert.That(secondUndo).IsTrue();
            await Assert.That(Order(session)).IsEqualTo("ABCD");
            await Assert.That(session.StepJournal[0].Preview).Contains("edited after drag");
            await Assert.That(session.StepJournal[1].IsIgnored).IsTrue();
            await Assert.That(session.StepJournal[3].StepId).IsEqualTo(fourth.StepId);
        }
    }

    [Test]
    [Arguments("arrow")]
    [Arguments("remove")]
    [Arguments("clear")]
    public async Task StructuralActionClearsDragHistory(string action)
    {
        using var session = CreateSession();
        var steps = AddSteps(session, 3);
        session.MoveStepToIndex(steps[0].StepId, 2);
        switch (action)
        {
            case "arrow": session.MoveStep(steps[0].StepId, RecorderStepMoveDirection.Earlier); break;
            case "remove": session.RemoveStep(steps[1].StepId); break;
            case "clear": session.Clear(); break;
        }

        var order = Order(session);
        await Assert.That(session.UndoStepMove()).IsFalse();
        await Assert.That(Order(session)).IsEqualTo(order);
    }

    [Test]
    public async Task NoOpAndInvalidMoveLeaveHistoryAndNotificationsUntouched()
    {
        using var session = CreateSession();
        var steps = AddSteps(session, 3);
        session.MoveStepToIndex(steps[0].StepId, 2);
        var changes = 0;
        session.SessionChanged += (_, _) => changes++;

        using (Assert.Multiple())
        {
            await Assert.That(session.MoveStepToIndex(steps[0].StepId, 2)).IsFalse();
            await Assert.That(session.MoveStepToIndex(steps[0].StepId, -1)).IsFalse();
            await Assert.That(session.MoveStepToIndex(steps[0].StepId, 3)).IsFalse();
            await Assert.That(session.MoveStepToIndex(Guid.NewGuid(), 0)).IsFalse();
            await Assert.That(session.MoveStep(steps[1].StepId, RecorderStepMoveDirection.Earlier)).IsFalse();
            await Assert.That(changes).IsEqualTo(0);
            await Assert.That(session.UndoStepMove()).IsTrue();
            await Assert.That(Order(session)).IsEqualTo("ABC");
        }
    }

    [Test]
    public async Task HistoryIsBoundedToLastHundredDrags()
    {
        using var session = CreateSession();
        var steps = AddSteps(session, 2);
        for (var index = 0; index < 101; index++)
        {
            session.MoveStepToIndex(steps[0].StepId, index % 2 == 0 ? 1 : 0);
        }

        var undoCount = 0;
        while (session.UndoStepMove())
        {
            undoCount++;
        }

        await Assert.That(undoCount).IsEqualTo(100);
        await Assert.That(Order(session)).IsEqualTo("BA");
    }

    [Test]
    public async Task DisposedSessionDropsUndoAndRejectsFurtherMoves()
    {
        using var session = CreateSession();
        var steps = AddSteps(session, 2);
        session.MoveStepToIndex(steps[0].StepId, 1);
        session.Dispose();
        await Assert.That(session.UndoStepMove()).IsFalse();
        await Assert.That(session.MoveStepToIndex(steps[0].StepId, 0)).IsFalse();
        await Assert.That(session.MoveStep(steps[0].StepId, RecorderStepMoveDirection.Earlier)).IsFalse();
        await Assert.That(Order(session)).IsEqualTo("BA");
    }

    [Test]
    public async Task BusyRejectsMoveAndUndoWithoutDroppingHistory()
    {
        var release = new TaskCompletionSource<RecorderSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = CreateSession(save: (_, _, _) => release.Task);
        var steps = AddSteps(session, 3);
        session.MoveStepToIndex(steps[0].StepId, 2);
        var saving = session.SaveAsync();
        var rejectedMove = session.MoveStepToIndex(steps[0].StepId, 0);
        var rejectedUndo = session.UndoStepMove();
        var busyOrder = Order(session);
        release.SetResult(Saved(3));
        await saving;

        using (Assert.Multiple())
        {
            await Assert.That(rejectedMove).IsFalse();
            await Assert.That(rejectedUndo).IsFalse();
            await Assert.That(busyOrder).IsEqualTo("BCA");
            await Assert.That(session.UndoStepMove()).IsTrue();
            await Assert.That(Order(session)).IsEqualTo("ABC");
        }
    }

    [Test]
    public async Task UndoRestoresDependencyGraphAndSaveAutosaveOrder()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage,
            RecorderScenarioDestinationSources.CompilableScenario);
        var savedSteps = Array.Empty<RecordedStep>();
        var autosavedOrders = new List<Guid[]>();
        using var session = CreateSession(
            save: (steps, _, _) =>
            {
                savedSteps = steps.ToArray();
                return Task.FromResult(Saved(steps.Count));
            },
            autosave: (steps, _, _) =>
            {
                autosavedOrders.Add(steps.Select(step => step.StepId).ToArray());
                return Task.FromResult(Saved(steps.Count));
            });
        var checkpointId = Guid.NewGuid();
        var checkpoint = new RecordedStep(RecordedActionKind.CaptureCheckpoint,
            Descriptor("Product"), StepId: Guid.NewGuid(), ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text, CheckpointId: checkpointId,
            CheckpointVariableName: "productBeforeSave");
        var assertion = new RecordedStep(RecordedActionKind.AssertValue,
            Descriptor("Product"), StepId: Guid.NewGuid(), ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text, ExpectedCheckpointId: checkpointId,
            ComparisonKind: RecorderComparisonKind.Equal);
        session.AddRecordedStepForTesting(checkpoint);
        session.AddRecordedStepForTesting(assertion);
        session.Start();
        session.MoveStepToIndex(assertion.StepId, 0);
        var invalid = !session.StepJournal[0].CanPersist;
        await WaitForIdle(session);
        session.UndoStepMove();
        await WaitForIdle(session);
        await session.SaveAsync();
        await session.SaveToDirectoryAsync(project.RootPath);
        var result = await project.SaveAsync(project.CreateSaveContext("Drag undo", "drag-undo"), savedSteps);
        var source = await File.ReadAllTextAsync(result.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(invalid).IsTrue();
            await Assert.That(session.StepJournal.All(entry => entry.CanPersist)).IsTrue();
            await Assert.That(autosavedOrders.Count).IsEqualTo(2);
            await Assert.That(autosavedOrders[0][0]).IsEqualTo(assertion.StepId);
            await Assert.That(autosavedOrders[1][0]).IsEqualTo(checkpoint.StepId);
            await Assert.That(savedSteps[0].StepId).IsEqualTo(checkpoint.StepId);
            await Assert.That(source.IndexOf("var productBeforeSave", StringComparison.Ordinal)).IsLessThan(
                source.IndexOf("IsEqualTo(productBeforeSave)", StringComparison.Ordinal));
            await Assert.That(RecorderGeneratedSourceCompiler.Compile(project.RootPath)).IsEmpty();
        }
    }

    [Test]
    [Arguments(3, 0, false, "DABC")]
    [Arguments(0, 3, true, "BCDA")]
    [Arguments(1, 2, true, "ACBD")]
    public async Task PointerDragUsesCardHalfAndKeyboardUndo(int source, int target, bool after, string expected)
    {
        var result = InUi(f =>
        {
            var sourceId = f.Ids[source];
            var originalCard = f.Row(sourceId);
            var start = f.SourcePoint(sourceId);
            var destination = f.RowPoint(f.Ids[target], after);
            f.Window.MouseDown(start, MouseButton.Left);
            f.Window.MouseMove(destination, RawInputModifiers.LeftMouseButton);
            var beforeDrop = Order(f.Session);
            var indicatorShown = f.Indicator.IsVisible;
            f.Overlay.RefreshForTesting();
            var sourceStillAttached = ReferenceEquals(originalCard, f.Row(sourceId))
                && TopLevel.GetTopLevel(originalCard) == f.Window;
            f.Window.MouseUp(destination, MouseButton.Left);
            f.Layout();
            var afterDrop = Order(f.Session);
            var firstBadge = f.Panel.Children[0].GetVisualDescendants().OfType<TextBlock>()
                .First(text => text.Text?.StartsWith('#') == true).Text;
            f.Press(Key.Z, RawInputModifiers.Control);
            f.Layout();
            return (beforeDrop, indicatorShown, sourceStillAttached, afterDrop, firstBadge,
                afterUndo: Order(f.Session), indicatorHidden: !f.Indicator.IsVisible);
        });

        using (Assert.Multiple())
        {
            await Assert.That(result.beforeDrop).IsEqualTo("ABCD");
            await Assert.That(result.indicatorShown).IsTrue();
            await Assert.That(result.sourceStillAttached).IsTrue();
            await Assert.That(result.afterDrop).IsEqualTo(expected);
            await Assert.That(result.firstBadge).StartsWith("#1 ");
            await Assert.That(result.afterUndo).IsEqualTo("ABCD");
            await Assert.That(result.indicatorHidden).IsTrue();
        }
    }

    [Test]
    [Arguments("escape")]
    [Arguments("outside")]
    [Arguments("capture")]
    [Arguments("change")]
    [Arguments("busy")]
    [Arguments("deactivate")]
    [Arguments("close")]
    [Arguments("ctrlz")]
    public async Task CancelledGestureLeavesOrderAndAllowsNextDrag(string reason)
    {
        var result = InUiAsync(async f =>
        {
            IPointer? pointer = null;
            f.Window.AddHandler(InputElement.PointerMovedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            var destination = f.RowPoint(f.Ids[0], false);
            f.Window.MouseDown(f.SourcePoint(f.Ids[2]), MouseButton.Left);
            f.Window.MouseMove(destination, RawInputModifiers.LeftMouseButton);
            Task<RecorderSaveResult>? saving = null;
            switch (reason)
            {
                case "escape": f.Press(Key.Escape); break;
                case "outside": destination = new Point(0, 0); break;
                case "capture": pointer!.Capture(null); break;
                case "change": f.Session.AddRecordedStepForTesting(Step("E")); f.Overlay.RefreshForTesting(); break;
                case "busy": saving = f.Session.SaveAsync(); break;
                case "deactivate":
                    // Headless Activate does not emit focus changes; deliver the platform notification.
                    typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(f.Window, null);
                    break;
                case "close": f.Window.Close(); break;
                case "ctrlz": f.Press(Key.Z, RawInputModifiers.Control); break;
            }


            if (reason != "close")
            {
                f.Window.MouseUp(destination, MouseButton.Left);
            }

            var order = Order(f.Session);
            var clean = !f.Indicator.IsVisible && pointer?.Captured is null && !f.Session.CanUndoStepMove;
            f.ReleaseSave.TrySetResult(Saved(4));
            if (saving is not null) await saving;
            var canDragAgain = true;
            if (reason != "close")
            {
                f.Window.Activate();
                f.Layout();
                f.Drag(f.Ids[2], f.Ids[0], false);
                canDragAgain = Order(f.Session).StartsWith("CAB", StringComparison.Ordinal);
            }

            return (order, clean, canDragAgain);
        });

        using (Assert.Multiple())
        {
            await Assert.That(result.order).IsEqualTo(reason == "change" ? "ABCDE" : "ABCD");
            await Assert.That(result.clean).IsTrue();
            await Assert.That(result.canDragAgain).IsTrue();
        }
    }

    [Test]
    public async Task ReturningIntoViewportAndClickingButtonsDoNotCreateSpuriousMoves()
    {
        var result = InUi(f =>
        {
            var start = f.SourcePoint(f.Ids[1]);
            f.Window.MouseDown(start, MouseButton.Left);
            f.Window.MouseUp(start, MouseButton.Left);
            f.Window.MouseDown(start, MouseButton.Right);
            f.Window.MouseMove(f.RowPoint(f.Ids[0], false), RawInputModifiers.RightMouseButton);
            f.Window.MouseUp(start, MouseButton.Right);
            var copy = f.Row(f.Ids[0]).GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content?.ToString() == "Copy");
            var copyPoint = copy.TranslatePoint(new Point(copy.Bounds.Width / 2, copy.Bounds.Height / 2), f.Window)!.Value;
            f.Window.MouseDown(copyPoint, MouseButton.Left);
            f.Window.MouseMove(f.RowPoint(f.Ids[2], false), RawInputModifiers.LeftMouseButton);
            f.Window.MouseUp(f.RowPoint(f.Ids[2], false), MouseButton.Left);
            var unchanged = Order(f.Session) == "ABCD" && !f.Session.CanUndoStepMove;
            f.Window.MouseDown(f.SourcePoint(f.Ids[2]), MouseButton.Left);
            f.Window.MouseMove(new Point(0, 0), RawInputModifiers.LeftMouseButton);
            var hiddenOutside = !f.Indicator.IsVisible;
            var destination = f.RowPoint(f.Ids[0], false);
            f.Window.MouseMove(destination, RawInputModifiers.LeftMouseButton);
            f.Window.MouseUp(destination, MouseButton.Left);
            f.Layout();
            return (unchanged, hiddenOutside, order: Order(f.Session));
        });

        await Assert.That(result.unchanged).IsTrue();
        await Assert.That(result.hiddenOutside).IsTrue();
        await Assert.That(result.order).IsEqualTo("CABD");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BusyDuringDragCancelsBeforeMouseRelease(bool movePointer)
    {
        var result = InUiAsync(async f =>
        {
            IPointer? pointer = null;
            f.Window.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            f.Window.MouseDown(f.SourcePoint(f.Ids[2]), MouseButton.Left);
            if (movePointer)
            {
                f.Window.MouseMove(f.RowPoint(f.Ids[0], false), RawInputModifiers.LeftMouseButton);
            }
            var saving = f.Session.SaveAsync();
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (pointer?.Captured is not null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            var cancelled = !f.Indicator.IsVisible && pointer?.Captured is null;
            var order = Order(f.Session);
            f.ReleaseSave.SetResult(Saved(4));
            await saving;
            return (cancelled, order);
        });

        await Assert.That(result.cancelled).IsTrue();
        await Assert.That(result.order).IsEqualTo("ABCD");
    }

    [Test]
    [Arguments("Apply")]
    [Arguments("Cancel")]
    public async Task EditorRequiresApplyOrCancelAndKeepsNativeTextUndo(string action)
    {
        var result = InUi(f =>
        {
            f.Drag(f.Ids[2], f.Ids[0], false);
            f.Row(f.Ids[0]).GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content?.ToString() == "Edit")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            f.Layout();
            var editor = (RecorderStepEditor)f.Overlay.LastStepEditorForTesting!;
            var input = editor.GetVisualDescendants().OfType<TextBox>().First();
            input.Focus();
            input.SelectAll();
            f.Window.KeyTextInput("changed");
            f.Press(Key.Z, RawInputModifiers.Control);
            var nativeUndo = input.Text;
            input.SelectAll();
            f.Window.KeyTextInput("final value");
            f.Drag(f.Ids[2], f.Ids[1], true);
            var editorPreserved = ReferenceEquals(editor, f.Overlay.LastStepEditorForTesting)
                && input.Text == "final value" && Order(f.Session) == "CABD";
            editor.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == action)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            f.Layout();
            var preview = f.Session.StepJournal.Single(entry => entry.StepId == f.Ids[0]).Preview;
            f.Drag(f.Ids[2], f.Ids[1], true);
            return (nativeUndo, editorPreserved, preview, order: Order(f.Session));
        });

        using (Assert.Multiple())
        {
            await Assert.That(result.nativeUndo).IsEqualTo("value A");
            await Assert.That(result.editorPreserved).IsTrue();
            await Assert.That(result.preview).Contains(action == "Apply" ? "final value" : "value A");
            await Assert.That(result.order).IsEqualTo("ABCD");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EdgeScrollMovesStepBeyondOriginalViewportAndStopsAfterDrop(bool upward)
    {
        using var headless = HeadlessUnitTestSession.StartNew(typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var result = headless.Dispatch(async () =>
        {
            using var f = new UiFixture(18);
            if (upward)
            {
                f.Scroll.Offset = new Vector(0, f.Scroll.Extent.Height - f.Scroll.Viewport.Height);
                f.Layout();
            }

            var originalOffset = f.Scroll.Offset.Y;
            var sourceId = upward ? f.Ids[^1] : f.Ids[0];
            var start = f.SourcePoint(sourceId);
            var edge = f.Scroll.TranslatePoint(new Point(30, upward ? 8 : f.Scroll.Viewport.Height - 8), f.Window)!.Value;
            f.Window.MouseDown(start, MouseButton.Left);
            f.Window.MouseMove(edge, RawInputModifiers.LeftMouseButton);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Math.Abs(f.Scroll.Offset.Y - originalOffset) < 140 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            var scrolled = Math.Abs(f.Scroll.Offset.Y - originalOffset) > 100;
            f.Window.MouseUp(edge, MouseButton.Left);
            f.Layout();
            var afterDrop = f.Session.StepJournal.Select(entry => entry.StepId).ToArray();
            var clean = !f.Indicator.IsVisible;
            var offset = f.Scroll.Offset.Y;
            // A UI timer tick after release must not keep moving the viewport.
            var nextTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (_, _) => { timer.Stop(); nextTick.TrySetResult(); };
            timer.Start();
            await nextTick.Task;
            return (scrolled, movedPastFirstPage: upward
                    ? Array.IndexOf(afterDrop, sourceId) <= f.Ids.Length - 4
                    : Array.IndexOf(afterDrop, sourceId) >= 3,
                clean, stable: offset == f.Scroll.Offset.Y);
        }, CancellationToken.None).GetAwaiter().GetResult();

        await Assert.That(result.scrolled).IsTrue();
        await Assert.That(result.movedPastFirstPage).IsTrue();
        await Assert.That(result.clean).IsTrue();
        await Assert.That(result.stable).IsTrue();
    }

    [Test]
    public async Task RenderedDragAndUndoCaptureBothThemes()
    {
        const string renderChild = "APPAUTOMATION_RECORDER_RENDER_CHILD";
        if (Environment.GetEnvironmentVariable(renderChild) != "1")
        {
            // Capture pixels in a fresh process, separate from the non-rendering UI tests.
            var launch = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (Path.GetFileNameWithoutExtension(launch.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                launch.ArgumentList.Add(typeof(RecorderStepDragDropTests).Assembly.Location);
            }

            launch.ArgumentList.Add("--treenode-filter");
            launch.ArgumentList.Add("/*/*/RecorderStepDragDropTests/RenderedDragAndUndoCaptureBothThemes");
            launch.Environment[renderChild] = "1";
            using var process = Process.Start(launch) ?? throw new InvalidOperationException("Renderer test did not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Assert.That(process.ExitCode).IsEqualTo(0).Because(await output + await errors);
            return;
        }

        foreach (var theme in new[] { RecorderOverlayTheme.Light, RecorderOverlayTheme.Dark })
        {
            await CaptureRenderedTheme(theme);
        }
    }

    private static async Task CaptureRenderedTheme(RecorderOverlayTheme theme)
    {
        using var headless = HeadlessUnitTestSession.StartNew(typeof(RenderedTestAppBuilder), AvaloniaTestIsolationLevel.PerTest);
        var result = headless.Dispatch(() =>
        {
            using var f = new UiFixture(theme: theme);
            var artifacts = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "TestResults", "RecorderDrag"));
            void CaptureState(string state)
            {
                f.Layout();
                using var bitmap = f.Window.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("Rendered recorder frame is unavailable.");
                bitmap.Save(Path.Combine(artifacts.FullName, $"{theme.ToString().ToLowerInvariant()}-{state}.png"));
            }

            CaptureState("before");
            var destination = f.RowPoint(f.Ids[0], false);
            f.Window.MouseDown(f.SourcePoint(f.Ids[2]), MouseButton.Left);
            f.Window.MouseMove(destination, RawInputModifiers.LeftMouseButton);
            CaptureState("dragging");
            var indicator = f.Indicator.IsVisible;
            f.Window.MouseUp(destination, MouseButton.Left);
            CaptureState("after");
            var moved = Order(f.Session);
            f.Press(Key.Z, RawInputModifiers.Control);
            CaptureState("undo");
            return (indicator, moved, undone: Order(f.Session));
        }, CancellationToken.None).GetAwaiter().GetResult();

        await Assert.That(result.indicator).IsTrue();
        await Assert.That(result.moved).IsEqualTo("CABD");
        await Assert.That(result.undone).IsEqualTo("ABCD");
    }

    private static async Task WaitForIdle(RecorderSession session)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (session.IsBusy && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        if (session.IsBusy) throw new TimeoutException("Recorder save did not complete.");
    }

    private static T InUi<T>(Func<UiFixture, T> action)
    {
        using var headless = HeadlessUnitTestSession.StartNew(typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        return headless.Dispatch(() =>
        {
            using var fixture = new UiFixture();
            return action(fixture);
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static T InUiAsync<T>(Func<UiFixture, Task<T>> action)
    {
        using var headless = HeadlessUnitTestSession.StartNew(typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        return headless.Dispatch(async () =>
        {
            using var fixture = new UiFixture();
            return await action(fixture);
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static RecorderSession CreateSession(
        Func<IReadOnlyList<RecordedStep>, string?, CancellationToken, Task<RecorderSaveResult>>? save = null,
        Func<IReadOnlyList<RecordedStep>, string?, CancellationToken, Task<RecorderSaveResult>>? autosave = null) =>
        new(RecorderTestWindow.CreateStub(), Options(), static () => new StackPanel(),
            attachWindowHandlers: false, saveOperation: save, autosaveOperation: autosave);

    private static AppAutomationRecorderOptions Options(RecorderOverlayTheme? theme = null) => new()
    {
        OverlayTheme = theme,
        ShowOverlay = false,
        DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false },
        Validation = new RecorderValidationOptions
        {
            ValidateSelectors = false,
            ValidateRuntimeTargets = false,
            CaptureInvalidSteps = true
        }
    };

    private static RecordedControlDescriptor Descriptor(string name) => new(name, UiControlType.TextBox,
        name, UiLocatorKind.AutomationId, false, typeof(TextBox).FullName!, null);

    private static RecordedStep Step(string name) => new(RecordedActionKind.EnterText, Descriptor(name),
        StringValue: $"value {name}", StepId: Guid.NewGuid());

    private static RecordedStep[] AddSteps(RecorderSession session, int count, bool variedHeight = false)
    {
        var steps = Enumerable.Range(0, count).Select(index =>
        {
            var step = Step(((char)('A' + index)).ToString());
            return variedHeight && index == 1 ? step with
            {
                ValidationStatus = RecorderValidationStatus.Warning,
                ValidationMessage = "Selector warning.\nReview the recorded target."
            } : step;
        }).ToArray();
        foreach (var step in steps) session.AddRecordedStepForTesting(step);
        return steps;
    }

    private static string Order(RecorderSession session) => string.Concat(session.StepJournal.Select(entry =>
        entry.Preview.Split("page.", StringSplitOptions.None)[1][0]));

    private static RecorderSaveResult Saved(int count) => RecorderSaveResult.Completed(
        "Saved.", "Page.cs", "Scenario.cs", count, 0);

    private sealed class UiFixture : IDisposable
    {
        public readonly TaskCompletionSource<RecorderSaveResult> ReleaseSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RecorderSession Session { get; }
        public RecorderOverlay Overlay { get; }
        public Window Window { get; }
        public ScrollViewer Scroll { get; }
        public Panel Panel { get; }
        public TextBlock Indicator { get; }
        public Guid[] Ids { get; }

        public UiFixture(int count = 4, double height = 980, RecorderOverlayTheme? theme = null)
        {
            Session = CreateSession(save: (_, _, _) => ReleaseSave.Task);
            var steps = AddSteps(Session, count, variedHeight: true);
            Ids = steps.Select(step => step.StepId).ToArray();
            Overlay = new RecorderOverlay();
            var options = Options(theme);
            Overlay.Attach(Session, options);
            Window = new Window
            {
                Width = 1080,
                Height = height,
                Content = Overlay,
                RequestedThemeVariant = theme == RecorderOverlayTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            Window.Show();
            Scroll = Overlay.FindControl<ScrollViewer>("StepJournalScrollViewer")!;
            Panel = Overlay.FindControl<Panel>("StepJournalPanel")!;
            Indicator = Overlay.FindControl<TextBlock>("StepDropIndicator")!;
            Layout();
            Scroll.Offset = Vector.Zero;
            Layout();
        }

        public Control Row(Guid id) => Panel.Children.Single(row => row.Tag is Guid stepId && stepId == id);

        public Point SourcePoint(Guid id)
        {
            var handle = Row(id).GetVisualDescendants().OfType<TextBlock>().Single(text =>
                AutomationProperties.GetAutomationId(text) == $"RecorderStepDragHandle_{id:N}");
            return handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), Window)!.Value;
        }

        public Point RowPoint(Guid id, bool after)
        {
            var row = Row(id);
            return row.TranslatePoint(new Point(30, row.Bounds.Height * (after ? 0.8 : 0.2)), Window)!.Value;
        }

        public void Drag(Guid source, Guid target, bool after)
        {
            Row(source).BringIntoView();
            Layout();
            Window.MouseDown(SourcePoint(source), MouseButton.Left);
            Row(target).BringIntoView();
            Layout();
            var point = RowPoint(target, after);
            Window.MouseMove(point, RawInputModifiers.LeftMouseButton);
            Window.MouseUp(point, MouseButton.Left);
            Layout();
        }

        public void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            var physicalKey = key == Key.Z ? PhysicalKey.Z : PhysicalKey.Escape;
            Window.KeyPress(key, modifiers, physicalKey, null);
            Window.KeyRelease(key, modifiers, physicalKey, null);
        }

        public void Layout()
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        }

        public void Dispose()
        {
            ReleaseSave.TrySetResult(Saved(Session.StepCount));
            Window.Close();
            Session.Dispose();
        }
    }

    private sealed class TestApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    private static class RenderedTestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
