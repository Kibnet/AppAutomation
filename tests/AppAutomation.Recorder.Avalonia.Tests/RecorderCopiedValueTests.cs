using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using Avalonia.Automation;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia.Tests;

[NotInParallel]
public sealed class RecorderCopiedValueTests
{
    [Test]
    public async Task CopiedValue_IsRecordedStandaloneAndReusedAsLocalVariableForPaste()
    {
        var source = TextBox("SourceValue", "Search result");
        var target = TextBox("TargetValue");
        var root = new StackPanel { Children = { source, target } };
        using var session = CreateSession(root);
        RecorderCopiedValueTargetSelection? selection = null;
        session.CopiedValueTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;
        session.Start();
        session.RefreshObservedControlsForTesting();

        session.BeginCopiedValueTargetSelection();
        session.SelectCopiedValueTargetForTesting(source);
        await session.CommitCopiedValueAsync(selection!, static (_, _) => Task.CompletedTask);

        session.RegisterCopiedValuePasteForTesting(target);
        session.RegisterKeyboardInputForTesting(target);
        target.Text = "Search result";
        session.FlushPendingStateForTesting();

        using (Assert.Multiple())
        {
            await Assert.That(session.StepCount).IsEqualTo(2);
            await Assert.That(session.PersistableStepCount).IsEqualTo(2);
            await Assert.That(session.CopiedValues.Count).IsEqualTo(1);
            await Assert.That(session.StepJournal[0].StatusMessage)
                .Contains("Copy SourceValue.Text");
            await Assert.That(session.StepJournal[1].StatusMessage)
                .Contains("Enter copied value");
            await Assert.That(session.StepJournal[1].CanPersist).IsTrue();
        }
    }

    [Test]
    public async Task ClipboardPaste_WithDifferentText_RemainsLiteralEnterText()
    {
        var source = TextBox("SourceValue", "Search result");
        var target = TextBox("TargetValue");
        var root = new StackPanel { Children = { source, target } };
        using var session = CreateSession(root);
        RecorderCopiedValueTargetSelection? selection = null;
        session.CopiedValueTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;
        session.Start();
        session.RefreshObservedControlsForTesting();
        session.BeginCopiedValueTargetSelection();
        session.SelectCopiedValueTargetForTesting(source);
        await session.CommitCopiedValueAsync(selection!, static (_, _) => Task.CompletedTask);

        session.RegisterCopiedValuePasteForTesting(target);
        session.RegisterKeyboardInputForTesting(target);
        target.Text = "Different text";
        session.FlushPendingStateForTesting();

        var preview = session.ExportPreview();
        using (Assert.Multiple())
        {
            await Assert.That(preview).Contains(
                "Page.EnterText(static page => page.TargetValue, \"Different text\");");
            await Assert.That(session.StepJournal[^1].CanPersist).IsTrue();
        }
    }

