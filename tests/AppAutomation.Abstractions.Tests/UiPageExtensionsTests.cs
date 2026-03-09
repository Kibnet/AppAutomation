using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class UiPageExtensionsTests
{
    [Test]
    public async Task SelectComboItem_Throws_WhenRequestedItemIsMissing()
    {
        var combo = new FakeComboBoxControl(
            "OperationCombo",
            new[]
            {
                new FakeComboBoxItem("GCD", "GCD"),
                new FakeComboBoxItem("LCM", "LCM")
            });
        var page = new ComboPage(new FakeResolver(("OperationCombo", combo)));

        Exception? exception = null;
        try
        {
            page.SelectComboItem(static candidate => candidate.OperationCombo, "MIN");
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception is InvalidOperationException).IsEqualTo(true);
            await Assert.That(combo.SelectedIndex).IsEqualTo(-1);
        }
    }

    [Test]
    public async Task WaitUntilNameEquals_ThrowsUiOperationException_WithFailureContext()
    {
        var label = new FakeLabelControl("ResultLabel", "Actual");
        var resolver = new FakeResolver(
            [("ResultLabel", (object)label)],
            artifacts:
            [
                new UiFailureArtifact(
                    Kind: "logical-tree",
                    LogicalName: "logical-tree",
                    RelativePath: "artifacts/ui-failures/fake/logical-tree.txt",
                    ContentType: "text/plain",
                    IsRequiredByContract: true,
                    InlineTextPreview: "Window -> ResultLabel")
            ]);
        var page = new DiagnosticsPage(resolver);

        UiOperationException? exception = null;
        try
        {
            page.WaitUntilNameEquals(static candidate => candidate.ResultLabel, "Expected", timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("WaitUntilNameEquals");
            await Assert.That(exception.FailureContext.AdapterId).IsEqualTo("fake-runtime");
            await Assert.That(exception.FailureContext.PageTypeFullName).Contains(nameof(DiagnosticsPage));
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("ResultLabel");
            await Assert.That(exception.FailureContext.LocatorValue).IsEqualTo("ResultLabel");
            await Assert.That(exception.FailureContext.LocatorKind).IsEqualTo(UiLocatorKind.Name);
            await Assert.That(exception.FailureContext.LastObservedValue).IsEqualTo("Actual");
            await Assert.That(exception.FailureContext.Artifacts.Count).IsEqualTo(1);
            await Assert.That(exception.InnerException is TimeoutException).IsEqualTo(true);
        }
    }

    [Test]
    public async Task WaitUntilNameEquals_ThrowsUiOperationException_WhenControlReadFails()
    {
        var label = new ThrowingLabelControl("ResultLabel", "stale element");
        var resolver = new FakeResolver(
            [("ResultLabel", (object)label)],
            artifacts:
            [
                new UiFailureArtifact(
                    Kind: "logical-tree",
                    LogicalName: "logical-tree",
                    RelativePath: "artifacts/ui-failures/fake/logical-tree.txt",
                    ContentType: "text/plain",
                    IsRequiredByContract: true,
                    InlineTextPreview: "Window -> ResultLabel")
            ]);
        var page = new DiagnosticsPage(resolver);

        UiOperationException? exception = null;
        try
        {
            page.WaitUntilNameEquals(static candidate => candidate.ResultLabel, "Expected", timeoutMs: 250);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.OperationName).IsEqualTo("WaitUntilNameEquals");
            await Assert.That(exception.FailureContext.LastObservedValue).Contains("stale element");
            await Assert.That(exception.FailureContext.Artifacts.Count).IsEqualTo(1);
            await Assert.That(exception.Message).Contains("Operation failed before timeout");
            await Assert.That(exception.InnerException is InvalidOperationException).IsEqualTo(true);
        }
    }

    [Test]
    public async Task SelectTabItem_ByStableTabItemControl_SelectsTab()
    {
        var tabItem = new FakeTabItemControl("TasksTabItem");
        var page = new TabsPage(new FakeResolver(("TasksTabItem", tabItem)));

        page
            .SelectTabItem(static candidate => candidate.TasksTabItem)
            .WaitUntilIsSelected(static candidate => candidate.TasksTabItem);

        await Assert.That(tabItem.IsSelected).IsEqualTo(true);
    }

    public static class ComboPageDefinitions
    {
        public static UiControlDefinition OperationCombo { get; } = new(
            "OperationCombo",
            UiControlType.ComboBox,
            "OperationCombo");
    }

    public static class DiagnosticsPageDefinitions
    {
        public static UiControlDefinition ResultLabel { get; } = new(
            "ResultLabel",
            UiControlType.Label,
            "ResultLabel",
            UiLocatorKind.Name,
            FallbackToName: false);
    }

    public static class TabsPageDefinitions
    {
        public static UiControlDefinition TasksTabItem { get; } = new(
            "TasksTabItem",
            UiControlType.TabItem,
            "TasksTabItem");
    }

    private sealed class ComboPage : UiPage
    {
        public ComboPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IComboBoxControl OperationCombo => Resolve<IComboBoxControl>(ComboPageDefinitions.OperationCombo);
    }

    private sealed class DiagnosticsPage : UiPage
    {
        public DiagnosticsPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ILabelControl ResultLabel => Resolve<ILabelControl>(DiagnosticsPageDefinitions.ResultLabel);
    }

    private sealed class TabsPage : UiPage
    {
        public TabsPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ITabItemControl TasksTabItem => Resolve<ITabItemControl>(TabsPageDefinitions.TasksTabItem);
    }

    private sealed class FakeResolver : IUiControlResolver, IUiArtifactCollector
    {
        private readonly Dictionary<string, object> _controls;
        private readonly IReadOnlyList<UiFailureArtifact> _artifacts;

        public FakeResolver(params (string PropertyName, object Control)[] controls)
            : this(controls, artifacts: Array.Empty<UiFailureArtifact>())
        {
        }

        public FakeResolver((string PropertyName, object Control)[] controls, IReadOnlyList<UiFailureArtifact> artifacts)
        {
            _controls = controls.ToDictionary(static entry => entry.PropertyName, static entry => entry.Control, StringComparer.Ordinal);
            _artifacts = artifacts;
        }

        public UiRuntimeCapabilities Capabilities { get; } = new("fake-runtime");

        public ValueTask<IReadOnlyList<UiFailureArtifact>> CollectAsync(
            UiFailureContext failureContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_artifacts);
        }

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            return _controls.TryGetValue(definition.PropertyName, out var control)
                ? (control as TControl
                    ?? throw new InvalidOperationException($"Control '{definition.PropertyName}' is not of expected type."))
                : throw new InvalidOperationException($"Unknown control '{definition.PropertyName}'.");
        }
    }

    private abstract class FakeControlBase : IUiControl
    {
        protected FakeControlBase(string automationId, string name)
        {
            AutomationId = automationId;
            Name = name;
        }

        public string AutomationId { get; }

        public string Name { get; protected set; }

        public bool IsEnabled { get; init; } = true;
    }

    private sealed class FakeLabelControl : FakeControlBase, ILabelControl
    {
        public FakeLabelControl(string automationId, string text)
            : base(automationId, text)
        {
        }

        public string Text => Name;
    }

    private sealed class ThrowingLabelControl : ILabelControl
    {
        private readonly string _errorMessage;

        public ThrowingLabelControl(string automationId, string errorMessage)
        {
            AutomationId = automationId;
            _errorMessage = errorMessage;
        }

        public string AutomationId { get; }

        public string Name => throw new InvalidOperationException(_errorMessage);

        public bool IsEnabled => true;

        public string Text => throw new InvalidOperationException(_errorMessage);
    }

    private sealed class FakeComboBoxControl : FakeControlBase, IComboBoxControl
    {
        private readonly IReadOnlyList<IComboBoxItem> _items;

        public FakeComboBoxControl(string automationId, IReadOnlyList<IComboBoxItem> items)
            : base(automationId, automationId)
        {
            _items = items;
        }

        public IReadOnlyList<IComboBoxItem> Items => _items;

        public IComboBoxItem? SelectedItem => SelectedIndex >= 0 && SelectedIndex < _items.Count
            ? _items[SelectedIndex]
            : null;

        public int SelectedIndex { get; set; } = -1;

        public void Select(int index)
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

    private sealed class FakeTabItemControl : FakeControlBase, ITabItemControl
    {
        public FakeTabItemControl(string automationId)
            : base(automationId, automationId)
        {
        }

        public bool IsSelected { get; private set; }

        public void Select()
        {
            IsSelected = true;
        }
    }

    private sealed record FakeComboBoxItem(string Text, string Name) : IComboBoxItem;
}
