namespace AppAutomation.Abstractions;

public enum SingleSelectResultsKind
{
    ComboBox = 0,
    ListBox = 1
}

public enum SingleSelectCommitMode
{
    Immediate = 0,
    Confirm = 1
}

/// <summary>Provider-neutral parts of a logical single-selection editor.</summary>
public sealed record SingleSelectParts(
    string RootLocator,
    string ResultsLocator,
    string? InputLocator = null,
    string? OpenButtonLocator = null,
    string? SelectedValueLocator = null,
    string? PopupRootLocator = null,
    string? ConfirmButtonLocator = null,
    string? CancelButtonLocator = null,
    SingleSelectResultsKind ResultsKind = SingleSelectResultsKind.ComboBox,
    SingleSelectCommitMode CommitMode = SingleSelectCommitMode.Immediate,
    bool PersistInputText = false,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true)
{
    public static SingleSelectParts ByAutomationIds(
        string rootAutomationId,
        string resultsAutomationId,
        string? inputAutomationId = null,
        string? openButtonAutomationId = null,
        string? selectedValueAutomationId = null,
        string? popupRootAutomationId = null,
        string? confirmButtonAutomationId = null,
        string? cancelButtonAutomationId = null,
        SingleSelectResultsKind resultsKind = SingleSelectResultsKind.ComboBox,
        SingleSelectCommitMode commitMode = SingleSelectCommitMode.Immediate,
        bool persistInputText = false)
    {
        return new SingleSelectParts(
            rootAutomationId,
            resultsAutomationId,
            inputAutomationId,
            openButtonAutomationId,
            selectedValueAutomationId,
            popupRootAutomationId,
            confirmButtonAutomationId,
            cancelButtonAutomationId,
            resultsKind,
            commitMode,
            persistInputText);
    }
}

public static partial class UiControlResolverExtensions
{
    public static IUiControlResolver WithSingleSelect(
        this IUiControlResolver innerResolver,
        string propertyName,
        SingleSelectParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        return innerResolver.WithAdapters(new SingleSelectControlAdapter(propertyName, parts));
    }
}