    [Test]
    public async Task CopiedValueGraph_RejectsForwardReferenceAndRemovalInvalidatesUse()
    {
        var copiedValueId = Guid.NewGuid();
        var definition = CopiedValueDefinition("SourceValue", copiedValueId);
        var use = CopiedValueUse("TargetValue", copiedValueId);
        var forward = RecorderScenarioGraphValidator.Validate([use, definition]);
        var root = new StackPanel();
        using var session = CreateSession(root);
        session.AddRecordedStepForTesting(definition);
        session.AddRecordedStepForTesting(use);
        session.RemoveStep(definition.StepId);

        using (Assert.Multiple())
        {
            await Assert.That(forward.Success).IsFalse();
            await Assert.That(forward.Error).Contains("missing or later copied value");
            await Assert.That(session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(session.StepJournal[0].CanPersist).IsFalse();
            await Assert.That(session.StepJournal[0].StatusMessage)
                .Contains("missing or later copied value");
        }
    }

    [Test]
    public async Task CopiedValueGraph_RejectsInputWithGeneratedAndCopiedSources()
    {
        var copiedValueId = Guid.NewGuid();
        var conflictingUse = CopiedValueUse("TargetValue", copiedValueId) with
        {
            GeneratedValueId = Guid.NewGuid()
        };

        var validation = RecorderScenarioGraphValidator.Validate(
            [CopiedValueDefinition("SourceValue", copiedValueId), conflictingUse]);

        using (Assert.Multiple())
        {
            await Assert.That(validation.Success).IsFalse();
            await Assert.That(validation.StepErrors[conflictingUse.StepId])
                .Contains("cannot use a generated value and a copied value at the same time");
        }
    }

    [Test]
    public async Task ClipboardCommit_SerializesChoicesAndBlocksStopOrClearUntilStepExists()
    {
        var source = TextBox("SourceValue", "Search result");
        using var session = CreateSession(source);
        RecorderCopiedValueTargetSelection? selection = null;
        session.CopiedValueTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;
        session.Start();
        session.RefreshObservedControlsForTesting();
        session.BeginCopiedValueTargetSelection();
        session.SelectCopiedValueTargetForTesting(source);
        var releaseWrite = new TaskCompletionSource();
        var writeCount = 0;

        var firstCommit = session.CommitCopiedValueAsync(
            selection!,
            (_, _) =>
            {
                writeCount++;
                return releaseWrite.Task;
            });
        var nextAction = RecorderTestSteps.CreateButtonClick("NextAction");
        session.AddRecordedStepForTesting(nextAction);
        var secondCommit = session.CommitCopiedValueAsync(
            selection!,
            (_, _) =>
            {
                writeCount++;
                return Task.CompletedTask;
            });
        session.Stop();
        session.Clear();

        using (Assert.Multiple())
        {
            await Assert.That(writeCount).IsEqualTo(1);
            await Assert.That(session.State).IsEqualTo(RecorderSessionState.Recording);
            await Assert.That(session.StepCount).IsEqualTo(2);
            await Assert.That(session.StepJournal[0].StatusMessage)
                .Contains("Copy SourceValue.Text");
            await Assert.That(session.StepJournal[1].StepId).IsEqualTo(nextAction.StepId);
        }

        releaseWrite.SetResult();
        await Task.WhenAll(firstCommit, secondCommit);

        using (Assert.Multiple())
        {
            await Assert.That(session.StepCount).IsEqualTo(2);
            await Assert.That(session.CopiedValues.Count).IsEqualTo(1);
            await Assert.That(session.StepJournal[0].CanPersist).IsTrue();
        }
    }

    [Test]
    public async Task ClipboardCommit_RollsBackReservedStepWhenClipboardWriteFails()
    {
        var source = TextBox("SourceValue", "Search result");
        using var session = CreateSession(source);
        RecorderCopiedValueTargetSelection? selection = null;
        session.CopiedValueTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;
        session.Start();
        session.RefreshObservedControlsForTesting();
        session.BeginCopiedValueTargetSelection();
        session.SelectCopiedValueTargetForTesting(source);

        await session.CommitCopiedValueAsync(
            selection!,
            static (_, _) => throw new InvalidOperationException("Clipboard unavailable"));

        using (Assert.Multiple())
        {
            await Assert.That(session.StepCount).IsEqualTo(0);
            await Assert.That(session.CopiedValues).IsEmpty();
            await Assert.That(session.LatestStatus).Contains("Clipboard unavailable");
        }
    }

    [Test]
    public async Task ClipboardCommit_DisposeCancelsContinuationWithoutEventsOrAutosave()
    {
        var source = TextBox("SourceValue", "Search result");
        var autosaveCount = 0;
        var session = CreateSession(
            source,
            autosaveOperation: (steps, _, _) =>
            {
                autosaveCount++;
                return Task.FromResult(RecorderSaveResult.Completed(
                    "Autosaved.",
                    pageFilePath: null,
                    scenarioFilePath: null,
                    persistedStepCount: steps.Count,
                    skippedStepCount: 0));
            });
        RecorderCopiedValueTargetSelection? selection = null;
        session.CopiedValueTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;
        session.Start();
        session.RefreshObservedControlsForTesting();
        session.BeginCopiedValueTargetSelection();
        session.SelectCopiedValueTargetForTesting(source);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = session.CommitCopiedValueAsync(
            selection!,
            (_, _) => releaseWrite.Task);
        var transientStep = RecorderTestSteps.CreateButtonClick("TransientAction");
        session.AddRecordedStepForTesting(transientStep);
        session.RemoveStep(transientStep.StepId);

        var eventCount = 0;
        session.SessionChanged += (_, _) => eventCount++;
        session.Dispose();
        var stepCountAfterDispose = session.StepCount;
        var statusAfterDispose = session.LatestStatus;

        releaseWrite.SetResult();
        await commit;

        using (Assert.Multiple())
        {
            await Assert.That(eventCount).IsEqualTo(0);
            await Assert.That(autosaveCount).IsEqualTo(0);
            await Assert.That(session.StepCount).IsEqualTo(stepCountAfterDispose);
            await Assert.That(session.LatestStatus).IsEqualTo(statusAfterDispose);
        }
    }

    [Test]
    public async Task Save_CopiedValueGraphGeneratesCompilableAsyncScenario()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage,
            RecorderScenarioDestinationSources.CompilableScenario);
        var context = project.CreateSaveContext("Copy value flow", "copy-value-draft");
        var copiedValueId = Guid.NewGuid();
        var steps = new[]
        {
            CopiedValueDefinition("SourceValue", copiedValueId),
            CopiedValueUse("TargetValue", copiedValueId)
        };
        var autosave = await project.AutosaveAsync(context, steps);
        var autosaveRead = RecorderAutosaveStateSerializer.TryRead(
            autosave.ScenarioFilePath!,
            out var autosaveState,
            out var autosaveError);
        var save = await project.SaveAsync(context, steps);
        var source = await File.ReadAllTextAsync(save.ScenarioFilePath!);
        var compileErrors = RecorderGeneratedSourceCompiler.Compile(project.RootPath);

