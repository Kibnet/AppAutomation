using Avalonia.Controls;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderSpinnerProxyCaptureTests
{
    [Test]
    public async Task SpinnerValue_DoesNotValidateWhenProxyPartIsNotATextBox()
    {
        using var recorder = SpinnerProxyCaptureFixture.Create();

        recorder.Start();
        recorder.EnterValue("10.5");
        recorder.ReplaceInteractivePart(new Border());
        recorder.RetryOnlyStepValidation();

        using (Assert.Multiple())
        {
            await Assert.That(recorder.OnlyStep.CanPersist).IsFalse();
            await Assert.That(recorder.OnlyStep.ValidationStatus).IsEqualTo(RecorderValidationStatus.Invalid);
        }
    }

    [Test]
    public async Task SpinnerValue_ThroughConfiguredProxy_RemainsPersistableAndIsSaved()
    {
        using var recorder = SpinnerProxyCaptureFixture.Create();

        recorder.Start();
        recorder.EnterValue("10.5");
        recorder.RetryOnlyStepValidation();
        var saveResult = await recorder.SaveAsync();
        var scenario = recorder.ReadGeneratedScenario(saveResult);

        using (Assert.Multiple())
        {
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(saveResult.Success).IsTrue();
            await Assert.That(saveResult.PersistedStepCount).IsEqualTo(1);
            await Assert.That(scenario).Contains(
                "Page.SetSpinnerValue(static page => page.QuantitySpinner, 10.5);");
            await Assert.That(scenario).DoesNotContain("spinner-textbox-fallback");
            await Assert.That(scenario).DoesNotContain("Mapped recorder locator");
        }
    }

    [Test]
    public async Task SpinnerValueAssertion_ThroughConfiguredProxy_RemainsPersistableAndIsSaved()
    {
        using var recorder = SpinnerProxyCaptureFixture.Create(initialValue: "12");

        recorder.Start();
        recorder.CaptureValueAssertion();
        recorder.RetryOnlyStepValidation();
        var saveResult = await recorder.SaveAsync();

        using (Assert.Multiple())
        {
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(saveResult.Success).IsTrue();
            await Assert.That(saveResult.PersistedStepCount).IsEqualTo(1);
            await Assert.That(recorder.ReadGeneratedScenario(saveResult)).Contains(
                "Page.WaitUntilValueEquals(static page => page.QuantitySpinner, 12);");
        }
    }
}
