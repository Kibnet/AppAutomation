using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal sealed class TimePickerCaptureFixture : IDisposable
{
    private readonly Grid _root;

    private TimePickerCaptureFixture(
        RecorderSession session,
        Grid root,
        TimePicker timePicker,
        TextBox? input,
        Button? confirmButton,
        Button? cancelButton,
        Button? unrelatedButton)
    {
        Session = session;
        _root = root;
        TimePicker = timePicker;
        Input = input;
        ConfirmButton = confirmButton;
        CancelButton = cancelButton;
        UnrelatedButton = unrelatedButton;
    }

    public RecorderSession Session { get; }

    public TimePicker TimePicker { get; }

    public TextBox? Input { get; }

    public Button? ConfirmButton { get; }

    public Button? CancelButton { get; }

    public Button? UnrelatedButton { get; }

    public RecorderStepJournalEntry OnlyStep => Session.StepJournal.Single();

    public static TimePickerCaptureFixture CreateStandard()
    {
        var root = new Grid();
        var timePicker = CreateControl<TimePicker>("StartTimePicker");
        root.Children.Add(timePicker);
        return CreateSession(
            root,
            timePicker,
            input: null,
            confirmButton: null,
            cancelButton: null,
            unrelatedButton: null,
            options: CreateOptions());
    }

    public static TimePickerCaptureFixture CreateConfirmedComposite()
    {
        var root = new Grid();
        root.Children.Add(CreateControl<Border>("DeliveryTimeEditor"));
        var input = CreateControl<TextBox>("DeliveryTimeInput");
        var timePicker = CreateControl<TimePicker>("DeliveryTimeSurface");
        var confirmButton = CreateControl<Button>("DeliveryTimeConfirm");
        var cancelButton = CreateControl<Button>("DeliveryTimeCancel");
        var unrelatedButton = CreateControl<Button>("ContinueButton");
        root.Children.Add(input);
        root.Children.Add(timePicker);
        root.Children.Add(confirmButton);
        root.Children.Add(cancelButton);
        root.Children.Add(unrelatedButton);

        var options = CreateOptions();
        options.TimePickerHints.Add(new RecorderTimePickerHint(
            "DeliveryTimeEditor",
            TimePickerParts.ByAutomationIds(
                "DeliveryTimeEditor",
                "DeliveryTimeSurface",
                inputAutomationId: "DeliveryTimeInput",
                confirmButtonAutomationId: "DeliveryTimeConfirm",
                cancelButtonAutomationId: "DeliveryTimeCancel",
                commitMode: TimePickerCommitMode.Confirm)));
        return CreateSession(root, timePicker, input, confirmButton, cancelButton, unrelatedButton, options);
    }

    public void Start() => Session.Start();

    public void EnterInternalText(string text)
    {
        if (Input is null)
        {
            throw new InvalidOperationException("This time picker does not have a configured input.");
        }

        Session.RegisterKeyboardInputForTesting(Input);
        Input.Text = text;
    }

    public void Select(TimeSpan value, bool keyboard)
    {
        if (keyboard)
        {
            Session.RegisterKeyboardInputForTesting(TimePicker);
        }
        else
        {
            Session.RegisterPointerInputForTesting(TimePicker);
        }

        TimePicker.SelectedTime = value;
    }

    public void Confirm(bool removePopupFirst = false)
    {
        if (Input is not null)
        {
            Input.Text = TimePicker.SelectedTime?.ToString("c") ?? string.Empty;
        }

        if (removePopupFirst)
        {
            _root.Children.Remove(TimePicker);
        }

        Session.CaptureButtonClickForTesting(ConfirmButton);
    }

    public void Cancel() => Session.CaptureButtonClickForTesting(CancelButton);

    public void DismissByClickingElsewhere() => Session.CaptureButtonClickForTesting(UnrelatedButton);

    public void CaptureAssertion() => Session.CaptureAssertionForTesting(
        Input is not null ? Input : TimePicker,
        RecorderAssertionMode.Text);

    public void RetryOnlyStepValidation()
    {
        if (!Session.RetryStepValidation(OnlyStep.StepId))
        {
            throw new InvalidOperationException("The time picker step could not be revalidated.");
        }
    }

    public void Dispose() => Session.Dispose();

    private static TimePickerCaptureFixture CreateSession(
        Grid root,
        TimePicker timePicker,
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
        return new TimePickerCaptureFixture(session, root, timePicker, input, confirmButton, cancelButton, unrelatedButton);
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