        using (Assert.Multiple())
        {
            await Assert.That(autosaveRead).IsTrue();
            await Assert.That(autosaveError).IsNull();
            await Assert.That(autosaveState!.Steps[0].CopiedValueId).IsEqualTo(copiedValueId);
            await Assert.That(autosaveState.Steps[1].InputCopiedValueId).IsEqualTo(copiedValueId);
            await Assert.That(save.Success).IsTrue();
            await Assert.That(source).Contains("public async Task Recorded_CopyValueFlow_");
            await Assert.That(source).Contains(
                "var copiedSourceValue = Page.SourceValue.Text ?? string.Empty;");
            await Assert.That(source).Contains(
                "await Page.CopyTextToClipboardAsync(copiedSourceValue);");
            await Assert.That(source).Contains(
                "await Page.PasteTextFromClipboardAsync(static page => page.TargetValue, copiedSourceValue);");
            await Assert.That(compileErrors).IsEmpty();
        }
    }

    private static TextBox TextBox(string automationId, string? text = null)
    {
        var textBox = new TextBox { Text = text };
        AutomationProperties.SetAutomationId(textBox, automationId);
        return textBox;
    }

    private static RecorderSession CreateSession(
        Control root,
        Func<IReadOnlyList<RecordedStep>, string?, CancellationToken, Task<RecorderSaveResult>>? autosaveOperation = null)
    {
        return new RecorderSession(
            RecorderTestWindow.CreateStub(),
            new AppAutomationRecorderOptions
            {
                Validation = new RecorderValidationOptions
                {
                    ValidateSelectors = true,
                    ValidateRuntimeTargets = false,
                    CaptureInvalidSteps = true
                }
            },
            validationRootProvider: () => root,
            attachWindowHandlers: false,
            autosaveOperation: autosaveOperation);
    }

    private static RecordedStep CopiedValueDefinition(string automationId, Guid copiedValueId) =>
        new(
            RecordedActionKind.CaptureCopiedValue,
            Descriptor(automationId),
            StringValue: "Search result",
            ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text,
            CopiedValueId: copiedValueId,
            CopiedValueVariableName: "copiedSourceValue",
            StepId: Guid.NewGuid());

    private static RecordedStep CopiedValueUse(string automationId, Guid copiedValueId) =>
        new(
            RecordedActionKind.EnterText,
            Descriptor(automationId),
            StringValue: "Search result",
            InputCopiedValueId: copiedValueId,
            StepId: Guid.NewGuid());

    private static RecordedControlDescriptor Descriptor(string automationId) =>
        new(
            automationId,
            UiControlType.TextBox,
            automationId,
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: nameof(TextBox),
            Warning: null);
}
