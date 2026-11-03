using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.Recorder.Avalonia;
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
    public async Task GroupedNativeHeadersReadFullParentPathThroughTheConfiguredResolver(bool avaloniaGrid)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var rows = new[] { new HeaderContractRow("First", "42", 500), new HeaderContractRow("Second", "42", 1200) };
        void SetRows(Control control, HeaderContractRow[] items)
        {
            if (control is DataGrid native)
            {
                var view = new global::Avalonia.Collections.DataGridCollectionView(items);
                view.GroupDescriptions.Add(new global::Avalonia.Collections.DataGridPathGroupDescription("Customer"));
                view.GroupDescriptions.Add(new global::Avalonia.Collections.DataGridPathGroupDescription("Order"));
                native.ItemsSource = view;
            }
            else if (control is Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl custom)
            {
                custom.ItemsSource = items;
                custom.ExpandAllGroups();
            }
        }
        var grid = HeadlessRuntime.Dispatch(() =>
        {
            Control control;
            if (avaloniaGrid)
            {
                var native = new DataGrid { Width = 720, Height = 360, AutoGenerateColumns = false };
                foreach (var field in new[] { "Customer", "Order", "Quantity" })
                    native.Columns.Add(new DataGridTextColumn
                    {
                        Header = field,
                        Binding = new global::Avalonia.Data.Binding(field)
                    });
                control = native;
            }
            else
            {
                var custom = new Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl
                { Width = 720, Height = 360, AutoGenerateColumns = false, AutoExpandAllGroups = true, ShowGroupPanel = false };
                custom.Columns.Add(new Eremex.AvaloniaUI.Controls.DataGrid.GridColumn
                { FieldName = "Customer", Header = "Контрагент", SortIndex = 0, SortDirection = System.ComponentModel.ListSortDirection.Ascending });
                custom.Columns.Add(new Eremex.AvaloniaUI.Controls.DataGrid.GridColumn
                { FieldName = "Order", Header = "Номер заказа", SortIndex = 1, SortDirection = System.ComponentModel.ListSortDirection.Ascending });
                custom.Columns.Add(new Eremex.AvaloniaUI.Controls.DataGrid.GridColumn { FieldName = "Quantity", Header = "Количество", BandName = "VolumePlan" });
                custom.Columns.Add(new Eremex.AvaloniaUI.Controls.DataGrid.GridColumn { FieldName = "OtherQuantity", Header = "Количество", BandName = "CostPlan" });
                foreach (var name in new[] { "Volume", "Cost" })
                {
                    var band = new Eremex.AvaloniaUI.Controls.DataGrid.GridBand { BandName = name, Header = name };
                    band.Bands.Add(new Eremex.AvaloniaUI.Controls.DataGrid.GridBand { BandName = name + "Plan", Header = "План" });
                    custom.Bands.Add(band);
                }
                custom.CustomGroupValueDisplayText += (_, e) => e.DisplayText = $"{e.Column.Header}: {e.Value}";
                custom.GroupCount = 2;
                control = custom;
            }
            SetRows(control, rows);
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(control, "HeaderContractGrid");
            session.MainWindow.Content = control;
            session.MainWindow.Show();
            session.MainWindow.UpdateLayout();
            session.MainWindow.UpdateLayout();
            return control;
        });
        var catalog = new GridAutomationCatalog().Add(GridAutomationDefinition.ByAutomationIds(
                "HeaderContractGrid", "HeaderContractGrid", "HeaderContractGrid")
            .WithColumns(GridColumnDefinition.Map("CustomerLogical").FromField("Customer"),
                GridColumnDefinition.Map("Order").FromField("Order"), GridColumnDefinition.Map("Quantity").FromField("Quantity"))
            .IdentifyRowsBy("CustomerLogical", "Order"));
        var resolved = new HeadlessControlResolver(session.MainWindow).WithGridAutomation(catalog)
            .Resolve<IGridControl>(new UiControlDefinition("HeaderContractGrid", UiControlType.Grid, "HeaderContractGrid"));
        var address = GridHeaderSelector.Group("CustomerLogical", "Second").ThenGroup("Order", "42");
        var availableHeader = GridHeaderKeyReader.CaptureFirst(resolved, ["CustomerLogical", "Order"]);
        await Assert.That(GridValueReader.ReadHeaderText(resolved, GridHeaderSelector.Column("Quantity")))
            .IsEqualTo(avaloniaGrid ? "Quantity" : "Количество");
        await Assert.That(GridValueReader.ReadHeaderText(resolved, address)).IsEqualTo(avaloniaGrid ? "42" : "Номер заказа: 42");
        HeadlessRuntime.Dispatch(() =>
        {
            SetRows(grid, [new HeaderContractRow("Second", "42", 1500), new HeaderContractRow("First", "42", 750)]);
            session.MainWindow.UpdateLayout();
        });
        var display = GridValueReader.ReadHeaderText(resolved, address);
        await Assert.That(avaloniaGrid ? display : TextValue.AfterFirstSeparator(display, ":")).IsEqualTo("42");
        await Assert.That(GridValueReader.ReadHeaderText(resolved, availableHeader)).IsEqualTo(avaloniaGrid ? "42" : "Номер заказа: 42");
        var nativeAddress = GridHeaderSelector.Group("Customer", "Second").ThenGroup("Order", "42");
        await Assert.That(GridHeaderSelector.FromAutomationValue(nativeAddress.ToAutomationValue())).IsEqualTo(nativeAddress);
        var unconfigured = new HeadlessControlResolver(session.MainWindow)
            .Resolve<IGridControl>(new UiControlDefinition("HeaderContractGrid", UiControlType.Grid, "HeaderContractGrid"));
        await Assert.That(GridValueReader.ReadCellNumber(unconfigured, GridRowSelector.ByCell("Customer", "Second").AndCell("Order", "42"), "Quantity"))
            .IsEqualTo(1500d);
        if (!avaloniaGrid)
        {
            await Assert.That(GridValueReader.ReadHeaderText(resolved, GridHeaderSelector.Band("Volume").ThenBand("VolumePlan"))).IsEqualTo("План");
            await Assert.That(GridValueReader.ReadHeaderText(resolved, GridHeaderSelector.Band("Cost").ThenBand("CostPlan"))).IsEqualTo("План");
            var row = GridRowSelector.ByCell("CustomerLogical", "Second").AndCell("Order", "42");
            await Assert.That(GridValueReader.ReadCellNumber(resolved, row, "Quantity")).IsEqualTo(1500d);
            await Assert.That(GridValueReader.ReadCellNumber(resolved, row, "OtherQuantity")).IsEqualTo(3000d);
            HeadlessRuntime.Dispatch(() =>
            {
                var headers = grid.GetVisualDescendants()
                    .OfType<Eremex.AvaloniaUI.Controls.DataGrid.Visuals.DataGridGroupRowControl>()
                    .Where(static header => header.IsEffectivelyVisible)
                    .OrderBy(header => header.TranslatePoint(default, grid)?.Y).ToArray();
                var target = headers.Last(static header => header.Level == 1);
                global::Avalonia.Automation.AutomationProperties.SetItemStatus(target, nativeAddress.ToAutomationValue());
                global::Avalonia.Automation.AutomationProperties.SetName(target, "Заказ: 42");
                foreach (var parent in headers.Where(static header => header.Level == 0)) parent.IsVisible = false;
            });
            await Assert.That(GridValueReader.ReadHeaderText(resolved, address)).IsEqualTo("Заказ: 42");
            await Assert.That(GridValueReader.ReadHeaderText(unconfigured, nativeAddress)).IsEqualTo("Заказ: 42");
        }

        // Another database: neither the recorded order nor its parent exists.
        var newRows = new[] { new HeaderContractRow("Other", "RU-other-1", 3100), new HeaderContractRow("Else", "RU-other-2", 4700) };
        HeadlessRuntime.Dispatch(() =>
        {
            // Remove the preceding synthetic publication before reusing native containers with other data.
            foreach (var header in grid.GetVisualDescendants().OfType<Eremex.AvaloniaUI.Controls.DataGrid.Visuals.DataGridGroupRowControl>())
            {
                header.ClearValue(global::Avalonia.Automation.AutomationProperties.ItemStatusProperty);
                header.ClearValue(global::Avalonia.Automation.AutomationProperties.NameProperty);
                header.ClearValue(Visual.IsVisibleProperty);
            }
            SetRows(grid, newRows);
            session.MainWindow.UpdateLayout();
        });
        var newHeader = GridHeaderKeyReader.CaptureFirst(resolved, ["CustomerLogical", "Order"]);
        await Assert.That(newHeader.Path.Last().Value).IsNotEqualTo("42");
        var newText = GridValueReader.ReadHeaderText(resolved, newHeader);
        await Assert.That(avaloniaGrid ? newText : TextValue.AfterFirstSeparator(newText, ":")).IsEqualTo(newHeader.Path.Last().Value);
        var availableRow = GridRowKeyReader.CaptureFirst(resolved);
        var otherAvailableRow = GridRowKeyReader.CaptureFirst(resolved, excludingRows: [availableRow]);
        await Assert.That(otherAvailableRow.Conditions.Select(static part => part.Value).ToArray())
            .IsNotEquivalentTo(availableRow.Conditions.Select(static part => part.Value).ToArray());
        var quantity = GridValueReader.ReadCellNumber(resolved, availableRow, "Quantity");
        var otherQuantity = GridValueReader.ReadCellNumber(resolved, otherAvailableRow, "Quantity");
        HeadlessRuntime.Dispatch(() => { SetRows(grid, newRows.Reverse().ToArray()); session.MainWindow.UpdateLayout(); });
        await Assert.That(GridValueReader.ReadCellNumber(resolved, availableRow, "Quantity")).IsEqualTo(quantity);
        await Assert.That(GridValueReader.ReadCellNumber(resolved, otherAvailableRow, "Quantity")).IsEqualTo(otherQuantity);
        await Assert.That(GridValueReader.ReadHeaderText(resolved, newHeader)).IsEqualTo(newText);

        HeadlessRuntime.Dispatch(() =>
        {
            if (grid is DataGrid native)
                ((global::Avalonia.Collections.DataGridCollectionView)native.CollectionView).Filter =
                    item => item is HeaderContractRow { Quantity: > 4000 };
            else
                ((Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl)grid).FilterString = "[Quantity] > 4000";
            session.MainWindow.UpdateLayout();
            grid.Dispatcher.RunJobs();
        });
        var filteredRow = GridRowKeyReader.CaptureFirst(resolved);
        await Assert.That(filteredRow.Conditions.Single(part => part.ColumnName == "Order").Value).IsEqualTo("RU-other-2");
        Assert.Throws<TimeoutException>(() =>
            GridRowKeyReader.CaptureFirst(resolved, excludingRows: [filteredRow], timeoutMs: 150));

        HeadlessRuntime.Dispatch(() =>
        {
            if (grid is DataGrid native)
                ((global::Avalonia.Collections.DataGridCollectionView)native.CollectionView).Filter = null;
            else
                ((Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl)grid).FilterString = null;
            session.MainWindow.UpdateLayout();
            grid.Dispatcher.RunJobs();
        });
        var restoredRow = GridRowKeyReader.CaptureFirst(resolved, excludingRows: [filteredRow]);
        await Assert.That(restoredRow.Conditions.Single(part => part.ColumnName == "Order").Value).IsEqualTo("RU-other-1");
    }

    public sealed record HeaderContractRow(string Customer, string Order, double Quantity)
    {
        public double OtherQuantity => Quantity * 2;
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task GroupedEditableGridChecksEveryFieldHeaderAndLaterRowWithoutRecordedKeys()
    {
        using var app = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var rows = Enumerable.Range(0, 100).Select(index => new FullGridContractRow(index)).ToArray();
        var fields = new (string Name, string Caption, GridCellValueKind Kind)[]
        {
            ("OrderNumber", "Номер заказа", GridCellValueKind.Text), ("MarketProductName", "Продукт", GridCellValueKind.Text),
            ("IsRequest", "Заявка", GridCellValueKind.Boolean), ("ProductionStatus", "🏭 Произв.", GridCellValueKind.Selection),
            ("ShipmentStatus", "🚚 Отгр.", GridCellValueKind.Selection), ("RequiredVolume", "Объем", GridCellValueKind.Number),
            ("ShippedVolume", "Объем отгруженного", GridCellValueKind.Number), ("UnitPrice", "Цена за единицу", GridCellValueKind.Number),
            ("Cost", "Общая стоимость", GridCellValueKind.Number), ("PriceSaveStatus", "Сохранение цены", GridCellValueKind.Text),
            ("CustomerName", "Контрагент", GridCellValueKind.Text), ("CreatedByLogin", "Автор", GridCellValueKind.Text),
            ("ContactPersonName", "Контактное лицо", GridCellValueKind.Text), ("OrderExternalId", "Внешний номер заказа", GridCellValueKind.Text),
            ("OrderSpecialRequirements", "Комментарий заказа", GridCellValueKind.Text), ("PreferencesForProduction", "Требования к производству", GridCellValueKind.Text),
            ("OrderCreatedAt", "Дата создания заказа", GridCellValueKind.Date), ("ActuallyShippedAt", "Фактическая дата отгрузки", GridCellValueKind.Date),
            ("ReadyDate", "Дата готовности", GridCellValueKind.DateTime), ("DeliveryAddressName", "Адрес доставки", GridCellValueKind.Text),
            ("UpdatedAt", "Дата последнего изменения", GridCellValueKind.Date), ("PlannedToDeliverFromTo", "Дата отгрузки", GridCellValueKind.Text)
        };
        string[] groupedFields = ["CustomerName", "OrderNumber", "OrderSpecialRequirements", "DeliveryAddressName"];
        var catalog = new GridAutomationCatalog().Add(GridAutomationDefinition.ByAutomationIds("FullContractGrid", "FullContractGrid", "FullContractGrid")
            .WithColumns(new[] { GridColumnDefinition.Auto("Id").ReadIdentityFromRow(GridRowAutomationProperty.ItemStatus) }
                .Concat(fields.Select(field => field.Kind == GridCellValueKind.DateTime
                    ? GridColumnDefinition.Auto(field.Name).AtRuntime(field.Caption).AsValue(field.Kind).FormatWith("dd.MM.yyyy HH:mm", "ru-RU")
                    : GridColumnDefinition.Auto(field.Name).AtRuntime(field.Caption).AsValue(field.Kind))).ToArray())
            .IdentifyRowsBy("Id"));
        var grid = HeadlessRuntime.Dispatch(() =>
        {
            var native = new Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl { AutoExpandAllGroups = true, ShowGroupedColumns = false, RowMinHeight = 31 };
            foreach (var field in fields)
            {
                var column = new Eremex.AvaloniaUI.Controls.DataGrid.GridColumn { FieldName = field.Name, Header = field.Caption, Width = new GridLength(160) };
                if (field.Kind == GridCellValueKind.Selection)
                    column.EditorProperties = new Eremex.AvaloniaUI.Controls.Editors.ComboBoxEditorProperties
                    { ItemsSource = new[] { "Запланирован", "Готов", "Частично отгружен", "Отгружен" } };
                else if (field.Name is "ShippedVolume" or "UnitPrice")
                    column.EditorProperties = new Eremex.AvaloniaUI.Controls.Editors.SpinEditorProperties { Minimum = 0 };
                else if (field.Kind == GridCellValueKind.DateTime)
                {
                    column.ReadOnly = true;
                    column.EditorProperties = new Eremex.AvaloniaUI.Controls.Editors.TextEditorProperties
                    { DisplayFormatString = "dd.MM.yyyy HH:mm" };
                }
                native.Columns.Add(column);
            }
            for (var index = 0; index < groupedFields.Length; index++)
            {
                native.Columns[groupedFields[index]].SortIndex = index;
                native.Columns[groupedFields[index]].SortDirection = System.ComponentModel.ListSortDirection.Ascending;
            }
            native.GroupCount = groupedFields.Length;
            native.CustomGroupValueDisplayText += (_, e) => e.DisplayText = $"{e.Column.Header}: {e.Value}";
            native.ItemsSource = rows;
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(native, "FullContractGrid");
            app.MainWindow.Width = 1100;
            app.MainWindow.Height = 650;
            app.MainWindow.Content = native;
            app.MainWindow.Show();
            app.MainWindow.UpdateLayout();
            return native;
        });
        var counts = HeadlessRuntime.Dispatch(() =>
        {
            using var recorder = new RecorderSession(app.MainWindow, new AppAutomationRecorderOptions
            {
                ShowOverlay = false,
                GridAutomation = catalog,
                Validation = new RecorderValidationOptions { ValidateSelectors = true, ValidateRuntimeTargets = false }
            }, validationRootProvider: () => grid, attachWindowHandlers: false);
            var details = (IRecorderCheckpointSessionDetails)recorder;
            RecorderCheckTargetSelection? selection = null;
            recorder.CheckTargetSelected += (_, e) => selection = e.Selection;
            recorder.Start();
            var cellsChecked = 0;
            var groupsChecked = 0;
            var columnsChecked = 0;
            var copyFactory = new RecorderStepFactory(new AppAutomationRecorderOptions { GridAutomation = catalog }, app.MainWindow);
            RecordedActionKind[] readActions = [RecordedActionKind.AssertValue, RecordedActionKind.CaptureCheckpoint, RecordedActionKind.CaptureCopiedValue];
            void RecordRead(RecordedActionKind action)
            {
                if (action == RecordedActionKind.AssertValue) details.CaptureCurrentValueAssertion(selection!);
                else if (action == RecordedActionKind.CaptureCheckpoint) details.CaptureCheckpoint(selection!, "cellValue");
                else
                {
                    var copied = new RecorderCopiedValueOption(Guid.NewGuid(), "copiedCell", RecorderValueKind.Text, "FullContractGrid", string.Empty);
                    var result = copyFactory.TryCreateCopiedValueStep(selection!.ValueSnapshot, copied);
                    if (!result.Success) throw new InvalidOperationException(result.Message);
                    recorder.AddRecordedStepForTesting(result.Step!);
                }
                if (recorder.StepCount != 1 || recorder.PersistableStepCount != 1
                    || !recorder.ExportPreview().Contains("GridValueReader.Read", StringComparison.Ordinal))
                    throw new InvalidOperationException($"{action} did not create one persistable value read: {recorder.LatestStatus}");
            }
            foreach (var header in grid.GetVisualDescendants().OfType<Eremex.AvaloniaUI.Controls.DataGrid.Visuals.DataGridGroupRowControl>()
                .Where(static header => header.IsEffectivelyVisible).ToArray())
            {
                foreach (var action in readActions)
                {
                    recorder.Clear();
                    recorder.BeginCheckTargetSelection();
                    recorder.SelectCheckTargetForTesting(header);
                    if (selection?.ValueSnapshot?.Prototype.GridHeader is null)
                        throw new InvalidOperationException(selection?.ValueDescriptionError ?? "Group header was not captured.");
                    RecordRead(action);
                    if (recorder.ExportPreview().Contains("GridHeaderSelector.Group(", StringComparison.Ordinal))
                        throw new InvalidOperationException("Group capture leaked a recorded path value.");
                }
                groupsChecked++;
            }
            foreach (var rowIndex in new[] { 0, 9, 99 })
                foreach (var field in fields.Where(field => !groupedFields.Contains(field.Name, StringComparer.Ordinal)))
                {
                    var column = grid.Columns[field.Name];
                    grid.FocusedItem = rows[rowIndex];
                    grid.FocusedColumn = column;
                    grid.Focus();
                    app.MainWindow.UpdateLayout();
                    grid.Dispatcher.RunJobs();
                    app.MainWindow.UpdateLayout();
                    global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    if (rowIndex == 0)
                    {
                        var heading = grid.GetVisualDescendants().OfType<Eremex.AvaloniaUI.Controls.DataControl.Visuals.ColumnHeaderControl>()
                            .First(header => header.IsEffectivelyVisible && header.Column is Eremex.AvaloniaUI.Controls.DataGrid.GridColumn headerColumn
                                && headerColumn.FieldName == field.Name);
                        foreach (var action in readActions)
                        {
                            recorder.Clear();
                            recorder.BeginCheckTargetSelection();
                            recorder.SelectCheckTargetForTesting(heading);
                            if (selection?.ValueSnapshot?.Prototype.GridHeader is not { Kind: GridHeaderKind.Column } headerAddress
                                || headerAddress.Path.Single().Name != field.Name)
                                throw new InvalidOperationException($"Column header '{field.Name}' was not captured: {selection?.ValueDescriptionError}");
                            RecordRead(action);
                            if (recorder.ExportPreview().Contains("GridRowSelector.ByCell(", StringComparison.Ordinal))
                                throw new InvalidOperationException($"Column header '{field.Name}' was incorrectly recorded as a data row.");
                        }
                        columnsChecked++;
                    }
                    var cell = grid.GetVisualDescendants().OfType<Eremex.AvaloniaUI.Controls.DataControl.Visuals.CellControl>()
                        .FirstOrDefault(control => control.IsEffectivelyVisible
                            && control.DataContext is Eremex.AvaloniaUI.Controls.DataControl.Visuals.CellData data
                            && ReferenceEquals(data.Row, rows[rowIndex])
                            && data.Column is Eremex.AvaloniaUI.Controls.DataGrid.GridColumn cellColumn && cellColumn.FieldName == field.Name)
                        ?? throw new InvalidOperationException($"Cell '{field.Name}' of row {rowIndex + 1} was not materialized.");
                    var cellOrigin = cell.TranslatePoint(default, grid)
                        ?? throw new InvalidOperationException("The cell has no position in its grid.");
                    var visibleCell = new Rect(cellOrigin, cell.Bounds.Size).Intersect(new Rect(grid.Bounds.Size));
                    if (visibleCell.Width <= 0 || visibleCell.Height <= 0)
                        throw new InvalidOperationException($"Cell '{field.Name}' of row {rowIndex + 1} is outside the viewport.");
                    var point = grid.TranslatePoint(visibleCell.Center, app.MainWindow)!.Value;
                    foreach (var action in readActions)
                    {
                        recorder.Clear();
                        recorder.BeginCheckTargetSelection();
                        recorder.SelectCheckTargetAtForTesting(cell, app.MainWindow, point);
                        if (selection?.ValueSnapshot is null) throw new InvalidOperationException($"{field.Name}: {selection?.ValueDescriptionError}");
                        if (field.Kind == GridCellValueKind.DateTime)
                        {
                            var expected = rows[rowIndex].ReadyDate is { } date
                                ? date.AddTicks(-(date.Ticks % TimeSpan.TicksPerMinute)) : (DateTime?)null;
                            if (selection.ValueSnapshot.Description.ValueKind != RecorderValueKind.DateTime
                                || selection.ValueSnapshot.Prototype.DateValue != expected)
                                throw new InvalidOperationException("The date-time checkpoint lost its date or minute precision.");
                        }
                        if (selection.ValueSnapshot.Prototype.GridTargetColumnName != field.Name)
                            throw new InvalidOperationException($"Check selected a different field instead of '{field.Name}'.");
                        if (selection.ValueSnapshot.Prototype.GridRowConditions?.Single().Value != rows[rowIndex].Id.ToString())
                            throw new InvalidOperationException($"Check selected a different row instead of row {rowIndex + 1}.");
                        RecordRead(action);
                        if (recorder.ExportPreview().Contains("GridRowSelector.ByCell(", StringComparison.Ordinal))
                            throw new InvalidOperationException($"{field.Name}: recorded row key leaked in {action}.");
                        cellsChecked++;
                    }
                }
            return (cellsChecked, groupsChecked, columnsChecked);
        });
        await Assert.That(counts.cellsChecked).IsEqualTo((fields.Length - groupedFields.Length) * 3 * 3);
        await Assert.That(counts.groupsChecked).IsEqualTo(groupedFields.Length);
        await Assert.That(counts.columnsChecked).IsEqualTo(fields.Length - groupedFields.Length);
        var resolved = new HeadlessControlResolver(app.MainWindow).WithGridAutomation(catalog)
            .Resolve<IGridControl>(new UiControlDefinition("FullContractGrid", UiControlType.Grid, "FullContractGrid"));
        var captured = new List<GridRowSelector>();
        for (var index = 0; index < rows.Length; index++) captured.Add(GridRowKeyReader.CaptureFirst(resolved, excludingRows: captured));
        await Assert.That(captured.Select(key => key.Conditions.Single().Value).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(rows.Length);
        await Assert.That(captured.Select(key => key.Conditions.Single().Value).ToArray())
            .IsEquivalentTo(rows.Select(static row => row.Id.ToString()).ToArray());
        var first = captured[0];
        var product = GridValueReader.ReadCellText(resolved, first, "MarketProductName");
        var firstRow = rows.Single(row => row.Id.ToString() == first.Conditions.Single().Value);
        var firstDateTime = firstRow.ReadyDate is { } dateTime
            ? dateTime.AddTicks(-(dateTime.Ticks % TimeSpan.TicksPerMinute)) : (DateTime?)null;
        await Assert.That(GridValueReader.ReadCellDateTime(resolved, first, "ReadyDate")).IsEqualTo(firstDateTime);
        HeadlessRuntime.Dispatch(() => { grid.ItemsSource = rows.Reverse().ToArray(); app.MainWindow.UpdateLayout(); });
        await Assert.That(GridValueReader.ReadCellText(resolved, first, "MarketProductName")).IsEqualTo(product);
    }

    public sealed record FullGridContractRow(int Index)
    {
        [System.ComponentModel.DataAnnotations.Key] public Guid Id { get; } = Guid.NewGuid();
        public string CustomerName => "Customer";
        public string OrderNumber => "CurrentOrder";
        public string OrderSpecialRequirements => "Requirements";
        public string DeliveryAddressName => "Address";
        public string MarketProductName => $"Product {Index}";
        public bool IsRequest => Index % 2 == 0;
        public string ProductionStatus => "Запланирован";
        public string ShipmentStatus => "Частично отгружен";
        public double RequiredVolume => 1000 + Index;
        public double ShippedVolume => 997 + Index;
        public double UnitPrice => 182;
        public double Cost => RequiredVolume * UnitPrice;
        public string PriceSaveStatus => "Saved";
        public string CreatedByLogin => "Author";
        public string ContactPersonName => "Contact";
        public string OrderExternalId => "ExternalOrder";
        public string PreferencesForProduction => "Production requirements";
        public DateTime OrderCreatedAt => new(2026, 10, 29);
        public DateTime? ActuallyShippedAt => Index % 2 == 0 ? new DateTime(2026, 10, 29) : null;
        public DateTime? ReadyDate => Index % 2 == 0 ? new DateTime(2026, 11, 3, 16, 21, 47).AddTicks(12345) : null;
        public DateTime UpdatedAt => new(2026, 10, 29);
        public string PlannedToDeliverFromTo => "29.10.2026 - 30.10.2026";
    }


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
                resultsKind: SingleSelectResultsKind.ListBox) with
            { FallbackToName = false });
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
    [Arguments("ARM-01", "Новый", "Запланирован", false, false, false, false)]
    [Arguments("ARM-02", "Новый", "Готов", false, false, false, false)]
    [Arguments("ARM-03", "Новый", "Запланирован", true, false, false, false)]
    [Arguments("ARM-02", "Готов", "Новый", false, false, false, false)]
    [Arguments("ARM-02", "Новый", "Готов", false, true, false, false)]
    [Arguments("ARM-02", "Новый", "Готов", false, false, true, false)]
    [Arguments("ARM-01", "🚚 ⚪ Не отгружен", "🚚 🟡 Частично отгружен", false, false, false, true)]
    [Arguments("ARM-03", "🚚 ⚪ Не отгружен", "🚚 🟡 Частично отгружен", true, false, false, true)]
    [Arguments("ARM-02", "🚚 🟡 Частично отгружен", "🚚 ⚪ Не отгружен", false, false, false, true)]
    [Arguments("ARM-02", "🚚 ⚪ Не отгружен", "🚚 🟡 Частично отгружен", false, true, false, true)]
    [NotInParallel("DesktopUi")]
    public async Task NativeCompositeGridCombo_CommitsTypedValueAndRowThroughUiParts(
        string rowKey, string initial, string expected, bool afterNavigation, bool cancel, bool expireDuringSelection, bool enumItems)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var rows = Enumerable.Range(0, 3).Select(index => enumItems
            ? (object)new EnumGridRow($"ARM-{index + 1:00}", ShipmentValue(initial))
            : new global::DotnetDebug.Avalonia.ArmDesktopGridRowViewModel(index, "Value", initial, "Red")).ToArray();
        static string KeyOf(object row) => row is EnumGridRow typed ? typed.Key
            : ((global::DotnetDebug.Avalonia.ArmDesktopGridRowViewModel)row).Key;
        static string StateOf(object row) => row is EnumGridRow typed ? ShipmentText(typed.State)
            : ((global::DotnetDebug.Avalonia.ArmDesktopGridRowViewModel)row).State;
        Eremex.AvaloniaUI.Controls.Editors.ComboBoxEditor? selectedEditor = null;
        Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl? nativeGrid = null;
        var keyboardSelections = 0;
        var timedInteractions = 0;
        var popupOpens = 0;
        var valueInputs = 0;
        var declaredValueInput = enumItems && afterNavigation;
        HeadlessRuntime.Dispatch(() =>
        {
            var grid = new Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl
            { ItemsSource = rows, AllowEditing = true, Height = 300 };
            nativeGrid = grid;
            if (declaredValueInput)
                grid.AddHandler(global::Avalonia.Input.InputElement.KeyDownEvent, (_, key) =>
                {
                    if (key.Key == global::Avalonia.Input.Key.Enter) key.Handled = true;
                }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(grid, "ArmComplexDataGridControl");
            grid.Columns.Add(new Eremex.AvaloniaUI.Controls.DataGrid.GridColumn
            { Header = "Key", FieldName = "Key", ReadOnly = true });
            grid.Columns.Add(new Eremex.AvaloniaUI.Controls.DataGrid.GridColumn
            {
                Header = "State",
                FieldName = "State",
                EditorProperties = new Eremex.AvaloniaUI.Controls.Editors.ComboBoxEditorProperties
                {
                    ItemsSource = enumItems
                        ? new Eremex.AvaloniaUI.Controls.Common.EnumItemsSource { EnumType = typeof(ShipmentState) }
                        : new[] { "Новый", "Запланирован", "Готов" }
                }
            });
            grid.ShownEditor += (_, args) =>
            {
                if (args.Editor is not Eremex.AvaloniaUI.Controls.Editors.ComboBoxEditor editor) return;
                selectedEditor = editor;
                editor.PropertyChanged += (_, change) =>
                {
                    if (change.Property == Eremex.AvaloniaUI.Controls.Editors.PopupEditor.IsPopupOpenProperty
                        && editor.IsPopupOpen)
                    {
                        popupOpens++;
                    }
                };
                editor.AddHandler(global::Avalonia.Input.InputElement.KeyDownEvent, (_, key) =>
                {
                    if (key.Key == global::Avalonia.Input.Key.Enter) keyboardSelections++;
                    if (key.Key is global::Avalonia.Input.Key.Down or global::Avalonia.Input.Key.Up)
                    {
                        if (expireDuringSelection && ++timedInteractions == 1) Thread.Sleep(1100);
                    }
                }, global::Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
                var registeredInputs = new HashSet<Control>();
                editor.TemplateApplied += (_, _) => ConfigureParts();
                editor.LayoutUpdated += (_, _) => ConfigureParts();
                ConfigureParts();

                void ConfigureParts()
                {
                    if (editor.RealEditor is Control input)
                    {
                        global::Avalonia.Automation.AutomationProperties.SetAutomationId(input, "GridCombo_Input");
                        if (declaredValueInput && input is global::Avalonia.Controls.TextBox text
                            && registeredInputs.Add(input))
                            text.TextChanged += (_, _) =>
                            {
                                var item = editor.ItemsSource?.Cast<Eremex.AvaloniaUI.Controls.Common.EnumMemberInfo>()
                                    .SingleOrDefault(item => item.Name == text.Text);
                                if (item is null) return;
                                valueInputs++;
                                editor.SelectedItem = item;
                                editor.EditorValue = item.Id;
                            };
                    }
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
            await Assert.That(timedInteractions).IsEqualTo(1);
            await Assert.That(keyboardSelections).IsEqualTo(0);
            await Assert.That(HeadlessRuntime.Dispatch(() => rows.All(row => StateOf(row) == initial))).IsTrue();
            await Assert.That(HeadlessRuntime.Dispatch(() => nativeGrid?.ActiveEditor is null)).IsTrue();
            return;
        }
        page.SelectGridCellComboItem(static candidate => candidate.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", rowKey), "State", expected,
            commitMode: cancel ? GridCellEditCommitMode.Cancel : GridCellEditCommitMode.Commit);
        if (!cancel)
        {
            await Assert.That(HeadlessRuntime.Dispatch(() => selectedEditor?.EditorValue))
                .IsEqualTo(enumItems ? (object)ShipmentValue(expected) : expected);
            if (!enumItems)
                await Assert.That(HeadlessRuntime.Dispatch(() => selectedEditor?.SelectedItem?.ToString())).IsEqualTo(expected);
        }
        if (cancel) await Assert.That(keyboardSelections).IsEqualTo(0);
        if (declaredValueInput)
        {
            await Assert.That(valueInputs > 0).IsTrue();
            await Assert.That(keyboardSelections).IsEqualTo(0);
        }
        await Assert.That(popupOpens).IsEqualTo(1);
        await Assert.That(HeadlessRuntime.Dispatch(() => nativeGrid?.ActiveEditor is null)).IsTrue();
        page.SelectTabItem(static candidate => candidate.ArmDesktopTabItem)
            .SelectTabItem(static candidate => candidate.DataGridTabItem);
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", rowKey), "State")).IsEqualTo(cancel ? initial : expected);
        await Assert.That(HeadlessRuntime.Dispatch(() => StateOf(rows.Single(row => KeyOf(row) == rowKey))))
            .IsEqualTo(cancel ? initial : expected);
        await Assert.That(HeadlessRuntime.Dispatch(() => rows.Where(row => KeyOf(row) != rowKey).All(row => StateOf(row) == initial)))
            .IsTrue();

        var secondRow = KeyOf(rows.First(row => KeyOf(row) != rowKey));
        var nextState = enumItems ? ShipmentText(ShipmentState.Shipped)
            : expected == "Готов" ? "Запланирован" : "Готов";
        page.SelectGridCellComboItem(static candidate => candidate.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", secondRow), "State", nextState);
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", secondRow), "State")).IsEqualTo(nextState);
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", rowKey), "State")).IsEqualTo(cancel ? initial : expected);
        await Assert.That(HeadlessRuntime.Dispatch(() => nativeGrid?.ActiveEditor is null)).IsTrue();
    }

    public enum ShipmentState
    {
        [System.ComponentModel.DataAnnotations.Display(Name = "🚚 ⚪ Не отгружен")] NotShipped,
        [System.ComponentModel.DataAnnotations.Display(Name = "🚚 🟡 Частично отгружен")] PartiallyShipped,
        [System.ComponentModel.DataAnnotations.Display(Name = "🚚 🟢 Отгружен")] Shipped
    }

    public sealed class EnumGridRow(string key, ShipmentState state)
    {
        [System.ComponentModel.DataAnnotations.Key] public string Key { get; } = key;
        public ShipmentState State { get; set; } = state;
    }

    private static string ShipmentText(ShipmentState value) => value switch
    {
        ShipmentState.NotShipped => "🚚 ⚪ Не отгружен",
        ShipmentState.PartiallyShipped => "🚚 🟡 Частично отгружен",
        _ => "🚚 🟢 Отгружен"
    };

    private static ShipmentState ShipmentValue(string text) => Enum.GetValues<ShipmentState>()
        .Single(value => ShipmentText(value) == text);

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
    [Arguments(false)]
    [Arguments(true)]
    [NotInParallel("DesktopUi")]
    public async Task NestedGridOpenRow_IgnoresContainersWithoutRowContext(bool clippedWideRow)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var first = new VisualGridTestRow("ROW-A");
        var second = new VisualGridTestRow("ROW-B");
        var targetPresses = 0;
        var opened = new List<string>();
        var rowWidth = 0d;
        var deliveredX = 0d;
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
                Width = clippedWideRow ? 2419 : 300,
                Height = 30,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                Background = global::Avalonia.Media.Brushes.Transparent,
                DataContext = new VisualGridRowContext(second)
            };
            target.AddHandler(
                global::Avalonia.Input.InputElement.PointerPressedEvent,
                (_, pointer) =>
                {
                    targetPresses++;
                    deliveredX = pointer.GetPosition(session.MainWindow).X;
                },
                global::Avalonia.Interactivity.RoutingStrategies.Bubble);
            target.DoubleTapped += (_, _) => opened.Add(second.Key);
            grid.Children.Add(clippedWideRow
                ? new Border
                {
                    Child = target,
                    Width = 320,
                    Height = 30,
                    ClipToBounds = true,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left
                }
                : target);
            session.MainWindow.Content = grid;
            session.MainWindow.Show();
            session.MainWindow.Dispatcher.RunJobs();
            session.MainWindow.UpdateLayout();
            rowWidth = target.Bounds.Width;
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
        if (clippedWideRow)
        {
            await Assert.That(rowWidth).IsGreaterThan(2000d);
            await Assert.That(deliveredX).IsLessThan(320d);
        }
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
        var overlayPresses = 0;
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
            var overlay = new global::Avalonia.Controls.Border
            {
                Background = global::Avalonia.Media.Brushes.Transparent,
                IsHitTestVisible = true
            };
            overlay.PointerPressed += (_, _) => overlayPresses++;
            root.Children.Add(overlay);
            session.MainWindow.Content = root;
            session.MainWindow.Dispatcher.RunJobs();
            session.MainWindow.UpdateLayout();
        });

        var failure = Assert.Throws<InvalidOperationException>(() =>
            ((IAddressableGridControl)page.DemoDataGrid).OpenRow(
                GridRowSelector.ByCell("Row", "R2"), 5000));
        await Assert.That(failure.Message).Contains("occluded");
        await Assert.That(overlayPresses).IsEqualTo(0);
        await Assert.That(page.GridOpenMarkerLabel.Text).IsNotEqualTo("Opened row: R2");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [NotInParallel("DesktopUi")]
    public async Task ReadGridCell_WaitsForStableRowAfterSourceReload(bool captureRowKey)
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
        var catalog = new GridAutomationCatalog().Add(GridAutomationDefinition.ByAutomationIds("DelayedRowsGrid", "DelayedRowsGrid", "DelayedRowsGrid")
            .WithColumns(GridColumnDefinition.Map("Key").FromField("Key"), GridColumnDefinition.Map("Value").FromField("Value"))
            .IdentifyRowsBy("Key"));
        var gridControl = new HeadlessControlResolver(session.MainWindow).WithGridAutomation(catalog).Resolve<IGridControl>(
            new UiControlDefinition("DelayedRowsGrid", UiControlType.Grid, "DelayedRowsGrid"));

        var value = GridValueReader.ReadCellText(
            gridControl,
            captureRowKey ? GridRowKeyReader.Capture(gridControl, 0) : GridRowSelector.ByCell("Key", "K1"),
            "Value");

        using (Assert.Multiple())
        {
            await Assert.That(value).IsEqualTo("Loaded");
            await Assert.That(host.ReadCount).IsGreaterThan(1);
        }
        var native = new HeadlessControlResolver(session.MainWindow).Resolve<IGridControl>(
            new UiControlDefinition("DelayedRowsGrid", UiControlType.Grid, "DelayedRowsGrid"));
        var before = host.ColumnReadCount;
        var found = ((IGridColumnMetadataControl)native).TryGetColumnIndex("Value", out var columnIndex);
        await Assert.That(found).IsTrue();
        await Assert.That(columnIndex).IsEqualTo(1);
        await Assert.That(host.ColumnReadCount - before).IsEqualTo(1);
    }

    private sealed class DelayedRowsGrid : global::Avalonia.Controls.StackPanel
    {
        private readonly List<DelayedGridRow> _rows = [];
        private bool _loadScheduled;

        public int ReadCount { get; private set; }
        public int ColumnReadCount { get; private set; }

        private static readonly IReadOnlyList<DelayedGridColumn> ColumnDefinitions = [new("Key"), new("Value")];

        public IReadOnlyList<DelayedGridColumn> Columns
        {
            get { ColumnReadCount++; return ColumnDefinitions; }
        }

        public IEnumerable<DelayedGridRow> CollectionView => _rows;

        public IEnumerable<DelayedGridRow> ItemsSource
        {
            get
            {
                ReadCount++;
                if (!_loadScheduled)
                {
                    _loadScheduled = true;
                    Dispatcher.Post(() =>
                    {
                        var row = new DelayedGridRow("K1", "Loaded");
                        _rows.Add(row);
                        Children.Add(new Border { DataContext = row, Height = 30 });
                        UpdateLayout();
                    }, global::Avalonia.Threading.DispatcherPriority.Background);
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
