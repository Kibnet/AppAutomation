using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class UiPageExtensionsTests
{
    [Test]
    public async Task SelectComboItem_Throws_WhenRequestedItemIsMissing()
    {
        var combo = new FakeComboBoxControl(
            "OperationCombo",
            new[]
            {
                new FakeComboBoxItem("GCD", "GCD"),
                new FakeComboBoxItem("LCM", "LCM")
            });
        var page = new ComboPage(new FakeResolver(("OperationCombo", combo)));

        Exception? exception = null;
        try
        {
            page.SelectComboItem(static candidate => candidate.OperationCombo, "MIN");
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception is InvalidOperationException).IsEqualTo(true);
            await Assert.That(combo.SelectedIndex).IsEqualTo(-1);
        }
    }

    [Test]
    public async Task SelectListBoxItem_SelectsItem_WhenRuntimeSupportsCapability()
    {
        var listBox = new FakeSelectableListBoxControl(
            "HierarchySelectionList",
            [
                new FakeListBoxItem("Prime", "Prime"),
                new FakeListBoxItem("Fibonacci", "Fibonacci")
            ]);
        var page = new ListPage(new FakeResolver(("HierarchySelectionList", listBox)));

        var returnedPage = page.SelectListBoxItem(static candidate => candidate.HierarchySelectionList, "Fibonacci");

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(returnedPage, page)).IsEqualTo(true);
            await Assert.That(listBox.SelectedItemText).IsEqualTo("Fibonacci");
        }
    }

    [Test]
    public async Task SelectListBoxItem_Throws_WhenRuntimeDoesNotSupportCapability()
    {
        var listBox = new FakeListBoxControl(
            "HierarchySelectionList",
            [
                new FakeListBoxItem("Prime", "Prime"),
                new FakeListBoxItem("Fibonacci", "Fibonacci")
            ]);
        var page = new ListPage(new FakeResolver(("HierarchySelectionList", listBox)));

        Exception? exception = null;
        try
        {
            page.SelectListBoxItem(static candidate => candidate.HierarchySelectionList, "Fibonacci");
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception is InvalidOperationException).IsEqualTo(true);
            await Assert.That(exception!.Message).Contains("does not support interactive selection");
        }
    }

    [Test]
    public async Task SelectTreeItem_UsesSelectedItemIdentity_WhenSelectedTextIsUnavailable()
    {
        var tree = new FakeTreeControl("DemoTree");
        var selectedItem = new FakeTreeItemControl("TreeNodeFibonacci", "Different header", string.Empty);
        var targetItem = new FakeTreeItemControl("TreeNodeFibonacci", "Fibonacci", "Fibonacci")
        {
            OnSelect = () => tree.SelectedTreeItem = selectedItem
        };

        tree.SetItems(targetItem);

        var page = new TreePage(new FakeResolver(("DemoTree", tree)));

        var returnedPage = page.SelectTreeItem(static candidate => candidate.DemoTree, "Fibonacci");

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(returnedPage, page)).IsEqualTo(true);
            await Assert.That(tree.SelectedTreeItem).IsNotNull();
            await Assert.That(tree.SelectedTreeItem!.AutomationId).IsEqualTo("TreeNodeFibonacci");
        }
    }

    [Test]
    public async Task WaitUntilNameEquals_ThrowsUiOperationException_WithFailureContext()
    {
        var label = new FakeLabelControl("ResultLabel", "Actual");
        var resolver = new FakeResolver(
            [("ResultLabel", (object)label)],
            artifacts:
            [
                new UiFailureArtifact(
                    Kind: "logical-tree",
                    LogicalName: "logical-tree",
                    RelativePath: "artifacts/ui-failures/fake/logical-tree.txt",
                    ContentType: "text/plain",
                    IsRequiredByContract: true,
                    InlineTextPreview: "Window -> ResultLabel")
            ]);
        var page = new DiagnosticsPage(resolver);

        UiOperationException? exception = null;
        try
        {
            page.WaitUntilNameEquals(static candidate => candidate.ResultLabel, "Expected", timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("WaitUntilNameEquals");
            await Assert.That(exception.FailureContext.AdapterId).IsEqualTo("fake-runtime");
            await Assert.That(exception.FailureContext.PageTypeFullName).Contains(nameof(DiagnosticsPage));
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("ResultLabel");
            await Assert.That(exception.FailureContext.LocatorValue).IsEqualTo("ResultLabel");
            await Assert.That(exception.FailureContext.LocatorKind).IsEqualTo(UiLocatorKind.Name);
            await Assert.That(exception.FailureContext.LastObservedValue).IsEqualTo("Actual");
            await Assert.That(exception.FailureContext.Artifacts.Count).IsEqualTo(1);
            await Assert.That(exception.InnerException is TimeoutException).IsEqualTo(true);
        }
    }

    [Test]
    public async Task WaitUntilNameEquals_ThrowsUiOperationException_WhenControlReadFails()
    {
        var label = new ThrowingLabelControl("ResultLabel", "stale element");
        var resolver = new FakeResolver(
            [("ResultLabel", (object)label)],
            artifacts:
            [
                new UiFailureArtifact(
                    Kind: "logical-tree",
                    LogicalName: "logical-tree",
                    RelativePath: "artifacts/ui-failures/fake/logical-tree.txt",
                    ContentType: "text/plain",
                    IsRequiredByContract: true,
                    InlineTextPreview: "Window -> ResultLabel")
            ]);
        var page = new DiagnosticsPage(resolver);

        UiOperationException? exception = null;
        try
        {
            page.WaitUntilNameEquals(static candidate => candidate.ResultLabel, "Expected", timeoutMs: 250);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("WaitUntilNameEquals");
            await Assert.That(exception.FailureContext.LastObservedValue).Contains("stale element");
            await Assert.That(exception.FailureContext.Artifacts.Count).IsEqualTo(1);
            await Assert.That(exception.Message).Contains("Operation failed before timeout");
            await Assert.That(exception.InnerException is InvalidOperationException).IsEqualTo(true);
        }
    }

    [Test]
    public async Task SelectTabItem_ByStableTabItemControl_SelectsTab()
    {
        var tabItem = new FakeTabItemControl("TasksTabItem");
        var page = new TabsPage(new FakeResolver(("TasksTabItem", tabItem)));

        page
            .SelectTabItem(static candidate => candidate.TasksTabItem)
            .WaitUntilIsSelected(static candidate => candidate.TasksTabItem);

        await Assert.That(tabItem.IsSelected).IsEqualTo(true);
    }

    [Test]
    public async Task WaitUntilTextEquals_OnLabel_ReturnsPage_WhenTextAlreadyMatches()
    {
        var label = new FakeLabelControl("ResultLabel", "Expected");
        var page = new DiagnosticsPage(new FakeResolver(("ResultLabel", label)));

        var returnedPage = page.WaitUntilTextEquals(static candidate => candidate.ResultLabel, "Expected");

        await Assert.That(ReferenceEquals(returnedPage, page)).IsEqualTo(true);
    }

    [Test]
    public async Task WaitUntilTextContains_OnTextBox_ReturnsPage_WhenTextAlreadyMatches()
    {
        var textBox = new FakeTextBoxControl("SearchBox", "Alpha Beta");
        var page = new TextPage(new FakeResolver(("SearchBox", textBox)));

        var returnedPage = page.WaitUntilTextContains(static candidate => candidate.SearchBox, "Beta");

        await Assert.That(ReferenceEquals(returnedPage, page)).IsEqualTo(true);
    }

    [Test]
    public async Task WaitUntilIsChecked_ThrowsUiOperationException_WithFailureContext()
    {
        var checkBox = new FakeCheckBoxControl("AgreeCheck") { IsChecked = false };
        var page = new StatePage(new FakeResolver(("AgreeCheck", checkBox)));

        UiOperationException? exception = null;
        try
        {
            page.WaitUntilIsChecked(static candidate => candidate.AgreeCheck, expected: true, timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("WaitUntilIsChecked");
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("AgreeCheck");
            await Assert.That(exception.FailureContext.LastObservedValue).IsEqualTo("IsChecked=False");
            await Assert.That(exception.InnerException is TimeoutException).IsEqualTo(true);
        }
    }

    [Test]
    public async Task WaitUntilIsEnabled_ThrowsUiOperationException_WithFailureContext()
    {
        var button = new FakeButtonControl("SaveButton") { IsEnabled = false };
        var page = new StatePage(new FakeResolver(("SaveButton", button)));

        UiOperationException? exception = null;
        try
        {
            page.WaitUntilIsEnabled(static candidate => candidate.SaveButton, expected: true, timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("WaitUntilIsEnabled");
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("SaveButton");
            await Assert.That(exception.FailureContext.LastObservedValue).IsEqualTo("IsEnabled=False");
            await Assert.That(exception.InnerException is TimeoutException).IsEqualTo(true);
        }
    }

    [Test]
    public async Task WaitUntilGridRowsAtLeast_ReturnsPage_WhenRowsAlreadyPresent()
    {
        var grid = new FakeGridControl(
            "EremexDemoDataGridAutomationBridge",
            [
                new FakeGridRowControl(new FakeGridCellControl("EX-R1")),
                new FakeGridRowControl(new FakeGridCellControl("EX-R2")),
                new FakeGridRowControl(new FakeGridCellControl("EX-R3"))
            ]);
        var page = new GridPage(new FakeResolver(("EremexDemoDataGridAutomationBridge", grid)));

        var returnedPage = page.WaitUntilGridRowsAtLeast(static candidate => candidate.EremexDemoDataGridAutomationBridge, 3);

        await Assert.That(ReferenceEquals(returnedPage, page)).IsEqualTo(true);
    }

    [Test]
    public async Task WaitUntilGridCellEquals_ThrowsUiOperationException_WithFailureContext()
    {
        var grid = new FakeGridControl(
            "EremexDemoDataGridAutomationBridge",
            [
                new FakeGridRowControl(new FakeGridCellControl("EX-R1")),
                new FakeGridRowControl(new FakeGridCellControl("EX-R2"))
            ]);
        var page = new GridPage(new FakeResolver(("EremexDemoDataGridAutomationBridge", grid)));

        UiOperationException? exception = null;
        try
        {
            page.WaitUntilGridCellEquals(
                static candidate => candidate.EremexDemoDataGridAutomationBridge,
                1,
                0,
                "EX-R3",
                timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("WaitUntilGridCellEquals");
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("EremexDemoDataGridAutomationBridge");
            await Assert.That(exception.FailureContext.LastObservedValue).IsEqualTo("EX-R2");
            await Assert.That(exception.InnerException is TimeoutException).IsEqualTo(true);
        }
    }

    [Test]
    public async Task GridUserActions_InvokeRuntimeActionControl_WhenSupported()
    {
        var grid = new FakeGridUserActionControl(
            "EremexDemoDataGridAutomationBridge",
            [
                new FakeGridRowControl(new FakeGridCellControl("EX-R1"), new FakeGridCellControl("EX-7")),
                new FakeGridRowControl(new FakeGridCellControl("EX-R2"), new FakeGridCellControl("EX-10"))
            ]);
        var page = new GridPage(new FakeResolver(("EremexDemoDataGridAutomationBridge", grid)));

        var returnedPage = page
            .OpenGridRow(static candidate => candidate.EremexDemoDataGridAutomationBridge, 1)
            .SortGridByColumn(static candidate => candidate.EremexDemoDataGridAutomationBridge, "Value")
            .ScrollGridToEnd(static candidate => candidate.EremexDemoDataGridAutomationBridge)
            .CopyGridCell(static candidate => candidate.EremexDemoDataGridAutomationBridge, 1, 0)
            .ExportGrid(static candidate => candidate.EremexDemoDataGridAutomationBridge);

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(returnedPage, page)).IsEqualTo(true);
            await Assert.That(grid.OpenedRowIndex).IsEqualTo(1);
            await Assert.That(grid.SortedColumnName).IsEqualTo("Value");
            await Assert.That(grid.ScrollToEndCount).IsEqualTo(1);
            await Assert.That(grid.CopiedCell).IsEqualTo((1, 0, "EX-R2"));
            await Assert.That(grid.ExportCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task GridUserActions_ThrowUiOperationException_WhenRuntimeDoesNotSupportActions()
    {
        var grid = new FakeGridControl(
            "EremexDemoDataGridAutomationBridge",
            [new FakeGridRowControl(new FakeGridCellControl("EX-R1"))]);
        var page = new GridPage(new FakeResolver(("EremexDemoDataGridAutomationBridge", grid)));

        UiOperationException? exception = null;
        try
        {
            page.OpenGridRow(static candidate => candidate.EremexDemoDataGridAutomationBridge, 0, timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("OpenGridRow");
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("EremexDemoDataGridAutomationBridge");
            await Assert.That(exception.Message).Contains("does not support user action 'OpenGridRow'");
            await Assert.That(exception.InnerException is NotSupportedException).IsEqualTo(true);
        }
    }

    [Test]
    public async Task GridCellEditMethods_InvokeRuntimeEditableGrid_WhenSupported()
    {
        var cells = new[]
        {
            new MutableFakeGridCellControl("OldText"),
            new MutableFakeGridCellControl("1"),
            new MutableFakeGridCellControl("2026-01-01"),
            new MutableFakeGridCellControl("OldCombo"),
            new MutableFakeGridCellControl("OldSearch")
        };
        var grid = new FakeEditableGridControl(
            "EremexDemoDataGridAutomationBridge",
            [new FakeGridRowControl(cells)]);
        var page = new GridPage(new FakeResolver(("EremexDemoDataGridAutomationBridge", grid)));

        var returnedPage = page
            .EditGridCellText(static candidate => candidate.EremexDemoDataGridAutomationBridge, 0, 0, "NewText")
            .EditGridCellNumber(static candidate => candidate.EremexDemoDataGridAutomationBridge, 0, 1, 12.5)
            .EditGridCellDate(static candidate => candidate.EremexDemoDataGridAutomationBridge, 0, 2, new DateTime(2026, 4, 22))
            .SelectGridCellComboItem(static candidate => candidate.EremexDemoDataGridAutomationBridge, 0, 3, "ComboValue")
            .SearchAndSelectGridCell(static candidate => candidate.EremexDemoDataGridAutomationBridge, 0, 4, "Combo", "SearchValue");

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(returnedPage, page)).IsEqualTo(true);
            await Assert.That(grid.Requests.Count).IsEqualTo(5);
            await Assert.That(grid.Requests[0].EditorKind).IsEqualTo(GridCellEditorKind.Text);
            await Assert.That(grid.Requests[1].EditorKind).IsEqualTo(GridCellEditorKind.Number);
            await Assert.That(grid.Requests[1].Value).IsEqualTo("12.5");
            await Assert.That(grid.Requests[2].EditorKind).IsEqualTo(GridCellEditorKind.Date);
            await Assert.That(grid.Requests[2].Value).IsEqualTo("2026-04-22");
            await Assert.That(grid.Requests[3].EditorKind).IsEqualTo(GridCellEditorKind.ComboBox);
            await Assert.That(grid.Requests[4].EditorKind).IsEqualTo(GridCellEditorKind.SearchPicker);
            await Assert.That(grid.Requests[4].SearchText).IsEqualTo("Combo");
            await Assert.That(cells[4].Value).IsEqualTo("SearchValue");
        }
    }

    [Test]
    public async Task GridCellEdit_CancelMode_KeepsOriginalCellValue()
    {
        var cell = new MutableFakeGridCellControl("Original");
        var grid = new FakeEditableGridControl(
            "EremexDemoDataGridAutomationBridge",
            [new FakeGridRowControl(cell)]);
        var page = new GridPage(new FakeResolver(("EremexDemoDataGridAutomationBridge", grid)));

        page.EditGridCellText(
            static candidate => candidate.EremexDemoDataGridAutomationBridge,
            0,
            0,
            "Changed",
            GridCellEditCommitMode.Cancel);

        using (Assert.Multiple())
        {
            await Assert.That(grid.Requests.Count).IsEqualTo(1);
            await Assert.That(grid.Requests[0].CommitMode).IsEqualTo(GridCellEditCommitMode.Cancel);
            await Assert.That(cell.Value).IsEqualTo("Original");
        }
    }

    [Test]
    public async Task GridCellEdit_ThrowsUiOperationException_WhenRuntimeDoesNotSupportEditing()
    {
        var grid = new FakeGridControl(
            "EremexDemoDataGridAutomationBridge",
            [new FakeGridRowControl(new FakeGridCellControl("EX-R1"))]);
        var page = new GridPage(new FakeResolver(("EremexDemoDataGridAutomationBridge", grid)));

        UiOperationException? exception = null;
        try
        {
            page.EditGridCellText(static candidate => candidate.EremexDemoDataGridAutomationBridge, 0, 0, "EX-R2", timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("EditGridCell");
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("EremexDemoDataGridAutomationBridge");
            await Assert.That(exception.Message).Contains("does not support cell editing");
            await Assert.That(exception.InnerException is NotSupportedException).IsEqualTo(true);
        }
    }

    [Test]
    public async Task SetDateRangeFilter_InvokesRuntimeFilterAndVerifiesApply()
    {
        var filter = new FakeDateRangeFilterControl("CreatedAtFilter");
        var page = new FilterPage(new FakeResolver(("CreatedAtFilter", filter)));

        var returnedPage = page.SetDateRangeFilter(
            static candidate => candidate.CreatedAtFilter,
            new DateTime(2026, 4, 1),
            new DateTime(2026, 4, 30));

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(returnedPage, page)).IsEqualTo(true);
            await Assert.That(filter.Requests.Count).IsEqualTo(1);
            await Assert.That(filter.Requests[0].CommitMode).IsEqualTo(FilterPopupCommitMode.Apply);
            await Assert.That(filter.FromValue).IsEqualTo(new DateTime(2026, 4, 1));
            await Assert.That(filter.ToValue).IsEqualTo(new DateTime(2026, 4, 30));
            await Assert.That(filter.OpenCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task SetNumericRangeFilter_CancelModeInvokesRuntimeFilterWithoutMutatingAppliedRange()
    {
        var filter = new FakeNumericRangeFilterControl("AmountFilter")
        {
            FromValue = 1,
            ToValue = 5
        };
        var page = new FilterPage(new FakeResolver(("AmountFilter", filter)));

        page.SetNumericRangeFilter(
            static candidate => candidate.AmountFilter,
            10,
            20,
            FilterPopupCommitMode.Cancel);

        using (Assert.Multiple())
        {
            await Assert.That(filter.Requests.Count).IsEqualTo(1);
            await Assert.That(filter.Requests[0].CommitMode).IsEqualTo(FilterPopupCommitMode.Cancel);
            await Assert.That(filter.FromValue).IsEqualTo(1);
            await Assert.That(filter.ToValue).IsEqualTo(5);
            await Assert.That(filter.OpenCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task SetDateRangeFilter_ThrowsUiOperationException_WhenRuntimeFilterFails()
    {
        var filter = new FakeDateRangeFilterControl("CreatedAtFilter")
        {
            Failure = new InvalidOperationException("popup part missing")
        };
        var page = new FilterPage(new FakeResolver(("CreatedAtFilter", filter)));

        UiOperationException? exception = null;
        try
        {
            page.SetDateRangeFilter(
                static candidate => candidate.CreatedAtFilter,
                new DateTime(2026, 4, 1),
                new DateTime(2026, 4, 30),
                timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("SetDateRangeFilter");
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("CreatedAtFilter");
            await Assert.That(exception.Message).Contains("popup part missing");
            await Assert.That(exception.InnerException is InvalidOperationException).IsEqualTo(true);
        }
    }

    [Test]
    public async Task ConfirmDialog_ValidatesMessageAndInvokesConfirm()
    {
        var dialog = new FakeDialogControl("DeleteDialog", "Delete selected record?");
        var page = new WorkflowPage(new FakeResolver(("DeleteDialog", dialog)));

        var returnedPage = page.ConfirmDialog(
            static candidate => candidate.DeleteDialog,
            "delete selected");

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(returnedPage, page)).IsEqualTo(true);
            await Assert.That(dialog.CompletedActions.Count).IsEqualTo(1);
            await Assert.That(dialog.CompletedActions[0]).IsEqualTo(DialogActionKind.Confirm);
        }
    }

    [Test]
    public async Task CancelDialog_ThrowsUiOperationException_WhenRuntimeDialogFails()
    {
        var dialog = new FakeDialogControl("DeleteDialog", "Delete selected record?")
        {
            Failure = new NotSupportedException("CancelButton is not configured")
        };
        var page = new WorkflowPage(new FakeResolver(("DeleteDialog", dialog)));

        UiOperationException? exception = null;
        try
        {
            page.CancelDialog(
                static candidate => candidate.DeleteDialog,
                "Delete",
                timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("CompleteDialog");
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("DeleteDialog");
            await Assert.That(exception.Message).Contains("CancelButton is not configured");
            await Assert.That(exception.InnerException is NotSupportedException).IsEqualTo(true);
        }
    }

    [Test]
    public async Task NotificationHelpers_WaitAndDismissNotification()
    {
        var notification = new FakeNotificationControl("ExportToast", "Export completed");
        var page = new WorkflowPage(new FakeResolver(("ExportToast", notification)));

        var returnedPage = page
            .WaitUntilNotificationContains(static candidate => candidate.ExportToast, "completed")
            .DismissNotification(static candidate => candidate.ExportToast);

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(returnedPage, page)).IsEqualTo(true);
            await Assert.That(notification.DismissCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task WaitUntilNotificationContains_ThrowsUiOperationException_WhenTextIsMissing()
    {
        var notification = new FakeNotificationControl("ExportToast", "Export queued");
        var page = new WorkflowPage(new FakeResolver(("ExportToast", notification)));

        UiOperationException? exception = null;
        try
        {
            page.WaitUntilNotificationContains(
                static candidate => candidate.ExportToast,
                "completed",
                timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("WaitUntilNotificationContains");
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("ExportToast");
            await Assert.That(exception.FailureContext.LastObservedValue).IsEqualTo("Export queued");
            await Assert.That(exception.InnerException is TimeoutException).IsEqualTo(true);
        }
    }

    [Test]
    public async Task SelectExportFolder_InvokesRuntimeExportAndWaitsStatus()
    {
        var export = new FakeFolderExportControl("ReportExport")
        {
            StatusText = "Export completed"
        };
        var page = new WorkflowPage(new FakeResolver(("ReportExport", export)));

        var returnedPage = page.SelectExportFolder(
            static candidate => candidate.ReportExport,
            @"C:\Exports\Reports",
            expectedStatusContains: "completed");

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(returnedPage, page)).IsEqualTo(true);
            await Assert.That(export.Requests.Count).IsEqualTo(1);
            await Assert.That(export.Requests[0]).IsEqualTo((@"C:\Exports\Reports", FolderExportCommitMode.Select));
            await Assert.That(export.SelectedFolderPath).IsEqualTo(@"C:\Exports\Reports");
        }
    }

    [Test]
    public async Task SelectExportFolder_ThrowsUiOperationException_WhenRuntimeExportFails()
    {
        var export = new FakeFolderExportControl("ReportExport")
        {
            Failure = new InvalidOperationException("folder picker unavailable")
        };
        var page = new WorkflowPage(new FakeResolver(("ReportExport", export)));

        UiOperationException? exception = null;
        try
        {
            page.SelectExportFolder(
                static candidate => candidate.ReportExport,
                @"C:\Exports\Reports",
                timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("SelectExportFolder");
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("ReportExport");
            await Assert.That(exception.Message).Contains("folder picker unavailable");
            await Assert.That(exception.InnerException is InvalidOperationException).IsEqualTo(true);
        }
    }

    [Test]
    public async Task OpenOrActivateShellPane_InvokesRuntimeShellAndVerifiesActivePane()
    {
        var shell = new FakeShellNavigationControl("Shell");
        var page = new WorkflowPage(new FakeResolver(("Shell", shell)));

        var returnedPage = page.OpenOrActivateShellPane(
            static candidate => candidate.Shell,
            "Customers");

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(returnedPage, page)).IsEqualTo(true);
            await Assert.That(shell.Requests.Count).IsEqualTo(1);
            await Assert.That(shell.Requests[0].PaneName).IsEqualTo("Customers");
            await Assert.That(shell.Requests[0].Mode).IsEqualTo(ShellPaneNavigationMode.OpenOrActivate);
            await Assert.That(shell.ActivePaneName).IsEqualTo("Customers");
            await Assert.That(shell.OpenPaneNames).Contains("Customers");
        }
    }

    [Test]
    public async Task ActivateShellPane_ThrowsUiOperationException_WhenRuntimeShellFails()
    {
        var shell = new FakeShellNavigationControl("Shell")
        {
            Failure = new NotSupportedException("pane tabs are not configured")
        };
        var page = new WorkflowPage(new FakeResolver(("Shell", shell)));

        UiOperationException? exception = null;
        try
        {
            page.ActivateShellPane(
                static candidate => candidate.Shell,
                "Customers",
                timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("ActivateShellPane");
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("Shell");
            await Assert.That(exception.Message).Contains("pane tabs are not configured");
            await Assert.That(exception.InnerException is NotSupportedException).IsEqualTo(true);
        }
    }

    public static class ComboPageDefinitions
    {
        public static UiControlDefinition OperationCombo { get; } = new(
            "OperationCombo",
            UiControlType.ComboBox,
            "OperationCombo");
    }

    public static class DiagnosticsPageDefinitions
    {
        public static UiControlDefinition ResultLabel { get; } = new(
            "ResultLabel",
            UiControlType.Label,
            "ResultLabel",
            UiLocatorKind.Name,
            FallbackToName: false);
    }

    public static class TabsPageDefinitions
    {
        public static UiControlDefinition TasksTabItem { get; } = new(
            "TasksTabItem",
            UiControlType.TabItem,
            "TasksTabItem");
    }

    public static class TextPageDefinitions
    {
        public static UiControlDefinition SearchBox { get; } = new(
            "SearchBox",
            UiControlType.TextBox,
            "SearchBox");
    }

    public static class StatePageDefinitions
    {
        public static UiControlDefinition AgreeCheck { get; } = new(
            "AgreeCheck",
            UiControlType.CheckBox,
            "AgreeCheck");

        public static UiControlDefinition SaveButton { get; } = new(
            "SaveButton",
            UiControlType.Button,
            "SaveButton");
    }

    public static class ListPageDefinitions
    {
        public static UiControlDefinition HierarchySelectionList { get; } = new(
            "HierarchySelectionList",
            UiControlType.ListBox,
            "HierarchySelectionList");
    }

    public static class TreePageDefinitions
    {
        public static UiControlDefinition DemoTree { get; } = new(
            "DemoTree",
            UiControlType.Tree,
            "DemoTree");
    }

    public static class GridPageDefinitions
    {
        public static UiControlDefinition EremexDemoDataGridAutomationBridge { get; } = new(
            "EremexDemoDataGridAutomationBridge",
            UiControlType.Grid,
            "EremexDemoDataGridAutomationBridge");
    }

    public static class FilterPageDefinitions
    {
        public static UiControlDefinition CreatedAtFilter { get; } = new(
            "CreatedAtFilter",
            UiControlType.DateRangeFilter,
            "CreatedAtFilter");

        public static UiControlDefinition AmountFilter { get; } = new(
            "AmountFilter",
            UiControlType.NumericRangeFilter,
            "AmountFilter");
    }

    public static class WorkflowPageDefinitions
    {
        public static UiControlDefinition DeleteDialog { get; } = new(
            "DeleteDialog",
            UiControlType.Dialog,
            "DeleteDialog");

        public static UiControlDefinition ExportToast { get; } = new(
            "ExportToast",
            UiControlType.Notification,
            "ExportToast");

        public static UiControlDefinition ReportExport { get; } = new(
            "ReportExport",
            UiControlType.FolderExport,
            "ReportExport");

        public static UiControlDefinition Shell { get; } = new(
            "Shell",
            UiControlType.ShellNavigation,
            "Shell");
    }

    private sealed class ComboPage : UiPage
    {
        public ComboPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IComboBoxControl OperationCombo => Resolve<IComboBoxControl>(ComboPageDefinitions.OperationCombo);
    }

    private sealed class DiagnosticsPage : UiPage
    {
        public DiagnosticsPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ILabelControl ResultLabel => Resolve<ILabelControl>(DiagnosticsPageDefinitions.ResultLabel);
    }

    private sealed class TabsPage : UiPage
    {
        public TabsPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ITabItemControl TasksTabItem => Resolve<ITabItemControl>(TabsPageDefinitions.TasksTabItem);
    }

    private sealed class TextPage : UiPage
    {
        public TextPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ITextBoxControl SearchBox => Resolve<ITextBoxControl>(TextPageDefinitions.SearchBox);
    }

    private sealed class StatePage : UiPage
    {
        public StatePage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ICheckBoxControl AgreeCheck => Resolve<ICheckBoxControl>(StatePageDefinitions.AgreeCheck);

        public IButtonControl SaveButton => Resolve<IButtonControl>(StatePageDefinitions.SaveButton);
    }

    private sealed class ListPage : UiPage
    {
        public ListPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IListBoxControl HierarchySelectionList => Resolve<IListBoxControl>(ListPageDefinitions.HierarchySelectionList);
    }

    private sealed class TreePage : UiPage
    {
        public TreePage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ITreeControl DemoTree => Resolve<ITreeControl>(TreePageDefinitions.DemoTree);
    }

    private sealed class GridPage : UiPage
    {
        public GridPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IGridControl EremexDemoDataGridAutomationBridge => Resolve<IGridControl>(GridPageDefinitions.EremexDemoDataGridAutomationBridge);
    }

    private sealed class FilterPage : UiPage
    {
        public FilterPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IDateRangeFilterControl CreatedAtFilter => Resolve<IDateRangeFilterControl>(FilterPageDefinitions.CreatedAtFilter);

        public INumericRangeFilterControl AmountFilter => Resolve<INumericRangeFilterControl>(FilterPageDefinitions.AmountFilter);
    }

    private sealed class WorkflowPage : UiPage
    {
        public WorkflowPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IDialogControl DeleteDialog => Resolve<IDialogControl>(WorkflowPageDefinitions.DeleteDialog);

        public INotificationControl ExportToast => Resolve<INotificationControl>(WorkflowPageDefinitions.ExportToast);

        public IFolderExportControl ReportExport => Resolve<IFolderExportControl>(WorkflowPageDefinitions.ReportExport);

        public IShellNavigationControl Shell => Resolve<IShellNavigationControl>(WorkflowPageDefinitions.Shell);
    }

    private sealed class FakeResolver : IUiControlResolver, IUiArtifactCollector
    {
        private readonly Dictionary<string, object> _controls;
        private readonly IReadOnlyList<UiFailureArtifact> _artifacts;

        public FakeResolver(params (string PropertyName, object Control)[] controls)
            : this(controls, artifacts: Array.Empty<UiFailureArtifact>())
        {
        }

        public FakeResolver((string PropertyName, object Control)[] controls, IReadOnlyList<UiFailureArtifact> artifacts)
        {
            _controls = controls.ToDictionary(static entry => entry.PropertyName, static entry => entry.Control, StringComparer.Ordinal);
            _artifacts = artifacts;
        }

        public UiRuntimeCapabilities Capabilities { get; } = new("fake-runtime");

        public ValueTask<IReadOnlyList<UiFailureArtifact>> CollectAsync(
            UiFailureContext failureContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_artifacts);
        }

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            return _controls.TryGetValue(definition.PropertyName, out var control)
                ? (control as TControl
                    ?? throw new InvalidOperationException($"Control '{definition.PropertyName}' is not of expected type."))
                : throw new InvalidOperationException($"Unknown control '{definition.PropertyName}'.");
        }
    }

    private abstract class FakeControlBase : IUiControl
    {
        protected FakeControlBase(string automationId, string name)
        {
            AutomationId = automationId;
            Name = name;
        }

        public string AutomationId { get; }

        public string Name { get; protected set; }

        public bool IsEnabled { get; init; } = true;
    }

    private sealed class FakeDateRangeFilterControl : FakeControlBase, IDateRangeFilterControl
    {
        public FakeDateRangeFilterControl(string automationId)
            : base(automationId, automationId)
        {
        }

        public DateTime? FromValue { get; private set; }

        public DateTime? ToValue { get; private set; }

        public Exception? Failure { get; init; }

        public int OpenCount { get; private set; }

        public List<DateRangeFilterRequest> Requests { get; } = [];

        public void Open()
        {
            OpenCount++;
        }

        public void SetRange(DateRangeFilterRequest request)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Requests.Add(request);
            Open();
            if (request.CommitMode == FilterPopupCommitMode.Cancel)
            {
                return;
            }

            FromValue = request.From ?? FromValue;
            ToValue = request.To ?? ToValue;
        }
    }

    private sealed class FakeNumericRangeFilterControl : FakeControlBase, INumericRangeFilterControl
    {
        public FakeNumericRangeFilterControl(string automationId)
            : base(automationId, automationId)
        {
        }

        public double? FromValue { get; set; }

        public double? ToValue { get; set; }

        public int OpenCount { get; private set; }

        public List<NumericRangeFilterRequest> Requests { get; } = [];

        public void Open()
        {
            OpenCount++;
        }

        public void SetRange(NumericRangeFilterRequest request)
        {
            Requests.Add(request);
            Open();
            if (request.CommitMode == FilterPopupCommitMode.Cancel)
            {
                return;
            }

            FromValue = request.From ?? FromValue;
            ToValue = request.To ?? ToValue;
        }
    }

    private sealed class FakeDialogControl : FakeControlBase, IDialogControl
    {
        public FakeDialogControl(string automationId, string messageText)
            : base(automationId, automationId)
        {
            MessageText = messageText;
        }

        public string MessageText { get; set; }

        public Exception? Failure { get; init; }

        public List<DialogActionKind> CompletedActions { get; } = [];

        public void Complete(DialogActionKind actionKind = DialogActionKind.Confirm)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            CompletedActions.Add(actionKind);
        }
    }

    private sealed class FakeNotificationControl : FakeControlBase, INotificationControl
    {
        public FakeNotificationControl(string automationId, string text)
            : base(automationId, automationId)
        {
            Text = text;
        }

        public string Text { get; set; }

        public Exception? Failure { get; init; }

        public int DismissCount { get; private set; }

        public void Dismiss()
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            DismissCount++;
        }
    }

    private sealed class FakeFolderExportControl : FakeControlBase, IFolderExportControl
    {
        public FakeFolderExportControl(string automationId)
            : base(automationId, automationId)
        {
        }

        public string? SelectedFolderPath { get; private set; }

        public string? StatusText { get; init; }

        public Exception? Failure { get; init; }

        public List<(string FolderPath, FolderExportCommitMode CommitMode)> Requests { get; } = [];

        public void SelectFolder(string folderPath, FolderExportCommitMode commitMode = FolderExportCommitMode.Select)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Requests.Add((folderPath, commitMode));
            if (commitMode == FolderExportCommitMode.Select)
            {
                SelectedFolderPath = folderPath;
            }
        }
    }

    private sealed class FakeShellNavigationControl : FakeControlBase, IShellNavigationControl
    {
        private readonly List<string> _openPaneNames = [];

        public FakeShellNavigationControl(string automationId)
            : base(automationId, automationId)
        {
        }

        public string? ActivePaneName { get; private set; }

        public IReadOnlyList<string> OpenPaneNames => _openPaneNames;

        public Exception? Failure { get; init; }

        public List<ShellPaneNavigationRequest> Requests { get; } = [];

        public void OpenOrActivate(ShellPaneNavigationRequest request)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Requests.Add(request);
            ActivePaneName = request.PaneName;
            if (!_openPaneNames.Contains(request.PaneName, StringComparer.OrdinalIgnoreCase))
            {
                _openPaneNames.Add(request.PaneName);
            }
        }
    }

    private sealed class FakeLabelControl : FakeControlBase, ILabelControl
    {
        public FakeLabelControl(string automationId, string text)
            : base(automationId, text)
        {
        }

        public string Text => Name;
    }

    private sealed class ThrowingLabelControl : ILabelControl
    {
        private readonly string _errorMessage;

        public ThrowingLabelControl(string automationId, string errorMessage)
        {
            AutomationId = automationId;
            _errorMessage = errorMessage;
        }

        public string AutomationId { get; }

        public string Name => throw new InvalidOperationException(_errorMessage);

        public bool IsEnabled => true;

        public string Text => throw new InvalidOperationException(_errorMessage);
    }

    private sealed class FakeComboBoxControl : FakeControlBase, IComboBoxControl
    {
        private readonly IReadOnlyList<IComboBoxItem> _items;

        public FakeComboBoxControl(string automationId, IReadOnlyList<IComboBoxItem> items)
            : base(automationId, automationId)
        {
            _items = items;
        }

        public IReadOnlyList<IComboBoxItem> Items => _items;

        public IComboBoxItem? SelectedItem => SelectedIndex >= 0 && SelectedIndex < _items.Count
            ? _items[SelectedIndex]
            : null;

        public int SelectedIndex { get; set; } = -1;

        public void SelectByIndex(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            SelectedIndex = index;
        }

        public void Expand()
        {
        }
    }

    private sealed class FakeTextBoxControl : FakeControlBase, ITextBoxControl
    {
        public FakeTextBoxControl(string automationId, string text)
            : base(automationId, automationId)
        {
            Text = text;
        }

        public string Text { get; set; }

        public void Enter(string value)
        {
            Text = value;
        }
    }

    private sealed class FakeCheckBoxControl : FakeControlBase, ICheckBoxControl
    {
        public FakeCheckBoxControl(string automationId)
            : base(automationId, automationId)
        {
        }

        public bool? IsChecked { get; set; }
    }

    private sealed class FakeButtonControl : FakeControlBase, IButtonControl
    {
        public FakeButtonControl(string automationId)
            : base(automationId, automationId)
        {
        }

        public int InvokeCount { get; private set; }

        public void Invoke()
        {
            InvokeCount++;
        }
    }

    private sealed class FakeTabItemControl : FakeControlBase, ITabItemControl
    {
        public FakeTabItemControl(string automationId)
            : base(automationId, automationId)
        {
        }

        public bool IsSelected { get; private set; }

        public void SelectTab()
        {
            IsSelected = true;
        }
    }

    private class FakeListBoxControl : FakeControlBase, IListBoxControl
    {
        public FakeListBoxControl(string automationId, IReadOnlyList<IListBoxItem> items)
            : base(automationId, automationId)
        {
            Items = items;
        }

        public IReadOnlyList<IListBoxItem> Items { get; }
    }

    private sealed class FakeSelectableListBoxControl : FakeListBoxControl, ISelectableListBoxControl
    {
        public FakeSelectableListBoxControl(string automationId, IReadOnlyList<IListBoxItem> items)
            : base(automationId, items)
        {
        }

        public string? SelectedItemText { get; private set; }

        public void SelectItem(string itemText)
        {
            var match = Items.FirstOrDefault(candidate =>
                string.Equals(candidate.Text, itemText, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new InvalidOperationException($"ListBox item '{itemText}' was not found.");
            }

            SelectedItemText = match.Text;
        }
    }

    private sealed class FakeTreeControl : FakeControlBase, ITreeControl
    {
        private IReadOnlyList<ITreeItemControl> _items = Array.Empty<ITreeItemControl>();

        public FakeTreeControl(string automationId)
            : base(automationId, automationId)
        {
        }

        public IReadOnlyList<ITreeItemControl> Items => _items;

        public ITreeItemControl? SelectedTreeItem { get; set; }

        public void SetItems(params ITreeItemControl[] items)
        {
            _items = items;
        }
    }

    private sealed class FakeTreeItemControl : FakeControlBase, ITreeItemControl
    {
        private IReadOnlyList<ITreeItemControl> _items = Array.Empty<ITreeItemControl>();

        public FakeTreeItemControl(string automationId, string name, string text)
            : base(automationId, name)
        {
            Text = text;
        }

        public bool IsSelected { get; set; }

        public string Text { get; }

        public IReadOnlyList<ITreeItemControl> Items => _items;

        public Action? OnSelect { get; init; }

        public void SetItems(params ITreeItemControl[] items)
        {
            _items = items;
        }

        public void Expand()
        {
        }

        public void SelectNode()
        {
            OnSelect?.Invoke();
        }
    }

    private class FakeGridControl : FakeControlBase, IGridControl
    {
        public FakeGridControl(string automationId, IReadOnlyList<IGridRowControl> rows)
            : base(automationId, automationId)
        {
            Rows = rows;
        }

        public IReadOnlyList<IGridRowControl> Rows { get; }

        public IGridRowControl? GetRowByIndex(int index)
        {
            return index >= 0 && index < Rows.Count
                ? Rows[index]
                : null;
        }
    }

    private sealed class FakeGridUserActionControl : FakeGridControl, IGridUserActionControl
    {
        public FakeGridUserActionControl(string automationId, IReadOnlyList<IGridRowControl> rows)
            : base(automationId, rows)
        {
        }

        public int? OpenedRowIndex { get; private set; }

        public string? SortedColumnName { get; private set; }

        public int ScrollToEndCount { get; private set; }

        public (int RowIndex, int ColumnIndex, string Value)? CopiedCell { get; private set; }

        public int ExportCount { get; private set; }

        public void OpenRow(int rowIndex)
        {
            OpenedRowIndex = rowIndex;
        }

        public void SortByColumn(string columnName)
        {
            SortedColumnName = columnName;
        }

        public void ScrollToEnd()
        {
            ScrollToEndCount++;
        }

        public string CopyCell(int rowIndex, int columnIndex)
        {
            var value = GetRowByIndex(rowIndex)?.Cells[columnIndex].Value
                ?? throw new InvalidOperationException("Cell was not found.");
            CopiedCell = (rowIndex, columnIndex, value);
            return value;
        }

        public void Export()
        {
            ExportCount++;
        }
    }

    private sealed class FakeEditableGridControl : FakeGridControl, IEditableGridControl
    {
        public FakeEditableGridControl(string automationId, IReadOnlyList<IGridRowControl> rows)
            : base(automationId, rows)
        {
        }

        public List<GridCellEditRequest> Requests { get; } = [];

        public void EditCell(GridCellEditRequest request)
        {
            Requests.Add(request);
            if (request.CommitMode == GridCellEditCommitMode.Cancel)
            {
                return;
            }

            var cell = GetRowByIndex(request.RowIndex)?.Cells[request.ColumnIndex] as MutableFakeGridCellControl
                ?? throw new InvalidOperationException("Editable fake grid cell was not found.");
            cell.Value = request.Value;
        }
    }

    private sealed record FakeGridRowControl(params IGridCellControl[] Cells) : IGridRowControl
    {
        IReadOnlyList<IGridCellControl> IGridRowControl.Cells => Cells;
    }

    private sealed record FakeGridCellControl(string Value) : IGridCellControl;

    private sealed class MutableFakeGridCellControl : IGridCellControl
    {
        public MutableFakeGridCellControl(string value)
        {
            Value = value;
        }

        public string Value { get; set; }
    }

    private sealed record FakeComboBoxItem(string Text, string Name) : IComboBoxItem;

    private sealed record FakeListBoxItem(string Text, string Name) : IListBoxItem;
}
