using System.Globalization;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using AppAutomation.Recorder.Avalonia.UI;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderMultiItemSpinnerCaptureTests
{
    [Test]
    [Arguments(1, "unit-b")]
    [Arguments(2, "unit-c")]
    public async Task CheckMode_SubsequentSpinner_UsesStableKeyForValueAndEnabledAssertions(
        int itemIndex,
        string itemKey)
    {
        using var fixture = MultiItemFixture.Create("unit-a", "unit-b", "unit-c");
        var input = fixture.InputAt(itemIndex);
        input.Text = "42";
        fixture.Session.Start();
        RecorderCheckTargetSelection? selection = null;
        fixture.Session.CheckTargetSelected += (_, args) => selection = args.Selection;
        fixture.Session.BeginCheckTargetSelection();
        fixture.Session.SelectCheckTargetForTesting(input);

        var overlay = new RecorderOverlay();
        overlay.Attach(fixture.Session, new AppAutomationRecorderOptions());
        var menu = overlay.CreateCheckMenuForTesting(selection!);
        var remember = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Remember value…"));
        var assertExpected = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Assert expected value…"));
        var details = (IRecorderCheckpointSessionDetails)fixture.Session;
        details.CaptureCheckpoint(selection!, "secondQuantity");
        details.CaptureCurrentValueAssertion(selection!);
        details.CaptureEnabledAssertion(selection!, expectedEnabled: true);
        var preview = fixture.Session.ExportPreview();

        using (Assert.Multiple())
        {
            await Assert.That(selection!.ValueDescription?.ValueKind).IsEqualTo(RecorderValueKind.Number);
            await Assert.That(remember.IsEnabled).IsTrue();
            await Assert.That(assertExpected.IsEnabled).IsTrue();
            await Assert.That(fixture.Session.PersistableStepCount).IsEqualTo(3).Because(fixture.Session.LatestStatus);
            await Assert.That(preview).Contains($"var unitQuantityEditorsItemKey = \"{itemKey}\";");
            await Assert.That(preview).Contains("Page.UnitQuantityEditors.ResolveSpinner(unitQuantityEditorsItemKey, 5000).Value");
            await Assert.That(preview).Contains("IsEqualTo(42)");
            await Assert.That(preview).Contains("Page.UnitQuantityEditors.ResolveSpinner(unitQuantityEditorsItemKey, 5000).IsEnabled");
            await Assert.That(preview).DoesNotContain("QuantityEditor_Input");
        }
    }

    [Test]
    public async Task CheckMode_SpinnerRoot_ReadsConfiguredInputWithoutUsingRepeatedRootId()
    {
        using var fixture = MultiItemFixture.Create("unit-a", "unit-b", "unit-c");
        var input = fixture.InputAt(1);
        input.Text = "42";
        var spinnerRoot = input.GetLogicalParent() as Control
            ?? throw new InvalidOperationException("Spinner root was not attached to the repeated item.");
        fixture.Session.Start();
        RecorderCheckTargetSelection? selection = null;
        fixture.Session.CheckTargetSelected += (_, args) => selection = args.Selection;
        fixture.Session.BeginCheckTargetSelection();
        fixture.Session.SelectCheckTargetForTesting(spinnerRoot);

        using (Assert.Multiple())
        {
            await Assert.That(selection!.ValueDescription?.CurrentValueText).IsEqualTo("42");
            await Assert.That(selection.ValueSnapshot?.Prototype.RepeatedItemKey).IsEqualTo("unit-b");
            await Assert.That(selection.ValueDescriptionError).IsEmpty();
        }
    }

    [Test]
    public async Task CheckMode_ReusesReadableItemKeyVariableAcrossCheckpointAndAssertion()
    {
        using var fixture = MultiItemFixture.CreateByName("уп", "шт", "кг");
        var input = fixture.InputAt(1);
        input.Text = "42";
        fixture.Session.Start();
        RecorderCheckTargetSelection? selection = null;
        fixture.Session.CheckTargetSelected += (_, args) => selection = args.Selection;
        fixture.Session.BeginCheckTargetSelection();
        fixture.Session.SelectCheckTargetForTesting(input);

        var details = (IRecorderCheckpointSessionDetails)fixture.Session;
        details.CaptureCheckpoint(selection!, "quantityCheckpoint");
        details.CaptureCurrentValueAssertion(selection!);
        var preview = fixture.Session.ExportPreview();

        using (Assert.Multiple())
        {
            await Assert.That(preview).Contains("var unitQuantityEditorsItemKey = \"шт\";");
            await Assert.That(selection!.ValueSnapshot?.Prototype.RepeatedItemKey).IsEqualTo("шт");
            await Assert.That(preview).Contains("ResolveSpinner(unitQuantityEditorsItemKey, 5000)");
            await Assert.That(preview).DoesNotContain("ResolveSpinner(\"шт\"");
            await Assert.That(preview.Split("var unitQuantityEditorsItemKey =", StringSplitOptions.None).Length)
                .IsEqualTo(2);
        }
    }

    [Test]
    public async Task CheckMode_EqualAssertionsForDifferentItemsAreBothRecorded()
    {
        using var fixture = MultiItemFixture.CreateByName("шт", "кг");
        fixture.InputAt(0).Text = "42";
        fixture.InputAt(1).Text = "42";
        fixture.Session.Start();
        var details = (IRecorderCheckpointSessionDetails)fixture.Session;
        RecorderCheckTargetSelection? selection = null;
        fixture.Session.CheckTargetSelected += (_, args) => selection = args.Selection;

        foreach (var index in new[] { 0, 1 })
        {
            fixture.Session.BeginCheckTargetSelection();
            fixture.Session.SelectCheckTargetForTesting(fixture.InputAt(index));
            details.CaptureCurrentValueAssertion(selection!);
        }

        var previews = fixture.Session.StepJournal.Select(entry => entry.Preview).ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(fixture.Session.StepCount).IsEqualTo(2);
            await Assert.That(previews[0]).Contains("\"шт\"");
            await Assert.That(previews[1]).Contains("\"кг\"");
            await Assert.That(fixture.Session.ExportPreview()).Contains("unitQuantityEditorsItemKey2");
        }
    }

    [Test]
    public async Task TwoNamedItems_GenerateDistinctVariablesAndReuseTheSelectedOne()
    {
        using var fixture = MultiItemFixture.CreateByName("шт", "кг");
        using var directory = new TemporaryDirectory();
        fixture.Session.Start();
        fixture.EnterValue(0, "10", useNumericUpDown: false);
        fixture.EnterValue(1, "20", useNumericUpDown: false);

        RecorderCheckTargetSelection? selection = null;
        fixture.Session.CheckTargetSelected += (_, args) => selection = args.Selection;
        fixture.Session.BeginCheckTargetSelection();
        fixture.Session.SelectCheckTargetForTesting(fixture.InputAt(1));
        ((IRecorderCheckpointSessionDetails)fixture.Session).CaptureCheckpoint(selection!, "quantityCheckpoint");
        var preview = fixture.Session.ExportPreview();
        var source = $$"""
            using AppAutomation.Abstractions;
            using TUnit.Assertions;
            using TUnit.Assertions.Extensions;

            public sealed class QuantityPage : UiPage
            {
                public QuantityPage(IUiControlResolver resolver) : base(resolver) { }
                public IMultiItemControlCollection UnitQuantityEditors => null!;
            }

            public sealed class RecordedScenario
            {
                private QuantityPage Page => null!;

                public void Run()
                {
                    {{preview}}
                }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "RecordedScenario.cs"), source);

        using (Assert.Multiple())
        {
            await Assert.That(preview).Contains("var unitQuantityEditorsItemKey = \"шт\";");
            await Assert.That(preview).Contains("var unitQuantityEditorsItemKey2 = \"кг\";");
            await Assert.That(preview).Contains("SetMultiItemSpinnerValue(static page => page.UnitQuantityEditors, unitQuantityEditorsItemKey, 10)");
            await Assert.That(preview).Contains("SetMultiItemSpinnerValue(static page => page.UnitQuantityEditors, unitQuantityEditorsItemKey2, 20)");
            await Assert.That(preview).Contains("ResolveSpinner(unitQuantityEditorsItemKey2, 5000).Value");
            await Assert.That(fixture.Session.StepJournal[1].Preview)
                .Contains("var unitQuantityEditorsItemKey2 = \"кг\";");
            await Assert.That(preview.Split("var unitQuantityEditorsItemKey2 =", StringSplitOptions.None).Length)
                .IsEqualTo(2);
            await Assert.That(RecorderGeneratedSourceCompiler.Compile(directory.Path)).IsEmpty();
        }
    }

    [Test]
    public async Task NamedItemKey_DoesNotShadowCheckpointVariable()
    {
        using var fixture = MultiItemFixture.CreateByName("шт");
        fixture.InputAt(0).Text = "42";
        fixture.Session.Start();
        RecorderCheckTargetSelection? selection = null;
        fixture.Session.CheckTargetSelected += (_, args) => selection = args.Selection;
        fixture.Session.BeginCheckTargetSelection();
        fixture.Session.SelectCheckTargetForTesting(fixture.InputAt(0));
        ((IRecorderCheckpointSessionDetails)fixture.Session)
            .CaptureCheckpoint(selection!, "unitQuantityEditorsItemKey");

        var preview = fixture.Session.ExportPreview();
        using (Assert.Multiple())
        {
            await Assert.That(preview).Contains("var unitQuantityEditorsItemKey2 = \"шт\";");
            await Assert.That(preview).Contains("var unitQuantityEditorsItemKey = Page.UnitQuantityEditors.ResolveSpinner(unitQuantityEditorsItemKey2, 5000).Value;");
        }
    }

    [Test]
    public async Task CheckMode_FormattedSpinnerValue_RemainsNumericAssertion()
    {
        using var fixture = MultiItemFixture.Create("unit-a", "unit-b");
        var input = fixture.InputAt(1);
        input.Text = "1\u00a0000";
        fixture.Session.Start();
        RecorderCheckTargetSelection? selection = null;
        fixture.Session.CheckTargetSelected += (_, args) => selection = args.Selection;
        fixture.Session.BeginCheckTargetSelection();
        fixture.Session.SelectCheckTargetForTesting(input);
        ((IRecorderCheckpointSessionDetails)fixture.Session).CaptureCurrentValueAssertion(selection!);

        using (Assert.Multiple())
        {
            await Assert.That(selection!.ValueDescription?.CurrentValueText).IsEqualTo("1000");
            await Assert.That(fixture.Session.PersistableStepCount).IsEqualTo(1).Because(fixture.Session.LatestStatus);
            await Assert.That(fixture.Session.ExportPreview()).Contains(
                "Page.UnitQuantityEditors.ResolveSpinner(unitQuantityEditorsItemKey, 5000).Value).IsEqualTo(1000)");
        }
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, false)]
    [Arguments(true, true)]
    [Arguments(false, true)]
    public async Task CheckMode_InvalidStableKey_RejectsRawInputFallback(bool duplicate, bool useNameKey)
    {
        var keys = duplicate
            ? new string?[] { "unit-a", "unit-b", "unit-b" }
            : ["unit-a", null, "unit-c"];
        using var fixture = useNameKey
            ? MultiItemFixture.CreateByName(keys)
            : MultiItemFixture.Create(keys);
        var input = fixture.InputAt(1);
        fixture.Session.Start();
        RecorderCheckTargetSelection? selection = null;
        fixture.Session.CheckTargetSelected += (_, args) => selection = args.Selection;
        fixture.Session.BeginCheckTargetSelection();
        fixture.Session.SelectCheckTargetForTesting(input);

        var details = (IRecorderCheckpointSessionDetails)fixture.Session;
        details.CaptureEnabledAssertion(selection!, expectedEnabled: true);

        using (Assert.Multiple())
        {
            await Assert.That(selection!.CanCaptureAssertions).IsFalse();
            await Assert.That(selection.ValueDescription).IsNull();
            await Assert.That(selection.ValueDescriptionError).Contains(
                duplicate ? "multiple item containers" : "empty stable key");
            await Assert.That(fixture.Session.StepCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task CheckMode_KeyedSpinner_CanBeCalculatedNumericOperand()
    {
        using var fixture = MultiItemFixture.Create("unit-a", "unit-b", "unit-c");
        var input = fixture.InputAt(1);
        input.Text = "42";
        fixture.Session.Start();
        RecorderCheckTargetSelection? selection = null;
        fixture.Session.CheckTargetSelected += (_, args) => selection = args.Selection;
        fixture.Session.BeginCheckTargetSelection();
        fixture.Session.SelectCheckTargetForTesting(input);

        var operandResolved = RecorderStepFactory.TryCreateNumericControlOperand(
            selection!.ValueSnapshot,
            out var operand,
            out var error);
        if (operandResolved)
        {
            ((IRecorderCheckpointSessionDetails)fixture.Session).CaptureCalculatedAssertion(
                selection,
                new RecorderNumericExpectedExpression(
                    RecorderArithmeticOperation.Add,
                    operand!,
                    RecorderNumericOperand.FromLiteral(1)));
        }

        using (Assert.Multiple())
        {
            await Assert.That(operandResolved).IsTrue().Because(error);
            await Assert.That(fixture.Session.PersistableStepCount).IsEqualTo(1).Because(fixture.Session.LatestStatus);
            await Assert.That(fixture.Session.ExportPreview()).Contains(
                "Page.UnitQuantityEditors.ResolveSpinner(unitQuantityEditorsItemKey, 5000).Value + 1");
        }
    }

    [Test]
    public async Task CheckMode_KeyedSpinner_GeneratedCheckpointAndAssertionsCompile()
    {
        using var fixture = MultiItemFixture.Create("unit-a", "unit-b", "unit-c");
        using var directory = new TemporaryDirectory();
        var input = fixture.InputAt(1);
        input.Text = "42";
        fixture.Session.Start();
        RecorderCheckTargetSelection? selection = null;
        fixture.Session.CheckTargetSelected += (_, args) => selection = args.Selection;
        fixture.Session.BeginCheckTargetSelection();
        fixture.Session.SelectCheckTargetForTesting(input);
        var details = (IRecorderCheckpointSessionDetails)fixture.Session;
        details.CaptureCheckpoint(selection!, "secondQuantity");
        details.CaptureCheckpointAssertion(selection!, details.Checkpoints.Single().CheckpointId);
        details.CaptureEnabledAssertion(selection!, expectedEnabled: true);
        var statements = fixture.Session.ExportPreview();
        var source = $$"""
            using AppAutomation.Abstractions;
            using TUnit.Assertions;
            using TUnit.Assertions.Extensions;
            using System.Threading.Tasks;

            public sealed class QuantityPage : UiPage
            {
                public QuantityPage(IUiControlResolver resolver) : base(resolver) { }
                public IMultiItemControlCollection UnitQuantityEditors => null!;
            }

            public sealed class RecordedScenario
            {
                private QuantityPage Page => null!;

                public async Task Run()
                {
                    {{statements}}
                }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "RecordedScenario.cs"), source);

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Session.PersistableStepCount).IsEqualTo(3).Because(fixture.Session.LatestStatus);
            await Assert.That(RecorderGeneratedSourceCompiler.Compile(directory.Path)).IsEmpty();
        }
    }

    [Test]
    public async Task CheckMode_CandidateGraph_DoesNotConflateDifferentStableKeys()
    {
        using var fixture = MultiItemFixture.Create("unit-a", "unit-b");
        var options = new AppAutomationRecorderOptions { MultiItemControls = CreateCatalog() };
        var factory = new RecorderStepFactory(options, () => fixture.Collection);

        var captured = factory.TryCaptureConfiguredSemanticValueSnapshot(
            [fixture.InputAt(0), fixture.InputAt(1)],
            out _,
            out _,
            out var error,
            out var definitiveFailure);

        using (Assert.Multiple())
        {
            await Assert.That(captured).IsFalse();
            await Assert.That(definitiveFailure).IsTrue();
            await Assert.That(error).Contains("unit-a");
            await Assert.That(error).Contains("unit-b");
        }
    }

    [Test]
    [Arguments(1, "unit-b", "12.5", false)]
    [Arguments(2, "unit-c", "18", true)]
    public async Task SubsequentItem_RecordsOneLogicalActionWithStableKey(
        int itemIndex,
        string itemKey,
        string value,
        bool useNumericUpDown)
    {
        using var fixture = useNumericUpDown
            ? MultiItemFixture.CreateWithNativeSpinner(itemIndex, "unit-a", "unit-b", "unit-c")
            : MultiItemFixture.Create("unit-a", "unit-b", "unit-c");

        fixture.Session.Start();
        fixture.EnterValue(itemIndex, value, useNumericUpDown);

        var step = fixture.Session.StepJournal.Single();
        using (Assert.Multiple())
        {
            await Assert.That(step.CanPersist).IsTrue().Because(step.StatusMessage);
            await Assert.That(step.Preview).Contains(
                $"var unitQuantityEditorsItemKey = \"{itemKey}\";");
            await Assert.That(step.Preview).Contains(
                $"Page.SetMultiItemSpinnerValue(static page => page.UnitQuantityEditors, unitQuantityEditorsItemKey, {value});");
            await Assert.That(step.Preview).DoesNotContain("EnterText");
            await Assert.That(step.Preview).DoesNotContain("SetSpinnerValue(");
        }
    }

    [Test]
    [Arguments("500", "500")]
    [Arguments("1\u00a0000", "1000")]
    [Arguments("10\u00a0001", "10001")]
    [Arguments("-1\u00a0234,5", "-1234.5")]
    [Arguments("12,5", "12.5")]
    public async Task LocalizedInput_RecordsNumericSpinnerAction(string input, string expectedNumber)
    {
        using var fixture = MultiItemFixture.Create("unit-a", "unit-b");

        fixture.Session.Start();
        fixture.EnterValue(1, input, useNumericUpDown: false);

        var step = fixture.Session.StepJournal.Single();
        using (Assert.Multiple())
        {
            await Assert.That(step.CanPersist).IsTrue().Because(step.StatusMessage);
            await Assert.That(step.Preview).Contains(
                $"Page.SetMultiItemSpinnerValue(static page => page.UnitQuantityEditors, unitQuantityEditorsItemKey, {expectedNumber});");
        }
    }

    [Test]
    public async Task UnconfiguredNumericUpDownInsideConfiguredRoot_UsesPrimitiveSpinnerAction()
    {
        using var fixture = MultiItemFixture.Create("unit-a");

        fixture.Session.Start();
        fixture.EnterNumericValue(0, "7");

        var step = fixture.Session.StepJournal.Single();
        using (Assert.Multiple())
        {
            await Assert.That(step.Preview).Contains("Page.SetSpinnerValue(");
            await Assert.That(step.Preview).DoesNotContain("SetMultiItemSpinnerValue");
        }
    }

    [Test]
    [Arguments(BrokenCapturePart.MissingItem, "ItemContainer")]
    [Arguments(BrokenCapturePart.MissingRoot, "SpinnerRoot")]
    [Arguments(BrokenCapturePart.MissingInput, "SpinnerInput")]
    public async Task BrokenConfiguredGraph_CreatesOnlyInvalidSemanticStep(
        BrokenCapturePart brokenPart,
        string expectedPart)
    {
        using var fixture = MultiItemFixture.CreateWithBrokenGraph(brokenPart);

        fixture.Session.Start();
        fixture.EnterBrokenValue("9");

        var step = fixture.Session.StepJournal.Single();
        using (Assert.Multiple())
        {
            await Assert.That(step.CanPersist).IsFalse();
            await Assert.That(step.StatusMessage).Contains(expectedPart);
            await Assert.That(step.Preview).Contains("SetMultiItemSpinnerValue");
            await Assert.That(step.Preview).DoesNotContain("EnterText");
            await Assert.That(step.Preview).DoesNotContain("SetSpinnerValue(");
        }
    }

    [Test]
    public async Task DuplicateStableKey_CreatesOnlyInvalidSemanticStep()
    {
        using var fixture = MultiItemFixture.Create("unit-a", "unit-b", "unit-b");

        fixture.Session.Start();
        fixture.EnterValue(1, "18", useNumericUpDown: false);

        var step = fixture.Session.StepJournal.Single();
        using (Assert.Multiple())
        {
            await Assert.That(step.CanPersist).IsFalse();
            await Assert.That(step.StatusMessage).Contains("multiple item containers");
            await Assert.That(step.Preview).Contains("SetMultiItemSpinnerValue");
            await Assert.That(step.Preview).DoesNotContain("EnterText");
        }
    }

    [Test]
    public async Task MissingStableKey_CreatesOnlyInvalidSemanticStep()
    {
        using var fixture = MultiItemFixture.Create("unit-a", null, "unit-c");

        fixture.Session.Start();
        fixture.EnterValue(1, "21", useNumericUpDown: false);

        var step = fixture.Session.StepJournal.Single();
        using (Assert.Multiple())
        {
            await Assert.That(step.CanPersist).IsFalse();
            await Assert.That(step.StatusMessage).Contains("expose an empty stable key");
            await Assert.That(step.Preview).Contains("SetMultiItemSpinnerValue");
            await Assert.That(step.Preview).DoesNotContain("EnterText");
        }
    }

    [Test]
    [Arguments("NaN")]
    [Arguments("Infinity")]
    public async Task NonFiniteValue_CreatesOnlyInvalidSemanticStep(string value)
    {
        using var fixture = MultiItemFixture.Create("unit-a", "unit-b", "unit-c");

        fixture.Session.Start();
        fixture.EnterValue(1, value, useNumericUpDown: false);

        var step = fixture.Session.StepJournal.Single();
        var nonFiniteValue = double.Parse(value, CultureInfo.InvariantCulture);
        var malformedStep = new RecordedStep(
            RecordedActionKind.SetMultiItemSpinnerValue,
            new RecordedControlDescriptor(
                "UnitQuantityEditors",
                UiControlType.MultiItemControlCollection,
                "UnitQuantityCollection",
                UiLocatorKind.AutomationId,
                FallbackToName: false,
                typeof(TextBox).FullName ?? nameof(TextBox),
                Warning: null),
            DoubleValue: nonFiniteValue,
            RepeatedItemKey: "unit-b");
        var options = new AppAutomationRecorderOptions { MultiItemControls = CreateCatalog() };
        var validated = new RecorderCommandRuntimeValidator(options).Validate(malformedStep);
        var preview = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null)
            .GeneratePreview(malformedStep);
        using (Assert.Multiple())
        {
            await Assert.That(step.CanPersist).IsFalse();
            await Assert.That(step.StatusMessage).Contains("not a finite number");
            await Assert.That(step.Preview).Contains("SetMultiItemSpinnerValue");
            await Assert.That(step.Preview).DoesNotContain("EnterText");
            await Assert.That(validated.CanPersist).IsFalse();
            await Assert.That(validated.RuntimeValidationFindings!.Any(
                static finding => finding.Code.EndsWith("payload-invalid-double", StringComparison.Ordinal))).IsTrue();
            await Assert.That(preview).Contains(
                double.IsNaN(nonFiniteValue) ? "double.NaN" : "double.PositiveInfinity");
        }
    }

    private sealed class MultiItemFixture : IDisposable
    {
        private readonly IReadOnlyList<TextBox?> _inputs;
        private readonly IReadOnlyList<NumericUpDown> _spinners;
        private readonly Control? _brokenSource;

        private MultiItemFixture(
            RecorderSession session,
            StackPanel collection,
            IReadOnlyList<TextBox?> inputs,
            IReadOnlyList<NumericUpDown> spinners,
            Control? brokenSource = null)
        {
            Session = session;
            Collection = collection;
            _inputs = inputs;
            _spinners = spinners;
            _brokenSource = brokenSource;
        }

        public RecorderSession Session { get; }

        public StackPanel Collection { get; }

        public TextBox InputAt(int itemIndex) => _inputs[itemIndex]
            ?? throw new InvalidOperationException("The fixture item does not expose a configured text input.");

        public static MultiItemFixture Create(params string?[] keys) =>
            CreateCore(nativeSpinnerItemIndex: null, keys, useNameKey: false);

        public static MultiItemFixture CreateByName(params string?[] names) =>
            CreateCore(nativeSpinnerItemIndex: null, names, useNameKey: true);

        public static MultiItemFixture CreateWithNativeSpinner(
            int nativeSpinnerItemIndex,
            params string?[] keys) =>
            CreateCore(nativeSpinnerItemIndex, keys, useNameKey: false);

        public static MultiItemFixture CreateWithBrokenGraph(BrokenCapturePart brokenPart)
        {
            var collection = new StackPanel();
            AutomationProperties.SetAutomationId(collection, "UnitQuantityCollection");
            Control source;
            switch (brokenPart)
            {
                case BrokenCapturePart.MissingItem:
                    source = CreateInput();
                    collection.Children.Add(source);
                    break;
                case BrokenCapturePart.MissingRoot:
                    var missingRootItem = CreateItem("unit-a");
                    source = CreateInput();
                    missingRootItem.Child = source;
                    collection.Children.Add(missingRootItem);
                    break;
                case BrokenCapturePart.MissingInput:
                    var missingInputItem = CreateItem("unit-a");
                    var spinner = new NumericUpDown { Value = 0 };
                    AutomationProperties.SetAutomationId(spinner, "QuantityEditor");
                    source = spinner;
                    missingInputItem.Child = spinner;
                    collection.Children.Add(missingInputItem);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(brokenPart), brokenPart, null);
            }

            return CreateFixture(collection, [], [], source);
        }

        private static MultiItemFixture CreateCore(
            int? nativeSpinnerItemIndex,
            IReadOnlyList<string?> keys,
            bool useNameKey)
        {
            var collection = new StackPanel();
            AutomationProperties.SetAutomationId(collection, "UnitQuantityCollection");
            var inputs = new List<TextBox?>();
            var spinners = new List<NumericUpDown>();
            for (var itemIndex = 0; itemIndex < keys.Count; itemIndex++)
            {
                var key = keys[itemIndex];
                var item = new Border();
                AutomationProperties.SetAutomationId(item, "UnitQuantityItem");
                if (useNameKey)
                {
                    AutomationProperties.SetName(item, key);
                    AutomationProperties.SetItemStatus(item, $"id-{itemIndex}");
                }
                else
                {
                    AutomationProperties.SetItemStatus(item, key);
                }
                TextBox? input = null;
                NumericUpDown spinner;
                if (nativeSpinnerItemIndex == itemIndex)
                {
                    input = CreateInput();
                    spinner = new LogicalChildNumericUpDown(input) { Value = 0 };
                    AutomationProperties.SetAutomationId(spinner, "QuantityEditor");
                    item.Child = spinner;
                }
                else
                {
                    var spinnerRoot = new StackPanel();
                    AutomationProperties.SetAutomationId(spinnerRoot, "QuantityEditor");
                    input = CreateInput();
                    spinner = new NumericUpDown { Value = 0 };
                    AutomationProperties.SetAutomationId(spinner, "DiscountEditor");
                    spinnerRoot.Children.Add(input);
                    spinnerRoot.Children.Add(spinner);
                    item.Child = spinnerRoot;
                }

                collection.Children.Add(item);
                inputs.Add(input);
                spinners.Add(spinner);
            }

            return CreateFixture(collection, inputs, spinners, useNameKey: useNameKey);
        }

        private static MultiItemFixture CreateFixture(
            StackPanel collection,
            IReadOnlyList<TextBox?> inputs,
            IReadOnlyList<NumericUpDown> spinners,
            Control? brokenSource = null,
            bool useNameKey = false)
        {
            var options = new AppAutomationRecorderOptions
            {
                ShowOverlay = false,
                DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false },
                Validation = new RecorderValidationOptions { CaptureInvalidSteps = true },
                MultiItemControls = CreateCatalog(useNameKey
                    ? UiAutomationValueProperty.Name
                    : UiAutomationValueProperty.ItemStatus)
            };
            var session = new RecorderSession(
                RecorderTestWindow.CreateStub(),
                options,
                () => collection,
                attachWindowHandlers: false,
                autosaveOperation: CompleteAutosaveAsync);
            session.AttachInputHandlersForTesting();
            return new MultiItemFixture(session, collection, inputs, spinners, brokenSource);
        }

        private static Border CreateItem(string key)
        {
            var item = new Border();
            AutomationProperties.SetAutomationId(item, "UnitQuantityItem");
            AutomationProperties.SetItemStatus(item, key);
            return item;
        }

        private static TextBox CreateInput()
        {
            var input = new TextBox { Text = "0" };
            AutomationProperties.SetAutomationId(input, "QuantityEditor_Input");
            return input;
        }

        public void EnterValue(int itemIndex, string value, bool useNumericUpDown)
        {
            if (useNumericUpDown)
            {
                var spinner = _spinners[itemIndex];
                Session.RegisterKeyboardInputForTesting(spinner);
                spinner.Value = decimal.Parse(value, CultureInfo.InvariantCulture);
            }
            else
            {
                var input = _inputs[itemIndex]
                    ?? throw new InvalidOperationException("The fixture item does not expose a configured text input.");
                Session.RegisterKeyboardInputForTesting(input);
                input.Text = value;
            }

            Session.FlushPendingStateForTesting();
        }

        public void EnterNumericValue(int itemIndex, string value)
        {
            var spinner = _spinners[itemIndex];
            Session.RegisterKeyboardInputForTesting(spinner);
            spinner.Value = decimal.Parse(value, CultureInfo.InvariantCulture);
            Session.FlushPendingStateForTesting();
        }

        public void EnterBrokenValue(string value)
        {
            switch (_brokenSource)
            {
                case TextBox input:
                    Session.RegisterKeyboardInputForTesting(input);
                    input.Text = value;
                    break;
                case NumericUpDown spinner:
                    Session.RegisterKeyboardInputForTesting(spinner);
                    spinner.Value = decimal.Parse(value, CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new InvalidOperationException("The fixture does not expose a broken configured source.");
            }

            Session.FlushPendingStateForTesting();
        }

        public void Dispose() => Session.Dispose();

        private static Task<RecorderSaveResult> CompleteAutosaveAsync(
            IReadOnlyList<RecordedStep> steps,
            string? outputDirectory,
            CancellationToken cancellationToken) =>
            Task.FromResult(RecorderSaveResult.Completed(
                "Autosaved.",
                pageFilePath: null,
                scenarioFilePath: null,
                persistedStepCount: steps.Count,
                skippedStepCount: 0));

        private sealed class LogicalChildNumericUpDown : NumericUpDown
        {
            public LogicalChildNumericUpDown(TextBox input)
            {
                LogicalChildren.Add(input);
            }
        }
    }

    public enum BrokenCapturePart
    {
        MissingItem,
        MissingRoot,
        MissingInput
    }

    private static MultiItemControlCatalog CreateCatalog(
        UiAutomationValueProperty keyProperty = UiAutomationValueProperty.ItemStatus)
    {
        var catalog = new MultiItemControlCatalog().Add(
            CreateDefinition("UnitQuantityEditors", "QuantityEditor", "QuantityEditor_Input", keyProperty));
        return catalog.Add(
            CreateDefinition("OtherUnitQuantityEditors", "OtherQuantityEditor", "OtherQuantityEditor_Input", keyProperty));
    }

    private static MultiItemControlDefinition CreateDefinition(
        string propertyName,
        string rootAutomationId,
        string inputAutomationId,
        UiAutomationValueProperty keyProperty) =>
        MultiItemControlDefinition.ByAutomationIds(
                    propertyName,
                    "UnitQuantityCollection",
                    "UnitQuantityCollection")
                .WithItems(
                    MultiItemRelativeLocator.ByAutomationId(
                        "UnitQuantityItem",
                        MultiItemRelativeLocatorScope.CollectionRoot),
                    RepeatedItemKeyDefinition.FromItem(keyProperty))
                .WithSpinner(new MultiItemSpinnerParts(
                    MultiItemRelativeLocator.ByAutomationId(
                        rootAutomationId,
                        MultiItemRelativeLocatorScope.ItemRoot),
                    MultiItemRelativeLocator.ByAutomationId(
                        inputAutomationId,
                        MultiItemRelativeLocatorScope.ControlRoot)));
}
