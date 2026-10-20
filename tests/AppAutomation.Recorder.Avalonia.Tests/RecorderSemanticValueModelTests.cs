using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderSemanticValueModelTests
{
    [Test]
    public async Task RelativeDateValidationUsesSuppliedCaptureDate()
    {
        var candidate = RecorderSemanticValueModel.CreateCandidate(DateSnapshot());
        var expression = new RecorderDateExpression(RecorderDateReferenceKind.RelativeToToday, 1);

        var valid = RecorderSemanticValueModel.TryNormalizeLiteralDateExpression(
            candidate,
            expression,
            new DateTime(2024, 1, 1),
            out var normalized,
            out var validError);
        var invalid = RecorderSemanticValueModel.TryNormalizeLiteralDateExpression(
            candidate,
            expression,
            DateTime.MaxValue.Date,
            out _,
            out var invalidError);

        await Assert.That(valid).IsTrue();
        await Assert.That(normalized).IsEqualTo(expression);
        await Assert.That(validError).IsEmpty();
        await Assert.That(invalid).IsFalse();
        await Assert.That(invalidError).Contains("outside the supported range");
    }

    [Test]
    public async Task SnapshotStepUsesSuppliedIdentityAndValidationTime()
    {
        var snapshot = DateSnapshot();
        var stepId = Guid.NewGuid();
        var validationAt = new DateTimeOffset(2024, 1, 1, 12, 30, 0, TimeSpan.Zero);

        var result = RecorderSemanticValueModel.CreateStepFromSnapshot(
            snapshot,
            snapshot.Prototype,
            "Captured date.",
            stepId,
            validationAt);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Step?.StepId).IsEqualTo(stepId);
        await Assert.That(result.Step?.LastValidationAt).IsEqualTo(validationAt);
        await Assert.That(result.Message).IsEqualTo("Captured date.");
    }

    private static RecorderSemanticValueSnapshot DateSnapshot()
    {
        var control = new RecordedControlDescriptor(
            "Date",
            UiControlType.DateTimePicker,
            "Date",
            UiLocatorKind.AutomationId,
            false,
            "DatePicker",
            null);
        var step = new RecordedStep(
            RecordedActionKind.AssertValue,
            control,
            DateValue: new DateTime(2024, 1, 1),
            ValueKind: RecorderValueKind.Date,
            ValueAccessorKind: RecorderValueAccessorKind.SelectedDate);
        return new RecorderSemanticValueSnapshot(
            step,
            new RecorderSemanticValueDescription(RecorderValueKind.Date, "dateCheckpoint", "2024-01-01"));
    }
}
