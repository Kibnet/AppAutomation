using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderColorPickerCaptureTests
{
    [Test]
    [Arguments(ColorPaletteKind.ListBox, false)]
    [Arguments(ColorPaletteKind.ListBox, true)]
    [Arguments(ColorPaletteKind.ComboBox, false)]
    [Arguments(ColorPaletteKind.ComboBox, true)]
    public async Task ImmediatePaletteSelection_RecordsOneCanonicalAction(
        ColorPaletteKind paletteKind,
        bool keyboard)
    {
        using var recorder = ColorPickerCaptureFixture.Create(ColorPickerCommitMode.Immediate, paletteKind);

        recorder.Start();
        recorder.SelectPalette("#FF336699", keyboard);

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(recorder.OnlyStep.Preview)
                .Contains("Page.SetColor(static page => page.AccentColor, \"#FF336699\");");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("SelectListBoxItem");
        }
    }

    [Test]
    public async Task ConfirmedPaletteSelection_WaitsForApplyAndCancelCreatesNoStep()
    {
        using var applied = ColorPickerCaptureFixture.Create();
        applied.Start();
        applied.SelectPalette("#80224466");
        await Assert.That(applied.Session.StepJournal).IsEmpty();
        applied.Apply();

        using var cancelled = ColorPickerCaptureFixture.Create();
        cancelled.Start();
        cancelled.SelectPalette("#FF336699");
        cancelled.Dismiss();

        using var unchanged = ColorPickerCaptureFixture.Create();
        unchanged.Start();
        unchanged.Apply();

        using (Assert.Multiple())
        {
            await Assert.That(applied.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(applied.OnlyStep.Preview).Contains("\"#80224466\"");
            await Assert.That(cancelled.Session.StepJournal).IsEmpty();
            await Assert.That(unchanged.Session.StepJournal).IsEmpty();
        }
    }

    [Test]
    public async Task CustomInputWithoutConfirmation_CreatesNoPrimitiveTextStep()
    {
        using var recorder = ColorPickerCaptureFixture.Create();

        recorder.Start();
        recorder.EnterCustomValue("#FF336699");
        recorder.Session.FlushPendingStateForTesting();

        await Assert.That(recorder.Session.StepJournal).IsEmpty();
    }

    [Test]
    public async Task CustomSelectionSource_SurvivesSynchronousPopupRemoval()
    {
        using var recorder = ColorPickerCaptureFixture.Create();

        recorder.Start();
        recorder.ConfirmCustomSelection("#336699");
        recorder.DetachPopup();

        using (Assert.Multiple())
        {
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(recorder.OnlyStep.Preview).Contains("\"#FF336699\"");
        }
    }

    [Test]
    public async Task Assertion_UsesLogicalPickerAndCanonicalCurrentValue()
    {
        using var recorder = ColorPickerCaptureFixture.Create();
        recorder.CurrentValue.Text = "#336699";

        recorder.Start();
        recorder.CaptureAssertion();

        await Assert.That(recorder.OnlyStep.Preview)
            .Contains("Page.WaitUntilColorEquals(static page => page.AccentColor, \"#FF336699\");");
    }

    [Test]
    public async Task UnconfiguredListBox_KeepsOrdinarySelectionBehavior()
    {
        using var recorder = ColorPickerCaptureFixture.Create();
        recorder.Session.Start();
        var ordinary = new global::Avalonia.Controls.ListBox
        {
            ItemsSource = new[] { "Item 42" },
            SelectedItem = "Item 42"
        };
        global::Avalonia.Automation.AutomationProperties.SetAutomationId(ordinary, "OrdinaryList");

        var result = new RecorderStepFactory(new AppAutomationRecorderOptions())
            .TryCreateListBoxStep(ordinary);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.SelectListBoxItem);
        }
    }

    [Test]
    public async Task InvalidSemanticColor_LogsFailureAndKeepsPrimitiveSelection()
    {
        var logger = new RecorderCaptureTestLogger();
        using var recorder = ColorPickerCaptureFixture.CreateInvalidSemanticPalette(logger);

        recorder.Start();
        recorder.SelectPalette("Item 42");

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(recorder.OnlyStep.Preview)
                .Contains("Page.SelectListBoxItem(static page => page.AccentColorPalette, \"Item 42\");");
            await Assert.That(logger.Entries.Any(static entry =>
                entry.EventId.Id == RecorderDiagnosticsEventIds.CaptureFailed.Id
                && entry.Message.Contains("not a valid", StringComparison.OrdinalIgnoreCase))).IsTrue();
        }
    }

    [Test]
    public async Task ConfiguredGridColorEdit_RecordsCanonicalSemanticAction()
    {
        var value = new TextBox { Text = "#336699" };
        var commit = new Button();
        AutomationProperties.SetAutomationId(value, "GridColorValue");
        AutomationProperties.SetAutomationId(commit, "GridColorCommit");
        var root = new StackPanel { Children = { value, commit } };
        var options = new AppAutomationRecorderOptions();
        options.GridEditHints.Add(new RecorderGridEditHint(
            "GridColorCommit",
            "ItemsGrid",
            "GridColorValue",
            0,
            2,
            GridCellEditorKind.Color));

        var result = new RecorderStepFactory(options, () => root).TryCreateGridEditStep(commit);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.EditGridCellColor);
            await Assert.That(result.Step.StringValue).IsEqualTo("#FF336699");
        }
    }
}
