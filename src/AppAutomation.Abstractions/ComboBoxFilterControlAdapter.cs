namespace AppAutomation.Abstractions;

/// <summary>
/// Configuration for composing a logical ComboBoxEditor-style filter from real popup parts.
/// </summary>
/// <param name="RootLocator">The locator for the logical filter root.</param>
/// <param name="OpenButtonLocator">The locator for the popup open button.</param>
/// <param name="ItemsContainerLocator">The locator for the selectable-items surface.</param>
/// <param name="ApplyButtonLocator">The optional locator for the apply button.</param>
/// <param name="CancelButtonLocator">The optional locator for the cancel button.</param>
/// <param name="LocatorKind">The locator strategy used by all parts.</param>
/// <param name="FallbackToName">Whether part resolution may fall back to name.</param>
/// <param name="ItemsKind">The primitive control kind used by the selectable-items surface.</param>
/// <param name="DisplayValueLocator">Optional locator of the visible committed-value text within the editor.</param>
public sealed record ComboBoxFilterParts(
    string RootLocator,
    string OpenButtonLocator,
    string ItemsContainerLocator,
    string? ApplyButtonLocator = null,
    string? CancelButtonLocator = null,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true,
    MultiSelectItemsKind ItemsKind = MultiSelectItemsKind.ListBox,
    string? DisplayValueLocator = null)
{
    /// <summary>
    /// Creates a filter parts configuration that uses automation IDs.
    /// </summary>
    public static ComboBoxFilterParts ByAutomationIds(
        string rootAutomationId,
        string openButtonAutomationId,
        string itemsContainerAutomationId,
        string? applyButtonAutomationId = null,
        string? cancelButtonAutomationId = null,
        MultiSelectItemsKind itemsKind = MultiSelectItemsKind.ListBox,
        string? displayValueAutomationId = null)
    {
        return new ComboBoxFilterParts(
            rootAutomationId,
            openButtonAutomationId,
            itemsContainerAutomationId,
            applyButtonAutomationId,
            cancelButtonAutomationId,
            ItemsKind: itemsKind,
            DisplayValueLocator: displayValueAutomationId);
    }

    internal MultiSelectParts ToMultiSelectParts()
    {
        return new MultiSelectParts(
            RootLocator,
            OpenButtonLocator,
            ItemsContainerLocator,
            ApplyButtonLocator,
            CancelButtonLocator,
            LocatorKind,
            FallbackToName,
            ItemsKind)
        { DisplayValueLocator = DisplayValueLocator };
    }
}

public static partial class UiControlResolverExtensions
{
    /// <summary>
    /// Registers one logical ComboBoxEditor-style filter for a page property.
    /// </summary>
    public static IUiControlResolver WithComboBoxFilter(
        this IUiControlResolver innerResolver,
        string propertyName,
        ComboBoxFilterParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);

        return innerResolver.WithAdapters(new ComboBoxFilterControlAdapter(propertyName, parts));
    }
}

/// <summary>
/// Reads the visible committed-value caption of a configured composite-editor part without
/// changing the text semantics of ordinary automation elements.
/// </summary>
public interface IComboBoxFilterDisplayValueReader
{
    /// <summary>Reads the unique visible value exposed by the configured part.</summary>
    string? ReadDisplayedValue(UiControlDefinition definition);

    /// <summary>Reads the caption with its published input hint and live UI-source identity.</summary>
    UiDisplayedValueSnapshot ReadDisplayedValueSnapshot(UiControlDefinition definition) =>
        new(ReadDisplayedValue(definition));

    /// <summary>Reads only the live UI-source identity, without inspecting its caption or popup contents.</summary>
    string? ReadDisplayedValueSourceIdentity(UiControlDefinition definition) => null;
}

/// <summary>UI-only display evidence. A hint is not an item; identity invalidates observations after source replacement.</summary>
public sealed record UiDisplayedValueSnapshot(string? Text, string? PlaceholderText = null, string? SourceIdentity = null)
{
    /// <summary>The complete committed selection when the provider can read it, or null when unavailable.
    /// An empty list is a confirmed empty selection, independent of the displayed caption.</summary>
    public IReadOnlyList<string>? CommittedItems { get; init; }
}

/// <summary>
/// Composes an <see cref="IComboBoxFilterControl"/> while reusing the exact-set popup lifecycle.
/// </summary>
public sealed class ComboBoxFilterControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly MultiSelectControlAdapter _innerAdapter;

    /// <summary>
    /// Initializes a logical ComboBoxEditor-style filter adapter.
    /// </summary>
    public ComboBoxFilterControlAdapter(string propertyName, ComboBoxFilterParts parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);

        _propertyName = propertyName.Trim();
        _innerAdapter = new MultiSelectControlAdapter(
            _propertyName,
            parts.ToMultiSelectParts(),
            allowMissingApplyButton: true);
    }

    /// <inheritdoc />
    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);

        return requestedType == typeof(IComboBoxFilterControl)
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        var inner = (IMultiSelectControl)_innerAdapter.Resolve(
            typeof(IMultiSelectControl),
            definition,
            innerResolver);
        return new ComboBoxFilterControl(inner);
    }

    private sealed class ComboBoxFilterControl : IComboBoxFilterControl, IReadableTextControl, IMultiSelectCommittedStateControl
    {
        private readonly IMultiSelectControl _inner;

        public ComboBoxFilterControl(IMultiSelectControl inner)
        {
            _inner = inner;
        }

        public string AutomationId => _inner.AutomationId;

        public string Name => _inner.Name;

        public string Text => ((IReadableTextControl)_inner).Text;

        public bool IsEnabled => _inner.IsEnabled;

        public IReadOnlyList<string> Items => _inner.Items;

        public IReadOnlyList<string> SelectedItems => _inner.SelectedItems;

        public bool IsOpen => _inner.IsOpen;

        public IReadOnlyList<string> RefreshCommittedItems(bool refreshLegacy = true) =>
            _inner is IMultiSelectCommittedStateControl committedState
                ? committedState.RefreshCommittedItems(refreshLegacy)
                : _inner.SelectedItems;

        public string DescribeLastObservation() => ((IMultiSelectCommittedStateControl)_inner).DescribeLastObservation();

        public void Open() => _inner.Open();

        public void SetSelectedItems(IReadOnlyCollection<string> values) => _inner.SetSelectedItems(values);

        public void Apply() => _inner.Apply();

        public void Cancel() => _inner.Cancel();
    }
}
