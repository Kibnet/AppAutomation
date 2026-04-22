using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.Abstractions;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.TestHost;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

public sealed class HeadlessControlResolverTests
{
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
    [NotInParallel("DesktopUi")]
    public async Task SelectListBoxItem_ByCapability_SelectsHeadlessItem()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));

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
    public async Task ResolveEremexDataGridBridge_ByAutomationId_ReadsRowsAndCells()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));

        page
            .SelectTabItem(static candidate => candidate.DataGridTabItem)
            .EnterText(static candidate => candidate.DataGridRowsInput, "5")
            .ClickButton(static candidate => candidate.BuildGridButton)
            .WaitUntilNameEquals(static candidate => candidate.GridResultLabel, "Grid rows: 5")
            .WaitUntilGridRowsAtLeast(static candidate => candidate.EremexDemoDataGridAutomationBridge, 5)
            .WaitUntilGridCellEquals(static candidate => candidate.EremexDemoDataGridAutomationBridge, 2, 0, "EX-R3")
            .WaitUntilGridCellEquals(static candidate => candidate.EremexDemoDataGridAutomationBridge, 2, 1, "EX-13")
            .WaitUntilGridCellEquals(static candidate => candidate.EremexDemoDataGridAutomationBridge, 2, 2, "EX-Odd");

        using (Assert.Multiple())
        {
            await Assert.That(page.Capabilities.SupportsGridCellAccess).IsEqualTo(true);
            await Assert.That(page.EremexDemoDataGrid.AutomationId).IsEqualTo("EremexDemoDataGrid");
            await Assert.That(page.EremexDemoDataGrid.IsEnabled).IsEqualTo(true);
            await Assert.That(page.EremexDemoDataGridAutomationBridge.Rows.Count >= 5).IsEqualTo(true);
            await Assert.That(page.EremexDemoDataGridAutomationBridge.GetRowByIndex(2)!.Cells[0].Value).IsEqualTo("EX-R3");
        }
    }
}
