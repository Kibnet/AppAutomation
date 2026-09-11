using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderSearchPickerCaptureTests
{
    [Test]
    public async Task MouseSelection_AfterTyping_RecordsSingleCompositeCommand()
    {
        using var recorder = SearchPickerCaptureFixture.CreateListBox();

        recorder.Start();
        recorder.Primary.TypeSearch("search");
        recorder.Primary.SelectByPointer("Search result");

        await AssertSingleCommand(
            recorder.Session,
            "Page.SearchAndSelect(static page => page.CustomerPicker, \"search\", \"Search result\");");
    }

    [Test]
    public async Task Selection_AfterDebounceWindow_StillUsesTypedSearch()
    {
        using var recorder = SearchPickerCaptureFixture.CreateListBox();

        recorder.Start();
        recorder.Primary.TypeSearch("search");
        Thread.Sleep(600);
        recorder.Primary.SelectByPointer("Search result");

        await AssertSingleCommand(
            recorder.Session,
            "Page.SearchAndSelect(static page => page.CustomerPicker, \"search\", \"Search result\");");
    }

    [Test]
    public async Task Selection_WithoutTyping_UsesCurrentSearchInput()
    {
        using var recorder = SearchPickerCaptureFixture.CreateListBox(initialSearchText: "customer");

        recorder.Start();
        recorder.Primary.SelectByPointer("Search result");

        await AssertSingleCommand(
            recorder.Session,
            "Page.SearchAndSelect(static page => page.CustomerPicker, \"customer\", \"Search result\");");
    }

    [Test]
    public async Task Selection_WithoutSearchText_UsesSelectedValueAsSearchText()
    {
        using var recorder = SearchPickerCaptureFixture.CreateListBox();

        recorder.Start();
        recorder.Primary.SelectByPointer("Item 42");

        await AssertSingleCommand(
            recorder.Session,
            "Page.SearchAndSelect(static page => page.CustomerPicker, \"Item 42\", \"Item 42\");");
    }

    [Test]
    public async Task SynchronousPopupDetach_RecordsPersistableLogicalCommand()
    {
        using var recorder = SearchPickerCaptureFixture.CreateListBox(
            initialSearchText: "customer",
            detachResultsOnSelection: true);

        recorder.Start();
        recorder.Primary.SelectByPointer("Search result");

        using (Assert.Multiple())
        {
            await AssertSingleCommand(
                recorder.Session,
                "Page.SearchAndSelect(static page => page.CustomerPicker, \"customer\", \"Search result\");");
            await Assert.That(recorder.Session.StepJournal[0].CanPersist).IsTrue();
            await Assert.That(recorder.Logger.Entries.Any(static entry =>
                entry.EventId.Id == RecorderDiagnosticsEventIds.SelectorValidationFailed.Id
                && entry.Message.Contains("CustomerPicker_Results", StringComparison.Ordinal))).IsFalse();
        }
    }

    [Test]
    public async Task KeyboardSelection_RecordsSameCompositeCommand()
    {
        using var recorder = SearchPickerCaptureFixture.CreateListBox(initialSearchText: "customer");

        recorder.Start();
        recorder.Primary.SelectByKeyboard("Search result");

        await AssertSingleCommand(
            recorder.Session,
            "Page.SearchAndSelect(static page => page.CustomerPicker, \"customer\", \"Search result\");");
    }

    [Test]
    public async Task MultiplePickers_UseTheHintMatchingTheSelectedResults()
    {
        using var recorder = SearchPickerCaptureFixture.CreateMultiple();

        recorder.Start();
        recorder.Secondary.SelectByPointer("Item 42");

        await AssertSingleCommand(
            recorder.Session,
            "Page.SearchAndSelect(static page => page.ProductPicker, \"product\", \"Item 42\");");
    }

    [Test]
    public async Task PendingTextFromAnotherPicker_IsNotUsedForSelection()
    {
        using var recorder = SearchPickerCaptureFixture.CreateMultiple();

        recorder.Start();
        recorder.Primary.TypeSearch("customer query");
        recorder.Secondary.SelectByPointer("Item 42");

        var selectionCommand = ExtractCommand(recorder.Session.StepJournal[^1].Preview);

        using (Assert.Multiple())
        {
            await Assert.That(selectionCommand).IsEqualTo(
                "Page.SearchAndSelect(static page => page.ProductPicker, \"product\", \"Item 42\");");
            await Assert.That(selectionCommand).DoesNotContain("customer query");
        }
    }

    [Test]
    public async Task ClosingPopupWithoutSelection_DoesNotRecordCommand()
    {
        using var recorder = SearchPickerCaptureFixture.CreateListBox(initialSearchText: "customer");

        recorder.Start();
        recorder.CloseWithoutSelection(recorder.Primary);

        await Assert.That(recorder.Session.StepJournal).IsEmpty();
    }

    [Test]
    public async Task UnconfiguredListBox_KeepsOrdinarySelectionCommand()
    {
        using var recorder = SearchPickerCaptureFixture.CreateListBox(
            logicalLocator: "AvailableItems",
            configured: false);

        recorder.Start();
        recorder.Primary.SelectByPointer("Item 42");

        await AssertSingleCommand(
            recorder.Session,
            "Page.SelectListBoxItem(static page => page.AvailableItems_Results, \"Item 42\");");
    }

    [Test]
    public async Task ComboBoxResults_RecordCompositeCommand()
    {
        using var recorder = SearchPickerCaptureFixture.CreateComboBox(initialSearchText: "customer");

        recorder.Start();
        recorder.Primary.SelectByPointer("Search result");

        await AssertSingleCommand(
            recorder.Session,
            "Page.SearchAndSelect(static page => page.CustomerPicker, \"customer\", \"Search result\");");
    }

    [Test]
    public async Task GeneratedCode_ContainsOnlyTheCompositeCommand()
    {
        using var recorder = SearchPickerCaptureFixture.CreateListBox();

        recorder.Start();
        recorder.Primary.TypeSearch("search");
        recorder.Session.CaptureButtonClickForTesting(recorder.Primary.OpenButton);
        recorder.Primary.SelectByPointer("Search result");

        var generatedCode = string.Join(
            Environment.NewLine,
            recorder.Session.StepJournal.Select(static step => ExtractCommand(step.Preview)));

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(generatedCode).IsEqualTo(
                "Page.SearchAndSelect(static page => page.CustomerPicker, \"search\", \"Search result\");");
            await Assert.That(generatedCode).DoesNotContain("Page.SetToggled");
            await Assert.That(generatedCode).DoesNotContain("Page.EnterText");
            await Assert.That(generatedCode).DoesNotContain("Page.SelectListBoxItem");
        }
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
