using AppAutomation.Abstractions;
using BenchmarkDotNet.Attributes;

namespace AppAutomation.Benchmarks.Benchmarks;

/// <summary>
/// Performance benchmarks for control resolution and core framework operations.
/// These benchmarks measure the overhead of the UI automation framework itself,
/// using mock implementations to isolate framework code from actual UI interactions.
/// </summary>
/// <remarks>
/// Benchmark categories:
/// 1. UiWait polling - measures the overhead of condition polling
/// 2. Control resolution - measures resolver pattern overhead
/// 3. Adapter chain - measures control adapter wrapping overhead
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ControlResolutionBenchmarks
{
    private MockControlResolver _resolver = null!;
    private UiControlDefinition _buttonDefinition = null!;
    private UiControlDefinition _textBoxDefinition = null!;
    private UiControlDefinition _comboBoxDefinition = null!;

    [GlobalSetup]
    public void Setup()
    {
        _resolver = new MockControlResolver();
        _buttonDefinition = new UiControlDefinition(
            "TestButton",
            UiControlType.Button,
            "TestButton",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
        _textBoxDefinition = new UiControlDefinition(
            "TestTextBox",
            UiControlType.TextBox,
            "TestTextBox",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
        _comboBoxDefinition = new UiControlDefinition(
            "TestComboBox",
            UiControlType.ComboBox,
            "TestComboBox",
            UiLocatorKind.AutomationId,
            FallbackToName: false);
    }

    // ============================================================================
    // Control Resolution Benchmarks
    // ============================================================================

    /// <summary>
    /// Measures the baseline cost of resolving a simple button control.
    /// </summary>
    [Benchmark(Baseline = true)]
    public IButtonControl ResolveButtonControl()
    {
        return _resolver.Resolve<IButtonControl>(_buttonDefinition);
    }

    /// <summary>
    /// Measures the cost of resolving a text box control (includes text property overhead).
    /// </summary>
    [Benchmark]
    public ITextBoxControl ResolveTextBoxControl()
    {
        return _resolver.Resolve<ITextBoxControl>(_textBoxDefinition);
    }

    /// <summary>
    /// Measures the cost of resolving a combo box with items (includes collection overhead).
    /// </summary>
    [Benchmark]
    public IComboBoxControl ResolveComboBoxControl()
    {
        return _resolver.Resolve<IComboBoxControl>(_comboBoxDefinition);
    }

    /// <summary>
    /// Measures repeated resolution of the same control (cache efficiency).
    /// </summary>
    [Benchmark]
    public IButtonControl ResolveButtonControl_Repeated()
    {
        IButtonControl? result = null;
        for (var i = 0; i < 10; i++)
        {
            result = _resolver.Resolve<IButtonControl>(_buttonDefinition);
        }
        return result!;
    }

    // ============================================================================
    // UiWait Polling Benchmarks
    // ============================================================================

    /// <summary>
    /// Measures the overhead of UiWait.TryUntil with an immediately successful condition.
    /// This represents the best-case scenario with minimal polling.
    /// </summary>
    [Benchmark]
    public UiWaitResult<bool> UiWait_ImmediateSuccess()
    {
        return UiWait.TryUntil(
            valueFactory: static () => true,
            condition: static value => value,
            options: new UiWaitOptions
            {
                Timeout = TimeSpan.FromSeconds(1),
                PollInterval = TimeSpan.FromMilliseconds(10)
            });
    }

    /// <summary>
    /// Measures UiWait with a condition that succeeds on the second poll.
    /// </summary>
    [Benchmark]
    public UiWaitResult<int> UiWait_SecondPollSuccess()
    {
        var counter = 0;
        return UiWait.TryUntil(
            valueFactory: () => ++counter,
            condition: static value => value >= 2,
            options: new UiWaitOptions
            {
                Timeout = TimeSpan.FromSeconds(1),
                PollInterval = TimeSpan.FromMilliseconds(1)
            });
    }

    /// <summary>
    /// Measures the overhead of creating UiWaitOptions.
    /// </summary>
    [Benchmark]
    public UiWaitOptions CreateWaitOptions()
    {
        return new UiWaitOptions
        {
            Timeout = TimeSpan.FromSeconds(5),
            PollInterval = TimeSpan.FromMilliseconds(100)
        };
    }

    // ============================================================================
    // Control Definition Benchmarks
    // ============================================================================

    /// <summary>
    /// Measures the cost of creating a control definition.
    /// </summary>
    [Benchmark]
    public UiControlDefinition CreateControlDefinition()
    {
        return new UiControlDefinition(
            "DynamicControl",
            UiControlType.Button,
            "DynamicControl",
            UiLocatorKind.AutomationId,
            FallbackToName: true);
    }

    // ============================================================================
    // Mock Implementations for Benchmarking
    // ============================================================================

    /// <summary>
    /// Mock resolver that creates controls without actual UI interaction.
    /// This isolates the framework overhead from UI automation overhead.
    /// </summary>
    private sealed class MockControlResolver : IUiControlResolver
    {
        public UiRuntimeCapabilities Capabilities { get; } = new(
            AdapterId: "benchmark-mock",
            SupportsGridCellAccess: false,
            SupportsCalendarRangeSelection: false,
            SupportsTreeNodeExpansionState: false,
            SupportsRawNativeHandles: false,
            SupportsScreenshots: false);

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            return typeof(TControl) switch
            {
                var t when t == typeof(IButtonControl) => (TControl)(object)new MockButtonControl(definition.PropertyName),
                var t when t == typeof(ITextBoxControl) => (TControl)(object)new MockTextBoxControl(definition.PropertyName),
                var t when t == typeof(IComboBoxControl) => (TControl)(object)new MockComboBoxControl(definition.PropertyName),
                var t when t == typeof(ILabelControl) => (TControl)(object)new MockLabelControl(definition.PropertyName),
                _ => throw new NotSupportedException($"Control type {typeof(TControl).Name} is not supported in benchmarks.")
            };
        }
    }

    private sealed class MockButtonControl : IButtonControl
    {
        public MockButtonControl(string automationId)
        {
            AutomationId = automationId;
        }

        public string AutomationId { get; }
        public string Name => AutomationId;
        public bool IsEnabled => true;

        public void Invoke() { }
    }

    private sealed class MockTextBoxControl : ITextBoxControl
    {
        public MockTextBoxControl(string automationId)
        {
            AutomationId = automationId;
        }

        public string AutomationId { get; }
        public string Name => AutomationId;
        public bool IsEnabled => true;
        public string Text { get; set; } = string.Empty;

        public void Enter(string value) => Text = value;
    }

    private sealed class MockComboBoxControl : IComboBoxControl
    {
        private readonly IReadOnlyList<IComboBoxItem> _items;

        public MockComboBoxControl(string automationId)
        {
            AutomationId = automationId;
            _items = new[]
            {
                new MockComboBoxItem("Item 1", "Item 1"),
                new MockComboBoxItem("Item 2", "Item 2"),
                new MockComboBoxItem("Item 3", "Item 3")
            };
        }

        public string AutomationId { get; }
        public string Name => AutomationId;
        public bool IsEnabled => true;
        public IReadOnlyList<IComboBoxItem> Items => _items;
        public IComboBoxItem? SelectedItem => SelectedIndex >= 0 ? _items[SelectedIndex] : null;
        public int SelectedIndex { get; set; } = -1;

        public void SelectByIndex(int index) => SelectedIndex = index;
        public void Expand() { }
    }

    private sealed record MockComboBoxItem(string Text, string Name) : IComboBoxItem;

    private sealed class MockLabelControl : ILabelControl
    {
        public MockLabelControl(string automationId)
        {
            AutomationId = automationId;
        }

        public string AutomationId { get; }
        public string Name => AutomationId;
        public bool IsEnabled => true;
        public string Text => "Mock Label";
    }
}
