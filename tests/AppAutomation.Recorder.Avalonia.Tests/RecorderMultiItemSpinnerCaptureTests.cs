using System.Globalization;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using Avalonia.Automation;
using Avalonia.Controls;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderMultiItemSpinnerCaptureTests
{
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
                $"Page.SetMultiItemSpinnerValue(static page => page.UnitQuantityEditors, \"{itemKey}\", {value});");
            await Assert.That(step.Preview).DoesNotContain("EnterText");
            await Assert.That(step.Preview).DoesNotContain("SetSpinnerValue(");
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
            await Assert.That(step.StatusMessage).Contains("not a finite invariant number");
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
            IReadOnlyList<TextBox?> inputs,
            IReadOnlyList<NumericUpDown> spinners,
            Control? brokenSource = null)
        {
            Session = session;
            _inputs = inputs;
            _spinners = spinners;
            _brokenSource = brokenSource;
        }

        public RecorderSession Session { get; }

        public static MultiItemFixture Create(params string?[] keys) =>
            CreateCore(nativeSpinnerItemIndex: null, keys);

        public static MultiItemFixture CreateWithNativeSpinner(
            int nativeSpinnerItemIndex,
            params string?[] keys) =>
            CreateCore(nativeSpinnerItemIndex, keys);

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
            params string?[] keys)
        {
            var collection = new StackPanel();
            AutomationProperties.SetAutomationId(collection, "UnitQuantityCollection");
            var inputs = new List<TextBox?>();
            var spinners = new List<NumericUpDown>();
            for (var itemIndex = 0; itemIndex < keys.Length; itemIndex++)
            {
                var key = keys[itemIndex];
                var item = new Border();
                AutomationProperties.SetAutomationId(item, "UnitQuantityItem");
                AutomationProperties.SetItemStatus(item, key);
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

            return CreateFixture(collection, inputs, spinners);
        }

        private static MultiItemFixture CreateFixture(
            StackPanel collection,
            IReadOnlyList<TextBox?> inputs,
            IReadOnlyList<NumericUpDown> spinners,
            Control? brokenSource = null)
        {
            var options = new AppAutomationRecorderOptions
            {
                ShowOverlay = false,
                DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false },
                Validation = new RecorderValidationOptions { CaptureInvalidSteps = true },
                MultiItemControls = CreateCatalog()
            };
            var session = new RecorderSession(
                RecorderTestWindow.CreateStub(),
                options,
                () => collection,
                attachWindowHandlers: false,
                autosaveOperation: CompleteAutosaveAsync);
            session.AttachInputHandlersForTesting();
            return new MultiItemFixture(session, inputs, spinners, brokenSource);
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

    private static MultiItemControlCatalog CreateCatalog()
    {
        var catalog = new MultiItemControlCatalog().Add(
            CreateDefinition("UnitQuantityEditors", "QuantityEditor", "QuantityEditor_Input"));
        return catalog.Add(
            CreateDefinition("OtherUnitQuantityEditors", "OtherQuantityEditor", "OtherQuantityEditor_Input"));
    }

    private static MultiItemControlDefinition CreateDefinition(
        string propertyName,
        string rootAutomationId,
        string inputAutomationId) =>
        MultiItemControlDefinition.ByAutomationIds(
                    propertyName,
                    "UnitQuantityCollection",
                    "UnitQuantityCollection")
                .WithItems(
                    MultiItemRelativeLocator.ByAutomationId(
                        "UnitQuantityItem",
                        MultiItemRelativeLocatorScope.CollectionRoot),
                    RepeatedItemKeyDefinition.FromItem(UiAutomationValueProperty.ItemStatus))
                .WithSpinner(new MultiItemSpinnerParts(
                    MultiItemRelativeLocator.ByAutomationId(
                        rootAutomationId,
                        MultiItemRelativeLocatorScope.ItemRoot),
                    MultiItemRelativeLocator.ByAutomationId(
                        inputAutomationId,
                        MultiItemRelativeLocatorScope.ControlRoot)));
}
