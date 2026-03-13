namespace AppAutomation.Abstractions;

public interface IUiControlAdapter
{
    bool CanResolve(Type requestedType, UiControlDefinition definition);

    object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver);
}

public sealed record SearchPickerParts(
    string SearchInputLocator,
    string ResultsLocator,
    string? ApplyButtonLocator = null,
    string? ExpandButtonLocator = null,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true)
{
    public static SearchPickerParts ByAutomationIds(
        string searchInputAutomationId,
        string resultsAutomationId,
        string? applyButtonAutomationId = null,
        string? expandButtonAutomationId = null)
    {
        return new SearchPickerParts(
            searchInputAutomationId,
            resultsAutomationId,
            applyButtonAutomationId,
            expandButtonAutomationId);
    }
}

public static class UiControlResolverExtensions
{
    public static IUiControlResolver WithAdapters(this IUiControlResolver innerResolver, params IUiControlAdapter[] adapters)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentNullException.ThrowIfNull(adapters);

        var effectiveAdapters = adapters
            .Where(static adapter => adapter is not null)
            .ToArray();

        return effectiveAdapters.Length == 0
            ? innerResolver
            : new AdapterAwareUiControlResolver(innerResolver, effectiveAdapters);
    }

    public static IUiControlResolver WithSearchPicker(
        this IUiControlResolver innerResolver,
        string propertyName,
        SearchPickerParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);

        return innerResolver.WithAdapters(new SearchPickerControlAdapter(propertyName, parts));
    }

    private sealed class AdapterAwareUiControlResolver : IUiControlResolver
    {
        private readonly IUiControlResolver _innerResolver;
        private readonly IReadOnlyList<IUiControlAdapter> _adapters;

        public AdapterAwareUiControlResolver(IUiControlResolver innerResolver, IReadOnlyList<IUiControlAdapter> adapters)
        {
            _innerResolver = innerResolver;
            _adapters = adapters;
        }

        public UiRuntimeCapabilities Capabilities => _innerResolver.Capabilities;

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            ArgumentNullException.ThrowIfNull(definition);

            foreach (var adapter in _adapters)
            {
                if (!adapter.CanResolve(typeof(TControl), definition))
                {
                    continue;
                }

                var resolved = adapter.Resolve(typeof(TControl), definition, _innerResolver);
                return resolved as TControl
                    ?? throw new InvalidOperationException(
                        $"Adapter '{adapter.GetType().FullName}' resolved '{definition.PropertyName}' to '{resolved.GetType().FullName}', which is incompatible with '{typeof(TControl).FullName}'.");
            }

            return _innerResolver.Resolve<TControl>(definition);
        }
    }
}

public sealed class SearchPickerControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly SearchPickerParts _parts;

    public SearchPickerControlAdapter(string propertyName, SearchPickerParts parts)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("Property name is required.", nameof(propertyName));
        }

        _propertyName = propertyName.Trim();
        _parts = parts ?? throw new ArgumentNullException(nameof(parts));
    }

    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);

        return requestedType == typeof(ISearchPickerControl)
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        var searchInput = innerResolver.Resolve<ITextBoxControl>(CreateDefinition("SearchInput", UiControlType.TextBox, _parts.SearchInputLocator));
        var results = innerResolver.Resolve<IComboBoxControl>(CreateDefinition("Results", UiControlType.ComboBox, _parts.ResultsLocator));
        var applyButton = string.IsNullOrWhiteSpace(_parts.ApplyButtonLocator)
            ? null
            : innerResolver.Resolve<IButtonControl>(CreateDefinition("ApplyButton", UiControlType.Button, _parts.ApplyButtonLocator));
        var expandButton = string.IsNullOrWhiteSpace(_parts.ExpandButtonLocator)
            ? null
            : innerResolver.Resolve<IButtonControl>(CreateDefinition("ExpandButton", UiControlType.Button, _parts.ExpandButtonLocator));

        return new SearchPickerControl(definition.PropertyName, searchInput, results, applyButton, expandButton);
    }

    private UiControlDefinition CreateDefinition(string suffix, UiControlType controlType, string locatorValue)
    {
        return new UiControlDefinition(
            $"{_propertyName}{suffix}",
            controlType,
            locatorValue,
            _parts.LocatorKind,
            _parts.FallbackToName);
    }

    private sealed class SearchPickerControl : ISearchPickerControl
    {
        private readonly ITextBoxControl _searchInput;
        private readonly IComboBoxControl _results;
        private readonly IButtonControl? _applyButton;
        private readonly IButtonControl? _expandButton;

        public SearchPickerControl(
            string automationId,
            ITextBoxControl searchInput,
            IComboBoxControl results,
            IButtonControl? applyButton,
            IButtonControl? expandButton)
        {
            AutomationId = automationId;
            _searchInput = searchInput;
            _results = results;
            _applyButton = applyButton;
            _expandButton = expandButton;
        }

        public string AutomationId { get; }

        public string Name => _results.Name;

        public bool IsEnabled =>
            _searchInput.IsEnabled
            && _results.IsEnabled
            && (_applyButton?.IsEnabled ?? true)
            && (_expandButton?.IsEnabled ?? true);

        public string SearchText => _searchInput.Text;

        public string? SelectedItemText => _results.SelectedItem?.Text ?? _results.SelectedItem?.Name;

        public IReadOnlyList<string> Items =>
            _results.Items.Select(static item => item.Text ?? item.Name).ToArray();

        public void Search(string value)
        {
            _searchInput.Enter(value);
            _applyButton?.Invoke();
        }

        public void Expand()
        {
            if (_expandButton is not null)
            {
                _expandButton.Invoke();
                return;
            }

            _results.Expand();
        }

        public void Select(string itemText)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemText);

            Expand();

            var normalizedTarget = Normalize(itemText);
            var index = _results.Items
                .Select((item, candidateIndex) => (Item: item, Index: candidateIndex))
                .Where(candidate =>
                    string.Equals(Normalize(candidate.Item.Text), normalizedTarget, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Normalize(candidate.Item.Name), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                .Select(static candidate => (int?)candidate.Index)
                .FirstOrDefault();

            if (index is null)
            {
                throw new InvalidOperationException($"Search picker item '{itemText}' was not found.");
            }

            _results.Select(index.Value);
        }

        private static string Normalize(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }
    }
}
