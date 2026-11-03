using System.ComponentModel.DataAnnotations;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

[NotInParallel]
public sealed class RecorderGroupedGridTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task NestedHeaderCaptureAndCopyKeepTheCompletePathAfterReorder(bool publishedAddress) => OnUI(async () =>
    {
        var root = Host();
        var first = Group("Customer", "First", 0, "Контрагент: First");
        var second = Group("Order", "42", 1, "Номер заказа: 42");
        var third = Group("Customer", "Second", 0, "Контрагент: Second");
        var fourth = Group("Order", "42", 1, "Номер заказа: 42");
        foreach (var group in new[] { first, second, third, fourth }) root.Children.Add(group);
        if (publishedAddress)
        {
            AutomationProperties.SetItemStatus(second, GridHeaderSelector.Group("Customer", "First").ThenGroup("Order", "42").ToAutomationValue());
            AutomationProperties.SetItemStatus(fourth, GridHeaderSelector.Group("Customer", "Second").ThenGroup("Order", "42").ToAutomationValue());
            AutomationProperties.SetName(second, "Номер заказа: 42");
            AutomationProperties.SetName(fourth, "Номер заказа: 42");
            root.Children.Remove(first);
            root.Children.Remove(third);
        }
        Arrange(root);
        using var session = Session(root);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, e) => selection = e.Selection;
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting((Control)fourth.Child!);
        await Assert.That(selection!.ValueSnapshot).IsNotNull();
        var address = selection.ValueSnapshot!.Prototype.GridHeader;
        await Assert.That(address).IsEqualTo(GridHeaderSelector.Group("Customer", "Second").ThenGroup("Order", "42"));
        ((IRecorderCheckpointSessionDetails)session).CaptureCurrentValueAssertion(selection);
        await Assert.That(session.ExportPreview()).Contains("GridHeaderKeyReader.CaptureFirst");
        await Assert.That(session.ExportPreview()).DoesNotContain("GridHeaderSelector.Group(");
        ((IRecorderCheckpointSessionDetails)session).CaptureCheckpoint(selection, "orderNumber");
        await Assert.That(session.ExportPreview()).Contains("GridHeaderKeyReader.CaptureFirst");
        await Assert.That(session.ExportPreview()).DoesNotContain("\"Second\"");

        root.Children.Clear();
        foreach (var group in publishedAddress ? new[] { fourth, second } : new[] { third, fourth, first, second }) root.Children.Add(group);
        Arrange(root);
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting((Control)fourth.Child!);
        await Assert.That(selection.ValueSnapshot!.Prototype.GridHeader).IsEqualTo(address);

        RecorderCopiedValueTargetSelection? copy = null;
        session.CopiedValueTargetSelected += (_, e) => copy = e.Selection;
        session.BeginCopiedValueTargetSelection();
        session.SelectCopiedValueTargetForTesting((Control)fourth.Child!);
        string? clipboard = null;
        await session.CommitCopiedValueAsync(copy!, (text, _) => { clipboard = text; return Task.CompletedTask; });
        await Assert.That(clipboard).IsEqualTo("Номер заказа: 42");
        await Assert.That(session.ExportPreview()).Contains("ReadHeaderText");
        await Assert.That(session.ExportPreview()).DoesNotContain("ReadCellText");
        await Assert.That(session.ExportPreview().Split("GridHeaderKeyReader.CaptureFirst").Length - 1).IsEqualTo(1);
        await Assert.That(session.ExportPreview()).DoesNotContain("\"42\"");

        var headerStep = selection.ValueSnapshot!.Prototype with
        {
            CheckpointId = Guid.NewGuid(),
            CheckpointVariableName = "orderNumber",
            TextTransform = new(":"),
            CapturedDisplayText = "Номер заказа: 42"
        };
        var rowStep = new RecordedStep(RecordedActionKind.SelectGridRow, headerStep.Control, StepId: Guid.NewGuid())
        {
            CanCaptureGridRowKey = true,
            GridRowConditions = [new("Id", "a74e0bb3-5a67-4630-81dd-be66a155201b")]
        };
        var openStep = rowStep with { ActionKind = RecordedActionKind.OpenGridRow, StepId = Guid.NewGuid() };
        var otherRow = rowStep with { StepId = Guid.NewGuid(), GridRowConditions = [new("Id", "another-explicit-row")] };
        var copiedStep = headerStep with
        {
            ActionKind = RecordedActionKind.CaptureCopiedValue,
            CheckpointId = null,
            CheckpointVariableName = null,
            CopiedValueId = Guid.NewGuid(),
            CopiedValueVariableName = "copiedOrder",
            StepId = Guid.NewGuid()
        };
        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var isolated = generator.GeneratePreview([headerStep, copiedStep, rowStep, openStep, otherRow]);
        await Assert.That(isolated).Contains("var gridRow = GridRowKeyReader.CaptureFirst(Page.ObservedGrid);");
        await Assert.That(isolated).Contains("page.ObservedGrid, gridRow)");
        await Assert.That(isolated.Split("GridRowKeyReader.CaptureFirst").Length - 1).IsEqualTo(2);
        await Assert.That(isolated).DoesNotContain("a74e0bb3");
        await Assert.That(isolated).DoesNotContain("another-explicit-row");
        await Assert.That(isolated).Contains("GridRowKeyReader.CaptureFirst(Page.ObservedGrid, excludingRows: new[] { gridRow })");
        await Assert.That(generator.GeneratePreview(rowStep)).DoesNotContain("a74e0bb3");
        await Assert.That(generator.GeneratePreview([headerStep, openStep])).DoesNotContain("a74e0bb3");
        await Assert.That(generator.GeneratePreviewForStep(copiedStep, [headerStep, copiedStep]))
            .Contains("ReadHeaderText(Page.ObservedGrid, gridHeader)");
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage, RecorderScenarioDestinationSources.CompilableScenario);
        var saved = await project.SaveAsync(project.CreateSaveContext("Grouped header", "grouped-draft"), [headerStep, copiedStep, rowStep, openStep]);
        await Assert.That(RecorderGeneratedSourceCompiler.Compile(project.RootPath)).IsEmpty();
        var state = new RecorderAutosaveState("Grouped header", "grouped-draft", "GroupedHeader", DateTimeOffset.UtcNow, [headerStep, copiedStep, rowStep, openStep]);
        var autosave = Path.Combine(project.RootPath, "header.autosave.cs");
        await File.WriteAllTextAsync(autosave, RecorderAutosaveStateSerializer.CreateMarker(state));
        var restored = RecorderAutosaveStateSerializer.TryRead(autosave, out var restoredState, out var error);
        await Assert.That(restored).IsTrue().Because(error);
        await Assert.That(restoredState!.Steps[0].GridHeader).IsEqualTo(address);
        await Assert.That(restoredState.Steps[0].TextTransform).IsEqualTo(new TextValueTransform(":"));
        await Assert.That(generator.GeneratePreview(restoredState.Steps)).DoesNotContain("a74e0bb3");
        await Assert.That(await File.ReadAllTextAsync(saved.ScenarioFilePath!)).Contains("TextValue.AfterFirstSeparator(GridValueReader.ReadHeaderText");
        var invalid = System.Text.Json.JsonSerializer.SerializeToNode(state)!;
        invalid["Steps"]![0]!["GridHeader"]!["Path"] = new System.Text.Json.Nodes.JsonArray();
        await File.WriteAllTextAsync(autosave, RecorderAutosaveStateSerializer.MarkerPrefix
            + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(invalid.ToJsonString())));
        await Assert.That(RecorderAutosaveStateSerializer.TryRead(autosave, out _, out var invalidError)).IsFalse();
        await Assert.That(invalidError).Contains("invalid recovery payload");
    });

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task HeaderRejectsDuplicateFullPathOrMissingParentWithoutEnabledFallback(bool missingParent, bool malformedAddress) => OnUI(async () =>
    {
        var root = Host();
        var target = Group("Order", "42", missingParent ? 1 : 0, "Номер заказа: 42");
        root.Children.Add(target);
        if (!missingParent) root.Children.Add(Group("Order", "42", 0, "Номер заказа: 42"));
        if (malformedAddress) AutomationProperties.SetItemStatus(target, "AppAutomation.GridHeader/1:{invalid}");
        Arrange(root);
        using var session = Session(root);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, e) => selection = e.Selection;
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting((Control)target.Child!);
        await Assert.That(selection!.ValueSnapshot).IsNull();
        await Assert.That(selection.CanCaptureAssertions).IsFalse();
        await Assert.That(selection.ValueDescriptionError).Contains(malformedAddress ? "UIA address is invalid"
            : missingParent ? "parent group path" : "matched 2");
        await Assert.That(session.StepCount).IsEqualTo(0);
    });

    [Test]
    public Task NativeHeadersWithTheSameCaptionUseDifferentFieldsAndBandPaths() => OnUI(async () =>
    {
        var root = Host();
        root.Columns = [new("Planned", "План"), new("Actual", "План")];
        var first = new ColumnHeader { Column = root.Columns[0], Child = new TextBlock { Text = "План" }, Height = 30 };
        var second = new ColumnHeader { Column = root.Columns[1], Child = new TextBlock { Text = "План" }, Height = 30 };
        root.Children.Add(first);
        root.Children.Add(second);
        var nested = Host();
        AutomationProperties.SetAutomationId(nested, "NestedGrid");
        nested.Children.Add(new ColumnHeader { Column = root.Columns[1], Child = new TextBlock { Text = "План" } });
        root.Children.Add(nested);
        var volume = new Band("Volume", "Объём", [new("PlannedVolume", "План", [])]);
        var cost = new Band("Cost", "Стоимость", [new("PlannedCost", "План", [])]);
        root.Bands = [volume, cost];
        var heading = new BandHeader { Band = cost.Bands[0], Child = new TextBlock { Text = "План" } };
        root.Children.Add(heading);
        Arrange(root);
        using var session = Session(root);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, e) => selection = e.Selection;
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting((Control)second.Child!);
        await Assert.That(selection!.ValueSnapshot!.Prototype.GridHeader).IsEqualTo(GridHeaderSelector.Column("Actual"));
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting((Control)heading.Child!);
        await Assert.That(selection.ValueSnapshot!.Prototype.GridHeader).IsEqualTo(GridHeaderSelector.Band("Cost").ThenBand("PlannedCost"));
    });

    [Test]
    public Task TypedGroupKeysDoNotUseTheirDisplayCaption() => OnUI(async () =>
    {
        var root = Host();
        var headers = new[] { Group("Approved", true, 0, "Утвержден"), Group("Approved", null, 0, "Нет"), Group("Approved", "", 0, "Нет") };
        foreach (var header in headers) root.Children.Add(header);
        Arrange(root);
        using var session = Session(root);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, e) => selection = e.Selection;
        var expected = new[] { GridHeaderSelector.Group("Approved", "true", GridCellValueKind.Boolean),
            GridHeaderSelector.Group("Approved", null), GridHeaderSelector.Group("Approved", "") };
        for (var index = 0; index < headers.Length; index++)
        {
            session.BeginCheckTargetSelection();
            session.SelectCheckTargetForTesting((Control)headers[index].Child!);
            await Assert.That(selection!.ValueSnapshot!.Prototype.GridHeader).IsEqualTo(expected[index]);
        }
    });

    [Test]
    [Arguments(GridCellValueKind.Text, "Product", RecorderValueKind.Text)]
    [Arguments(GridCellValueKind.Number, "1\u00a0200", RecorderValueKind.Number)]
    [Arguments(GridCellValueKind.Selection, "Pending", RecorderValueKind.Text)]
    [Arguments(GridCellValueKind.Boolean, "Нет", RecorderValueKind.Boolean)]
    [Arguments(GridCellValueKind.Date, "23.10.2026", RecorderValueKind.Date)]
    [Arguments(GridCellValueKind.DateTime, "03.11.2026 16:21", RecorderValueKind.DateTime)]
    [Arguments(GridCellValueKind.Time, "12:34:56", RecorderValueKind.Time)]
    [Arguments(GridCellValueKind.Color, "#FF0022", RecorderValueKind.Color)]
    public Task ReadOnlyLaterCellKeepsItsTypedCheckAndDisplayedCopy(
        GridCellValueKind kind, string display, RecorderValueKind expectedKind) => OnUI(async () =>
    {
        object value = kind switch
        {
            GridCellValueKind.Number => 1200d,
            GridCellValueKind.Boolean => false,
            GridCellValueKind.Date => new DateTime(2026, 10, 23),
            GridCellValueKind.DateTime => new DateTime(2026, 11, 3, 16, 21, 43).AddTicks(12345),
            GridCellValueKind.Time => new TimeSpan(12, 34, 56),
            GridCellValueKind.Color => "#FF0022",
            _ => display
        };
        var rows = Enumerable.Range(0, 11).Select(i => new Row(i.ToString(), value)).ToArray();
        var root = Host();
        root.ItemsSource = rows;
        var column = new Column("Value", "Значение");
        var input = new TextBox
        {
            Text = display,
            IsReadOnly = true,
            DataContext = new Cell(rows[8], column, value)
        };
        root.Children.Add(input);
        Arrange(root);
        var valueColumn = GridColumnDefinition.Map("Value").FromField("Value").AsValue(kind);
        if (kind == GridCellValueKind.DateTime) valueColumn = valueColumn.FormatWith("dd.MM.yyyy HH:mm", "ru-RU");
        var definition = GridAutomationDefinition.ByAutomationIds("ObservedGrid", "ObservedGrid", "ObservedGrid")
            .WithColumns(GridColumnDefinition.Map("Id").FromField("Id"),
                valueColumn)
            .IdentifyRowsBy("Id");
        using var session = Session(root, definition);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, e) => selection = e.Selection;
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(input);
        await Assert.That(selection!.ValueDescriptionError).IsNullOrEmpty();
        await Assert.That(selection.ValueSnapshot!.Description.ValueKind).IsEqualTo(expectedKind);
        await Assert.That(selection.ValueSnapshot.Prototype.CapturedDisplayText).IsEqualTo(display);
        if (kind == GridCellValueKind.DateTime)
            await Assert.That(selection.ValueSnapshot.Prototype.DateValue).IsEqualTo(new DateTime(2026, 11, 3, 16, 21, 0));
        await Assert.That(selection.ValueSnapshot.Prototype.GridRowConditions!.Single().Value).IsEqualTo("8");
        var factory = new RecorderStepFactory(new AppAutomationRecorderOptions(), () => root);
        var firstStep = factory.TryCreateAssertionStep(selection.ValueSnapshot, RecorderAssertionMode.Auto).Step!;
        var details = (IRecorderCheckpointSessionDetails)session;
        RecordedStep? dateTimeReferenceAssertion = null;
        await Assert.That(selection.ValueSnapshot.Prototype.GridCapturedRowPosition).IsNull();
        details.CaptureCheckpoint(selection, "cellCheckpoint");
        var checkpointPreview = session.ExportPreview();
        if (kind == GridCellValueKind.DateTime)
        {
            await Assert.That(checkpointPreview).Contains("GridValueReader.ReadCellDateTime");
            details.CaptureCheckpointAssertion(selection, details.Checkpoints.Single().CheckpointId);
            dateTimeReferenceAssertion = factory.TryCreateCheckpointAssertionStep(
                selection.ValueSnapshot, details.Checkpoints.Single()).Step!;
            await Assert.That(session.ExportPreview()).Contains(".IsEqualTo(cellCheckpoint)");
            details.CaptureLiteralAssertion(selection, "2026-11-03 16:22", RecorderComparisonKind.Equal);
            await Assert.That(session.ExportPreview()).Contains("System.DateTime(2026, 11, 3, 16, 22, 0)");
            details.CapturePresenceAssertion(selection, expectEmpty: false);
            await Assert.That(session.ExportPreview()).Contains(".IsNotNull()");
        }
        await Assert.That(checkpointPreview).Contains("GridRowKeyReader.CaptureFirst(Page.ObservedGrid)");
        await Assert.That(checkpointPreview).DoesNotContain("GridRowSelector.ByCell");
        var checkpointStep = factory.TryCreateCheckpointStep(selection.ValueSnapshot, "cellCheckpoint").Step!;
        if (dateTimeReferenceAssertion is not null)
            dateTimeReferenceAssertion = dateTimeReferenceAssertion with { ExpectedCheckpointId = checkpointStep.CheckpointId };
        session.Clear();

        RecorderCopiedValueTargetSelection? copy = null;
        session.CopiedValueTargetSelected += (_, e) => copy = e.Selection;
        session.BeginCopiedValueTargetSelection();
        session.SelectCopiedValueTargetForTesting(input);
        await Assert.That(copy!.TargetSelection.ValueSnapshot!.Prototype.CapturedDisplayText).IsEqualTo(display);
        await Assert.That(copy.CopiedValue.PreviewValue).IsEqualTo(display);
        string? clipboard = null;
        await session.CommitCopiedValueAsync(copy!, (text, _) => { clipboard = text; return Task.CompletedTask; });
        var copyPreview = session.ExportPreview();
        await Assert.That(copyPreview).Contains("GridRowKeyReader.CaptureFirst(Page.ObservedGrid)");
        await Assert.That(copyPreview).DoesNotContain("GridRowSelector.ByCell");
        var copyStep = factory.TryCreateCopiedValueStep(copy.TargetSelection.ValueSnapshot, copy.CopiedValue).Step!;
        await Assert.That(clipboard).IsEqualTo(display);
        session.Clear();

        details.CaptureCurrentValueAssertion(selection);
        var firstCheck = session.ExportPreview();
        await Assert.That(firstCheck).Contains("var gridRow = GridRowKeyReader.CaptureFirst(Page.ObservedGrid);");
        await Assert.That(firstCheck).DoesNotContain("ByCell(\"Id\", \"8\")");
        await Assert.That(firstCheck).Contains("await Assert.That(GridValueReader.ReadCell");

        // A different captured row needs its own runtime address, not another recorded key.
        input.DataContext = new Cell(rows[9], column, value);
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(input);
        details.CaptureCurrentValueAssertion(selection!);
        var secondStep = factory.TryCreateAssertionStep(selection!.ValueSnapshot, RecorderAssertionMode.Auto).Step!;
        var checks = session.ExportPreview();
        await Assert.That(checks).Contains("var gridRow2 = GridRowKeyReader.CaptureFirst(Page.ObservedGrid, excludingRows: new[] { gridRow });");
        await Assert.That(checks).DoesNotContain("GridRowSelector.ByCell");

        session.BeginCopiedValueTargetSelection();
        session.SelectCopiedValueTargetForTesting(input);
        await Assert.That(copy!.TargetSelection.ValueSnapshot!.Prototype.CapturedDisplayText).IsEqualTo(display);
        await Assert.That(copy.CopiedValue.PreviewValue).IsEqualTo(display);
        await session.CommitCopiedValueAsync(copy!, (text, _) => { clipboard = text; return Task.CompletedTask; });
        await Assert.That(clipboard).IsEqualTo(display);
        await Assert.That(input.Text).IsEqualTo(display);
        await Assert.That(root.ItemsSource[8].Value).IsEqualTo(value);
        await Assert.That(session.ExportPreview()).Contains("ReadCellText");

        RecordedStep[] steps = dateTimeReferenceAssertion is null
            ? [checkpointStep, copyStep, firstStep, secondStep]
            : [checkpointStep, dateTimeReferenceAssertion, copyStep, firstStep, secondStep];
        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var combined = generator.GeneratePreview(steps);
        await Assert.That(combined).DoesNotContain("GridRowSelector.ByCell");
        await Assert.That(combined.Split("GridRowKeyReader.CaptureFirst").Length - 1).IsEqualTo(2);
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage, RecorderScenarioDestinationSources.CompilableScenario);
        var saved = await project.SaveAsync(project.CreateSaveContext("Cell checks", "cell-checks"), steps);
        await Assert.That(RecorderGeneratedSourceCompiler.Compile(project.RootPath)).IsEmpty();
        await Assert.That(await File.ReadAllTextAsync(saved.ScenarioFilePath!)).DoesNotContain("GridRowSelector.ByCell");
        var autosaved = await project.AutosaveAsync(project.CreateSaveContext("Cell checks", "cell-checks"), steps);
        await Assert.That(RecorderAutosaveStateSerializer.TryRead(autosaved.ScenarioFilePath!, out var state, out var error)).IsTrue().Because(error);
        await Assert.That(project.Generator.GeneratePreview(state!.Steps)).IsEqualTo(combined);
        foreach (var step in new[] { checkpointStep, copyStep, firstStep })
            await Assert.That(generator.GeneratePreviewForStep(step, steps)).DoesNotContain("GridRowSelector.ByCell");
    });

    [Test]
    public Task ConfiguredGridValueResolverUsesRuntimeKeysForAllReadActions() => OnUI(async () =>
    {
        var root = Host();
        const string recordedKey = "c637ffe8-540b-44fb-821a-059fcf6aef13";
        root.ItemsSource = [new Row(recordedKey, "Product")];
        var input = new TextBox { Text = "Product", IsReadOnly = true };
        root.Children.Add(input);
        Arrange(root);
        var definition = GridAutomationDefinition.ByAutomationIds("ObservedGrid", "ObservedGrid", "ObservedGrid")
            .WithColumns(GridColumnDefinition.Auto("Id"), GridColumnDefinition.Auto("Value"))
            .IdentifyRowsBy("Id");
        var resolver = new SemanticValueResolver(source => ReferenceEquals(source, input)
            ? RecorderSemanticValueResolution.Resolved(new RecorderSemanticValueTarget(
                "ObservedGrid", UiControlType.Grid, RecorderValueKind.Text, RecorderValueAccessorKind.GridCellValue)
            {
                StringValue = "Product",
                GridContext = new([new("Id", recordedKey)], "Value")
            })
            : RecorderSemanticValueResolution.NotHandled);
        using var session = Session(root, definition, resolver);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, e) => selection = e.Selection;
        var details = (IRecorderCheckpointSessionDetails)session;
        foreach (var action in new[] { RecordedActionKind.CaptureCheckpoint, RecordedActionKind.CaptureCopiedValue, RecordedActionKind.AssertValue })
        {
            session.Clear();
            session.BeginCheckTargetSelection();
            session.SelectCheckTargetForTesting(input);
            await Assert.That(selection!.ValueSnapshot).IsNotNull();
            await Assert.That(selection.ValueSnapshot!.Prototype.GridCapturedRowPosition).IsNull();
            if (action == RecordedActionKind.CaptureCheckpoint) details.CaptureCheckpoint(selection, "productCheckpoint");
            else if (action == RecordedActionKind.AssertValue) details.CaptureCurrentValueAssertion(selection);
            else
            {
                RecorderCopiedValueTargetSelection? copied = null;
                session.CopiedValueTargetSelected += (_, e) => copied = e.Selection;
                session.BeginCopiedValueTargetSelection();
                session.SelectCopiedValueTargetForTesting(input);
                await session.CommitCopiedValueAsync(copied!, (_, _) => Task.CompletedTask);
            }
            await Assert.That(session.ExportPreview()).DoesNotContain(recordedKey);
            await Assert.That(session.ExportPreview()).Contains("GridRowKeyReader.CaptureFirst(Page.ObservedGrid)");
        }
    });

    [Test]
    public Task CatalogComboOpenPartDoesNotRecordStandaloneToggle() => OnUI(async () =>
    {
        var root = Host();
        var row = new Row("row", "Pending");
        root.ItemsSource = [row];
        var toggle = new global::Avalonia.Controls.Primitives.ToggleButton
        {
            Width = 100,
            Height = 30,
            DataContext = new Cell(row, new Column("Value", "State"), "Pending")
        };
        AutomationProperties.SetAutomationId(toggle, "StateOpen");
        root.Children.Add(toggle);
        Arrange(root);
        var definition = GridAutomationDefinition.ByAutomationIds("ObservedGrid", "ObservedGrid", "ObservedGrid")
            .WithColumns(GridColumnDefinition.Auto("Id"), GridColumnDefinition.Auto("Value")
                .EditWith(GridCellEditorKind.ComboBox, new GridCellEditorParts(
                    OpenButton: new GridRelativeLocator("StateOpen", GridRelativeLocatorScope.GridRoot))))
            .IdentifyRowsBy("Id");
        var options = new AppAutomationRecorderOptions { GridAutomation = new GridAutomationCatalog().Add(definition), ShowOverlay = false };
        var factory = new RecorderStepFactory(options, () => root);
        await Assert.That(factory.ShouldSuppressGridComboSelectionButton(toggle)).IsTrue();
        var unrelated = new global::Avalonia.Controls.Primitives.ToggleButton();
        AutomationProperties.SetAutomationId(unrelated, "StateOpen");
        await Assert.That(factory.ShouldSuppressGridComboSelectionButton(unrelated)).IsFalse();
        using var session = Session(root, definition);
        session.Start();
        session.CaptureButtonClickForTesting(toggle);
        await Assert.That(session.StepCount).IsEqualTo(0);
    });

    [Test]
    public async Task EveryRegisteredRowCommandReusesDistinctRuntimeTargets()
    {
        var grid = new RecordedControlDescriptor("ObservedGrid", UiControlType.Grid, "ObservedGrid", UiLocatorKind.AutomationId, false, typeof(GridHost).FullName!, null);
        const string firstKey = "recorded-row-one";
        const string secondKey = "recorded-row-two";
        RecordedStep Address(RecordedStep step, string key, string? column = null) => step with
        {
            StepId = Guid.NewGuid(),
            CanCaptureGridRowKey = true,
            GridRowConditions = [new("Id", key)],
            GridTargetColumnName = column
        };
        var product = Address(new(RecordedActionKind.CaptureCheckpoint, grid, ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.GridCellValue, CheckpointId: Guid.NewGuid(), CheckpointVariableName: "productCheckpoint"), firstKey, "MarketProductName");
        var quantity = Address(product with
        {
            CheckpointId = Guid.NewGuid(),
            CheckpointVariableName = "quantityCheckpoint",
            ValueKind = RecorderValueKind.Number,
            DoubleValue = 1000
        }, firstKey, "RequiredVolume");
        var steps = new List<RecordedStep> { product, quantity };
        foreach (var action in new[]
        {
            RecordedActionKind.SelectGridRow, RecordedActionKind.OpenGridRow, RecordedActionKind.SelectGridCellComboItem,
            RecordedActionKind.EditGridCellNumber, RecordedActionKind.EditGridCellText, RecordedActionKind.EditGridCellDate,
            RecordedActionKind.EditGridCellTime, RecordedActionKind.EditGridCellColor, RecordedActionKind.SetGridCellChecked,
            RecordedActionKind.CopyGridCell, RecordedActionKind.SearchAndSelectGridCell,
            RecordedActionKind.WaitUntilGridCellEquals, RecordedActionKind.WaitUntilGridContainsRow
        })
        {
            foreach (var key in new[] { secondKey, firstKey, secondKey, firstKey })
                steps.Add(Address(new(action, grid, StringValue: action == RecordedActionKind.EditGridCellColor ? "#FF0022" : "Ready",
                    ItemValue: "Ready", DoubleValue: 997, BoolValue: true, DateValue: new DateTime(2026, 10, 29), TimeValue: new TimeSpan(12, 0, 0)),
                    key, action == RecordedActionKind.SelectGridCellComboItem ? "ShipmentStatus" : "ShippedVolume"));
        }
        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var preview = generator.GeneratePreview(steps);
        await Assert.That(preview).DoesNotContain(firstKey);
        await Assert.That(preview).DoesNotContain(secondKey);
        await Assert.That(preview).DoesNotContain("GridRowSelector.ByCell");
        await Assert.That(preview.Split("GridRowKeyReader.CaptureFirst").Length - 1).IsEqualTo(2);
        await Assert.That(preview).Contains("GridRowKeyReader.CaptureFirst(Page.ObservedGrid, excludingRows: new[] { gridRow })");
        foreach (var step in steps)
        {
            var entryPreview = generator.GeneratePreviewForStep(step, steps);
            await Assert.That(entryPreview).DoesNotContain(firstKey);
            await Assert.That(entryPreview).DoesNotContain(secondKey);
        }
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage, RecorderScenarioDestinationSources.CompilableScenario);
        var saved = await project.AutosaveAsync(project.CreateSaveContext("All grid commands", "all-grid-commands"), steps);
        await Assert.That(RecorderGeneratedSourceCompiler.Compile(project.RootPath)).IsEmpty();
        await Assert.That(RecorderAutosaveStateSerializer.TryRead(saved.ScenarioFilePath!, out var restored, out var error)).IsTrue().Because(error);
        await Assert.That(generator.GeneratePreview(restored!.Steps)).IsEqualTo(preview);
    }

    private static Task OnUI(Func<Task> action) => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RecorderGroupedGridTests).Assembly)
        .Dispatch(async () => { await action(); return true; }, CancellationToken.None);

    private static GridHost Host()
    {
        var grid = new GridHost { Width = 500, Height = 400 };
        AutomationProperties.SetAutomationId(grid, "ObservedGrid");
        return grid;
    }
    private static GroupHeader Group(string field, object? value, int level, string display) =>
        new()
        {
            Column = new(field, field),
            GroupValue = value,
            Level = level,
            GroupValueDisplayText = display,
            Child = new TextBlock { Text = display },
            Height = 35
        };
    private static void Arrange(Control root) { root.Measure(new Size(500, 400)); root.Arrange(new Rect(0, 0, 500, 400)); }
    private static RecorderSession Session(Control root, GridAutomationDefinition? definition = null, IRecorderSemanticValueResolver? resolver = null)
    {
        var options = new AppAutomationRecorderOptions
        {
            ShowOverlay = false,
            GridAutomation = definition is null ? new() : new GridAutomationCatalog().Add(definition),
            Validation = new() { ValidateSelectors = true, ValidateRuntimeTargets = true, CaptureInvalidSteps = true }
        };
        if (resolver is not null) options.SemanticValueResolvers.Add(resolver);
        return new(RecorderTestWindow.CreateStub(), options, validationRootProvider: () => root, attachWindowHandlers: false);
    }

    private sealed class SemanticValueResolver(Func<Control, RecorderSemanticValueResolution> resolve) : IRecorderSemanticValueResolver
    {
        public RecorderSemanticValueResolution Resolve(Control source) => resolve(source);
    }

    public sealed class GridHost : StackPanel
    {
        public IReadOnlyList<Row> ItemsSource { get; set; } = [];
        public IReadOnlyList<Column> Columns { get; set; } = [new("Customer", "Контрагент"), new("Order", "Номер заказа")];
        public IReadOnlyList<Band> Bands { get; set; } = [];
    }
    public sealed record Row([property: Key] string Id, object Value);
    public sealed record Column(string FieldName, string Header);
    public sealed record Cell(Row Row, Column Column, object Value);
    public sealed class GroupHeader : Border
    {
        public string HeaderKind => "Group";
        public Column Column { get; init; } = null!;
        public object? GroupValue { get; init; }
        public int Level { get; init; }
        public string GroupValueDisplayText { get; init; } = string.Empty;
    }
    public sealed class ColumnHeader : Border { public Column Column { get; init; } = null!; }
    public sealed record Band(string BandName, string Header, IReadOnlyList<Band> Bands);
    public sealed class BandHeader : Border { public Band Band { get; init; } = null!; }
}
