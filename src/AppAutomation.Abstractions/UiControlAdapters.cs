using System.Globalization;
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
/// <param name="ResultsKind">The primitive control kind used by the results surface.</param>
/// <param name="SearchInputLocator">The locator for the search text input control.</param>
/// <param name="ResultsLocator">The locator for the results control.</param>
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
    bool FallbackToName = true,
    SearchPickerResultsKind ResultsKind = SearchPickerResultsKind.ComboBox)
{
    /// <summary>
    /// Creates a <see cref="SearchPickerParts"/> configuration using automation IDs.
    /// </summary>
    /// <param name="searchInputAutomationId">The automation ID of the search input.</param>
    /// <param name="resultsAutomationId">The automation ID of the results control.</param>
    /// <param name="applyButtonAutomationId">Optional automation ID of the apply button.</param>
    /// <param name="expandButtonAutomationId">Optional automation ID of the expand button.</param>
    /// <param name="resultsKind">The primitive results control kind.</param>
    /// <returns>A configured <see cref="SearchPickerParts"/> instance.</returns>
    public static SearchPickerParts ByAutomationIds(
        string searchInputAutomationId,
        string resultsAutomationId,
        string? applyButtonAutomationId = null,
        string? expandButtonAutomationId = null,
        SearchPickerResultsKind resultsKind = SearchPickerResultsKind.ComboBox)
    {
        return new SearchPickerParts(
            searchInputAutomationId,
            resultsAutomationId,
            applyButtonAutomationId,
            expandButtonAutomationId,
            ResultsKind: resultsKind);
    }
}

/// <summary>
/// Specifies the primitive control kind used by a composed search-picker results surface.
/// </summary>
public enum SearchPickerResultsKind
{
    /// <summary>
    /// Results are exposed through a combo-box-like surface.
    /// </summary>
    ComboBox = 0,

    /// <summary>
    /// Results are exposed through a list-box-like surface.
    /// </summary>
    ListBox = 1
}

/// <summary>
/// Configuration for composing a date range popup filter from individual UI controls.
/// </summary>
/// <param name="FromLocator">The locator for the lower-bound date editor.</param>
/// <param name="ToLocator">The locator for the upper-bound date editor.</param>
/// <param name="ApplyButtonLocator">The locator for the apply button.</param>
/// <param name="CancelButtonLocator">The locator for the cancel button.</param>
/// <param name="OpenButtonLocator">Optional locator for the popup open trigger.</param>
/// <param name="EditorKind">The primitive editor kind used by both date endpoints.</param>
/// <param name="LocatorKind">The locator strategy for all components. Defaults to <see cref="UiLocatorKind.AutomationId"/>.</param>
/// <param name="FallbackToName">Whether components should fall back to name-based lookup. Defaults to <see langword="true"/>.</param>
public sealed record DateRangeFilterParts(
    string FromLocator,
    string ToLocator,
    string ApplyButtonLocator,
    string CancelButtonLocator,
    string? OpenButtonLocator = null,
    FilterValueEditorKind EditorKind = FilterValueEditorKind.DateTimePicker,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true)
{
    /// <summary>
    /// Creates a <see cref="DateRangeFilterParts"/> configuration using automation IDs.
    /// </summary>
    public static DateRangeFilterParts ByAutomationIds(
        string fromAutomationId,
        string toAutomationId,
        string applyButtonAutomationId,
        string cancelButtonAutomationId,
        string? openButtonAutomationId = null,
        FilterValueEditorKind editorKind = FilterValueEditorKind.DateTimePicker)
    {
        return new DateRangeFilterParts(
            fromAutomationId,
            toAutomationId,
            applyButtonAutomationId,
            cancelButtonAutomationId,
            openButtonAutomationId,
            editorKind);
    }
}

/// <summary>
/// Configuration for composing a numeric range popup filter from individual UI controls.
/// </summary>
/// <param name="FromLocator">The locator for the lower-bound numeric editor.</param>
/// <param name="ToLocator">The locator for the upper-bound numeric editor.</param>
/// <param name="ApplyButtonLocator">The locator for the apply button.</param>
/// <param name="CancelButtonLocator">The locator for the cancel button.</param>
/// <param name="OpenButtonLocator">Optional locator for the popup open trigger.</param>
/// <param name="EditorKind">The primitive editor kind used by both numeric endpoints.</param>
/// <param name="LocatorKind">The locator strategy for all components. Defaults to <see cref="UiLocatorKind.AutomationId"/>.</param>
/// <param name="FallbackToName">Whether components should fall back to name-based lookup. Defaults to <see langword="true"/>.</param>
public sealed record NumericRangeFilterParts(
    string FromLocator,
    string ToLocator,
    string ApplyButtonLocator,
    string CancelButtonLocator,
    string? OpenButtonLocator = null,
    FilterValueEditorKind EditorKind = FilterValueEditorKind.Spinner,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true)
{
    /// <summary>
    /// Creates a <see cref="NumericRangeFilterParts"/> configuration using automation IDs.
    /// </summary>
    public static NumericRangeFilterParts ByAutomationIds(
        string fromAutomationId,
        string toAutomationId,
        string applyButtonAutomationId,
        string cancelButtonAutomationId,
        string? openButtonAutomationId = null,
        FilterValueEditorKind editorKind = FilterValueEditorKind.Spinner)
    {
        return new NumericRangeFilterParts(
            fromAutomationId,
            toAutomationId,
            applyButtonAutomationId,
            cancelButtonAutomationId,
            openButtonAutomationId,
            editorKind);
    }
}

