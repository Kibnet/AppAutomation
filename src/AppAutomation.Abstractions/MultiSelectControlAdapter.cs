namespace AppAutomation.Abstractions;

internal interface IMultiSelectCommittedStateControl
{
    IReadOnlyList<string> RefreshCommittedItems(bool refreshLegacy = true);

    string DescribeLastObservation();
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
    /// <summary>Optional locator of the visible committed-value part; defaults to the editor root.</summary>
    public string? DisplayValueLocator { get; init; }

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

        return new MultiSelectControl(definition.PropertyName, _parts, innerResolver, _state,
            confirmedFilter: definition.ControlType == UiControlType.ComboBoxFilter && _parts.ApplyButtonLocator is not null);
    }

    private static void ValidateParts(MultiSelectParts parts, bool allowMissingApplyButton)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.RootLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.OpenButtonLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.ItemsContainerLocator);
        if (parts.DisplayValueLocator is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(parts.DisplayValueLocator);
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

    private sealed class MultiSelectControl : IMultiSelectControl, IReadableTextControl, IMultiSelectCommittedStateControl
    {
        private readonly MultiSelectParts _parts;
        private readonly IUiControlResolver _innerResolver;
        private readonly MultiSelectState _state;
        private readonly bool _confirmedFilter;
        private IMultiSelectItemsControl? _items;
        private bool? _lastIsOpen;

        public MultiSelectControl(
            string automationId,
            MultiSelectParts parts,
            IUiControlResolver innerResolver,
            MultiSelectState state,
            bool confirmedFilter)
        {
            AutomationId = automationId;
            _parts = parts;
            _innerResolver = innerResolver;
            _state = state;
            _confirmedFilter = confirmedFilter;
        }

        public string AutomationId { get; }

        public string Name => ResolveRoot().Name;

        public bool IsEnabled => ResolveRoot().IsEnabled;

        public string Text => ReadDisplaySnapshot().Text?.Trim()
            ?? throw new InvalidOperationException(
                $"Multi-select '{AutomationId}' does not expose readable displayed value text. Configure DisplayValueLocator.");

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
                    if (!TryResolveItems(out var items))
                    {
                        throw new UiControlResolutionException(UiControlResolutionFailure.Detached,
                            $"Multi-select popup '{AutomationId}' disappeared while reading its current selection.");
                    }
                    return ReadCurrentSelection(items);
                }

                return RefreshCommittedItems(refreshLegacy: false);
            }
        }

        public bool IsOpen
        {
            get
            {
                var items = _items;
                if (items is null && !TryResolveItems(out items))
                {
                    _lastIsOpen = false;
                    _state.ObservePopupState(false);
                    return false;
                }

                var isOpen = items is IUiControlAvailability availability
                    ? availability.IsAvailable
                    : throw new NotSupportedException(
                        $"Multi-select popup '{AutomationId}' requires an availability-aware items container.");
                _lastIsOpen = isOpen;
                _state.ObservePopupState(isOpen);
                if (!isOpen)
                    _items = null;
                return isOpen;
            }
        }

        public IReadOnlyList<string> RefreshCommittedItems(bool refreshLegacy = true)
        {
            // The operation state only gates a live read; it never proves the current selection.
            if (_state.HasOpenPopup && IsOpen)
                return ReadCurrentSelection(ResolveItems());
            var snapshot = ReadClosedSelectionSnapshot();
            if (snapshot.CommittedItems is { } committed)
            {
                var items = committed.ToArray();
                _state.ObserveCommitted(items);
                return items;
            }
            if (TryReadDisplayedItems(snapshot, out var displayedItems))
            {
                _state.ObserveCommitted(displayedItems);
                return displayedItems;
            }
            if (refreshLegacy)
                return IsOpen ? ReadCurrentSelection(ResolveItems()) : ReadInitialCommittedItems();
            return _state.TryGetCommittedItems(out var committedItems)
                ? committedItems
                : throw new InvalidOperationException(
                    $"Multi-select popup '{AutomationId}' has no committed selection snapshot. "
                    + "Refresh its selection explicitly before reading it while closed.");
        }

        public string DescribeLastObservation() => $"IsOpen={_lastIsOpen?.ToString() ?? "<unobserved>"}; {_state.Describe()}";

        private UiDisplayedValueSnapshot ReadDisplaySnapshot()
        {
            var definition = CreateDisplayDefinition();
            if (_innerResolver is IComboBoxFilterDisplayValueReader reader)
                return reader.ReadDisplayedValueSnapshot(definition);
            return new(_innerResolver.Resolve<IUiControl>(definition) switch
            {
                IReadableTextControl readable => readable.Text,
                ILabelControl label => label.Text,
                ITextBoxControl input => input.Text,
                _ => null
            });
        }

        private UiControlDefinition CreateDisplayDefinition() =>
            CreateDefinition("DisplayValue", UiControlType.AutomationElement,
                _parts.DisplayValueLocator ?? _parts.RootLocator);

        private UiDisplayedValueSnapshot ReadClosedSelectionSnapshot()
        {
            if (_innerResolver is not IComboBoxFilterDisplayValueReader reader)
                return _confirmedFilter ? throw UnreadableCommittedSelection() : ReadDisplaySnapshot();

            var separateDisplay = _parts.DisplayValueLocator is { } displayLocator
                && !string.Equals(displayLocator, _parts.RootLocator, StringComparison.Ordinal);
            var snapshot = reader.ReadDisplayedValueSnapshot(
                CreateDefinition("Root", UiControlType.AutomationElement, _parts.RootLocator) with
                {
                    ValueReadKind = _confirmedFilter ? UiValueReadKind.ConfirmedFilterSelection
                        : separateDisplay ? UiValueReadKind.CommittedSelection : UiValueReadKind.CommittedSelectionOrDisplay
                });
            _state.ObserveSelectionSource(snapshot.SourceIdentity);
            if (_confirmedFilter && snapshot.CommittedItems is null) throw UnreadableCommittedSelection();
            return snapshot.CommittedItems is not null || !separateDisplay
                ? snapshot : ReadDisplaySnapshot();
        }

        private NotSupportedException UnreadableCommittedSelection() =>
            new($"Multi-select '{_parts.RootLocator}' does not expose its complete committed selection.");

        private bool TryReadDisplayedItems(UiDisplayedValueSnapshot snapshot, out string[] items)
        {
            items = [];
            _state.ObserveDisplaySource(snapshot.SourceIdentity);
            var text = snapshot.Text?.Trim();
            if (text is null)
                return false;
            if (text.Length == 0)
                return true;
            if (!string.IsNullOrWhiteSpace(snapshot.PlaceholderText)
                && string.Equals(text, snapshot.PlaceholderText.Trim(), StringComparison.Ordinal))
            {
                // A real item may have the same caption as the hint. That case requires an explicit selection read.
                return snapshot.SourceIdentity is not null
                    && _state.TryGetAvailableItems(out var candidates)
                    && !candidates.Contains(text, StringComparer.OrdinalIgnoreCase);
            }
            char[] separators = [',', ';'];
            if (text.IndexOfAny(separators) < 0)
            {
                items = [text];
                return true;
            }
            // A caption containing delimiters is not proof of an item boundary.
            if (!_state.TryGetAvailableItems(out var available)
                || available.Any(item => item.IndexOfAny(separators) >= 0))
                return false;
            items = text.Split(separators, StringSplitOptions.TrimEntries);
            return items.All(item => available.Contains(item, StringComparer.OrdinalIgnoreCase))
                && items.Distinct(StringComparer.OrdinalIgnoreCase).Count() == items.Length;
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
            if (_innerResolver is IComboBoxFilterDisplayValueReader reader)
                _state.ObserveDisplaySource(reader.ReadDisplayedValueSourceIdentity(CreateDisplayDefinition()));
            var availableItems = items.SetSelectedItemsAndGetAvailableItems(values);
            _state.ObserveItemsAfterSelection(availableItems);
            if (string.IsNullOrWhiteSpace(_parts.ApplyButtonLocator))
            {
                _state.InvalidateCommittedAfterApply();
                _items = null;
            }
        }

        public void Apply()
        {
            _items = null;
            if (!string.IsNullOrWhiteSpace(_parts.ApplyButtonLocator))
            {
                ResolveButton("ApplyButton", _parts.ApplyButtonLocator).Invoke();
            }
            else if (IsOpen)
            {
                ResolveButton("OpenButton", _parts.OpenButtonLocator).Invoke();
            }

            _state.InvalidateCommittedAfterApply();
        }

        public void Cancel()
        {
            _items = null;
            if (string.IsNullOrWhiteSpace(_parts.CancelButtonLocator))
            {
                throw new NotSupportedException(
                    $"Multi-select popup '{AutomationId}' does not have a configured cancel button.");
            }

            ResolveButton("CancelButton", _parts.CancelButtonLocator).Invoke();
            _state.DiscardPending();
        }

        private string[] ReadInitialCommittedItems()
        {
            if (!string.IsNullOrWhiteSpace(_parts.ApplyButtonLocator)
                && string.IsNullOrWhiteSpace(_parts.CancelButtonLocator))
                throw UnreadableCommittedSelection();

            Open();
            var items = ResolveItems();
            var selectedItems = ReadCurrentSelection(items);

            if (IsOpen)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(_parts.CancelButtonLocator))
                    {
                        Cancel();
                    }
                    else
                    {
                        ResolveButton("OpenButton", _parts.OpenButtonLocator).Invoke();
                        _state.DiscardPending();
                    }
                }
                catch (UiControlResolutionException exception) when (exception.IsTransient && !IsOpen)
                {
                    // The popup closed after the snapshot; this read did not change its selection.
                    _state.DiscardPending();
                }
            }

            _state.ObserveCommitted(selectedItems);
            return selectedItems;
        }

        private string[] ReadCurrentSelection(IMultiSelectItemsControl items)
        {
            var availableItems = _state.TryGetAvailableItems(out var cachedItems)
                ? cachedItems
                : items.Items;
            var selectedItems = items.SelectedItems.ToArray();
            _state.Observe(availableItems, selectedItems);
            return selectedItems;
        }

        private IUiControl ResolveRoot()
        {
            return _innerResolver.Resolve<IUiControl>(
                CreateDefinition("Root", UiControlType.AutomationElement, _parts.RootLocator));
        }

        private IMultiSelectItemsControl ResolveItems()
        {
            return _items ??= _innerResolver.Resolve<IMultiSelectItemsControl>(
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
            catch (UiControlResolutionException exception) when (exception.IsTransient)
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
        private bool _hasAvailableItems;
        private bool _hasCommittedItems;
        private bool _refreshCommittedOnNextObservation;
        private bool _hasOpenPopup;
        private string[]? _observedSelection;
        private string? _displaySourceIdentity;
        private string? _committedSelectionSourceIdentity;

        public bool HasOpenPopup
        {
            get
            {
                lock (_sync)
                {
                    return _hasOpenPopup;
                }
            }
        }

        public void ObservePopupState(bool isOpen)
        {
            lock (_sync)
            {
                _hasOpenPopup = isOpen;
            }
        }

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

        public bool TryGetAvailableItems(out IReadOnlyList<string> items)
        {
            lock (_sync)
            {
                items = _availableItems.ToArray();
                return _hasAvailableItems;
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
                _hasOpenPopup = true;
                _hasAvailableItems = false;
                _observedSelection = null;
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
                _observedSelection = observedSelection;
                if (!_hasCommittedItems || _refreshCommittedOnNextObservation)
                {
                    _committedItems = observedSelection;
                    _hasCommittedItems = true;
                }

                _refreshCommittedOnNextObservation = false;
            }
        }

        public void ObserveItemsAfterSelection(IEnumerable<string> availableItems)
        {
            lock (_sync)
            {
                _hasOpenPopup = true;
                _availableItems = availableItems.ToArray();
                _hasAvailableItems = true;
                _refreshCommittedOnNextObservation = false;
            }
        }

        public void ObserveCommitted(string[] items)
        {
            lock (_sync)
            {
                var observed = items.ToArray();
                _observedSelection = observed;
                _committedItems = observed;
                _hasCommittedItems = true;
            }
        }

        public void ObserveSelectionSource(string? identity)
        {
            lock (_sync)
            {
                if (identity is null) return;
                InvalidateIfReplaced(_committedSelectionSourceIdentity, identity);
                _committedSelectionSourceIdentity = identity;
            }
        }

        public void ObserveDisplaySource(string? identity)
        {
            lock (_sync)
            {
                if (identity is null)
                    return;
                InvalidateIfReplaced(_displaySourceIdentity, identity);
                _displaySourceIdentity = identity;
            }
        }

        private void InvalidateIfReplaced(string? prior, string current)
        {
            if (prior is null || prior == current) return;
            _hasAvailableItems = false;
            _hasCommittedItems = false;
            _observedSelection = null;
        }

        public string Describe()
        {
            lock (_sync)
            {
                static string Format(string[]? values) => values is null
                    ? "<unobserved>" : $"[{string.Join(", ", values.Select(value => $"'{value}'"))}]";
                return $"Available={Format(_hasAvailableItems ? _availableItems : null)}; Selected={Format(_observedSelection)}";
            }
        }

        public void InvalidateCommittedAfterApply()
        {
            lock (_sync)
            {
                _hasOpenPopup = false;
                _hasCommittedItems = false;
                _observedSelection = null;
                _refreshCommittedOnNextObservation = false;
            }
        }

        public void DiscardPending()
        {
            lock (_sync)
            {
                _hasOpenPopup = false;
                _refreshCommittedOnNextObservation = false;
            }
        }
    }
}
