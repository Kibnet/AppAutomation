using System.Text.Json;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.UI;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Layout;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

[NotInParallel]
public sealed class RecorderNumericTextCalculatorTests
{
    [Test]
    public async Task DecimalCheckpoint_UsesCapturedTextAndCalculatesQuantityTimesPrice()
    {
        var price = new TextBox { Text = "182,50 руб." };
        AutomationProperties.SetAutomationId(price, "UnitPrice");
        using var session = CreateSession(new StackPanel { Children = { price } });
        session.SetLastHoveredControlForTesting(price);
        session.CaptureCheckpoint("unitPrice");
        var checkpointId = session.Checkpoints.Single().CheckpointId;
        var editorService = (IRecorderStepEditingSessionDetails)session;
        editorService.TryCreateStepEditDraft(session.StepJournal.Single().StepId, out var draft, out _);
        var result = editorService.ApplyStepEdit(draft! with
        {
            ValueKind = RecorderValueKind.Number,
            ValueAccessorKind = RecorderValueAccessorKind.NumericText,
            NumericType = RecorderNumericType.Decimal,
            DoubleValue = 182.5
        });
        await Assert.That(result.Success).IsTrue().Because(result.Message);

        var total = new RecordedStep(
            RecordedActionKind.AssertValue,
            new RecordedControlDescriptor("DisplayedTotal", UiControlType.TextBox, "DisplayedTotal", UiLocatorKind.AutomationId, false, nameof(TextBox), null),
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            ComparisonKind: RecorderComparisonKind.Equal,
            NumericExpectedExpression: new RecorderNumericExpectedExpression(
                RecorderArithmeticOperation.Multiply,
                RecorderNumericOperand.FromControl(
                    new RecordedControlDescriptor("RequiredVolume", UiControlType.Spinner, "RequiredVolume", UiLocatorKind.AutomationId, false, "NumericUpDown", null),
                    RecorderValueAccessorKind.NumericValue),
                RecorderNumericOperand.FromCheckpoint(checkpointId)));
        var checkpoint = new RecordedStep(
            RecordedActionKind.CaptureCheckpoint,
            new RecordedControlDescriptor("UnitPrice", UiControlType.TextBox, "UnitPrice", UiLocatorKind.AutomationId, false, nameof(TextBox), null),
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            CheckpointId: checkpointId,
            CheckpointVariableName: "unitPrice")
        {
            NumericType = RecorderNumericType.Decimal
        };
        var code = new CodeGeneration.AuthoringCodeGenerator(new SourceScanning.AuthoringProjectScanner(), null)
            .GeneratePreview([checkpoint, total]);

        await Assert.That(code).Contains("var unitPriceText = Page.UnitPrice.Text;");
        await Assert.That(code).Contains("var unitPrice = NumericText.ParseDecimal(unitPriceText);");
        await Assert.That(code).Contains("NumericDisplayValueReader.ReadDecimal(Page.DisplayedTotal)");
        await Assert.That(code).Contains("NumericValue.ToDecimal(Page.RequiredVolume.Value) * unitPrice");

        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage,
            RecorderScenarioDestinationSources.CompilableScenario);
        var saved = await project.SaveAsync(project.CreateSaveContext(), [checkpoint, total]);
        await Assert.That(saved.Success).IsTrue();
        await Assert.That(RecorderGeneratedSourceCompiler.Compile(project.RootPath)).IsEmpty();
        var savedCode = File.ReadAllText(saved.ScenarioFilePath!);
        static string NormalizeWhitespace(string value) =>
            System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ").Trim();
        await Assert.That(NormalizeWhitespace(savedCode)).Contains(NormalizeWhitespace(code));

        var replay = RecorderGeneratedSourceCompiler.CompileNumericAssertion(code);
        await replay("365,00 руб.");
        Exception? mismatch = null;
        try
        {
            await replay("364,00 руб.");
        }
        catch (Exception exception)
        {
            mismatch = exception;
        }