/// <summary>
/// Configuration for composing a modal dialog from individual UI controls.
/// </summary>
/// <param name="MessageLocator">The locator for the dialog message label.</param>
/// <param name="ConfirmButtonLocator">The locator for the confirm button.</param>
/// <param name="CancelButtonLocator">Optional locator for the cancel button.</param>
/// <param name="DismissButtonLocator">Optional locator for the dismiss/close button.</param>
/// <param name="LocatorKind">The locator strategy for all components. Defaults to <see cref="UiLocatorKind.AutomationId"/>.</param>
/// <param name="FallbackToName">Whether components should fall back to name-based lookup. Defaults to <see langword="true"/>.</param>
public sealed record DialogControlParts(
    string MessageLocator,
    string ConfirmButtonLocator,
    string? CancelButtonLocator = null,
    string? DismissButtonLocator = null,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true)
{
    /// <summary>
    /// Creates a <see cref="DialogControlParts"/> configuration using automation IDs.
    /// </summary>
    public static DialogControlParts ByAutomationIds(
        string messageAutomationId,
        string confirmButtonAutomationId,
        string? cancelButtonAutomationId = null,
        string? dismissButtonAutomationId = null)
    {
        return new DialogControlParts(
            messageAutomationId,
            confirmButtonAutomationId,
            cancelButtonAutomationId,
            dismissButtonAutomationId);
    }
}

/// <summary>
/// Configuration for composing a toast, notification, or status message from individual UI controls.
/// </summary>
/// <param name="TextLocator">The locator for the notification text label.</param>
/// <param name="DismissButtonLocator">Optional locator for the dismiss button.</param>
/// <param name="LocatorKind">The locator strategy for all components. Defaults to <see cref="UiLocatorKind.AutomationId"/>.</param>
/// <param name="FallbackToName">Whether components should fall back to name-based lookup. Defaults to <see langword="true"/>.</param>
public sealed record NotificationControlParts(
    string TextLocator,
    string? DismissButtonLocator = null,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true)
{
    /// <summary>
    /// Creates a <see cref="NotificationControlParts"/> configuration using automation IDs.
    /// </summary>
    public static NotificationControlParts ByAutomationIds(
        string textAutomationId,
        string? dismissButtonAutomationId = null)
    {
        return new NotificationControlParts(textAutomationId, dismissButtonAutomationId);
    }
}

/// <summary>
/// Configuration for composing a folder export picker from individual UI controls.
/// </summary>
/// <param name="OpenButtonLocator">The locator for the button that opens the export picker.</param>
/// <param name="FolderPathInputLocator">The locator for the folder path text input.</param>
/// <param name="SelectButtonLocator">The locator for the select/continue button.</param>
/// <param name="CancelButtonLocator">The locator for the cancel button.</param>
/// <param name="StatusLocator">Optional locator for an export status label.</param>
/// <param name="LocatorKind">The locator strategy for all components. Defaults to <see cref="UiLocatorKind.AutomationId"/>.</param>
/// <param name="FallbackToName">Whether components should fall back to name-based lookup. Defaults to <see langword="true"/>.</param>
public sealed record FolderExportControlParts(
    string OpenButtonLocator,
    string FolderPathInputLocator,
    string SelectButtonLocator,
    string CancelButtonLocator,
    string? StatusLocator = null,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true)
{
    /// <summary>
    /// Creates a <see cref="FolderExportControlParts"/> configuration using automation IDs.
    /// </summary>
    public static FolderExportControlParts ByAutomationIds(
        string openButtonAutomationId,
        string folderPathInputAutomationId,
        string selectButtonAutomationId,
        string cancelButtonAutomationId,
        string? statusAutomationId = null)
    {
        return new FolderExportControlParts(
            openButtonAutomationId,
            folderPathInputAutomationId,
            selectButtonAutomationId,
            cancelButtonAutomationId,
            statusAutomationId);
    }
}

/// <summary>
/// Configuration for composing shell navigation from stable primitive controls.
/// </summary>
/// <param name="NavigationLocator">Optional locator for the navigation source used to open panes.</param>
/// <param name="PaneTabsLocator">Optional locator for the tab control containing open panes.</param>
/// <param name="ActivePaneLabelLocator">Optional locator for a label containing the active pane title.</param>
/// <param name="NavigationKind">The primitive control kind used by the navigation source.</param>
/// <param name="LocatorKind">The locator strategy for all components. Defaults to <see cref="UiLocatorKind.AutomationId"/>.</param>
/// <param name="FallbackToName">Whether components should fall back to name-based lookup. Defaults to <see langword="true"/>.</param>
public sealed record ShellNavigationParts(
    string? NavigationLocator = null,
    string? PaneTabsLocator = null,
    string? ActivePaneLabelLocator = null,
    ShellNavigationSourceKind NavigationKind = ShellNavigationSourceKind.Tree,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true)
{
    /// <summary>
    /// Creates a <see cref="ShellNavigationParts"/> configuration using automation IDs.
    /// </summary>
    public static ShellNavigationParts ByAutomationIds(
        string? navigationAutomationId = null,
        string? paneTabsAutomationId = null,
        string? activePaneLabelAutomationId = null,
        ShellNavigationSourceKind navigationKind = ShellNavigationSourceKind.Tree)
    {
        return new ShellNavigationParts(
            navigationAutomationId,
            paneTabsAutomationId,
            activePaneLabelAutomationId,
            navigationKind);
    }
}

