namespace AppAutomation.Abstractions;

internal interface IMultiSelectCommittedStateControl
{
    bool TryGetCommittedItems(out IReadOnlyList<string> items);
}

/// <summary>
/// Configuration for composing a multi-select popup from real primitive controls.
/// </summary>
/// <param name="RootLocator">The locator for the multi-select editor root.</param>
/// <param name="OpenButtonLocator">The locator for the button that opens the popup.</param>
/// <param name="ItemsContainerLocator">The locator for the container holding checkbox items.</param>
/// <param name="ApplyButtonLocator">The locator for the apply button.</param>
/// <param name="CancelButtonLocator">The optional locator for the cancel button.</param>
/// <param name="LocatorKind">The locator strategy used by all parts.</param>
/// <param name="FallbackToName">Whether part resolution may fall back to name.</param>
/// <param name="ItemsKind">The primitive control kind used by the selectable-items surface.</param>
public sealed record MultiSelectParts(
    string RootLocator,
    string OpenButtonLocator,
    string ItemsContainerLocator,
    string? ApplyButtonLocator,
    string? CancelButtonLocator = null,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true,
    MultiSelectItemsKind ItemsKind = MultiSelectItemsKind.ListBox)
{
    /// <summary>
    /// Creates a multi-select parts configuration that uses automation IDs.
    /// </summary>
    public static MultiSelectParts ByAutomationIds(
        string rootAutomationId,
        string openButtonAutomationId,
        string itemsContainerAutomationId,
        string applyButtonAutomationId,
        string? cancelButtonAutomationId = null,
        MultiSelectItemsKind itemsKind = MultiSelectItemsKind.ListBox)
    {
        return new MultiSelectParts(
            rootAutomationId,
            openButtonAutomationId,
            itemsContainerAutomationId,
            applyButtonAutomationId,
            cancelButtonAutomationId,
            ItemsKind: itemsKind);
    }
}

/// <summary>
/// Specifies the primitive control kind used by a composed multi-select items surface.
/// </summary>
public enum MultiSelectItemsKind
{
    /// <summary>
    /// Items are exposed through a list-box-like surface.
    /// </summary>
    ListBox = 0,

    /// <summary>
    /// Items are exposed through a combo-box-like surface that supports zero or one selected value.
    /// </summary>
    ComboBox = 1
}

public static partial class UiControlResolverExtensions
{
    /// <summary>
    /// Registers a multi-select popup composite control for a specific page property.
    /// </summary>
    public static IUiControlResolver WithMultiSelect(
        this IUiControlResolver innerResolver,
        string propertyName,
        MultiSelectParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);

        return innerResolver.WithAdapters(new MultiSelectControlAdapter(propertyName, parts));
    }
}

