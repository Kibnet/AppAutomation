using System.Reflection;
using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class UiControlAdapterTests
{
    [Test]
    public async Task ComboBoxFilterAdapter_AppliesOneOrManyValuesAndReplaysCancel()
    {
        var context = CreateComboBoxFilterContext(hasApplyButton: true);

        context.Page
            .ApplyFilterSelection(static page => page.StatusFilter, [])
            .ApplyFilterSelection(static page => page.StatusFilter, ["Pending"])
            .ApplyFilterSelection(static page => page.StatusFilter, ["Pending", "Closed"])
            .CancelFilterSelection(static page => page.StatusFilter, ["Open"]);

        using (Assert.Multiple())
        {
            await Assert.That(context.Page.StatusFilter.SelectedItems).IsEquivalentTo(["Pending", "Closed"]);
            await Assert.That(context.Items.SelectedItems).IsEquivalentTo(["Pending", "Closed"]);
            await Assert.That(context.OpenButton.InvokeCount).IsEqualTo(4);
            await Assert.That(context.ApplyButton.InvokeCount).IsEqualTo(3);
            await Assert.That(context.CancelButton.InvokeCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task ComboBoxFilterAdapter_AppliesImmediateSelectionWithoutApplyButton()
    {
        var context = CreateComboBoxFilterContext(hasApplyButton: false);
        context.Items.OnSetSelectedItems = _ => context.Items.IsAvailable = false;

        context.Page.ApplyFilterSelection(static page => page.StatusFilter, ["Closed"]);

        using (Assert.Multiple())
        {
            await Assert.That(context.Page.StatusFilter.SelectedItems).IsEquivalentTo(["Closed"]);
            await Assert.That(context.OpenButton.InvokeCount).IsEqualTo(1);
            await Assert.That(context.ApplyButton.InvokeCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task MultiSelectAdapter_AppliesExactSelectionAndCancelRestoresCommittedItems()
    {
        var editorRoot = new FakeControl("CategoriesEditor");
        var items = new FakeMultiSelectItemsControl(
            "CategoriesItems",
            ["Alpha", "Beta", "Gamma"],
            ["Alpha"]);
        var openButton = new FakeButtonControl("CategoriesOpenButton")
        {
            OnInvoke = () => items.IsAvailable = true
        };
        var applyButton = new FakeButtonControl("CategoriesApplyButton")
        {
            OnInvoke = () => items.IsAvailable = false
        };
        var cancelButton = new FakeButtonControl("CategoriesCancelButton")
        {
            OnInvoke = () =>
            {
                items.SetSelectedItems(["Alpha", "Gamma"]);
                items.IsAvailable = false;
            }
        };
        var resolver = new FakeResolver(
                ("CategoriesEditor", editorRoot),
                ("CategoriesOpenButton", openButton),
                ("CategoriesItems", items),
                ("CategoriesApplyButton", applyButton),
                ("CategoriesCancelButton", cancelButton))
            .WithMultiSelect(
                "Categories",
                MultiSelectParts.ByAutomationIds(
                    "CategoriesEditor",
                    "CategoriesOpenButton",
                    "CategoriesItems",
                    "CategoriesApplyButton",
                    "CategoriesCancelButton"));
        var page = new MultiSelectPage(resolver);

        page
            .SelectMultiItems(static candidate => candidate.Categories, ["Gamma", "Alpha"])
            .CancelMultiSelection(static candidate => candidate.Categories, ["Beta"]);

        using (Assert.Multiple())
        {
            await Assert.That(page.Categories.IsOpen).IsFalse();
            await Assert.That(page.Categories.SelectedItems).IsEquivalentTo(["Alpha", "Gamma"]);
            await Assert.That(items.SelectedItems).IsEquivalentTo(["Alpha", "Gamma"]);
            await Assert.That(openButton.InvokeCount).IsEqualTo(2);
            await Assert.That(applyButton.InvokeCount).IsEqualTo(1);
            await Assert.That(cancelButton.InvokeCount).IsEqualTo(1);
        }

        await Assert.That(() => page.SelectMultiItems(
                static candidate => candidate.Categories,
                ["Missing"],
                timeoutMs: 50))
            .Throws<UiOperationException>();
        await Assert.That(() => page.SelectMultiItems(
                static candidate => candidate.Categories,
                ["Alpha", "alpha"],
                timeoutMs: 50))
            .Throws<UiOperationException>();
    }

    [Test]
    public async Task MultiSelectAdapter_ReReadsSelectionAfterPopupReopens()
    {
        var context = CreateComboBoxFilterContext(hasApplyButton: true);
        context.Page.ApplyFilterSelection(static page => page.StatusFilter, ["Pending"]);
        context.Items.SetSelectedItems(["Closed"]);

        context.Page.StatusFilter.Open();

        await Assert.That(context.Page.StatusFilter.SelectedItems).IsEquivalentTo(["Closed"]);
        context.Items.IsAvailable = false;
    }

    [Test]
    public async Task MultiSelectAdapter_ReadsInitialCommittedSelectionWhileClosed()
    {
        var context = CreateComboBoxFilterContext(hasApplyButton: true);

        var selectedItems = context.Page.StatusFilter.SelectedItems;

        using (Assert.Multiple())
        {
            await Assert.That(selectedItems).IsEquivalentTo(["Open"]);
            await Assert.That(context.Items.IsAvailable).IsFalse();
            await Assert.That(context.OpenButton.InvokeCount).IsEqualTo(1);
            await Assert.That(context.CancelButton.InvokeCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task MultiSelectAdapter_UsesProviderSelectionSnapshotWithoutPreReadingItems()
    {
        var context = CreateComboBoxFilterContext(hasApplyButton: true);

        context.Page.ApplyFilterSelection(static page => page.StatusFilter, ["Pending", "Closed"]);

        using (Assert.Multiple())
        {
            await Assert.That(context.Items.SelectionSnapshotCount).IsEqualTo(1);
            await Assert.That(context.Items.ItemsReadCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task CancelMultiSelection_ReusesKnownCommittedItemsWithoutScanningPopupSelection()
    {
        var context = CreateComboBoxFilterContext(hasApplyButton: true);
        context.Page.ApplyFilterSelection(static page => page.StatusFilter, ["Pending", "Closed"]);
        context.Items.ResetObservationCounts();

        context.Page.CancelFilterSelection(static page => page.StatusFilter, ["Open"]);

        await Assert.That(context.Items.SelectedItemsReadCount).IsEqualTo(0);
    }

    [Test]
    public async Task SearchPickerAdapter_SupportsSharedPageFlow()
    {
        var searchInput = new FakeTextBoxControl("HistoryFilterInput");
        var applyButton = new FakeButtonControl("ApplyFilterButton");
        var comboBox = new FakeComboBoxControl(
            "OperationCombo",
            new[]
            {
                new FakeComboBoxItem("Greatest Common Divisor", "Greatest Common Divisor"),
                new FakeComboBoxItem("Least Common Multiple", "Least Common Multiple")
            });

        var resolver = new FakeResolver(
            ("HistoryFilterInput", searchInput),
            ("ApplyFilterButton", applyButton),
            ("OperationCombo", comboBox))
            .WithSearchPicker(
                "HistoryOperationPicker",
                SearchPickerParts.ByAutomationIds(
                    "HistoryFilterInput",
                    "OperationCombo",
                    applyButtonAutomationId: "ApplyFilterButton"));
        var page = new SearchPickerPage(resolver);

        page.SearchAndSelect(
            static candidate => candidate.HistoryOperationPicker,
            "least",
            "Least Common Multiple");

        using (Assert.Multiple())
        {
            await Assert.That(page.HistoryOperationPicker.SearchText).IsEqualTo("least");
            await Assert.That(page.HistoryOperationPicker.SelectedItemText).IsEqualTo("Least Common Multiple");
            await Assert.That(page.HistoryOperationPicker.Items.Count).IsEqualTo(2);
            await Assert.That(applyButton.InvokeCount).IsEqualTo(1);
            await Assert.That(comboBox.SelectedIndex).IsEqualTo(1);
        }
    }

    [Test]
    public async Task SearchPickerAdapter_WaitsForActionsEnabledByEarlierPhases()
    {
        var searchInput = new FakeTextBoxControl("ProductPickerInput");
        var expandButton = new FakeButtonControl("ProductPickerExpand")
        {
            IsEnabled = false
        };
        var applyButton = new FakeButtonControl("ProductPickerApply")
        {
            IsEnabled = false,
            OnInvoke = () => EnableAfterDelay(expandButton)
        };
        searchInput.OnEnter = _ => EnableAfterDelay(applyButton);
        var results = new FakeSelectableListBoxControl(
            "ProductPickerResults",
            [new FakeListBoxItem("Item 42", "Item 42")]);
        var resolver = new FakeResolver(
                ("ProductPickerInput", searchInput),
                ("ProductPickerApply", applyButton),
                ("ProductPickerExpand", expandButton),
                ("ProductPickerResults", results))
            .WithSearchPicker(
                "ProductPicker",
                SearchPickerParts.ByAutomationIds(
                    "ProductPickerInput",
                    "ProductPickerResults",
                    "ProductPickerApply",
                    "ProductPickerExpand",
                    SearchPickerResultsKind.ListBox));
        var page = new ProductPickerPage(resolver);

        page.SearchAndSelect(
            static candidate => candidate.ProductPicker,
            "Item",
            "Item 42",
            timeoutMs: 1000);

        using (Assert.Multiple())
        {
            await Assert.That(applyButton.WasInvokedWhileDisabled).IsFalse();
            await Assert.That(expandButton.WasInvokedWhileDisabled).IsFalse();
            await Assert.That(results.SelectedItemText).IsEqualTo("Item 42");
        }
    }

    [Test]
    public async Task SearchPickerAdapter_ReflectsConfiguredActionButtonAvailability()
    {
        var searchInput = new FakeTextBoxControl("HistoryFilterInput");
        var applyButton = new FakeButtonControl("ApplyFilterButton");
        var expandButton = new FakeButtonControl("ExpandFilterButton");
        var comboBox = new FakeComboBoxControl(
            "OperationCombo",
            [new FakeComboBoxItem("Least Common Multiple", "Least Common Multiple")]);
        var resolver = new FakeResolver(
                ("HistoryFilterInput", searchInput),
                ("ApplyFilterButton", applyButton),
                ("ExpandFilterButton", expandButton),
                ("OperationCombo", comboBox))
            .WithSearchPicker(
                "HistoryOperationPicker",
                SearchPickerParts.ByAutomationIds(
                    "HistoryFilterInput",
                    "OperationCombo",
                    "ApplyFilterButton",
                    "ExpandFilterButton",
                    SearchPickerResultsKind.ComboBox));
        var picker = new SearchPickerPage(resolver).HistoryOperationPicker;

        applyButton.IsEnabled = false;
        await Assert.That(picker.IsEnabled).IsFalse();

        applyButton.IsEnabled = true;
        expandButton.IsEnabled = false;
        await Assert.That(picker.IsEnabled).IsFalse();

        expandButton.IsEnabled = true;
        await Assert.That(picker.IsEnabled).IsTrue();
    }

    [Test]
    public async Task SearchPickerParts_PreservesPublishedSevenValueApiShape()
    {
        var constructorParameterTypes = new[]
        {
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(UiLocatorKind),
            typeof(bool),
            typeof(SearchPickerResultsKind)
        };
        var factoryParameterTypes = new[]
        {
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(SearchPickerResultsKind)
        };
        var deconstructParameterTypes = constructorParameterTypes
            .Select(static type => type.MakeByRefType())
            .ToArray();

        var constructor = typeof(SearchPickerParts).GetConstructor(constructorParameterTypes);
        var factory = typeof(SearchPickerParts).GetMethod(
            nameof(SearchPickerParts.ByAutomationIds),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            factoryParameterTypes,
            modifiers: null);
        var deconstruct = typeof(SearchPickerParts).GetMethod(
            "Deconstruct",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            deconstructParameterTypes,
            modifiers: null);

        using (Assert.Multiple())
        {
            await Assert.That(constructor).IsNotNull();
            await Assert.That(factory).IsNotNull();
            await Assert.That(deconstruct).IsNotNull();
        }
    }

    [Test]
    public async Task SearchPickerAdapter_SupportsListBackedResultsFlow()
    {
        var searchInput = new FakeTextBoxControl("HistoryFilterInput");
        var applyButton = new FakeButtonControl("ApplyFilterButton");
        var expandButton = new FakeButtonControl("ExpandFilterButton");
        var listBox = new FakeSelectableListBoxControl(
            "OperationResults",
            new[]
            {
                new FakeListBoxItem("Greatest Common Divisor", "Greatest Common Divisor"),
                new FakeListBoxItem("Least Common Multiple", "Least Common Multiple")
            });

        var resolver = new FakeResolver(
            ("HistoryFilterInput", searchInput),
            ("ApplyFilterButton", applyButton),
            ("ExpandFilterButton", expandButton),
            ("OperationResults", listBox))
            .WithSearchPicker(
                "HistoryOperationPicker",
                SearchPickerParts.ByAutomationIds(
                    "HistoryFilterInput",
                    "OperationResults",
                    applyButtonAutomationId: "ApplyFilterButton",
                    expandButtonAutomationId: "ExpandFilterButton",
                    resultsKind: SearchPickerResultsKind.ListBox));
        var page = new SearchPickerPage(resolver);

        page.SearchAndSelect(
            static candidate => candidate.HistoryOperationPicker,
            "least",
            "Least Common Multiple");

        using (Assert.Multiple())
        {
            await Assert.That(page.HistoryOperationPicker.SearchText).IsEqualTo("least");
            await Assert.That(page.HistoryOperationPicker.SelectedItemText).IsEqualTo("Least Common Multiple");
            await Assert.That(page.HistoryOperationPicker.Items.Count).IsEqualTo(2);
            await Assert.That(applyButton.InvokeCount).IsEqualTo(1);
            await Assert.That(expandButton.InvokeCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task SearchPickerAdapter_SearchOpenedList_DoesNotTogglePopupClosed()
    {
        var searchInput = new FakeTextBoxControl("ServerSearchComboBox_Input");
        var expandButton = new FakeButtonControl("ServerSearchComboBox_OpenButton");
        var listBox = new FakeSelectableListBoxControl(
            "ServerSearchComboBox_Results",
            [
                new FakeListBoxItem("Product 42", "Product 42")
            ]);

        var resolver = new FakeResolver(
            ("ServerSearchComboBox_Input", searchInput),
            ("ServerSearchComboBox_OpenButton", expandButton),
            ("ServerSearchComboBox_Results", listBox))
            .WithSearchPicker(
                "ServerSearchComboBox",
                SearchPickerParts.ByAutomationIds(
                    "ServerSearchComboBox_Input",
                    "ServerSearchComboBox_Results",
                    expandButtonAutomationId: "ServerSearchComboBox_OpenButton",
                    resultsKind: SearchPickerResultsKind.ListBox,
                    opensOnSearch: true));
        var page = new ServerSearchComboBoxPage(resolver);
        expandButton.IsEnabled = false;

        var isEnabledWithoutExpandAction = page.ServerSearchComboBox.IsEnabled;

        page.SearchAndSelect(
            static candidate => candidate.ServerSearchComboBox,
            "product",
            "Product 42");

        using (Assert.Multiple())
        {
            await Assert.That(isEnabledWithoutExpandAction).IsTrue();
            await Assert.That(expandButton.InvokeCount).IsEqualTo(0);
            await Assert.That(listBox.SelectedItemText).IsEqualTo("Product 42");
        }
    }

    [Test]
    public async Task SearchPickerAdapter_SelectedItemText_DoesNotFallbackToSearchInputBeforeSelection()
    {
        var searchInput = new FakeTextBoxControl("HistoryFilterInput");
        var expandButton = new FakeButtonControl("ExpandFilterButton");
        var listBox = new FakeSelectableListBoxControl(
            "OperationResults",
            new[]
            {
                new FakeListBoxItem("Least Common Multiple", "Least Common Multiple")
            });

        var resolver = new FakeResolver(
            ("HistoryFilterInput", searchInput),
            ("ExpandFilterButton", expandButton),
            ("OperationResults", listBox))
            .WithSearchPicker(
                "HistoryOperationPicker",
                SearchPickerParts.ByAutomationIds(
                    "HistoryFilterInput",
                    "OperationResults",
                    expandButtonAutomationId: "ExpandFilterButton",
                    resultsKind: SearchPickerResultsKind.ListBox));
        var page = new SearchPickerPage(resolver);
        var picker = page.HistoryOperationPicker;

        picker.Search("Least Common Multiple");

        await Assert.That(picker.SelectedItemText).IsNull();
    }

    [Test]
    public async Task SearchPickerAdapter_WaitsForDelayedListBackedResultsAfterExpand()
    {
        var searchInput = new FakeTextBoxControl("OrderCustomerSearch_Input");
        var expandButton = new FakeButtonControl("OrderCustomerSearch_OpenButton");
        var listBox = new DelayedFakeSelectableListBoxControl(
            "OrderCustomerSearch_Results",
            expandButton,
            [
                new FakeListBoxItem("АЭРОСКАН ООО", "АЭРОСКАН ООО")
            ]);

        var resolver = new FakeResolver(
            ("OrderCustomerSearch_Input", searchInput),
            ("OrderCustomerSearch_OpenButton", expandButton),
            ("OrderCustomerSearch_Results", listBox))
            .WithSearchPicker(
                "OrderCustomerSearch",
                SearchPickerParts.ByAutomationIds(
                    "OrderCustomerSearch_Input",
                    "OrderCustomerSearch_Results",
                    expandButtonAutomationId: "OrderCustomerSearch_OpenButton",
                    resultsKind: SearchPickerResultsKind.ListBox));
        var page = new OrderCustomerSearchPage(resolver);

        page.SearchAndSelect(
            static candidate => candidate.OrderCustomerSearch,
            "АЭРОСКАН ООО",
            "АЭРОСКАН ООО",
            timeoutMs: 1000);

        using (Assert.Multiple())
        {
            await Assert.That(searchInput.Text).IsEqualTo("АЭРОСКАН ООО");
            await Assert.That(expandButton.InvokeCount).IsEqualTo(1);
            await Assert.That(listBox.ItemsReadCount >= 2).IsEqualTo(true);
            await Assert.That(listBox.SelectedItemText).IsEqualTo("АЭРОСКАН ООО");
        }
    }

    [Test]
    public async Task SearchPickerAdapter_DefersDetachedResultsAndRetainsSelectionAfterClose()
    {
        var searchInput = new FakeTextBoxControl("OrderCustomerSearch_Input");
        var expandButton = new FakeButtonControl("OrderCustomerSearch_OpenButton");
        var listBox = new FakeSelectableListBoxControl(
            "OrderCustomerSearch_Results",
            [
                new FakeListBoxItem("АЭРОСКАН ООО", "АЭРОСКАН ООО")
            ]);

        var innerResolver = new PopupResultsFakeResolver(
            expandButton,
            ("OrderCustomerSearch_Input", searchInput),
            ("OrderCustomerSearch_OpenButton", expandButton),
            ("OrderCustomerSearch_Results", listBox));
        var resolver = innerResolver
            .WithSearchPicker(
                "OrderCustomerSearch",
                SearchPickerParts.ByAutomationIds(
                    "OrderCustomerSearch_Input",
                    "OrderCustomerSearch_Results",
                    expandButtonAutomationId: "OrderCustomerSearch_OpenButton",
                    resultsKind: SearchPickerResultsKind.ListBox));
        var page = new OrderCustomerSearchPage(resolver);

        page.SearchAndSelect(
            static candidate => candidate.OrderCustomerSearch,
            "АЭРОСКАН ООО",
            "АЭРОСКАН ООО",
            timeoutMs: 1000);
        innerResolver.ResultsAvailable = false;

        using (Assert.Multiple())
        {
            await Assert.That(expandButton.InvokeCount).IsEqualTo(1);
            await Assert.That(innerResolver.ResultsResolveAttemptsBeforeExpand).IsEqualTo(0);
            await Assert.That(page.OrderCustomerSearch.SelectedItemText).IsEqualTo("АЭРОСКАН ООО");
        }

        searchInput.Text = "Другой клиент";
        await Assert.That(page.OrderCustomerSearch.SelectedItemText).IsNull();
    }

    [Test]
    public async Task SearchPickerAdapter_SelectItemDirectly_ExpandsDetachedListBackedResultsOnce()
    {
        var searchInput = new FakeTextBoxControl("OrderCustomerSearch_Input");
        var expandButton = new FakeButtonControl("OrderCustomerSearch_OpenButton");
        var listBox = new FakeSelectableListBoxControl(
            "OrderCustomerSearch_Results",
            [
                new FakeListBoxItem("АЭРОСКАН ООО", "АЭРОСКАН ООО")
            ]);

        var innerResolver = new PopupResultsFakeResolver(
            expandButton,
            ("OrderCustomerSearch_Input", searchInput),
            ("OrderCustomerSearch_OpenButton", expandButton),
            ("OrderCustomerSearch_Results", listBox));
        var resolver = innerResolver
            .WithSearchPicker(
                "OrderCustomerSearch",
                SearchPickerParts.ByAutomationIds(
                    "OrderCustomerSearch_Input",
                    "OrderCustomerSearch_Results",
                    expandButtonAutomationId: "OrderCustomerSearch_OpenButton",
                    resultsKind: SearchPickerResultsKind.ListBox));
        var page = new OrderCustomerSearchPage(resolver);

        page.OrderCustomerSearch.Search("АЭРОСКАН ООО");
        page.OrderCustomerSearch.SelectItem("АЭРОСКАН ООО");

        using (Assert.Multiple())
        {
            await Assert.That(searchInput.Text).IsEqualTo("АЭРОСКАН ООО");
            await Assert.That(expandButton.InvokeCount).IsEqualTo(1);
            await Assert.That(innerResolver.ResultsResolveAttemptsBeforeExpand).IsEqualTo(0);
            await Assert.That(listBox.SelectedItemText).IsEqualTo("АЭРОСКАН ООО");
        }
    }

    [Test]
    public async Task SearchPickerAdapter_WithInputPartTarget_StillResolvesCompositeControl()
    {
        var searchInput = new FakeTextBoxControl("OrderCustomerSearch_Input");
        var listBox = new FakeSelectableListBoxControl(
            "OrderCustomerSearch_Results",
            [
                new FakeListBoxItem("Customer Alpha", "Customer Alpha"),
                new FakeListBoxItem("Customer Beta", "Customer Beta")
            ]);

        var resolver = new FakeResolver(
            ("OrderCustomerSearch_Input", searchInput),
            ("OrderCustomerSearch_Results", listBox))
            .WithSearchPicker(
                "OrderCustomerSearch",
                SearchPickerParts.ByAutomationIds(
                    "OrderCustomerSearch_Input",
                    "OrderCustomerSearch_Results",
                    resultsKind: SearchPickerResultsKind.ListBox));
        var page = new SearchPickerInputPartPage(resolver);

        page.SearchAndSelect(
            static candidate => candidate.OrderCustomerSearch_Input,
            "alpha",
            "Customer Alpha");

        using (Assert.Multiple())
        {
            await Assert.That(searchInput.Text).IsEqualTo("alpha");
            await Assert.That(listBox.SelectedItemText).IsEqualTo("Customer Alpha");
            await Assert.That(page.OrderCustomerSearch_Input.SelectedItemText).IsEqualTo("Customer Alpha");
        }
    }

    [Test]
    public async Task DateRangeFilterAdapter_OpensSetsDateValuesAndApplies()
    {
        var openButton = new FakeButtonControl("OpenCreatedAtFilterButton");
        var applyButton = new FakeButtonControl("ApplyCreatedAtFilterButton");
        var cancelButton = new FakeButtonControl("CancelCreatedAtFilterButton");
        var fromEditor = new FakeDateTimePickerControl("CreatedAtFromEditor");
        var toEditor = new FakeDateTimePickerControl("CreatedAtToEditor");
        var resolver = new FakeResolver(
            ("OpenCreatedAtFilterButton", openButton),
            ("ApplyCreatedAtFilterButton", applyButton),
            ("CancelCreatedAtFilterButton", cancelButton),
            ("CreatedAtFromEditor", fromEditor),
            ("CreatedAtToEditor", toEditor))
            .WithDateRangeFilter(
                "CreatedAtFilter",
                DateRangeFilterParts.ByAutomationIds(
                    "CreatedAtFromEditor",
                    "CreatedAtToEditor",
                    "ApplyCreatedAtFilterButton",
                    "CancelCreatedAtFilterButton",
                    openButtonAutomationId: "OpenCreatedAtFilterButton"));
        var page = new FilterPage(resolver);

        page.CreatedAtFilter.SetRange(new DateRangeFilterRequest(
            new DateTime(2026, 4, 1),
            new DateTime(2026, 4, 30)));

        using (Assert.Multiple())
        {
            await Assert.That(page.CreatedAtFilter.FromValue).IsEqualTo(new DateTime(2026, 4, 1));
            await Assert.That(page.CreatedAtFilter.ToValue).IsEqualTo(new DateTime(2026, 4, 30));
            await Assert.That(openButton.InvokeCount).IsEqualTo(1);
            await Assert.That(applyButton.InvokeCount).IsEqualTo(1);
            await Assert.That(cancelButton.InvokeCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task DateRangeFilterAdapter_SupportsTextEditorsAndCancel()
    {
        var applyButton = new FakeButtonControl("ApplyCreatedAtFilterButton");
        var cancelButton = new FakeButtonControl("CancelCreatedAtFilterButton");
        var fromEditor = new FakeTextBoxControl("CreatedAtFromEditor");
        var toEditor = new FakeTextBoxControl("CreatedAtToEditor");
        var resolver = new FakeResolver(
            ("ApplyCreatedAtFilterButton", applyButton),
            ("CancelCreatedAtFilterButton", cancelButton),
            ("CreatedAtFromEditor", fromEditor),
            ("CreatedAtToEditor", toEditor))
            .WithDateRangeFilter(
                "CreatedAtFilter",
                DateRangeFilterParts.ByAutomationIds(
                    "CreatedAtFromEditor",
                    "CreatedAtToEditor",
                    "ApplyCreatedAtFilterButton",
                    "CancelCreatedAtFilterButton",
                    editorKind: FilterValueEditorKind.TextBox));
        var page = new FilterPage(resolver);

        page.CreatedAtFilter.SetRange(new DateRangeFilterRequest(
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 31),
            FilterPopupCommitMode.Cancel));

        using (Assert.Multiple())
        {
            await Assert.That(fromEditor.Text).IsEqualTo("2026-05-01");
            await Assert.That(toEditor.Text).IsEqualTo("2026-05-31");
            await Assert.That(applyButton.InvokeCount).IsEqualTo(0);
            await Assert.That(cancelButton.InvokeCount).IsEqualTo(1);
            await Assert.That(page.CreatedAtFilter.FromValue).IsEqualTo(new DateTime(2026, 5, 1));
            await Assert.That(page.CreatedAtFilter.ToValue).IsEqualTo(new DateTime(2026, 5, 31));
        }
    }

    [Test]
    public async Task NumericRangeFilterAdapter_SetsSpinnerValuesAndApplies()
    {
        var openButton = new FakeButtonControl("OpenAmountFilterButton");
        var applyButton = new FakeButtonControl("ApplyAmountFilterButton");
        var cancelButton = new FakeButtonControl("CancelAmountFilterButton");
        var fromEditor = new FakeSpinnerControl("AmountFromEditor");
        var toEditor = new FakeSpinnerControl("AmountToEditor");
        var resolver = new FakeResolver(
            ("OpenAmountFilterButton", openButton),
            ("ApplyAmountFilterButton", applyButton),
            ("CancelAmountFilterButton", cancelButton),
            ("AmountFromEditor", fromEditor),
            ("AmountToEditor", toEditor))
            .WithNumericRangeFilter(
                "AmountFilter",
                NumericRangeFilterParts.ByAutomationIds(
                    "AmountFromEditor",
                    "AmountToEditor",
                    "ApplyAmountFilterButton",
                    "CancelAmountFilterButton",
                    openButtonAutomationId: "OpenAmountFilterButton"));
        var page = new FilterPage(resolver);

        page.AmountFilter.SetRange(new NumericRangeFilterRequest(10.5, 42.25));

        using (Assert.Multiple())
        {
            await Assert.That(page.AmountFilter.FromValue).IsEqualTo(10.5);
            await Assert.That(page.AmountFilter.ToValue).IsEqualTo(42.25);
            await Assert.That(openButton.InvokeCount).IsEqualTo(1);
            await Assert.That(applyButton.InvokeCount).IsEqualTo(1);
            await Assert.That(cancelButton.InvokeCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task NumericRangeFilterAdapter_SupportsTextEditorsAndCancel()
    {
        var applyButton = new FakeButtonControl("ApplyAmountFilterButton");
        var cancelButton = new FakeButtonControl("CancelAmountFilterButton");
        var fromEditor = new FakeTextBoxControl("AmountFromEditor");
        var toEditor = new FakeTextBoxControl("AmountToEditor");
        var resolver = new FakeResolver(
            ("ApplyAmountFilterButton", applyButton),
            ("CancelAmountFilterButton", cancelButton),
            ("AmountFromEditor", fromEditor),
            ("AmountToEditor", toEditor))
            .WithNumericRangeFilter(
                "AmountFilter",
                NumericRangeFilterParts.ByAutomationIds(
                    "AmountFromEditor",
                    "AmountToEditor",
                    "ApplyAmountFilterButton",
                    "CancelAmountFilterButton",
                    editorKind: FilterValueEditorKind.TextBox));
        var page = new FilterPage(resolver);

        page.AmountFilter.SetRange(new NumericRangeFilterRequest(
            1000.125,
            2000.25,
            FilterPopupCommitMode.Cancel));

        using (Assert.Multiple())
        {
            await Assert.That(fromEditor.Text).IsEqualTo("1000.125");
            await Assert.That(toEditor.Text).IsEqualTo("2000.25");
            await Assert.That(applyButton.InvokeCount).IsEqualTo(0);
            await Assert.That(cancelButton.InvokeCount).IsEqualTo(1);
            await Assert.That(page.AmountFilter.FromValue).IsEqualTo(1000.125);
            await Assert.That(page.AmountFilter.ToValue).IsEqualTo(2000.25);
        }
    }

    [Test]
    public async Task DialogAdapter_ExposesMessageAndCompletesConfiguredActions()
    {
        var message = new FakeLabelControl("DeleteDialogMessage", "Delete selected record?");
        var confirmButton = new FakeButtonControl("ConfirmDeleteButton");
        var cancelButton = new FakeButtonControl("CancelDeleteButton");
        var dismissButton = new FakeButtonControl("DismissDeleteButton");
        var resolver = new FakeResolver(
            ("DeleteDialogMessage", message),
            ("ConfirmDeleteButton", confirmButton),
            ("CancelDeleteButton", cancelButton),
            ("DismissDeleteButton", dismissButton))
            .WithDialog(
                "DeleteDialog",
                DialogControlParts.ByAutomationIds(
                    "DeleteDialogMessage",
                    "ConfirmDeleteButton",
                    cancelButtonAutomationId: "CancelDeleteButton",
                    dismissButtonAutomationId: "DismissDeleteButton"));
        var page = new WorkflowPage(resolver);

        page.DeleteDialog.Complete();
        page.DeleteDialog.Complete(DialogActionKind.Cancel);
        page.DeleteDialog.Complete(DialogActionKind.Dismiss);

        using (Assert.Multiple())
        {
            await Assert.That(page.DeleteDialog.MessageText).IsEqualTo("Delete selected record?");
            await Assert.That(confirmButton.InvokeCount).IsEqualTo(1);
            await Assert.That(cancelButton.InvokeCount).IsEqualTo(1);
            await Assert.That(dismissButton.InvokeCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task NotificationAdapter_ExposesTextAndDismisses()
    {
        var text = new FakeLabelControl("ExportToastText", "Export completed");
        var dismissButton = new FakeButtonControl("DismissExportToastButton");
        var resolver = new FakeResolver(
            ("ExportToastText", text),
            ("DismissExportToastButton", dismissButton))
            .WithNotification(
                "ExportToast",
                NotificationControlParts.ByAutomationIds(
                    "ExportToastText",
                    dismissButtonAutomationId: "DismissExportToastButton"));
        var page = new WorkflowPage(resolver);

        page.ExportToast.Dismiss();

        using (Assert.Multiple())
        {
            await Assert.That(page.ExportToast.Text).IsEqualTo("Export completed");
            await Assert.That(dismissButton.InvokeCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task FolderExportAdapter_SelectModeOpensWritesPathAndSelects()
    {
        var openButton = new FakeButtonControl("OpenReportExportButton");
        var folderInput = new FakeTextBoxControl("ReportExportFolderInput");
        var selectButton = new FakeButtonControl("SelectReportExportFolderButton");
        var cancelButton = new FakeButtonControl("CancelReportExportFolderButton");
        var status = new FakeLabelControl("ReportExportStatus", "Export ready");
        var resolver = new FakeResolver(
            ("OpenReportExportButton", openButton),
            ("ReportExportFolderInput", folderInput),
            ("SelectReportExportFolderButton", selectButton),
            ("CancelReportExportFolderButton", cancelButton),
            ("ReportExportStatus", status))
            .WithFolderExport(
                "ReportExport",
                FolderExportControlParts.ByAutomationIds(
                    "OpenReportExportButton",
                    "ReportExportFolderInput",
                    "SelectReportExportFolderButton",
                    "CancelReportExportFolderButton",
                    statusAutomationId: "ReportExportStatus"));
        var page = new WorkflowPage(resolver);

        page.ReportExport.SelectFolder(@"C:\Exports\Reports");

        using (Assert.Multiple())
        {
            await Assert.That(page.ReportExport.SelectedFolderPath).IsEqualTo(@"C:\Exports\Reports");
            await Assert.That(page.ReportExport.StatusText).IsEqualTo("Export ready");
            await Assert.That(openButton.InvokeCount).IsEqualTo(1);
            await Assert.That(selectButton.InvokeCount).IsEqualTo(1);
            await Assert.That(cancelButton.InvokeCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task FolderExportAdapter_CancelModeOpensAndCancelsWithoutPathMutation()
    {
        var openButton = new FakeButtonControl("OpenReportExportButton");
        var folderInput = new FakeTextBoxControl("ReportExportFolderInput");
        var selectButton = new FakeButtonControl("SelectReportExportFolderButton");
        var cancelButton = new FakeButtonControl("CancelReportExportFolderButton");
        var resolver = new FakeResolver(
            ("OpenReportExportButton", openButton),
            ("ReportExportFolderInput", folderInput),
            ("SelectReportExportFolderButton", selectButton),
            ("CancelReportExportFolderButton", cancelButton))
            .WithFolderExport(
                "ReportExport",
                FolderExportControlParts.ByAutomationIds(
                    "OpenReportExportButton",
                    "ReportExportFolderInput",
                    "SelectReportExportFolderButton",
                    "CancelReportExportFolderButton"));
        var page = new WorkflowPage(resolver);

        page.ReportExport.SelectFolder(@"C:\Exports\Reports", FolderExportCommitMode.Cancel);

        using (Assert.Multiple())
        {
            await Assert.That(page.ReportExport.SelectedFolderPath).IsEqualTo(string.Empty);
            await Assert.That(openButton.InvokeCount).IsEqualTo(1);
            await Assert.That(selectButton.InvokeCount).IsEqualTo(0);
            await Assert.That(cancelButton.InvokeCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task ShellNavigationAdapter_OpensPaneThroughTreeNavigation()
    {
        var activePaneLabel = new FakeLabelControl("ActivePaneTitle", "Home");
        var customersNode = new FakeTreeItemControl("ShellNodeCustomers", "Customers", "Customers")
        {
            OnSelect = () => activePaneLabel.Text = "Customers"
        };
        var navigationTree = new FakeTreeControl("MainNavigation", customersNode);
        var resolver = new FakeResolver(
            ("MainNavigation", navigationTree),
            ("ActivePaneTitle", activePaneLabel))
            .WithShellNavigation(
                "Shell",
                ShellNavigationParts.ByAutomationIds(
                    "MainNavigation",
                    activePaneLabelAutomationId: "ActivePaneTitle"));
        var page = new WorkflowPage(resolver);

        page.Shell.OpenOrActivate(new ShellPaneNavigationRequest("Customers", ShellPaneNavigationMode.Open));

        using (Assert.Multiple())
        {
            await Assert.That(customersNode.SelectCount).IsEqualTo(1);
            await Assert.That(page.Shell.ActivePaneName).IsEqualTo("Customers");
        }
    }

    [Test]
    public async Task ShellNavigationAdapter_OpensPaneThroughSelectableListNavigation()
    {
        var navigationList = new FakeSelectableListBoxControl(
            "ShellNavigationList",
            [
                new FakeListBoxItem("Customers", "Customers"),
                new FakeListBoxItem("Reports", "Reports")
            ]);
        var resolver = new FakeResolver(("ShellNavigationList", navigationList))
            .WithShellNavigation(
                "Shell",
                ShellNavigationParts.ByAutomationIds(
                    "ShellNavigationList",
                    navigationKind: ShellNavigationSourceKind.ListBox));
        var page = new WorkflowPage(resolver);

        page.Shell.OpenOrActivate(new ShellPaneNavigationRequest("Reports", ShellPaneNavigationMode.Open));

        await Assert.That(navigationList.SelectedItemText).IsEqualTo("Reports");
    }

    [Test]
    public async Task ShellNavigationAdapter_OpenOrActivatePrefersExistingPaneTab()
    {
        var customersNode = new FakeTreeItemControl("ShellNodeCustomers", "Customers", "Customers");
        var navigationTree = new FakeTreeControl("MainNavigation", customersNode);
        var customersTab = new FakeTabItemControl("CustomersPaneTab", "Customers");
        var homeTab = new FakeTabItemControl("HomePaneTab", "Home") { IsSelected = true };
        var paneTabs = new FakeTabControl("DockPaneTabs", homeTab, customersTab);
        var resolver = new FakeResolver(
            ("MainNavigation", navigationTree),
            ("DockPaneTabs", paneTabs))
            .WithShellNavigation(
                "Shell",
                ShellNavigationParts.ByAutomationIds(
                    "MainNavigation",
                    paneTabsAutomationId: "DockPaneTabs"));
        var page = new WorkflowPage(resolver);

        page.Shell.OpenOrActivate(new ShellPaneNavigationRequest("Customers"));

        using (Assert.Multiple())
        {
            await Assert.That(customersNode.SelectCount).IsEqualTo(0);
            await Assert.That(customersTab.IsSelected).IsEqualTo(true);
            await Assert.That(page.Shell.ActivePaneName).IsEqualTo("Customers");
        }
    }

    [Test]
    public async Task ShellNavigationAdapter_ThrowsWhenActivationTabsAreMissing()
    {
        var navigationTree = new FakeTreeControl(
            "MainNavigation",
            new FakeTreeItemControl("ShellNodeCustomers", "Customers", "Customers"));
        var resolver = new FakeResolver(("MainNavigation", navigationTree))
            .WithShellNavigation(
                "Shell",
                ShellNavigationParts.ByAutomationIds("MainNavigation"));
        var page = new WorkflowPage(resolver);

        await Assert.That(() => page.Shell.OpenOrActivate(
                new ShellPaneNavigationRequest("Customers", ShellPaneNavigationMode.Activate)))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task ShellNavigationAdapter_ActivatesPaneWithoutNavigationSource_WhenPaneTabsAreConfigured()
    {
        var customersTab = new FakeTabItemControl("CustomersPaneTab", "Customers");
        var reportsTab = new FakeTabItemControl("ReportsPaneTab", "Reports") { IsSelected = true };
        var paneTabs = new FakeTabControl("DockPaneTabs", reportsTab, customersTab);
        var resolver = new FakeResolver(("DockPaneTabs", paneTabs))
            .WithShellNavigation(
                "Shell",
                ShellNavigationParts.ByAutomationIds(
                    navigationAutomationId: null,
                    paneTabsAutomationId: "DockPaneTabs"));
        var page = new WorkflowPage(resolver);

        page.Shell.OpenOrActivate(new ShellPaneNavigationRequest("Customers", ShellPaneNavigationMode.Activate));

        using (Assert.Multiple())
        {
            await Assert.That(customersTab.IsSelected).IsEqualTo(true);
            await Assert.That(page.Shell.ActivePaneName).IsEqualTo("Customers");
            await Assert.That(page.Shell.IsEnabled).IsEqualTo(true);
        }
    }

    [Test]
    public async Task ShellNavigationAdapter_OpenOrActivateWithoutNavigationSource_ThrowsExplicitDiagnostic()
    {
        var customersTab = new FakeTabItemControl("CustomersPaneTab", "Customers");
        var reportsTab = new FakeTabItemControl("ReportsPaneTab", "Reports") { IsSelected = true };
        var paneTabs = new FakeTabControl("DockPaneTabs", reportsTab, customersTab);
        var resolver = new FakeResolver(("DockPaneTabs", paneTabs))
            .WithShellNavigation(
                "Shell",
                ShellNavigationParts.ByAutomationIds(
                    navigationAutomationId: null,
                    paneTabsAutomationId: "DockPaneTabs"));
        var page = new WorkflowPage(resolver);

        var exception = await Assert.That(() => page.Shell.OpenOrActivate(
                new ShellPaneNavigationRequest("Invoices", ShellPaneNavigationMode.OpenOrActivate)))
            .Throws<NotSupportedException>();

        await Assert.That(exception!.Message).Contains("navigation source is not configured");
    }

    [Test]
    public async Task PrimitiveProxyAdapter_WithGenericProxy_ResolvesTextBoxThroughInnerLocator()
    {
        var textBox = new FakeTextBoxControl("ServerFilterInput");
        var resolver = new FakeResolver(("ServerFilterInput", textBox))
            .WithProxy(
                "ServerFilterEditor",
                PrimitiveProxyTarget.ByAutomationId("ServerFilterInput", UiControlType.TextBox));
        var page = new ProxyPage(resolver);

        page.EnterText(static candidate => candidate.ServerFilterEditor, "prod");

        using (Assert.Multiple())
        {
            await Assert.That(textBox.Text).IsEqualTo("prod");
            await Assert.That(page.ServerFilterEditor.AutomationId).IsEqualTo("ServerFilterInput");
        }
    }

    [Test]
    public async Task PrimitiveProxyAdapter_WithButtonProxy_InvokesInnerButton()
    {
        var button = new FakeButtonControl("SplitButtonPrimaryPart");
        var resolver = new FakeResolver(("SplitButtonPrimaryPart", button))
            .WithButtonProxy("SplitPrimaryAction", "SplitButtonPrimaryPart");
        var page = new ProxyPage(resolver);

        page.ClickButton(static candidate => candidate.SplitPrimaryAction);

        await Assert.That(button.InvokeCount).IsEqualTo(1);
    }

    [Test]
    public async Task PrimitiveProxyAdapter_WithListBoxProxy_PreservesSelectableListBehavior()
    {
        var listBox = new FakeSelectableListBoxControl(
            "ListViewItemsSurface",
            [
                new FakeListBoxItem("Customers", "Customers"),
                new FakeListBoxItem("Reports", "Reports")
            ]);
        var resolver = new FakeResolver(("ListViewItemsSurface", listBox))
            .WithListBoxProxy("ListGallery", "ListViewItemsSurface");
        var page = new ProxyPage(resolver);

        page.SelectListBoxItem(static candidate => candidate.ListGallery, "Reports");

        using (Assert.Multiple())
        {
            await Assert.That(listBox.SelectedItemText).IsEqualTo("Reports");
            await Assert.That(page.ListGallery.AutomationId).IsEqualTo("ListViewItemsSurface");
        }
    }

    [Test]
    public async Task WithAdaptersFromAssembly_RegistersAdaptersFromAssembly()
    {
        var resolver = new MinimalResolver()
            .WithAdaptersFromAssembly(typeof(TestableAdapter).Assembly);

        var definition = new UiControlDefinition(
            "TestProperty",
            UiControlType.AutomationElement,
            "TestLocator",
            UiLocatorKind.AutomationId,
            FallbackToName: false);

        var control = resolver.Resolve<IUiControl>(definition);

        await Assert.That(control.AutomationId).IsEqualTo("ResolvedByTestableAdapter");
    }

    [Test]
    public async Task WithDefaultAdapters_ReturnsResolverWithoutError()
    {
        var resolver = new MinimalResolver();

        var wrappedResolver = resolver.WithDefaultAdapters();

        await Assert.That(wrappedResolver).IsNotNull();
        await Assert.That(wrappedResolver.Capabilities.AdapterId).IsEqualTo("minimal-runtime");
    }

    [Test]
    public async Task WithAdaptersFromAssembly_SkipsAbstractAndInterfaceTypes()
    {
        var resolver = new MinimalResolver()
            .WithAdaptersFromAssembly(typeof(AbstractAdapter).Assembly);

        await Assert.That(resolver).IsNotNull();
    }

    [Test]
    public async Task WithAdaptersFromAssembly_SkipsAdaptersWithoutParameterlessConstructors()
    {
        var resolver = new MinimalResolver()
            .WithAdaptersFromAssembly(typeof(ParameterizedAdapter).Assembly);

        await Assert.That(resolver).IsNotNull();
        await Assert.That(resolver.Capabilities.AdapterId).IsEqualTo("minimal-runtime");
    }

    [Test]
    public async Task WithAdaptersFromAssembly_ThrowsOnNullResolver()
    {
        IUiControlResolver? nullResolver = null;

        await Assert.That(() => nullResolver!.WithAdaptersFromAssembly(typeof(TestableAdapter).Assembly))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task WithAdaptersFromAssembly_ThrowsOnNullAssembly()
    {
        var resolver = new MinimalResolver();

        await Assert.That(() => resolver.WithAdaptersFromAssembly(null!))
            .Throws<ArgumentNullException>();
    }

    private static ComboBoxFilterTestContext CreateComboBoxFilterContext(bool hasApplyButton)
    {
        var items = new FakeMultiSelectItemsControl(
            "StatusFilterItems",
            ["Open", "Pending", "Closed"],
            ["Open"]);
        var committedItems = new[] { "Open" };
        var openButton = new FakeButtonControl("StatusFilterOpenButton")
        {
            OnInvoke = () => items.IsAvailable = true
        };
        var applyButton = new FakeButtonControl("StatusFilterApplyButton")
        {
            OnInvoke = () =>
            {
                committedItems = items.SelectedItems.ToArray();
                items.IsAvailable = false;
            }
        };
        var cancelButton = new FakeButtonControl("StatusFilterCancelButton")
        {
            OnInvoke = () =>
            {
                items.SetSelectedItems(committedItems);
                items.IsAvailable = false;
            }
        };
        var resolver = new FakeResolver(
                ("StatusFilterRoot", new FakeControl("StatusFilterRoot")),
                ("StatusFilterOpenButton", openButton),
                ("StatusFilterItems", items),
                ("StatusFilterApplyButton", applyButton),
                ("StatusFilterCancelButton", cancelButton))
            .WithComboBoxFilter(
                "StatusFilter",
                ComboBoxFilterParts.ByAutomationIds(
                    "StatusFilterRoot",
                    "StatusFilterOpenButton",
                    "StatusFilterItems",
                    applyButtonAutomationId: hasApplyButton ? "StatusFilterApplyButton" : null,
                    cancelButtonAutomationId: hasApplyButton ? "StatusFilterCancelButton" : null));

        return new ComboBoxFilterTestContext(
            new ComboBoxFilterPage(resolver),
            items,
            openButton,
            applyButton,
            cancelButton);
    }

    private sealed record ComboBoxFilterTestContext(
        ComboBoxFilterPage Page,
        FakeMultiSelectItemsControl Items,
        FakeButtonControl OpenButton,
        FakeButtonControl ApplyButton,
        FakeButtonControl CancelButton);

    public static class SearchPickerPageDefinitions
    {
        public static UiControlDefinition HistoryOperationPicker { get; } = new(
            "HistoryOperationPicker",
            UiControlType.AutomationElement,
            "HistoryOperationPicker",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
    }

    public static class MultiSelectPageDefinitions
    {
        public static UiControlDefinition Categories { get; } = new(
            "Categories",
            UiControlType.MultiSelect,
            "Categories",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
    }

    public static class ComboBoxFilterPageDefinitions
    {
        public static UiControlDefinition StatusFilter { get; } = new(
            "StatusFilter",
            UiControlType.ComboBoxFilter,
            "StatusFilter",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
    }

    public static class SearchPickerInputPartPageDefinitions
    {
        public static UiControlDefinition OrderCustomerSearch_Input { get; } = new(
            "OrderCustomerSearch_Input",
            UiControlType.SearchPicker,
            "OrderCustomerSearch_Input",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
    }

    public static class OrderCustomerSearchPageDefinitions
    {
        public static UiControlDefinition OrderCustomerSearch { get; } = new(
            "OrderCustomerSearch",
            UiControlType.SearchPicker,
            "OrderCustomerSearch",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
    }

    public static class ServerSearchComboBoxPageDefinitions
    {
        public static UiControlDefinition ServerSearchComboBox { get; } = new(
            "ServerSearchComboBox",
            UiControlType.SearchPicker,
            "ServerSearchComboBox",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
    }

    public static class ProductPickerPageDefinitions
    {
        public static UiControlDefinition ProductPicker { get; } = new(
            "ProductPicker",
            UiControlType.SearchPicker,
            "ProductPicker",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
    }

    private sealed class SearchPickerPage : UiPage
    {
        public SearchPickerPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ISearchPickerControl HistoryOperationPicker => Resolve<ISearchPickerControl>(SearchPickerPageDefinitions.HistoryOperationPicker);
    }

    private sealed class MultiSelectPage : UiPage
    {
        public MultiSelectPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IMultiSelectControl Categories => Resolve<IMultiSelectControl>(MultiSelectPageDefinitions.Categories);
    }

    private sealed class ComboBoxFilterPage : UiPage
    {
        public ComboBoxFilterPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IComboBoxFilterControl StatusFilter =>
            Resolve<IComboBoxFilterControl>(ComboBoxFilterPageDefinitions.StatusFilter);
    }

    private sealed class OrderCustomerSearchPage : UiPage
    {
        public OrderCustomerSearchPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ISearchPickerControl OrderCustomerSearch =>
            Resolve<ISearchPickerControl>(OrderCustomerSearchPageDefinitions.OrderCustomerSearch);
    }

    private sealed class ServerSearchComboBoxPage : UiPage
    {
        public ServerSearchComboBoxPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ISearchPickerControl ServerSearchComboBox =>
            Resolve<ISearchPickerControl>(ServerSearchComboBoxPageDefinitions.ServerSearchComboBox);
    }

    private sealed class ProductPickerPage : UiPage
    {
        public ProductPickerPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ISearchPickerControl ProductPicker =>
            Resolve<ISearchPickerControl>(ProductPickerPageDefinitions.ProductPicker);
    }

    private sealed class SearchPickerInputPartPage : UiPage
    {
        public SearchPickerInputPartPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ISearchPickerControl OrderCustomerSearch_Input =>
            Resolve<ISearchPickerControl>(SearchPickerInputPartPageDefinitions.OrderCustomerSearch_Input);
    }

    public static class ProxyPageDefinitions
    {
        public static UiControlDefinition ServerFilterEditor { get; } = new(
            "ServerFilterEditor",
            UiControlType.TextBox,
            "ServerFilterEditor",
            UiLocatorKind.AutomationId,
            FallbackToName: false);

        public static UiControlDefinition SplitPrimaryAction { get; } = new(
            "SplitPrimaryAction",
            UiControlType.Button,
            "SplitPrimaryAction",
            UiLocatorKind.AutomationId,
            FallbackToName: false);

        public static UiControlDefinition ListGallery { get; } = new(
            "ListGallery",
            UiControlType.ListBox,
            "ListGallery",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
    }

    private sealed class ProxyPage : UiPage
    {
        public ProxyPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ITextBoxControl ServerFilterEditor => Resolve<ITextBoxControl>(ProxyPageDefinitions.ServerFilterEditor);

        public IButtonControl SplitPrimaryAction => Resolve<IButtonControl>(ProxyPageDefinitions.SplitPrimaryAction);

        public IListBoxControl ListGallery => Resolve<IListBoxControl>(ProxyPageDefinitions.ListGallery);
    }

    public static class FilterPageDefinitions
    {
        public static UiControlDefinition CreatedAtFilter { get; } = new(
            "CreatedAtFilter",
            UiControlType.DateRangeFilter,
            "CreatedAtFilter",
            UiLocatorKind.AutomationId,
            FallbackToName: false);

        public static UiControlDefinition AmountFilter { get; } = new(
            "AmountFilter",
            UiControlType.NumericRangeFilter,
            "AmountFilter",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
    }

    public static class WorkflowPageDefinitions
    {
        public static UiControlDefinition DeleteDialog { get; } = new(
            "DeleteDialog",
            UiControlType.Dialog,
            "DeleteDialog",
            UiLocatorKind.AutomationId,
            FallbackToName: false);

        public static UiControlDefinition ExportToast { get; } = new(
            "ExportToast",
            UiControlType.Notification,
            "ExportToast",
            UiLocatorKind.AutomationId,
            FallbackToName: false);

        public static UiControlDefinition ReportExport { get; } = new(
            "ReportExport",
            UiControlType.FolderExport,
            "ReportExport",
            UiLocatorKind.AutomationId,
            FallbackToName: false);

        public static UiControlDefinition Shell { get; } = new(
            "Shell",
            UiControlType.ShellNavigation,
            "Shell",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
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

    private sealed class FakeResolver : IUiControlResolver
    {
        private readonly Dictionary<string, object> _controls;

        public FakeResolver(params (string LocatorValue, object Control)[] controls)
        {
            _controls = controls.ToDictionary(static entry => entry.LocatorValue, static entry => entry.Control, StringComparer.Ordinal);
        }

        public UiRuntimeCapabilities Capabilities { get; } = new("fake-runtime");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            return _controls.TryGetValue(definition.LocatorValue, out var control)
                ? (control as TControl
                    ?? throw new InvalidOperationException($"Control '{definition.LocatorValue}' is not of expected type."))
                : throw new InvalidOperationException($"Unknown control '{definition.LocatorValue}'.");
        }
    }

    private sealed class PopupResultsFakeResolver : IUiControlResolver
    {
        private readonly FakeButtonControl _expandButton;
        private readonly Dictionary<string, object> _controls;

        public PopupResultsFakeResolver(FakeButtonControl expandButton, params (string LocatorValue, object Control)[] controls)
        {
            _expandButton = expandButton;
            _controls = controls.ToDictionary(static entry => entry.LocatorValue, static entry => entry.Control, StringComparer.Ordinal);
        }

        public int ResultsResolveAttemptsBeforeExpand { get; private set; }

        public bool ResultsAvailable { get; set; } = true;

        public UiRuntimeCapabilities Capabilities { get; } = new("fake-runtime");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            if (string.Equals(definition.LocatorValue, "OrderCustomerSearch_Results", StringComparison.Ordinal)
                && (_expandButton.InvokeCount == 0 || !ResultsAvailable))
            {
                ResultsResolveAttemptsBeforeExpand++;
                throw new InvalidOperationException("Popup results are not attached before expand.");
            }

            return _controls.TryGetValue(definition.LocatorValue, out var control)
                ? (control as TControl
                    ?? throw new InvalidOperationException($"Control '{definition.LocatorValue}' is not of expected type."))
                : throw new InvalidOperationException($"Unknown control '{definition.LocatorValue}'.");
        }
    }

    private abstract class FakeControlBase : IUiControl
    {
        protected FakeControlBase(string automationId)
        {
            AutomationId = automationId;
            Name = automationId;
        }

        public string AutomationId { get; }

        public string Name { get; protected set; }

        public bool IsEnabled { get; set; } = true;
    }

    private sealed class FakeTextBoxControl : FakeControlBase, ITextBoxControl
    {
        public FakeTextBoxControl(string automationId)
            : base(automationId)
        {
            Text = string.Empty;
        }

        public string Text { get; set; }

        public Action<string>? OnEnter { get; set; }

        public void Enter(string value)
        {
            Text = value;
            OnEnter?.Invoke(value);
        }
    }

    private sealed class FakeLabelControl : FakeControlBase, ILabelControl
    {
        public FakeLabelControl(string automationId, string text)
            : base(automationId)
        {
            Text = text;
            Name = text;
        }

        public string Text
        {
            get => Name;
            set => Name = value;
        }
    }

    private sealed class FakeButtonControl : FakeControlBase, IButtonControl
    {
        public FakeButtonControl(string automationId)
            : base(automationId)
        {
        }

        public int InvokeCount { get; private set; }

        public bool WasInvokedWhileDisabled { get; private set; }

        public Action? OnInvoke { get; init; }

        public void Invoke()
        {
            if (!IsEnabled)
            {
                WasInvokedWhileDisabled = true;
                throw new InvalidOperationException($"Button '{AutomationId}' is disabled.");
            }

            InvokeCount++;
            OnInvoke?.Invoke();
        }
    }

    private static void EnableAfterDelay(FakeControlBase control)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            control.IsEnabled = true;
        });
    }

    private sealed class FakeControl : FakeControlBase
    {
        public FakeControl(string automationId)
            : base(automationId)
        {
        }
    }

    private sealed class FakeMultiSelectItemsControl : FakeControlBase, IMultiSelectItemsControl, IUiControlAvailability
    {
        private readonly string[] _items;
        private string[] _selectedItems;

        public FakeMultiSelectItemsControl(
            string automationId,
            IReadOnlyCollection<string> items,
            IReadOnlyCollection<string> selectedItems)
            : base(automationId)
        {
            _items = items.ToArray();
            _selectedItems = selectedItems.ToArray();
        }

        public IReadOnlyList<string> Items
        {
            get
            {
                ItemsReadCount++;
                return _items;
            }
        }

        public IReadOnlyList<string> SelectedItems
        {
            get
            {
                SelectedItemsReadCount++;
                return _selectedItems;
            }
        }

        public bool IsAvailable { get; set; }

        public int ItemsReadCount { get; private set; }

        public int SelectionSnapshotCount { get; private set; }

        public int SelectedItemsReadCount { get; private set; }

        public Action<IReadOnlyCollection<string>>? OnSetSelectedItems { get; set; }

        public void SetSelectedItems(IReadOnlyCollection<string> values)
        {
            _selectedItems = values.ToArray();
            OnSetSelectedItems?.Invoke(values);
        }

        public IReadOnlyList<string> SetSelectedItemsAndGetAvailableItems(IReadOnlyCollection<string> values)
        {
            SelectionSnapshotCount++;
            var missingItems = values
                .Where(value => !_items.Contains(value, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (missingItems.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Multi-select items were not found: [{string.Join(", ", missingItems)}].");
            }

            SetSelectedItems(values);
            return _items;
        }

        public void ResetObservationCounts()
        {
            ItemsReadCount = 0;
            SelectedItemsReadCount = 0;
            SelectionSnapshotCount = 0;
        }
    }

    private sealed class FakeDateTimePickerControl : FakeControlBase, IDateTimePickerControl
    {
        public FakeDateTimePickerControl(string automationId)
            : base(automationId)
        {
        }

        public DateTime? SelectedDate { get; set; }
    }

    private sealed class FakeSpinnerControl : FakeControlBase, ISpinnerControl
    {
        public FakeSpinnerControl(string automationId)
            : base(automationId)
        {
        }

        public double Value { get; set; }
    }

    private sealed class FakeComboBoxControl : FakeControlBase, IComboBoxControl
    {
        private readonly IReadOnlyList<IComboBoxItem> _items;

        public FakeComboBoxControl(string automationId, IReadOnlyList<IComboBoxItem> items)
            : base(automationId)
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

    private sealed record FakeComboBoxItem(string Text, string Name) : IComboBoxItem;

    private sealed class FakeTabControl : FakeControlBase, ITabControl
    {
        private readonly IReadOnlyList<FakeTabItemControl> _items;

        public FakeTabControl(string automationId, params FakeTabItemControl[] items)
            : base(automationId)
        {
            _items = items;
        }

        public IReadOnlyList<ITabItemControl> Items => _items;

        public void SelectTabItem(string itemText)
        {
            var normalizedTarget = NormalizeLookupText(itemText);
            var item = _items.FirstOrDefault(candidate =>
                string.Equals(NormalizeLookupText(candidate.Name), normalizedTarget, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeLookupText(candidate.AutomationId), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Tab item '{itemText}' was not found.");

            foreach (var candidate in _items)
            {
                candidate.IsSelected = false;
            }

            item.SelectTab();
        }
    }

    private sealed class FakeTabItemControl : FakeControlBase, ITabItemControl
    {
        public FakeTabItemControl(string automationId, string name)
            : base(automationId)
        {
            Name = name;
        }

        public bool IsSelected { get; set; }

        public void SelectTab()
        {
            IsSelected = true;
        }
    }

    private sealed class FakeTreeControl : FakeControlBase, ITreeControl
    {
        public FakeTreeControl(string automationId, params ITreeItemControl[] items)
            : base(automationId)
        {
            Items = items;
        }

        public IReadOnlyList<ITreeItemControl> Items { get; }

        public ITreeItemControl? SelectedTreeItem { get; private set; }

        public void Select(FakeTreeItemControl item)
        {
            SelectedTreeItem = item;
        }
    }

    private sealed class FakeTreeItemControl : FakeControlBase, ITreeItemControl
    {
        private IReadOnlyList<ITreeItemControl> _items = Array.Empty<ITreeItemControl>();

        public FakeTreeItemControl(string automationId, string name, string text)
            : base(automationId)
        {
            Name = name;
            Text = text;
        }

        public bool IsSelected { get; set; }

        public string Text { get; }

        public IReadOnlyList<ITreeItemControl> Items => _items;

        public int SelectCount { get; private set; }

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
            SelectCount++;
            IsSelected = true;
            OnSelect?.Invoke();
        }
    }

    private sealed class FakeSelectableListBoxControl : FakeControlBase, ISelectableListBoxControl
    {
        public FakeSelectableListBoxControl(string automationId, IReadOnlyList<IListBoxItem> items)
            : base(automationId)
        {
            Items = items;
        }

        public IReadOnlyList<IListBoxItem> Items { get; }

        public string? SelectedItemText { get; private set; }

        public void SelectItem(string itemText)
        {
            var normalizedTarget = NormalizeLookupText(itemText);
            var item = Items.FirstOrDefault(candidate =>
                string.Equals(NormalizeLookupText(candidate.Text), normalizedTarget, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeLookupText(candidate.Name), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"List item '{itemText}' was not found.");

            SelectedItemText = item.Text ?? item.Name;
        }
    }

    private sealed class DelayedFakeSelectableListBoxControl : FakeControlBase, ISelectableListBoxControl
    {
        private readonly FakeButtonControl _expandButton;
        private readonly IReadOnlyList<IListBoxItem> _items;

        public DelayedFakeSelectableListBoxControl(
            string automationId,
            FakeButtonControl expandButton,
            IReadOnlyList<IListBoxItem> items)
            : base(automationId)
        {
            _expandButton = expandButton;
            _items = items;
        }

        public int ItemsReadCount { get; private set; }

        public IReadOnlyList<IListBoxItem> Items
        {
            get
            {
                if (_expandButton.InvokeCount == 0)
                {
                    return Array.Empty<IListBoxItem>();
                }

                ItemsReadCount++;
                return ItemsReadCount >= 2 ? _items : Array.Empty<IListBoxItem>();
            }
        }

        public string? SelectedItemText { get; private set; }

        public void SelectItem(string itemText)
        {
            var normalizedTarget = NormalizeLookupText(itemText);
            var item = Items.FirstOrDefault(candidate =>
                string.Equals(NormalizeLookupText(candidate.Text), normalizedTarget, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeLookupText(candidate.Name), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"List item '{itemText}' was not found.");

            SelectedItemText = item.Text ?? item.Name;
        }
    }

    private sealed record FakeListBoxItem(string? Text, string? Name) : IListBoxItem;

    private static string NormalizeLookupText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private sealed class MinimalResolver : IUiControlResolver
    {
        public UiRuntimeCapabilities Capabilities { get; } = new("minimal-runtime");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            throw new NotSupportedException("MinimalResolver does not resolve controls directly.");
        }
    }

    public sealed class TestableAdapter : IUiControlAdapter
    {
        public bool CanResolve(Type requestedType, UiControlDefinition definition) => true;

        public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
        {
            return new FakeControl("ResolvedByTestableAdapter");
        }

        private sealed class FakeControl : IUiControl
        {
            public FakeControl(string automationId)
            {
                AutomationId = automationId;
                Name = automationId;
            }

            public string AutomationId { get; }

            public string Name { get; }

            public bool IsEnabled => true;
        }
    }

    public abstract class AbstractAdapter : IUiControlAdapter
    {
        public abstract bool CanResolve(Type requestedType, UiControlDefinition definition);

        public abstract object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver);
    }

    public sealed class ParameterizedAdapter : IUiControlAdapter
    {
        public ParameterizedAdapter(string propertyName)
        {
            PropertyName = propertyName;
        }

        public string PropertyName { get; }

        public bool CanResolve(Type requestedType, UiControlDefinition definition) => false;

        public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
        {
            throw new NotSupportedException();
        }
    }
}
