using System.Reflection;

namespace AppAutomation.Abstractions;

/// <summary>
/// Defines an adapter that can intercept and transform control resolution requests.
/// </summary>
/// <remarks>
/// <para>
/// Control adapters enable composition of multiple primitive controls into higher-level
/// abstractions. For example, a search picker can be composed from a text box and a combo box.
/// </para>
/// <para>
/// Adapters are checked in order when resolving controls. The first adapter that returns
/// <see langword="true"/> from <see cref="CanResolve"/> handles the resolution.
/// </para>
/// </remarks>
public interface IUiControlAdapter
{
    /// <summary>
    /// Determines whether this adapter can handle a resolution request.
    /// </summary>
    /// <param name="requestedType">The control interface type being requested.</param>
    /// <param name="definition">The control definition with locator information.</param>
    /// <returns><see langword="true"/> if this adapter can handle the request; otherwise, <see langword="false"/>.</returns>
    bool CanResolve(Type requestedType, UiControlDefinition definition);

    /// <summary>
    /// Resolves a control using this adapter's logic.
    /// </summary>
    /// <param name="requestedType">The control interface type being requested.</param>
    /// <param name="definition">The control definition with locator information.</param>
    /// <param name="innerResolver">The underlying resolver for resolving component controls.</param>
    /// <returns>The resolved control instance.</returns>
    object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver);
}

/// <summary>
/// Configuration for composing a search picker from individual UI controls.
/// </summary>
/// <remarks>
/// A search picker is a composite control that combines a search input, results list,
/// and optionally expand and apply buttons. This record specifies the locators for each component.
/// </remarks>
/// <param name="SearchInputLocator">The locator for the search text input control.</param>
/// <param name="ResultsLocator">The locator for the results combo box control.</param>
/// <param name="ApplyButtonLocator">Optional locator for an apply/submit button.</param>
/// <param name="ExpandButtonLocator">Optional locator for an expand/dropdown button.</param>
/// <param name="LocatorKind">The locator strategy for all components. Defaults to <see cref="UiLocatorKind.AutomationId"/>.</param>
/// <param name="FallbackToName">Whether components should fall back to name-based lookup. Defaults to <see langword="true"/>.</param>
public sealed record SearchPickerParts(
    string SearchInputLocator,
    string ResultsLocator,
    string? ApplyButtonLocator = null,
    string? ExpandButtonLocator = null,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true)
{
    /// <summary>
    /// Creates a <see cref="SearchPickerParts"/> configuration using automation IDs.
    /// </summary>
    /// <param name="searchInputAutomationId">The automation ID of the search input.</param>
    /// <param name="resultsAutomationId">The automation ID of the results combo box.</param>
    /// <param name="applyButtonAutomationId">Optional automation ID of the apply button.</param>
    /// <param name="expandButtonAutomationId">Optional automation ID of the expand button.</param>
    /// <returns>A configured <see cref="SearchPickerParts"/> instance.</returns>
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

/// <summary>
/// Extension methods for configuring <see cref="IUiControlResolver"/> with adapters.
/// </summary>
public static class UiControlResolverExtensions
{
    /// <summary>
    /// Wraps the resolver with custom control adapters.
    /// </summary>
    /// <param name="innerResolver">The resolver to wrap.</param>
    /// <param name="adapters">The adapters to apply, checked in order.</param>
    /// <returns>A new resolver that applies the adapters before delegating to the inner resolver.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="innerResolver"/> or <paramref name="adapters"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// var resolver = baseResolver.WithAdapters(new MyCustomAdapter());
    /// </code>
    /// </example>
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

    /// <summary>
    /// Registers a search picker composite control for a specific property.
    /// </summary>
    /// <param name="innerResolver">The resolver to extend.</param>
    /// <param name="propertyName">The name of the property that will resolve to <see cref="ISearchPickerControl"/>.</param>
    /// <param name="parts">Configuration specifying the component control locators.</param>
    /// <returns>A new resolver that handles the search picker resolution.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="propertyName"/> is empty or whitespace.</exception>
    /// <example>
    /// <code>
    /// var resolver = baseResolver.WithSearchPicker(
    ///     "CustomerPicker",
    ///     SearchPickerParts.ByAutomationIds("txtSearch", "cboResults"));
    /// </code>
    /// </example>
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

    /// <summary>
    /// Discovers and registers all <see cref="IUiControlAdapter"/> implementations from an assembly.
    /// </summary>
    /// <param name="resolver">The resolver to extend.</param>
    /// <param name="assembly">The assembly to scan for adapter types.</param>
    /// <returns>A new resolver with all discovered adapters registered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver"/> or <paramref name="assembly"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method uses reflection to find and instantiate all non-abstract classes that implement
    /// <see cref="IUiControlAdapter"/> and have a parameterless constructor.
    /// </remarks>
    public static IUiControlResolver WithAdaptersFromAssembly(
        this IUiControlResolver resolver,
        Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(assembly);

        var adapterTypes = assembly.GetTypes()
            .Where(static t => !t.IsAbstract && !t.IsInterface && typeof(IUiControlAdapter).IsAssignableFrom(t));

        var adapters = adapterTypes
            .Select(static t => (IUiControlAdapter?)Activator.CreateInstance(t))
            .Where(static adapter => adapter is not null)
            .Cast<IUiControlAdapter>()
            .ToArray();

        return resolver.WithAdapters(adapters);
    }

    /// <summary>
    /// Registers all default adapters from the AppAutomation.Abstractions assembly.
    /// </summary>
    /// <param name="resolver">The resolver to extend.</param>
    /// <returns>A new resolver with default adapters registered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver"/> is <see langword="null"/>.</exception>
    public static IUiControlResolver WithDefaultAdapters(this IUiControlResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return resolver.WithAdaptersFromAssembly(typeof(IUiControlAdapter).Assembly);
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

/// <summary>
/// An adapter that creates composite <see cref="ISearchPickerControl"/> instances from primitive controls.
/// </summary>
/// <remarks>
/// This adapter is used internally by <see cref="UiControlResolverExtensions.WithSearchPicker"/>
/// to compose search pickers from text box and combo box controls.
/// </remarks>
public sealed class SearchPickerControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly SearchPickerParts _parts;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchPickerControlAdapter"/> class.
    /// </summary>
    /// <param name="propertyName">The name of the property to intercept.</param>
    /// <param name="parts">Configuration specifying the component control locators.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="propertyName"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="parts"/> is <see langword="null"/>.</exception>
    public SearchPickerControlAdapter(string propertyName, SearchPickerParts parts)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("Property name is required.", nameof(propertyName));
        }

        _propertyName = propertyName.Trim();
        _parts = parts ?? throw new ArgumentNullException(nameof(parts));
    }

    /// <inheritdoc />
    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);

        return requestedType == typeof(ISearchPickerControl)
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
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

        public void Select(string itemText) => SelectItem(itemText);

        public void SelectItem(string itemText)
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

            _results.SelectByIndex(index.Value);
        }

        private static string Normalize(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }
    }
}