/// <summary>
/// Extension methods for configuring <see cref="IUiControlResolver"/> with adapters.
/// </summary>
public static partial class UiControlResolverExtensions
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
    /// Registers a date range popup filter composite control for a specific property.
    /// </summary>
    public static IUiControlResolver WithDateRangeFilter(
        this IUiControlResolver innerResolver,
        string propertyName,
        DateRangeFilterParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);

        return innerResolver.WithAdapters(new DateRangeFilterControlAdapter(propertyName, parts));
    }

    /// <summary>
    /// Registers a numeric range popup filter composite control for a specific property.
    /// </summary>
    public static IUiControlResolver WithNumericRangeFilter(
        this IUiControlResolver innerResolver,
        string propertyName,
        NumericRangeFilterParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);

        return innerResolver.WithAdapters(new NumericRangeFilterControlAdapter(propertyName, parts));
    }

    /// <summary>
    /// Registers a modal dialog composite control for a specific property.
    /// </summary>
    public static IUiControlResolver WithDialog(
        this IUiControlResolver innerResolver,
        string propertyName,
        DialogControlParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);

        return innerResolver.WithAdapters(new DialogControlAdapter(propertyName, parts));
    }

    /// <summary>
    /// Registers a notification composite control for a specific property.
    /// </summary>
    public static IUiControlResolver WithNotification(
        this IUiControlResolver innerResolver,
        string propertyName,
        NotificationControlParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);

        return innerResolver.WithAdapters(new NotificationControlAdapter(propertyName, parts));
    }

    /// <summary>
    /// Registers a folder export composite control for a specific property.
    /// </summary>
    public static IUiControlResolver WithFolderExport(
        this IUiControlResolver innerResolver,
        string propertyName,
        FolderExportControlParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);

        return innerResolver.WithAdapters(new FolderExportControlAdapter(propertyName, parts));
    }

    /// <summary>
    /// Registers a shell navigation composite control for a specific property.
    /// </summary>
    public static IUiControlResolver WithShellNavigation(
        this IUiControlResolver innerResolver,
        string propertyName,
        ShellNavigationParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);

        return innerResolver.WithAdapters(new ShellNavigationControlAdapter(propertyName, parts));
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
            .Where(static t =>
                !t.IsAbstract
                && !t.IsInterface
                && typeof(IUiControlAdapter).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null);

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
        var results = ResolveResults(innerResolver);
        var applyButton = string.IsNullOrWhiteSpace(_parts.ApplyButtonLocator)
            ? null
            : innerResolver.Resolve<IButtonControl>(CreateDefinition("ApplyButton", UiControlType.Button, _parts.ApplyButtonLocator));
        var expandButton = string.IsNullOrWhiteSpace(_parts.ExpandButtonLocator)
            ? null
            : innerResolver.Resolve<IButtonControl>(CreateDefinition("ExpandButton", UiControlType.Button, _parts.ExpandButtonLocator));

        return new SearchPickerControl(definition.PropertyName, searchInput, results, applyButton, expandButton);
    }

    private ISearchPickerResultsSurface ResolveResults(IUiControlResolver innerResolver)
    {
        return _parts.ResultsKind switch
        {
            SearchPickerResultsKind.ComboBox => new ComboBoxResultsSurface(
                innerResolver.Resolve<IComboBoxControl>(CreateDefinition("Results", UiControlType.ComboBox, _parts.ResultsLocator))),
            SearchPickerResultsKind.ListBox => new ListBoxResultsSurface(
                innerResolver.Resolve<ISelectableListBoxControl>(CreateDefinition("Results", UiControlType.ListBox, _parts.ResultsLocator))),
            _ => throw new NotSupportedException($"Search picker '{_propertyName}' does not support results kind '{_parts.ResultsKind}'.")
        };
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
        private readonly ISearchPickerResultsSurface _results;
        private readonly IButtonControl? _applyButton;
        private readonly IButtonControl? _expandButton;

        public SearchPickerControl(
            string automationId,
            ITextBoxControl searchInput,
            ISearchPickerResultsSurface results,
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

        public string? SelectedItemText => _results.SelectedItemText;

        public IReadOnlyList<string> Items => _results.Items;

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
            _results.SelectItem(itemText);
        }
    }

    private interface ISearchPickerResultsSurface
    {
        string Name { get; }

        bool IsEnabled { get; }

        string? SelectedItemText { get; }

        IReadOnlyList<string> Items { get; }

        void Expand();

        void SelectItem(string itemText);
    }

    private sealed class ComboBoxResultsSurface : ISearchPickerResultsSurface
    {
        private readonly IComboBoxControl _comboBox;

        public ComboBoxResultsSurface(IComboBoxControl comboBox)
        {
            _comboBox = comboBox;
        }

        public string Name => _comboBox.Name;

        public bool IsEnabled => _comboBox.IsEnabled;

        public string? SelectedItemText => _comboBox.SelectedItem?.Text ?? _comboBox.SelectedItem?.Name;

        public IReadOnlyList<string> Items =>
            _comboBox.Items.Select(static item => item.Text ?? item.Name).ToArray();

        public void Expand() => _comboBox.Expand();

        public void SelectItem(string itemText)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemText);

            var normalizedTarget = Normalize(itemText);
            var index = _comboBox.Items
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

            _comboBox.SelectByIndex(index.Value);
        }
    }

    private sealed class ListBoxResultsSurface : ISearchPickerResultsSurface
    {
        private readonly ISelectableListBoxControl _listBox;

        public ListBoxResultsSurface(ISelectableListBoxControl listBox)
        {
            _listBox = listBox;
        }

        public string Name => _listBox.Name;

        public bool IsEnabled => _listBox.IsEnabled;

        public string? SelectedItemText => _listBox.SelectedItemText;

        public IReadOnlyList<string> Items =>
            _listBox.Items.Select(static item => item.Text ?? item.Name ?? string.Empty).ToArray();

        public void Expand()
        {
        }

        public void SelectItem(string itemText) => _listBox.SelectItem(itemText);
    }

    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}

