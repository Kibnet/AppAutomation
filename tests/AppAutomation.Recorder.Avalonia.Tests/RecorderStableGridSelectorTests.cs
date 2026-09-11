using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using System.ComponentModel.DataAnnotations;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

[NotInParallel("RecorderGridCapture")]
public sealed class RecorderStableGridSelectorTests
{
    [Test]
    public async Task Generator_RendersCompositeSelectorAndNamedTargetColumn()
    {
        var step = new RecordedStep(
            RecordedActionKind.WaitUntilGridCellEquals,
            GridDescriptor(),
            StringValue: "Ready",
            RowIndex: 7,
            ColumnIndex: 2)
        {
            GridRowConditions =
            [
                new RecordedGridRowCondition("OrderId", "ORD-42"),
                new RecordedGridRowCondition("Item", "Pump")
            ],
            GridTargetColumnName = "Status"
        };

        var preview = CreateGenerator().GeneratePreview(step);

        await Assert.That(preview).Contains(
            "Page.WaitUntilGridCellEquals(static page => page.OrdersGrid, "
            + "GridRowSelector.ByCell(\"OrderId\", \"ORD-42\").AndCell(\"Item\", \"Pump\"), "
            + "\"Status\", \"Ready\");");
    }

    [Test]
    public async Task Generator_KeepsLegacyIndexesWithoutNamedPayload()
    {
        var step = new RecordedStep(
            RecordedActionKind.CopyGridCell,
            GridDescriptor(),
            RowIndex: 7,
            ColumnIndex: 2);

        var preview = CreateGenerator().GeneratePreview(step);

        await Assert.That(preview).Contains(
            "Page.CopyGridCell(static page => page.OrdersGrid, 7, 2);");
    }

