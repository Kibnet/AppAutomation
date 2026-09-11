using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal sealed class SpinnerProxyCaptureFixture : IDisposable
{
    private readonly RecorderScenarioDestinationProject _project;
    private readonly Border _wrapper;

    private SpinnerProxyCaptureFixture(
        RecorderScenarioDestinationProject project,
        RecorderSession session,
        Border wrapper,
        TextBox input)
    {
        _project = project;
        _wrapper = wrapper;
        Session = session;
        Input = input;
    }

    public RecorderSession Session { get; }

    public TextBox Input { get; }

    public RecorderStepJournalEntry OnlyStep => Session.StepJournal.Single();

    public static SpinnerProxyCaptureFixture Create(string initialValue = "")
    {
        var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.MainWindowPage,
            RecorderScenarioDestinationSources.RootScenario);
        var options = CreateOptions(project.RootPath);
        var root = CreateSpinnerSurface(initialValue, out var wrapper, out var input);
        var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            () => root,
            attachWindowHandlers: false,
            autosaveOperation: CompleteAutosaveAsync);
        session.AttachInputHandlersForTesting();

        return new SpinnerProxyCaptureFixture(project, session, wrapper, input);
    }

    public void Start() => Session.Start();

    public void EnterValue(string value)
    {
        Session.RegisterKeyboardInputForTesting(Input);
        Input.Text = value;
        Session.FlushPendingStateForTesting();
    }

    public void CaptureValueAssertion()
    {
        Session.CaptureAssertionForTesting(Input, RecorderAssertionMode.Text);
    }

    public void ReplaceInteractivePart(Control replacement)
    {
        AutomationProperties.SetAutomationId(replacement, "QuantitySpinnerInput");
        _wrapper.Child = replacement;
    }

    public void RetryOnlyStepValidation()
    {
        if (!Session.RetryStepValidation(OnlyStep.StepId))
        {
            throw new InvalidOperationException("The recorded spinner step could not be revalidated.");
        }
    }

    public async Task<RecorderSaveResult> SaveAsync()
    {
        await WaitUntilIdleAsync();
        return await Session.SaveAsync();
    }

    public string ReadGeneratedScenario(RecorderSaveResult saveResult)
    {
        return File.ReadAllText(saveResult.ScenarioFilePath!);
    }

    public void Dispose()
    {
        Session.Dispose();
        _project.Dispose();
    }

    private static AppAutomationRecorderOptions CreateOptions(string projectDirectory)
    {
        var options = new AppAutomationRecorderOptions
        {
            AuthoringProjectDirectory = projectDirectory,
            OutputSubdirectory = "Recorded",
            PageNamespace = "Sample.Authoring.Pages",
            PageClassName = "MainWindowPage",
            ScenarioNamespace = "Sample.Authoring.Tests",
            ScenarioClassName = "Scenarios",
            ScenarioName = "Spinner proxy flow",
            ShowOverlay = false,
            DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false },
            Validation = new RecorderValidationOptions { CaptureInvalidSteps = true }
        };
        options.ConfigureSpinnerProxy("QuantitySpinner", "QuantitySpinnerInput");
        return options;
    }

    private static StackPanel CreateSpinnerSurface(
        string initialValue,
        out Border wrapper,
        out TextBox input)
    {
        var root = new StackPanel();
        wrapper = new Border();
        input = new TextBox { Text = initialValue };
        AutomationProperties.SetAutomationId(wrapper, "QuantitySpinner");
        AutomationProperties.SetAutomationId(input, "QuantitySpinnerInput");
        wrapper.Child = input;
        root.Children.Add(wrapper);
        return root;
    }

    private static Task<RecorderSaveResult> CompleteAutosaveAsync(
        IReadOnlyList<RecordedStep> steps,
        string? outputDirectory,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(RecorderSaveResult.Completed(
            "Autosaved.",
            pageFilePath: null,
            scenarioFilePath: null,
            persistedStepCount: steps.Count,
            skippedStepCount: 0));
    }

    private async Task WaitUntilIdleAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (Session.IsBusy)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for Recorder autosave to finish.");
            }

            await Task.Delay(20);
        }
    }
}
