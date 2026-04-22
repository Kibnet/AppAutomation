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

    private sealed record FakeGridRowControl(params IGridCellControl[] Cells) : IGridRowControl
    {
        IReadOnlyList<IGridCellControl> IGridRowControl.Cells => Cells;
    }

    private sealed record FakeGridCellControl(string Value) : IGridCellControl;

    private sealed record FakeComboBoxItem(string Text, string Name) : IComboBoxItem;

    private sealed record FakeListBoxItem(string Text, string Name) : IListBoxItem;
}