/// <summary>
/// An adapter that creates composite <see cref="IDateRangeFilterControl"/> instances from primitive controls.
/// </summary>
public sealed class DateRangeFilterControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly DateRangeFilterParts _parts;

    /// <summary>
    /// Initializes a new instance of the <see cref="DateRangeFilterControlAdapter"/> class.
    /// </summary>
    public DateRangeFilterControlAdapter(string propertyName, DateRangeFilterParts parts)
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

        return requestedType == typeof(IDateRangeFilterControl)
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        return new DateRangeFilterControl(definition.PropertyName, _parts, innerResolver);
    }

    private sealed class DateRangeFilterControl : IDateRangeFilterControl
    {
        private readonly DateRangeFilterParts _parts;
        private readonly IUiControlResolver _innerResolver;

        public DateRangeFilterControl(string automationId, DateRangeFilterParts parts, IUiControlResolver innerResolver)
        {
            AutomationId = automationId;
            _parts = parts;
            _innerResolver = innerResolver;
        }

        public string AutomationId { get; }

        public string Name => TryReadOpenButtonName() ?? AutomationId;

        public bool IsEnabled => string.IsNullOrWhiteSpace(_parts.OpenButtonLocator)
            || ResolveButton("OpenButton", _parts.OpenButtonLocator).IsEnabled;

        public DateTime? FromValue => ResolveEndpoint("From", _parts.FromLocator).Value;

        public DateTime? ToValue => ResolveEndpoint("To", _parts.ToLocator).Value;

        public void Open()
        {
            if (!string.IsNullOrWhiteSpace(_parts.OpenButtonLocator))
            {
                ResolveButton("OpenButton", _parts.OpenButtonLocator).Invoke();
            }
        }

        public void SetRange(DateRangeFilterRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            Open();

            if (request.From is not null)
            {
                ResolveEndpoint("From", _parts.FromLocator).Value = request.From.Value;
            }

            if (request.To is not null)
            {
                ResolveEndpoint("To", _parts.ToLocator).Value = request.To.Value;
            }

            var commitButton = request.CommitMode == FilterPopupCommitMode.Apply
                ? ResolveButton("ApplyButton", _parts.ApplyButtonLocator)
                : ResolveButton("CancelButton", _parts.CancelButtonLocator);
            commitButton.Invoke();
        }

        private string? TryReadOpenButtonName()
        {
            try
            {
                return string.IsNullOrWhiteSpace(_parts.OpenButtonLocator)
                    ? null
                    : ResolveButton("OpenButton", _parts.OpenButtonLocator).Name;
            }
            catch
            {
                return null;
            }
        }

        private IButtonControl ResolveButton(string suffix, string? locatorValue)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locatorValue);

            return _innerResolver.Resolve<IButtonControl>(CreateDefinition(suffix, UiControlType.Button, locatorValue));
        }

        private IDateEndpoint ResolveEndpoint(string suffix, string locatorValue)
        {
            return _parts.EditorKind switch
            {
                FilterValueEditorKind.DateTimePicker => new DateTimePickerEndpoint(
                    _innerResolver.Resolve<IDateTimePickerControl>(CreateDefinition(suffix, UiControlType.DateTimePicker, locatorValue))),
                FilterValueEditorKind.TextBox => new TextBoxDateEndpoint(
                    _innerResolver.Resolve<ITextBoxControl>(CreateDefinition(suffix, UiControlType.TextBox, locatorValue))),
                _ => throw new NotSupportedException($"Date range filter '{AutomationId}' does not support editor kind '{_parts.EditorKind}'.")
            };
        }

        private UiControlDefinition CreateDefinition(string suffix, UiControlType controlType, string locatorValue)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locatorValue);

            return new UiControlDefinition(
                $"{AutomationId}{suffix}",
                controlType,
                locatorValue,
                _parts.LocatorKind,
                _parts.FallbackToName);
        }
    }

    private interface IDateEndpoint
    {
        DateTime? Value { get; set; }
    }

    private sealed class DateTimePickerEndpoint : IDateEndpoint
    {
        private readonly IDateTimePickerControl _control;

        public DateTimePickerEndpoint(IDateTimePickerControl control)
        {
            _control = control;
        }

        public DateTime? Value
        {
            get => _control.SelectedDate?.Date;
            set => _control.SelectedDate = value?.Date;
        }
    }

    private sealed class TextBoxDateEndpoint : IDateEndpoint
    {
        private readonly ITextBoxControl _control;

        public TextBoxDateEndpoint(ITextBoxControl control)
        {
            _control = control;
        }

        public DateTime? Value
        {
            get
            {
                var text = _control.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                if (DateTime.TryParseExact(
                    text,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var exactDate))
                {
                    return exactDate.Date;
                }

                return DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var parsedDate)
                    ? parsedDate.Date
                    : null;
            }

            set => _control.Enter(value?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }
}