/// <summary>
/// Composes an <see cref="IMultiSelectControl"/> from a popup root, checkbox-items surface, and buttons.
/// </summary>
public sealed class MultiSelectControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly MultiSelectParts _parts;
    private readonly MultiSelectState _state = new();

    /// <summary>
    /// Initializes a new multi-select composite adapter.
    /// </summary>
    public MultiSelectControlAdapter(string propertyName, MultiSelectParts parts)
        : this(propertyName, parts, allowMissingApplyButton: false)
    {
    }

    internal MultiSelectControlAdapter(
        string propertyName,
        MultiSelectParts parts,
        bool allowMissingApplyButton)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        _propertyName = propertyName.Trim();
        _parts = parts ?? throw new ArgumentNullException(nameof(parts));
        ValidateParts(parts, allowMissingApplyButton);
    }

    /// <inheritdoc />
    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);

        return requestedType == typeof(IMultiSelectControl)
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        return new MultiSelectControl(definition.PropertyName, _parts, innerResolver, _state);
    }

    private static void ValidateParts(MultiSelectParts parts, bool allowMissingApplyButton)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.RootLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.OpenButtonLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.ItemsContainerLocator);
        if (!allowMissingApplyButton)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parts.ApplyButtonLocator);
        }
        else if (parts.ApplyButtonLocator is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parts.ApplyButtonLocator);
        }

        if (parts.CancelButtonLocator is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parts.CancelButtonLocator);
        }
    }

    private sealed class MultiSelectControl : IMultiSelectControl, IMultiSelectCommittedStateControl
    {
        private readonly MultiSelectParts _parts;
        private readonly IUiControlResolver _innerResolver;
        private readonly MultiSelectState _state;

        public MultiSelectControl(
            string automationId,
            MultiSelectParts parts,
            IUiControlResolver innerResolver,
            MultiSelectState state)
        {
            AutomationId = automationId;
            _parts = parts;
            _innerResolver = innerResolver;
            _state = state;
        }

        public string AutomationId { get; }

        public string Name => ResolveRoot().Name;

        public bool IsEnabled => ResolveRoot().IsEnabled;

        public IReadOnlyList<string> Items
        {
            get
            {
                if (_state.TryGetAvailableItems(out var cachedItems))
                {
                    return cachedItems;
                }

                if (!TryResolveItems(out var items))
                {
                    return _state.AvailableItems;
                }

                var availableItems = items.Items.ToArray();
                _state.Observe(availableItems, items.SelectedItems);
                return availableItems;
            }
        }

        public IReadOnlyList<string> SelectedItems
        {
            get
            {
                if (IsOpen)
                {
                    if (_state.TryGetPendingItems(out var pendingItems))
                    {
                        return pendingItems;
                    }

                    if (!TryResolveItems(out var items))
                    {
                        return _state.CommittedItems;
                    }

                    var selectedItems = items.SelectedItems.ToArray();
                    _state.Observe(items.Items, selectedItems);
                    return selectedItems;
                }

                return _state.CommittedItems;
            }
        }

        public bool IsOpen
        {
            get
            {
                if (!TryResolveItems(out var items))
                {
                    return false;
                }

                return items is IUiControlAvailability availability
                    ? availability.IsAvailable
                    : throw new NotSupportedException(
                        $"Multi-select popup '{AutomationId}' requires an availability-aware items container.");
            }
        }

        public bool TryGetCommittedItems(out IReadOnlyList<string> items)
        {
            return _state.TryGetCommittedItems(out items);
        }

        public void Open()
        {
            if (IsOpen)
            {
                return;
            }

            ResolveButton("OpenButton", _parts.OpenButtonLocator).Invoke();
            _state.BeginOpen();
        }

        public void SetSelectedItems(IReadOnlyCollection<string> values)
        {
            ArgumentNullException.ThrowIfNull(values);

            var items = ResolveItems();
            var availableItems = items.SetSelectedItemsAndGetAvailableItems(values);
            _state.SetPending(availableItems, values);
            if (string.IsNullOrWhiteSpace(_parts.ApplyButtonLocator))
            {
                _state.CommitPending();
            }
        }

        public void Apply()
        {
            if (!string.IsNullOrWhiteSpace(_parts.ApplyButtonLocator))
            {
                ResolveButton("ApplyButton", _parts.ApplyButtonLocator).Invoke();
            }

            _state.CommitPending();
        }

        public void Cancel()
        {
            if (string.IsNullOrWhiteSpace(_parts.CancelButtonLocator))
            {
                throw new NotSupportedException(
                    $"Multi-select popup '{AutomationId}' does not have a configured cancel button.");
            }

            ResolveButton("CancelButton", _parts.CancelButtonLocator).Invoke();
            _state.DiscardPending();
        }

        private IUiControl ResolveRoot()
        {
            return _innerResolver.Resolve<IUiControl>(
                CreateDefinition("Root", UiControlType.AutomationElement, _parts.RootLocator));
        }

        private IMultiSelectItemsControl ResolveItems()
        {
            return _innerResolver.Resolve<IMultiSelectItemsControl>(
                CreateDefinition(
                    "Items",
                    _parts.ItemsKind == MultiSelectItemsKind.ComboBox
                        ? UiControlType.ComboBox
                        : UiControlType.ListBox,
                    _parts.ItemsContainerLocator));
        }

        private bool TryResolveItems(out IMultiSelectItemsControl items)
        {
            try
            {
                items = ResolveItems();
                return true;
            }
            catch
            {
                items = null!;
                return false;
            }
        }

        private IButtonControl ResolveButton(string suffix, string locatorValue)
        {
            return _innerResolver.Resolve<IButtonControl>(
                CreateDefinition(suffix, UiControlType.Button, locatorValue));
        }

        private UiControlDefinition CreateDefinition(string suffix, UiControlType controlType, string locatorValue)
        {
            return new UiControlDefinition(
                $"{AutomationId}{suffix}",
                controlType,
                locatorValue,
                _parts.LocatorKind,
                _parts.FallbackToName);
        }

    }

    private sealed class MultiSelectState
    {
        private readonly object _sync = new();
        private string[] _availableItems = [];
        private string[] _committedItems = [];
        private string[] _pendingItems = [];
        private bool _hasAvailableItems;
        private bool _hasCommittedItems;
        private bool _hasPendingItems;
        private bool _refreshCommittedOnNextObservation;

        public IReadOnlyList<string> AvailableItems
        {
            get
            {
                lock (_sync)
                {
                    return _availableItems.ToArray();
                }
            }
        }

        public IReadOnlyList<string> CommittedItems
        {
            get
            {
                lock (_sync)
                {
                    return _committedItems.ToArray();
                }
            }
        }

        public bool TryGetAvailableItems(out IReadOnlyList<string> items)
        {
            lock (_sync)
            {
                items = _availableItems.ToArray();
                return _hasAvailableItems;
            }
        }

        public bool TryGetPendingItems(out IReadOnlyList<string> items)
        {
            lock (_sync)
            {
                items = _pendingItems.ToArray();
                return _hasPendingItems;
            }
        }

        public bool TryGetCommittedItems(out IReadOnlyList<string> items)
        {
            lock (_sync)
            {
                items = _committedItems.ToArray();
                return _hasCommittedItems;
            }
        }

        public void BeginOpen()
        {
            lock (_sync)
            {
                _hasAvailableItems = false;
                _hasPendingItems = false;
                _pendingItems = [];
                _refreshCommittedOnNextObservation = true;
            }
        }

        public void Observe(IEnumerable<string> availableItems, IEnumerable<string> selectedItems)
        {
            lock (_sync)
            {
                _availableItems = availableItems.ToArray();
                _hasAvailableItems = true;
                var observedSelection = selectedItems.ToArray();
                if (!_hasCommittedItems || _refreshCommittedOnNextObservation)
                {
                    _committedItems = observedSelection;
                    _hasCommittedItems = true;
                }

                _pendingItems = observedSelection;
                _hasPendingItems = true;
                _refreshCommittedOnNextObservation = false;
            }
        }

        public void SetPending(IEnumerable<string> availableItems, IEnumerable<string> selectedItems)
        {
            lock (_sync)
            {
                _availableItems = availableItems.ToArray();
                _hasAvailableItems = true;
                _pendingItems = selectedItems.ToArray();
                _hasPendingItems = true;
                _refreshCommittedOnNextObservation = false;
            }
        }

        public void CommitPending()
        {
            lock (_sync)
            {
                _committedItems = _pendingItems.ToArray();
                _hasCommittedItems = true;
                _hasPendingItems = false;
                _refreshCommittedOnNextObservation = false;
            }
        }

        public void DiscardPending()
        {
            lock (_sync)
            {
                _pendingItems = _committedItems.ToArray();
                _hasPendingItems = false;
                _refreshCommittedOnNextObservation = false;
            }
        }
    }
}
