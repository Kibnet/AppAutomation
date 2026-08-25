using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class GridRowSelectorTests
{
    [Test]
    public async Task Selector_BuildsImmutableCompositeKey()
    {
        var order = GridRowSelector.ByCell("OrderId", "ORD-42");
        var line = order.AndCell("Item", "Pump");

        using (Assert.Multiple())
        {
            await Assert.That(order.Conditions.Count).IsEqualTo(1);
            await Assert.That(line.Conditions.Count).IsEqualTo(2);
            await Assert.That(line.Conditions[0].ColumnName).IsEqualTo("OrderId");
            await Assert.That(line.Conditions[1].Value).IsEqualTo("Pump");
        }
    }

    [Test]
    public async Task Selector_RejectsEmptyAndDuplicateColumnNames()
    {
        await Assert.That(() => GridRowSelector.ByCell(" ", "ORD-42"))
            .Throws<ArgumentException>();
        await Assert.That(() => GridRowSelector.ByCell("OrderId", "ORD-42").AndCell("OrderId", "ORD-43"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task NamedOperations_ResolveCurrentRowAfterInsertAndReorder()
    {
        var fixture = new GridFixture(
            Row("ORD-1", "Draft", "10"),
            Row("ORD-2", "Ready", "20"));
        var page = fixture.CreatePage();
        var order = GridRowSelector.ByCell("OrderId", "ORD-2");

        fixture.Rows.Insert(0, Row("ORD-NEW", "New", "5"));
        fixture.Rows.Reverse();

        page
            .WaitUntilGridContainsRow(static candidate => candidate.Orders, order)
            .WaitUntilGridCellEquals(static candidate => candidate.Orders, order, "Status", "Ready")
            .OpenGridRow(static candidate => candidate.Orders, order)
            .CopyGridCell(static candidate => candidate.Orders, order, "Amount")
            .EditGridCellText(static candidate => candidate.Orders, order, "Status", "Done");

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Grid.OpenedOrderId).IsEqualTo("ORD-2");
            await Assert.That(fixture.Grid.CopiedValue).IsEqualTo("20");
            await Assert.That(fixture.Rows.Single(row => row.Cells[0].Value == "ORD-2").Cells[1].Value).IsEqualTo("Done");
        }
    }

    [Test]
    public async Task NamedEdit_FollowsRowWhenEditChangesItsPosition()
    {
        var fixture = new GridFixture(
            Row("ORD-1", "Draft", "10"),
            Row("ORD-2", "Ready", "20"));
        fixture.Grid.AfterEdit = _ => fixture.Rows.Reverse();

        fixture.CreatePage().EditGridCellText(
            static page => page.Orders,
            GridRowSelector.ByCell("OrderId", "ORD-2"),
            "Status",
            "Done",
            timeoutMs: 250);

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Rows[0].Cells[0].Value).IsEqualTo("ORD-2");
            await Assert.That(fixture.Rows[0].Cells[1].Value).IsEqualTo("Done");
            await Assert.That(fixture.Grid.LastRequest!.TimeoutMs).IsEqualTo(250);
        }
    }

    [Test]
    public async Task WaitUntilGridContainsRow_ObservesRowAddedDuringWait()
    {
        var fixture = new GridFixture(Row("ORD-1", "Draft", "10"));
        var page = fixture.CreatePage();
        var delayedRow = GridRowSelector.ByCell("OrderId", "ORD-2");
        var addRow = Task.Run(async () =>
        {
            await Task.Delay(150);
            fixture.Rows.Add(Row("ORD-2", "Ready", "20"));
        });

        var returnedPage = page.WaitUntilGridContainsRow(
            static candidate => candidate.Orders,
            delayedRow,
            timeoutMs: 1000);
        await addRow;

        await Assert.That(ReferenceEquals(returnedPage, page)).IsTrue();
    }

    [Test]
    public async Task NamedOperation_RejectsAmbiguousRowInsteadOfUsingFirstMatch()
    {
        var fixture = new GridFixture(
            Row("ORD-1", "Draft", "10"),
            Row("ORD-1", "Ready", "20"));
        var page = fixture.CreatePage();

        var exception = await Assert.That(() => page.OpenGridRow(
                static candidate => candidate.Orders,
                GridRowSelector.ByCell("OrderId", "ORD-1")))
            .Throws<UiOperationException>();

        using (Assert.Multiple())
        {
            await Assert.That(exception!.Message).Contains("matched 2 rows");
            await Assert.That(fixture.Grid.OpenedOrderId).IsNull();
        }
    }

    [Test]
    public async Task GridColumnAdapter_PreservesActualRuntimeCapabilities()
    {
        var readOnly = new ReadOnlyGrid("ReadOnly", []);
        var combined = new ActionEditableGrid("Combined", []);

        var resolvedReadOnly = new GridResolver(readOnly)
            .WithGridColumns("Orders", ["OrderId"])
            .Resolve<IGridControl>(GridPage.Definition);
        var resolvedCombined = new GridResolver(combined)
            .WithGridColumns("Orders", ["OrderId"])
            .Resolve<IGridControl>(GridPage.Definition);

        using (Assert.Multiple())
        {
            await Assert.That(resolvedReadOnly is IGridColumnMetadataControl).IsTrue();
            await Assert.That(resolvedReadOnly is IGridUserActionControl).IsFalse();
            await Assert.That(resolvedReadOnly is IEditableGridControl).IsFalse();
            await Assert.That(resolvedCombined is IGridColumnMetadataControl).IsTrue();
            await Assert.That(resolvedCombined is IGridUserActionControl).IsTrue();
            await Assert.That(resolvedCombined is IEditableGridControl).IsTrue();
        }
    }

    [Test]
    public async Task GridValueReader_StableSelectorFollowsRowAfterReorder()
    {
        var fixture = new GridFixture(
            Row("ITEM-1", "Draft", "10"),
            Row("ITEM-2", "Ready", "20"));
        var page = fixture.CreatePage();
        var selector = GridRowSelector.ByCell("OrderId", "ITEM-2");

        fixture.Rows.Insert(0, Row("ITEM-NEW", "New", "5"));
        fixture.Rows.Reverse();

        var value = GridValueReader.ReadCellText(page.Orders, selector, "Status");

        await Assert.That(value).IsEqualTo("Ready");
    }

    [Test]
    public async Task GridValueReader_IndexFallbackReadsConfiguredCell()
    {
        var fixture = new GridFixture(Row("ITEM-1", "Ready", "20"));

        var value = GridValueReader.ReadCellText(fixture.CreatePage().Orders, rowIndex: 0, columnIndex: 2);

        await Assert.That(value).IsEqualTo("20");
    }

    private static MutableRow Row(string orderId, string status, string amount)
    {
        return new MutableRow(orderId, status, amount);
    }

    private sealed class GridFixture
    {
        public GridFixture(params MutableRow[] rows)
        {
            Rows = [.. rows];
            Grid = new ActionEditableGrid("OrdersGrid", Rows);
        }

        public List<MutableRow> Rows { get; }

        public ActionEditableGrid Grid { get; }

        public GridPage CreatePage()
        {
            var resolver = new GridResolver(Grid).WithGridColumns(
                "Orders",
                ["OrderId", "Status", "Amount"]);
            return new GridPage(resolver);
        }
    }

    private sealed class GridPage : UiPage
    {
        public static UiControlDefinition Definition { get; } = new(
            "Orders",
            UiControlType.Grid,
            "OrdersGrid");

        public GridPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IGridControl Orders => Resolve<IGridControl>(Definition);
    }

    private sealed class GridResolver(IGridControl grid) : IUiControlResolver
    {
        public UiRuntimeCapabilities Capabilities { get; } = new("grid-selector-test");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            return grid as TControl
                ?? throw new InvalidOperationException($"Unexpected control type: {typeof(TControl).Name}.");
        }
    }

    private class ReadOnlyGrid(string automationId, IReadOnlyList<MutableRow> rows) : IGridControl
    {
        public string AutomationId { get; } = automationId;

        public string Name => AutomationId;

        public bool IsEnabled => true;

        public IReadOnlyList<IGridRowControl> Rows => rows;

        public IGridRowControl? GetRowByIndex(int index)
        {
            return index >= 0 && index < rows.Count ? rows[index] : null;
        }
    }

    private sealed class ActionEditableGrid(string automationId, IReadOnlyList<MutableRow> rows)
        : ReadOnlyGrid(automationId, rows), IGridUserActionControl, IEditableGridControl
    {
        public Action<GridCellEditRequest>? AfterEdit { get; set; }

        public GridCellEditRequest? LastRequest { get; private set; }

        public string? OpenedOrderId { get; private set; }

        public string? CopiedValue { get; private set; }

        public void OpenRow(int rowIndex)
        {
            OpenedOrderId = GetRowByIndex(rowIndex)?.Cells[0].Value;
        }

        public void SortByColumn(string columnName)
        {
        }

        public void ScrollToEnd()
        {
        }

        public string CopyCell(int rowIndex, int columnIndex)
        {
            CopiedValue = GetRowByIndex(rowIndex)?.Cells[columnIndex].Value
                ?? throw new InvalidOperationException("Cell was not found.");
            return CopiedValue;
        }

        public void Export()
        {
        }

        public void EditCell(GridCellEditRequest request)
        {
            LastRequest = request;
            if (request.CommitMode == GridCellEditCommitMode.Commit
                && GetRowByIndex(request.RowIndex)?.Cells[request.ColumnIndex] is MutableCell cell)
            {
                cell.Value = request.Value;
            }

            AfterEdit?.Invoke(request);
        }
    }

    private sealed class MutableRow(params string[] values) : IGridRowControl
    {
        public IReadOnlyList<IGridCellControl> Cells { get; } = values
            .Select(static value => (IGridCellControl)new MutableCell(value))
            .ToArray();
    }

    private sealed class MutableCell(string value) : IGridCellControl
    {
        public string Value { get; set; } = value;
    }
}