/// <summary>
/// An adapter that creates composite <see cref="INumericRangeFilterControl"/> instances from primitive controls.
/// </summary>
public sealed class NumericRangeFilterControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly NumericRangeFilterParts _parts;

    /// <summary>
    /// Initializes a new instance of the <see cref="NumericRangeFilterControlAdapter"/> class.
    /// </summary>
    public NumericRangeFilterControlAdapter(string propertyName, NumericRangeFilterParts parts)
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

        return requestedType == typeof(INumericRangeFilterControl)
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        return new NumericRangeFilterControl(definition.PropertyName, _parts, innerResolver);
    }

    private sealed class NumericRangeFilterControl : INumericRangeFilterControl
    {
        private readonly NumericRangeFilterParts _parts;
        private readonly IUiControlResolver _innerResolver;

        public NumericRangeFilterControl(string automationId, NumericRangeFilterParts parts, IUiControlResolver innerResolver)
        {
            AutomationId = automationId;
            _parts = parts;
            _innerResolver = innerResolver;
        }

        public string AutomationId { get; }

        public string Name => TryReadOpenButtonName() ?? AutomationId;

        public bool IsEnabled => string.IsNullOrWhiteSpace(_parts.OpenButtonLocator)
            || ResolveButton("OpenButton", _parts.OpenButtonLocator).IsEnabled;

        public double? FromValue => ResolveEndpoint("From", _parts.FromLocator).Value;

        public double? ToValue => ResolveEndpoint("To", _parts.ToLocator).Value;

        public void Open()
        {
            if (!string.IsNullOrWhiteSpace(_parts.OpenButtonLocator))
            {
                ResolveButton("OpenButton", _parts.OpenButtonLocator).Invoke();
            }
        }

        public void SetRange(NumericRangeFilterRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            Open();

            if (request.From is not null)
            {
                ResolveEndpoint("From", _parts.FromLocator).Value = request.From.Value;
            }

            if (request.To is not null)
            {
                ResolveEndpoint("To", _parts.ToLocator).Value = request.To.Value;
            }

            var commitButton = request.CommitMode == FilterPopupCommitMode.Apply
                ? ResolveButton("ApplyButton", _parts.ApplyButtonLocator)
                : ResolveButton("CancelButton", _parts.CancelButtonLocator);
            commitButton.Invoke();
        }

        private string? TryReadOpenButtonName()
        {
            try
            {
                return string.IsNullOrWhiteSpace(_parts.OpenButtonLocator)
                    ? null
                    : ResolveButton("OpenButton", _parts.OpenButtonLocator).Name;
            }
            catch
            {
                return null;
            }
        }

        private IButtonControl ResolveButton(string suffix, string? locatorValue)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locatorValue);

            return _innerResolver.Resolve<IButtonControl>(CreateDefinition(suffix, UiControlType.Button, locatorValue));
        }

        private INumericEndpoint ResolveEndpoint(string suffix, string locatorValue)
        {
            return _parts.EditorKind switch
            {
                FilterValueEditorKind.Spinner => new SpinnerEndpoint(
                    _innerResolver.Resolve<ISpinnerControl>(CreateDefinition(suffix, UiControlType.Spinner, locatorValue))),
                FilterValueEditorKind.TextBox => new TextBoxNumericEndpoint(
                    _innerResolver.Resolve<ITextBoxControl>(CreateDefinition(suffix, UiControlType.TextBox, locatorValue))),
                _ => throw new NotSupportedException($"Numeric range filter '{AutomationId}' does not support editor kind '{_parts.EditorKind}'.")
            };
        }

        private UiControlDefinition CreateDefinition(string suffix, UiControlType controlType, string locatorValue)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locatorValue);

            return new UiControlDefinition(
                $"{AutomationId}{suffix}",
                controlType,
                locatorValue,
                _parts.LocatorKind,
                _parts.FallbackToName);
        }
    }

    private interface INumericEndpoint
    {
        double? Value { get; set; }
    }

    private sealed class SpinnerEndpoint : INumericEndpoint
    {
        private readonly ISpinnerControl _control;

        public SpinnerEndpoint(ISpinnerControl control)
        {
            _control = control;
        }

        public double? Value
        {
            get => _control.Value;
            set
            {
                if (value is not null)
                {
                    _control.Value = value.Value;
                }
            }
        }
    }

    private sealed class TextBoxNumericEndpoint : INumericEndpoint
    {
        private readonly ITextBoxControl _control;

        public TextBoxNumericEndpoint(ITextBoxControl control)
        {
            _control = control;
        }

        public double? Value
        {
            get
            {
                var text = _control.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                return double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                    ? value
                    : null;
            }

            set => _control.Enter(value?.ToString("G17", CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }
}

/// <summary>
/// An adapter that creates composite <see cref="IDialogControl"/> instances from primitive controls.
/// </summary>
public sealed class DialogControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly DialogControlParts _parts;

    /// <summary>
    /// Initializes a new instance of the <see cref="DialogControlAdapter"/> class.
    /// </summary>
    public DialogControlAdapter(string propertyName, DialogControlParts parts)
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

        return requestedType == typeof(IDialogControl)
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        return new DialogControl(definition.PropertyName, _parts, innerResolver);
    }

    private sealed class DialogControl : IDialogControl
    {
        private readonly DialogControlParts _parts;
        private readonly IUiControlResolver _innerResolver;

        public DialogControl(string automationId, DialogControlParts parts, IUiControlResolver innerResolver)
        {
            AutomationId = automationId;
            _parts = parts;
            _innerResolver = innerResolver;
        }

        public string AutomationId { get; }

        public string Name => TryReadMessageText() ?? AutomationId;

        public bool IsEnabled => ResolveLabel("Message", _parts.MessageLocator).IsEnabled;

        public string MessageText => ResolveLabel("Message", _parts.MessageLocator).Text;

        public void Complete(DialogActionKind actionKind = DialogActionKind.Confirm)
        {
            var button = actionKind switch
            {
                DialogActionKind.Confirm => ResolveButton("ConfirmButton", _parts.ConfirmButtonLocator, actionKind),
                DialogActionKind.Cancel => ResolveButton("CancelButton", _parts.CancelButtonLocator, actionKind),
                DialogActionKind.Dismiss => ResolveButton("DismissButton", _parts.DismissButtonLocator, actionKind),
                _ => throw new ArgumentOutOfRangeException(nameof(actionKind), actionKind, "Unsupported dialog action.")
            };
            button.Invoke();
        }

        private string? TryReadMessageText()
        {
            try
            {
                return MessageText;
            }
            catch
            {
                return null;
            }
        }

        private ILabelControl ResolveLabel(string suffix, string locatorValue)
        {
            return _innerResolver.Resolve<ILabelControl>(CreateDefinition(suffix, UiControlType.Label, locatorValue));
        }

        private IButtonControl ResolveButton(string suffix, string? locatorValue, DialogActionKind actionKind)
        {
            if (string.IsNullOrWhiteSpace(locatorValue))
            {
                throw new NotSupportedException(
                    $"Dialog '{AutomationId}' does not support action '{actionKind}' because '{suffix}' is not configured.");
            }

            return _innerResolver.Resolve<IButtonControl>(CreateDefinition(suffix, UiControlType.Button, locatorValue));
        }

        private UiControlDefinition CreateDefinition(string suffix, UiControlType controlType, string locatorValue)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locatorValue);

            return new UiControlDefinition(
                $"{AutomationId}{suffix}",
                controlType,
                locatorValue,
                _parts.LocatorKind,
                _parts.FallbackToName);
        }
    }
}

