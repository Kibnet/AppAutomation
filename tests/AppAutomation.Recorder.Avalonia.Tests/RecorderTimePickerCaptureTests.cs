using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderTimePickerCaptureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StandardSelection_FromPointerOrKeyboard_CreatesOneLosslessStep(bool keyboard)
    {
        using var recorder = TimePickerCaptureFixture.CreateStandard();
        var selectedTime = new TimeSpan(9, 45, 30);

        recorder.Start();
        recorder.Select(selectedTime, keyboard);

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(recorder.OnlyStep.Preview).Contains(
                $"Page.SetTime(static page => page.StartTimePicker, new global::System.TimeSpan({selectedTime.Ticks}L));");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("recorder warning");
        }
    }

    [Test]
    public async Task CompositeSelection_IsRecordedOnlyAfterConfirm()
    {
        using var recorder = TimePickerCaptureFixture.CreateConfirmedComposite();

        recorder.Start();
        recorder.EnterInternalText("09:45");
        recorder.Select(new TimeSpan(9, 45, 0), keyboard: false);

        await Assert.That(recorder.Session.StepJournal).IsEmpty();

        recorder.Confirm();

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(recorder.OnlyStep.Preview).Contains("page.DeliveryTimeEditor");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("EnterText");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("ClickButton");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("recorder warning");
        }
    }

    [Test]
    public async Task CompositeSelection_CancelCreatesNoStep()
    {
        using var recorder = TimePickerCaptureFixture.CreateConfirmedComposite();

        recorder.Start();
        recorder.Select(new TimeSpan(10, 15, 0), keyboard: true);
        recorder.Cancel();
        recorder.Session.FlushPendingStateForTesting();

        await Assert.That(recorder.Session.StepJournal).IsEmpty();
    }

    [Test]
    public async Task CompositeSelection_DismissedByFocusLossIsNotConfirmedLater()
    {
        using var recorder = TimePickerCaptureFixture.CreateConfirmedComposite();

        recorder.Start();
        recorder.Select(new TimeSpan(10, 15, 0), keyboard: false);
        recorder.DismissByClickingElsewhere();
        recorder.Confirm();

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(recorder.OnlyStep.Preview).Contains("ContinueButton");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("SetTime");
        }
    }

    [Test]
    public async Task ConfirmedSelection_RemainsPersistableAfterPopupDisappears()
    {
        using var recorder = TimePickerCaptureFixture.CreateConfirmedComposite();

        recorder.Start();
        recorder.Select(new TimeSpan(11, 30, 0), keyboard: false);
        recorder.Confirm(removePopupFirst: true);
        recorder.RetryOnlyStepValidation();

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(recorder.OnlyStep.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
        }
    }

    [Test]
    public async Task TimeAssertion_UsesLogicalLocatorAndSurvivesPopupRemoval()
    {
        using var recorder = TimePickerCaptureFixture.CreateConfirmedComposite();

        recorder.Start();
        recorder.Select(new TimeSpan(12, 0, 0), keyboard: true);
        recorder.Confirm();
        recorder.Session.Clear();
        recorder.Start();
        recorder.CaptureAssertion();
        recorder.Confirm(removePopupFirst: true);
        recorder.RetryOnlyStepValidation();

        using (Assert.Multiple())
        {
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(recorder.OnlyStep.Preview).Contains("Page.WaitUntilTimeEquals(static page => page.DeliveryTimeEditor");
        }
    }
}
