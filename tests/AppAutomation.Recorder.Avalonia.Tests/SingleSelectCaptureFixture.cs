using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal sealed class SingleSelectCaptureFixture : IDisposable
{
    private readonly Grid _root;

    private SingleSelectCaptureFixture(
        RecorderSession session,
        Grid root,
        Control results,
        TextBox? input,
        Button? confirmButton,
        Button? cancelButton,
        Button? unrelatedButton)
    {
        Session = session;
        _root = root;
        Results = results;
        Input = input;
        ConfirmButton = confirmButton;
        CancelButton = cancelButton;
        UnrelatedButton = unrelatedButton;
    }

    public RecorderSession Session { get; }

    public Control Results { get; }

    public TextBox? Input { get; }

    public Button? ConfirmButton { get; }

    public Button? CancelButton { get; }

    public Button? UnrelatedButton { get; }

    public RecorderStepJournalEntry OnlyStep => Session.StepJournal.Single();

    public static SingleSelectCaptureFixture CreateImmediate(bool detachResultsOnSelection = false)
    {
        var root = new Grid();
        root.Children.Add(CreateControl<Border>("CategorySelector"));
        var results = CreateControl<ComboBox>("CategorySelectorResults");
        results.ItemsSource = new[] { "Item 42", "Search result" };
        root.Children.Add(results);
        if (detachResultsOnSelection)
        {
            results.SelectionChanged += (_, _) => root.Children.Remove(results);
        }

        var options = CreateOptions();
        options.SingleSelectHints.Add(new RecorderSingleSelectHint(
            "CategorySelector",
            SingleSelectParts.ByAutomationIds(
                "CategorySelector",
                "CategorySelectorResults")));
        return CreateSession(root, results, input: null, confirmButton: null, cancelButton: null, unrelatedButton: null, options);
    }

    public static SingleSelectCaptureFixture CreateEditableConfirmed(bool persistInputText = false)
    {
        var root = new Grid();
        root.Children.Add(CreateControl<Border>("CategorySelector"));
        var input = CreateControl<TextBox>("CategorySelectorInput");
        var results = CreateControl<ListBox>("CategorySelectorResults");
        results.ItemsSource = new[] { "Item 42", "Search result" };
        var confirmButton = CreateControl<Button>("CategorySelectorConfirm");
        var cancelButton = CreateControl<Button>("CategorySelectorCancel");
        var unrelatedButton = CreateControl<Button>("ContinueButton");
        root.Children.Add(input);
        root.Children.Add(results);
        root.Children.Add(confirmButton);
        root.Children.Add(cancelButton);
        root.Children.Add(unrelatedButton);

        var options = CreateOptions();
        options.SingleSelectHints.Add(new RecorderSingleSelectHint(
            "CategorySelector",
            SingleSelectParts.ByAutomationIds(
                "CategorySelector",
                "CategorySelectorResults",
                inputAutomationId: "CategorySelectorInput",
                confirmButtonAutomationId: "CategorySelectorConfirm",
                cancelButtonAutomationId: "CategorySelectorCancel",
                resultsKind: SingleSelectResultsKind.ListBox,
                commitMode: SingleSelectCommitMode.Confirm,
                persistInputText: persistInputText)));
        return CreateSession(root, results, input, confirmButton, cancelButton, unrelatedButton, options);
    }

    public static SingleSelectCaptureFixture CreateStandardComboBox()
    {
        var root = new Grid();
        var results = CreateControl<ComboBox>("StandardCategoryCombo");
        results.ItemsSource = new[] { "Item 42", "Search result" };
        root.Children.Add(results);
        return CreateSession(
            root,
            results,
            input: null,
            confirmButton: null,
            cancelButton: null,
            unrelatedButton: null,
            CreateOptions());
    }

    public static SingleSelectCaptureFixture CreateInvalidSemanticComboBox(RecorderCaptureTestLogger logger)
    {
        var root = new Grid();
        var results = CreateControl<ComboBox>("StandardCategoryCombo");
        results.ItemsSource = new[] { "Item 42", "Search result" };
        root.Children.Add(results);

        var options = new AppAutomationRecorderOptions
        {
            ShowOverlay = false,
            Logger = logger,
            DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false }
        };
        options.SingleSelectHints.Add(new RecorderSingleSelectHint(
            "CategorySelector",
            SingleSelectParts.ByAutomationIds("CategorySelector", "StandardCategoryCombo")));
        return CreateSession(
            root,
            results,
            input: null,
            confirmButton: null,
            cancelButton: null,
            unrelatedButton: null,
            options);
    }

    public void Start() => Session.Start();

    public void Type(string text)
    {
        if (Input is null)
        {
            throw new InvalidOperationException("This single-selection editor does not have an input field.");
        }

        Session.RegisterKeyboardInputForTesting(Input);
        Input.Text = text;
    }

    public void Select(string item, bool keyboard = false)
    {
        if (keyboard)
        {
            Session.RegisterKeyboardInputForTesting(Results);
        }
        else
        {
            Session.RegisterPointerInputForTesting(Results);
        }

        switch (Results)
        {
            case ComboBox comboBox:
                comboBox.SelectedItem = item;
                break;
            case ListBox listBox:
                listBox.SelectedItem = item;
                break;
            default:
                throw new InvalidOperationException($"Unsupported results control '{Results.GetType().Name}'.");
        }
    }

    public void Confirm(bool removeResultsFirst = false)
    {
        if (removeResultsFirst)
        {
            _root.Children.Remove(Results);
        }

        Session.CaptureButtonClickForTesting(ConfirmButton);
    }

    public void Cancel() => Session.CaptureButtonClickForTesting(CancelButton);

    public void Dismiss() => Session.CaptureButtonClickForTesting(UnrelatedButton);

    public void Flush() => Session.FlushPendingStateForTesting();

    public void RetryOnlyStepValidation()
    {
        if (!Session.RetryStepValidation(OnlyStep.StepId))
        {
            throw new InvalidOperationException("The single-selection step could not be revalidated.");
        }
    }

    public void Dispose() => Session.Dispose();

    private static SingleSelectCaptureFixture CreateSession(
        Grid root,
        Control results,
        TextBox? input,
        Button? confirmButton,
        Button? cancelButton,
        Button? unrelatedButton,
        AppAutomationRecorderOptions options)
    {
        var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            () => root,
            attachWindowHandlers: false);
        session.AttachInputHandlersForTesting();
        return new SingleSelectCaptureFixture(session, root, results, input, confirmButton, cancelButton, unrelatedButton);
    }

    private static AppAutomationRecorderOptions CreateOptions()
    {
        return new AppAutomationRecorderOptions
        {
            ShowOverlay = false,
            DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false }
        };
    }

    private static TControl CreateControl<TControl>(string automationId)
        where TControl : Control, new()
    {
        var control = new TControl();
        AutomationProperties.SetAutomationId(control, automationId);
        return control;
    }
}
