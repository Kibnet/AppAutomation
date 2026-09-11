using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderSingleSelectCaptureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ImmediateSelection_FromPointerOrKeyboard_CreatesOneLogicalStep(bool keyboard)
    {
        using var recorder = SingleSelectCaptureFixture.CreateImmediate();

        recorder.Start();
        recorder.Select("Item 42", keyboard);

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(recorder.OnlyStep.Preview)
                .Contains("Page.SelectComboItem(static page => page.CategorySelector, \"Item 42\");");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("recorder warning");
        }
    }

    [Test]
    public async Task EditableSelection_DiscardsSearchTextAndWaitsForConfirm()
    {
        using var recorder = SingleSelectCaptureFixture.CreateEditableConfirmed();

        recorder.Start();
        recorder.Type("item");
        recorder.Select("Item 42");

        await Assert.That(recorder.Session.StepJournal).IsEmpty();

        recorder.Confirm();

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(recorder.OnlyStep.Preview).Contains("page.CategorySelector");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("EnterText");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("ClickButton");
        }
    }

    [Test]
    public async Task CancelOrDismiss_DoesNotRecordSelection()
    {
        using var cancelled = SingleSelectCaptureFixture.CreateEditableConfirmed();
        cancelled.Start();
        cancelled.Select("Item 42");
        cancelled.Cancel();
        cancelled.Flush();

        using var dismissed = SingleSelectCaptureFixture.CreateEditableConfirmed();
        dismissed.Start();
        dismissed.Select("Item 42");
        dismissed.Dismiss();

        using (Assert.Multiple())
        {
            await Assert.That(cancelled.Session.StepJournal).IsEmpty();
            await Assert.That(dismissed.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(dismissed.OnlyStep.Preview).Contains("ContinueButton");
            await Assert.That(dismissed.OnlyStep.Preview).DoesNotContain("SelectComboItem");
        }
    }

    [Test]
    public async Task ConfirmedSelection_RemainsPersistableAfterResultsDisappear()
    {
        using var recorder = SingleSelectCaptureFixture.CreateEditableConfirmed();

        recorder.Start();
        recorder.Select("Search result");
        recorder.Confirm(removeResultsFirst: true);
        recorder.RetryOnlyStepValidation();

        using (Assert.Multiple())
        {
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(recorder.OnlyStep.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(recorder.OnlyStep.Preview).Contains("page.CategorySelector");
        }
    }

    [Test]
    public async Task ImmediateSelection_IsCapturedBeforePopupRemovesItsResults()
    {
        using var recorder = SingleSelectCaptureFixture.CreateImmediate(detachResultsOnSelection: true);

        recorder.Start();
        recorder.Select("Item 42");
        recorder.RetryOnlyStepValidation();

        using (Assert.Multiple())
        {
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(recorder.OnlyStep.Preview).Contains("page.CategorySelector");
        }
    }

    [Test]
    public async Task InputWithoutSelection_IsPersistedOnlyWhenExplicitlyConfigured()
    {
        using var searchOnly = SingleSelectCaptureFixture.CreateEditableConfirmed();
        searchOnly.Start();
        searchOnly.Type("item");
        searchOnly.Flush();

        using var editableValue = SingleSelectCaptureFixture.CreateEditableConfirmed(persistInputText: true);
        editableValue.Start();
        editableValue.Type("custom value");
        editableValue.Flush();

        using (Assert.Multiple())
        {
            await Assert.That(searchOnly.Session.StepJournal).IsEmpty();
            await Assert.That(editableValue.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(editableValue.OnlyStep.Preview).Contains("Page.EnterText");
        }
    }

    [Test]
    public async Task StandardComboBox_KeepsExistingCaptureBehavior()
    {
        using var recorder = SingleSelectCaptureFixture.CreateStandardComboBox();

        recorder.Start();
        recorder.Select("Search result");

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(recorder.OnlyStep.Preview)
                .Contains("Page.SelectComboItem(static page => page.StandardCategoryCombo, \"Search result\");");
        }
    }

    [Test]
    public async Task InvalidSemanticHint_LogsFailureAndKeepsPrimitiveSelection()
    {
        var logger = new RecorderCaptureTestLogger();
        using var recorder = SingleSelectCaptureFixture.CreateInvalidSemanticComboBox(logger);

        recorder.Start();
        recorder.Select("Search result");

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(recorder.OnlyStep.Preview)
                .Contains("Page.SelectComboItem(static page => page.StandardCategoryCombo, \"Search result\");");
            await Assert.That(logger.Entries.Any(static entry =>
                entry.EventId.Id == RecorderDiagnosticsEventIds.CaptureFailed.Id
                && entry.Message.Contains("could not be re-resolved", StringComparison.OrdinalIgnoreCase))).IsTrue();
        }
    }
}
