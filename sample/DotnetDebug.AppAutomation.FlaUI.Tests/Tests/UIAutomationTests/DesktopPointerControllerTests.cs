using System.Drawing;
using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Input;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

/// <summary>Fault injection over the primitive backend; these tests never access the OS pointer.</summary>
public sealed class DesktopPointerControllerTests
{
    private static readonly Point Saved = new(77, 88);
    private static readonly Point Target = new(140, 160);

    [Test]
    public async Task Click_SavesImmediatelyBeforeActionAndRestoresWithAllButtonsUp()
    {
        var fake = new FakeBackend();
        var coordinator = new FakeCoordinator();
        await Controller(fake, coordinator).RunAsync("click", TimeSpan.FromSeconds(1), op => op.ClickAsync(Target, new()));
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
        await Assert.That(fake.Events.SequenceEqual(["move:140,160", "down:Left", "up:Left", "move:77,88"])).IsTrue();
        await Assert.That(fake.MovedWhilePressed).IsFalse();
        await Assert.That(coordinator.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task DoubleAndRightClick_PreserveButtonAndClickCount()
    {
        var fake = new FakeBackend { DoubleClickTime = TimeSpan.FromMilliseconds(200) };
        await Controller(fake).RunAsync("double", TimeSpan.FromSeconds(1), op => op.ClickAsync(Target,
            new PointerClickOptions { Button = PointerButton.Right, ClickCount = 2 }));
        await Assert.That(fake.Events.Count(value => value == "down:Right")).IsEqualTo(2);
        await Assert.That(fake.Events.Count(value => value == "up:Right")).IsEqualTo(2);
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
    }

    [Test]
    public async Task SlowSecondTargetValidation_DoesNotTurnDoubleClickIntoTwoSingleClicks()
    {
        var fake = new FakeBackend { DoubleClickTime = TimeSpan.FromMilliseconds(50) };
        var validations = 0;
        var error = await Capture(() => Controller(fake).RunAsync("double", TimeSpan.FromSeconds(1),
            op => op.ClickAsync(Target, new PointerClickOptions { ClickCount = 2 }, () =>
            {
                if (++validations == 2) Thread.Sleep(80);
            })));
        await Assert.That(error is TimeoutException).IsTrue();
        await Assert.That(fake.DownPositions.Count).IsEqualTo(1);
        await Assert.That(fake.UpPositions.Count).IsEqualTo(1);
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
    }

    [Test]
    public async Task TwoSingleClicks_AreSeparatedByTheNativeDoubleClickInterval()
    {
        var fake = new FakeBackend { DoubleClickTime = TimeSpan.FromMilliseconds(45) };
        var controller = Controller(fake);
        await controller.RunAsync("first", TimeSpan.FromSeconds(1), op => op.ClickAsync(Target, new()));
        await controller.RunAsync("second", TimeSpan.FromSeconds(1), op => op.ClickAsync(Target, new()));
        await Assert.That(fake.DownTimes[1] - fake.DownTimes[0] >= TimeSpan.FromMilliseconds(40)).IsTrue();
    }

    [Test]
    public async Task LongDoubleClickInterval_DoesNotDelayRestoreOrSessionDispose_NextCallTimesOutBeforeSnapshot()
    {
        var fake = new FakeBackend { DoubleClickTime = TimeSpan.FromSeconds(5) };
        var queue = new FakeCoordinator();
        var controller = Controller(fake, queue);
        var session = new PointerSession();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await controller.RunAsync("first", TimeSpan.FromSeconds(1), op => op.ClickAsync(Target, new()), session: session);
        session.Dispose();
        await Assert.That(timer.Elapsed < TimeSpan.FromSeconds(1)).IsTrue();
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
        var readsAfterFirst = fake.Reads;
        var eventsAfterFirst = fake.Events.Count;
        var error = await Capture(() => controller.RunAsync("second", TimeSpan.FromMilliseconds(80), op => op.ClickAsync(Target, new())));
        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(fake.Reads).IsEqualTo(readsAfterFirst);
        await Assert.That(fake.Events.Count).IsEqualTo(eventsAfterFirst);
        await Assert.That(queue.Releases).IsEqualTo(2);
    }

    [Test]
    public async Task MonotonicCooldown_UsesUptimeAndTreatsRebootOrCorruptFutureStateConservatively()
    {
        var interval = TimeSpan.FromMilliseconds(500);
        await Assert.That(DesktopInputCoordinator.ComputeCooldown(10_100, 10_000, interval)).IsEqualTo(TimeSpan.FromMilliseconds(400));
        await Assert.That(DesktopInputCoordinator.ComputeCooldown(11_000, 10_000, interval)).IsEqualTo(TimeSpan.Zero);
        await Assert.That(DesktopInputCoordinator.ComputeCooldown(100, 10_000, interval)).IsEqualTo(interval);
        await Assert.That(DesktopInputCoordinator.ComputeCooldown(10_000, -1, interval)).IsEqualTo(interval);
        // A wall clock correction is intentionally absent from the computation's inputs.
        await Assert.That(DesktopInputCoordinator.ComputeCooldown(10_100, DateTime.MaxValue.Ticks, interval)).IsEqualTo(interval);
    }

    [Test]
    public async Task Hover_ObservesTargetAndReturnsOnlyAfterVerification()
    {
        var fake = new FakeBackend();
        var count = 0;
        await Controller(fake).RunAsync("hover", TimeSpan.FromSeconds(1), op => op.HoverAsync(Target, async ct =>
        {
            count++;
            await Task.Delay(20, ct);
            await Assert.That(fake.State.Position).IsEqualTo(Target);
        }));
        await Assert.That(count).IsEqualTo(1);
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
        await Assert.That(fake.Events.Any(value => value.StartsWith("down:"))).IsFalse();
    }

    [Test]
    public async Task Hover_AlreadyAtTargetDoesNotLeaveAndReenter()
    {
        var fake = new FakeBackend { State = new PointerState(Target, PointerButtons.None, 0) };
        await Controller(fake).RunAsync("hover", TimeSpan.FromSeconds(1), op => op.HoverAsync(Target, _ => Task.CompletedTask));
        await Assert.That(fake.Events.All(value => value == "move:140,160")).IsTrue();
    }

    [Test]
    public async Task HoverAssertionFailure_PreservesOriginalExceptionAndRestores()
    {
        var fake = new FakeBackend();
        var expected = new InvalidOperationException("assertion evidence");
        var actual = await Capture(() => Controller(fake).RunAsync("hover", TimeSpan.FromSeconds(1),
            op => op.HoverAsync(Target, _ => Task.FromException(expected))));
        await Assert.That(ReferenceEquals(actual, expected)).IsTrue();
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
        await Assert.That(DesktopPointer.IsTerminalFailure(new AggregateException(actual!))).IsTrue();
        var artifacts = (IReadOnlyList<UiFailureArtifact>)actual!.Data["AppAutomation.Pointer.Artifacts"]!;
        await Assert.That(artifacts.Single().ContentType).IsEqualTo("application/x-ndjson");
        await Assert.That(artifacts.Single().InlineTextPreview!.Contains("assertion evidence")).IsFalse();
    }

    [Test]
    public async Task NonFinishingHoverTask_DeadlineCancelsCallbackAndRestores()
    {
        var fake = new FakeBackend();
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken callbackToken = default;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var error = await Capture(() => Controller(fake).RunAsync("hover", TimeSpan.FromMilliseconds(70), op => op.HoverAsync(Target, ct =>
        {
            callbackToken = ct;
            return never.Task;
        })));
        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(callbackToken.IsCancellationRequested).IsTrue();
        await Assert.That(timer.Elapsed < TimeSpan.FromSeconds(2)).IsTrue();
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
    }

    [Test]
    public async Task NestedPointerFromHover_IsRejectedWithoutAcquiringAgain()
    {
        var fake = new FakeBackend();
        var coordinator = new FakeCoordinator();
        var controller = Controller(fake, coordinator);
        var error = await Capture(() => controller.RunAsync("hover", TimeSpan.FromSeconds(1), op => op.HoverAsync(Target,
            _ => controller.RunAsync("nested", TimeSpan.FromSeconds(1), nested => nested.ClickAsync(Target, new())))));
        await Assert.That(error!.Message.Contains("Nested pointer")).IsTrue();
        await Assert.That(coordinator.Acquisitions).IsEqualTo(1);
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
    }

    [Test]
    public async Task LateCallback_CannotStartPointerWorkAfterDeadline()
    {
        var fake = new FakeBackend();
        var controller = Controller(fake);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateError = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Capture(() => controller.RunAsync("hover", TimeSpan.FromMilliseconds(50), op => op.HoverAsync(Target, async _ =>
        {
            await resume.Task;
            lateError.SetResult(await Capture(() => controller.RunAsync("late", TimeSpan.FromSeconds(1), nested => nested.ClickAsync(Target, new()))));
        })));
        resume.SetResult();
        var error = await lateError.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.That(error!.Message.Contains("context cannot start")).IsTrue();
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
    }

    [Test]
    public async Task Interference_RestoresOnceWithoutReplayingGesture()
    {
        var fake = new FakeBackend();
        var error = await Capture(() => Controller(fake).RunAsync("hover", TimeSpan.FromSeconds(1), op => op.HoverAsync(Target, _ =>
        {
            fake.State = fake.State with { Position = new Point(300, 300) };
            return Task.CompletedTask;
        })));
        await Assert.That(error is PointerInterferenceException).IsTrue();
        await Assert.That(fake.Events.Count(value => value == "move:140,160")).IsEqualTo(1);
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
    }

    [Test]
    public async Task ForeignButtonDownWithoutMovement_WaitsForNaturalUpBeforeRestoring()
    {
        var fake = new FakeBackend();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = await Capture(() => Controller(fake).RunAsync("hover", TimeSpan.FromSeconds(1), op => op.HoverAsync(Target, ct =>
        {
            fake.State = fake.State with { Buttons = PointerButtons.Right };
            _ = Task.Run(async () => { await Task.Delay(60); fake.State = fake.State with { Buttons = PointerButtons.None }; release.SetResult(); });
            return Task.CompletedTask;
        })));
        await release.Task;
        await Assert.That(error is PointerInterferenceException).IsTrue();
        await Assert.That(fake.Events.Contains("up:Right")).IsFalse();
        await Assert.That(fake.MovedWhilePressed).IsFalse();
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
    }

    [Test]
    public async Task ForeignButtonStaysDown_SkipsRestoreFaultsSessionAndReleasesOwnership()
    {
        var fake = new FakeBackend();
        var queue = new FakeCoordinator();
        using var session = new PointerSession();
        var error = await Capture(() => Controller(fake, queue).RunAsync("hover", TimeSpan.FromSeconds(2), op => op.HoverAsync(Target, _ =>
        {
            fake.State = fake.State with { Buttons = PointerButtons.Right };
            return Task.CompletedTask;
        }), session: session));
        await Assert.That(error is PointerInterferenceException).IsTrue();
        await Assert.That(error!.Data.Contains(DesktopPointerController.CleanupKey)).IsTrue();
        await Assert.That(fake.State.Position).IsEqualTo(Target);
        await Assert.That(fake.Events.Contains("up:Right")).IsFalse();
        await Assert.That(fake.MovedWhilePressed).IsFalse();
        await Assert.That(session.IsFaulted).IsTrue();
        await Assert.That(queue.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task ForeignModifierDuringHover_AbortsWithoutReplayingAndDoesNotReleaseUserKey()
    {
        var fake = new FakeBackend();
        var error = await Capture(() => Controller(fake).RunAsync("hover", TimeSpan.FromSeconds(1), op => op.HoverAsync(Target, _ =>
        {
            fake.State = fake.State with { Modifiers = 2 };
            return Task.CompletedTask;
        })));
        await Assert.That(error is PointerInterferenceException).IsTrue();
        await Assert.That(fake.State.Modifiers).IsEqualTo(2);
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
        await Assert.That(fake.Events.Any(value => value.StartsWith("down:") || value.StartsWith("up:"))).IsFalse();
        await Assert.That(fake.Events.Count(value => value == "move:140,160")).IsEqualTo(1);
    }

    [Test]
    public async Task SavedMonitorDisappearsDuringHover_ReportsCleanupFailureWithoutParkingElsewhere()
    {
        var fake = new FakeBackend();
        var queue = new FakeCoordinator();
        using var session = new PointerSession();
        var error = await Capture(() => Controller(fake, queue).RunAsync("hover", TimeSpan.FromSeconds(1), op => op.HoverAsync(Target, _ =>
        {
            fake.Monitors = [new Rectangle(Target.X - 10, Target.Y - 10, 20, 20)];
            return Task.CompletedTask;
        }), session: session));
        await Assert.That(error is PointerCleanupException).IsTrue();
        await Assert.That(fake.State.Position).IsEqualTo(Target);
        await Assert.That(fake.Events.SequenceEqual(["move:140,160"])).IsTrue();
        await Assert.That(session.IsFaulted).IsTrue();
        await Assert.That(queue.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task DownDeliveredThenThrows_StillReleasesOwnedButtonBeforeRestore()
    {
        var fake = new FakeBackend { FailDownAfterDelivery = true };
        var error = await Capture(() => Controller(fake).RunAsync("click", TimeSpan.FromSeconds(1), op => op.ClickAsync(Target, new())));
        await Assert.That(error!.Message).IsEqualTo("down failed after delivery");
        await Assert.That(fake.State.Buttons).IsEqualTo(PointerButtons.None);
        await Assert.That(fake.Events.Contains("up:Left")).IsTrue();
        await Assert.That(fake.MovedWhilePressed).IsFalse();
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
    }

    [Test]
    public async Task OwnedUpCannotBeConfirmed_DoesNotRestorePressedCursor()
    {
        var fake = new FakeBackend { NeverReleaseOwnedButton = true };
        using var session = new PointerSession();
        var error = await Capture(() => Controller(fake).RunAsync("click", TimeSpan.FromSeconds(2), op => op.ClickAsync(Target, new()), session: session));
        await Assert.That(error is PointerInterferenceException).IsTrue();
        await Assert.That(error!.Data.Contains(DesktopPointerController.CleanupKey)).IsTrue();
        await Assert.That(fake.State.Position).IsEqualTo(Target);
        await Assert.That(fake.MovedWhilePressed).IsFalse();
        await Assert.That(session.IsFaulted).IsTrue();
    }

    [Test]
    public async Task RestoreFailureAfterAssertion_AttachesCleanupWithoutMaskingAssertion()
    {
        var fake = new FakeBackend { FailRestore = true };
        var primary = new InvalidOperationException("assertion failure");
        var error = await Capture(() => Controller(fake).RunAsync("hover", TimeSpan.FromSeconds(1), op => op.HoverAsync(Target, _ => Task.FromException(primary))));
        await Assert.That(ReferenceEquals(primary, error)).IsTrue();
        await Assert.That(primary.Data[DesktopPointerController.CleanupKey] is PointerCleanupException).IsTrue();
    }

    [Test]
    public async Task RestoreFailureAfterSuccess_IsReportedAsFailure()
    {
        var fake = new FakeBackend { FailRestore = true };
        var error = await Capture(() => Controller(fake).RunAsync("hover", TimeSpan.FromSeconds(1), op => op.HoverAsync(Target, _ => Task.CompletedTask)));
        await Assert.That(error is PointerCleanupException).IsTrue();
    }

    [Test]
    public async Task NegativeCoordinatesAreValid_EmptyMonitorGapIsRejected()
    {
        var fake = new FakeBackend { Monitors = [new Rectangle(-800, 0, 800, 600), new Rectangle(0, 0, 800, 600)] };
        await Controller(fake).RunAsync("negative", TimeSpan.FromSeconds(1), op => op.ClickAsync(new Point(-200, 100), new()));
        await Assert.That(fake.Events.Contains("move:-200,100")).IsTrue();
        var error = await Capture(() => Controller(fake).RunAsync("gap", TimeSpan.FromSeconds(1), op => op.ClickAsync(new Point(100, 700), new())));
        await Assert.That(error!.Message.Contains("outside every physical monitor")).IsTrue();
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
    }

    [Test]
    public async Task RejectedPositionMayRetryOnce_ButUnexpectedPositionIsNotChased()
    {
        var fake = new FakeBackend { IgnoreFirstMove = true };
        await Controller(fake).RunAsync("retry", TimeSpan.FromSeconds(1), op => op.ClickAsync(Target, new()));
        await Assert.That(fake.Events.Count(value => value == "move:140,160")).IsEqualTo(2);
        fake = new FakeBackend { InterfereOnFirstMove = true };
        var error = await Capture(() => Controller(fake).RunAsync("interference", TimeSpan.FromSeconds(1), op => op.ClickAsync(Target, new())));
        await Assert.That(error is PointerInterferenceException).IsTrue();
        await Assert.That(fake.Events.Count(value => value == "move:140,160")).IsEqualTo(1);
        await Assert.That(fake.Events.Any(value => value.StartsWith("down:"))).IsFalse();
    }

    [Test]
    public async Task ModifierPreflightTimesOutWithoutTouchingMouse()
    {
        var fake = new FakeBackend { State = new PointerState(Saved, PointerButtons.None, 2) };
        var error = await Capture(() => Controller(fake).RunAsync("click", TimeSpan.FromMilliseconds(35), op => op.ClickAsync(Target, new())));
        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(fake.Events.Count).IsEqualTo(0);
    }

    [Test]
    public async Task HeldEscape_PreflightDoesNotStartOrSilentlyCancelTheDrag()
    {
        var fake = new FakeBackend { State = new PointerState(Saved, PointerButtons.None, WindowsPointerBackend.EscapeInputFlag) };
        var error = await Capture(() => Controller(fake).RunAsync("drag", TimeSpan.FromMilliseconds(35),
            op => op.DragAsync(Target, new Point(300, 300), new())));
        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(fake.Events.Count).IsEqualTo(0);
        await Assert.That(fake.State.Modifiers).IsEqualTo(WindowsPointerBackend.EscapeInputFlag);
    }

    [Test]
    public async Task UnavailableDesktopDoesNotInjectAnything()
    {
        var fake = new FakeBackend { DesktopUnavailable = true };
        var queue = new FakeCoordinator();
        var error = await Capture(() => Controller(fake, queue).RunAsync("click", TimeSpan.FromSeconds(1), op => op.ClickAsync(Target, new())));
        await Assert.That(error!.Message).IsEqualTo("desktop unavailable");
        await Assert.That(fake.Events.Count).IsEqualTo(0);
        await Assert.That(queue.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task DragDeliversPathReleasesAtDestinationThenRestores()
    {
        var fake = new FakeBackend();
        var destination = new Point(280, 300);
        await Controller(fake).RunAsync("drag", TimeSpan.FromSeconds(1), op => op.DragAsync(Target, destination,
            new PointerDragOptions { Duration = TimeSpan.FromMilliseconds(64) }));
        await Assert.That(fake.DownPositions.Single()).IsEqualTo(Target);
        await Assert.That(fake.UpPositions.Single()).IsEqualTo(destination);
        await Assert.That(fake.Events.Count(value => value.StartsWith("move:")) >= 5).IsTrue();
        await Assert.That(fake.MotionPackets >= 4).IsTrue();
        await Assert.That(fake.Events[^2]).IsEqualTo("up:Left");
        await Assert.That(fake.Events[^1]).IsEqualTo("move:77,88");
    }

    [Test]
    public async Task DragCancellationReleasesButtonAndRestores()
    {
        var fake = new FakeBackend();
        using var cancelled = new CancellationTokenSource(45);
        var error = await Capture(() => Controller(fake).RunAsync("drag", TimeSpan.FromSeconds(1), op => op.DragAsync(Target, new Point(350, 350),
            new PointerDragOptions { Duration = TimeSpan.FromMilliseconds(160) }), cancelled.Token));
        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(fake.State.Buttons).IsEqualTo(PointerButtons.None);
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
        await Assert.That(fake.Events[^2]).IsEqualTo("up:Left");
    }

    [Test]
    public async Task TinyPublicDragIsRejected_InternalScrollbarKeepsExactTinyEndpoint()
    {
        var fake = new FakeBackend();
        var endpoint = new Point(Target.X, Target.Y + 1);
        var error = await Capture(() => Controller(fake).RunAsync("drag", TimeSpan.FromSeconds(1), op => op.DragAsync(Target, endpoint, new())));
        await Assert.That(error is ArgumentException).IsTrue();
        await Assert.That(fake.DownPositions.Count).IsEqualTo(0);
        await Controller(fake).RunAsync("thumb", TimeSpan.FromSeconds(1), op => op.DragAsync(Target, endpoint,
            new PointerDragOptions { Duration = TimeSpan.FromMilliseconds(32) }, requireDragThreshold: false));
        await Assert.That(fake.UpPositions.Single()).IsEqualTo(endpoint);
    }

    [Test]
    public async Task SessionDisposalCancelsHoverWithoutRestoringSessionStartPosition()
    {
        var fake = new FakeBackend();
        var session = new PointerSession();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = Controller(fake).RunAsync("hover", TimeSpan.FromSeconds(4), op => op.HoverAsync(Target, async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }), session: session);
        await started.Task;
        await Task.Run(session.Dispose);
        var error = await Capture(() => operation);
        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
    }

    [Test]
    public async Task DisposedSessionRejectsLateOperationBeforeOwnershipOrSnapshot()
    {
        var fake = new FakeBackend();
        var queue = new FakeCoordinator();
        var session = new PointerSession();
        session.Dispose();
        var error = await Capture(() => Controller(fake, queue).RunAsync("late", TimeSpan.FromSeconds(1),
            op => op.ClickAsync(Target, new()), session: session));
        await Assert.That(error is ObjectDisposedException).IsTrue();
        await Assert.That(queue.Acquisitions).IsEqualTo(0);
        await Assert.That(fake.Reads).IsEqualTo(0);
        await Assert.That(fake.Events.Count).IsEqualTo(0);
    }

    [Test]
    public async Task QueueCancellationDoesNotSnapshotOrMoveCursor()
    {
        var fake = new FakeBackend();
        var queue = new FakeCoordinator();
        using var owned = queue.Acquire("fake", default);
        var error = await Task.Run(() => Capture(() => Controller(fake, queue).RunAsync("queued", TimeSpan.FromMilliseconds(35), op => op.ClickAsync(Target, new()))));
        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(fake.Reads).IsEqualTo(0);
        await Assert.That(fake.Events.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Geometry_RejectsEmptyHiddenAndOutsideBoundsButAllowsDisabledHoverAndOrigin()
    {
        foreach (var bounds in new[] { Rectangle.Empty, new Rectangle(2, 2, -1, 10), new Rectangle(2, 2, 10, 0) })
        {
            var error = await Capture(() =>
            {
                _ = DesktopPointerTargetResolver.ChoosePoint(bounds, false, true, true);
                return Task.CompletedTask;
            });
            await Assert.That(error is InvalidOperationException).IsTrue();
        }
        var valid = new Rectangle(-5, -5, 10, 10);
        var hidden = await Capture(() => { _ = DesktopPointerTargetResolver.ChoosePoint(valid, true, true, true); return Task.CompletedTask; });
        var disabled = await Capture(() => { _ = DesktopPointerTargetResolver.ChoosePoint(valid, false, false, true); return Task.CompletedTask; });
        var outside = await Capture(() => { _ = DesktopPointerTargetResolver.ChoosePoint(valid, false, true, true, explicitPoint: new Point(50, 50)); return Task.CompletedTask; });
        await Assert.That(hidden is InvalidOperationException).IsTrue();
        await Assert.That(disabled is InvalidOperationException).IsTrue();
        await Assert.That(outside is InvalidOperationException).IsTrue();
        await Assert.That(DesktopPointerTargetResolver.ChoosePoint(valid, false, false, false)).IsEqualTo(Point.Empty);
        await Assert.That(DesktopPointerTargetResolver.ChoosePoint(new Rectangle(-300, 0, 100, 100), false, true, true)).IsEqualTo(new Point(-250, 50));
    }

    [Test]
    public async Task AbsoluteMotionCoordinates_RoundTripPhysicalPixelsAcrossNegativeVirtualDesktop()
    {
        var desktop = new Rectangle(-3840, -1080, 8960, 3240);
        foreach (var point in new[] { desktop.Location, Point.Empty, new Point(-100, 300), new Point(2153, 1083), new Point(desktop.Right - 1, desktop.Bottom - 1) })
        {
            var normalized = WindowsPointerBackend.NormalizeAbsolutePoint(point, desktop);
            var decoded = new Point((int)((long)normalized.X * desktop.Width / 65536) + desktop.Left,
                (int)((long)normalized.Y * desktop.Height / 65536) + desktop.Top);
            await Assert.That(decoded).IsEqualTo(point);
        }
    }

    [Test]
    public async Task UpFailureBeforeAndAfterDelivery_BothGetSafeCleanup()
    {
        foreach (var delivered in new[] { false, true })
        {
            var fake = new FakeBackend { FailFirstUp = true, DeliverFailedUp = delivered };
            var error = await Capture(() => Controller(fake).RunAsync("click", TimeSpan.FromSeconds(1), op => op.ClickAsync(Target, new())));
            await Assert.That(error!.Message).IsEqualTo("up send failed");
            await Assert.That(fake.State.Buttons).IsEqualTo(PointerButtons.None);
            await Assert.That(fake.State.Position).IsEqualTo(Saved);
            await Assert.That(fake.Events.Count(value => value == "up:Left")).IsEqualTo(2);
            await Assert.That(fake.MovedWhilePressed).IsFalse();
        }
    }

    [Test]
    public async Task InitialReadFailureDoesNotInventAnOriginOrMoveAnything()
    {
        var fake = new FakeBackend { FailReadNumber = 1 };
        var queue = new FakeCoordinator();
        var error = await Capture(() => Controller(fake, queue).RunAsync("click", TimeSpan.FromSeconds(1), op => op.ClickAsync(Target, new())));
        await Assert.That(error!.Message).IsEqualTo("state read failed");
        await Assert.That(fake.Events.Count).IsEqualTo(0);
        await Assert.That(queue.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task CleanupReadFailureDoesNotMoveCursorWithUnknownButtons()
    {
        var fake = new FakeBackend();
        var primary = new InvalidOperationException("assertion failed");
        var error = await Capture(() => Controller(fake).RunAsync("hover", TimeSpan.FromSeconds(1), op => op.HoverAsync(Target, _ =>
        {
            fake.FailReads = true;
            return Task.FromException(primary);
        })));
        await Assert.That(ReferenceEquals(error, primary)).IsTrue();
        await Assert.That(primary.Data.Contains(DesktopPointerController.CleanupKey)).IsTrue();
        await Assert.That(fake.State.Position).IsEqualTo(Target);
        await Assert.That(fake.Events.Contains("move:77,88")).IsFalse();
    }

    [Test]
    public async Task ExactTargetValidationBeforeEachClick_PreventsInputIntoReplacement()
    {
        var fake = new FakeBackend();
        var error = await Capture(() => Controller(fake).RunAsync("click", TimeSpan.FromSeconds(1), op => op.ClickAsync(Target, new(),
            () => throw new PointerInterferenceException("replacement"))));
        await Assert.That(error is PointerInterferenceException).IsTrue();
        await Assert.That(fake.DownPositions.Count).IsEqualTo(0);
        await Assert.That(fake.State.Position).IsEqualTo(Saved);

        var checks = 0;
        fake = new FakeBackend();
        error = await Capture(() => Controller(fake).RunAsync("double", TimeSpan.FromSeconds(1), op => op.ClickAsync(Target, new PointerClickOptions { ClickCount = 2 },
            () => { if (++checks == 2) throw new PointerInterferenceException("replaced after first click"); })));
        await Assert.That(error is PointerInterferenceException).IsTrue();
        await Assert.That(fake.DownPositions.Count).IsEqualTo(1);
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
    }

    [Test]
    public async Task ExactHoverAndDragValidation_RunBeforeCallbackAndBeforeDrop()
    {
        var fake = new FakeBackend();
        var callbacks = 0;
        var hoverError = await Capture(() => Controller(fake).RunAsync("hover", TimeSpan.FromSeconds(1), op => op.HoverAsync(Target,
            _ => { callbacks++; return Task.CompletedTask; }, validateExact: () => throw new PointerInterferenceException("replacement"))));
        await Assert.That(hoverError is PointerInterferenceException).IsTrue();
        await Assert.That(callbacks).IsEqualTo(0);
        fake = new FakeBackend();
        var dragError = await Capture(() => Controller(fake).RunAsync("drag", TimeSpan.FromSeconds(1), op => op.DragAsync(Target, new Point(300, 300),
            new PointerDragOptions { Duration = TimeSpan.FromMilliseconds(32) },
            validateDestination: () => throw new PointerInterferenceException("replaced destination"))));
        await Assert.That(dragError is PointerInterferenceException).IsTrue();
        await Assert.That(fake.Events[^2]).IsEqualTo("up:Left");
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
    }

    [Test]
    public async Task ThrowingCallbackCancellationRegistration_DoesNotMaskAssertion()
    {
        var fake = new FakeBackend();
        var primary = new InvalidOperationException("assertion failure");
        var registrationRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = await Capture(() => Controller(fake).RunAsync("hover", TimeSpan.FromSeconds(1), op => op.HoverAsync(Target, ct =>
        {
            ct.Register(() =>
            {
                registrationRan.SetResult();
                throw new InvalidOperationException("bad cancellation callback");
            });
            return Task.FromException(primary);
        })));
        await registrationRan.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.That(ReferenceEquals(error, primary)).IsTrue();
        await Assert.That(fake.State.Position).IsEqualTo(Saved);
    }

    private static DesktopPointerController Controller(FakeBackend fake, FakeCoordinator? queue = null) => new(fake, queue ?? new());
    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception error) { return error; }
    }

    private sealed class FakeBackend : IPointerBackend
    {
        private readonly object _lock = new();
        private PointerState _state = new(Saved, PointerButtons.None, 0);
        private int _moves;
        internal PointerState State { get { lock (_lock) return _state; } set { lock (_lock) _state = value; } }
        internal List<string> Events { get; } = [];
        internal List<Point> DownPositions { get; } = [];
        internal List<Point> UpPositions { get; } = [];
        internal List<TimeSpan> DownTimes { get; } = [];
        private readonly System.Diagnostics.Stopwatch _time = System.Diagnostics.Stopwatch.StartNew();
        internal Rectangle[] Monitors { get; set; } = [new Rectangle(-1000, -1000, 3000, 3000)];
        internal bool FailDownAfterDelivery { get; init; }
        internal bool NeverReleaseOwnedButton { get; init; }
        internal bool FailRestore { get; init; }
        internal bool IgnoreFirstMove { get; init; }
        internal bool InterfereOnFirstMove { get; init; }
        internal bool DesktopUnavailable { get; init; }
        internal bool FailFirstUp { get; init; }
        internal bool DeliverFailedUp { get; init; }
        internal int FailReadNumber { get; init; }
        internal bool FailReads { get; set; }
        private int _ups;
        internal bool MovedWhilePressed { get; private set; }
        internal int Reads { get; private set; }
        internal int MotionPackets { get; private set; }
        public string DesktopKey => "fake";
        public TimeSpan DoubleClickTime { get; init; } = TimeSpan.FromMilliseconds(1);
        public Size DragThreshold => new(4, 4);
        public void EnsureDesktopAvailable() { if (DesktopUnavailable) throw new InvalidOperationException("desktop unavailable"); }
        public PointerState ReadState()
        {
            Reads++;
            if (FailReads || FailReadNumber == Reads) throw new InvalidOperationException("state read failed");
            return State;
        }
        public bool IsOnMonitor(Point point) => Monitors.Any(bounds => bounds.Contains(point));
        public void SetPosition(Point point)
        {
            Events.Add($"move:{point.X},{point.Y}");
            MovedWhilePressed |= State.Buttons != PointerButtons.None;
            _moves++;
            if (FailRestore && point == Saved) throw new InvalidOperationException("restore failed");
            if (IgnoreFirstMove && _moves == 1) return;
            State = State with { Position = InterfereOnFirstMove && _moves == 1 ? new Point(310, 310) : point };
        }
        public void MoveWithInput(Point point) { MotionPackets++; SetPosition(point); }
        public void Down(PointerButton button)
        {
            Events.Add($"down:{button}"); DownPositions.Add(State.Position); DownTimes.Add(_time.Elapsed);
            State = State with { Buttons = State.Buttons | WindowsPointerBackend.Mask(button) };
            if (FailDownAfterDelivery) throw new InvalidOperationException("down failed after delivery");
        }
        public void Up(PointerButton button)
        {
            Events.Add($"up:{button}"); UpPositions.Add(State.Position);
            _ups++;
            if (FailFirstUp && _ups == 1)
            {
                if (DeliverFailedUp) State = State with { Buttons = State.Buttons & ~WindowsPointerBackend.Mask(button) };
                throw new InvalidOperationException("up send failed");
            }
            if (!NeverReleaseOwnedButton) State = State with { Buttons = State.Buttons & ~WindowsPointerBackend.Mask(button) };
        }
        public void Wheel(double amount, bool horizontal) => Events.Add($"wheel:{amount}:{horizontal}");
    }

    private sealed class FakeCoordinator : IDesktopInputCoordinator
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        internal int Acquisitions;
        internal int Releases;
        private long? _lastButtonUptimeMilliseconds;
        public IDisposable Acquire(string desktopKey, CancellationToken cancellationToken)
        {
            _gate.Wait(cancellationToken);
            Interlocked.Increment(ref Acquisitions);
            return new Lease(this);
        }
        private sealed class Lease(FakeCoordinator owner) : IDisposable, IDesktopClickTimeline
        {
            public void Dispose() { Interlocked.Increment(ref owner.Releases); owner._gate.Release(); }
            public TimeSpan RemainingCooldown(TimeSpan interval)
            {
                return owner._lastButtonUptimeMilliseconds is { } last
                    ? DesktopInputCoordinator.ComputeCooldown(Environment.TickCount64, last, interval) : TimeSpan.Zero;
            }
            public void RecordButtonEvent(long uptimeMilliseconds) => owner._lastButtonUptimeMilliseconds = uptimeMilliseconds;
        }
    }
}
