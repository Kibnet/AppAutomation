using System;
using System.Linq;
using DotnetDebug.AppAutomation.Authoring.Pages;
using AppAutomation.Abstractions;
using AppAutomation.TUnit;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Authoring.Tests.UIAutomationTests;

public abstract partial class MainWindowScenariosBase<TSession> : UiTestBase<TSession, MainWindowPage>
    where TSession : class, IUiTestSession
{
    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Calculate_Gcd_WithDefaultSettings_ShowsResultStepsAndHistory()
    {
        var initialHistoryItems = Page.HistoryList.Items.Count;

        Page.EnterText(p => p.NumbersInput, "48 18 30");
        Page.SelectComboItem(p => p.OperationCombo, "GCD");
        Page.SetChecked(p => p.ShowStepsCheck, true);
        Page.ClickButton(p => p.CalculateButton);
        Page.WaitUntilNameEquals(p => p.ResultText, "GCD = 6");
        Page.WaitUntilHasItemsAtLeast(p => p.StepsList, 1);
        Page.WaitUntilListBoxContains(p => p.HistoryList, "GCD");

        using (Assert.Multiple())
        {
            await UiAssert.TextEqualsAsync(() => Page.ResultText.Text, "GCD = 6");
            await UiAssert.TextContainsAsync(() => Page.ModeLabel.Text, "Mode: GCD");
            await UiAssert.TextContainsAsync(() => Page.ModeLabel.Text, "Steps: On");
            await UiAssert.NumberAtLeastAsync(() => Page.StepsList.Items.Count, 1);
            await UiAssert.NumberAtLeastAsync(() => Page.HistoryList.Items.Count, initialHistoryItems + 1);
            await UiAssert.TextEqualsAsync(() => Page.ErrorText.Text, string.Empty);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Calculate_Lcm_UsesNegativeAndAbsoluteOption()
    {
        var initialHistoryItems = Page.HistoryList.Items.Count;

        Page
            .EnterText(p => p.NumbersInput, "-4 8 12")
            .SelectComboItem(p => p.OperationCombo, "LCM")
            .SetChecked(p => p.UseAbsoluteValuesCheck, true)
            .SetChecked(p => p.ShowStepsCheck, false)
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, "LCM = 24")
            .WaitUntilListBoxContains(p => p.HistoryList, "LCM");

        using (Assert.Multiple())
        {
            await UiAssert.TextEqualsAsync(() => Page.ResultText.Text, "LCM = 24");
            await UiAssert.TextContainsAsync(() => Page.ModeLabel.Text, "Mode: LCM");
            await UiAssert.TextContainsAsync(() => Page.ModeLabel.Text, "Absolute: On");
            await UiAssert.TextEqualsAsync(() => Page.ErrorText.Text, string.Empty);
            await UiAssert.NumberAtLeastAsync(() => Page.HistoryList.Items.Count, initialHistoryItems + 1);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Calculate_Min_RespectsAbsoluteCheckbox()
    {
        Page
            .SelectComboItem(p => p.OperationCombo, "MIN")
            .SetChecked(p => p.UseAbsoluteValuesCheck, false)
            .EnterText(p => p.NumbersInput, "-10 2 5")
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, "MIN = -10");

        using (Assert.Multiple())
        {
            await UiAssert.TextEqualsAsync(() => Page.ResultText.Text, "MIN = -10");
            await UiAssert.TextContainsAsync(() => Page.ModeLabel.Text, "Mode: MIN");
            await UiAssert.TextContainsAsync(() => Page.ModeLabel.Text, "Absolute: Off");
        }

        Page
            .SetChecked(p => p.UseAbsoluteValuesCheck, true)
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, "MIN = 2");

        await UiAssert.TextEqualsAsync(() => Page.ResultText.Text, "MIN = 2");
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Calculate_InvalidInput_ShowsError_NoHistory()
    {
        var initialHistoryItems = Page.HistoryList.Items.Count;

        Page
            .SelectComboItem(p => p.OperationCombo, "GCD")
            .EnterText(p => p.NumbersInput, "48 x 30")
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameContains(p => p.ErrorText, "Invalid integer: x");

        using (Assert.Multiple())
        {
            await UiAssert.TextContainsAsync(() => Page.ErrorText.Text, "Invalid integer: x");
            await UiAssert.TextEqualsAsync(() => Page.ResultText.Text, string.Empty);
        }

        await Assert.That(Page.HistoryList.Items.Count).IsEqualTo(initialHistoryItems);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task FilterHistory_ByText_ShowsOnlyMatchingItems()
    {
        Page
            .EnterText(p => p.NumbersInput, "48 18 30")
            .SelectComboItem(p => p.OperationCombo, "GCD")
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, "GCD = 6");

        Page
            .EnterText(p => p.NumbersInput, "4 8 12")
            .SelectComboItem(p => p.OperationCombo, "LCM")
            .SetChecked(p => p.UseAbsoluteValuesCheck, true)
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, "LCM = 24");

        Page
            .EnterText(p => p.HistoryFilterInput, "LCM")
            .ClickButton(p => p.ApplyFilterButton)
            .WaitUntilHasItemsAtLeast(p => p.HistoryList, 1);

        var filteredHistory = Page.HistoryList.Items
            .Select(item => item.Text ?? item.Name ?? string.Empty)
            .ToArray();

        await Assert.That(filteredHistory.Length >= 1).IsEqualTo(true);
        await Assert.That(filteredHistory.All(item => item.Contains("LCM", StringComparison.Ordinal))).IsEqualTo(true);

        Page
            .EnterText(p => p.HistoryFilterInput, string.Empty)
            .ClickButton(p => p.ApplyFilterButton);

        await UiAssert.NumberAtLeastAsync(() => Page.HistoryList.Items.Count, 2);

        Page.ClickButton(p => p.ClearHistoryButton);

        await Assert.That(Page.HistoryList.Items.Count).IsEqualTo(0);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task ControlMix_SliderSpinnerRadioToggle_BuildsSeriesAndShowsProgress()
    {
        const int timeoutMs = 15000;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        Page
            .SelectTabItem(p => p.ControlMixTabItem)
            .SelectComboItem(p => p.MixModeCombo, "Fibonacci")
            .SetChecked(p => p.MixShowDetailsCheck, true)
            .SetChecked(p => p.MixDirectionAscendingRadio, true)
            .SetChecked(p => p.MixDirectionDescendingRadio, false)
            .SetToggled(p => p.MixAdvancedToggle, true)
            .WaitUntilIsToggled(p => p.MixAdvancedToggle, true);

        Page.SetSpinnerValue(p => p.MixCountSpinner, 10);

        await Assert.That(Page.MixCountSpinner.Text).IsEqualTo("10");

        Page
            .SetSliderValue(p => p.MixSpeedSlider, 4)
            .EnterText(p => p.MixInput, "1 2")
            .WaitUntilIsToggled(p => p.MixAdvancedToggle, true)
            .WaitUntilIsSelected(p => p.MixDirectionAscendingRadio, true)
            .WaitUntilIsSelected(p => p.MixDirectionDescendingRadio, false)
            .ClickButton(p => p.MixRunButton)
            .WaitUntilProgressAtLeast(p => p.SeriesProgressBar, 100, timeoutMs)
            .WaitUntilNameContains(p => p.SeriesResult, "Series[Fibonacci]", timeoutMs);

        await Assert.That(Page.SeriesResult.Text).Contains("Series[Fibonacci]");
        await Assert.That(Page.SeriesResult.Text).Contains("count=10");
        await Assert.That(Page.SeriesResult.Text).Contains("max=305");
        await Assert.That(Page.SeriesResult.Text).Contains("min=1");

        using (Assert.Multiple())
        {
            await UiAssert.TextContainsAsync(() => Page.SeriesResult.Text, "Series[Fibonacci]", timeout);
            await UiAssert.TextContainsAsync(() => Page.SeriesResult.Text, "count=10", timeout);
            await UiAssert.TextContainsAsync(() => Page.SeriesResult.Text, "max=305", timeout);
            await UiAssert.TextContainsAsync(() => Page.SeriesResult.Text, "min=1", timeout);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task DataGrid_BuildSelectClear_ShowsRowsSelectionAndValidation()
    {
        Page
            .SelectTabItem(p => p.DataGridTabItem)
            .EnterText(p => p.DataGridRowsInput, "5")
            .ClickButton(p => p.BuildGridButton)
            .WaitUntilNameEquals(p => p.GridResultLabel, "Grid rows: 5")
            .WaitUntilNameEquals(p => p.GridSelectionLabel, "No row selected")
            .WaitUntilNameEquals(p => p.DataGridErrorText, string.Empty);

        Page
            .EnterText(p => p.DataGridSelectRowInput, "2")
            .ClickButton(p => p.SelectGridRowButton)
            .WaitUntilNameEquals(p => p.GridSelectionLabel, "Selected row: R3");

        using (Assert.Multiple())
        {
            await UiAssert.TextEqualsAsync(() => Page.GridResultLabel.Text, "Grid rows: 5");
            await UiAssert.TextEqualsAsync(() => Page.GridSelectionLabel.Text, "Selected row: R3");
            await UiAssert.TextEqualsAsync(() => Page.DataGridErrorText.Text, string.Empty);
        }

        Page
            .EnterText(p => p.DataGridSelectRowInput, "99")
            .ClickButton(p => p.SelectGridRowButton)
            .WaitUntilNameContains(p => p.DataGridErrorText, "out of range");

        await UiAssert.TextContainsAsync(() => Page.DataGridErrorText.Text, "out of range");

        Page
            .ClickButton(p => p.ClearGridButton)
            .WaitUntilNameEquals(p => p.GridResultLabel, string.Empty)
            .WaitUntilNameEquals(p => p.GridSelectionLabel, "No row selected")
            .WaitUntilNameEquals(p => p.DataGridErrorText, string.Empty);

        using (Assert.Multiple())
        {
            await UiAssert.TextEqualsAsync(() => Page.GridResultLabel.Text, string.Empty);
            await UiAssert.TextEqualsAsync(() => Page.GridSelectionLabel.Text, "No row selected");
            await UiAssert.TextEqualsAsync(() => Page.DataGridErrorText.Text, string.Empty);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task DataGrid_EremexRecorderBridge_GeneratedFlowWorks()
    {
        Page
            .SelectTabItem(p => p.DataGridTabItem)
            .EnterText(p => p.DataGridRowsInput, "5")
            .ClickButton(p => p.BuildGridButton)
            .WaitUntilNameEquals(p => p.GridResultLabel, "Grid rows: 5")
            .WaitUntilGridRowsAtLeast(p => p.EremexDemoDataGridAutomationBridge, 5)
            .WaitUntilGridCellEquals(p => p.EremexDemoDataGridAutomationBridge, 2, 0, "EX-R3")
            .WaitUntilGridCellEquals(p => p.EremexDemoDataGridAutomationBridge, 2, 1, "EX-13")
            .WaitUntilGridCellEquals(p => p.EremexDemoDataGridAutomationBridge, 2, 2, "EX-Odd")
            .WaitUntilIsEnabled(p => p.EremexDemoDataGrid, true);

        using (Assert.Multiple())
        {
            await Assert.That(Page.EremexDemoDataGrid.AutomationId).IsEqualTo("EremexDemoDataGrid");
            await Assert.That(Page.EremexDemoDataGridAutomationBridge.Rows.Count).IsGreaterThanOrEqualTo(5);
            await Assert.That(Page.EremexDemoDataGridAutomationBridge.GetRowByIndex(2)!.Cells[0].Value).IsEqualTo("EX-R3");
            await Assert.That(Page.EremexDemoDataGridAutomationBridge.GetRowByIndex(2)!.Cells[1].Value).IsEqualTo("EX-13");
            await Assert.That(Page.EremexDemoDataGridAutomationBridge.GetRowByIndex(2)!.Cells[2].Value).IsEqualTo("EX-Odd");
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task ArmDesktop_PrimitivesWrappersAndSearch_Work()
    {
        Page
            .SelectTabItem(p => p.ArmDesktopTabItem)
            .EnterText(p => p.ArmCopyTextBox, "ARM-COPY-42")
            .ClickButton(p => p.ArmCopyButton)
            .WaitUntilNameEquals(p => p.ArmCopyResultLabel, "Copied: ARM-COPY-42")
            .SetChecked(p => p.ArmSearchFuzzyToggle, true)
            .WaitUntilIsChecked(p => p.ArmSearchFuzzyToggle, true)
            .SearchAndSelect(p => p.ArmSearchPicker, "customer", "Customer Alpha")
            .WaitUntilNameContains(p => p.ArmSearchStatusLabel, "Customer Alpha")
            .SearchAndSelect(p => p.ArmServerSearchPicker, "product", "Product 42")
            .WaitUntilNameContains(p => p.ArmServerPickerStatusLabel, "Product 42")
            .ClickButton(p => p.ArmServerPickerClearButton)
            .WaitUntilNameEquals(p => p.ArmServerPickerStatusLabel, "Server picker cleared");

        using (Assert.Multiple())
        {
            await UiAssert.TextEqualsAsync(() => Page.ArmCopyResultLabel.Text, "Copied: ARM-COPY-42");
            await UiAssert.TextContainsAsync(() => Page.ArmSearchStatusLabel.Text, "Customer Alpha");
            await UiAssert.TextContainsAsync(() => Page.ArmServerPickerStatusLabel.Text, "cleared");
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task ArmDesktop_GridActionsAndEditableCells_Work()
    {
        Page
            .SelectTabItem(p => p.ArmDesktopTabItem)
            .ClickButton(p => p.ArmGridBuildButton)
            .WaitUntilNameEquals(p => p.ArmGridStatusLabel, "Grid rows: 3")
            .WaitUntilGridRowsAtLeast(p => p.ArmGridAutomationBridge, 3)
            .WaitUntilGridCellEquals(p => p.ArmGridAutomationBridge, 0, 0, "ARM-01")
            .WaitUntilGridCellEquals(p => p.ArmGridAutomationBridge, 0, 1, "Value-1")
            .EnterText(p => p.ArmGridEditValueInput, "Edited-42")
            .WaitUntilTextEquals(p => p.ArmGridEditValueInput, "Edited-42")
            .ClickButton(p => p.ArmGridCommitEditButton)
            .WaitUntilNameContains(p => p.ArmGridStatusLabel, "Edited-42")
            .WaitUntilGridCellEquals(p => p.ArmGridAutomationBridge, 0, 1, "Edited-42")
            .ClickButton(p => p.ArmGridOpenButton)
            .WaitUntilNameContains(p => p.ArmGridStatusLabel, "ARM-01")
            .ClickButton(p => p.ArmGridLoadMoreButton)
            .WaitUntilGridRowsAtLeast(p => p.ArmGridAutomationBridge, 5)
            .WaitUntilNameEquals(p => p.ArmGridStatusLabel, "Grid rows: 5")
            .ClickButton(p => p.ArmGridSortButton)
            .WaitUntilNameEquals(p => p.ArmGridStatusLabel, "Grid sorted by value")
            .ClickButton(p => p.ArmGridCopyButton)
            .WaitUntilNameEquals(p => p.ArmGridStatusLabel, "Grid copied")
            .ClickButton(p => p.ArmGridExportButton)
            .WaitUntilNameEquals(p => p.ArmGridStatusLabel, "Grid export requested")
            .WaitUntilIsEnabled(p => p.ArmEremexDataGridHost, true);

        using (Assert.Multiple())
        {
            await UiAssert.TextContainsAsync(() => Page.ArmGridStatusLabel.Text, "export");
            await Assert.That(Page.ArmGridAutomationBridge.Rows.Count).IsGreaterThanOrEqualTo(5);
            await Assert.That(Page.ArmEremexDataGridHost.AutomationId).IsEqualTo("ArmEremexDataGridHost");
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task ArmDesktop_FiltersDialogsNotificationsAndExport_Work()
    {
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 30);

        Page
            .SelectTabItem(p => p.ArmDesktopTabItem)
            .SetDateRangeFilter(p => p.ArmDateRangeFilter, from, to)
            .WaitUntilNameContains(p => p.ArmDateRangeStatusLabel, "2026-04-01..2026-04-30")
            .SetNumericRangeFilter(p => p.ArmNumericRangeFilter, 10.5, 42.25)
            .WaitUntilNameContains(p => p.ArmNumericRangeStatusLabel, "10.5..42.25")
            .ConfirmDialog(p => p.ArmDialog, "Delete selected")
            .WaitUntilNameEquals(p => p.ArmDialogResultLabel, "Dialog confirmed")
            .WaitUntilNotificationContains(p => p.ArmNotification, "Export ready")
            .DismissNotification(p => p.ArmNotification)
            .WaitUntilNameEquals(p => p.ArmNotificationStatusLabel, "Notification dismissed")
            .SelectExportFolder(
                p => p.ArmFolderExport,
                @"C:\Exports\Arm",
                expectedStatusContains: @"C:\Exports\Arm")
            .WaitUntilNameContains(p => p.ArmFolderExportStatusLabel, @"C:\Exports\Arm");

        using (Assert.Multiple())
        {
            await UiAssert.TextContainsAsync(() => Page.ArmDateRangeStatusLabel.Text, "2026-04-01");
            await UiAssert.TextContainsAsync(() => Page.ArmNumericRangeStatusLabel.Text, "42.25");
            await UiAssert.TextEqualsAsync(() => Page.ArmDialogResultLabel.Text, "Dialog confirmed");
            await UiAssert.TextContainsAsync(() => Page.ArmFolderExportStatusLabel.Text, @"C:\Exports\Arm");
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task ArmDesktop_ShellStatusLoadingApprovalAndCrud_Work()
    {
        Page
            .SelectTabItem(p => p.ArmDesktopTabItem)
            .OpenOrActivateShellPane(p => p.ArmShellNavigation, "Reports")
            .WaitUntilNameEquals(p => p.ArmShellActivePaneLabel, "Reports")
            .ActivateShellPane(p => p.ArmShellNavigation, "Customers")
            .WaitUntilNameEquals(p => p.ArmShellActivePaneLabel, "Customers")
            .ClickButton(p => p.ArmReloadButton)
            .WaitUntilProgressAtLeast(p => p.ArmLoadingProgressBar, 100)
            .WaitUntilNameEquals(p => p.ArmLoadingStatusLabel, "Reloaded: 100%")
            .SetToggled(p => p.ArmStatusExpanderToggle, true)
            .WaitUntilNameEquals(p => p.ArmStatusLabel, "Status expanded: True")
            .SetToggled(p => p.ArmMetadataToggle, true)
            .WaitUntilNameEquals(p => p.ArmMetadataStatusLabel, "Metadata visible: True")
            .SetToggled(p => p.ArmApprovalToggle, true)
            .WaitUntilNameEquals(p => p.ArmApprovalStatusLabel, "Approval: approved")
            .ClickButton(p => p.ArmCrudAddButton)
            .WaitUntilNameEquals(p => p.ArmActionStatusLabel, "CRUD: added")
            .ClickButton(p => p.ArmCrudEditButton)
            .WaitUntilNameEquals(p => p.ArmActionStatusLabel, "CRUD: edited")
            .ClickButton(p => p.ArmCrudDeleteButton)
            .WaitUntilNameEquals(p => p.ArmActionStatusLabel, "CRUD: deleted")
            .ClickButton(p => p.ArmSaveButton)
            .WaitUntilNameEquals(p => p.ArmActionStatusLabel, "Action: saved")
            .ClickButton(p => p.ArmSaveCloseButton)
            .WaitUntilNameEquals(p => p.ArmActionStatusLabel, "Action: saved and closed")
            .ClickButton(p => p.ArmCloseButton)
            .WaitUntilNameEquals(p => p.ArmActionStatusLabel, "Action: closed");

        using (Assert.Multiple())
        {
            await UiAssert.TextEqualsAsync(() => Page.ArmShellActivePaneLabel.Text, "Customers");
            await UiAssert.TextEqualsAsync(() => Page.ArmLoadingStatusLabel.Text, "Reloaded: 100%");
            await UiAssert.TextEqualsAsync(() => Page.ArmApprovalStatusLabel.Text, "Approval: approved");
            await UiAssert.TextEqualsAsync(() => Page.ArmActionStatusLabel.Text, "Action: closed");
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task DateTime_InvalidRange_ShowsValidation()
    {
        Page
            .SelectTabItem(p => p.DateTimeTabItem)
            .SetDate(p => p.StartDatePicker, DateTime.Today)
            .SetDate(p => p.EndDatePicker, DateTime.Today.AddDays(-2))
            .ClickButton(p => p.DateDiffButton)
            .WaitUntilNameContains(p => p.DateErrorText, "End date should be greater than or equal to start date");

        using (Assert.Multiple())
        {
            await UiAssert.TextContainsAsync(() => Page.DateErrorText.Text, "End date should be greater than or equal to start date");
            await Assert.That(Page.DateDiffList.Items.Count).IsEqualTo(0);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task DateTime_ComputeDateDifference_ByDatePickers()
    {
        Page
            .SelectTabItem(p => p.DateTimeTabItem)
            .SetDate(p => p.StartDatePicker, DateTime.Today.AddDays(-7))
            .SetDate(p => p.EndDatePicker, DateTime.Today)
            .ClickButton(p => p.DateDiffButton)
            .WaitUntilNameContains(p => p.DateResult, "Date difference = 7 days")
            .WaitUntilHasItemsAtLeast(p => p.DateDiffList, 5);

        using (Assert.Multiple())
        {
            await UiAssert.TextContainsAsync(() => Page.DateResult.Text, "7 days");
            await UiAssert.TextContainsAsync(() => Page.DateDiffList.Items[0].Text ?? string.Empty, "Start:");
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task TabControl_NavigatesAcrossTabs_WithExpectedControls()
    {
        Page
            .SelectTabItem(p => p.ControlMixTabItem)
            .SelectTabItem(p => p.HierarchyTabItem)
            .SelectTabItem(p => p.DateTimeTabItem)
            .SetDate(p => p.StartDatePicker, DateTime.Today.AddDays(-2))
            .SetDate(p => p.EndDatePicker, DateTime.Today)
            .ClickButton(p => p.DateDiffButton)
            .WaitUntilNameContains(p => p.DateResult, "Date difference = 2 days")
            .SelectTabItem(p => p.MathTabItem)
            .SelectComboItem(p => p.OperationCombo, "GCD")
            .EnterText(p => p.NumbersInput, "6 9")
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, "GCD = 3");

        using (Assert.Multiple())
        {
            await UiAssert.TextEqualsAsync(() => Page.ResultText.Text, "GCD = 3");
            await UiAssert.TextContainsAsync(() => Page.ModeLabel.Text, "Mode: GCD");
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Hierarchy_SelectTreeItem_ShowsSelectionInResult()
    {
        Page.SelectTabItem(p => p.HierarchyTabItem);

        Page
            .SelectTreeItem(p => p.DemoTree, "Fibonacci")
            .WaitUntilHasItemsAtLeast(p => p.HierarchySelectionList, 2)
            .WaitUntilListBoxContains(p => p.HierarchySelectionList, "Fibonacci");

        using (Assert.Multiple())
        {
            await UiAssert.TextContainsAsync(() => Page.HierarchyResultLabel.Text, "Fibonacci");
            await UiAssert.NumberAtLeastAsync(() => Page.HierarchySelectionList.Items.Count, 2);
        }

        Page
            .ClickButton(p => p.HierarchyClearSelectionButton)
            .WaitUntilNameEquals(p => p.HierarchyResultLabel, "No node selected");

        await UiAssert.TextEqualsAsync(() => Page.HierarchyResultLabel.Text, "No node selected");
    }
}
