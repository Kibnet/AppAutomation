using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Session;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using DotnetDebug.AppAutomation.TestHost;
using FlaUI.Core.AutomationElements;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

public sealed class FlaUiControlResolverTests
{
    private static readonly UiWaitOptions DesktopControlWaitOptions = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        PollInterval = TimeSpan.FromMilliseconds(200)
    };

    private static readonly string[] ExpectedMultiSelectItems =
    [
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta",
        "Eta", "Theta", "Iota", "Kappa", "Lambda", "Mu",
        "Nu", "Xi", "Omicron", "Pi", "Rho", "Sigma",
        "Tau", "Upsilon", "Phi", "Chi", "Psi", "Omega"
    ];

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task EremexMultiSelectPopup_ExposesInstrumentedPartsAndReadsAllItems()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions());
        var desktop = session.MainWindow.Automation.GetDesktop();
        var page = MainWindowFlaUiPageFactory.Create(session);
        page.SelectTabItem(static candidate => candidate.ControlMixTabItem);
        page.MultiSelection.Open();
        var popup = FindInstrumentedMultiSelectPopup(session, desktop);

        using (Assert.Multiple())
        {
            await Assert.That(popup.Results.AutomationId).IsEqualTo("MultiSelection_Results");
            await Assert.That(popup.ApplyButton.AutomationId).IsEqualTo("MultiSelection_ApplyButton");
            await Assert.That(popup.CancelButton.AutomationId).IsEqualTo("MultiSelection_CancelButton");
            await Assert.That(page.MultiSelection.Items).IsEquivalentTo(ExpectedMultiSelectItems);
        }

        popup.CancelButton.Click();
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task MultiSelectMissingItem_DoesNotPartiallyChangeDesktopSelection()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions());
        var page = MainWindowFlaUiPageFactory.Create(session);
        page.SelectTabItem(static candidate => candidate.ControlMixTabItem);
        page.MultiSelection.Open();

        try
        {
            var resolver = new FlaUiControlResolver(session.MainWindow, session.ConditionFactory);
            var items = resolver.Resolve<IMultiSelectItemsControl>(new UiControlDefinition(
                "MultiSelectionItems",
                UiControlType.ListBox,
                "MultiSelection_Results",
                UiLocatorKind.AutomationId,
                FallbackToName: false));
            items.SetSelectedItems(["Alpha"]);

            await Assert.That(() => items.SetSelectedItems(["Beta", "Missing"]))
                .Throws<InvalidOperationException>();
            await Assert.That(items.SelectedItems).IsEquivalentTo(["Alpha"]);
        }
        finally
        {
            if (page.MultiSelection.IsOpen)
            {
                page.MultiSelection.Cancel();
            }
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task SelectListBoxItem_ByCapability_SelectsDesktopItem()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions());
        var page = new MainWindowPage(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));

        page
            .SelectTabItem(static candidate => candidate.HierarchyTabItem)
            .SelectTreeItem(static candidate => candidate.DemoTree, "Fibonacci")
            .WaitUntilHasItemsAtLeast(static candidate => candidate.HierarchySelectionList, 2)
            .SelectListBoxItem(static candidate => candidate.HierarchySelectionList, "Fibonacci");

        var selectableList = page.HierarchySelectionList as ISelectableListBoxControl;

        using (Assert.Multiple())
        {
            await Assert.That(selectableList).IsNotNull();
            await Assert.That(selectableList!.SelectedItemText).IsEqualTo("Fibonacci");
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task ServerSearchComboBox_GridPopupStaysClosedUntilEditorIsUsed()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions());
        var desktop = session.MainWindow.Automation.GetDesktop();
        var page = new MainWindowPage(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));

        page.SelectTabItem(static candidate => candidate.DataGridTabItem);
        var input = WaitForDesktopElement(session, desktop, "SearchPickerGridEditor_Input", "search input");
        var isPopupVisible = IsVisible(desktop.FindFirstDescendant(
            session.ConditionFactory.ByAutomationId("SearchPickerGridEditor_Results")));

        await Assert.That(isPopupVisible).IsFalse();

        input.AsTextBox().Text = "a";
        var results = UiWait.Until(
            () => desktop.FindFirstDescendant(
                session.ConditionFactory.ByAutomationId("SearchPickerGridEditor_Results")),
            IsVisible,
            DesktopControlWaitOptions,
            "ServerSearchComboBox results did not become visible after text input.");

        await Assert.That(results).IsNotNull();
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task EremexDataGridBridge_ByAutomationId_ReadsDesktopRowsAndCells()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions());
        var page = new MainWindowPage(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));

        page
            .SelectTabItem(static candidate => candidate.DataGridTabItem)
            .EnterText(static candidate => candidate.DataGridRowsInput, "5")
            .ClickButton(static candidate => candidate.BuildGridButton)
            .WaitUntilNameEquals(static candidate => candidate.GridResultLabel, "Grid rows: 5")
            .WaitUntilGridRowsAtLeast(static candidate => candidate.EremexDemoDataGridAutomationBridge, 5)
            .WaitUntilGridCellEquals(static candidate => candidate.EremexDemoDataGridAutomationBridge, 2, 0, "EX-R3")
            .WaitUntilGridCellEquals(static candidate => candidate.EremexDemoDataGridAutomationBridge, 2, 1, "EX-13")
            .WaitUntilGridCellEquals(static candidate => candidate.EremexDemoDataGridAutomationBridge, 2, 2, "EX-Odd");

        var eremexAnchor = UiWait.Until(
            () => session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId("EremexDemoDataGrid")),
            static element => element is not null && TryRead(() => element.IsAvailable),
            DesktopControlWaitOptions,
            "Eremex DataGrid automation anchor was not found by AutomationId.")
            ?? throw new InvalidOperationException("Eremex DataGrid automation anchor was not found by AutomationId.");
        var bridgeElement = UiWait.Until(
            () => session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId("EremexDemoDataGridAutomationBridge")),
            static element => element is not null && TryRead(() => element.IsAvailable),
            DesktopControlWaitOptions,
            "Eremex DataGrid automation bridge was not found by AutomationId.")
            ?? throw new InvalidOperationException("Eremex DataGrid automation bridge was not found by AutomationId.");

        var visibleTexts = ReadElementNames(session.MainWindow);

        using (Assert.Multiple())
        {
            await Assert.That(eremexAnchor.AutomationId).IsEqualTo("EremexDemoDataGrid");
            await Assert.That(TryRead(() => bridgeElement.Patterns.Grid.IsSupported)).IsEqualTo(false);
            await Assert.That(page.EremexDemoDataGrid.AutomationId).IsEqualTo("EremexDemoDataGrid");
            await Assert.That(page.EremexDemoDataGridAutomationBridge.Rows.Count >= 5).IsEqualTo(true);
            await Assert.That(page.EremexDemoDataGridAutomationBridge.GetRowByIndex(2)!.Cells[0].Value).IsEqualTo("EX-R3");
            await Assert.That(ContainsText(visibleTexts, "Eremex DataGrid")).IsEqualTo(true);
            await Assert.That(page.GridResultLabel.Text).Contains("Grid rows:");
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task VisualGridOpenRow_DoubleClicksDesktopBridgeRow()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions());
        var page = new MainWindowPage(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));

        page
            .SelectTabItem(static candidate => candidate.ArmDesktopTabItem)
            .ClickButton(static candidate => candidate.ArmGridBuildButton)
            .WaitUntilNameEquals(static candidate => candidate.ArmGridStatusLabel, "Grid rows: 3")
            .OpenGridRow(static candidate => candidate.ArmGridAutomationBridge, 0)
            .WaitUntilNameEquals(static candidate => candidate.ArmGridStatusLabel, "Grid opened: ARM-01");

        await Assert.That(page.ArmGridStatusLabel.Text).IsEqualTo("Grid opened: ARM-01");
    }

    private static string[] ReadElementNames(AutomationElement root)
    {
        return root.FindAllDescendants()
            .Prepend(root)
            .Select(static element => TryRead(() => element.Name) ?? string.Empty)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static MultiSelectPopupParts FindInstrumentedMultiSelectPopup(
        DesktopAppSession session,
        AutomationElement desktop)
    {
        return new MultiSelectPopupParts(
            WaitForDesktopElement(session, desktop, "MultiSelection_Results", "results"),
            WaitForDesktopElement(session, desktop, "MultiSelection_ApplyButton", "Apply button"),
            WaitForDesktopElement(session, desktop, "MultiSelection_CancelButton", "Cancel button"));
    }

    private static AutomationElement WaitForDesktopElement(
        DesktopAppSession session,
        AutomationElement desktop,
        string automationId,
        string description)
    {
        var failureMessage = $"The instrumented multi-select popup part '{description}' was not exposed.";
        return UiWait.Until(
                () => desktop.FindFirstDescendant(session.ConditionFactory.ByAutomationId(automationId)),
                static element => element is not null && TryRead(() => element.IsAvailable),
                DesktopControlWaitOptions,
                failureMessage)
            ?? throw new InvalidOperationException(failureMessage);
    }

    private static bool ContainsText(IEnumerable<string> texts, string expected)
    {
        return texts.Any(text => text.Contains(expected, StringComparison.Ordinal));
    }

    private static bool IsVisible(AutomationElement? element)
    {
        return element is not null
            && TryRead(() => element.IsAvailable && !element.IsOffscreen);
    }

    private static T? TryRead<T>(Func<T> accessor)
    {
        try
        {
            return accessor();
        }
        catch
        {
            return default;
        }
    }

    private sealed record MultiSelectPopupParts(
        AutomationElement Results,
        AutomationElement ApplyButton,
        AutomationElement CancelButton);
}