    [Test]
    public async Task Recorder_UsesOnlyConfiguredIdentityColumns()
    {
        var fixture = new GridCaptureFixture(
            [
                new OrderRow("ORD-41", "North", "Draft"),
                new OrderRow("ORD-42", "North", "Ready")
            ],
            identityColumns: ["OrderId", "Customer"]);

        var result = fixture.CaptureCell(rowIndex: 1, columnIndex: 2);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step!.GridTargetColumnName).IsEqualTo("Status");
            await Assert.That(result.Step.GridRowConditions).IsEquivalentTo(
            [
                new RecordedGridRowCondition("OrderId", "ORD-42"),
                new RecordedGridRowCondition("Customer", "North")
            ]);
        }
    }

    [Test]
    public async Task Recorder_UsesDisplayedIdentityValueInsteadOfModelToString()
    {
        var fixture = new GridCaptureFixture(
            [new OrderRow("ORD-42", "North", "Ready")],
            identityColumns: ["OrderId"],
            displayValue: static (row, columnIndex) => columnIndex == 0 ? "Order #42" : ValueAt(row, columnIndex));

        var result = fixture.CaptureCell(rowIndex: 0, columnIndex: 2);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step!.GridRowConditions).IsEquivalentTo(
            [
                new RecordedGridRowCondition("OrderId", "Order #42")
            ]);
        }
    }

    [Test]
    public async Task Recorder_FollowsDisplayedRowDataAfterSorting()
    {
        var fixture = new GridCaptureFixture(
            [
                new OrderRow("ORD-42", "North", "Ready"),
                new OrderRow("ORD-41", "South", "Draft")
            ],
            identityColumns: ["OrderId"],
            displayedAutomationRowIndex: static rowIndex => 1 - rowIndex);

        var result = fixture.CaptureCell(rowIndex: 0, columnIndex: 2);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step!.GridRowConditions).IsEquivalentTo(
            [
                new RecordedGridRowCondition("OrderId", "ORD-42")
            ]);
        }
    }

    [Test]
    public async Task Recorder_RecordsRowAssertionWithStableIdentity()
    {
        var fixture = new GridCaptureFixture(
            [
                new OrderRow("ORD-41", "North", "Draft"),
                new OrderRow("ORD-42", "South", "Ready")
            ],
            identityColumns: ["OrderId"]);

        var result = fixture.CaptureRow(rowIndex: 1);
        var preview = CreateGenerator().GeneratePreview(result.Step!);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilGridContainsRow);
            await Assert.That(result.Step.GridRowConditions).IsEquivalentTo(
            [
                new RecordedGridRowCondition("OrderId", "ORD-42")
            ]);
            await Assert.That(preview).Contains(
                "Page.WaitUntilGridContainsRow(static page => page.OrdersGrid, "
                + "GridRowSelector.ByCell(\"OrderId\", \"ORD-42\"));");
        }
    }

    [Test]
    public async Task Recorder_ConfiguredNamedGridStepDoesNotProduceRuntimeAdapterWarning()
    {
        var fixture = new GridCaptureFixture(
            [new OrderRow("ORD-42", "North", "Ready")],
            identityColumns: ["OrderId"]);

        var result = fixture.CaptureCell(rowIndex: 0, columnIndex: 2);
        var validated = new RecorderCommandRuntimeValidator(fixture.Options)
            .Validate(result.Step!);
        var findings = validated.RuntimeValidationFindings ?? [];

        using (Assert.Multiple())
        {
            await Assert.That(findings.Any(static finding =>
                finding.Code.EndsWith("grid-column-metadata-adapter-required", StringComparison.Ordinal))).IsFalse();
            await Assert.That(findings.Any(static finding => finding.ShouldSurface)).IsFalse();
        }
    }

    [Test]
    public async Task Recorder_RejectsNonUniqueConfiguredIdentity()
    {
        var fixture = new GridCaptureFixture(
            [
                new OrderRow("ORD-42", "North", "Draft"),
                new OrderRow("ORD-42", "South", "Ready")
            ],
            identityColumns: ["OrderId"]);

        var result = fixture.CaptureCell(rowIndex: 1, columnIndex: 2);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Message).Contains("matches 2 rows");
        }
    }

    [Test]
    public async Task Recorder_RejectsIdentityMadeEmptyByTargetColumn()
    {
        var fixture = new GridCaptureFixture(
            [new OrderRow("ORD-42", "North", "Ready")],
            identityColumns: ["Status"]);

        var result = fixture.CaptureCell(rowIndex: 0, columnIndex: 2);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Message).Contains("identity is empty after excluding target column");
        }
    }

    [Test]
    public async Task Recorder_KeepsIndexesWhenIdentityIsNotConfigured()
    {
        var fixture = new GridCaptureFixture(
            [
                new OrderRow("ORD-39", "West", "Draft"),
                new OrderRow("ORD-40", "South", "Ready"),
                new OrderRow("ORD-41", "East", "Draft"),
                new OrderRow("ORD-42", "North", "Ready")
            ],
            identityColumns: []);

        var assertion = fixture.CaptureCell(rowIndex: 3, columnIndex: 2);
        var checkpoint = fixture.CaptureCheckpointCell(rowIndex: 3, columnIndex: 2);
        var validatedCheckpoint = new RecorderCommandRuntimeValidator(new AppAutomationRecorderOptions())
            .Validate(checkpoint.Step!);
        var preview = CreateGenerator().GeneratePreview([validatedCheckpoint]);

        using (Assert.Multiple())
        {
            await Assert.That(assertion.Success).IsTrue();
            await Assert.That(assertion.Step!.RowIndex).IsEqualTo(3);
            await Assert.That(assertion.Step.ColumnIndex).IsEqualTo(2);
            await Assert.That(checkpoint.Success).IsTrue();
            await Assert.That(checkpoint.Step!.RowIndex).IsEqualTo(3);
            await Assert.That(checkpoint.Step.ColumnIndex).IsEqualTo(2);
            await Assert.That(checkpoint.Step.GridRowConditions).IsNull();
            await Assert.That(checkpoint.Step.GridTargetColumnName).IsNull();
            await Assert.That(validatedCheckpoint.CanPersist).IsTrue();
            await Assert.That(preview).Contains(
                "var cellValue = GridValueReader.ReadCellText(Page.OrdersGrid, 3, 2);");
        }
    }

    [Test]
    public async Task Recorder_UsesGridSearchPickerCellForCheckpointCapture()
    {
        var row = new ItemRow("ITEM-42", "Search result");
        var root = new StackPanel();
        var sourceGrid = new GridHost { ItemsSource = new[] { row } };
        var sourceRow = new Border();
        var picker = new Border();
        var input = new TextBox { Text = row.Product };
        var targetGrid = new StackPanel();
        var displayedCell = new Button
        {
            Content = row.Product,
            DataContext = row
        };

        AutomationProperties.SetAutomationId(sourceGrid, "ItemsGridVisual");
        AutomationProperties.SetAutomationId(sourceRow, "ItemsGrid_Row0");
        AutomationProperties.SetAutomationId(picker, "ItemPicker");
        AutomationProperties.SetAutomationId(input, "ItemPicker_Input");
        AutomationProperties.SetAutomationId(targetGrid, "ItemsGrid");
        AutomationProperties.SetAutomationId(displayedCell, "ItemsGrid_Row0_ProductCell");

        picker.Child = input;
        sourceRow.Child = picker;
        sourceGrid.Children.Add(sourceRow);
        targetGrid.Children.Add(displayedCell);
        root.Children.Add(sourceGrid);
        root.Children.Add(targetGrid);

        var options = new AppAutomationRecorderOptions();
        options.GridHints.Add(new RecorderGridHint(
            "ItemsGridVisual",
            "ItemsGrid",
            ["Key", "Product"]));
        options.GridSearchPickerHints.Add(new RecorderGridSearchPickerHint(
            "ItemPicker",
            "ItemsGrid",
            SearchPickerParts.ByAutomationIds("ItemPicker_Input", "ItemPicker_Results"),
            ColumnName: "Product"));
        var factory = new RecorderStepFactory(options, () => root);

        var described = factory.TryDescribeSemanticValue(input, out var description, out var descriptionError);
        var result = factory.TryCreateCheckpointStep(input, "productBeforeSave");
        var preview = CreateGenerator().GeneratePreview(result.Step!);

        using (Assert.Multiple())
        {
            await Assert.That(described).IsTrue();
            await Assert.That(descriptionError).IsEmpty();
            await Assert.That(description!.ValueKind).IsEqualTo(RecorderValueKind.GridCellText);
            await Assert.That(description.CurrentValueText).IsEqualTo("Search result");
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step!.Control.LocatorValue).IsEqualTo("ItemsGrid");
            await Assert.That(result.Step.Control.ControlType).IsEqualTo(UiControlType.Grid);
            await Assert.That(result.Step.ValueKind).IsEqualTo(RecorderValueKind.GridCellText);
            await Assert.That(result.Step.ValueAccessorKind).IsEqualTo(RecorderValueAccessorKind.GridCellText);
            await Assert.That(result.Step.StringValue).IsEqualTo("Search result");
            await Assert.That(result.Step.RowIndex).IsEqualTo(0);
            await Assert.That(result.Step.ColumnIndex).IsEqualTo(1);
            await Assert.That(result.Step.CanPersist).IsTrue();
            await Assert.That(preview).Contains(
                "GridValueReader.ReadCellText(Page.ItemsGrid, 0, 1)");
        }
    }

    [Test]
    public async Task Recorder_DoesNotTreatConfiguredGridBackgroundAsBooleanValue()
    {
        var row = new ItemRow("ITEM-42", "Search result");
        var root = new StackPanel();
        var sourceGrid = new GridHost { ItemsSource = new[] { row } };
        var targetGrid = new Border();
        AutomationProperties.SetAutomationId(sourceGrid, "ItemsGridVisual");
        AutomationProperties.SetAutomationId(targetGrid, "ItemsGrid");
        root.Children.Add(sourceGrid);
        root.Children.Add(targetGrid);

        var options = new AppAutomationRecorderOptions();
        options.GridHints.Add(new RecorderGridHint(
            "ItemsGridVisual",
            "ItemsGrid",
            ["Key", "Product"])
        {
            RowIdentityColumnPropertyNames = ["Key"]
        });
        var factory = new RecorderStepFactory(options, () => root);

        var captured = factory.TryCaptureSemanticValueSnapshot(
            sourceGrid,
            out var snapshot,
            out var error);

        using (Assert.Multiple())
        {
            await Assert.That(captured).IsFalse();
            await Assert.That(snapshot).IsNull();
            await Assert.That(error).Contains("concrete grid cell");
            await Assert.That(error).DoesNotContain("IsEnabled");
        }
    }

    [Test]
    [Arguments(0, "ITEM-10")]
    [Arguments(4, "ITEM-50")]
    public async Task GridCatalog_CapturesEveryColumnAcrossVisibleRowsWithStableAddress(
        int rowIndex,
        string expectedKey)
    {
        foreach (var column in new[]
                 {
                     new CatalogCaptureColumn(
                         "Key",
                         "Key",
                         "rowKey",
                         RecorderValueKind.Text,
                         "ReadCellText"),
                     new CatalogCaptureColumn(
                         "RequiredQuantity",
                         "RequiredAmount",
                         "requiredAmount",
                         RecorderValueKind.Number,
                         "ReadCellNumber")
                 })
        {
            var fixture = new CatalogGridCaptureFixture(
                includeRowIdentity: true,
                rowIndex,
                column.SourceFieldName);

            var result = fixture.CaptureCheckpoint(column.CheckpointName);
            var preview = CreateGenerator().GeneratePreview(result.Step!);

            using (Assert.Multiple())
            {
                await Assert.That(result.Success).IsTrue();
                await Assert.That(result.Step!.ValueKind).IsEqualTo(column.ValueKind);
                await Assert.That(result.Step.ValueAccessorKind).IsEqualTo(RecorderValueAccessorKind.GridCellValue);
                await Assert.That(result.Step.GridRowConditions).IsEquivalentTo(
                [
                    new RecordedGridRowCondition("Key", expectedKey)
                ]);
                await Assert.That(result.Step.GridTargetColumnName).IsEqualTo(column.LogicalName);
                await Assert.That(result.Step.RowIndex).IsNull();
                await Assert.That(result.Step.ColumnIndex).IsNull();
                await Assert.That(preview).Contains(
                    $"var {column.CheckpointName} = GridValueReader.{column.ReaderMethod}(Page.ItemsGrid, "
                    + $"GridRowSelector.ByCell(\"Key\", \"{expectedKey}\"), \"{column.LogicalName}\");");
                await Assert.That(preview).DoesNotContain("ArchiveGrid");
            }
        }
    }

    [Test]
    public async Task GridCatalog_RejectsCaptureWithoutStableRowIdentity()
    {
        var fixture = new CatalogGridCaptureFixture(includeRowIdentity: false);

        var result = fixture.CaptureCheckpoint();

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Message).Contains("Configure IdentifyRowsBy");
        }
    }

    [Test]
    public async Task NativeGrid_UsesOnlyExplicitModelKeyAsAutomaticIdentity()
    {
        var grid = new NativeGridMetadataHost
        {
            ItemsSource = new[] { new NativeGridRow("ITEM-42", "Ready") },
            Columns =
            [
                new NativeTextColumn("Key", "Key", isReadOnly: true),
                new NativeTextColumn("State", "State", isReadOnly: true),
                new NativeTextColumn("Localized status", string.Empty, isReadOnly: true)
            ]
        };

        var columns = GridCellMetadataExtractor.ReadNativeColumns(grid);

        using (Assert.Multiple())
        {
            await Assert.That(columns.Single(column => column.SourceFieldName == "Key").IsStableIdentityCandidate)
                .IsTrue();
            await Assert.That(columns.Single(column => column.SourceFieldName == "State").IsStableIdentityCandidate)
                .IsFalse();
            await Assert.That(columns.Single(column => column.LogicalName == "Localized status").SourceFieldName)
                .IsEmpty();
        }
    }

    [Test]
    [Arguments(GridCellEditorKind.Text, "EditGridCellText")]
    [Arguments(GridCellEditorKind.Number, "EditGridCellNumber")]
    [Arguments(GridCellEditorKind.Date, "EditGridCellDate")]
    [Arguments(GridCellEditorKind.Time, "EditGridCellTime")]
    [Arguments(GridCellEditorKind.CheckBox, "SetGridCellChecked")]
    [Arguments(GridCellEditorKind.Color, "EditGridCellColor")]
    public async Task GridCatalog_RecordsTypedCellActionWithStableAddress(
        GridCellEditorKind editorKind,
        string generatedMethod)
    {
        var fixture = new CatalogGridActionFixture(editorKind);

        var result = fixture.Capture();
        var preview = CreateGenerator().GeneratePreview(result.Step!);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step!.CanPersist).IsTrue();
            await Assert.That(result.Step.GridRowConditions).IsEquivalentTo(
            [
                new RecordedGridRowCondition("Key", "ITEM-42")
            ]);
            await Assert.That(result.Step.GridTargetColumnName).IsEqualTo("Value");
            await Assert.That(result.Step.RowIndex).IsNull();
            await Assert.That(result.Step.ColumnIndex).IsNull();
            await Assert.That(preview).Contains($"Page.{generatedMethod}(static page => page.ItemsGrid");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GridCatalog_TextEditIsRecordedOnlyWhenCommittedOrCancelled(bool cancel)
    {
        using var fixture = new CatalogGridTransactionFixture();

        fixture.EnterText("Updated");
        if (cancel)
        {
            fixture.Cancel();
        }
        else
        {
            fixture.Commit();
        }

        var entry = fixture.Session.StepJournal.Single();
        using (Assert.Multiple())
        {
            await Assert.That(entry.CanPersist).IsTrue();
            await Assert.That(entry.Preview).Contains(
                "Page.EditGridCellText(static page => page.ItemsGrid, "
                + "GridRowSelector.ByCell(\"Key\", \"ITEM-42\"), \"Value\", \"Updated\"");
            await Assert.That(entry.Preview.Contains("GridCellEditCommitMode.Cancel", StringComparison.Ordinal))
                .IsEqualTo(cancel);
            await Assert.That(entry.Preview).DoesNotContain("Page.EnterText");
        }
    }

    [Test]
    [Arguments(GridCellEditorKind.ComboBox, "SelectGridCellComboItem")]
    [Arguments(GridCellEditorKind.SearchPicker, "SearchAndSelectGridCell")]
    public async Task GridCatalog_RecordsSelectionActionWithStableAddress(
        GridCellEditorKind editorKind,
        string generatedMethod)
    {
        using var fixture = new GridComboSelectionFixture(
            useCatalog: true,
            catalogEditorKind: editorKind,
            addDuplicateEditorPart: true);

        fixture.SelectStatus("Ready", keyboard: false);

        var entry = fixture.Session.StepJournal.Single();
        using (Assert.Multiple())
        {
            await Assert.That(entry.CanPersist).IsTrue();
            await Assert.That(entry.Preview).Contains(
                $"Page.{generatedMethod}(static page => page.ItemsGrid, "
                + "GridRowSelector.ByCell(\"Key\", \"ITEM-42\"), \"Status\"");
            await Assert.That(entry.Preview).DoesNotContain("SelectListBoxItem");
        }
    }

    [Test]
    public async Task Recorder_UsesConfiguredGridEditorCellForCheckpointComparison()
    {
        var root = new StackPanel();
        var editor = new Border();
        var interactiveInput = new TextBox { Text = "800" };
        var targetGrid = new StackPanel();
        var valueBridge = new TextBox { Text = "800" };

        AutomationProperties.SetAutomationId(editor, "QuantityEditor");
        AutomationProperties.SetAutomationId(targetGrid, "ItemsGrid");
        AutomationProperties.SetAutomationId(valueBridge, "ItemsGrid_Row0_QuantityEditor");

        editor.Child = interactiveInput;
        targetGrid.Children.Add(valueBridge);
        root.Children.Add(editor);
        root.Children.Add(targetGrid);

        var options = new AppAutomationRecorderOptions();
        options.GridEditHints.Add(new RecorderGridEditHint(
            "QuantityEditor",
            "ItemsGrid",
            "ItemsGrid_Row0_QuantityEditor",
            RowIndex: 0,
            ColumnIndex: 3,
            EditorKind: GridCellEditorKind.Number));
        var factory = new RecorderStepFactory(options, () => root);

        var described = factory.TryDescribeSemanticValue(
            interactiveInput,
            out var description,
            out var descriptionError);
        var checkpoint = factory.TryCreateCheckpointStep(interactiveInput, "quantityBeforeSave");
        var assertion = factory.TryCreateCheckpointAssertionStep(
            interactiveInput,
            new RecorderCheckpointOption(
                checkpoint.Step!.CheckpointId!.Value,
                "quantityBeforeSave",
                checkpoint.Step.ValueKind!.Value,
                checkpoint.Step.Control.ProposedPropertyName));
        var preview = CreateGenerator().GeneratePreview([checkpoint.Step, assertion.Step!]);

        using (Assert.Multiple())
        {
            await Assert.That(described).IsTrue();
            await Assert.That(descriptionError).IsEmpty();
            await Assert.That(description!.ValueKind).IsEqualTo(RecorderValueKind.GridCellText);
            await Assert.That(description.CurrentValueText).IsEqualTo("800");
            await Assert.That(checkpoint.Success).IsTrue();
            await Assert.That(assertion.Success).IsTrue();
            await Assert.That(checkpoint.Step.Control.LocatorValue).IsEqualTo("ItemsGrid");
            await Assert.That(checkpoint.Step.RowIndex).IsEqualTo(0);
            await Assert.That(checkpoint.Step.ColumnIndex).IsEqualTo(3);
            await Assert.That(assertion.Step!.Control.LocatorValue).IsEqualTo("ItemsGrid");
            await Assert.That(assertion.Step.RowIndex).IsEqualTo(0);
            await Assert.That(assertion.Step.ColumnIndex).IsEqualTo(3);
            await Assert.That(preview).Contains(
                "var quantityBeforeSave = GridValueReader.ReadCellText(Page.ItemsGrid, 0, 3);");
            await Assert.That(preview).Contains(
                "GridValueReader.ReadCellText(Page.ItemsGrid, 0, 3)");
            await Assert.That(preview).Contains("quantityBeforeSave");
            await Assert.That(preview).Contains("await Assert.That(");
            await Assert.That(preview).DoesNotContain("global::TUnit.Assertions");
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Recorder_CapturesGridComboSelectionAsOneLogicalStep(bool keyboard, bool detachedPopup)
    {
        using var fixture = new GridComboSelectionFixture(detachPopupBeforeSelection: detachedPopup);

        fixture.SelectStatus("Ready", keyboard);

        var entry = fixture.Session.StepJournal.Single();
        using (Assert.Multiple())
        {
            await Assert.That(entry.CanPersist).IsTrue();
            await Assert.That(entry.Preview).Contains(
                "Page.SelectGridCellComboItem(static page => page.ItemsGrid, "
                + "GridRowSelector.ByCell(\"Key\", \"ITEM-42\"), \"Status\", \"Ready\");");
            await Assert.That(entry.Preview).DoesNotContain("SelectListBoxItem");
            await Assert.That(entry.Preview).DoesNotContain("StatusEditor");
            await Assert.That(entry.Preview).DoesNotContain("recorder warning");
        }
    }

    [Test]
    public async Task Recorder_CapturesComboBoxEditorSelectionAsOneLogicalGridStep()
    {
        using var fixture = new GridComboSelectionFixture(useComboBox: true);

        await Assert.That(fixture.ShouldSuppressEditorOpenAction()).IsTrue();
        fixture.SelectStatus("Ready", keyboard: false);

        var entry = fixture.Session.StepJournal.Single();
        var capturedStep = fixture.CreateCurrentSelectionStep().Step!;
        var validated = new RecorderCommandRuntimeValidator(fixture.Options)
            .Validate(capturedStep);
        using (Assert.Multiple())
        {
            await Assert.That(entry.Preview).Contains(
                "Page.SelectGridCellComboItem(static page => page.ItemsGrid, "
                + "GridRowSelector.ByCell(\"Key\", \"ITEM-42\"), \"Status\", \"Ready\");");
            await Assert.That(validated.RuntimeValidationFindings ?? [])
                .DoesNotContain(static finding => finding.ShouldSurface);
        }
    }

    [Test]
    public async Task Recorder_UsesCapturedGridContextWhenPopupClosesDuringSelection()
    {
        using var fixture = new GridComboSelectionFixture(removePopupOnSelection: true);

        fixture.SelectStatus("Ready", keyboard: false);

        var entry = fixture.Session.StepJournal.Single();
        using (Assert.Multiple())
        {
            await Assert.That(fixture.IsPopupAttached).IsFalse();
            await Assert.That(entry.CanPersist).IsTrue();
            await Assert.That(entry.Preview).Contains("SelectGridCellComboItem");
            await Assert.That(entry.Preview).DoesNotContain("PART_ItemsSelector");
        }
    }

    [Test]
    public async Task Recorder_DoesNotCreateGridComboStepWhenPopupClosesWithoutSelection()
    {
        using var fixture = new GridComboSelectionFixture();

        fixture.OpenAndCloseWithoutSelection();

        await Assert.That(fixture.Session.StepJournal).IsEmpty();
    }

    [Test]
    public async Task Recorder_DoesNotCreatePrimitiveStepForGridComboWithMissingColumnContext()
    {
        using var fixture = new GridComboSelectionFixture(exposeColumnContext: false);

        fixture.SelectStatus("Ready", keyboard: false);

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Session.StepJournal).IsEmpty();
            await Assert.That(fixture.Session.LatestStatus).Contains("could not resolve an unambiguous");
            await Assert.That(fixture.Session.LatestPreview).DoesNotContain("SelectListBoxItem");
        }
    }

    [Test]
    public async Task Recorder_UsesGridIndexesWhenStableRowIdentityIsNotConfigured()
    {
        using var fixture = new GridComboSelectionFixture(useStableIdentity: false);

        fixture.SelectStatus("Ready", keyboard: false);

        var entry = fixture.Session.StepJournal.Single();
        await Assert.That(entry.Preview).Contains(
            "Page.SelectGridCellComboItem(static page => page.ItemsGrid, 0, 1, \"Ready\");");
    }

    [Test]
    public async Task Recorder_SuppressesLateSelectionEventAfterGridComboCapture()
    {
        using var fixture = new GridComboSelectionFixture();

        fixture.SelectStatus("Ready", keyboard: false);
        fixture.RaiseLateSelection("Draft");

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(fixture.Session.StepJournal[0].Preview).Contains("\"Ready\"");
        }
    }

    [Test]
    public async Task Recorder_SearchesAllSupportedGridRowContextProperties()
    {
        foreach (var propertyName in new[] { "Row", "RowData", "DataItem", "Item" })
        {
            using var fixture = new GridComboSelectionFixture(rowContextPropertyName: propertyName);

            fixture.SelectStatus("Ready", keyboard: false);

            var entry = fixture.Session.StepJournal.Single();
            await Assert.That(entry.Preview).Contains(
                "Page.SelectGridCellComboItem(static page => page.ItemsGrid, "
                + "GridRowSelector.ByCell(\"Key\", \"ITEM-42\"), \"Status\", \"Ready\");");
        }
    }

    [Test]
    public async Task CheckMode_SkipsAmbiguousAvaloniaObjectIndexersAndResolvesLogicalGridCell()
    {
        var row = new ItemRow("ITEM-42", "Ready");
        var root = new StackPanel();
        var sourceGrid = new GridHost { ItemsSource = new[] { row } };
        var sourceRow = new Border { DataContext = row };
        var sourceCell = new Border { DataContext = row };
        var editor = new Border { DataContext = row };
        var internalSelector = new ComboBox
        {
            DataContext = new AmbiguousItemContext(),
            ItemsSource = new[] { "Draft", "Ready" },
            SelectedIndex = 1
        };
        var targetGrid = new StackPanel();
        var keyCell = new TextBlock { Text = row.Key, DataContext = row };
        var statusCell = new TextBlock { Text = row.Product, DataContext = row };

        AutomationProperties.SetAutomationId(sourceGrid, "ItemsGridVisual");
        AutomationProperties.SetAutomationId(sourceRow, "ItemsGridVisual_Row0");
        AutomationProperties.SetAutomationId(sourceCell, "ItemsGridVisual_Row0_Cell1");
        AutomationProperties.SetAutomationId(editor, "StatusEditor");
        AutomationProperties.SetAutomationId(targetGrid, "ItemsGrid");
        AutomationProperties.SetAutomationId(keyCell, "ItemsGrid_Row0_Cell0");
        AutomationProperties.SetAutomationId(statusCell, "ItemsGrid_Row0_Cell1");

        editor.Child = internalSelector;
        sourceCell.Child = editor;
        sourceRow.Child = sourceCell;
        sourceGrid.Children.Add(sourceRow);
        targetGrid.Children.Add(keyCell);
        targetGrid.Children.Add(statusCell);
        root.Children.Add(sourceGrid);
        root.Children.Add(targetGrid);

        var options = new AppAutomationRecorderOptions();
        options.GridHints.Add(new RecorderGridHint(
            "ItemsGridVisual",
            "ItemsGrid",
            ["Key", "Status"])
        {
            RowIdentityColumnPropertyNames = ["Key"]
        });
        using var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            validationRootProvider: () => root,
            attachWindowHandlers: false);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;

        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(
            internalSelector,
            [internalSelector, editor, sourceCell, sourceRow, sourceGrid]);

        using (Assert.Multiple())
        {
            await Assert.That(selection).IsNotNull();
            await Assert.That(selection!.ValueDescription?.ValueKind).IsEqualTo(RecorderValueKind.GridCellText);
            await Assert.That(selection.ValueDescription?.CurrentValueText).IsEqualTo("Ready");
            await Assert.That(selection.ValueSnapshot!.Prototype.Control.LocatorValue).IsEqualTo("ItemsGrid");
            await Assert.That(selection.ValueSnapshot.Prototype.RowIndex).IsEqualTo(0);
            await Assert.That(selection.ValueSnapshot.Prototype.ColumnIndex).IsEqualTo(1);
            await Assert.That(selection.ValueSnapshot.Prototype.GridTargetColumnName).IsEqualTo("Status");
            await Assert.That(selection.ValueSnapshot.Prototype.Control.LocatorValue).DoesNotContain("StatusEditor");
        }
    }

    private static AuthoringCodeGenerator CreateGenerator()
    {
        return new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
    }

    private static RecordedControlDescriptor GridDescriptor()
    {
        return new RecordedControlDescriptor(
            "OrdersGrid",
            UiControlType.Grid,
            "OrdersGrid",
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: typeof(Border).FullName!,
            Warning: null);
    }

    private sealed class GridCaptureFixture
    {
        private readonly StackPanel _root = new();
        private readonly GridHost _grid;
        private readonly Panel _bridge;
        private readonly IReadOnlyList<OrderRow> _rows;
        private readonly RecorderStepFactory _factory;

        public AppAutomationRecorderOptions Options { get; }

        public GridCaptureFixture(
            IReadOnlyList<OrderRow> rows,
            IReadOnlyList<string> identityColumns,
            Func<OrderRow, int, string>? displayValue = null,
            Func<int, int>? displayedAutomationRowIndex = null)
        {
            _rows = rows;
            _grid = new GridHost { ItemsSource = rows };
            _bridge = new StackPanel();
            AutomationProperties.SetAutomationId(_grid, "OrdersGridVisual");
            AutomationProperties.SetAutomationId(_bridge, "OrdersGrid");
            _root.Children.Add(_grid);
            _root.Children.Add(_bridge);

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                for (var columnIndex = 0; columnIndex < 3; columnIndex++)
                {
                    var displayedCell = new TextBlock
                    {
                        Text = displayValue?.Invoke(rows[rowIndex], columnIndex)
                            ?? ValueAt(rows[rowIndex], columnIndex),
                        DataContext = rows[rowIndex]
                    };
                    AutomationProperties.SetAutomationId(
                        displayedCell,
                        $"OrdersGrid_Row{displayedAutomationRowIndex?.Invoke(rowIndex) ?? rowIndex}_Cell{columnIndex}");
                    _bridge.Children.Add(displayedCell);

                    var sourceCell = new TextBlock
                    {
                        Text = ValueAt(rows[rowIndex], columnIndex),
                        DataContext = rows[rowIndex]
                    };
                    AutomationProperties.SetAutomationId(
                        sourceCell,
                        $"OrdersGridVisual_Row{rowIndex}_Cell{columnIndex}");
                    _grid.Children.Add(sourceCell);
                }
            }

            Options = new AppAutomationRecorderOptions();
            Options.GridHints.Add(new RecorderGridHint(
                "OrdersGridVisual",
                "OrdersGrid",
                ["OrderId", "Customer", "Status"])
            {
                RowIdentityColumnPropertyNames = identityColumns
            });
            _factory = new RecorderStepFactory(Options, () => _root);
        }

        public StepCreationResult CaptureCell(int rowIndex, int columnIndex)
        {
            return _factory.TryCreateAssertionStep(FindCell(rowIndex, columnIndex), RecorderAssertionMode.Text);
        }

        public StepCreationResult CaptureCheckpointCell(int rowIndex, int columnIndex)
        {
            return _factory.TryCreateCheckpointStep(FindCell(rowIndex, columnIndex), "cellValue");
        }

        private TextBlock FindCell(int rowIndex, int columnIndex)
        {
            return _grid.Children
                .OfType<TextBlock>()
                .Single(candidate => string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    $"OrdersGridVisual_Row{rowIndex}_Cell{columnIndex}",
                    StringComparison.Ordinal));
        }

        public StepCreationResult CaptureRow(int rowIndex)
        {
            var row = new Border { DataContext = _rows[rowIndex] };
            _grid.Children.Add(row);
            return _factory.TryCreateAssertionStep(row, RecorderAssertionMode.Text);
        }
    }

    private sealed class GridComboSelectionFixture : IDisposable
    {
        private readonly Border _editor;
        private readonly Control _results;
        private readonly RecorderStepFactory _stepFactory;

        public GridComboSelectionFixture(
            bool useComboBox = false,
            bool removePopupOnSelection = false,
            bool exposeColumnContext = true,
            bool useStableIdentity = true,
            bool detachPopupBeforeSelection = false,
            string rowContextPropertyName = "Row",
            bool useCatalog = false,
            GridCellEditorKind catalogEditorKind = GridCellEditorKind.ComboBox,
            bool addDuplicateEditorPart = false)
        {
            var row = new ItemRow("ITEM-42", "Draft");
            var column = new GridColumnContext(exposeColumnContext ? "WorkflowStatus" : "UnmappedColumn");
            var value = exposeColumnContext ? row.Product : "Unmapped value";
            var cellContext = CreateGridCellContext(
                rowContextPropertyName,
                row,
                column,
                value);
            var root = new StackPanel();
            var sourceGrid = new GridHost { ItemsSource = new[] { row } };
            var sourceRow = new StackPanel { DataContext = cellContext };
            var keyCell = new TextBlock { Text = row.Key, DataContext = row };
            var statusCell = new Border { DataContext = cellContext };
            _editor = new Border { DataContext = cellContext };
            _results = useComboBox
                ? new ComboBox
                {
                    Name = "PART_ItemsSelector",
                    DataContext = cellContext,
                    ItemsSource = new[] { "Draft", "Ready" }
                }
                : new ListBox
                {
                    Name = "PART_ItemsSelector",
                    DataContext = cellContext,
                    ItemsSource = new[] { "Draft", "Ready" }
                };
            var targetGrid = new StackPanel();
            var targetKeyCell = new TextBlock { Text = row.Key, DataContext = row };
            var targetStatusCell = new TextBlock { Text = row.Product, DataContext = row };

            AutomationProperties.SetAutomationId(sourceGrid, "ItemsGridVisual");
            AutomationProperties.SetAutomationId(sourceRow, "ItemsGridVisual_Row0");
            AutomationProperties.SetAutomationId(keyCell, "ItemsGridVisual_Row0_Cell0");
            AutomationProperties.SetAutomationId(_editor, "StatusEditor");
            AutomationProperties.SetAutomationId(targetGrid, "ItemsGrid");
            AutomationProperties.SetAutomationId(targetKeyCell, "ItemsGrid_Row0_Cell0");
            AutomationProperties.SetAutomationId(targetStatusCell, "ItemsGrid_Row0_Cell1");

            if (!detachPopupBeforeSelection)
            {
                _editor.Child = _results;
            }
            statusCell.Child = _editor;
            sourceRow.Children.Add(keyCell);
            sourceRow.Children.Add(statusCell);
            if (addDuplicateEditorPart)
            {
                var otherCell = new Border();
                var otherEditor = new Border();
                var duplicateResults = new ListBox
                {
                    Name = "PART_ItemsSelector",
                    ItemsSource = new[] { "Draft", "Ready" }
                };
                otherEditor.Child = duplicateResults;
                otherCell.Child = otherEditor;
                sourceRow.Children.Add(otherCell);
            }
            sourceGrid.Children.Add(sourceRow);
            targetGrid.Children.Add(targetKeyCell);
            targetGrid.Children.Add(targetStatusCell);
            root.Children.Add(sourceGrid);
            root.Children.Add(targetGrid);
            if (detachPopupBeforeSelection)
            {
                root.Children.Add(_results);
            }

            var gridAutomation = new GridAutomationCatalog();
            if (useCatalog)
            {
                var definition = GridAutomationDefinition
                    .ByAutomationIds("ItemsGrid", "ItemsGridVisual", "ItemsGrid")
                    .WithColumns(
                        GridColumnDefinition.Auto("Key"),
                        GridColumnDefinition.Map("Status")
                            .FromField("WorkflowStatus")
                            .DisplayValueFrom("Product")
                            .EditWith(
                                catalogEditorKind,
                                new GridCellEditorParts(
                                    Results: new GridRelativeLocator(
                                        "PART_ItemsSelector",
                                        GridRelativeLocatorScope.EditorRoot,
                                        UiLocatorKind.Name))));
                if (useStableIdentity)
                {
                    definition = definition.IdentifyRowsBy("Key");
                }

                gridAutomation = gridAutomation.Add(definition);
            }

            Options = new AppAutomationRecorderOptions
            {
                Validation = new RecorderValidationOptions { ValidateRuntimeTargets = false },
                GridAutomation = gridAutomation
            };
            if (!useCatalog)
            {
                Options.GridHints.Add(new RecorderGridHint(
                    "ItemsGridVisual",
                    "ItemsGrid",
                    ["Key", "Status"])
                {
                    RowIdentityColumnPropertyNames = useStableIdentity ? ["Key"] : []
                });
            }
            _stepFactory = new RecorderStepFactory(Options, () => root);

            Session = new RecorderSession(
                RecorderTestWindow.CreateStub(),
                Options,
                validationRootProvider: () => root,
                attachWindowHandlers: false);
            if (removePopupOnSelection)
            {
                switch (_results)
                {
                    case ComboBox comboBox:
                        comboBox.SelectionChanged += (_, _) => _editor.Child = null;
                        break;
                    case ListBox listBox:
                        listBox.SelectionChanged += (_, _) => _editor.Child = null;
                        break;
                }
            }

            Session.Start();
            Session.RefreshObservedControlsForTesting();
        }

        public RecorderSession Session { get; }

        public AppAutomationRecorderOptions Options { get; }

        public bool IsPopupAttached => ReferenceEquals(_editor.Child, _results);

        public bool ShouldSuppressEditorOpenAction()
        {
            return _stepFactory.ShouldSuppressGridComboSelectionButton(_results);
        }

        public StepCreationResult CreateCurrentSelectionStep()
        {
            return _results switch
            {
                ComboBox comboBox => _stepFactory.TryCreateGridComboSelectionStep(comboBox).StepResult,
                ListBox listBox => _stepFactory.TryCreateGridComboSelectionStep(listBox).StepResult,
                _ => throw new InvalidOperationException("Unsupported grid selection source.")
            };
        }

        public void SelectStatus(string status, bool keyboard)
        {
            if (keyboard)
            {
                Session.RegisterKeyboardInputForTesting(_results);
            }
            else
            {
                Session.RegisterPointerInputFromSourceForTesting(_results);
            }

            SetSelectedItem(status);
        }

        public void OpenAndCloseWithoutSelection()
        {
            Session.RegisterPointerInputFromSourceForTesting(_results);
            _editor.Child = null;
        }

        public void RaiseLateSelection(string status)
        {
            SetSelectedItem(status);
        }

        private void SetSelectedItem(string status)
        {
            switch (_results)
            {
                case ComboBox comboBox:
                    comboBox.SelectedItem = status;
                    break;
                case ListBox listBox:
                    listBox.SelectedItem = status;
                    break;
            }
        }

        public void Dispose()
        {
            Session.Dispose();
        }

        private static object CreateGridCellContext(
            string propertyName,
            ItemRow row,
            GridColumnContext column,
            string value)
        {
            return propertyName switch
            {
                "Row" => new GridCellContext(row, column, value),
                "RowData" => new GridRowDataContext(row, column, value),
                "DataItem" => new GridDataItemContext(row, column, value),
                "Item" => new GridItemContext(row, column, value),
                _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null)
            };
        }
    }

    private sealed class CatalogGridCaptureFixture
    {
        private readonly RecorderStepFactory _factory;
        private readonly TextBlock _selectedCell;

        public CatalogGridCaptureFixture(
            bool includeRowIdentity,
            int selectedRowIndex = 0,
            string selectedSourceField = "RequiredQuantity")
        {
            var rows = new[]
            {
                new CatalogItemRow("ITEM-10", 10.5m),
                new CatalogItemRow("ITEM-20", 20.5m),
                new CatalogItemRow("ITEM-30", 30.5m),
                new CatalogItemRow("ITEM-40", 40.5m),
                new CatalogItemRow("ITEM-50", 10.5m)
            };
            var row = rows[selectedRowIndex];
            object selectedValue = selectedSourceField switch
            {
                "Key" => row.Key,
                "RequiredQuantity" => row.RequiredQuantity,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(selectedSourceField),
                    selectedSourceField,
                    null)
            };
            var cellContext = new CatalogCellContext(
                row,
                new GridColumnContext(selectedSourceField),
                selectedValue);
            var root = new StackPanel();
            var archiveRow = new CatalogItemRow("ITEM-10", 999m);
            var archiveGrid = new GridHost { ItemsSource = new[] { archiveRow } };
            var archiveRuntimeGrid = new Border();
            var sourceGrid = new GridHost { ItemsSource = rows };
            var rowPresenter = new Border { DataContext = cellContext };
            _selectedCell = new TextBlock
            {
                Text = selectedValue.ToString(),
                DataContext = cellContext
            };
            var runtimeGrid = new Border();

            AutomationProperties.SetAutomationId(archiveGrid, "ArchiveGridVisual");
            AutomationProperties.SetAutomationId(archiveRuntimeGrid, "ArchiveGrid");
            AutomationProperties.SetAutomationId(sourceGrid, "ItemsGridVisual");
            AutomationProperties.SetAutomationId(runtimeGrid, "ItemsGrid");
            rowPresenter.Child = _selectedCell;
            sourceGrid.Children.Add(rowPresenter);
            root.Children.Add(archiveGrid);
            root.Children.Add(archiveRuntimeGrid);
            root.Children.Add(sourceGrid);
            root.Children.Add(runtimeGrid);

            var definition = CreateDefinition("ItemsGrid", "ItemsGridVisual", "ItemsGrid");
            if (includeRowIdentity)
            {
                definition = definition.IdentifyRowsBy("Key");
            }

            var archiveDefinition = CreateDefinition(
                    "ArchiveGrid",
                    "ArchiveGridVisual",
                    "ArchiveGrid")
                .IdentifyRowsBy("Key");

            var options = new AppAutomationRecorderOptions
            {
                GridAutomation = new GridAutomationCatalog()
                    .Add(archiveDefinition)
                    .Add(definition),
                Validation = new RecorderValidationOptions { ValidateRuntimeTargets = false }
            };
            _factory = new RecorderStepFactory(options, () => root);
        }

        public StepCreationResult CaptureCheckpoint(string variableName = "requiredAmount")
        {
            return _factory.TryCreateCheckpointStep(_selectedCell, variableName);
        }

        private static GridAutomationDefinition CreateDefinition(
            string pagePropertyName,
            string captureAutomationId,
            string runtimeAutomationId)
        {
            return GridAutomationDefinition
                .ByAutomationIds(pagePropertyName, captureAutomationId, runtimeAutomationId)
                .WithColumns(
                    GridColumnDefinition.Auto("Key"),
                    GridColumnDefinition.Map("RequiredAmount")
                        .FromField("RequiredQuantity")
                        .AsValue(GridCellValueKind.Number));
        }
    }

    private sealed class CatalogGridActionFixture
    {
        private readonly RecorderStepFactory _factory;
        private readonly Control _editor;

        public CatalogGridActionFixture(GridCellEditorKind editorKind)
        {
            var value = CreateValue(editorKind);
            var row = new CatalogActionRow("ITEM-42", value.RawValue);
            var context = new CatalogActionCellContext(
                row,
                new GridColumnContext("Value"),
                value.RawValue);
            var root = new StackPanel();
            var sourceGrid = new GridHost { ItemsSource = new[] { row } };
            var cell = new Border { DataContext = context };
            _editor = value.Editor;
            _editor.DataContext = context;
            var runtimeGrid = new Border();

            AutomationProperties.SetAutomationId(sourceGrid, "ItemsGridVisual");
            AutomationProperties.SetAutomationId(runtimeGrid, "ItemsGrid");
            cell.Child = _editor;
            sourceGrid.Children.Add(cell);
            root.Children.Add(sourceGrid);
            root.Children.Add(runtimeGrid);

            var definition = GridAutomationDefinition
                .ByAutomationIds("ItemsGrid", "ItemsGridVisual", "ItemsGrid")
                .WithColumns(
                    GridColumnDefinition.Auto("Key"),
                    GridColumnDefinition.Auto("Value")
                        .AsValue(value.ValueKind)
                        .EditWith(editorKind))
                .IdentifyRowsBy("Key");
            var options = new AppAutomationRecorderOptions
            {
                GridAutomation = new GridAutomationCatalog().Add(definition),
                Validation = new RecorderValidationOptions { ValidateRuntimeTargets = false }
            };
            _factory = new RecorderStepFactory(options, () => root);
        }

        public StepCreationResult Capture()
        {
            return _factory.TryCreateGridCellEditStep(_editor).StepResult;
        }

        private static CatalogActionValue CreateValue(GridCellEditorKind editorKind)
        {
            return editorKind switch
            {
                GridCellEditorKind.Text => new CatalogActionValue(
                    new TextBox { Text = "Updated" },
                    "Updated",
                    GridCellValueKind.Text),
                GridCellEditorKind.Number => new CatalogActionValue(
                    new NumericUpDown { Value = 12.5m },
                    12.5m,
                    GridCellValueKind.Number),
                GridCellEditorKind.Date => new CatalogActionValue(
                    new DatePicker { SelectedDate = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero) },
                    new DateTime(2026, 9, 3),
                    GridCellValueKind.Date),
                GridCellEditorKind.Time => new CatalogActionValue(
                    new TimePicker { SelectedTime = new TimeSpan(9, 45, 0) },
                    new TimeSpan(9, 45, 0),
                    GridCellValueKind.Time),
                GridCellEditorKind.CheckBox => new CatalogActionValue(
                    new CheckBox { IsChecked = true },
                    true,
                    GridCellValueKind.Boolean),
                GridCellEditorKind.Color => new CatalogActionValue(
                    new TextBox { Text = "#336699" },
                    "#336699",
                    GridCellValueKind.Color),
                _ => throw new ArgumentOutOfRangeException(nameof(editorKind), editorKind, null)
            };
        }
    }

    private sealed class CatalogGridTransactionFixture : IDisposable
    {
        private readonly TextBox _editor;

        public CatalogGridTransactionFixture()
        {
            var row = new CatalogActionRow("ITEM-42", "Initial");
            var context = new CatalogActionCellContext(
                row,
                new GridColumnContext("Value"),
                row.Value);
            var root = new StackPanel();
            var sourceGrid = new GridHost { ItemsSource = new[] { row } };
            var cell = new Border { DataContext = context };
            _editor = new TextBox { Text = "Initial", DataContext = context };
            var runtimeGrid = new Border();

            AutomationProperties.SetAutomationId(sourceGrid, "ItemsGridVisual");
            AutomationProperties.SetAutomationId(runtimeGrid, "ItemsGrid");
            cell.Child = _editor;
            sourceGrid.Children.Add(cell);
            root.Children.Add(sourceGrid);
            root.Children.Add(runtimeGrid);

            var definition = GridAutomationDefinition
                .ByAutomationIds("ItemsGrid", "ItemsGridVisual", "ItemsGrid")
                .WithColumns(
                    GridColumnDefinition.Auto("Key"),
                    GridColumnDefinition.Auto("Value")
                        .AsValue(GridCellValueKind.Text)
                        .EditWith(GridCellEditorKind.Text))
                .IdentifyRowsBy("Key");
            var options = new AppAutomationRecorderOptions
            {
                GridAutomation = new GridAutomationCatalog().Add(definition),
                Validation = new RecorderValidationOptions { ValidateRuntimeTargets = false }
            };

            Session = new RecorderSession(
                RecorderTestWindow.CreateStub(),
                options,
                validationRootProvider: () => root,
                attachWindowHandlers: false);
            Session.Start();
            Session.RefreshObservedControlsForTesting();
        }

        public RecorderSession Session { get; }

        public void EnterText(string text)
        {
            Session.RegisterKeyboardInputForTesting(_editor);
            _editor.Text = text;
        }

        public void Commit() => Session.FlushPendingStateForTesting();

        public void Cancel() => Session.CancelPendingGridCellEditForTesting();

        public void Dispose() => Session.Dispose();
    }

    private static string ValueAt(OrderRow row, int columnIndex)
    {
        return columnIndex switch
        {
            0 => row.OrderId,
            1 => row.Customer,
            2 => row.Status,
            _ => throw new ArgumentOutOfRangeException(nameof(columnIndex))
        };
    }

    private sealed class GridHost : Panel
    {
        public object? ItemsSource { get; init; }
    }

    private sealed class NativeGridMetadataHost : Panel
    {
        public object? ItemsSource { get; init; }

        public IReadOnlyList<NativeTextColumn> Columns { get; init; } = [];
    }

    private sealed class NativeTextColumn(
        string header,
        string sortMemberPath,
        bool isReadOnly)
    {
        public string Header { get; } = header;

        public string SortMemberPath { get; } = sortMemberPath;

        public bool IsVisible => true;

        public bool IsReadOnly { get; } = isReadOnly;
    }

    private sealed record NativeGridRow(
        [property: Key] string Key,
        string State);

    private sealed record OrderRow(string OrderId, string Customer, string Status);

    private sealed record ItemRow(string Key, string Product);

    private sealed class CatalogItemRow(string key, decimal requiredQuantity) : IEquatable<CatalogItemRow>
    {
        public string Key { get; } = key;

        public decimal RequiredQuantity { get; } = requiredQuantity;

        public bool Equals(CatalogItemRow? other)
        {
            return other is not null && RequiredQuantity == other.RequiredQuantity;
        }

        public override bool Equals(object? obj) => obj is CatalogItemRow other && Equals(other);

        public override int GetHashCode() => RequiredQuantity.GetHashCode();
    }

    private sealed record CatalogCaptureColumn(
        string SourceFieldName,
        string LogicalName,
        string CheckpointName,
        RecorderValueKind ValueKind,
        string ReaderMethod);

    private sealed record CatalogActionRow(string Key, object? Value);

    private sealed record CatalogActionCellContext(
        CatalogActionRow Row,
        GridColumnContext Column,
        object? Value);

    private sealed record CatalogActionValue(
        Control Editor,
        object? RawValue,
        GridCellValueKind ValueKind);

    private sealed record CatalogCellContext(
        CatalogItemRow Row,
        GridColumnContext Column,
        object Value);

    private sealed record GridCellContext(ItemRow Row, GridColumnContext Column, string Value);

    private sealed record GridRowDataContext(ItemRow RowData, GridColumnContext Column, string Value);

    private sealed record GridDataItemContext(ItemRow DataItem, GridColumnContext Column, string Value);

    private sealed record GridItemContext(ItemRow Item, GridColumnContext Column, string Value);

    private sealed record GridColumnContext(string FieldName);

    private sealed class AmbiguousItemContext : AvaloniaObject;
}