/// <summary>Composes an <see cref="IComboBoxControl"/> from stable primitive parts.</summary>
public sealed class SingleSelectControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly SingleSelectParts _parts;

    public SingleSelectControlAdapter(string propertyName, SingleSelectParts parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        _propertyName = propertyName.Trim();
        _parts = parts ?? throw new ArgumentNullException(nameof(parts));
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.RootLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.ResultsLocator);
        if (parts.CommitMode == SingleSelectCommitMode.Confirm
            && string.IsNullOrWhiteSpace(parts.ConfirmButtonLocator))
        {
            throw new ArgumentException("A confirmation button locator is required for confirmed single-selection editors.", nameof(parts));
        }
    }

    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        return requestedType == typeof(IComboBoxControl)
            && definition.ControlType == UiControlType.ComboBox
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        return new CompositeSingleSelectControl(definition.LocatorValue, _propertyName, _parts, innerResolver);
    }

    private sealed class CompositeSingleSelectControl : IComboBoxControl, IUiControlAvailability, ISingleSelectOperationControl
    {
        private readonly string _propertyName;
        private readonly SingleSelectParts _parts;
        private readonly IUiControlResolver _resolver;
        private IReadOnlyList<IComboBoxItem> _items = Array.Empty<IComboBoxItem>();

        public CompositeSingleSelectControl(
            string automationId,
            string propertyName,
            SingleSelectParts parts,
            IUiControlResolver resolver)
        {
            AutomationId = automationId;
            _propertyName = propertyName;
            _parts = parts;
            _resolver = resolver;
        }

        public string AutomationId { get; }

        public string Name => TryResolveRoot()?.Name ?? ReadSelectedValue() ?? AutomationId;

        public bool IsEnabled => TryResolveRoot()?.IsEnabled == true;

        public bool IsAvailable => TryResolveRoot() is { } root
            && (root as IUiControlAvailability)?.IsAvailable != false;

        public IReadOnlyList<IComboBoxItem> Items
        {
            get
            {
                if (TryResolveSurface() is { } surface)
                {
                    _items = surface.Items.Select(static text => (IComboBoxItem)new SingleSelectItem(text)).ToArray();
                }

                return _items;
            }
        }

        public IComboBoxItem? SelectedItem
        {
            get
            {
                var selected = ReadSelectedValue();
                return string.IsNullOrWhiteSpace(selected) ? null : new SingleSelectItem(selected);
            }
        }

        public int SelectedIndex
        {
            get
            {
                var selected = ReadSelectedValue();
                if (string.IsNullOrWhiteSpace(selected))
                {
                    return -1;
                }

                return Items
                    .Select((item, index) => (item, index))
                    .Where(candidate => Matches(candidate.item, selected))
                    .Select(static candidate => candidate.index)
                    .DefaultIfEmpty(-1)
                    .First();
            }
            set => SelectByIndex(value);
        }

        public void SelectByIndex(int index)
        {
            if (index < 0 || index >= Items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            SelectItem(Items[index].Text, 5000);
        }

        public void Expand()
        {
            OpenAndResolveSurface(5000).Expand();
        }

        public void SelectItem(string itemText, int timeoutMs)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemText);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);

            var budget = UiOperationTimeoutBudget.Start(timeoutMs, "single-selection");
            var surface = OpenAndResolveSurface(budget);
            var selection = WaitForSingleMatch(surface, itemText, budget.Remaining);

            surface.Select(selection.Match!);
            if (_parts.CommitMode == SingleSelectCommitMode.Confirm)
            {
                InvokeButton(_parts.ConfirmButtonLocator!, "Confirm", budget.Remaining);
            }

            if (!string.IsNullOrWhiteSpace(_parts.PopupRootLocator))
            {
                WaitForPopupClosure(budget.Remaining);
            }

            WaitForCommittedSelection(selection.Match!, budget.Remaining);
            _items = selection.AvailableItems.Select(static text => (IComboBoxItem)new SingleSelectItem(text)).ToArray();
        }

        private ISingleSelectSurface OpenAndResolveSurface(int timeoutMs) =>
            OpenAndResolveSurface(UiOperationTimeoutBudget.Start(timeoutMs, "single-selection"));

        private ISingleSelectSurface OpenAndResolveSurface(UiOperationTimeoutBudget budget)
        {
            IUiControl? root = null;
            UiWait.Until(
                () =>
                {
                    root = TryResolveRoot();
                    return root is not null
                        && root.IsEnabled
                        && (root as IUiControlAvailability)?.IsAvailable != false;
                },
                static ready => ready,
                new UiWaitOptions
                {
                    Timeout = budget.Remaining,
                    PollInterval = TimeSpan.FromMilliseconds(50)
                },
                $"Single-selection editor '{_parts.RootLocator}' did not become available.");

            if (!string.IsNullOrWhiteSpace(_parts.OpenButtonLocator))
            {
                InvokeButton(_parts.OpenButtonLocator, "Open", budget.Remaining);
            }

            ISingleSelectSurface? surface = null;
            UiWait.Until(
                () =>
                {
                    surface = TryResolveSurface();
                    return surface is not null && surface.IsEnabled;
                },
                static ready => ready,
                new UiWaitOptions
                {
                    Timeout = budget.Remaining,
                    PollInterval = TimeSpan.FromMilliseconds(50)
                },
                $"Single-selection results '{_parts.ResultsLocator}' did not become available.");
            surface!.Expand();
            return surface;
        }

        private IUiControl? TryResolveRoot()
        {
            try
            {
                return _resolver.Resolve<IUiControl>(Definition("Root", UiControlType.AutomationElement, _parts.RootLocator));
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private ISingleSelectSurface? TryResolveSurface()
        {
            try
            {
                return _parts.ResultsKind switch
                {
                    SingleSelectResultsKind.ComboBox => new ComboSurface(
                        _resolver.Resolve<IComboBoxControl>(Definition("Results", UiControlType.ComboBox, _parts.ResultsLocator))),
                    SingleSelectResultsKind.ListBox => new ListSurface(
                        _resolver.Resolve<ISelectableListBoxControl>(Definition("Results", UiControlType.ListBox, _parts.ResultsLocator))),
                    _ => throw new NotSupportedException($"Unsupported single-selection results kind '{_parts.ResultsKind}'.")
                };
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private string? ReadSelectedValue()
        {
            if (_parts.CommitMode == SingleSelectCommitMode.Immediate)
            {
                var surfaceValue = TryResolveSurface()?.SelectedText;
                if (!string.IsNullOrWhiteSpace(surfaceValue))
                {
                    return surfaceValue.Trim();
                }
            }

            var displayedValue = TryReadText(_parts.SelectedValueLocator)
                ?? TryReadText(_parts.InputLocator)
                ?? TryReadRootSelection();
            if (!string.IsNullOrWhiteSpace(displayedValue))
            {
                return displayedValue.Trim();
            }

            return null;
        }

        private string? TryReadRootSelection()
        {
            var definition = Definition("CommittedRoot", UiControlType.ComboBox, _parts.RootLocator);
            try
            {
                var comboBox = _resolver.Resolve<IComboBoxControl>(definition);
                return comboBox.SelectedItem?.Text ?? comboBox.SelectedItem?.Name;
            }
            catch (InvalidOperationException)
            {
                try
                {
                    return _resolver.Resolve<IReadableTextControl>(
                        Definition("CommittedRootText", UiControlType.Label, _parts.RootLocator)).Text;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        private void WaitForCommittedSelection(string expected, TimeSpan timeout)
        {
            UiWait.Until(
                ReadSelectedValue,
                actual => string.Equals(actual?.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase),
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Single-selection editor '{_parts.RootLocator}' did not commit item '{expected}'.");
        }

        private string? TryReadText(string? locator)
        {
            if (string.IsNullOrWhiteSpace(locator))
            {
                return null;
            }

            try
            {
                return _resolver.Resolve<ITextBoxControl>(Definition("ValueText", UiControlType.TextBox, locator)).Text;
            }
            catch (InvalidOperationException)
            {
                try
                {
                    return _resolver.Resolve<IReadableTextControl>(Definition("ValueLabel", UiControlType.Label, locator)).Text;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        private void InvokeButton(string locator, string suffix, TimeSpan timeout)
        {
            IButtonControl? button = null;
            UiWait.Until(
                () => (button = TryResolveButton(locator, suffix))?.IsEnabled == true,
                static ready => ready,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Single-selection {suffix.ToLowerInvariant()} button '{locator}' did not become available.");
            button!.Invoke();
        }

        private IButtonControl? TryResolveButton(string? locator, string suffix)
        {
            if (string.IsNullOrWhiteSpace(locator))
            {
                return null;
            }

            try
            {
                return _resolver.Resolve<IButtonControl>(Definition(suffix, UiControlType.Button, locator));
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private void WaitForPopupClosure(TimeSpan timeout)
        {
            UiWait.Until(
                IsPopupAvailable,
                static isAvailable => !isAvailable,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Single-selection popup '{_parts.PopupRootLocator}' did not close.");
        }

        private static SelectionMatch WaitForSingleMatch(
            ISingleSelectSurface surface,
            string itemText,
            TimeSpan timeout)
        {
            var result = UiWait.TryUntil(
                () => FindSingleMatch(surface, itemText),
                static candidate => candidate.Match is not null,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) });
            if (!result.Success || result.Value.Match is null)
            {
                throw new InvalidOperationException(
                    $"Single-selection item '{itemText}' was not found. Available items: [{string.Join(", ", result.Value.AvailableItems)}].");
            }

            return result.Value;
        }

        private static SelectionMatch FindSingleMatch(ISingleSelectSurface surface, string itemText)
        {
            var availableItems = surface.Items;
            var matches = availableItems
                .Where(candidate => string.Equals(candidate.Trim(), itemText.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Single-selection item '{itemText}' is ambiguous. Matching items: [{string.Join(", ", matches)}].");
            }

            return new SelectionMatch(availableItems, matches.SingleOrDefault());
        }

        private bool IsPopupAvailable()
        {
            IUiControl popupRoot;
            try
            {
                popupRoot = _resolver.Resolve<IUiControl>(Definition(
                    "PopupRoot",
                    UiControlType.AutomationElement,
                    _parts.PopupRootLocator!));
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            return popupRoot is IUiControlAvailability availability
                ? availability.IsAvailable
                : throw new InvalidOperationException(
                    $"Single-selection popup '{_parts.PopupRootLocator}' must expose {nameof(IUiControlAvailability)}.");
        }

        private UiControlDefinition Definition(string suffix, UiControlType type, string locator)
        {
            return new UiControlDefinition($"{_propertyName}{suffix}", type, locator, _parts.LocatorKind, _parts.FallbackToName);
        }

        private static bool Matches(IComboBoxItem item, string value)
        {
            return string.Equals(item.Text.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Name.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private sealed record SelectionMatch(IReadOnlyList<string> AvailableItems, string? Match);
    }

    private interface ISingleSelectSurface
    {
        bool IsEnabled { get; }

        IReadOnlyList<string> Items { get; }

        string? SelectedText { get; }

        void Expand();

        void Select(string text);
    }

    private sealed class ComboSurface(IComboBoxControl inner) : ISingleSelectSurface
    {
        public bool IsEnabled => inner.IsEnabled;

        public IReadOnlyList<string> Items => inner.Items.Select(static item => item.Text ?? item.Name).ToArray();

        public string? SelectedText => inner.SelectedItem?.Text ?? inner.SelectedItem?.Name;

        public void Expand() => inner.Expand();

        public void Select(string text)
        {
            var index = Items
                .Select((item, candidateIndex) => (item, candidateIndex))
                .Single(candidate => string.Equals(candidate.item.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase))
                .candidateIndex;
            inner.SelectByIndex(index);
        }
    }

    private sealed class ListSurface(ISelectableListBoxControl inner) : ISingleSelectSurface
    {
        public bool IsEnabled => inner.IsEnabled;

        public IReadOnlyList<string> Items => inner.Items.Select(static item => item.Text ?? item.Name ?? string.Empty).ToArray();

        public string? SelectedText => inner.SelectedItemText;

        public void Expand()
        {
        }

        public void Select(string text) => inner.SelectItem(text);
    }

    private sealed record SingleSelectItem(string Text) : IComboBoxItem
    {
        public string Name => Text;
    }
}

internal interface ISingleSelectOperationControl
{
    void SelectItem(string itemText, int timeoutMs);
}
