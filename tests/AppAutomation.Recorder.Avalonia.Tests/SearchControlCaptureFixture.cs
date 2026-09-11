using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal sealed class SearchControlCaptureFixture : IDisposable
{
    private SearchControlCaptureFixture(RecorderSession session, TextBox input, StackPanel root)
    {
        Session = session;
        Input = input;
        Root = root;
    }

    public RecorderSession Session { get; }

    public TextBox Input { get; }

    private StackPanel Root { get; }

    public static SearchControlCaptureFixture Create(string initialText = "")
    {
        var input = WithAutomationId(new TextBox { Text = initialText }, "TableSearchInput");
        var root = WithAutomationId(new StackPanel(), "TableSearchRoot");
        root.Children.Add(input);

        var options = new AppAutomationRecorderOptions { ShowOverlay = false };
        options.SearchControlHints.Add(new RecorderSearchControlHint(
            "TableSearch",
            SearchControlParts.ByAutomationIds(
                "TableSearchInput",
                "SearchHistoryItemButton",
                historyResultsKind: SearchHistoryResultsKind.Buttons)));

        var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            () => root,
            attachWindowHandlers: false);
        session.AttachInputHandlersForTesting();
        session.Start();
        return new SearchControlCaptureFixture(session, input, root);
    }

    public void EnterText(string value)
    {
        Session.RegisterKeyboardInputForTesting(Input);
        Input.Text = value;
        Session.FlushPendingStateForTesting();
    }

    public void ApplyHistory(string value)
    {
        Session.RegisterKeyboardInputForTesting(Input);
        Input.Text = "typed manually";
        var item = WithAutomationId(new Button { Content = value }, "SearchHistoryItemButton");
        AutomationProperties.SetName(item, value);
        Root.Children.Add(item);
        Session.RefreshObservedControlsForTesting();
        Session.RegisterPointerInputForTesting(item);
        Input.Text = value;
        Session.CaptureButtonClickForTesting(item);
        Session.FlushPendingStateForTesting();
    }

    public void Dispose() => Session.Dispose();

    private static TControl WithAutomationId<TControl>(TControl control, string automationId)
        where TControl : Control
    {
        AutomationProperties.SetAutomationId(control, automationId);
        return control;
    }
}
