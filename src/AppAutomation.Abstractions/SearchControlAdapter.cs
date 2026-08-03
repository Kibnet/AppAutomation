namespace AppAutomation.Abstractions;

internal interface ISearchControlExecutionPhases
{
    bool IsSearchActionEnabled { get; }

    bool IsHistoryOpenActionEnabled { get; }

    void EnterSearchInput(string value);

    void InvokeSearchAction();
}

/// <summary>
/// Configuration for composing one logical search control from its input and history popup parts.
/// </summary>
public sealed record SearchControlParts(
    string SearchInputLocator,
    string HistoryResultsLocator,
    string? SearchButtonLocator = null,
    string? HistoryOpenButtonLocator = null,
    string? HistoryRootLocator = null,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true,
    SearchHistoryResultsKind HistoryResultsKind = SearchHistoryResultsKind.Buttons)
{
    /// <summary>Creates a search-control configuration using automation IDs.</summary>
    public static SearchControlParts ByAutomationIds(
        string searchInputAutomationId,
        string historyResultsAutomationId,
        string? searchButtonAutomationId = null,
        string? historyOpenButtonAutomationId = null,
        string? historyRootAutomationId = null,
        SearchHistoryResultsKind historyResultsKind = SearchHistoryResultsKind.Buttons)
    {
        return new SearchControlParts(
            searchInputAutomationId,
            historyResultsAutomationId,
            searchButtonAutomationId,
            historyOpenButtonAutomationId,
            historyRootAutomationId,
            HistoryResultsKind: historyResultsKind);
    }
}

/// <summary>Specifies how visible search-history items are exposed by the application.</summary>
public enum SearchHistoryResultsKind
{
    /// <summary>History items are repeated buttons sharing the configured locator, as in ARM SearchControl.</summary>
    Buttons = 0,

    /// <summary>History items are exposed by a selectable list box.</summary>
    ListBox = 1
}

public static partial class UiControlResolverExtensions
{
    /// <summary>Registers one logical search control for a page property.</summary>
    public static IUiControlResolver WithSearchControl(
        this IUiControlResolver innerResolver,
        string propertyName,
        SearchControlParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);

        return innerResolver.WithAdapters(new SearchControlAdapter(propertyName, parts));
    }
}

