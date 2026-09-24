using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.TestHost;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

public sealed class HeadlessControlResolverTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    [NotInParallel("DesktopUi")]
    public async Task ResolveButton_IsEnabledTracksCommand(bool parentEnabled)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var command = new EnableableCommand();
        var button = HeadlessRuntime.Dispatch(() =>
        {
            var control = new global::Avalonia.Controls.Button { Command = command };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(control, "ActionButton");
            session.MainWindow.Content = new global::Avalonia.Controls.StackPanel
            {
                IsEnabled = parentEnabled,
                Children = { control }
            };
            return control;
        });
        var resolver = new HeadlessControlResolver(session.MainWindow);
        var resolvedButton = resolver.Resolve<IButtonControl>(new UiControlDefinition(
            "ActionButton", UiControlType.Button, "ActionButton"));

        foreach (var canExecute in new[] { false, true, false })
        {
            HeadlessRuntime.Dispatch(() => command.SetCanExecute(canExecute));

            using (Assert.Multiple())
            {
                await Assert.That(HeadlessRuntime.Dispatch(() => button.IsEnabled)).IsTrue();
                await Assert.That(resolvedButton.IsEnabled).IsEqualTo(canExecute && parentEnabled);
            }
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task ResolveControls_IsEnabledTracksDisabledAncestor()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var controls = HeadlessRuntime.Dispatch(() =>
        {
            var button = new global::Avalonia.Controls.Button();
            var input = new global::Avalonia.Controls.TextBox();
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(button, "ActionButton");
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(input, "ValueInput");
            var ancestor = new global::Avalonia.Controls.Border
            {
                Child = new global::Avalonia.Controls.StackPanel { Children = { button, input } }
            };
            session.MainWindow.Content = ancestor;
            return (Ancestor: ancestor, Button: button, Input: input);
        });
        var resolver = new HeadlessControlResolver(session.MainWindow);
        var resolvedButton = resolver.Resolve<IButtonControl>(new UiControlDefinition(
            "ActionButton", UiControlType.Button, "ActionButton"));
        var resolvedInput = resolver.Resolve<ITextBoxControl>(new UiControlDefinition(
            "ValueInput", UiControlType.TextBox, "ValueInput"));

        foreach (var enabled in new[] { false, true, false })
        {
            HeadlessRuntime.Dispatch(() => { controls.Ancestor.IsEnabled = enabled; });

            using (Assert.Multiple())
            {
                await Assert.That(HeadlessRuntime.Dispatch(() => controls.Button.IsEnabled)).IsTrue();
                await Assert.That(HeadlessRuntime.Dispatch(() => controls.Input.IsEnabled)).IsTrue();
                await Assert.That(resolvedButton.IsEnabled).IsEqualTo(enabled);
                await Assert.That(resolvedInput.IsEnabled).IsEqualTo(enabled);
            }
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [NotInParallel("DesktopUi")]
    public async Task ScopedLookup_RequiresOneMatchInsideScope(int matchCount)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        HeadlessRuntime.Dispatch(() =>
        {
            var scope = new global::Avalonia.Controls.StackPanel { Name = "SelectedScope" };
            global::Avalonia.Automation.AutomationProperties.SetName(scope, "SelectedScope");
            for (var index = 0; index < matchCount; index++)
            {
                var button = new global::Avalonia.Controls.Button { Name = "RepeatedItem" };
                global::Avalonia.Automation.AutomationProperties.SetName(button, "RepeatedItem");
                scope.Children.Add(button);
            }

            var outside = new global::Avalonia.Controls.Button { Name = "RepeatedItem" };
            global::Avalonia.Automation.AutomationProperties.SetName(outside, "RepeatedItem");
            session.MainWindow.Content = new global::Avalonia.Controls.StackPanel
            {
                Children = { scope, outside }
            };
            return true;
        });
        var resolver = new HeadlessControlResolver(session.MainWindow);
        var definition = new UiControlDefinition("SelectedButton", UiControlType.Button,
            "RepeatedItem", UiLocatorKind.Name)
        {
            Scope = new UiControlScope("SelectedScope", UiLocatorKind.Name)
        };

        if (matchCount == 1)
        {
            await Assert.That(resolver.Resolve<IButtonControl>(definition)).IsNotNull();
            return;
        }

        UiControlResolutionException? failure = null;
        try
        {
            resolver.Resolve<IButtonControl>(definition);
        }
        catch (UiControlResolutionException exception)
        {
            failure = exception;
        }
        await Assert.That(failure?.Failure).IsEqualTo(matchCount == 0
            ? UiControlResolutionFailure.NotFound : UiControlResolutionFailure.Ambiguous);
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task Resolve_DoesNotFallbackToName_ForAutomationIdLocator_WhenDisabled()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var resolver = new HeadlessControlResolver(session.MainWindow);
        var definition = new UiControlDefinition(
            "MathTabByName",
            UiControlType.TabItem,
            "Math",
            UiLocatorKind.AutomationId,
            FallbackToName: false);

        Exception? exception = null;
        try
        {
            resolver.Resolve<IUiControl>(definition);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        await Assert.That(exception is InvalidOperationException).IsEqualTo(true);
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task SelectTabItem_ByStableTabItemControl_SelectsHeadlessTab()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));

        page
            .SelectTabItem(static candidate => candidate.ControlMixTabItem)
            .WaitUntilIsSelected(static candidate => candidate.ControlMixTabItem);

        await Assert.That(page.ControlMixTabItem.IsSelected).IsEqualTo(true);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [NotInParallel("DesktopUi")]
    public async Task SelectListBoxItem_ByCapability_SelectsHeadlessItem(bool useAutomationName)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var entries = Enumerable.Range(1, 24).Select(index => new DisplayListEntry($"Item {index}")).ToArray();
        var list = HeadlessRuntime.Dispatch(() =>
        {
            var control = new global::Avalonia.Controls.ListBox
            {
                Height = 100,
                ItemsSource = entries,
                ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<DisplayListEntry>((entry, _) =>
                {
                    var text = new global::Avalonia.Controls.TextBlock
                    {
                        Text = useAutomationName ? "Visual decoration" : entry?.Caption
                    };
                    if (useAutomationName)
                    {
                        global::Avalonia.Automation.AutomationProperties.SetName(text, entry?.Caption);
                    }

                    return text;
                })
            };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(control, "ItemList");
            session.MainWindow.Content = control;
            session.MainWindow.Show();
            session.MainWindow.Dispatcher.RunJobs();
            session.MainWindow.UpdateLayout();
            return control;
        });
        var selectableList = new HeadlessControlResolver(session.MainWindow).Resolve<ISelectableListBoxControl>(
            new UiControlDefinition("ItemList", UiControlType.ListBox, "ItemList"));

        await Assert.That(selectableList.Items.Select(item => item.Text).ToArray())
            .IsEquivalentTo(entries.Select(entry => (string?)entry.Caption).ToArray());
        selectableList.SelectItem("Item 24");
        HeadlessRuntime.Dispatch(() => { list.ScrollIntoView(0); list.UpdateLayout(); });

        using (Assert.Multiple())
        {
            await Assert.That(selectableList.SelectedItemText).IsEqualTo("Item 24");
            await Assert.That(HeadlessRuntime.Dispatch(() => list.SelectedItem)).IsSameReferenceAs(entries[23]);
        }

        HeadlessRuntime.Dispatch(() => { list.ItemsSource = entries.Append(new DisplayListEntry("Item 24")).ToArray(); });
        var duplicate = Assert.Throws<InvalidOperationException>(() => selectableList.SelectItem("Item 24"));
        var exactDuplicate = Assert.Throws<InvalidOperationException>(() =>
            ((IExactSelectableListBoxControl)selectableList).SelectItemExact("Item 24"));
        await Assert.That(duplicate.Message).Contains("ambiguous");
        await Assert.That(exactDuplicate.Message).Contains("ambiguous");
        await Assert.That(HeadlessRuntime.Dispatch(() => list.SelectedItem)).IsSameReferenceAs(entries[23]);
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task MultiSelectItemsSurface_UsesComboBoxAsZeroOrOneValueSet()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var resolver = new HeadlessControlResolver(session.MainWindow);
        var items = resolver.Resolve<IMultiSelectItemsControl>(new UiControlDefinition(
            "OperationFilterItems",
            UiControlType.ComboBox,
            "OperationCombo"));

        items.SetSelectedItems(["LCM"]);
        var selected = items.SelectedItems;
        items.SetSelectedItems([]);

        using (Assert.Multiple())
        {
            await Assert.That(selected).IsEquivalentTo(["LCM"]);
            await Assert.That(items.SelectedItems).IsEmpty();
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task ResolveAvaloniaDataGrid_ByAutomationId_ReadsBoundRowsAndCells()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));

        page
            .SelectTabItem(static candidate => candidate.DataGridTabItem)
            .EnterText(static candidate => candidate.DataGridRowsInput, "5")
            .ClickButton(static candidate => candidate.BuildGridButton)
            .WaitUntilNameEquals(static candidate => candidate.GridResultLabel, "Grid rows: 5")
            .WaitUntilGridRowsAtLeast(static candidate => candidate.DemoDataGrid, 5)
            .WaitUntilGridCellEquals(
                static candidate => candidate.DemoDataGrid,
                GridRowSelector.ByCell("Row", "R3"),
                "Value",
                "13")
            .WaitUntilGridCellEquals(
                static candidate => candidate.DemoDataGrid,
                GridRowSelector.ByCell("Row", "R3"),
                "Parity",
                "Odd");

        using (Assert.Multiple())
        {
            await Assert.That(page.DemoDataGrid is IAddressableGridControl).IsTrue();
            await Assert.That(page.DemoDataGrid is IGridColumnMetadataControl).IsTrue();
            await Assert.That(page.DemoDataGrid.Rows.Count).IsGreaterThanOrEqualTo(5);
            await Assert.That(page.DemoDataGrid.GetRowByIndex(2)!.Cells[0].Value).IsEqualTo("R3");
            await Assert.That(page.DemoDataGrid.GetRowByIndex(2)!.Cells[1].Value).IsEqualTo("13");
            await Assert.That(page.DemoDataGrid.GetRowByIndex(2)!.Cells[2].Value).IsEqualTo("Odd");
        }
    }

    private sealed class DisplayListEntry(string caption)
    {
        public string Caption { get; } = caption;
    }

    private sealed class EnableableCommand : global::System.Windows.Input.ICommand
    {
        private bool _canExecute;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute;

        public void Execute(object? parameter) => throw new InvalidOperationException("This test only reads availability.");

        public void SetCanExecute(bool value)
        {
            _canExecute = value;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
