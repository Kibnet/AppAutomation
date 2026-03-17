using AppAutomation.Abstractions;
using System.Reflection;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class UiControlAdapterTests
{
    [Test]
    public async Task SearchPickerAdapter_SupportsSharedPageFlow()
    {
        var searchInput = new FakeTextBoxControl("HistoryFilterInput");
        var applyButton = new FakeButtonControl("ApplyFilterButton");
        var comboBox = new FakeComboBoxControl(
            "OperationCombo",
            new[]
            {
                new FakeComboBoxItem("Greatest Common Divisor", "Greatest Common Divisor"),
                new FakeComboBoxItem("Least Common Multiple", "Least Common Multiple")
            });

        var resolver = new FakeResolver(
            ("HistoryFilterInput", searchInput),
            ("ApplyFilterButton", applyButton),
            ("OperationCombo", comboBox))
            .WithSearchPicker(
                "HistoryOperationPicker",
                SearchPickerParts.ByAutomationIds(
                    "HistoryFilterInput",
                    "OperationCombo",
                    applyButtonAutomationId: "ApplyFilterButton"));
        var page = new SearchPickerPage(resolver);

        page.SearchAndSelect(
            static candidate => candidate.HistoryOperationPicker,
            "least",
            "Least Common Multiple");

        using (Assert.Multiple())
        {
            await Assert.That(page.HistoryOperationPicker.SearchText).IsEqualTo("least");
            await Assert.That(page.HistoryOperationPicker.SelectedItemText).IsEqualTo("Least Common Multiple");
            await Assert.That(page.HistoryOperationPicker.Items.Count).IsEqualTo(2);
            await Assert.That(applyButton.InvokeCount).IsEqualTo(1);
            await Assert.That(comboBox.SelectedIndex).IsEqualTo(1);
        }
    }

    [Test]
    public async Task WithAdaptersFromAssembly_RegistersAdaptersFromAssembly()
    {
        var resolver = new MinimalResolver()
            .WithAdaptersFromAssembly(typeof(TestableAdapter).Assembly);

        var definition = new UiControlDefinition(
            "TestProperty",
            UiControlType.AutomationElement,
            "TestLocator",
            UiLocatorKind.AutomationId,
            FallbackToName: false);

        var control = resolver.Resolve<IUiControl>(definition);

        await Assert.That(control.AutomationId).IsEqualTo("ResolvedByTestableAdapter");
    }

    [Test]
    public async Task WithDefaultAdapters_ReturnsResolverWithoutError()
    {
        var resolver = new MinimalResolver();

        var wrappedResolver = resolver.WithDefaultAdapters();

        await Assert.That(wrappedResolver).IsNotNull();
        await Assert.That(wrappedResolver.Capabilities.AdapterId).IsEqualTo("minimal-runtime");
    }

    [Test]
    public async Task WithAdaptersFromAssembly_SkipsAbstractAndInterfaceTypes()
    {
        var resolver = new MinimalResolver()
            .WithAdaptersFromAssembly(typeof(AbstractAdapter).Assembly);

        await Assert.That(resolver).IsNotNull();
    }

    [Test]
    public async Task WithAdaptersFromAssembly_ThrowsOnNullResolver()
    {
        IUiControlResolver? nullResolver = null;

        await Assert.That(() => nullResolver!.WithAdaptersFromAssembly(typeof(TestableAdapter).Assembly))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task WithAdaptersFromAssembly_ThrowsOnNullAssembly()
    {
        var resolver = new MinimalResolver();

        await Assert.That(() => resolver.WithAdaptersFromAssembly(null!))
            .Throws<ArgumentNullException>();
    }

    public static class SearchPickerPageDefinitions
    {
        public static UiControlDefinition HistoryOperationPicker { get; } = new(
            "HistoryOperationPicker",
            UiControlType.AutomationElement,
            "HistoryOperationPicker",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
    }

    private sealed class SearchPickerPage : UiPage
    {
        public SearchPickerPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ISearchPickerControl HistoryOperationPicker => Resolve<ISearchPickerControl>(SearchPickerPageDefinitions.HistoryOperationPicker);
    }

    private sealed class FakeResolver : IUiControlResolver
    {
        private readonly Dictionary<string, object> _controls;

        public FakeResolver(params (string LocatorValue, object Control)[] controls)
        {
            _controls = controls.ToDictionary(static entry => entry.LocatorValue, static entry => entry.Control, StringComparer.Ordinal);
        }

        public UiRuntimeCapabilities Capabilities { get; } = new("fake-runtime");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            return _controls.TryGetValue(definition.LocatorValue, out var control)
                ? (control as TControl
                    ?? throw new InvalidOperationException($"Control '{definition.LocatorValue}' is not of expected type."))
                : throw new InvalidOperationException($"Unknown control '{definition.LocatorValue}'.");
        }
    }

    private abstract class FakeControlBase : IUiControl
    {
        protected FakeControlBase(string automationId)
        {
            AutomationId = automationId;
            Name = automationId;
        }

        public string AutomationId { get; }

        public string Name { get; protected set; }

        public bool IsEnabled { get; init; } = true;
    }

    private sealed class FakeTextBoxControl : FakeControlBase, ITextBoxControl
    {
        public FakeTextBoxControl(string automationId)
            : base(automationId)
        {
            Text = string.Empty;
        }

        public string Text { get; set; }

        public void Enter(string value)
        {
            Text = value;
        }
    }

    private sealed class FakeButtonControl : FakeControlBase, IButtonControl
    {
        public FakeButtonControl(string automationId)
            : base(automationId)
        {
        }

        public int InvokeCount { get; private set; }

        public void Invoke()
        {
            InvokeCount++;
        }
    }

    private sealed class FakeComboBoxControl : FakeControlBase, IComboBoxControl
    {
        private readonly IReadOnlyList<IComboBoxItem> _items;

        public FakeComboBoxControl(string automationId, IReadOnlyList<IComboBoxItem> items)
            : base(automationId)
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

    private sealed record FakeComboBoxItem(string Text, string Name) : IComboBoxItem;

    private sealed class MinimalResolver : IUiControlResolver
    {
        public UiRuntimeCapabilities Capabilities { get; } = new("minimal-runtime");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            throw new NotSupportedException("MinimalResolver does not resolve controls directly.");
        }
    }

    public sealed class TestableAdapter : IUiControlAdapter
    {
        public bool CanResolve(Type requestedType, UiControlDefinition definition) => true;

        public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
        {
            return new FakeControl("ResolvedByTestableAdapter");
        }

        private sealed class FakeControl : IUiControl
        {
            public FakeControl(string automationId)
            {
                AutomationId = automationId;
                Name = automationId;
            }

            public string AutomationId { get; }

            public string Name { get; }

            public bool IsEnabled => true;
        }
    }

    public abstract class AbstractAdapter : IUiControlAdapter
    {
        public abstract bool CanResolve(Type requestedType, UiControlDefinition definition);

        public abstract object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver);
    }
}