/// <summary>Composes an <see cref="ISearchControl"/> from provider-neutral primitive controls.</summary>
public sealed class SearchControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly SearchControlParts _parts;

    /// <summary>Initializes a search-control adapter.</summary>
    public SearchControlAdapter(string propertyName, SearchControlParts parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.SearchInputLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.HistoryResultsLocator);
        ValidateOptionalLocator(parts.SearchButtonLocator, nameof(parts.SearchButtonLocator));
        ValidateOptionalLocator(parts.HistoryOpenButtonLocator, nameof(parts.HistoryOpenButtonLocator));
        ValidateOptionalLocator(parts.HistoryRootLocator, nameof(parts.HistoryRootLocator));

        _propertyName = propertyName.Trim();
        _parts = parts;
    }

    /// <inheritdoc />
    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);

        return requestedType == typeof(ISearchControl)
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        var input = innerResolver.Resolve<ITextBoxControl>(
            CreateDefinition("Input", UiControlType.TextBox, _parts.SearchInputLocator));
        return new SearchControl(
            definition.PropertyName,
            input,
            CreateHistoryItems(innerResolver),
            CreateOptionalButton(innerResolver, "SearchButton", _parts.SearchButtonLocator),
            CreateOptionalButton(innerResolver, "HistoryOpenButton", _parts.HistoryOpenButtonLocator),
            CreateHistoryAvailability(innerResolver));
    }

    private ISearchHistoryItemsControl CreateHistoryItems(IUiControlResolver resolver)
    {
        return new DeferredHistoryItemsControl(() => _parts.HistoryResultsKind switch
        {
            SearchHistoryResultsKind.Buttons => resolver.Resolve<ISearchHistoryItemsControl>(
                CreateHistoryDefinition("HistoryButtons", UiControlType.Button, _parts.HistoryResultsLocator)),
            SearchHistoryResultsKind.ListBox => new ListBoxHistoryItemsControl(
                resolver.Resolve<ISelectableListBoxControl>(
                    CreateHistoryDefinition("HistoryList", UiControlType.ListBox, _parts.HistoryResultsLocator))),
            _ => throw new NotSupportedException(
                $"Search control '{_propertyName}' does not support history kind '{_parts.HistoryResultsKind}'.")
        }, _parts.HistoryResultsLocator);
    }

    private Func<bool> CreateHistoryAvailability(IUiControlResolver resolver)
    {
        if (string.IsNullOrWhiteSpace(_parts.HistoryRootLocator))
        {
            return () => CreateHistoryItems(resolver).IsAvailable;
        }

        return () =>
        {
            try
            {
                var root = resolver.Resolve<IUiControl>(
                    CreateDefinition("HistoryRoot", UiControlType.AutomationElement, _parts.HistoryRootLocator));
                return root is IUiControlAvailability availability
                    ? availability.IsAvailable
                    : throw new NotSupportedException(
                        $"Search history root for '{_propertyName}' must expose {nameof(IUiControlAvailability)}.");
            }
            catch (NotSupportedException)
            {
                throw;
            }
            catch
            {
                return CreateHistoryItems(resolver).IsAvailable;
            }
        };
    }

    private IButtonControl? CreateOptionalButton(
        IUiControlResolver resolver,
        string suffix,
        string? locator)
    {
        return string.IsNullOrWhiteSpace(locator)
            ? null
            : new DeferredButtonControl(
                () => resolver.Resolve<IButtonControl>(CreateDefinition(suffix, UiControlType.Button, locator)),
                locator);
    }

    private UiControlDefinition CreateDefinition(string suffix, UiControlType type, string locator)
    {
        return new UiControlDefinition(
            $"{_propertyName}{suffix}",
            type,
            locator,
            _parts.LocatorKind,
            _parts.FallbackToName);
    }

    private UiControlDefinition CreateHistoryDefinition(string suffix, UiControlType type, string locator)
    {
        var definition = CreateDefinition(suffix, type, locator);
        return string.IsNullOrWhiteSpace(_parts.HistoryRootLocator)
            ? definition
            : definition with
            {
                Scope = new UiControlScope(
                    _parts.HistoryRootLocator,
                    _parts.LocatorKind,
                    _parts.FallbackToName)
                {
                    AnchorLocatorValue = _parts.SearchInputLocator
                }
            };
    }

    private static void ValidateOptionalLocator(string? locator, string parameterName)
    {
        if (locator is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locator, parameterName);
        }
    }

    private sealed class SearchControl : ISearchControl, IReadableTextControl, ISearchControlExecutionPhases
    {
        private readonly ITextBoxControl _input;
        private readonly ISearchHistoryItemsControl _history;
        private readonly IButtonControl? _searchButton;
        private readonly IButtonControl? _historyOpenButton;
        private readonly Func<bool> _isHistoryAvailable;
        private bool _historyOpenRequested;

        public SearchControl(
            string automationId,
            ITextBoxControl input,
            ISearchHistoryItemsControl history,
            IButtonControl? searchButton,
            IButtonControl? historyOpenButton,
            Func<bool> isHistoryAvailable)
        {
            AutomationId = automationId;
            _input = input;
            _history = history;
            _searchButton = searchButton;
            _historyOpenButton = historyOpenButton;
            _isHistoryAvailable = isHistoryAvailable;
        }

        public string AutomationId { get; }

        public string Name => _input.Name;

        public bool IsEnabled => _input.IsEnabled;

        public string Text => _input.Text;

        public IReadOnlyList<string> HistoryItems => _history.Items;

        public bool IsHistoryOpen => _isHistoryAvailable();

        bool ISearchControlExecutionPhases.IsSearchActionEnabled => _searchButton?.IsEnabled ?? true;

        bool ISearchControlExecutionPhases.IsHistoryOpenActionEnabled =>
            IsHistoryOpen
            || (_historyOpenButton?.IsEnabled ?? _input.IsEnabled);

        public void EnterSearch(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            EnterSearchInput(value);
            InvokeSearchAction();
        }

        public void ClearSearch()
        {
            EnterSearchInput(string.Empty);
            InvokeSearchAction();
        }

        void ISearchControlExecutionPhases.EnterSearchInput(string value) => EnterSearchInput(value);

        void ISearchControlExecutionPhases.InvokeSearchAction() => InvokeSearchAction();

        public void OpenHistory()
        {
            if (IsHistoryOpen)
            {
                _historyOpenRequested = true;
                return;
            }

            if (_historyOpenButton is not null)
            {
                _historyOpenButton.Invoke();
                _historyOpenRequested = true;
                return;
            }

            // Entering the existing value focuses the real editor in desktop providers.
            // ARM SearchControl opens history from the editor's focus/pointer handlers.
            _input.Enter(_input.Text);
            _historyOpenRequested = true;
        }

        public void ApplySearchFromHistory(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (!_historyOpenRequested && !IsHistoryOpen)
            {
                OpenHistory();
            }

            try
            {
                _history.Apply(value);
            }
            finally
            {
                _historyOpenRequested = false;
            }
        }

        private void EnterSearchInput(string value)
        {
            _historyOpenRequested = false;
            _input.Enter(value);
        }

        private void InvokeSearchAction() => _searchButton?.Invoke();
    }

    private sealed class DeferredHistoryItemsControl : ISearchHistoryItemsControl
    {
        private readonly Func<ISearchHistoryItemsControl> _resolve;
        private readonly string _locator;

        public DeferredHistoryItemsControl(Func<ISearchHistoryItemsControl> resolve, string locator)
        {
            _resolve = resolve;
            _locator = locator;
        }

        public string AutomationId => _locator;

        public string Name => TryResolve()?.Name ?? string.Empty;

        public bool IsEnabled => TryResolve()?.IsEnabled ?? true;

        public bool IsAvailable => TryResolve()?.IsAvailable ?? false;

        public IReadOnlyList<string> Items => TryResolve()?.Items ?? Array.Empty<string>();

        public void Apply(string itemText) => _resolve().Apply(itemText);

        private ISearchHistoryItemsControl? TryResolve()
        {
            try
            {
                return _resolve();
            }
            catch
            {
                return null;
            }
        }
    }

    private sealed class ListBoxHistoryItemsControl : ISearchHistoryItemsControl
    {
        private readonly ISelectableListBoxControl _inner;

        public ListBoxHistoryItemsControl(ISelectableListBoxControl inner)
        {
            _inner = inner;
        }

        public string AutomationId => _inner.AutomationId;

        public string Name => _inner.Name;

        public bool IsEnabled => _inner.IsEnabled;

        public bool IsAvailable => _inner is IUiControlAvailability availability
            ? availability.IsAvailable
            : throw new NotSupportedException(
                $"Search history list '{AutomationId}' must expose {nameof(IUiControlAvailability)}.");

        public IReadOnlyList<string> Items => _inner.Items
            .Select(static item => item.Text ?? item.Name ?? string.Empty)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        public void Apply(string itemText)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemText);
            var items = Items;
            var exactMatches = items
                .Where(candidate => string.Equals(candidate, itemText, StringComparison.Ordinal))
                .ToArray();
            if (exactMatches.Length == 0)
            {
                throw new InvalidOperationException($"Search history item '{itemText}' was not found.");
            }

            if (exactMatches.Length > 1)
            {
                throw new InvalidOperationException($"Search history item '{itemText}' is ambiguous.");
            }

            if (_inner is IExactSelectableListBoxControl exactListBox)
            {
                exactListBox.SelectItemExact(exactMatches[0]);
                return;
            }

            if (items.Count(candidate => string.Equals(candidate, itemText, StringComparison.OrdinalIgnoreCase)) > 1)
            {
                throw new NotSupportedException(
                    $"Search history list '{AutomationId}' must expose {nameof(IExactSelectableListBoxControl)} "
                    + "when item texts differ only by case.");
            }

            _inner.SelectItem(exactMatches[0]);
        }
    }

    private sealed class DeferredButtonControl : IButtonControl
    {
        private readonly Func<IButtonControl> _resolve;
        private readonly string _locator;

        public DeferredButtonControl(Func<IButtonControl> resolve, string locator)
        {
            _resolve = resolve;
            _locator = locator;
        }

        public string AutomationId => _locator;

        public string Name => TryResolve()?.Name ?? string.Empty;

        public bool IsEnabled => TryResolve()?.IsEnabled ?? false;

        public void Invoke() => _resolve().Invoke();

        private IButtonControl? TryResolve()
        {
            try
            {
                return _resolve();
            }
            catch
            {
                return null;
            }
        }
    }
}
