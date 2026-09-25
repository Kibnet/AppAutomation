using System.Globalization;
using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class MultiItemControlTests
{
    [Test]
    public async Task Catalog_IsImmutableAndRejectsDuplicatePropertiesAndCaptureSignatures()
    {
        var definition = CreateDefinition("UnitQuantityEditors", "QuantityEditor");
        var empty = new MultiItemControlCatalog();
        var configured = empty.Add(definition);

        var duplicateProperty = Assert.Throws<ArgumentException>(() =>
            configured.Add(CreateDefinition("UnitQuantityEditors", "OtherEditor")));
        var duplicateSignature = Assert.Throws<ArgumentException>(() =>
            configured.Add(CreateDefinition("OtherQuantityEditors", "QuantityEditor")));
        var invalidCaptureLocator = Assert.Throws<ArgumentException>(() =>
            empty.Add(CreateDefinition(
                "InvalidCaptureLocator",
                "CaptureEditor",
                captureLocatorKind: (UiLocatorKind)int.MaxValue)));
        var invalidRuntimeLocator = Assert.Throws<ArgumentException>(() =>
            empty.Add(CreateDefinition(
                "InvalidRuntimeLocator",
                "RuntimeEditor",
                runtimeLocatorKind: (UiLocatorKind)int.MaxValue)));
        var invalidDirectAdapter = Assert.Throws<ArgumentException>(() =>
            new MultiItemControlAdapter(CreateDefinition(
                "InvalidDirectAdapter",
                "AdapterEditor",
                runtimeLocatorKind: (UiLocatorKind)int.MaxValue)));

        using (Assert.Multiple())
        {
            await Assert.That(empty.Count).IsEqualTo(0);
            await Assert.That(configured.Count).IsEqualTo(1);
            await Assert.That(configured.Fingerprint).IsNotEqualTo(empty.Fingerprint);
            await Assert.That(duplicateProperty.Message).Contains("configured more than once");
            await Assert.That(duplicateSignature.Message).Contains("capture signature");
            await Assert.That(invalidCaptureLocator.Message).Contains(nameof(MultiItemControlDefinition.CaptureLocatorKind));
            await Assert.That(invalidRuntimeLocator.Message).Contains(nameof(MultiItemControlDefinition.RuntimeLocatorKind));
            await Assert.That(invalidDirectAdapter.Message).Contains(nameof(MultiItemControlDefinition.RuntimeLocatorKind));
        }
    }

    [Test]
    public async Task SetMultiItemSpinnerValue_ResolvesStableKeyAfterItemsAreReordered()
    {
        var definition = CreateDefinition("UnitQuantityEditors", "QuantityEditor");
        var catalog = new MultiItemControlCatalog().Add(definition);
        var runtime = new FakeRuntimeResolver(
        [
            new FakeItem("unit-a", 10),
            new FakeItem("unit-b", 20),
            new FakeItem("unit-c", 30)
        ]);
        var page = new MultiItemPage(runtime.WithMultiItemControls(catalog));

        page.SetMultiItemSpinnerValue(static candidate => candidate.UnitQuantityEditors, "unit-b", 12.5);
        runtime.ReverseItems();
        page.SetMultiItemSpinnerValue(static candidate => candidate.UnitQuantityEditors, "unit-b", 14.5);
        var spinner = page.UnitQuantityEditors.ResolveSpinner("unit-b", timeoutMs: 5000);
        var nanError = Assert.Throws<ArgumentOutOfRangeException>(() => spinner.Value = double.NaN);
        var infinityError = Assert.Throws<ArgumentOutOfRangeException>(() => spinner.Value = double.PositiveInfinity);

        using (Assert.Multiple())
        {
            await Assert.That(runtime.Items.Single(item => item.Key == "unit-b").Value).IsEqualTo(14.5);
            await Assert.That(runtime.Items[0].Key).IsEqualTo("unit-c");
            await Assert.That(runtime.RequestedKeys).IsEquivalentTo(
                ["unit-b", "unit-b", "unit-b", "unit-b", "unit-b"]);
            await Assert.That(nanError.Message).Contains("finite");
            await Assert.That(infinityError.Message).Contains("finite");
        }
    }

    private static MultiItemControlDefinition CreateDefinition(
        string propertyName,
        string spinnerRoot,
        UiLocatorKind captureLocatorKind = UiLocatorKind.AutomationId,
        UiLocatorKind runtimeLocatorKind = UiLocatorKind.AutomationId) =>
        MultiItemControlDefinition.ByLocators(
                propertyName,
                "UnitQuantityCollection",
                captureLocatorKind,
                "UnitQuantityCollection",
                runtimeLocatorKind)
            .WithItems(
                MultiItemRelativeLocator.ByAutomationId(
                    "UnitQuantityItem",
                    MultiItemRelativeLocatorScope.CollectionRoot),
                RepeatedItemKeyDefinition.FromItem(UiAutomationValueProperty.ItemStatus))
            .WithSpinner(new MultiItemSpinnerParts(
                MultiItemRelativeLocator.ByAutomationId(
                    spinnerRoot,
                    MultiItemRelativeLocatorScope.ItemRoot),
                MultiItemRelativeLocator.ByAutomationId(
                    "QuantityEditor_Input",
                    MultiItemRelativeLocatorScope.ControlRoot)));

    private static class MultiItemPageDefinitions
    {
        public static readonly UiControlDefinition UnitQuantityEditors = new(
            "UnitQuantityEditors",
            UiControlType.MultiItemControlCollection,
            "UnitQuantityCollection");
    }

    private sealed class MultiItemPage : UiPage
    {
        public MultiItemPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IMultiItemControlCollection UnitQuantityEditors =>
            Resolve<IMultiItemControlCollection>(MultiItemPageDefinitions.UnitQuantityEditors);
    }

    private sealed class FakeRuntimeResolver(List<FakeItem> items) :
        IUiControlResolver,
        IMultiItemControlRuntimeResolver
    {
        public List<FakeItem> Items { get; } = items;

        public List<string> RequestedKeys { get; } = [];

        public UiRuntimeCapabilities Capabilities { get; } = new("fake-multi-item");

        public MultiItemControlState ReadMultiItemCollectionState(MultiItemControlDefinition definition)
        {
            _ = definition;
            return new MultiItemControlState(IsAvailable: true, IsEnabled: true);
        }

        public void ReverseItems() => Items.Reverse();

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class =>
            throw new InvalidOperationException($"Unknown primitive control '{definition.PropertyName}'.");

        public ISpinnerControl ResolveMultiItemSpinner(
            MultiItemControlDefinition definition,
            string itemKey,
            int timeoutMs)
        {
            _ = definition;
            _ = timeoutMs;
            RequestedKeys.Add(itemKey);
            var matches = Items.Where(item => string.Equals(item.Key, itemKey, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException($"Stable key '{itemKey}' matched {matches.Length} items.");
            }

            var item = matches[0];
            return new SpinnerTextControl(
                "QuantityEditor",
                () => item.Key,
                () => true,
                () => true,
                () => item.Value.ToString("R", CultureInfo.InvariantCulture),
                text => item.Value = double.Parse(text, CultureInfo.InvariantCulture));
        }
    }

    private sealed class FakeItem(string key, double value)
    {
        public string Key { get; } = key;

        public double Value { get; set; } = value;
    }

}
