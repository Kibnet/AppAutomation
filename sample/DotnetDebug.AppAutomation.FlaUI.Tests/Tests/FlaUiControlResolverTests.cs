using AppAutomation.Abstractions;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using DotnetDebug.AppAutomation.TestHost;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Session;
using FlaUI.Core.AutomationElements;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

public sealed class FlaUiControlResolverTests
{
    private static readonly UiWaitOptions EremexGridWaitOptions = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        PollInterval = TimeSpan.FromMilliseconds(200)
    };

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
            EremexGridWaitOptions,
            "Eremex DataGrid automation anchor was not found by AutomationId.")
            ?? throw new InvalidOperationException("Eremex DataGrid automation anchor was not found by AutomationId.");
        var bridgeElement = UiWait.Until(
            () => session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId("EremexDemoDataGridAutomationBridge")),
            static element => element is not null && TryRead(() => element.IsAvailable),
            EremexGridWaitOptions,
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

    private static string[] ReadElementNames(AutomationElement root)
    {
        return root.FindAllDescendants()
            .Prepend(root)
            .Select(static element => TryRead(() => element.Name) ?? string.Empty)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ContainsText(IEnumerable<string> texts, string expected)
    {
        return texts.Any(text => text.Contains(expected, StringComparison.Ordinal));
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
}