/// <summary>
/// An adapter that creates composite <see cref="INotificationControl"/> instances from primitive controls.
/// </summary>
public sealed class NotificationControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly NotificationControlParts _parts;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationControlAdapter"/> class.
    /// </summary>
    public NotificationControlAdapter(string propertyName, NotificationControlParts parts)
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

        return requestedType == typeof(INotificationControl)
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        return new NotificationControl(definition.PropertyName, _parts, innerResolver);
    }

    private sealed class NotificationControl : INotificationControl
    {
        private readonly NotificationControlParts _parts;
        private readonly IUiControlResolver _innerResolver;

        public NotificationControl(string automationId, NotificationControlParts parts, IUiControlResolver innerResolver)
        {
            AutomationId = automationId;
            _parts = parts;
            _innerResolver = innerResolver;
        }

        public string AutomationId { get; }

        public string Name => TryReadText() ?? AutomationId;

        public bool IsEnabled => ResolveLabel("Text", _parts.TextLocator).IsEnabled;

        public string Text => ResolveLabel("Text", _parts.TextLocator).Text;

        public void Dismiss()
        {
            if (string.IsNullOrWhiteSpace(_parts.DismissButtonLocator))
            {
                throw new NotSupportedException(
                    $"Notification '{AutomationId}' cannot be dismissed because 'DismissButton' is not configured.");
            }

            ResolveButton("DismissButton", _parts.DismissButtonLocator).Invoke();
        }

        private string? TryReadText()
        {
            try
            {
                return Text;
            }
            catch
            {
                return null;
            }
        }

        private ILabelControl ResolveLabel(string suffix, string locatorValue)
        {
            return _innerResolver.Resolve<ILabelControl>(CreateDefinition(suffix, UiControlType.Label, locatorValue));
        }

        private IButtonControl ResolveButton(string suffix, string locatorValue)
        {
            return _innerResolver.Resolve<IButtonControl>(CreateDefinition(suffix, UiControlType.Button, locatorValue));
        }

        private UiControlDefinition CreateDefinition(string suffix, UiControlType controlType, string locatorValue)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locatorValue);

            return new UiControlDefinition(
                $"{AutomationId}{suffix}",
                controlType,
                locatorValue,
                _parts.LocatorKind,
                _parts.FallbackToName);
        }
    }
}

