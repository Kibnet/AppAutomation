using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.TestHost;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

public sealed class HeadlessControlResolverTests
{

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [NotInParallel("DesktopUi")]
    public async Task MultilineGridText_CommitsExactValueBeforeTheNextNumericEdit(bool remoteCommitTarget)
    {
        const string multilineVariable = "APPAUTOMATION_GRID_MULTILINE_TEXT";
        const string targetVariable = "APPAUTOMATION_GRID_REMOTE_COMMIT_TARGET";
        var previousMultiline = Environment.GetEnvironmentVariable(multilineVariable);
        var previousTarget = Environment.GetEnvironmentVariable(targetVariable);
        Environment.SetEnvironmentVariable(multilineVariable, "1");
        Environment.SetEnvironmentVariable(targetVariable, remoteCommitTarget ? "1" : null);
        try
        {
            using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
            HeadlessRuntime.Dispatch(() => session.MainWindow.Show());
            var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow)
                .WithGridAutomation(global::DotnetDebug.AppAutomation.Configuration.SampleGridAutomation
                    .CreateMultilineFixtureCatalog(remoteCommitTarget)));
            page.SelectTabItem(static candidate => candidate.DataGridTabItem);
            var row = GridRowSelector.ByCell("Key", "ARM-02");
            const string text = "Box with a label";
            page.EditGridCellText(static candidate => candidate.ArmComplexDataGridControl, row, "Value", text);
            await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl, row, "Value"))
                .IsEqualTo(text);
            page.EditGridCellNumber(static candidate => candidate.ArmComplexDataGridControl, row, "RequiredAmount", 1500);
            await Assert.That(GridValueReader.ReadCellNumber(page.ArmComplexDataGridControl, row, "RequiredAmount"))
                .IsEqualTo(1500d);
            await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl, row, "Value"))
                .IsEqualTo(text);
            await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
                GridRowSelector.ByCell("Key", "ARM-01"), "Value")).IsEqualTo("Value-1");
        }
        finally
        {
            Environment.SetEnvironmentVariable(multilineVariable, previousMultiline);
            Environment.SetEnvironmentVariable(targetVariable, previousTarget);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [NotInParallel("DesktopUi")]
    public async Task SearchHistory_ReopensAfterClearWithOrWithoutInputFocus(bool inputAlreadyFocused)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var applied = 0;
        var history = new List<string>();
        var controls = HeadlessRuntime.Dispatch(() =>
        {
            var input = new TextBox { Width = 240 };
            var search = new Button { Content = "Search" };
            var items = new StackPanel();
            var root = new Border { Child = items, Width = 240 };
            var popup = new global::Avalonia.Controls.Primitives.Popup { Child = root, PlacementTarget = input };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(input, "HistoryInput");
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(search, "RunSearch");
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(root, "HistoryRoot");
            input.GotFocus += (_, _) => popup.IsOpen = history.Count > 0;
            input.PointerReleased += (_, _) => popup.IsOpen = history.Count > 0;
            search.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(input.Text) && !history.Contains(input.Text))
                {
                    var value = input.Text;
                    history.Add(value);
                    var item = new Button { Content = value };
                    global::Avalonia.Automation.AutomationProperties.SetAutomationId(item, "HistoryItem");
                    item.Click += (_, _) => { applied++; input.Text = value; popup.IsOpen = false; };
                    items.Children.Add(item);
                }
                popup.IsOpen = false;
            };
            var panel = new StackPanel();
            panel.Children.Add(input);
            panel.Children.Add(search);
            panel.Children.Add(popup);
            session.MainWindow.Content = panel;
            session.MainWindow.Show();
            session.MainWindow.Dispatcher.RunJobs();
            return (Input: input, Search: search, Popup: popup);
        });
        var resolver = new HeadlessControlResolver(session.MainWindow).WithSearchControl(
            "ArmTableSearch", SearchControlParts.ByAutomationIds(
                "HistoryInput", "HistoryItem", searchButtonAutomationId: "RunSearch",
                historyRootAutomationId: "HistoryRoot"));
        var page = new MainWindowPage(resolver);
        page.EnterSearch(static candidate => candidate.ArmTableSearch, "saved-order");
        page.ClearSearch(static candidate => candidate.ArmTableSearch);
        HeadlessRuntime.Dispatch(() =>
        {
            if (!inputAlreadyFocused) controls.Search.Focus();
        });
        await Assert.That(HeadlessRuntime.Dispatch(() => controls.Popup.IsOpen)).IsFalse();
        page.ApplySearchFromHistory(static candidate => candidate.ArmTableSearch, "saved-order");
        await Assert.That(page.ArmTableSearch.Text).IsEqualTo("saved-order");
        await Assert.That(page.ArmTableSearch.IsHistoryOpen).IsFalse();
        await Assert.That(applied).IsEqualTo(1);
        page.ClearSearch(static candidate => candidate.ArmTableSearch);
        page.ApplySearchFromHistory(static candidate => candidate.ArmTableSearch, "saved-order");
        await Assert.That(applied).IsEqualTo(2);
        await Assert.That(history).IsEquivalentTo(["saved-order"]);
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task DisabledCustomSingleSelect_ReadsCurrentPublishedValue()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var editor = HeadlessRuntime.Dispatch(() =>
        {
            var control = new global::Avalonia.Controls.Border { IsEnabled = false };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(control, "ReadOnlySelection");
            global::Avalonia.Automation.AutomationProperties.SetName(control, "Ready for production");
            session.MainWindow.Content = control;
            session.MainWindow.Show();
            session.MainWindow.Dispatcher.RunJobs();
            return control;
        });
        var resolver = new HeadlessControlResolver(session.MainWindow).WithSingleSelect(
            "ReadOnlySelection",
            SingleSelectParts.ByAutomationIds(
                "ReadOnlySelection",
                "UnavailableResults",
                selectedValueAutomationId: "ReadOnlySelection",
                resultsKind: SingleSelectResultsKind.ListBox) with { FallbackToName = false });
        var selection = resolver.Resolve<IComboBoxControl>(new UiControlDefinition(
            "ReadOnlySelection", UiControlType.ComboBox, "ReadOnlySelection"));

        await Assert.That(selection.IsEnabled).IsEqualTo(false);
        await Assert.That(selection.SelectedItem?.Text).IsEqualTo("Ready for production");

        HeadlessRuntime.Dispatch(() =>
            global::Avalonia.Automation.AutomationProperties.SetName(editor, "Released"));
        await Assert.That(selection.SelectedItem?.Text).IsEqualTo("Released");

        HeadlessRuntime.Dispatch(() =>
            global::Avalonia.Automation.AutomationProperties.SetName(editor, string.Empty));
        await Assert.That(selection.SelectedItem).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [NotInParallel("DesktopUi")]
    public async Task FlyoutButtonInvocationTogglesDetachedPanelAvailability(bool isDropDownButton)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        global::Avalonia.Controls.Flyout? flyout = null;
        HeadlessRuntime.Dispatch(() =>
        {
            var panel = new global::Avalonia.Controls.Border
            {
                Child = new global::Avalonia.Controls.TextBlock { Text = "Filter content" }
            };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(panel, "FilterPanel");
            flyout = new global::Avalonia.Controls.Flyout { Content = panel };
            global::Avalonia.Controls.Button button = isDropDownButton
                ? new global::Avalonia.Controls.DropDownButton()
                : new global::Avalonia.Controls.Button();
            button.Content = "Filters";
            button.Flyout = flyout;
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(button, "FilterButton");
            session.MainWindow.Content = button;
        });
        var resolver = new HeadlessControlResolver(session.MainWindow);
        var buttonControl = resolver.Resolve<IButtonControl>(new UiControlDefinition(
            "FilterButton", UiControlType.Button, "FilterButton"));

        buttonControl.Invoke();

        await Assert.That(HeadlessRuntime.Dispatch(() => flyout!.IsOpen)).IsEqualTo(true);
        var panelControl = resolver.Resolve<IUiControlAvailability>(new UiControlDefinition(
            "FilterPanel", UiControlType.AutomationElement, "FilterPanel"));
        await Assert.That(panelControl.AutomationId).IsEqualTo("FilterPanel");
        await Assert.That(panelControl.IsAvailable).IsEqualTo(true);

        buttonControl.Invoke();

        await Assert.That(HeadlessRuntime.Dispatch(() => flyout!.IsOpen)).IsEqualTo(false);
        await Assert.That(panelControl.IsAvailable).IsEqualTo(false);

        buttonControl.Invoke();

        await Assert.That(HeadlessRuntime.Dispatch(() => flyout!.IsOpen)).IsEqualTo(true);
        await Assert.That(panelControl.IsAvailable).IsEqualTo(true);
    }

    [Test]
    [Arguments("prefix", 2, "ORDER-02")]
    [Arguments("reordered", 21, null)]
    [Arguments("sorted-view", 38, "ORDER-21")]
    [Arguments("sorted-view", 0, null)]
    [NotInParallel("DesktopUi")]
    public async Task VirtualizedGridUsesOnlyProvenCurrentViewOrder(
        string scenario,
        int position,
        string? expectedKey)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var rows = Enumerable.Range(0, 60)
            .Select(index => new VirtualOrderRow($"ORDER-{index:D2}"))
            .ToArray();
        HeadlessRuntime.Dispatch(() =>
        {
            var grid = new VirtualOrdersGrid
            {
                ItemsSource = rows,
                CollectionView = scenario == "sorted-view" ? rows.Reverse().ToArray() : null,
                Width = 240,
                Height = 90
            };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(grid, "VirtualOrdersGrid");
            var visibleRows = scenario == "prefix"
                ? rows.Take(3)
                : new[] { rows[22], rows[21], rows[20] };
            foreach (var row in visibleRows)
            {
                grid.Children.Add(new global::Avalonia.Controls.Border
                {
                    DataContext = row,
                    Width = 240,
                    Height = 30,
                    Child = new global::Avalonia.Controls.TextBlock { Text = row.Key }
                });
            }

            session.MainWindow.Content = grid;
            grid.Measure(new Size(240, 90));
            grid.Arrange(new Rect(0, 0, 240, 90));
        });
        var catalog = new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds(
                    "VirtualOrdersGrid", "VirtualOrdersGrid", "VirtualOrdersGrid")
                .WithColumns(GridColumnDefinition.Map("Key").FromField("Key"))
                .IdentifyRowsBy("Key"));
        var resolver = new HeadlessControlResolver(session.MainWindow).WithGridAutomation(catalog);
        var gridControl = resolver.Resolve<IGridControl>(new UiControlDefinition(
            "VirtualOrdersGrid", UiControlType.Grid, "VirtualOrdersGrid"));

        if (expectedKey is null)
        {
            var failure = Assert.Throws<InvalidOperationException>(() =>
                GridRowKeyReader.Capture(gridControl, position));
            await Assert.That(failure.Message).Contains($"cannot prove the displayed position {position}");
            return;
        }

        var selector = GridRowKeyReader.Capture(gridControl, position);
        await Assert.That(selector.Conditions.Single().Value).IsEqualTo(expectedKey);
    }

    private sealed record VirtualOrderRow(string Key);

    private sealed record VirtualOrderColumn(string FieldName);

    private sealed class VirtualOrdersGrid : global::Avalonia.Controls.StackPanel
    {
        public IReadOnlyList<VirtualOrderRow> ItemsSource { get; init; } = [];

        public IReadOnlyList<VirtualOrderRow>? CollectionView { get; init; }

        public IReadOnlyList<VirtualOrderColumn> Columns { get; } =
            [new VirtualOrderColumn("Key")];

    }
    
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task CompositeFilterReadsRenderedValueFromItsLogicalRoot()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        HeadlessRuntime.Dispatch(() =>
        {
            var editor = new global::Avalonia.Controls.Border
            {
                Child = new global::Avalonia.Controls.TextBlock { Text = "ООО Тест" }
            };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(editor, "FilterEditor");
            session.MainWindow.Content = editor;
        });
        var resolver = new HeadlessControlResolver(session.MainWindow)
            .WithComboBoxFilter(
                "ArmStatusFilter",
                ComboBoxFilterParts.ByAutomationIds("FilterEditor", "FilterOpen", "FilterResults"));
        var page = new MainWindowPage(resolver);

        page.WaitUntilTextEquals(static candidate => candidate.ArmStatusFilter, "ООО Тест", timeoutMs: 250);
        await Assert.That(UiControlText.Read(page.ArmStatusFilter)).IsEqualTo("ООО Тест");
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task CompositeFilterReadsExplicitDisplayPartWithoutChangingGenericText()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        HeadlessRuntime.Dispatch(() =>
        {
            var root = new global::Avalonia.Controls.StackPanel();
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(root, "FilterEditor");
            var value = new global::Avalonia.Controls.TextBlock { Text = "Pending" };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(value, "FilterDisplay");
            root.Children.Add(value);
            root.Children.Add(new global::Avalonia.Controls.TextBlock { Text = "Additional text" });
            session.MainWindow.Content = root;
        });
        var baseResolver = new HeadlessControlResolver(session.MainWindow);
        var generic = baseResolver.Resolve<IReadableTextControl>(new UiControlDefinition(
            "FilterEditor", UiControlType.AutomationElement, "FilterEditor"));
        var resolver = baseResolver.WithComboBoxFilter(
            "ArmStatusFilter",
            ComboBoxFilterParts.ByAutomationIds(
                "FilterEditor", "FilterOpen", "FilterResults", displayValueAutomationId: "FilterDisplay"));
        var page = new MainWindowPage(resolver);

        using (Assert.Multiple())
        {
            await Assert.That(generic.Text).IsEmpty();
            await Assert.That(UiControlText.Read(page.ArmStatusFilter)).IsEqualTo("Pending");
        }
    }
    

    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [NotInParallel("DesktopUi")]
    public async Task NotificationParts_AreReadOnlyInsideVisibleOwningRoot(int partCount)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        HeadlessRuntime.Dispatch(() =>
        {
            var host = new global::Avalonia.Controls.StackPanel();
            var outside = new global::Avalonia.Controls.Label { Content = "Outside message" };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(outside, "ArmNotificationText");
            host.Children.Add(outside);
            var sameNameRoot = new global::Avalonia.Controls.StackPanel();
            global::Avalonia.Automation.AutomationProperties.SetName(sameNameRoot, "ArmNotification");
            host.Children.Add(sameNameRoot);
            foreach (var visible in new[] { true, false })
            {
                var root = new global::Avalonia.Controls.StackPanel { IsVisible = visible };
                global::Avalonia.Automation.AutomationProperties.SetAutomationId(root, "ArmNotification");
                for (var i = 0; i < (visible ? partCount : 1); i++)
                {
                    var label = new global::Avalonia.Controls.Label { Content = visible ? "Own message" : "Hidden message" };
                    global::Avalonia.Automation.AutomationProperties.SetAutomationId(label, "ArmNotificationText");
                    root.Children.Add(label);
                }

                if (visible && partCount == 1)
                {
                    var sameNamePart = new global::Avalonia.Controls.Label { Content = "Decoy message" };
                    global::Avalonia.Automation.AutomationProperties.SetName(sameNamePart, "ArmNotificationText");
                    root.Children.Add(sameNamePart);
                }

                if (!visible || partCount >= 0)
                {
                    host.Children.Add(root);
                }
            }

            session.MainWindow.Content = host;
        });
        var resolver = new HeadlessControlResolver(session.MainWindow)
            .WithNotification("ArmNotification", NotificationControlParts.ByAutomationIds("ArmNotificationText"));
        var page = new MainWindowPage(resolver);
        var notification = page.ArmNotification;
        if (partCount is -1 or 1)
        {
            await Assert.That(notification.Text).IsEqualTo(partCount == -1 ? string.Empty : "Own message");
        }
        else
        {
            var exception = await Assert.That(() => notification.Text).Throws<UiControlResolutionException>();
            await Assert.That(exception.Failure).IsEqualTo(partCount == 0
                ? UiControlResolutionFailure.NotFound : UiControlResolutionFailure.Ambiguous);
            await Assert.That(exception.Message).Contains("inside its notification root");
        }
    }

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
            var control = new global::Avalonia.Controls.Button
            {
                Command = command,
                Content = new global::Avalonia.Controls.TextBlock { Text = "Saved_value" }
            };
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
        await Assert.That(((IReadableTextControl)resolvedButton).Text).IsEqualTo("Saved_value");

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
    public async Task MultiSelectItemsSurface_UsesRenderedListCaptionsForObjectItems()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var entries = new[] { new DisplayListEntry("Customer A"), new DisplayListEntry("Customer B") };
        var list = HeadlessRuntime.Dispatch(() =>
        {
            var control = new global::Avalonia.Controls.ListBox
            {
                ItemsSource = entries,
                ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<DisplayListEntry>((entry, _) =>
                {
                    var text = new global::Avalonia.Controls.TextBlock { Text = entry?.Caption };
                    global::Avalonia.Automation.AutomationProperties.SetName(text, entry?.Caption);
                    return text;
                })
            };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(control, "CustomerResults");
            session.MainWindow.Content = control;
            session.MainWindow.Show();
            session.MainWindow.Dispatcher.RunJobs();
            session.MainWindow.UpdateLayout();
            return control;
        });
        var items = new HeadlessControlResolver(session.MainWindow).Resolve<IMultiSelectItemsControl>(
            new UiControlDefinition("CustomerResults", UiControlType.ListBox, "CustomerResults"));

        var available = items.Items;
        items.SetSelectedItems(["Customer B"]);

        using (Assert.Multiple())
        {
            await Assert.That(available).IsEquivalentTo(["Customer A", "Customer B"]);
            await Assert.That(items.SelectedItems).IsEquivalentTo(["Customer B"]);
            await Assert.That(HeadlessRuntime.Dispatch(() => list.SelectedItem)).IsSameReferenceAs(entries[1]);
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task MultiSelectItemsSurface_UsesRenderedCheckBoxCaptionsForObjectItems()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var entries = new[] { new DisplayListEntry("Not canceled"), new DisplayListEntry("Canceled") };
        HeadlessRuntime.Dispatch(() =>
        {
            var panel = new global::Avalonia.Controls.StackPanel();
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(panel, "CancelResults");
            foreach (var entry in entries)
            {
                panel.Children.Add(new global::Avalonia.Controls.CheckBox
                {
                    Content = entry,
                    ContentTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<DisplayListEntry>((value, _) =>
                        new global::Avalonia.Controls.TextBlock { Text = value?.Caption })
                });
            }

            session.MainWindow.Content = panel;
            session.MainWindow.Show();
            session.MainWindow.Dispatcher.RunJobs();
            session.MainWindow.UpdateLayout();
        });
        var items = new HeadlessControlResolver(session.MainWindow).Resolve<IMultiSelectItemsControl>(
            new UiControlDefinition("CancelResults", UiControlType.ListBox, "CancelResults"));

        var available = items.Items;
        items.SetSelectedItems(["Not canceled"]);

        using (Assert.Multiple())
        {
            await Assert.That(available).IsEquivalentTo(["Not canceled", "Canceled"]);
            await Assert.That(items.SelectedItems).IsEquivalentTo(["Not canceled"]);
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

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task OpenGridRow_DoubleTapsTheResolvedVisualRow()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        HeadlessRuntime.Dispatch(() =>
        {
            session.MainWindow.Show();
            session.MainWindow.Dispatcher.RunJobs();
            session.MainWindow.UpdateLayout();
        });
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));

        page
            .SelectTabItem(static candidate => candidate.DataGridTabItem)
            .EnterText(static candidate => candidate.DataGridRowsInput, "3")
            .ClickButton(static candidate => candidate.BuildGridButton)
            .WaitUntilGridRowsAtLeast(static candidate => candidate.DemoDataGrid, 3)
            .OpenGridRow(static candidate => candidate.DemoDataGrid, GridRowSelector.ByCell("Row", "R2"));

        await Assert.That(page.GridOpenMarkerLabel.Text).IsEqualTo("Opened row: R2");
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    [NotInParallel("DesktopUi")]
    public async Task CompositeGridCombo_ChangesSelectedValueInsteadOfOnlyInputText(
        bool delayedResults,
        bool requiresPointerCommit)
    {
        const string fixtureVariable = "APPAUTOMATION_FLAUI_GRID_COMPOSITE_COMBO";
        var previous = Environment.GetEnvironmentVariable(fixtureVariable);
        const string delayVariable = "APPAUTOMATION_GRID_COMBO_DELAY_RESULTS";
        var previousDelay = Environment.GetEnvironmentVariable(delayVariable);
        const string confirmationVariable = "APPAUTOMATION_GRID_COMBO_REQUIRE_POINTER_COMMIT";
        var previousConfirmation = Environment.GetEnvironmentVariable(confirmationVariable);
        Environment.SetEnvironmentVariable(fixtureVariable, "1");
        Environment.SetEnvironmentVariable(delayVariable, delayedResults ? "1" : null);
        Environment.SetEnvironmentVariable(confirmationVariable, requiresPointerCommit ? "1" : null);
        try
        {
            using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
            HeadlessRuntime.Dispatch(() =>
            {
                session.MainWindow.Show();
                session.MainWindow.Dispatcher.RunJobs();
                session.MainWindow.UpdateLayout();
            });
            var catalog = CreateCompositeGridComboCatalog();
            var page = new MainWindowPage(
                new HeadlessControlResolver(session.MainWindow).WithGridAutomation(catalog));
            page.SelectTabItem(static candidate => candidate.DataGridTabItem);

            page.SelectGridCellComboItem(
                static candidate => candidate.ArmComplexDataGridControl,
                GridRowSelector.ByCell("Key", "ARM-02"),
                "State",
                "Closed");

            await Assert.That(GridValueReader.ReadCellText(
                page.ArmComplexDataGridControl,
                GridRowSelector.ByCell("Key", "ARM-02"),
                "State")).IsEqualTo("Closed");
            await Assert.That(GridValueReader.ReadCellText(
                page.ArmComplexDataGridControl,
                GridRowSelector.ByCell("Key", "ARM-01"),
                "State")).IsEqualTo("Open");
            var committedState = HeadlessRuntime.Dispatch(() =>
                ((global::DotnetDebug.Avalonia.MainWindowViewModel)session.MainWindow.DataContext!)
                    .ArmGridRows.Single(static row => row.Key == "ARM-02").State);
            await Assert.That(committedState).IsEqualTo("Closed");
        }
        finally
        {
            Environment.SetEnvironmentVariable(fixtureVariable, previous);
            Environment.SetEnvironmentVariable(delayVariable, previousDelay);
            Environment.SetEnvironmentVariable(confirmationVariable, previousConfirmation);
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task CompositeGridCombo_RejectsMissingOptionWithoutWritingReadOnlyInput()
    {
        const string fixtureVariable = "APPAUTOMATION_FLAUI_GRID_COMPOSITE_COMBO";
        var previous = Environment.GetEnvironmentVariable(fixtureVariable);
        Environment.SetEnvironmentVariable(fixtureVariable, "1");
        try
        {
            using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
            var textChanges = 0;
            HeadlessRuntime.Dispatch(() =>
            {
                session.MainWindow.Show();
                var grid = session.MainWindow.FindControl<DataGrid>("ArmComplexDataGridControl")!;
                grid.PreparingCellForEdit += (_, args) =>
                {
                    var input = args.EditingElement.GetVisualDescendants().OfType<TextBox>().Single();
                    input.TextChanged += (_, _) =>
                    {
                        if (input.Text != "Pending") textChanges++;
                    };
                };
            });
            var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow)
                .WithGridAutomation(CreateCompositeGridComboCatalog()));
            page.SelectTabItem(static candidate => candidate.DataGridTabItem);
            Exception? failure = null;
            try
            {
                page.SelectGridCellComboItem(static candidate => candidate.ArmComplexDataGridControl,
                    GridRowSelector.ByCell("Key", "ARM-02"), "State", "Missing option", timeoutMs: 1000);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            await Assert.That(failure).IsNotNull();
            await Assert.That(textChanges).IsEqualTo(0);
            await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
                GridRowSelector.ByCell("Key", "ARM-02"), "State")).IsEqualTo("Pending");
        }
        finally
        {
            Environment.SetEnvironmentVariable(fixtureVariable, previous);
        }
    }

    [Test]
    [Arguments("ARM-01", "Новый", "Запланирован", false, false, false)]
    [Arguments("ARM-02", "Новый", "Готов", false, false, false)]
    [Arguments("ARM-03", "Новый", "Запланирован", true, false, false)]
    [Arguments("ARM-02", "Готов", "Новый", false, false, false)]
    [Arguments("ARM-02", "Новый", "Готов", false, true, false)]
    [Arguments("ARM-02", "Новый", "Готов", false, false, true)]
    [NotInParallel("DesktopUi")]
    public async Task NativeCompositeGridCombo_CommitsEditorAndRowWithoutWritingInput(
        string rowKey, string initial, string expected, bool afterNavigation, bool cancel, bool expireDuringSelection)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var rows = Enumerable.Range(0, 3).Select(index =>
            new global::DotnetDebug.Avalonia.ArmDesktopGridRowViewModel(index, "Value", initial, "Red")).ToArray();
        Eremex.AvaloniaUI.Controls.Editors.ComboBoxEditor? selectedEditor = null;
        Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl? nativeGrid = null;
        var keyboardSelections = 0;
        var navigationKeys = 0;
        var popupOpens = 0;
        HeadlessRuntime.Dispatch(() =>
        {
            var grid = new Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl
                { ItemsSource = rows, AllowEditing = true, Height = 300 };
            nativeGrid = grid;
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(grid, "ArmComplexDataGridControl");
            grid.Columns.Add(new Eremex.AvaloniaUI.Controls.DataGrid.GridColumn
                { Header = "Key", FieldName = "Key", ReadOnly = true });
            grid.Columns.Add(new Eremex.AvaloniaUI.Controls.DataGrid.GridColumn
            {
                Header = "State", FieldName = "State",
                EditorProperties = new Eremex.AvaloniaUI.Controls.Editors.ComboBoxEditorProperties
                    { ItemsSource = new[] { "Новый", "Запланирован", "Готов" } }
            });
            grid.ShownEditor += (_, args) =>
            {
                if (args.Editor is not Eremex.AvaloniaUI.Controls.Editors.ComboBoxEditor editor) return;
                selectedEditor = editor;
                editor.PropertyChanged += (_, change) =>
                {
                    if (change.Property == Eremex.AvaloniaUI.Controls.Editors.PopupEditor.IsPopupOpenProperty
                        && editor.IsPopupOpen) popupOpens++;
                };
                editor.AddHandler(global::Avalonia.Input.InputElement.KeyDownEvent, (_, key) =>
                {
                    if (key.Key == global::Avalonia.Input.Key.Enter) keyboardSelections++;
                    if (key.Key is global::Avalonia.Input.Key.Down or global::Avalonia.Input.Key.Up)
                    {
                        navigationKeys++;
                        if (expireDuringSelection && navigationKeys == 1) Thread.Sleep(1100);
                    }
                }, global::Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
                editor.TemplateApplied += (_, _) => ConfigureParts();
                editor.LayoutUpdated += (_, _) => ConfigureParts();
                ConfigureParts();

                void ConfigureParts()
                {
                    if (editor.RealEditor is Control input)
                        global::Avalonia.Automation.AutomationProperties.SetAutomationId(input, "GridCombo_Input");
                    var open = editor.GetVisualDescendants().OfType<Control>()
                        .FirstOrDefault(static part => part.Name == "PART_PopupOpenButton");
                    if (open is not null)
                        global::Avalonia.Automation.AutomationProperties.SetAutomationId(open, "GridCombo_Open");
                    if (editor.PopupContent is Control popup)
                    {
                        var results = popup.GetVisualDescendants().OfType<ListBox>()
                            .Prepend(popup as ListBox).FirstOrDefault(static part => part?.Name == "PART_ItemsSelector");
                        if (results is not null)
                            global::Avalonia.Automation.AutomationProperties.SetAutomationId(results, "GridCombo_Results");
                    }
                }
            };
            var gridTab = new TabItem { Header = "Grid", Content = grid };
            var otherTab = new TabItem { Header = "Other", Content = new TextBlock { Text = "Other view" } };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(gridTab, "DataGridTabItem");
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(otherTab, "ArmDesktopTabItem");
            session.MainWindow.Content = new TabControl { ItemsSource = new[] { gridTab, otherTab }, SelectedIndex = 0 };
            session.MainWindow.Show();
            session.MainWindow.UpdateLayout();
        });
        var catalog = new GridAutomationCatalog().Add(GridAutomationDefinition.ByAutomationIds(
                "ArmComplexDataGridControl", "ArmComplexDataGridControl", "ArmComplexDataGridControl")
            .WithColumns(GridColumnDefinition.Auto("Key"),
                GridColumnDefinition.Auto("State").AsValue(GridCellValueKind.Selection)
                    .EditWith(GridCellEditorKind.ComboBox, new GridCellEditorParts(
                        Input: new GridRelativeLocator("GridCombo_Input"),
                        Results: new GridRelativeLocator("GridCombo_Results", GridRelativeLocatorScope.DetachedPopup),
                        OpenButton: new GridRelativeLocator("GridCombo_Open"))))
            .IdentifyRowsBy("Key"));
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow).WithGridAutomation(catalog));
        if (afterNavigation)
            page.SelectTabItem(static candidate => candidate.ArmDesktopTabItem)
                .SelectTabItem(static candidate => candidate.DataGridTabItem);
        if (expireDuringSelection)
        {
            Exception? failure = null;
            try
            {
                page.SelectGridCellComboItem(static candidate => candidate.ArmComplexDataGridControl,
                    GridRowSelector.ByCell("Key", rowKey), "State", expected, timeoutMs: 1000);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            await Assert.That(failure).IsNotNull();
            await Assert.That(navigationKeys).IsEqualTo(1);
            await Assert.That(keyboardSelections).IsEqualTo(0);
            await Assert.That(HeadlessRuntime.Dispatch(() => rows.All(row => row.State == initial))).IsTrue();
            await Assert.That(HeadlessRuntime.Dispatch(() => nativeGrid?.ActiveEditor is null)).IsTrue();
            return;
        }
        page.SelectGridCellComboItem(static candidate => candidate.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", rowKey), "State", expected,
            commitMode: cancel ? GridCellEditCommitMode.Cancel : GridCellEditCommitMode.Commit);
        if (!cancel)
        {
            await Assert.That(HeadlessRuntime.Dispatch(() => selectedEditor?.EditorValue?.ToString())).IsEqualTo(expected);
            await Assert.That(HeadlessRuntime.Dispatch(() => selectedEditor?.SelectedItem?.ToString())).IsEqualTo(expected);
        }
        await Assert.That(keyboardSelections).IsEqualTo(cancel ? 0 : 1);
        await Assert.That(popupOpens).IsEqualTo(1);
        await Assert.That(HeadlessRuntime.Dispatch(() => nativeGrid?.ActiveEditor is null)).IsTrue();
        page.SelectTabItem(static candidate => candidate.ArmDesktopTabItem)
            .SelectTabItem(static candidate => candidate.DataGridTabItem);
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", rowKey), "State")).IsEqualTo(cancel ? initial : expected);
        await Assert.That(HeadlessRuntime.Dispatch(() => rows.Single(row => row.Key == rowKey).State))
            .IsEqualTo(cancel ? initial : expected);
        await Assert.That(HeadlessRuntime.Dispatch(() => rows.Where(row => row.Key != rowKey).All(row => row.State == initial)))
            .IsTrue();

        var secondRow = rows.First(row => row.Key != rowKey).Key;
        var nextState = expected == "Готов" ? "Запланирован" : "Готов";
        page.SelectGridCellComboItem(static candidate => candidate.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", secondRow), "State", nextState);
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", secondRow), "State")).IsEqualTo(nextState);
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", rowKey), "State")).IsEqualTo(cancel ? initial : expected);
        await Assert.That(HeadlessRuntime.Dispatch(() => nativeGrid?.ActiveEditor is null)).IsTrue();
    }

    private static GridAutomationCatalog CreateCompositeGridComboCatalog() => new GridAutomationCatalog().Add(
        GridAutomationDefinition.ByAutomationIds(
                "ArmComplexDataGridControl", "ArmComplexDataGridControl", "ArmComplexDataGridControl")
            .WithColumns(
                GridColumnDefinition.Auto("Key").ReadIdentityFromRow(GridRowAutomationProperty.ItemStatus),
                GridColumnDefinition.Auto("State").AsValue(GridCellValueKind.Selection)
                    .EditWith(GridCellEditorKind.ComboBox, new GridCellEditorParts(
                        Input: new GridRelativeLocator("GridCombo_Input"),
                        Results: new GridRelativeLocator("GridCombo_Results", GridRelativeLocatorScope.DetachedPopup),
                        OpenButton: new GridRelativeLocator("GridCombo_Open", GridRelativeLocatorScope.GridRoot))))
            .IdentifyRowsBy("Key"));

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task NestedGridOpenRow_IgnoresContainersWithoutRowContext()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var first = new VisualGridTestRow("ROW-A");
        var second = new VisualGridTestRow("ROW-B");
        var targetPresses = 0;
        var opened = new List<string>();
        HeadlessRuntime.Dispatch(() =>
        {
            var grid = new VisualGridTestHost { ItemsSource = [first, second] };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(grid, "NestedGrid");
            grid.Children.Add(new global::Avalonia.Controls.Border
            {
                Width = 300,
                Height = 30,
                Background = global::Avalonia.Media.Brushes.Transparent,
                DataContext = null
            });
            grid.Children.Add(new global::Avalonia.Controls.Border
            {
                Width = 300,
                Height = 30,
                Background = global::Avalonia.Media.Brushes.Transparent,
                DataContext = new VisualGridRowContext(first)
            });
            var target = new global::Avalonia.Controls.Border
            {
                Width = 300,
                Height = 30,
                Background = global::Avalonia.Media.Brushes.Transparent,
                DataContext = new VisualGridRowContext(second)
            };
            target.AddHandler(
                global::Avalonia.Input.InputElement.PointerPressedEvent,
                (_, _) => targetPresses++,
                global::Avalonia.Interactivity.RoutingStrategies.Bubble);
            target.DoubleTapped += (_, _) => opened.Add(second.Key);
            grid.Children.Add(target);
            session.MainWindow.Content = grid;
            session.MainWindow.Show();
            session.MainWindow.Dispatcher.RunJobs();
            session.MainWindow.UpdateLayout();
        });
        var catalog = new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds("NestedGrid", "NestedGrid", "NestedGrid")
                .WithColumns(GridColumnDefinition.Auto("Key"))
                .IdentifyRowsBy("Key"));
        var gridControl = new HeadlessControlResolver(session.MainWindow)
            .WithGridAutomation(catalog)
            .Resolve<IGridControl>(new UiControlDefinition(
                "NestedGrid", UiControlType.Grid, "NestedGrid"));

        ((IAddressableGridControl)gridControl).OpenRow(
            GridRowSelector.ByCell("Key", "ROW-B"), 5000);

        await Assert.That(targetPresses).IsEqualTo(2);
        await Assert.That(opened).IsEquivalentTo(new[] { "ROW-B" });
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task NestedGridOutsideViewport_CapturesAndOpensStableRow()
    {
        const string fixtureVariable = "APPAUTOMATION_NESTED_GRID_OUTSIDE_VIEWPORT";
        var previous = Environment.GetEnvironmentVariable(fixtureVariable);
        Environment.SetEnvironmentVariable(fixtureVariable, "1");
        try
        {
            using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
            HeadlessRuntime.Dispatch(() =>
            {
                session.MainWindow.Show();
                session.MainWindow.Dispatcher.RunJobs();
                session.MainWindow.UpdateLayout();
            });
            var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow)
                .WithGridAutomation(DotnetDebug.AppAutomation.Configuration.SampleGridAutomation.CreateHeadlessCatalog()));
            page.SelectTabItem(static candidate => candidate.DataGridTabItem);

            var row = GridRowKeyReader.Capture(page.ArmComplexDataGridControl, 0);
            page.OpenGridRow(static candidate => candidate.ArmComplexDataGridControl, row);

            using (Assert.Multiple())
            {
                await Assert.That(row.Conditions.Single().Value).IsEqualTo("ARM-01");
                await Assert.That(page.GridOpenMarkerLabel.Text).IsEqualTo("Opened row: ARM-01");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(fixtureVariable, previous);
        }
    }

    private sealed record VisualGridTestRow(string Key);

    private sealed record VisualGridRowContext(VisualGridTestRow Row);

    private sealed record VisualGridTestColumn(string FieldName);

    private sealed class VisualGridTestHost : global::Avalonia.Controls.StackPanel
    {
        public object[] ItemsSource { get; set; } = [];

        public VisualGridTestColumn[] Columns { get; } = [new("Key")];
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task OpenGridRow_RejectsAnOccludedRowInsteadOfReportingGestureSuccess()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        HeadlessRuntime.Dispatch(() =>
        {
            session.MainWindow.Show();
            session.MainWindow.Dispatcher.RunJobs();
            session.MainWindow.UpdateLayout();
        });
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));
        page
            .SelectTabItem(static candidate => candidate.DataGridTabItem)
            .EnterText(static candidate => candidate.DataGridRowsInput, "3")
            .ClickButton(static candidate => candidate.BuildGridButton)
            .WaitUntilGridRowsAtLeast(static candidate => candidate.DemoDataGrid, 3);

        HeadlessRuntime.Dispatch(() =>
        {
            var originalContent = (global::Avalonia.Controls.Control)session.MainWindow.Content!;
            session.MainWindow.Content = null;
            var root = new global::Avalonia.Controls.Grid();
            root.Children.Add(originalContent);
            root.Children.Add(new global::Avalonia.Controls.Border
            {
                Background = global::Avalonia.Media.Brushes.Transparent,
                IsHitTestVisible = true
            });
            session.MainWindow.Content = root;
            session.MainWindow.Dispatcher.RunJobs();
            session.MainWindow.UpdateLayout();
        });

        var failure = Assert.Throws<InvalidOperationException>(() =>
            ((IAddressableGridControl)page.DemoDataGrid).OpenRow(
                GridRowSelector.ByCell("Row", "R2"), 5000));
        await Assert.That(failure.Message).Contains("occluded");
        await Assert.That(page.GridOpenMarkerLabel.Text).IsNotEqualTo("Opened row: R2");
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task ReadGridCell_WaitsForStableRowAfterSourceReload()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var host = HeadlessRuntime.Dispatch(() =>
        {
            var grid = new DelayedRowsGrid();
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(grid, "DelayedRowsGrid");
            session.MainWindow.Content = grid;
            session.MainWindow.Show();
            return grid;
        });
        var gridControl = new HeadlessControlResolver(session.MainWindow).Resolve<IGridControl>(
            new UiControlDefinition("DelayedRowsGrid", UiControlType.Grid, "DelayedRowsGrid"));

        var value = GridValueReader.ReadCellText(
            gridControl,
            GridRowSelector.ByCell("Key", "K1"),
            "Value");

        using (Assert.Multiple())
        {
            await Assert.That(value).IsEqualTo("Loaded");
            await Assert.That(host.ReadCount).IsGreaterThan(1);
        }
    }

    private sealed class DelayedRowsGrid : global::Avalonia.Controls.StackPanel
    {
        private readonly List<DelayedGridRow> _rows = [];
        private bool _loadScheduled;

        public int ReadCount { get; private set; }

        public IReadOnlyList<DelayedGridColumn> Columns { get; } =
            [new("Key"), new("Value")];

        public IEnumerable<DelayedGridRow> ItemsSource
        {
            get
            {
                ReadCount++;
                if (!_loadScheduled)
                {
                    _loadScheduled = true;
                    Dispatcher.Post(() => _rows.Add(new DelayedGridRow("K1", "Loaded")),
                        global::Avalonia.Threading.DispatcherPriority.Background);
                }

                return _rows;
            }
        }
    }

    private sealed record DelayedGridColumn(string FieldName);

    private sealed record DelayedGridRow(string Key, string Value);

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
