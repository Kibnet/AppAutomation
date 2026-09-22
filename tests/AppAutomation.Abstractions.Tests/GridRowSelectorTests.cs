using AppAutomation.Abstractions;
using System.Globalization;
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
    public async Task CatalogGrid_ReResolvesStableAddressForActionAndPostcondition()
    {
        var fixture = new GridFixture(
            Row("ITEM-01", "Draft", "10"),
            Row("ITEM-02", "Ready", "20"));
        fixture.Grid.AfterEdit = _ => fixture.Rows.Reverse();
        var page = fixture.CreateCatalogPage();

        page
            .EditGridCellText(
                static candidate => candidate.Orders,
                GridRowSelector.ByCell("Code", "ITEM-02"),
                "State",
                "Done",
                timeoutMs: 250)
            .CopyGridCell(
                static candidate => candidate.Orders,
                GridRowSelector.ByCell("Code", "ITEM-02"),
                "Amount",
                timeoutMs: 250);

        using (Assert.Multiple())
        {
            await Assert.That(page.Orders is IAddressableGridControl).IsTrue();
            await Assert.That(fixture.Rows[0].Cells[0].Value).IsEqualTo("ITEM-02");
            await Assert.That(fixture.Rows[0].Cells[1].Value).IsEqualTo("Done");
            await Assert.That(fixture.Grid.CopiedValue).IsEqualTo("20");
            await Assert.That(fixture.Grid.IndexedOperationCount).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task CatalogGrid_UsesNativeStableAddressAndMergesOnlyMappedColumns()
    {
        var nativeRow = Row("ITEM-42", "internal-state", "raw-amount") with
        {
            ValueSource = new NativeProjection(
                new NativeStatus("Ready"),
                20m)
        };
        var nativeGrid = new NativeAddressableGrid(
            nativeRow,
            ["Order identifier", "Status caption", "Required amount"]);
        var catalog = new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds(
                    pagePropertyName: "Orders",
                    captureAutomationId: "OrdersGridVisual",
                    runtimeAutomationId: "OrdersGrid")
                .WithColumns(
                    GridColumnDefinition.Map("Code")
                        .FromField("OrderId")
                        .AtRuntime("Order identifier"),
                    GridColumnDefinition.Auto("Status")
                        .AtRuntime("Status caption")
                        .DisplayValueFrom("Status.Name"),
                    GridColumnDefinition.Auto("Amount")
                        .AtRuntime("Required amount")
                        .DisplayValueFrom("Amount")
                        .FormatWith("N1", "en-US")
                        .AsValue(GridCellValueKind.Number),
                    GridColumnDefinition.Auto("HiddenNote"))
                .IdentifyRowsBy("Code"));
        var page = new GridPage(new GridResolver(nativeGrid).WithGridAutomation(catalog));
        nativeGrid.TransientNotFoundResolutions = 2;

        page.WaitUntilGridCellEquals(
            static candidate => candidate.Orders,
            GridRowSelector.ByCell("Code", "ITEM-42"),
            "Status",
            "Ready",
            timeoutMs: 1000);

        var value = GridValueReader.ReadCellText(
            page.Orders,
            GridRowSelector.ByCell("Code", "ITEM-42"),
            "Status");
        var statusAddress = nativeGrid.LastAddress;
        var metadata = (IGridColumnMetadataControl)page.Orders;
        var amount = GridValueReader.ReadCellText(
            page.Orders,
            GridRowSelector.ByCell("Code", "ITEM-42"),
            "Amount");
        foreach (var cell in nativeRow.Cells.Cast<MutableCell>())
        {
            cell.SemanticSnapshot = new GridCellValueSnapshot(cell.Value, cell.Value)
            {
                ValueSource = nativeRow.ValueSource
            };
        }

        var fallbackPage = new GridPage(new GridResolver(new ReadOnlyGrid("OrdersGrid", [nativeRow]))
            .WithGridAutomation(catalog));
        var projectedIdentity = GridRowSelector.ByCell("Code", "ITEM-42").AndCell("Status", "Ready");
        var fallbackValue = GridValueReader.ReadCellValue(fallbackPage.Orders, projectedIdentity, "Amount");
        var fallbackStatus = GridValueReader.ReadCellText(fallbackPage.Orders, projectedIdentity, "Status");
        var indexedDisplay = GridValueReader.ReadCellText(fallbackPage.Orders, rowIndex: 0, columnIndex: 2);
        var displayRow = Row("ITEM-42", "Ready", "20.0");
        displayRow = displayRow with
        {
            Cells = displayRow.Cells.Select(static cell => (IGridCellControl)new DisplayCell(cell.Value)).ToArray()
        };

        var displayPage = new GridPage(new GridResolver(new ReadOnlyGrid("OrdersGrid", [displayRow]))
            .WithGridAutomation(catalog));
        var displayValue = GridValueReader.ReadCellValue(displayPage.Orders, projectedIdentity, "Amount");
        ((MutableCell)nativeRow.Cells[1]).SemanticSnapshot = new GridCellValueSnapshot(null)
            { ValueSource = new NativeProjection(null, 20m) };
        var nullProjection = GridValueReader.ReadCellValue(
            fallbackPage.Orders, GridRowSelector.ByCell("Code", "ITEM-42"), "Status");
        var invalidPathCatalog = new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds("Orders", "OrdersGridVisual", "OrdersGrid")
                .WithColumns(
                    GridColumnDefinition.Map("Code")
                        .FromField("OrderId")
                        .AtRuntime("Order identifier"),
                    GridColumnDefinition.Auto("Status")
                        .AtRuntime("Status caption")
                        .DisplayValueFrom("Status.Caption"),
                    GridColumnDefinition.Auto("Amount").AtRuntime("Required amount"))
                .IdentifyRowsBy("Code"));
        var invalidPathPage = new GridPage(
            new GridResolver(nativeGrid).WithGridAutomation(invalidPathCatalog));
        var invalidPathException = await Assert.That(() => GridValueReader.ReadCellText(
                invalidPathPage.Orders,
                GridRowSelector.ByCell("Code", "ITEM-42"),
                "Status"))
            .Throws<InvalidOperationException>();
        var displayOnlyGrid = new NativeAddressableGrid(
            Row("ITEM-54", "Rendered status", "25"),
            ["OrderId", "Status", "Amount"],
            isDisplayOnly: true);
        var displayOnlyCatalog = new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds("Orders", "OrdersGridVisual", "OrdersGrid")
                .WithColumns(
                    GridColumnDefinition.Map("Code").FromField("OrderId"),
                    GridColumnDefinition.Auto("Status").DisplayValueFrom("Status.Name"),
                    GridColumnDefinition.Auto("Amount"))
                .IdentifyRowsBy("Code"));
        var displayOnlyPage = new GridPage(
            new GridResolver(displayOnlyGrid).WithGridAutomation(displayOnlyCatalog));
        var displayOnlyStatus = GridValueReader.ReadCellText(
            displayOnlyPage.Orders,
            GridRowSelector.ByCell("Code", "ITEM-54"),
            "Status");
        var unmarkedDisplayGrid = new NativeAddressableGrid(
            Row("ITEM-55", "Rendered status", "25"),
            ["OrderId", "Status", "Amount"]);
        var unmarkedDisplayPage = new GridPage(
            new GridResolver(unmarkedDisplayGrid).WithGridAutomation(displayOnlyCatalog));
        var unmarkedDisplayException = await Assert.That(() => GridValueReader.ReadCellText(
                unmarkedDisplayPage.Orders,
                GridRowSelector.ByCell("Code", "ITEM-55"),
                "Status"))
            .Throws<InvalidOperationException>();
        var localizedNativeGrid = new NativeAddressableGrid(
            new MutableRow("ITEM-53", "Да", "533,60 kg"),
            ["OrderId", "Approved", "Amount"]);
        var localizedCatalog = new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds("Orders", "OrdersGridVisual", "OrdersGrid")
                .WithColumns(
                    GridColumnDefinition.Map("Code").FromField("OrderId"),
                    GridColumnDefinition.Auto("Approved")
                        .AsValue(GridCellValueKind.Boolean)
                        .WithBooleanDisplayText("Да", "Нет"),
                    GridColumnDefinition.Auto("Amount")
                        .FormatWith("N2", "ru-RU")
                        .AsValue(GridCellValueKind.Number))
                .IdentifyRowsBy("Code"));
        var localizedPage = new GridPage(
            new GridResolver(localizedNativeGrid).WithGridAutomation(localizedCatalog));
        var localizedBoolean = GridValueReader.ReadCellBoolean(
            localizedPage.Orders,
            GridRowSelector.ByCell("Code", "ITEM-53"),
            "Approved");
        var localizedNumber = GridValueReader.ReadCellNumber(
            localizedPage.Orders,
            GridRowSelector.ByCell("Code", "ITEM-53"),
            "Amount");

        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        InvalidOperationException? ambiguousCultureException;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
            var ambiguousCatalog = new GridAutomationCatalog().Add(
                GridAutomationDefinition.ByAutomationIds("Orders", "OrdersGridVisual", "OrdersGrid")
                    .WithColumns(
                        GridColumnDefinition.Map("Code").FromField("OrderId"),
                        GridColumnDefinition.Auto("Amount").AsValue(GridCellValueKind.Number))
                    .IdentifyRowsBy("Code"));
            var ambiguousPage = new GridPage(
                new GridResolver(localizedNativeGrid).WithGridAutomation(ambiguousCatalog));
            ambiguousCultureException = await Assert.That(() => GridValueReader.ReadCellNumber(
                    ambiguousPage.Orders,
                    GridRowSelector.ByCell("Code", "ITEM-53"),
                    "Amount"))
                .Throws<InvalidOperationException>();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }

        using (Assert.Multiple())
        {
            await Assert.That(value).IsEqualTo("Ready");
            await Assert.That(amount).IsEqualTo("20.0");
            await Assert.That(fallbackStatus).IsEqualTo(value);
            await Assert.That(fallbackValue.DisplayText).IsEqualTo(amount);
            await Assert.That(fallbackValue.ValueKind).IsEqualTo(GridCellValueKind.Number);
            await Assert.That(fallbackValue.CultureName).IsEqualTo("en-US");
            await Assert.That(fallbackValue.RawValue).IsEqualTo(20m);
            await Assert.That(indexedDisplay).IsEqualTo(amount);
            await Assert.That(displayValue.DisplayText).IsEqualTo(amount);
            await Assert.That(displayValue.RawValue).IsEqualTo(20m);
            await Assert.That(nullProjection.IsNull).IsTrue();
            await Assert.That(metadata.ColumnNames).IsEquivalentTo(
                ["Order identifier", "Status caption", "Required amount"]);
            await Assert.That(metadata.TryGetColumnIndex("HiddenNote", out _)).IsFalse();
            await Assert.That(statusAddress!.ColumnName).IsEqualTo("Status caption");
            await Assert.That(statusAddress.Row.Conditions[0].ColumnName).IsEqualTo("Order identifier");
            await Assert.That(displayOnlyStatus).IsEqualTo("Rendered status");
            await Assert.That(unmarkedDisplayException!.Message).Contains("Status.Name");
            await Assert.That(nativeGrid.ResolveRowCallCount).IsGreaterThanOrEqualTo(4);
            await Assert.That(localizedBoolean).IsTrue();
            await Assert.That(localizedNumber).IsEqualTo(533.6d);
            await Assert.That(ambiguousCultureException!.Message).Contains("culture-ambiguous");
            await Assert.That(ambiguousCultureException.Message).Contains("53360");
            await Assert.That(invalidPathException!.Message).Contains("Orders");
            await Assert.That(invalidPathException.Message).Contains("Status");
            await Assert.That(invalidPathException.Message).Contains("Caption");
        }
    }

    [Test]
    public async Task CatalogGrid_LocalizesBooleanIdentityAndCopiedValue()
    {
        var grid = new ActionEditableGrid(
            "OrdersGrid",
            [Row("True", "Payload", "10")]);
        var catalog = new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds("Orders", "OrdersGridVisual", "OrdersGrid")
                .WithColumns(
                    GridColumnDefinition.Auto("Approved")
                        .AsValue(GridCellValueKind.Boolean)
                        .WithBooleanDisplayText("Да", "Нет"),
                    GridColumnDefinition.Auto("Value"),
                    GridColumnDefinition.Auto("Amount"))
                .IdentifyRowsBy("Approved"));
        var page = new GridPage(new GridResolver(grid).WithGridAutomation(catalog));
        var addressable = (IAddressableGridControl)page.Orders;
        var selector = GridRowSelector.ByCell("Approved", "Да");

        var resolution = addressable.ResolveRow(selector, 1000);
        var copied = addressable.CopyCell(new GridCellAddress(selector, "Approved"), 1000);

        using (Assert.Multiple())
        {
            await Assert.That(resolution.State).IsEqualTo(GridRowResolutionState.Unique);
            await Assert.That(copied).IsEqualTo("Да");
            await Assert.That(grid.IndexedOperationCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task GridAutomationCatalog_IsImmutableAndRejectsConflictingCaptureLocators()
    {
        var first = CreateCatalog();
        var sameDefinitionInDifferentCatalog = CreateCatalog();
        var conflictingRuntime = GridAutomationDefinition.ByAutomationIds(
            "OtherGrid",
            "OtherGridVisual",
            "OrdersGrid");
        var delimiterCatalogA = new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds("A", "B|AutomationId:C", "Runtime"));
        var delimiterCatalogB = new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds("A|AutomationId:B", "C", "Runtime"));
        var keyboardEditorCatalog = new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds("KeyboardGrid", "KeyboardGrid", "KeyboardGrid")
                .WithColumns(GridColumnDefinition.Auto("Amount")
                    .AsValue(GridCellValueKind.Number)
                    .EditWith(
                        GridCellEditorKind.Number,
                        new GridCellEditorParts(
                            Input: new GridRelativeLocator("AmountInput"),
                            CommitTarget: new GridRelativeLocator(
                                "RowCommitTarget",
                                GridRelativeLocatorScope.Row),
                            UseKeyboardInput: true))));
        var defaultEditorCatalog = new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds("KeyboardGrid", "KeyboardGrid", "KeyboardGrid")
                .WithColumns(GridColumnDefinition.Auto("Amount")
                    .AsValue(GridCellValueKind.Number)
                    .EditWith(
                        GridCellEditorKind.Number,
                        new GridCellEditorParts(
                            Input: new GridRelativeLocator("AmountInput")))));

        using (Assert.Multiple())
        {
            await Assert.That(first.Count).IsEqualTo(1);
            await Assert.That(first.Fingerprint).IsEqualTo(sameDefinitionInDifferentCatalog.Fingerprint);
            await Assert.That(delimiterCatalogA.Fingerprint).IsNotEqualTo(delimiterCatalogB.Fingerprint);
            await Assert.That(keyboardEditorCatalog.Fingerprint).IsNotEqualTo(defaultEditorCatalog.Fingerprint);
            await Assert.That(() => first.Add(
                    GridAutomationDefinition.ByAutomationIds("OtherGrid", "OrdersGridVisual", "OtherGridRuntime")))
                .Throws<ArgumentException>();
            await Assert.That(() => first.Add(conflictingRuntime)).Throws<ArgumentException>();
            await Assert.That(() => GridAutomationDefinition
                    .ByAutomationIds("InvalidGrid", "InvalidGridVisual", "InvalidGrid")
                    .IdentifyRowsBy("Code")
                    .WithColumns(GridColumnDefinition.Auto("State")))
                .Throws<ArgumentException>();
            await Assert.That(() => GridColumnDefinition.Map("Value").DisplayValueFrom("Item..Name"))
                .Throws<ArgumentException>();
            await Assert.That(() => new GridAutomationCatalog().Add(
                    GridAutomationDefinition.ByAutomationIds("InvalidKinds", "InvalidKinds", "InvalidKinds")
                        .WithColumns(GridColumnDefinition.Auto("Value")
                            .AsValue(GridCellValueKind.Number)
                            .EditWith(GridCellEditorKind.CheckBox))))
                .Throws<ArgumentException>();
            await Assert.That(() => new GridAutomationCatalog().Add(
                    GridAutomationDefinition.ByAutomationIds("InvalidEnum", "InvalidEnum", "InvalidEnum")
                        .WithColumns(GridColumnDefinition.Auto("Value")
                            .AsValue((GridCellValueKind)int.MaxValue))))
                .Throws<ArgumentException>();
            await Assert.That(() => new GridAutomationCatalog().Add(
                    GridAutomationDefinition
                        .ByAutomationIds("InvalidIdentitySource", "InvalidIdentitySource", "InvalidIdentitySource")
                        .WithColumns(GridColumnDefinition.Auto("Value")
                            .ReadIdentityFromRow(GridRowAutomationProperty.ItemStatus))))
                .Throws<ArgumentException>();
            await Assert.That(() => GridColumnDefinition.Auto("Approved")
                    .WithBooleanDisplayText("Да", "Да"))
                .Throws<ArgumentException>();
            await Assert.That(() => new GridAutomationCatalog().Add(
                    GridAutomationDefinition
                        .ByAutomationIds("InvalidBoolean", "InvalidBoolean", "InvalidBoolean")
                        .WithColumns(GridColumnDefinition.Auto("Approved")
                            .WithBooleanDisplayText("Да", "Нет"))))
                .Throws<ArgumentException>();
            await Assert.That(() => new GridAutomationCatalog().Add(
                    GridAutomationDefinition
                        .ByAutomationIds("AmbiguousCommit", "AmbiguousCommit", "AmbiguousCommit")
                        .WithColumns(GridColumnDefinition.Auto("Amount")
                            .AsValue(GridCellValueKind.Number)
                            .EditWith(
                                GridCellEditorKind.Number,
                                new GridCellEditorParts(
                                    ConfirmButton: new GridRelativeLocator("ConfirmButton"),
                                    CommitTarget: new GridRelativeLocator(
                                        "RowCommitTarget",
                                        GridRelativeLocatorScope.Row))))))
                .Throws<ArgumentException>();
            await Assert.That(() => GridAutomationDefinition
                    .ByAutomationIds("CrossCollision", "CrossCollision", "CrossCollision")
                    .WithColumns(
                        GridColumnDefinition.Map("Code").FromField("Id"),
                        GridColumnDefinition.Map("Id").FromField("Status")))
                .Throws<ArgumentException>();
            await Assert.That(() => GridAutomationDefinition
                    .ByAutomationIds("RuntimeCrossCollision", "RuntimeCrossCollision", "RuntimeCrossCollision")
                    .WithColumns(
                        GridColumnDefinition.Map("Code").FromField("Id").AtRuntime("State"),
                        GridColumnDefinition.Map("State").FromField("Status")))
                .Throws<ArgumentException>();
        }

        var strictRuntimeGrid = new NativeAddressableGrid(
            Row("ITEM-1", "10", string.Empty),
            ["OrderId", "RequiredVolume"],
            isDisplayOnly: true);
        var strictRuntimeCatalog = new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds("Orders", "OrdersGridVisual", "OrdersGrid")
                .WithColumns(
                    GridColumnDefinition.Map("Code").FromField("OrderId"),
                    GridColumnDefinition.Map("Required")
                        .FromField("RequiredVolume")
                        .AtRuntime("Required amount"))
                .IdentifyRowsBy("Code"));
        var strictRuntimePage = new GridPage(
            new GridResolver(strictRuntimeGrid).WithGridAutomation(strictRuntimeCatalog));

        var strictRuntimeException = await Assert.That(() => _ = strictRuntimePage.Orders)
            .Throws<InvalidOperationException>();
        await Assert.That(strictRuntimeException!.Message).Contains("Required amount");
    }

    [Test]
    public async Task WaitUntilGridContainsRow_ObservesRowAddedDuringWait()
    {
        var fixture = new GridFixture(Row("ORD-1", "Draft", "10"));
        var page = fixture.CreatePage();
        var delayedRow = GridRowSelector.ByCell("OrderId", "ORD-2");
        GridPage? returnedPage = null;
        await ControlledStateChange.RunAsync(
            observed =>
            {
                fixture.Grid.OnReadRows = observed;
                returnedPage = page.WaitUntilGridContainsRow(static candidate => candidate.Orders, delayedRow);
            },
            () =>
            {
                lock (fixture.Rows)
                {
                    fixture.Rows.Add(Row("ORD-2", "Ready", "20"));
                }
            });

        await Assert.That(ReferenceEquals(returnedPage, page)).IsTrue();
        await Assert.That(fixture.Rows.Count).IsEqualTo(2);
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
    [Arguments("en-US")]
    [Arguments("ru-RU")]
    public async Task GridValueReader_IndexFallbackReadsConfiguredCell(string cultureName)
    {
        var fixture = new GridFixture(Row("ITEM-1", "Ready", "20"));
        var page = fixture.CreatePage();
        var row = GridRowSelector.ByCell("OrderId", "ITEM-1");
        var cell = (MutableCell)fixture.Rows[0].Cells[2];
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            await Assert.That(GridValueReader.ReadCellText(page.Orders, rowIndex: 0, columnIndex: 2))
                .IsEqualTo("20");

            cell.SemanticSnapshot = new GridCellValueSnapshot(533.6m.ToString("N2", culture))
                { CultureName = cultureName };
            await Assert.That(GridValueReader.ReadCellNumber(page.Orders, row, "Amount")).IsEqualTo(533.6d);

            cell.SemanticSnapshot = new GridCellValueSnapshot("03/09/2026") { CultureName = cultureName };
            var expectedDate = cultureName == "ru-RU" ? new DateTime(2026, 9, 3) : new DateTime(2026, 3, 9);
            await Assert.That(GridValueReader.ReadCellDate(page.Orders, row, "Amount")).IsEqualTo(expectedDate);

            var expectedTime = new TimeSpan(0, 12, 34, 56, 700);
            cell.SemanticSnapshot = new GridCellValueSnapshot(expectedTime.ToString("g", culture))
                { CultureName = cultureName };
            await Assert.That(GridValueReader.ReadCellTime(page.Orders, row, "Amount")).IsEqualTo(expectedTime);

            cell.SemanticSnapshot = new GridCellValueSnapshot(null) { CultureName = cultureName };
            await Assert.That(GridValueReader.ReadCellDate(page.Orders, row, "Amount")).IsNull();
            await Assert.That(GridValueReader.ReadCellTime(page.Orders, row, "Amount")).IsNull();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
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

        public GridPage CreateCatalogPage()
        {
            var resolver = new GridResolver(Grid).WithGridAutomation(CreateCatalog());
            return new GridPage(resolver);
        }
    }

    private static GridAutomationCatalog CreateCatalog()
    {
        return new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds(
                    pagePropertyName: "Orders",
                    captureAutomationId: "OrdersGridVisual",
                    runtimeAutomationId: "OrdersGrid")
                .WithColumns(
                    GridColumnDefinition.Map("Code").FromField("OrderId"),
                    GridColumnDefinition.Map("State").FromField("Status").EditWith(GridCellEditorKind.Text),
                    GridColumnDefinition.Auto("Amount").AsValue(GridCellValueKind.Number))
                .IdentifyRowsBy("Code"));
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

        public Action? OnReadRows { get; set; }

        public IReadOnlyList<IGridRowControl> Rows
        {
            get
            {
                IGridRowControl[] snapshot;
                lock (rows)
                {
                    snapshot = rows.ToArray();
                }

                OnReadRows?.Invoke();
                return snapshot;
            }
        }

        public IGridRowControl? GetRowByIndex(int index)
        {
            return index >= 0 && index < rows.Count ? rows[index] : null;
        }
    }

    private sealed class ActionEditableGrid(string automationId, IReadOnlyList<MutableRow> rows)
        : ReadOnlyGrid(automationId, rows), IGridUserActionControl, IEditableGridControl, IIndexedAddressableGridControl
    {
        public Action<GridCellEditRequest>? AfterEdit { get; set; }

        public GridCellEditRequest? LastRequest { get; private set; }

        public string? OpenedOrderId { get; private set; }

        public string? CopiedValue { get; private set; }

        public int IndexedOperationCount { get; private set; }

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

        public GridRowResolution ResolveRow(GridIndexedRowSelector row, int timeoutMs)
        {
            var matches = FindMatches(row);
            return matches.Count switch
            {
                0 => GridRowResolution.NotFound("no match"),
                1 => GridRowResolution.Unique("unique match"),
                _ => GridRowResolution.Ambiguous(matches.Count, "ambiguous match")
            };
        }

        public GridCellValueSnapshot ReadCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            int timeoutMs)
        {
            IndexedOperationCount++;
            var rowIndex = ResolveUniqueIndex(row);
            var value = GetRowByIndex(rowIndex)?.Cells[column.ColumnIndex].Value;
            return new GridCellValueSnapshot(value, value, column.ValueKind);
        }

        public string CopyCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            int timeoutMs)
        {
            IndexedOperationCount++;
            var rowIndex = ResolveUniqueIndex(row);
            CopiedValue = GetRowByIndex(rowIndex)?.Cells[column.ColumnIndex].Value
                ?? throw new InvalidOperationException("Cell was not found.");
            return CopiedValue;
        }

        public void EditCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            IndexedOperationCount++;
            EditCell(new GridCellEditRequest(
                ResolveUniqueIndex(row),
                column.ColumnIndex,
                request.Value,
                request.EditorKind,
                request.CommitMode,
                request.SearchText)
            {
                TimeoutMs = timeoutMs,
                EditorParts = request.EditorParts
            });
        }

        public void OpenRow(GridIndexedRowSelector row, int timeoutMs)
        {
            IndexedOperationCount++;
            OpenRow(ResolveUniqueIndex(row));
        }

        private int ResolveUniqueIndex(GridIndexedRowSelector row)
        {
            var matches = FindMatches(row);
            return matches.Count == 1
                ? matches[0]
                : throw new InvalidOperationException($"Expected one row, found {matches.Count}.");
        }

        private List<int> FindMatches(GridIndexedRowSelector row)
        {
            var matches = new List<int>();
            for (var rowIndex = 0; rowIndex < Rows.Count; rowIndex++)
            {
                var cells = Rows[rowIndex].Cells;
                if (row.Conditions.All(condition =>
                        condition.ColumnIndex < cells.Count
                        && string.Equals(
                            ReadExpectedCellText(cells[condition.ColumnIndex], condition.Column),
                            condition.ExpectedText,
                            StringComparison.Ordinal)))
                {
                    matches.Add(rowIndex);
                }
            }

            return matches;
        }

        private static string ReadExpectedCellText(
            IGridCellControl cell,
            GridRuntimeColumn column)
        {
            if (column.ValueKind == GridCellValueKind.Boolean
                && bool.TryParse(cell.Value, out var boolean)
                && column.BooleanTrueDisplayText is not null
                && column.BooleanFalseDisplayText is not null)
            {
                return boolean
                    ? column.BooleanTrueDisplayText
                    : column.BooleanFalseDisplayText;
            }

            return cell.Value;
        }
    }

    private sealed class NativeAddressableGrid(
        MutableRow row,
        IReadOnlyList<string> columnNames,
        bool isDisplayOnly = false)
        : ReadOnlyGrid("OrdersGrid", [row]), IAddressableGridControl, IGridColumnMetadataControl
    {
        public IReadOnlyList<string> ColumnNames { get; } = columnNames;

        public GridCellAddress? LastAddress { get; private set; }

        public int TransientNotFoundResolutions { get; set; }

        public int ResolveRowCallCount { get; private set; }

        public GridRowResolution ResolveRow(GridRowSelector selector, int timeoutMs)
        {
            ResolveRowCallCount++;
            if (TransientNotFoundResolutions > 0)
            {
                TransientNotFoundResolutions--;
                return GridRowResolution.NotFound("native row is loading");
            }

            return selector.Conditions.Count == 1
                && string.Equals(selector.Conditions[0].ColumnName, ColumnNames[0], StringComparison.Ordinal)
                && string.Equals(selector.Conditions[0].Value, row.Cells[0].Value, StringComparison.Ordinal)
                    ? GridRowResolution.Unique("native stable address")
                    : GridRowResolution.NotFound("native stable address");
        }

        public GridCellValueSnapshot ReadCell(GridCellAddress address, int timeoutMs)
        {
            LastAddress = address;
            var resolution = ResolveRow(address.Row, timeoutMs);
            if (resolution.State != GridRowResolutionState.Unique)
            {
                throw new InvalidOperationException("Grid row selector matched 0 rows.");
            }

            if (!TryGetColumnIndex(address.ColumnName, out var columnIndex))
            {
                throw new InvalidOperationException("Column was not found.");
            }

            var value = row.Cells[columnIndex].Value;
            return new GridCellValueSnapshot(value, value, GridCellValueKind.Text)
            {
                ValueSource = row.ValueSource,
                IsDisplayOnly = isDisplayOnly
            };
        }

        public bool TryGetColumnIndex(string columnName, out int columnIndex)
        {
            for (var index = 0; index < ColumnNames.Count; index++)
            {
                if (string.Equals(ColumnNames[index], columnName, StringComparison.Ordinal))
                {
                    columnIndex = index;
                    return true;
                }
            }

            columnIndex = -1;
            return false;
        }

        public string CopyCell(GridCellAddress address, int timeoutMs)
        {
            return ReadCell(address, timeoutMs).DisplayText ?? string.Empty;
        }

        public void EditCell(GridCellAddress address, GridCellValueEditRequest request, int timeoutMs)
        {
            throw new NotSupportedException();
        }

        public void OpenRow(GridRowSelector selector, int timeoutMs)
        {
            throw new NotSupportedException();
        }
    }

    private sealed record NativeStatus(string Name);

    private sealed record NativeProjection(NativeStatus? Status, decimal Amount);

    private sealed record MutableRow : IGridRowControl
    {
        public MutableRow(params string[] values)
        {
            Cells = values
            .Select(static value => (IGridCellControl)new MutableCell(value))
            .ToArray();
        }

        public IReadOnlyList<IGridCellControl> Cells { get; init; }

        public object? ValueSource { get; init; }
    }

    private sealed class MutableCell(string value) : IGridCellControl, IGridCellValueControl
    {
        public string Value { get; set; } = value;

        public GridCellValueSnapshot? SemanticSnapshot { get; set; }

        public GridCellValueSnapshot ValueSnapshot => SemanticSnapshot ?? new GridCellValueSnapshot(Value, Value);
    }

    private sealed record DisplayCell(string Value) : IGridCellControl;
}