/// <summary>
/// An adapter that creates composite <see cref="IFolderExportControl"/> instances from primitive controls.
/// </summary>
public sealed class FolderExportControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly FolderExportControlParts _parts;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderExportControlAdapter"/> class.
    /// </summary>
    public FolderExportControlAdapter(string propertyName, FolderExportControlParts parts)
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

        return requestedType == typeof(IFolderExportControl)
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        return new FolderExportControl(definition.PropertyName, _parts, innerResolver);
    }

    private sealed class FolderExportControl : IFolderExportControl
    {
        private readonly FolderExportControlParts _parts;
        private readonly IUiControlResolver _innerResolver;

        public FolderExportControl(string automationId, FolderExportControlParts parts, IUiControlResolver innerResolver)
        {
            AutomationId = automationId;
            _parts = parts;
            _innerResolver = innerResolver;
        }

        public string AutomationId { get; }

        public string Name => TryReadOpenButtonName() ?? AutomationId;

        public bool IsEnabled => ResolveButton("OpenButton", _parts.OpenButtonLocator).IsEnabled;

        public string? SelectedFolderPath => ResolveTextBox("FolderPathInput", _parts.FolderPathInputLocator).Text;

        public string? StatusText => string.IsNullOrWhiteSpace(_parts.StatusLocator)
            ? null
            : ResolveLabel("Status", _parts.StatusLocator).Text;

        public void SelectFolder(string folderPath, FolderExportCommitMode commitMode = FolderExportCommitMode.Select)
        {
            if (commitMode == FolderExportCommitMode.Select)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
            }

            ResolveButton("OpenButton", _parts.OpenButtonLocator).Invoke();

            switch (commitMode)
            {
                case FolderExportCommitMode.Select:
                    ResolveTextBox("FolderPathInput", _parts.FolderPathInputLocator).Enter(folderPath);
                    ResolveButton("SelectButton", _parts.SelectButtonLocator).Invoke();
                    break;
                case FolderExportCommitMode.Cancel:
                    ResolveButton("CancelButton", _parts.CancelButtonLocator).Invoke();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(commitMode), commitMode, "Unsupported folder export commit mode.");
            }
        }

        private string? TryReadOpenButtonName()
        {
            try
            {
                return ResolveButton("OpenButton", _parts.OpenButtonLocator).Name;
            }
            catch
            {
                return null;
            }
        }

        private IButtonControl ResolveButton(string suffix, string locatorValue)
        {
            return _innerResolver.Resolve<IButtonControl>(CreateDefinition(suffix, UiControlType.Button, locatorValue));
        }

        private ITextBoxControl ResolveTextBox(string suffix, string locatorValue)
        {
            return _innerResolver.Resolve<ITextBoxControl>(CreateDefinition(suffix, UiControlType.TextBox, locatorValue));
        }

        private ILabelControl ResolveLabel(string suffix, string locatorValue)
        {
            return _innerResolver.Resolve<ILabelControl>(CreateDefinition(suffix, UiControlType.Label, locatorValue));
        }

        private UiControlDefinition CreateDefinition(string suffix, UiControlType controlType, string locatorValue)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locatorValue);

            return new UiControlDefinition(
                $"{AutomationId}{suffix}",
                controlType,
                locatorValue,
                _parts.LocatorKind,
                _parts.FallbackToName);
        }
    }
}

