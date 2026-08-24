using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal sealed class MenuCaptureFixture : IDisposable
{
    private MenuCaptureFixture(
        RecorderSession session,
        MenuItem parent,
        MenuItem nestedLeaf,
        MenuItem directLeaf)
    {
        Session = session;
        Parent = parent;
        NestedLeaf = nestedLeaf;
        DirectLeaf = directLeaf;
    }

    public RecorderSession Session { get; }

    public MenuItem Parent { get; }

    public MenuItem NestedLeaf { get; }

    public MenuItem DirectLeaf { get; }

    public RecorderStepJournalEntry OnlyStep => Session.StepJournal.Single();

    public static MenuCaptureFixture Create(bool duplicateNestedLeaf = false)
    {
        var root = new StackPanel();
        var menu = new Menu();
        AutomationProperties.SetAutomationId(menu, "MainMenu");

        var actions = new MenuItem { Header = "_Actions" };
        var export = new MenuItem { Header = "_Export" };
        var snapshot = new MenuItem { Header = "Snapshot" };
        AutomationProperties.SetAutomationId(snapshot, "SnapshotMenuItem");
        export.Items.Add(snapshot);
        if (duplicateNestedLeaf)
        {
            export.Items.Add(new MenuItem { Header = "Snapshot" });
        }

        actions.Items.Add(export);
        var refresh = new MenuItem { Header = "Refresh" };
        AutomationProperties.SetAutomationId(refresh, "RefreshMenuItem");
        menu.Items.Add(actions);
        menu.Items.Add(refresh);
        root.Children.Add(menu);

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
        return new MenuCaptureFixture(session, actions, snapshot, refresh);
    }

    public void Start() => Session.Start();

    public void Invoke(MenuItem item, bool keyboard = false)
    {
        if (keyboard)
        {
            Session.RegisterKeyboardInputForTesting(item);
        }
        else
        {
            Session.RegisterPointerInputForTesting(item);
        }

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
    }

    public void Dispose() => Session.Dispose();
}
