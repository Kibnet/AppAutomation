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
        MenuItem directLeaf,
        Button primaryOwner,
        Button secondaryOwner,
        MenuItem primaryContextLeaf,
        MenuItem nestedContextLeaf,
        MenuItem secondaryContextLeaf)
    {
        Session = session;
        Parent = parent;
        NestedLeaf = nestedLeaf;
        DirectLeaf = directLeaf;
        PrimaryOwner = primaryOwner;
        SecondaryOwner = secondaryOwner;
        PrimaryContextLeaf = primaryContextLeaf;
        NestedContextLeaf = nestedContextLeaf;
        SecondaryContextLeaf = secondaryContextLeaf;
    }

    public RecorderSession Session { get; }

    public MenuItem Parent { get; }

    public MenuItem NestedLeaf { get; }

    public MenuItem DirectLeaf { get; }

    public Button PrimaryOwner { get; }

    public Button SecondaryOwner { get; }

    public MenuItem PrimaryContextLeaf { get; }

    public MenuItem NestedContextLeaf { get; }

    public MenuItem SecondaryContextLeaf { get; }

    public RecorderStepJournalEntry OnlyStep => Session.StepJournal.Single();

    public static MenuCaptureFixture Create(
        bool duplicateMenuLeaf = false,
        bool duplicateContextLeaf = false)
    {
        var root = new StackPanel();
        var menu = new Menu();
        AutomationProperties.SetAutomationId(menu, "MainMenu");

        var actions = new MenuItem { Header = "_Actions" };
        var export = new MenuItem { Header = "_Export" };
        var snapshot = new MenuItem { Header = new TextBlock { Text = "Snapshot" } };
        AutomationProperties.SetAutomationId(snapshot, "SnapshotMenuItem");
        SetItems(
            export,
            duplicateMenuLeaf
                ? new[] { snapshot, new MenuItem { Header = "Snapshot" } }
                : new[] { snapshot });
        SetItems(actions, export);
        var refresh = new MenuItem { Header = "Refresh" };
        AutomationProperties.SetAutomationId(refresh, "RefreshMenuItem");
        SetItems(menu, actions, refresh);
        root.Children.Add(menu);

        var primaryContextLeaf = new MenuItem { Header = "Pin" };
        var nestedContextLeaf = new MenuItem { Header = "Summary" };
        var contextExport = new MenuItem { Header = "Export" };
        SetItems(
            contextExport,
            duplicateContextLeaf
                ? new[] { nestedContextLeaf, new MenuItem { Header = "Summary" } }
                : new[] { nestedContextLeaf });

        var primaryOwner = CreateOwner("ItemSurface", primaryContextLeaf, contextExport);
        var secondaryContextLeaf = new MenuItem { Header = "Pin" };
        var secondaryOwner = CreateOwner("SecondarySurface", secondaryContextLeaf);
        root.Children.Add(primaryOwner);
        root.Children.Add(secondaryOwner);

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
        return new MenuCaptureFixture(
            session,
            actions,
            snapshot,
            refresh,
            primaryOwner,
            secondaryOwner,
            primaryContextLeaf,
            nestedContextLeaf,
            secondaryContextLeaf);
    }

    public void Start() => Session.Start();

    public void InvokeMenuItem(MenuItem item, bool keyboard = false)
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

    public void InvokeContextMenuItem(Button owner, MenuItem item, bool keyboard = false)
    {
        Session.RegisterContextMenuOwnerForTesting(owner, keyboard);
        if (!keyboard)
        {
            Session.RegisterContextMenuItemPointerForTesting(item);
        }

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
    }

    public void CancelContextMenu(Button owner)
    {
        Session.RegisterContextMenuOwnerForTesting(owner);
        Session.CancelContextMenuForTesting();
    }

    public void Dispose() => Session.Dispose();

    private static Button CreateOwner(string automationId, params MenuItem[] items)
    {
        var owner = new Button { Content = automationId };
        AutomationProperties.SetAutomationId(owner, automationId);
        owner.ContextMenu = new ContextMenu();
        SetItems(owner.ContextMenu, items);

        return owner;
    }

    private static void SetItems(ItemsControl owner, params MenuItem[] items)
    {
        owner.ItemsSource = items;
        foreach (var item in items)
        {
            ((ISetLogicalParent)item).SetParent(owner);
        }
    }
}