/// <summary>
/// An adapter that creates composite <see cref="IShellNavigationControl"/> instances from primitive controls.
/// </summary>
public sealed class ShellNavigationControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly ShellNavigationParts _parts;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellNavigationControlAdapter"/> class.
    /// </summary>
    public ShellNavigationControlAdapter(string propertyName, ShellNavigationParts parts)
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

        return requestedType == typeof(IShellNavigationControl)
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        return new ShellNavigationControl(definition.PropertyName, _parts, innerResolver);
    }

    private sealed class ShellNavigationControl : IShellNavigationControl
    {
        private readonly ShellNavigationParts _parts;
        private readonly IUiControlResolver _innerResolver;

        public ShellNavigationControl(string automationId, ShellNavigationParts parts, IUiControlResolver innerResolver)
        {
            AutomationId = automationId;
            _parts = parts;
            _innerResolver = innerResolver;
        }

        public string AutomationId { get; }

        public string Name => ActivePaneName ?? AutomationId;

        public bool IsEnabled
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_parts.NavigationLocator))
                {
                    return ResolveNavigationSource().IsEnabled;
                }

                if (ResolvePaneTabsOrDefault() is { } paneTabs)
                {
                    return paneTabs.IsEnabled;
                }

                if (!string.IsNullOrWhiteSpace(_parts.ActivePaneLabelLocator))
                {
                    return ResolveLabel("ActivePaneLabel", _parts.ActivePaneLabelLocator!).IsEnabled;
                }

                return false;
            }
        }

        public string? ActivePaneName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_parts.ActivePaneLabelLocator))
                {
                    return ResolveLabel("ActivePaneLabel", _parts.ActivePaneLabelLocator).Text;
                }

                var selectedTab = ResolvePaneTabsOrDefault()?.Items
                    .FirstOrDefault(static item => item.IsSelected);
                return selectedTab is null ? null : GetDisplayText(selectedTab);
            }
        }

        public IReadOnlyList<string> OpenPaneNames =>
            ResolvePaneTabsOrDefault()?.Items
                .Select(GetDisplayText)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToArray()
            ?? Array.Empty<string>();

        public void OpenOrActivate(ShellPaneNavigationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.PaneName);

            switch (request.Mode)
            {
                case ShellPaneNavigationMode.Open:
                    OpenPane(request.PaneName);
                    break;
                case ShellPaneNavigationMode.Activate:
                    ActivatePane(request.PaneName);
                    break;
                case ShellPaneNavigationMode.OpenOrActivate:
                    if (!TryActivateOpenPane(request.PaneName))
                    {
                        OpenPane(request.PaneName);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "Unsupported shell navigation mode.");
            }
        }

        private bool TryActivateOpenPane(string paneName)
        {
            var matchingPaneName = FindOpenPaneName(paneName);
            if (matchingPaneName is null)
            {
                return false;
            }

            ActivatePane(matchingPaneName);
            return true;
        }

        private string? FindOpenPaneName(string paneName)
        {
            var normalizedPaneName = NormalizeLookupText(paneName);
            return OpenPaneNames.FirstOrDefault(candidate =>
                string.Equals(NormalizeLookupText(candidate), normalizedPaneName, StringComparison.OrdinalIgnoreCase));
        }

        private void OpenPane(string paneName)
        {
            if (string.IsNullOrWhiteSpace(_parts.NavigationLocator))
            {
                throw new NotSupportedException(
                    $"Shell navigation '{AutomationId}' cannot open panes because a navigation source is not configured.");
            }

            var navigationLocator = _parts.NavigationLocator;
            switch (_parts.NavigationKind)
            {
                case ShellNavigationSourceKind.Tree:
                    SelectTreeItem(ResolveTree("Navigation", navigationLocator), paneName);
                    break;
                case ShellNavigationSourceKind.ListBox:
                    SelectListItem(ResolveListBox("Navigation", navigationLocator), paneName);
                    break;
                case ShellNavigationSourceKind.Tab:
                    ResolveTab("Navigation", navigationLocator).SelectTabItem(paneName);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Shell navigation '{AutomationId}' does not support navigation kind '{_parts.NavigationKind}'.");
            }
        }

        private void ActivatePane(string paneName)
        {
            var paneTabs = ResolvePaneTabsOrDefault()
                ?? throw new NotSupportedException(
                    $"Shell navigation '{AutomationId}' cannot activate pane '{paneName}' because pane tabs are not configured.");

            paneTabs.SelectTabItem(paneName);
        }

        private IUiControl ResolveNavigationSource()
        {
            if (string.IsNullOrWhiteSpace(_parts.NavigationLocator))
            {
                throw new NotSupportedException(
                    $"Shell navigation '{AutomationId}' cannot open panes because a navigation source is not configured.");
            }

            var navigationLocator = _parts.NavigationLocator;
            return _parts.NavigationKind switch
            {
                ShellNavigationSourceKind.Tree => ResolveTree("Navigation", navigationLocator),
                ShellNavigationSourceKind.ListBox => ResolveListBox("Navigation", navigationLocator),
                ShellNavigationSourceKind.Tab => ResolveTab("Navigation", navigationLocator),
                _ => throw new NotSupportedException(
                    $"Shell navigation '{AutomationId}' does not support navigation kind '{_parts.NavigationKind}'.")
            };
        }

        private ITreeControl ResolveTree(string suffix, string locatorValue)
        {
            return _innerResolver.Resolve<ITreeControl>(CreateDefinition(suffix, UiControlType.Tree, locatorValue));
        }

        private IListBoxControl ResolveListBox(string suffix, string locatorValue)
        {
            return _innerResolver.Resolve<IListBoxControl>(CreateDefinition(suffix, UiControlType.ListBox, locatorValue));
        }

        private ITabControl ResolveTab(string suffix, string locatorValue)
        {
            return _innerResolver.Resolve<ITabControl>(CreateDefinition(suffix, UiControlType.Tab, locatorValue));
        }

        private ITabControl? ResolvePaneTabsOrDefault()
        {
            return string.IsNullOrWhiteSpace(_parts.PaneTabsLocator)
                ? null
                : ResolveTab("PaneTabs", _parts.PaneTabsLocator);
        }

        private ILabelControl ResolveLabel(string suffix, string locatorValue)
        {
            return _innerResolver.Resolve<ILabelControl>(CreateDefinition(suffix, UiControlType.Label, locatorValue));
        }

        private UiControlDefinition CreateDefinition(string suffix, UiControlType controlType, string locatorValue)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locatorValue);

            return new UiControlDefinition(
                $"{AutomationId}{suffix}",
                controlType,
                locatorValue,
                _parts.LocatorKind,
                _parts.FallbackToName);
        }

        private static void SelectTreeItem(ITreeControl tree, string paneName)
        {
            var item = FindTreeItem(tree.Items, paneName)
                ?? throw new InvalidOperationException($"Shell navigation tree item '{paneName}' was not found.");

            item.SelectNode();
        }

        private static ITreeItemControl? FindTreeItem(IEnumerable<ITreeItemControl> items, string paneName)
        {
            var normalizedPaneName = NormalizeLookupText(paneName);
            foreach (var item in items)
            {
                if (string.Equals(NormalizeLookupText(item.Text), normalizedPaneName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(NormalizeLookupText(item.Name), normalizedPaneName, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }

                try
                {
                    item.Expand();
                }
                catch
                {
                    // Tree expansion is best effort across mixed runtimes.
                }

                var nested = FindTreeItem(item.Items, paneName);
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static void SelectListItem(IListBoxControl listBox, string paneName)
        {
            if (listBox is not ISelectableListBoxControl selectableListBox)
            {
                throw new NotSupportedException(
                    $"Shell navigation list '{listBox.AutomationId}' does not support interactive selection.");
            }

            selectableListBox.SelectItem(paneName);
        }

        private static string GetDisplayText(IUiControl control)
        {
            return FirstNonWhiteSpace(control.Name, control.AutomationId) ?? string.Empty;
        }

        private static string? FirstNonWhiteSpace(params string?[] values)
        {
            return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        }

        private static string NormalizeLookupText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }
    }
}
