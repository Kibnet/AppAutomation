using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderCustomSearchPickerCaptureTests
{
    [Test]
    public async Task ConfirmedSelection_RecordsOneLogicalSearchCommand()
    {
        using var recorder = CustomSearchPickerCaptureFixture.Create();

        recorder.Start();
        recorder.Primary.TypeSearch("pickup");
        recorder.Primary.ConfirmSelection("Pickup option");

        await AssertSingleCommand(
            recorder.Session,
            "Page.SearchAndSelect(static page => page.LocationPicker, \"pickup\", \"Pickup option\");");
    }

    [Test]
    public async Task SelectionWithoutInput_UsesSelectedValueAsSearchText()
    {
        using var recorder = CustomSearchPickerCaptureFixture.Create();

        recorder.Start();
        recorder.Primary.ConfirmSelection("Pickup option");

        await AssertSingleCommand(
            recorder.Session,
            "Page.SearchAndSelect(static page => page.LocationPicker, \"Pickup option\", \"Pickup option\");");
    }

    [Test]
    public async Task InputWithoutConfirmedSelection_RemainsTextEntry()
    {
        using var recorder = CustomSearchPickerCaptureFixture.Create();

        recorder.Start();
        recorder.Primary.TypeSearch("pickup");
        recorder.Stop();

        await AssertSingleCommand(
            recorder.Session,
            "Page.EnterText(static page => page.LocationPicker_Input, \"pickup\");");
    }

    [Test]
    public async Task DetachedPopup_StillRecordsPersistableLogicalCommand()
    {
        using var recorder = CustomSearchPickerCaptureFixture.Create();

        recorder.Start();
        recorder.Primary.TypeSearch("pickup");
        recorder.DetachResults(recorder.Primary);
        recorder.Primary.ConfirmSelection("Pickup option");

        using (Assert.Multiple())
        {
            await AssertSingleCommand(
                recorder.Session,
                "Page.SearchAndSelect(static page => page.LocationPicker, \"pickup\", \"Pickup option\");");
            await Assert.That(recorder.Logger.Entries.Any(static entry =>
                entry.EventId.Id == RecorderDiagnosticsEventIds.SelectorValidationFailed.Id
                && entry.Message.Contains("LocationPicker_Results", StringComparison.Ordinal))).IsFalse();
        }
    }

    [Test]
    public async Task MultiplePickers_UseTheMatchingLogicalLocator()
    {
        using var recorder = CustomSearchPickerCaptureFixture.CreateMultiple();

        recorder.Start();
        recorder.Secondary.TypeSearch("secondary");
        recorder.Secondary.ConfirmSelection("Search result");

        await AssertSingleCommand(
            recorder.Session,
            "Page.SearchAndSelect(static page => page.SecondaryPicker, \"secondary\", \"Search result\");");
    }

    [Test]
    public async Task MissingHint_DoesNotConsumeInputText()
    {
        using var recorder = CustomSearchPickerCaptureFixture.Create(configured: false);

        recorder.Start();
        recorder.Primary.TypeSearch("pickup");
        recorder.Primary.ConfirmSelection("Pickup option");
        recorder.Stop();

        await AssertSingleCommand(
            recorder.Session,
            "Page.EnterText(static page => page.LocationPicker_Input, \"pickup\");");
    }

    [Test]
    public async Task AmbiguousHint_DoesNotConsumeInputText()
    {
        using var recorder = CustomSearchPickerCaptureFixture.Create(duplicateHint: true);

        recorder.Start();
        recorder.Primary.TypeSearch("pickup");
        recorder.Primary.ConfirmSelection("Pickup option");
        recorder.Stop();

        await AssertSingleCommand(
            recorder.Session,
            "Page.EnterText(static page => page.LocationPicker_Input, \"pickup\");");
    }

    [Test]
    public async Task ClosingWithoutConfirmation_DoesNotRecordSelection()
    {
        using var recorder = CustomSearchPickerCaptureFixture.Create();

        recorder.Start();
        recorder.DetachResults(recorder.Primary);

        await Assert.That(recorder.Session.StepJournal).IsEmpty();
    }

    [Test]
    public async Task DisposedRecorder_DetachesSelectionSource()
    {
        var recorder = CustomSearchPickerCaptureFixture.Create();

        recorder.Start();
        recorder.Dispose();
        recorder.Primary.ConfirmSelection("Pickup option");

        await Assert.That(recorder.Session.StepJournal).IsEmpty();
    }

    [Test]
    public async Task InternalInteractions_AreNotPersistedBesideCompositeCommand()
    {
        using var recorder = CustomSearchPickerCaptureFixture.Create();

        recorder.Start();
        recorder.Primary.TypeSearch("pickup");
        recorder.Session.CaptureButtonClickForTesting(recorder.Primary.OpenButton);
        recorder.Primary.ConfirmSelection("Pickup option");

        var generatedCode = string.Join(
            Environment.NewLine,
            recorder.Session.StepJournal.Select(static step => ExtractCommand(step.Preview)));

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(generatedCode).IsEqualTo(
                "Page.SearchAndSelect(static page => page.LocationPicker, \"pickup\", \"Pickup option\");");
            await Assert.That(generatedCode).DoesNotContain("Page.EnterText");
            await Assert.That(generatedCode).DoesNotContain("Page.Click");
            await Assert.That(generatedCode).DoesNotContain("Page.SelectListBoxItem");
            await Assert.That(generatedCode).DoesNotContain("Page.SelectComboItem");
        }
    }

    [Test]
    public async Task GeneratedCommand_CompilesAgainstExistingSearchPickerContract()
    {
        using var recorder = CustomSearchPickerCaptureFixture.Create();
        using var directory = new TemporaryDirectory();

        recorder.Start();
        recorder.Primary.TypeSearch("pickup");
        recorder.Primary.ConfirmSelection("Pickup option");
        var command = ExtractCommand(recorder.Session.StepJournal.Single().Preview);
        var source = $$"""
            using AppAutomation.Abstractions;

            public sealed class LocationPage : UiPage
            {
                public LocationPage(IUiControlResolver resolver) : base(resolver)
                {
                }

                public ISearchPickerControl LocationPicker => null!;
            }

            public sealed class RecordedScenario
            {
                private LocationPage Page => null!;

                public void Run()
                {
                    {{command}}
                }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "RecordedScenario.cs"), source);

        var errors = RecorderGeneratedSourceCompiler.Compile(directory.Path);

        await Assert.That(errors).IsEmpty();
    }

    private static async Task AssertSingleCommand(RecorderSession session, string expectedCommand)
    {
        using (Assert.Multiple())
        {
            await Assert.That(session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(ExtractCommand(session.StepJournal[0].Preview)).IsEqualTo(expectedCommand);
            await Assert.That(session.StepJournal[0].CanPersist).IsTrue();
        }
    }

    private static string ExtractCommand(string preview)
    {
        var commentIndex = preview.IndexOf(" //", StringComparison.Ordinal);
        return commentIndex < 0 ? preview : preview[..commentIndex];
    }
}
