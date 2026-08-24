using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal sealed class ExpanderCaptureFixture : IDisposable
{
    private ExpanderCaptureFixture(
        RecorderSession session,
        Expander expander,
        ToggleButton headerToggle)
    {
        Session = session;
        Expander = expander;
        HeaderToggle = headerToggle;
    }

    public RecorderSession Session { get; }

    public Expander Expander { get; }

    public ToggleButton HeaderToggle { get; }

    public RecorderStepJournalEntry OnlyStep => Session.StepJournal.Single();

    public static ExpanderCaptureFixture Create(bool initiallyExpanded = false)
    {
        var root = new Grid();
        var expander = new Expander
        {
            Header = "Details",
            IsExpanded = initiallyExpanded
        };
        AutomationProperties.SetAutomationId(expander, "DetailsExpander");
        root.Children.Add(expander);

        var headerToggle = new ToggleButton { Content = "Details" };
        SetTemplatedParent(headerToggle, expander);

        var options = new AppAutomationRecorderOptions
        {
            ShowOverlay = false,
            DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false }
        };
        var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            () => root,
            attachWindowHandlers: false);
        session.AttachInputHandlersForTesting();
        return new ExpanderCaptureFixture(session, expander, headerToggle);
    }

    public void Start() => Session.Start();

    public void SetExpanded(bool expanded, bool keyboard)
    {
        if (keyboard)
        {
            Session.RegisterKeyboardInputForTesting(HeaderToggle);
        }
        else
        {
            Session.RegisterPointerInputFromSourceForTesting(HeaderToggle);
        }

        Expander.IsExpanded = expanded;
    }

    public void CaptureHeaderClick() => Session.CaptureButtonClickForTesting(HeaderToggle);

    public void CaptureAssertion() => Session.CaptureAssertionForTesting(Expander, RecorderAssertionMode.Checked);

    public void Dispose() => Session.Dispose();

    private static void SetTemplatedParent(Control child, Control parent)
    {
        typeof(StyledElement)
            .GetProperty(nameof(StyledElement.TemplatedParent))!
            .SetValue(child, parent);
    }
}