        await Assert.That(mismatch).IsNotNull();
    }

    [Test]
    public async Task IntegerAdditionAboveDoublePrecision_UsesExactDecimalReplay()
    {
        var checkpointId = Guid.NewGuid();
        var checkpoint = new RecordedStep(
            RecordedActionKind.CaptureCheckpoint,
            new RecordedControlDescriptor("UnitPrice", UiControlType.TextBox, "UnitPrice",
                UiLocatorKind.AutomationId, false, nameof(TextBox), null),
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            CheckpointId: checkpointId,
            CheckpointVariableName: "largeInteger")
        {
            NumericType = RecorderNumericType.Integer
        };
        var assertion = new RecordedStep(
            RecordedActionKind.AssertValue,
            new RecordedControlDescriptor("DisplayedTotal", UiControlType.TextBox, "DisplayedTotal",
                UiLocatorKind.AutomationId, false, nameof(TextBox), null),
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            ComparisonKind: RecorderComparisonKind.Equal,
            NumericExpectedExpression: new RecorderNumericExpectedExpression(
                RecorderArithmeticOperation.Add,
                RecorderNumericOperand.FromCheckpoint(checkpointId),
                RecorderNumericOperand.FromLiteral(1)));
        var code = new CodeGeneration.AuthoringCodeGenerator(new SourceScanning.AuthoringProjectScanner(), null)
            .GeneratePreview([checkpoint, assertion]);

        await Assert.That(code).Contains("NumericDisplayValueReader.ReadDecimal(Page.DisplayedTotal)");
        await Assert.That(code).Contains("NumericValue.ToDecimal(largeInteger) + 1m");
        var replay = RecorderGeneratedSourceCompiler.CompileNumericAssertion(code, "9007199254740992");
        await replay("9007199254740993");
    }

    [Test]
    public async Task CheckpointEditor_OffersIntegerDecimalAndDoubleWithoutChangingOriginalText()
    {
        var input = new TextBox { Text = "182" };
        AutomationProperties.SetAutomationId(input, "UnitPrice");
        using var session = CreateSession(new StackPanel { Children = { input } });
        session.SetLastHoveredControlForTesting(input);
        session.CaptureCheckpoint("unitPrice");
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        overlay.GetLogicalDescendants().OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = overlay.LastStepEditorForTesting!;
        var choices = editor.GetLogicalDescendants().OfType<ComboBox>()
            .Single(combo => combo.Name == "RecorderStepEditCheckpointValueKind");
        var values = choices.Items.Cast<RecorderCheckpointValueType>().ToArray();
        choices.SelectedItem = RecorderCheckpointValueType.Decimal;
        editor.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Assert.That(values).IsEquivalentTo([
            RecorderCheckpointValueType.Text,
            RecorderCheckpointValueType.Integer,
            RecorderCheckpointValueType.Decimal,
            RecorderCheckpointValueType.Double]);
        await Assert.That(session.ExportPreview()).Contains("var unitPriceText = Page.UnitPrice.Text;");
        await Assert.That(session.ExportPreview()).Contains("var unitPrice = NumericText.ParseDecimal(unitPriceText);");
        await Assert.That(input.Text).IsEqualTo("182");
        ((IRecorderStepEditingSessionDetails)session).TryCreateStepEditDraft(
            session.StepJournal.Single().StepId, out var edited, out _);
        await Assert.That(edited!.NumericType).IsEqualTo(RecorderNumericType.Decimal);
    }

    [Test]
    public async Task IntegerDivision_PromotesToDecimalAndLegacyStepKeepsDoubleCode()
    {
        var checkpointId = Guid.NewGuid();
        var descriptor = new RecordedControlDescriptor(
            "Quantity", UiControlType.TextBox, "Quantity", UiLocatorKind.AutomationId, false, nameof(TextBox), null);
        var typed = new RecordedStep(
            RecordedActionKind.CaptureCheckpoint, descriptor,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            CheckpointId: checkpointId,
            CheckpointVariableName: "quantity")
        {
            NumericType = RecorderNumericType.Integer
        };
        var assertion = new RecordedStep(
            RecordedActionKind.AssertValue, descriptor,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            ComparisonKind: RecorderComparisonKind.Equal,
            NumericExpectedExpression: new RecorderNumericExpectedExpression(
                RecorderArithmeticOperation.Divide,
                RecorderNumericOperand.FromCheckpoint(checkpointId),
                RecorderNumericOperand.FromLiteral(2)));
        var generator = new CodeGeneration.AuthoringCodeGenerator(new SourceScanning.AuthoringProjectScanner(), null);
        var typedCode = generator.GeneratePreview([typed, assertion]);
        var legacyCode = generator.GeneratePreview([typed with { NumericType = null }, assertion]);

        await Assert.That(typedCode).Contains("NumericValue.ToDecimal(quantity) / 2m");
        await Assert.That(typedCode).Contains("NumericDisplayValueReader.ReadDecimal(Page.Quantity)");
        await Assert.That(legacyCode).Contains("var quantity = NumericDisplayValueReader.Read(Page.Quantity);");
        await Assert.That(legacyCode).Contains("quantity / 2");
        var restored = JsonSerializer.Deserialize<RecordedStep>(JsonSerializer.Serialize(typed));
        await Assert.That(restored?.NumericType).IsEqualTo(RecorderNumericType.Integer);
    }

    [Test]
    public async Task AdditionalIntegerDivision_PreservesFractionalResult()
    {
        var id = Guid.NewGuid();
        var divisorId = Guid.NewGuid();
        var descriptor = new RecordedControlDescriptor(
            "Quantity", UiControlType.TextBox, "Quantity", UiLocatorKind.AutomationId, false, nameof(TextBox), null);
        var checkpoint = new RecordedStep(
            RecordedActionKind.CaptureCheckpoint, descriptor,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            CheckpointId: id,
            CheckpointVariableName: "quantity")
        {
            NumericType = RecorderNumericType.Integer
        };
        var divisor = checkpoint with
        {
            StepId = Guid.NewGuid(),
            CheckpointId = divisorId,
            CheckpointVariableName = "divisor"
        };
        var assertion = new RecordedStep(
            RecordedActionKind.AssertValue, descriptor,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            ComparisonKind: RecorderComparisonKind.Equal,
            NumericExpectedExpression: new RecorderNumericExpectedExpression(
                RecorderArithmeticOperation.Add,
                RecorderNumericOperand.FromCheckpoint(id),
                RecorderNumericOperand.FromLiteral(2),
                [new RecorderNumericOperationStep(
                    RecorderArithmeticOperation.Divide,
                    RecorderNumericOperand.FromCheckpoint(divisorId))]));

        var steps = new[] { checkpoint, divisor, assertion };
        var graph = RecorderScenarioGraphValidator.Validate(steps);
        var code = new CodeGeneration.AuthoringCodeGenerator(new SourceScanning.AuthoringProjectScanner(), null)
            .GeneratePreview(steps);

        using (Assert.Multiple())
        {
            await Assert.That(graph.Success).IsTrue().Because(graph.Error ?? "Graph validation failed.");
            await Assert.That(code).Contains("(NumericValue.ToDecimal(quantity) + 2m) / NumericValue.ToDecimal(divisor)");
            await Assert.That(code).Contains("NumericDisplayValueReader.ReadDecimal(Page.Quantity)");
        }
    }

    [Test]
    public async Task DecimalCalculation_RejectsNonzeroLiteralThatWouldBecomeZero()
    {
        var id = Guid.NewGuid();
        var descriptor = new RecordedControlDescriptor(
            "Price", UiControlType.TextBox, "Price", UiLocatorKind.AutomationId, false, nameof(TextBox), null);
        var checkpoint = new RecordedStep(
            RecordedActionKind.CaptureCheckpoint, descriptor,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            CheckpointId: id,
            CheckpointVariableName: "price")
        {
            NumericType = RecorderNumericType.Decimal
        };
        var assertion = new RecordedStep(
            RecordedActionKind.AssertValue, descriptor,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            ComparisonKind: RecorderComparisonKind.Equal,
            NumericExpectedExpression: new RecorderNumericExpectedExpression(
                RecorderArithmeticOperation.Add,
                RecorderNumericOperand.FromCheckpoint(id),
                RecorderNumericOperand.FromLiteral(1e-29)));

        var graph = RecorderScenarioGraphValidator.Validate([checkpoint, assertion]);
        await Assert.That(graph.Success).IsFalse();
        await Assert.That(graph.Error).Contains("round to zero");
    }

    [Test]
    public async Task DecimalCalculation_RejectsLiteralOutsideDecimalRangeBeforeGeneratingCode()
    {
        var id = Guid.NewGuid();
        var control = new RecordedControlDescriptor(
            "UnitPrice", UiControlType.TextBox, "UnitPrice", UiLocatorKind.AutomationId, false, nameof(TextBox), null);
        var checkpoint = new RecordedStep(
            RecordedActionKind.CaptureCheckpoint, control,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            CheckpointId: id,
            CheckpointVariableName: "price")
        {
            NumericType = RecorderNumericType.Decimal
        };
        var assertion = new RecordedStep(
            RecordedActionKind.AssertValue, control,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            ComparisonKind: RecorderComparisonKind.Equal,
            NumericExpectedExpression: new RecorderNumericExpectedExpression(
                RecorderArithmeticOperation.Multiply,
                RecorderNumericOperand.FromCheckpoint(id),
                RecorderNumericOperand.FromLiteral(double.MaxValue)));

        var graph = RecorderScenarioGraphValidator.Validate([checkpoint, assertion]);
        await Assert.That(graph.Success).IsFalse();
        await Assert.That(graph.Error).Contains("outside the decimal range");
    }

    [Test]
    public async Task DecimalCalculation_ReadsDirectTextOperandWithoutDoubleConversion()
    {
        var id = Guid.NewGuid();
        var amount = new RecordedControlDescriptor(
            "Amount", UiControlType.TextBox, "Amount", UiLocatorKind.AutomationId, false, nameof(TextBox), null);
        var price = new RecordedControlDescriptor(
            "UnitPrice", UiControlType.TextBox, "UnitPrice", UiLocatorKind.AutomationId, false, nameof(TextBox), null);
        var checkpoint = new RecordedStep(
            RecordedActionKind.CaptureCheckpoint, amount,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            CheckpointId: id,
            CheckpointVariableName: "amount")
        {
            NumericType = RecorderNumericType.Decimal
        };
        var assertion = new RecordedStep(
            RecordedActionKind.AssertValue, amount,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            ComparisonKind: RecorderComparisonKind.Equal,
            NumericExpectedExpression: new RecorderNumericExpectedExpression(
                RecorderArithmeticOperation.Multiply,
                RecorderNumericOperand.FromCheckpoint(id),
                RecorderNumericOperand.FromControl(price, RecorderValueAccessorKind.NumericText)));

        var code = new CodeGeneration.AuthoringCodeGenerator(new SourceScanning.AuthoringProjectScanner(), null)
            .GeneratePreview([checkpoint, assertion]);
        await Assert.That(code).Contains("amount * NumericDisplayValueReader.ReadDecimal(Page.UnitPrice)");
        await Assert.That(code).DoesNotContain("NumericValue.ToDecimal(NumericDisplayValueReader.Read(Page.UnitPrice))");
    }

    [Test]
    public async Task TypedCheckpoint_DoesNotReuseAnotherCheckpointVariableForCapturedText()
    {
        var source = new RecordedControlDescriptor(
            "UnitPrice", UiControlType.TextBox, "UnitPrice", UiLocatorKind.AutomationId, false, nameof(TextBox), null);
        var existing = new RecordedStep(
            RecordedActionKind.CaptureCheckpoint, source,
            ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text,
            CheckpointId: Guid.NewGuid(),
            CheckpointVariableName: "priceText");
        var numeric = new RecordedStep(
            RecordedActionKind.CaptureCheckpoint, source,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            CheckpointId: Guid.NewGuid(),
            CheckpointVariableName: "price")
        {
            NumericType = RecorderNumericType.Decimal
        };

        var code = new CodeGeneration.AuthoringCodeGenerator(new SourceScanning.AuthoringProjectScanner(), null)
            .GeneratePreview([existing, numeric]);
        await Assert.That(code).Contains("var priceText = Page.UnitPrice.Text;");
        await Assert.That(code).Contains("var priceText2 = Page.UnitPrice.Text;");
        await Assert.That(code).Contains("var price = NumericText.ParseDecimal(priceText2);");
    }

    [Test]
    public async Task FormattedReadOnlyAmount_OffersCalculatorAndRecordsCurrentNumericValue()
    {
        var amount = new TextBox { Text = "1\u00a0200 руб.", IsReadOnly = true };
        AutomationProperties.SetAutomationId(amount, "DisplayedTotal");
        var root = new StackPanel { Children = { amount } };
        using var session = CreateSession(root);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, args) => selection = args.Selection;
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(amount);

        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        var calculator = overlay.CreateCheckMenuForTesting(selection!).Items
            .OfType<MenuItem>()
            .Single(item => string.Equals(item.Header?.ToString(), "Compare with calculated value…", StringComparison.Ordinal));
        ((IRecorderCheckpointSessionDetails)session).CaptureCalculatedAssertion(
            selection!,
            new RecorderNumericExpectedExpression(
                RecorderArithmeticOperation.Add,
                RecorderNumericOperand.FromLiteral(1000),
                RecorderNumericOperand.FromLiteral(200)));
        var editing = (IRecorderStepEditingSessionDetails)session;
        var editable = editing.TryCreateStepEditDraft(
            session.StepJournal.Single().StepId,
            out var draft,
            out var editError);
        var applied = editable && editing.ApplyStepEdit(draft!).Success;

        using (Assert.Multiple())
        {
            await Assert.That(calculator.IsEnabled).IsTrue();
            await Assert.That(session.StepCount).IsEqualTo(1).Because(session.LatestStatus);
            await Assert.That(session.StepJournal.Single().CanPersist).IsTrue();
            await Assert.That(editable).IsTrue().Because(editError ?? "Step editor did not open.");
            await Assert.That(applied).IsTrue();
            await Assert.That(session.ExportPreview()).Contains("NumericDisplayValueReader.Read(Page.DisplayedTotal)");
            await Assert.That(session.ExportPreview()).Contains("1000");
            await Assert.That(session.ExportPreview()).Contains("200");
        }
    }

    [Test]
    public async Task CalculatedCheck_CanAddAnotherNumericVariable()
    {
        var total = new TextBox { Text = "500 руб.", IsReadOnly = true };
        AutomationProperties.SetAutomationId(total, "DisplayedTotal");
        using var session = CreateSession(new StackPanel { Children = { total } });
        var quantityId = Guid.NewGuid();
        var priceId = Guid.NewGuid();
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.CaptureCheckpoint,
            new RecordedControlDescriptor("RequiredVolume", UiControlType.Spinner, "RequiredVolume", UiLocatorKind.AutomationId, false, "NumericUpDown", null),
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericValue,
            CheckpointId: quantityId,
            CheckpointVariableName: "quantity"));
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.CaptureCheckpoint,
            new RecordedControlDescriptor("UnitPrice", UiControlType.TextBox, "UnitPrice", UiLocatorKind.AutomationId, false, nameof(TextBox), null),
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            CheckpointId: priceId,
            CheckpointVariableName: "price")
        {
            NumericType = RecorderNumericType.Decimal
        });
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, args) => selection = args.Selection;
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(total);

        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        var editor = overlay.CreateCalculatedAssertionEditorForTesting(selection!);
        var addTerm = editor.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == "RecorderCalculatedAddTerm");
        addTerm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        addTerm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var firstVariable = editor.GetLogicalDescendants().OfType<ComboBox>()
            .Single(combo => combo.Name == "RecorderCalculatedAdditionalTerm1Checkpoint");
        var secondVariable = editor.GetLogicalDescendants().OfType<ComboBox>()
            .Single(combo => combo.Name == "RecorderCalculatedAdditionalTerm2Checkpoint");
        var firstOperation = editor.GetLogicalDescendants().OfType<ComboBox>()
            .Single(combo => combo.Name == "RecorderCalculatedAdditionalTerm1Operation");
        var secondOperation = editor.GetLogicalDescendants().OfType<ComboBox>()
            .Single(combo => combo.Name == "RecorderCalculatedAdditionalTerm2Operation");
        var save = editor.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == "RecorderCalculatedAdd");
        var emptyTermDisablesSave = !save.IsEnabled;
        firstVariable.SelectedIndex = 0;
        secondVariable.SelectedIndex = 1;
        firstOperation.SelectedIndex = (int)RecorderArithmeticOperation.Subtract;
        secondOperation.SelectedIndex = (int)RecorderArithmeticOperation.Multiply;
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        using (Assert.Multiple())
        {
            await Assert.That(emptyTermDisablesSave).IsTrue();
            await Assert.That(session.StepCount).IsEqualTo(3);
            await Assert.That(session.ExportPreview()).Contains("((0m + 0m) - NumericValue.ToDecimal(quantity)) * price");
        }
    }

    [Test]
    public async Task CalculatedCheck_AddsMultipleNumericVariablesWithDecimalPrice()
    {
        var quantity1 = Guid.NewGuid();
        var price1 = Guid.NewGuid();
        var quantity2 = Guid.NewGuid();
        var price2 = Guid.NewGuid();
        var quantityControl = new RecordedControlDescriptor(
            "RequiredVolume", UiControlType.Spinner, "RequiredVolume", UiLocatorKind.AutomationId, false, "NumericUpDown", null);
        var priceControl = new RecordedControlDescriptor(
            "UnitPrice", UiControlType.TextBox, "UnitPrice", UiLocatorKind.AutomationId, false, nameof(TextBox), null);
        var totalControl = new RecordedControlDescriptor(
            "DisplayedTotal", UiControlType.TextBox, "DisplayedTotal", UiLocatorKind.AutomationId, false, nameof(TextBox), null);
        var checkpoints = new[]
        {
            new RecordedStep(RecordedActionKind.CaptureCheckpoint, quantityControl,
                ValueKind: RecorderValueKind.Number, ValueAccessorKind: RecorderValueAccessorKind.NumericValue,
                CheckpointId: quantity1, CheckpointVariableName: "quantity1"),
            new RecordedStep(RecordedActionKind.CaptureCheckpoint, priceControl,
                ValueKind: RecorderValueKind.Number, ValueAccessorKind: RecorderValueAccessorKind.NumericText,
                CheckpointId: price1, CheckpointVariableName: "price1") { NumericType = RecorderNumericType.Decimal },
            new RecordedStep(RecordedActionKind.CaptureCheckpoint, quantityControl,
                ValueKind: RecorderValueKind.Number, ValueAccessorKind: RecorderValueAccessorKind.NumericValue,
                CheckpointId: quantity2, CheckpointVariableName: "quantity2"),
            new RecordedStep(RecordedActionKind.CaptureCheckpoint, priceControl,
                ValueKind: RecorderValueKind.Number, ValueAccessorKind: RecorderValueAccessorKind.NumericText,
                CheckpointId: price2, CheckpointVariableName: "price2") { NumericType = RecorderNumericType.Decimal }
        };
        var expression = new RecorderNumericExpectedExpression(
            RecorderArithmeticOperation.Multiply,
            RecorderNumericOperand.FromCheckpoint(quantity1),
            RecorderNumericOperand.FromCheckpoint(price1),
            [
                new RecorderNumericOperationStep(
                    RecorderArithmeticOperation.Add,
                    RecorderNumericOperand.FromCheckpoint(quantity2)),
                new RecorderNumericOperationStep(
                    RecorderArithmeticOperation.Divide,
                    RecorderNumericOperand.FromCheckpoint(price2))
            ]);
        var assertion = new RecordedStep(
            RecordedActionKind.AssertValue, totalControl,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            ComparisonKind: RecorderComparisonKind.Equal,
            NumericExpectedExpression: expression);
        var steps = checkpoints.Append(assertion).ToArray();
        var graph = RecorderScenarioGraphValidator.Validate(steps);
        var code = new CodeGeneration.AuthoringCodeGenerator(new SourceScanning.AuthoringProjectScanner(), null)
            .GeneratePreview(steps);
        var restored = JsonSerializer.Deserialize<RecorderNumericExpectedExpression>(
            JsonSerializer.Serialize(expression));
        var invalidOperation = assertion with
        {
            NumericExpectedExpression = expression with
            {
                AdditionalOperations =
                [
                    new RecorderNumericOperationStep(
                        (RecorderArithmeticOperation)99,
                        RecorderNumericOperand.FromCheckpoint(quantity2))
                ]
            }
        };

        using (Assert.Multiple())
        {
            await Assert.That(graph.Success).IsTrue().Because(graph.Error ?? "Graph validation failed.");
            await Assert.That(code).Contains("((NumericValue.ToDecimal(quantity1) * price1) + NumericValue.ToDecimal(quantity2)) / price2");
            await Assert.That(code).Contains("NumericDisplayValueReader.ReadDecimal(Page.DisplayedTotal)");
            await Assert.That(restored?.AdditionalOperations?.Count).IsEqualTo(2);
            await Assert.That(restored?.AdditionalOperations?[1].Operation).IsEqualTo(RecorderArithmeticOperation.Divide);
            await Assert.That(RecorderScenarioGraphValidator.Validate(steps.Where(step => step.CheckpointId != quantity2).ToArray()).Success).IsFalse();
            await Assert.That(RecorderScenarioGraphValidator.Validate(checkpoints.Append(invalidOperation).ToArray()).Success).IsFalse();
        }

        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage,
            RecorderScenarioDestinationSources.CompilableScenario);
        var saved = await project.SaveAsync(project.CreateSaveContext(), steps);
        await Assert.That(saved.Success).IsTrue();
        await Assert.That(RecorderGeneratedSourceCompiler.Compile(project.RootPath)).IsEmpty();
    }

    [Test]
    public async Task CalculatedStepEditor_AddsRemovesAndRestoresAdditionalTerm()
    {
        var quantityId = Guid.NewGuid();
        var priceId = Guid.NewGuid();
        var quantity = new RecordedControlDescriptor(
            "RequiredVolume", UiControlType.Spinner, "RequiredVolume", UiLocatorKind.AutomationId, false, "NumericUpDown", null);
        var price = new RecordedControlDescriptor(
            "UnitPrice", UiControlType.TextBox, "UnitPrice", UiLocatorKind.AutomationId, false, nameof(TextBox), null);
        var total = new RecordedControlDescriptor(
            "DisplayedTotal", UiControlType.TextBox, "DisplayedTotal", UiLocatorKind.AutomationId, false, nameof(TextBox), null);
        using var session = CreateSession(new StackPanel());
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.CaptureCheckpoint, quantity,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericValue,
            CheckpointId: quantityId,
            CheckpointVariableName: "quantity"));
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.CaptureCheckpoint, price,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            CheckpointId: priceId,
            CheckpointVariableName: "price")
        {
            NumericType = RecorderNumericType.Decimal
        });
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.AssertValue, total,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericText,
            ComparisonKind: RecorderComparisonKind.Equal,
            NumericExpectedExpression: new RecorderNumericExpectedExpression(
                RecorderArithmeticOperation.Multiply,
                RecorderNumericOperand.FromCheckpoint(quantityId),
                RecorderNumericOperand.FromCheckpoint(priceId))));

        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        session.Start();
        OpenLastStepEditor();
        var editor = overlay.LastStepEditorForTesting!;
        Click(editor, "RecorderStepEditCalculatedAddTerm");
        var variable = editor.GetLogicalDescendants().OfType<ComboBox>()
            .Single(combo => combo.Name == "RecorderStepEditAdditionalTerm1Checkpoint");
        var operation = editor.GetLogicalDescendants().OfType<ComboBox>()
            .Single(combo => combo.Name == "RecorderStepEditAdditionalTerm1Operation");
        variable.SelectedIndex = 0;
        operation.SelectedItem = RecorderArithmeticOperation.Subtract;
        Click(editor, "RecorderStepEditApply");

        var editedCode = session.ExportPreview();
        OpenLastStepEditor();
        editor = overlay.LastStepEditorForTesting!;
        var restoredTerms = editor.GetLogicalDescendants().OfType<ComboBox>()
            .Count(combo => combo.Name == "RecorderStepEditAdditionalTerm1Checkpoint" && combo.SelectedIndex == 0);
        var restoredOperation = editor.GetLogicalDescendants().OfType<ComboBox>()
            .Single(combo => combo.Name == "RecorderStepEditAdditionalTerm1Operation").SelectedItem;
        Click(editor, "RecorderStepEditAdditionalTerm1Remove");
        Click(editor, "RecorderStepEditCancel");
        var afterCancel = session.ExportPreview();
        OpenLastStepEditor();
        editor = overlay.LastStepEditorForTesting!;
        Click(editor, "RecorderStepEditAdditionalTerm1Remove");
        Click(editor, "RecorderStepEditApply");

        using (Assert.Multiple())
        {
            await Assert.That(editedCode).Contains("- NumericValue.ToDecimal(quantity)");
            await Assert.That(restoredTerms).IsEqualTo(1);
            await Assert.That(restoredOperation).IsEqualTo(RecorderArithmeticOperation.Subtract);
            await Assert.That(afterCancel).IsEqualTo(editedCode);
            await Assert.That(session.ExportPreview()).DoesNotContain("- NumericValue.ToDecimal(quantity)");
        }

        void OpenLastStepEditor() => overlay.GetLogicalDescendants().OfType<Button>()
            .Last(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        static void Click(Control root, string name) => root.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == name)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [Test]
    public async Task DecimalCheckpointInAdditionalTerm_PromotesTheEntireExpression()
    {
        var quantityId = Guid.NewGuid();
        var priceId = Guid.NewGuid();
        var quantity = new RecordedControlDescriptor(
            "RequiredVolume", UiControlType.Spinner, "RequiredVolume", UiLocatorKind.AutomationId, false, "NumericUpDown", null);
        var price = new RecordedControlDescriptor(
            "UnitPrice", UiControlType.TextBox, "UnitPrice", UiLocatorKind.AutomationId, false, nameof(TextBox), null);
        var total = new RecordedControlDescriptor(
            "DisplayedTotal", UiControlType.TextBox, "DisplayedTotal", UiLocatorKind.AutomationId, false, nameof(TextBox), null);
        var steps = new[]
        {
            new RecordedStep(RecordedActionKind.CaptureCheckpoint, quantity,
                ValueKind: RecorderValueKind.Number, ValueAccessorKind: RecorderValueAccessorKind.NumericValue,
                CheckpointId: quantityId, CheckpointVariableName: "quantity"),
            new RecordedStep(RecordedActionKind.CaptureCheckpoint, price,
                ValueKind: RecorderValueKind.Number, ValueAccessorKind: RecorderValueAccessorKind.NumericText,
                CheckpointId: priceId, CheckpointVariableName: "price") { NumericType = RecorderNumericType.Decimal },
            new RecordedStep(RecordedActionKind.AssertValue, total,
                ValueKind: RecorderValueKind.Number, ValueAccessorKind: RecorderValueAccessorKind.NumericText,
                ComparisonKind: RecorderComparisonKind.Equal,
                NumericExpectedExpression: new RecorderNumericExpectedExpression(
                    RecorderArithmeticOperation.Add,
                    RecorderNumericOperand.FromCheckpoint(quantityId),
                    RecorderNumericOperand.FromLiteral(1),
                    [new RecorderNumericOperationStep(
                        RecorderArithmeticOperation.Add,
                        RecorderNumericOperand.FromCheckpoint(priceId))]))
        };

        var code = new CodeGeneration.AuthoringCodeGenerator(new SourceScanning.AuthoringProjectScanner(), null)
            .GeneratePreview(steps);

        await Assert.That(code).Contains("(NumericValue.ToDecimal(quantity) + 1m) + price");
        await Assert.That(code).Contains("NumericDisplayValueReader.ReadDecimal(Page.DisplayedTotal)");
    }

    [Test]
    public async Task IdentifierText_KeepsTextChecksAndDoesNotOfferCalculator()
    {
        var identifier = new TextBox { Text = "RU260408-5" };
        AutomationProperties.SetAutomationId(identifier, "OrderNumber");
        using var session = CreateSession(new StackPanel { Children = { identifier } });
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, args) => selection = args.Selection;
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(identifier);

        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        var calculator = overlay.CreateCheckMenuForTesting(selection!).Items
            .OfType<MenuItem>()
            .Single(item => string.Equals(item.Header?.ToString(), "Compare with calculated value…", StringComparison.Ordinal));
        ((IRecorderCheckpointSessionDetails)session).CaptureCheckpoint(selection!, "orderNumber");

        await Assert.That(calculator.IsEnabled).IsFalse();
        await Assert.That(session.ExportPreview()).Contains("Page.OrderNumber.Text");
    }

    [Test]
    public async Task FormattedText_CanAlsoBeSelectedAsCalculatedControlOperand()
    {
        var amount = new TextBox { Text = "500 руб." };
        AutomationProperties.SetAutomationId(amount, "DisplayedTotal");
        using var session = CreateSession(new StackPanel { Children = { amount } });
        session.Start();
        var details = (IRecorderCheckpointSessionDetails)session;
        RecorderNumericOperandTargetSelection? selection = null;
        details.NumericOperandTargetSelected += (_, args) => selection = args.Selection;

        details.BeginNumericOperandTargetSelection();
        session.SelectNumericOperandTargetForTesting(amount);

        await Assert.That(selection?.Operand?.ValueAccessorKind)
            .IsEqualTo(RecorderValueAccessorKind.NumericText);
        await Assert.That(selection?.Error).IsNullOrEmpty();
    }

    [Test]
    public async Task EditingTextCheckpointAsNumber_UsesCurrentUiValueInCalculation()
    {
        var price = new TextBox { Text = "182" };
        var total = new TextBox { Text = "364 руб.", IsReadOnly = true };
        AutomationProperties.SetAutomationId(price, "InitialPrice");
        AutomationProperties.SetAutomationId(total, "DisplayedTotal");
        using var session = CreateSession(new StackPanel { Children = { price, total } });
        session.Start();
        session.SetLastHoveredControlForTesting(price);
        session.CaptureCheckpoint("initialPrice");
        var checkpointId = session.Checkpoints.Single().CheckpointId;
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, args) => selection = args.Selection;
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(total);
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        overlay.GetLogicalDescendants().OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = overlay.LastStepEditorForTesting!;
        editor.GetLogicalDescendants().OfType<ComboBox>()
            .Single(combo => combo.Name == "RecorderStepEditCheckpointValueKind")
            .SelectedItem = RecorderCheckpointValueType.Double;
        editor.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        ((IRecorderCheckpointSessionDetails)session).CaptureCalculatedAssertion(
            selection!,
            new RecorderNumericExpectedExpression(
                RecorderArithmeticOperation.Multiply,
                RecorderNumericOperand.FromCheckpoint(checkpointId),
                RecorderNumericOperand.FromLiteral(2)));
        ((IRecorderStepEditingSessionDetails)session).TryCreateStepEditDraft(
            session.StepJournal[0].StepId, out var editedCheckpoint, out _);

        using (Assert.Multiple())
        {
            await Assert.That(editedCheckpoint!.ValueKind).IsEqualTo(RecorderValueKind.Number);
            await Assert.That(editedCheckpoint.ValueAccessorKind)
                .IsEqualTo(RecorderValueAccessorKind.NumericText);
            await Assert.That(session.PersistableStepCount).IsEqualTo(2).Because(session.LatestStatus);
            await Assert.That(session.ExportPreview()).Contains("var initialPriceText = Page.InitialPrice.Text;");
            await Assert.That(session.ExportPreview()).Contains("var initialPrice = NumericText.ParseDouble(initialPriceText);");
            await Assert.That(session.ExportPreview()).Contains(
                "NumericDisplayValueReader.Read(Page.DisplayedTotal)).IsEqualTo(initialPrice * 2)");
            await Assert.That(session.ExportPreview()).DoesNotContain("global::AppAutomation.Abstractions.NumericDisplayValueReader");
        }
    }

    [Test]
    public async Task EditingNonNumericTextCheckpointAsNumber_IsRejectedWithoutChangingStep()
    {
        var input = new TextBox { Text = "RU260408-5" };
        AutomationProperties.SetAutomationId(input, "OrderNumber");
        using var session = CreateSession(new StackPanel { Children = { input } });
        session.SetLastHoveredControlForTesting(input);
        session.CaptureCheckpoint("orderNumber");
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        overlay.GetLogicalDescendants().OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = overlay.LastStepEditorForTesting!;
        editor.GetLogicalDescendants().OfType<ComboBox>()
            .Single(combo => combo.Name == "RecorderStepEditCheckpointValueKind")
            .SelectedItem = RecorderCheckpointValueType.Double;
        ((IRecorderStepEditingSessionDetails)session).TryCreateStepEditDraft(
            session.StepJournal.Single().StepId, out var unchangedCheckpoint, out _);

        await Assert.That(editor.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply").IsEnabled).IsFalse();
        await Assert.That(unchangedCheckpoint!.ValueKind).IsEqualTo(RecorderValueKind.Text);
        await Assert.That(session.ExportPreview()).Contains("Page.OrderNumber.Text");
    }

    [Test]
    public async Task EditingCheckpointPreview_PreservesTheEnteredTextAfterApply()
    {
        var input = new TextBox { Text = "1485 за шт" };
        AutomationProperties.SetAutomationId(input, "CurrentPrice");
        using var session = CreateSession(new StackPanel { Children = { input } });
        session.SetLastHoveredControlForTesting(input);
        session.CaptureCheckpoint("currentPrice");
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        OpenEditor();
        var editor = overlay.LastStepEditorForTesting!;
        editor.GetLogicalDescendants().OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditStringValue").Text = "1485";
        editor.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        ((IRecorderStepEditingSessionDetails)session).TryCreateStepEditDraft(
            session.StepJournal.Single().StepId, out var edited, out _);
        OpenEditor();
        var reopenedEditor = overlay.LastStepEditorForTesting!;
        var reopenedValue = reopenedEditor.GetLogicalDescendants().OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditStringValue").Text;
        var reopenedPreview = reopenedEditor.GetLogicalDescendants().OfType<TextBlock>()
            .Single(control => control.Name == "RecorderStepEditCurrentPreview").Text;
        using (Assert.Multiple())
        {
            await Assert.That(edited!.CurrentPreview).IsEqualTo("1485");
            await Assert.That(edited.StringValue).IsEqualTo("1485");
            await Assert.That(reopenedValue).IsEqualTo("1485");
            await Assert.That(reopenedPreview).IsEqualTo("Current preview: 1485");
            await Assert.That(session.ExportPreview()).Contains("Page.CurrentPrice.Text");
        }

        void OpenEditor() => overlay.GetLogicalDescendants().OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [Test]
    public async Task EditedCheckpointPreview_CanBecomeNumericWhileReplayReadsCurrentUiText()
    {
        var input = new TextBox { Text = "1485 за шт" };
        AutomationProperties.SetAutomationId(input, "CurrentPrice");
        using var session = CreateSession(new StackPanel { Children = { input } });
        session.SetLastHoveredControlForTesting(input);
        session.CaptureCheckpoint("currentPrice");
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        overlay.GetLogicalDescendants().OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = overlay.LastStepEditorForTesting!;
        editor.GetLogicalDescendants().OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditStringValue").Text = "1485";
        editor.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        overlay.GetLogicalDescendants().OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        editor = overlay.LastStepEditorForTesting!;
        editor.GetLogicalDescendants().OfType<ComboBox>()
            .Single(control => control.Name == "RecorderStepEditCheckpointValueKind")
            .SelectedItem = RecorderCheckpointValueType.Decimal;
        editor.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        ((IRecorderStepEditingSessionDetails)session).TryCreateStepEditDraft(
            session.StepJournal.Single().StepId, out var edited, out _);
        using (Assert.Multiple())
        {
            await Assert.That(edited!.ValueKind).IsEqualTo(RecorderValueKind.Number);
            await Assert.That(edited.NumericType).IsEqualTo(RecorderNumericType.Decimal);
            await Assert.That(edited.CurrentPreview).IsEqualTo("1485");
            await Assert.That(session.ExportPreview()).Contains("NumericText.ParseDecimal(currentPriceText)");
            await Assert.That(session.ExportPreview()).DoesNotContain("1485");
            await Assert.That(input.Text).IsEqualTo("1485 за шт");
            await Assert.That(NumericText.ParseDecimal(input.Text)).IsEqualTo(1485m);
        }
    }

    [Test]
    public async Task StepEditor_AlignsApplyAndCancelOnLeftWithoutChangingTheirActions()
    {
        var input = new TextBox { Text = "before" };
        AutomationProperties.SetAutomationId(input, "EditableText");
        using var session = CreateSession(new StackPanel { Children = { input } });
        session.SetLastHoveredControlForTesting(input);
        session.CaptureCheckpoint("savedText");

        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        overlay.GetLogicalDescendants().OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = overlay.LastStepEditorForTesting!;
        var apply = editor.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply");
        var cancel = editor.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditCancel");
        var buttons = (StackPanel)apply.GetLogicalParent()!;

        using (Assert.Multiple())
        {
            await Assert.That(buttons.HorizontalAlignment).IsEqualTo(HorizontalAlignment.Left);
            await Assert.That(buttons.Children.SequenceEqual([apply, cancel])).IsTrue();
        }

        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Assert.That(session.Checkpoints.Single().VariableName).IsEqualTo("savedText");
    }

    private static RecorderSession CreateSession(Control root) => new(
        RecorderTestWindow.CreateStub(),
        new AppAutomationRecorderOptions
        {
            ShowOverlay = false,
            Validation = new RecorderValidationOptions
            {
                ValidateSelectors = false,
                ValidateRuntimeTargets = false,
                CaptureInvalidSteps = true
            }
        },
        validationRootProvider: () => root,
        attachWindowHandlers: false);
}
