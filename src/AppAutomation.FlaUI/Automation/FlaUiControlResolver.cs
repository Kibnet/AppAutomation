using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Extensions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;
using CultureInfo = System.Globalization.CultureInfo;
using DateTimeStyles = System.Globalization.DateTimeStyles;

namespace AppAutomation.FlaUI.Automation;

public sealed class FlaUiControlResolver : IUiControlResolver, IUiArtifactCollector
{
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageKeyUp = 0x0101;
    private const uint WindowMessageLeftButtonDown = 0x0201;
    private const uint WindowMessageLeftButtonUp = 0x0202;
    private const int MouseKeyLeftButton = 0x0001;
    private const int VirtualKeySpace = 0x20;
    private const int SpaceKeyDownData = 0x00390001;
    private const int SpaceKeyUpData = unchecked((int)0xC0390001);

    private readonly Window _window;
    private readonly ConditionFactory _conditionFactory;

    public FlaUiControlResolver(Window window, ConditionFactory conditionFactory)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _conditionFactory = conditionFactory ?? throw new ArgumentNullException(nameof(conditionFactory));
    }

    public UiRuntimeCapabilities Capabilities { get; } = new(
        AdapterId: "flaui",
        SupportsGridCellAccess: true,
        SupportsCalendarRangeSelection: true,
        SupportsTreeNodeExpansionState: true,
        SupportsRawNativeHandles: true,
        SupportsScreenshots: true);

    public TControl Resolve<TControl>(UiControlDefinition definition)
        where TControl : class
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (typeof(TControl) == typeof(IMultiSelectItemsControl))
        {
            return (TControl)(object)new FlaUiMultiSelectItemsControl(FindElement(definition));
        }

        object resolved = definition.ControlType switch
        {
            UiControlType.TextBox => new FlaUiTextBoxControl(FindElement(definition).AsTextBox()),
            UiControlType.Button => new FlaUiButtonControl(FindElement(definition).AsButton()),
            UiControlType.Label => new FlaUiLabelControl(FindElement(definition).AsLabel()),
            UiControlType.ListBox => new FlaUiListBoxControl(FindElement(definition).AsListBox()),
            UiControlType.CheckBox => new FlaUiCheckBoxControl(FindElement(definition).AsCheckBox()),
            UiControlType.ComboBox => new FlaUiComboBoxControl(FindElement(definition).AsComboBox()),
            UiControlType.RadioButton => new FlaUiRadioButtonControl(FindElement(definition).AsRadioButton()),
            UiControlType.ToggleButton => new FlaUiToggleButtonControl(FindElement(definition).AsToggleButton()),
            UiControlType.Slider => new FlaUiSliderControl(FindElement(definition).AsSlider()),
            UiControlType.ProgressBar => new FlaUiProgressBarControl(FindElement(definition).AsProgressBar()),
            UiControlType.Calendar => new FlaUiCalendarControl(FindElement(definition).AsCalendar()),
            UiControlType.DateTimePicker => new FlaUiDateTimePickerControl(FindElement(definition).AsDateTimePicker()),
            UiControlType.Spinner => new FlaUiSpinnerControl(FindElement(definition).AsSpinner()),
            UiControlType.Tab => new FlaUiTabControl(FindElement(definition).AsTab()),
            UiControlType.TabItem => new FlaUiTabItemControl(FindElement(definition).AsTabItem()),
            UiControlType.Tree => new FlaUiTreeControl(FindElement(definition).AsTree()),
            UiControlType.TreeItem => new FlaUiTreeItemControl(FindElement(definition).AsTreeItem()),
            UiControlType.DataGridView => new FlaUiDataGridViewControl(FindElement(definition).AsDataGridView()),
            UiControlType.Grid => ResolveGrid(definition),
            UiControlType.DataGridViewRow or UiControlType.GridRow => new FlaUiGridRowControl(FindGridRow(definition)),
            UiControlType.DataGridViewCell or UiControlType.GridCell => new FlaUiGridCellControl(FindGridCell(definition)),
            _ => new FlaUiControl(FindElement(definition))
        };

        return resolved as TControl
            ?? throw new InvalidOperationException(
                $"Resolved control '{definition.PropertyName}' cannot be cast to '{typeof(TControl).FullName}'.");
    }

    public ValueTask<IReadOnlyList<UiFailureArtifact>> CollectAsync(
        UiFailureContext failureContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var screenshotArtifact = CreateScreenshotArtifact();
        var windowHandleArtifact = CreateWindowHandleArtifact();
        var processInfoArtifact = CreateProcessInfoArtifact();

        IReadOnlyList<UiFailureArtifact> artifacts =
        [
            new UiFailureArtifact(
                Kind: "logical-tree",
                LogicalName: "logical-tree",
                RelativePath: "artifacts/ui-failures/flaui/logical-tree.txt",
                ContentType: "text/plain",
                IsRequiredByContract: true,
                InlineTextPreview: BuildLogicalTreeSnapshot()),
            screenshotArtifact,
            processInfoArtifact,
            windowHandleArtifact
        ];

        return ValueTask.FromResult(artifacts);
    }

    private GridRow FindGridRow(UiControlDefinition definition)
    {
        return definition.ControlType == UiControlType.DataGridViewRow
            ? FindElement(definition).AsGridRow()
            : FindElement(definition).AsGridRow();
    }

    private GridCell FindGridCell(UiControlDefinition definition)
    {
        return definition.ControlType == UiControlType.DataGridViewCell
            ? FindElement(definition).AsGridCell()
            : FindElement(definition).AsGridCell();
    }

    private IGridControl ResolveGrid(UiControlDefinition definition)
    {
        var element = FindElement(definition);
        if (TryRead(() => element.Patterns.Grid.IsSupported) != true)
        {
            var fallbackGrid = TryRead(() => element.AsGrid());
            return new FlaUiVisualGridControl(
                _window,
                definition.LocatorValue,
                fallbackGrid is null ? null : new FlaUiGridControl(fallbackGrid));
        }

        return new FlaUiGridControl(element.AsGrid());
    }

    private AutomationElement FindElement(UiControlDefinition definition)
    {
        var element = _window.FindFirstDescendant(CreateCondition(definition.LocatorValue, definition.LocatorKind));
        if (element is not null)
        {
            return element;
        }

        if (definition.FallbackToName && definition.LocatorKind != UiLocatorKind.Name)
        {
            element = _window.FindFirstDescendant(CreateCondition(definition.LocatorValue, UiLocatorKind.Name));
            if (element is not null)
            {
                return element;
            }
        }

        var rootSearch = definition.LocatorKind switch
        {
            UiLocatorKind.AutomationId => SearchByAutomationId(definition.LocatorValue),
            UiLocatorKind.Name => SearchByName(definition.LocatorValue),
            _ => SearchByAutomationId(definition.LocatorValue)
        };

        if (rootSearch is not null)
        {
            return rootSearch;
        }

        throw new ElementNotAvailableException(
            $"Element with locator [{definition.LocatorKind}:{definition.LocatorValue}] was not found.");
    }

    private PropertyCondition CreateCondition(string locatorValue, UiLocatorKind locatorKind)
    {
        return locatorKind switch
        {
            UiLocatorKind.AutomationId => _conditionFactory.ByAutomationId(locatorValue),
            UiLocatorKind.Name => _conditionFactory.ByName(locatorValue),
            _ => throw new ArgumentOutOfRangeException(nameof(locatorKind), locatorKind, "Unsupported locator kind.")
        };
    }

    private AutomationElement? SearchByAutomationId(string locatorValue)
    {
        var direct = _window.FindAllDescendants(factory => factory.ByAutomationId(locatorValue));
        if (direct.Length > 0)
        {
            return direct.FirstOrDefault(candidate => candidate?.IsAvailable == true);
        }

        var detached = SearchDetachedProcessRootsByAutomationId(locatorValue);
        if (detached is not null)
        {
            return detached;
        }

        var normalized = locatorValue.Trim().ToLowerInvariant();
        var descendant = _window.FindAllDescendants()
            .FirstOrDefault(candidate =>
            {
                if (!candidate.IsAvailable)
                {
                    return false;
                }

                var automationId = TryRead(() => candidate.AutomationId)?.ToLowerInvariant();
                return automationId is not null && (automationId == normalized || automationId.StartsWith(normalized, StringComparison.Ordinal));
            });

        return descendant ?? SearchDetachedProcessRootsByAutomationIdPrefix(normalized);
    }

    private AutomationElement? SearchByName(string locatorValue)
    {
        var direct = _window.FindAllDescendants(factory => factory.ByName(locatorValue));
        if (direct.Length > 0)
        {
            return direct.FirstOrDefault(candidate => candidate?.IsAvailable == true);
        }

        var detached = SearchDetachedProcessRootsByName(locatorValue);
        if (detached is not null)
        {
            return detached;
        }

        var normalized = locatorValue.Trim().ToLowerInvariant();
        var descendant = _window.FindAllDescendants()
            .FirstOrDefault(candidate =>
            {
                if (!candidate.IsAvailable)
                {
                    return false;
                }

                var name = TryRead(() => candidate.Name)?.ToLowerInvariant();
                return name is not null && (name == normalized || name.Contains(normalized, StringComparison.Ordinal));
            });

        return descendant ?? SearchDetachedProcessRootsByNameContains(normalized);
    }

    private AutomationElement? SearchDetachedProcessRootsByAutomationId(string locatorValue)
    {
        foreach (var root in EnumerateDetachedProcessRoots())
        {
            var direct = TryRead(() => root.FindAllDescendants(factory => factory.ByAutomationId(locatorValue)))
                ?? Array.Empty<AutomationElement>();
            var match = direct.FirstOrDefault(IsAttachedAndAvailable);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private AutomationElement? SearchDetachedProcessRootsByName(string locatorValue)
    {
        foreach (var root in EnumerateDetachedProcessRoots())
        {
            var direct = TryRead(() => root.FindAllDescendants(factory => factory.ByName(locatorValue)))
                ?? Array.Empty<AutomationElement>();
            var match = direct.FirstOrDefault(IsAttachedAndAvailable);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private AutomationElement? SearchDetachedProcessRootsByAutomationIdPrefix(string normalizedLocatorValue)
    {
        foreach (var root in EnumerateDetachedProcessRoots())
        {
            var match = FindAutomationDescendants(root)
                .FirstOrDefault(candidate =>
                {
                    if (!IsAttachedAndAvailable(candidate))
                    {
                        return false;
                    }

                    var automationId = TryRead(() => candidate.AutomationId)?.ToLowerInvariant();
                    return automationId is not null
                        && (automationId == normalizedLocatorValue || automationId.StartsWith(normalizedLocatorValue, StringComparison.Ordinal));
                });
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private AutomationElement? SearchDetachedProcessRootsByNameContains(string normalizedLocatorValue)
    {
        foreach (var root in EnumerateDetachedProcessRoots())
        {
            var match = FindAutomationDescendants(root)
                .FirstOrDefault(candidate =>
                {
                    if (!IsAttachedAndAvailable(candidate))
                    {
                        return false;
                    }

                    var name = TryRead(() => candidate.Name)?.ToLowerInvariant();
                    return name is not null
                        && (name == normalizedLocatorValue || name.Contains(normalizedLocatorValue, StringComparison.Ordinal));
                });
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private AutomationElement[] EnumerateDetachedProcessRoots()
    {
        var processId = TryRead(() => _window.FrameworkAutomationElement.ProcessId.ValueOrDefault);
        if (processId <= 0)
        {
            return Array.Empty<AutomationElement>();
        }

        var desktop = TryRead(() => _window.Automation.GetDesktop());
        if (desktop is null)
        {
            return Array.Empty<AutomationElement>();
        }

        var windowHandle = TryRead(() => _window.FrameworkAutomationElement.NativeWindowHandle.ValueOrDefault);
        var roots = TryRead(() => desktop.FindAllChildren(factory => factory.ByProcessId(processId)))
            ?? Array.Empty<AutomationElement>();

        return roots
            .Where(candidate => candidate?.IsAvailable == true && !IsSameNativeWindow(candidate, windowHandle))
            .ToArray();
    }

    private static bool IsSameNativeWindow(AutomationElement candidate, IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var candidateHandle = TryRead(() => candidate.FrameworkAutomationElement.NativeWindowHandle.ValueOrDefault);
        return candidateHandle != IntPtr.Zero && candidateHandle == windowHandle;
    }

    private static bool IsAttachedAndAvailable(AutomationElement? candidate)
    {
        if (candidate is null)
        {
            return false;
        }

        try
        {
            return candidate.IsAvailable
                && candidate.Parent is not null;
        }
        catch
        {
            return false;
        }
    }

    private static T? TryRead<T>(Func<T> accessor)
    {
        try
        {
            return accessor();
        }
        catch
        {
            return default;
        }
    }

    private string BuildLogicalTreeSnapshot()
    {
        var builder = new StringBuilder();
        AppendElement(builder, _window, depth: 0);

        foreach (var candidate in _window.FindAllDescendants())
        {
            AppendElement(builder, candidate, depth: 1);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendElement(StringBuilder builder, AutomationElement element, int depth)
    {
        builder.Append(' ', depth * 2)
            .Append(TryRead(() => element.ControlType.ToString()) ?? "<unknown>")
            .Append(" | Id=")
            .Append(TryRead(() => element.AutomationId) ?? string.Empty)
            .Append(" | Name=")
            .Append(TryRead(() => element.Name) ?? string.Empty)
            .AppendLine();
    }

    private UiFailureArtifact CreateScreenshotArtifact()
    {
        try
        {
            using var screenshot = _window.Capture();
            return new UiFailureArtifact(
                Kind: "screenshot",
                LogicalName: "window-screenshot",
                RelativePath: "artifacts/ui-failures/flaui/window.png",
                ContentType: "image/png",
                IsRequiredByContract: true,
                InlineTextPreview: $"{screenshot.Width}x{screenshot.Height}");
        }
        catch (Exception ex)
        {
            return new UiFailureArtifact(
                Kind: "screenshot-unavailable",
                LogicalName: "window-screenshot",
                RelativePath: "artifacts/ui-failures/flaui/window.png",
                ContentType: "text/plain",
                IsRequiredByContract: false,
                InlineTextPreview: ex.Message);
        }
    }

    private UiFailureArtifact CreateWindowHandleArtifact()
    {
        var handle = TryRead(() => _window.FrameworkAutomationElement.NativeWindowHandle.ValueOrDefault);
        var isAvailable = handle != IntPtr.Zero;

        return new UiFailureArtifact(
            Kind: "window-handle",
            LogicalName: "window-handle",
            RelativePath: "artifacts/ui-failures/flaui/window-handle.txt",
            ContentType: "text/plain",
            IsRequiredByContract: isAvailable,
            InlineTextPreview: isAvailable
                ? $"0x{handle.ToInt64():X}"
                : "Window handle unavailable.");
    }

    private UiFailureArtifact CreateProcessInfoArtifact()
    {
        var processId = TryRead(() => _window.FrameworkAutomationElement.ProcessId.ValueOrDefault);
        if (processId <= 0)
        {
            return new UiFailureArtifact(
                Kind: "process-info",
                LogicalName: "process-info",
                RelativePath: "artifacts/ui-failures/flaui/process-info.txt",
                ContentType: "text/plain",
                IsRequiredByContract: false,
                InlineTextPreview: "Process id unavailable.");
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            var startedAt = TryRead(() => process.StartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            return new UiFailureArtifact(
                Kind: "process-info",
                LogicalName: "process-info",
                RelativePath: "artifacts/ui-failures/flaui/process-info.txt",
                ContentType: "text/plain",
                IsRequiredByContract: true,
                InlineTextPreview: $"Pid={processId}; Name={process.ProcessName}; StartedAtUtc={startedAt ?? "<unknown>"}");
        }
        catch (Exception ex)
        {
            return new UiFailureArtifact(
                Kind: "process-info",
                LogicalName: "process-info",
                RelativePath: "artifacts/ui-failures/flaui/process-info.txt",
                ContentType: "text/plain",
                IsRequiredByContract: false,
                InlineTextPreview: $"Pid={processId}; Error={ex.Message}");
        }
    }

    private abstract class FlaUiControlBase<TControl> : IUiControlAvailability
        where TControl : AutomationElement
    {
        protected FlaUiControlBase(TControl inner)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        protected TControl Inner { get; }

        public string AutomationId => TryRead(() => Inner.AutomationId) ?? string.Empty;

        public string Name => TryRead(() => Inner.Name) ?? string.Empty;

        public bool IsEnabled => TryRead(() => Inner.IsEnabled);

        public bool IsAvailable => TryRead(() => Inner.IsAvailable && !Inner.IsOffscreen);

        protected static TResult? TryRead<TResult>(Func<TResult> accessor)
        {
            try
            {
                return accessor();
            }
            catch
            {
                return default;
            }
        }
    }

    private sealed class FlaUiControl : FlaUiControlBase<AutomationElement>, IReadableTextControl
    {
        public FlaUiControl(AutomationElement inner) : base(inner)
        {
        }

        public string Text => ReadAutomationElementVisibleText(Inner) ?? string.Empty;
    }

    private sealed class FlaUiTextBoxControl : FlaUiControlBase<TextBox>, ITextBoxControl
    {
        public FlaUiTextBoxControl(TextBox inner) : base(inner)
        {
        }

        public string Text
        {
            get => TryRead(() => Inner.Text) ?? string.Empty;
            set => Inner.Text = value;
        }

        public void Enter(string value)
        {
            Inner.EnterText(value);
        }
    }

    private sealed class FlaUiButtonControl : FlaUiControlBase<Button>, IButtonControl, IReadableTextControl
    {
        public FlaUiButtonControl(Button inner) : base(inner)
        {
        }

        public string Text => ReadAutomationElementVisibleText(Inner) ?? string.Empty;

        public void Invoke()
        {
            if (Inner.Patterns.Toggle.IsSupported && IsOpenToggleButton(Inner))
            {
                var togglePattern = Inner.Patterns.Toggle.Pattern;
                if (togglePattern.ToggleState.Value != ToggleState.On)
                {
                    togglePattern.Toggle();
                }

                return;
            }

            if (Inner.Patterns.Invoke.IsSupported)
            {
                Inner.Invoke();
                return;
            }

            if (Inner.Patterns.Toggle.IsSupported)
            {
                Inner.Patterns.Toggle.Pattern.Toggle();
                return;
            }

            Inner.Click();
        }

        private static bool IsOpenToggleButton(AutomationElement element)
        {
            return IsOpenToggleToken(TryRead(() => element.AutomationId))
                || IsOpenToggleToken(TryRead(() => element.Name));
        }

        private static bool IsOpenToggleToken(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && (value.EndsWith("OpenButton", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("PopupOpenButton", StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class FlaUiLabelControl : FlaUiControlBase<Label>, ILabelControl
    {
        public FlaUiLabelControl(Label inner) : base(inner)
        {
        }

        public string Text => TryRead(() => Inner.Text) ?? ReadAutomationElementVisibleText(Inner) ?? string.Empty;
    }

    private sealed class FlaUiListBoxControl : FlaUiControlBase<ListBox>, ISelectableListBoxControl, IReadableTextControl
    {
        public FlaUiListBoxControl(ListBox inner) : base(inner)
        {
        }

        public IReadOnlyList<IListBoxItem> Items =>
            ReadItems();

        public string Text
        {
            get
            {
                var selectedText = SelectedItemText;
                return !string.IsNullOrWhiteSpace(selectedText)
                    ? selectedText
                    : string.Join(" ", Items.Select(static item => item.Text).Where(static text => !string.IsNullOrWhiteSpace(text)));
            }
        }

        public string? SelectedItemText => ReadSelectedText();

        private IListBoxItem[] ReadItems()
        {
            return GetSelectableItems()
                .Select(candidate =>
                {
                    var text = ReadAutomationElementText(candidate);
                    return (IListBoxItem)new FlaUiListBoxItem(text, text);
                })
                .Where(static item => !string.IsNullOrWhiteSpace(item.Text) || !string.IsNullOrWhiteSpace(item.Name))
                .ToArray();
        }

        public void SelectItem(string itemText)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemText);

            var normalizedTarget = NormalizeLookupText(itemText);
            var candidate = GetSelectableItems().FirstOrDefault(element =>
            {
                var text = ReadAutomationElementText(element);
                return string.Equals(
                    NormalizeLookupText(text),
                    normalizedTarget,
                    StringComparison.OrdinalIgnoreCase);
            });
            if (candidate is null)
            {
                throw new InvalidOperationException($"ListBox item '{itemText}' was not found.");
            }

            if (TrySelect(candidate) && (SelectionMatches(normalizedTarget) || SelectionStateUnavailable()))
            {
                return;
            }

            if (TryClick(candidate) && (SelectionMatches(normalizedTarget) || SelectionStateUnavailable()))
            {
                return;
            }

            throw new InvalidOperationException($"ListBox item '{itemText}' could not be selected.");
        }

        private List<AutomationElement> GetSelectableItems()
        {
            var timeout = Stopwatch.StartNew();
            List<AutomationElement> items;
            do
            {
                items = ReadSelectableItems();
                if (items.Count > 0)
                {
                    return items;
                }

                Thread.Sleep(50);
            }
            while (timeout.Elapsed < TimeSpan.FromSeconds(1));

            return items;
        }

        private List<AutomationElement> ReadSelectableItems()
        {
            var items = new List<AutomationElement>();

            try
            {
                foreach (var item in Inner.Items)
                {
                    if (item is not null && !items.Contains(item))
                    {
                        items.Add(item);
                    }
                }
            }
            catch
            {
                // Some providers do not expose direct list items.
            }

            foreach (var candidate in FindAutomationDescendants(Inner))
            {
                if (candidate is null || candidate == Inner || items.Contains(candidate) || !IsListItemCandidate(candidate))
                {
                    continue;
                }

                var text = ReadAutomationElementText(candidate);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    items.Add(candidate);
                }
            }

            return items;
        }

        private string? ReadSelectedText()
        {
            var selectedCandidate = GetSelectableItems().FirstOrDefault(candidate =>
            {
                try
                {
                    return candidate.Patterns.SelectionItem.IsSupported
                        && candidate.Patterns.SelectionItem.Pattern.IsSelected.Value;
                }
                catch
                {
                    return false;
                }
            });

            return selectedCandidate is null
                ? null
                : ReadAutomationElementText(selectedCandidate);
        }

        private bool SelectionMatches(string expectedText)
        {
            if (string.IsNullOrWhiteSpace(expectedText))
            {
                return true;
            }

            try
            {
                return string.Equals(
                    NormalizeLookupText(ReadSelectedText()),
                    expectedText,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool SelectionStateUnavailable()
        {
            try
            {
                _ = Inner.IsAvailable;
                _ = Inner.FindAllDescendants();
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsListItemCandidate(AutomationElement candidate)
        {
            return candidate.ControlType == ControlType.ListItem
                || candidate.ControlType == ControlType.Text
                || candidate.ControlType == ControlType.DataItem;
        }

        private static bool TrySelect(AutomationElement candidate)
        {
            try
            {
                if (candidate.Patterns.SelectionItem.IsSupported)
                {
                    candidate.Patterns.SelectionItem.Pattern.Select();
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryClick(AutomationElement candidate)
        {
            try
            {
                candidate.Click();
                return true;
            }
            catch
            {
            }

            try
            {
                if (candidate.Patterns.Invoke.IsSupported)
                {
                    candidate.Patterns.Invoke.Pattern.Invoke();
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }

    private sealed class FlaUiListBoxItem : IListBoxItem
    {
        public FlaUiListBoxItem(string? text, string? name)
        {
            Text = text;
            Name = name;
        }

        public string? Text { get; }

        public string? Name { get; }
    }

    private sealed class FlaUiCheckBoxControl : FlaUiControlBase<CheckBox>, ICheckBoxControl, IReadableTextControl
    {
        public FlaUiCheckBoxControl(CheckBox inner) : base(inner)
        {
        }

        public string Text => ReadAutomationElementVisibleText(Inner) ?? string.Empty;

        public bool? IsChecked
        {
            get => TryRead(() => Inner.IsChecked);
            set => Inner.IsChecked = value == true;
        }

        public void SetChecked(bool value, string itemText)
        {
            if (IsChecked == value)
            {
                return;
            }

            var toggle = Inner.Patterns.Toggle.PatternOrDefault
                ?? throw new InvalidOperationException(
                    $"Multi-select checkbox '{itemText}' does not expose a Toggle pattern.");
            toggle.Toggle();

            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(1))
            {
                if (IsChecked == value)
                {
                    return;
                }

                Thread.Sleep(25);
            }

            throw new InvalidOperationException(
                $"Multi-select checkbox '{itemText}' did not change to '{value}' after Toggle.");
        }
    }

    private sealed class FlaUiMultiSelectItemsControl : FlaUiControlBase<AutomationElement>, IMultiSelectItemsControl
    {
        private const int MaxScrollSteps = 256;
        private MultiSelectItemSnapshot[]? _lastItems;

        public FlaUiMultiSelectItemsControl(AutomationElement inner) : base(inner)
        {
        }

        public IReadOnlyList<string> Items => GetItems()
            .Select(static item => item.Text)
            .ToArray();

        public IReadOnlyList<string> SelectedItems => GetItems()
            .Where(static item => item.IsChecked)
            .Select(static item => item.Text)
            .ToArray();

        public void SetSelectedItems(IReadOnlyCollection<string> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            var normalizedValues = NormalizeRequestedItems(values);
            var items = ReadAllItems(item =>
            {
                var shouldBeChecked = normalizedValues.Contains(item.Text);
                item.Control.SetChecked(shouldBeChecked, item.Text);
            });
            _lastItems = items;
            ValidateAvailableItems(items.Select(static item => item.Text), normalizedValues);
        }

        private MultiSelectItemSnapshot[] GetItems()
        {
            return _lastItems ??= ReadAllItems();
        }

        private MultiSelectItemSnapshot[] ReadAllItems(Action<MultiSelectCheckBox>? processItem = null)
        {
            var items = new Dictionary<string, MultiSelectItemSnapshot>(StringComparer.OrdinalIgnoreCase);
            var scroll = FindVerticalScrollPattern(Inner);
            var scrollBarRange = scroll is null ? FindVerticalScrollBarRange(Inner) : null;
            AddCurrentItems(items, processItem);

            if (scroll is not null)
            {
                var startingPosition = scroll.VerticalScrollPercent.ValueOrDefault;
                TraverseItems(items, processItem, () => ScrollForward(scroll));
                if (startingPosition > 0)
                {
                    TraverseItems(items, processItem, () => ScrollBackward(scroll));
                }
            }
            else if (scrollBarRange is not null)
            {
                var startingPosition = scrollBarRange.Value.ValueOrDefault;
                var minimum = scrollBarRange.Minimum.ValueOrDefault;
                TraverseItems(items, processItem, () => ScrollForward(scrollBarRange));
                if (startingPosition > minimum)
                {
                    TraverseItems(items, processItem, () => ScrollBackward(scrollBarRange));
                }
            }

            return items.Values.ToArray();
        }

        private void TraverseItems(
            IDictionary<string, MultiSelectItemSnapshot> items,
            Action<MultiSelectCheckBox>? processItem,
            Func<bool> scroll)
        {
            for (var step = 0; step < MaxScrollSteps; step++)
            {
                if (!scroll())
                {
                    return;
                }

                AddCurrentItems(items, processItem);
            }

            throw new InvalidOperationException(
                $"Multi-select items traversal exceeded {MaxScrollSteps} vertical scroll steps.");
        }

        private void AddCurrentItems(
            IDictionary<string, MultiSelectItemSnapshot> items,
            Action<MultiSelectCheckBox>? processItem)
        {
            var currentItems = FindAutomationDescendants(Inner)
                .Where(candidate => candidate.ControlType == ControlType.CheckBox && IsVisibleItem(candidate))
                .Select(candidate =>
                {
                    var text = ReadAutomationElementText(candidate)?.Trim() ?? string.Empty;
                    return new MultiSelectCheckBox(text, new FlaUiCheckBoxControl(candidate.AsCheckBox()));
                })
                .Where(static item => !string.IsNullOrWhiteSpace(item.Text))
                .ToArray();

            if (currentItems
                    .Select(static item => item.Text)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != currentItems.Length)
            {
                throw new InvalidOperationException("Multi-select items container exposes duplicate item text.");
            }

            foreach (var item in currentItems)
            {
                if (items.ContainsKey(item.Text))
                {
                    continue;
                }

                processItem?.Invoke(item);
                items.Add(item.Text, new MultiSelectItemSnapshot(item.Text, item.Control.IsChecked == true));
            }
        }

        private bool IsVisibleItem(AutomationElement candidate)
        {
            if (TryRead(() => candidate.IsAvailable && !candidate.IsOffscreen) != true)
            {
                return false;
            }

            var viewport = TryRead(() => Inner.BoundingRectangle);
            var bounds = TryRead(() => candidate.BoundingRectangle);
            if (viewport.Width <= 0 || viewport.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return false;
            }

            return System.Drawing.Rectangle.Intersect(viewport, bounds) is { Width: > 0, Height: > 0 };
        }

        private static IScrollPattern? FindVerticalScrollPattern(AutomationElement root)
        {
            for (var current = root; current is not null; current = TryRead(() => current.Parent))
            {
                var scroll = current.Patterns.Scroll.PatternOrDefault;
                if (scroll?.VerticallyScrollable.ValueOrDefault == true)
                {
                    return scroll;
                }
            }

            return FindAutomationDescendants(root)
                .Select(static candidate => candidate.Patterns.Scroll.PatternOrDefault)
                .FirstOrDefault(static scroll => scroll?.VerticallyScrollable.ValueOrDefault == true);
        }

        private static IRangeValuePattern? FindVerticalScrollBarRange(AutomationElement root)
        {
            for (var current = root; current is not null; current = TryRead(() => current.Parent))
            {
                var range = FindAutomationDescendants(current)
                    .Where(static candidate => candidate.ControlType == ControlType.ScrollBar)
                    .Where(IsVerticalScrollBar)
                    .Select(static candidate => candidate.Patterns.RangeValue.PatternOrDefault)
                    .FirstOrDefault(static candidate =>
                        candidate is not null
                        && candidate.IsReadOnly.ValueOrDefault == false
                        && candidate.Maximum.ValueOrDefault > candidate.Minimum.ValueOrDefault);
                if (range is not null)
                {
                    return range;
                }

                if (current.ControlType == ControlType.Window)
                {
                    return null;
                }
            }

            return null;
        }

        private static bool IsVerticalScrollBar(AutomationElement element)
        {
            var orientation = element.FrameworkAutomationElement.Orientation.ValueOrDefault;
            if (orientation == OrientationType.Vertical)
            {
                return true;
            }

            var bounds = element.BoundingRectangle;
            return bounds.Height > bounds.Width;
        }

        private static bool ScrollBackward(IScrollPattern scroll)
        {
            var previousPosition = scroll.VerticalScrollPercent.ValueOrDefault;
            if (previousPosition <= 0)
            {
                return false;
            }

            scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeDecrement);
            Thread.Sleep(50);
            return scroll.VerticalScrollPercent.ValueOrDefault < previousPosition;
        }

        private static bool ScrollForward(IScrollPattern scroll)
        {
            var previousPosition = scroll.VerticalScrollPercent.ValueOrDefault;
            if (previousPosition < 0 || previousPosition >= 100)
            {
                return false;
            }

            scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            Thread.Sleep(50);
            return scroll.VerticalScrollPercent.ValueOrDefault > previousPosition;
        }

        private static bool ScrollForward(IRangeValuePattern scrollBarRange)
        {
            var previousPosition = scrollBarRange.Value.ValueOrDefault;
            var maximum = scrollBarRange.Maximum.ValueOrDefault;
            var minimum = scrollBarRange.Minimum.ValueOrDefault;
            var range = maximum - minimum;
            var positionTolerance = Math.Max(0.001, range / 10_000);
            if (previousPosition >= maximum - positionTolerance)
            {
                return false;
            }

            var largeChange = scrollBarRange.LargeChange.ValueOrDefault;
            largeChange = Math.Max(largeChange, range / 10);

            scrollBarRange.SetValue(Math.Min(maximum, previousPosition + largeChange));
            Thread.Sleep(50);
            return scrollBarRange.Value.ValueOrDefault > previousPosition + positionTolerance;
        }

        private static bool ScrollBackward(IRangeValuePattern scrollBarRange)
        {
            var previousPosition = scrollBarRange.Value.ValueOrDefault;
            var maximum = scrollBarRange.Maximum.ValueOrDefault;
            var minimum = scrollBarRange.Minimum.ValueOrDefault;
            var range = maximum - minimum;
            var positionTolerance = Math.Max(0.001, range / 10_000);
            if (previousPosition <= minimum + positionTolerance)
            {
                return false;
            }

            var largeChange = scrollBarRange.LargeChange.ValueOrDefault;
            largeChange = Math.Max(largeChange, range / 10);

            scrollBarRange.SetValue(Math.Max(minimum, previousPosition - largeChange));
            Thread.Sleep(50);
            return scrollBarRange.Value.ValueOrDefault < previousPosition - positionTolerance;
        }

        private static HashSet<string> NormalizeRequestedItems(IEnumerable<string> values)
        {
            var normalized = values.Select(static value => value?.Trim() ?? string.Empty).ToArray();
            if (normalized.Any(string.IsNullOrWhiteSpace)
                || normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            {
                throw new ArgumentException("Multi-select item values must be non-empty and distinct.", nameof(values));
            }

            return normalized.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static void ValidateAvailableItems(
            IEnumerable<string> availableValues,
            IReadOnlySet<string> requestedValues)
        {
            var available = availableValues.ToArray();
            if (available.Distinct(StringComparer.OrdinalIgnoreCase).Count() != available.Length)
            {
                throw new InvalidOperationException("Multi-select items container exposes duplicate item text.");
            }

            var missing = requestedValues.Where(value => !available.Contains(value, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Multi-select items were not found: [{string.Join(", ", missing)}].");
            }
        }

        private sealed record MultiSelectCheckBox(string Text, FlaUiCheckBoxControl Control);

        private sealed record MultiSelectItemSnapshot(string Text, bool IsChecked);
    }

    private sealed class FlaUiComboBoxControl : FlaUiControlBase<ComboBox>, IComboBoxControl, IReadableTextControl
    {
        public FlaUiComboBoxControl(ComboBox inner) : base(inner)
        {
        }

        public IReadOnlyList<IComboBoxItem> Items =>
            GetSelectableItems()
                .Select(ToComboBoxItem)
                .ToArray();

        public IComboBoxItem? SelectedItem
        {
            get
            {
                var selectedText = ReadSelectedText();
                if (string.IsNullOrWhiteSpace(selectedText))
                {
                    return null;
                }

                return new FlaUiComboBoxTextItem(selectedText, selectedText);
            }
        }

        public string Text => SelectedItem?.Text ?? string.Empty;

        public int SelectedIndex
        {
            get
            {
                var selected = SelectedItem;
                if (selected is null)
                {
                    return -1;
                }

                var selectedText = selected.Text;
                for (var index = 0; index < Items.Count; index++)
                {
                    if (string.Equals(NormalizeLookupText(Items[index].Text), NormalizeLookupText(selectedText), StringComparison.OrdinalIgnoreCase))
                    {
                        return index;
                    }
                }

                return -1;
            }
            set => SelectByIndex(value);
        }

        public void SelectByIndex(int index) => Select(index);

        public void Select(int index)
        {
            var items = GetSelectableItems();
            if (index < 0 || index >= items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Expand();

            var candidate = items[index];
            var expectedText = NormalizeLookupText(ReadAutomationElementText(candidate));

            try
            {
                Inner.Select(index);
                if (SelectionMatches(expectedText))
                {
                    return;
                }
            }
            catch
            {
                // Fall back to direct item interaction below.
            }

            TryClick(candidate);
        }

        public void Expand()
        {
            try
            {
                Inner.Expand();
            }
            catch
            {
                // Some providers do not expose expand directly.
            }
        }

        private List<AutomationElement> GetSelectableItems()
        {
            var items = new List<AutomationElement>();

            try
            {
                foreach (var item in Inner.Items)
                {
                    if (item is not null && !items.Contains(item))
                    {
                        items.Add(item);
                    }
                }
            }
            catch
            {
                // Some providers do not expose direct combo box items.
            }

            foreach (var candidate in FindAutomationDescendants(Inner))
            {
                if (candidate is null || items.Contains(candidate) || !IsComboItemCandidate(candidate))
                {
                    continue;
                }

                var text = ReadAutomationElementText(candidate);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    items.Add(candidate);
                }
            }

            return items;
        }

        private IComboBoxItem ToComboBoxItem(AutomationElement item)
        {
            if (item is ComboBoxItem comboBoxItem)
            {
                return new FlaUiComboBoxItem(comboBoxItem);
            }

            var text = ReadAutomationElementText(item) ?? string.Empty;
            return new FlaUiComboBoxTextItem(text, text);
        }

        private string? ReadSelectedText()
        {
            var selected = TryRead(() => Inner.SelectedItem);
            if (selected is ComboBoxItem comboBoxItem)
            {
                return ReadAutomationElementText(comboBoxItem);
            }

            if (selected is AutomationElement selectedElement)
            {
                return ReadAutomationElementText(selectedElement);
            }

            var selectedText = selected?.ToString();
            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                return selectedText;
            }

            var selectedCandidate = GetSelectableItems().FirstOrDefault(candidate =>
            {
                try
                {
                    return candidate.Patterns.SelectionItem.IsSupported
                        && candidate.Patterns.SelectionItem.Pattern.IsSelected.Value;
                }
                catch
                {
                    return false;
                }
            });

            if (selectedCandidate is not null)
            {
                return ReadAutomationElementText(selectedCandidate);
            }

            var valuePatternText = TryRead(() =>
            {
                if (Inner.Patterns.Value.IsSupported)
                {
                    return Inner.Patterns.Value.Pattern.Value;
                }

                return null;
            });
            if (!string.IsNullOrWhiteSpace(valuePatternText))
            {
                return valuePatternText;
            }

            return null;
        }

        private bool SelectionMatches(string expectedText)
        {
            if (string.IsNullOrWhiteSpace(expectedText))
            {
                return true;
            }

            return string.Equals(
                NormalizeLookupText(ReadSelectedText()),
                expectedText,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsComboItemCandidate(AutomationElement candidate)
        {
            return candidate.ControlType == ControlType.ListItem
                || candidate.ControlType == ControlType.Text
                || candidate.ControlType == ControlType.Button
                || candidate.ControlType == ControlType.DataItem;
        }

        private static bool TryClick(AutomationElement candidate)
        {
            try
            {
                candidate.Click();
                return true;
            }
            catch
            {
            }

            try
            {
                if (candidate.Patterns.Invoke.IsSupported)
                {
                    candidate.Patterns.Invoke.Pattern.Invoke();
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }

    private sealed class FlaUiComboBoxItem : IComboBoxItem
    {
        private readonly ComboBoxItem _inner;

        public FlaUiComboBoxItem(ComboBoxItem inner)
        {
            _inner = inner;
        }

        public string Text => _inner.Text ?? string.Empty;

        public string Name => _inner.Name ?? Text;
    }

    private sealed record FlaUiComboBoxTextItem(string Text, string Name) : IComboBoxItem;

    private sealed class FlaUiRadioButtonControl : FlaUiControlBase<RadioButton>, IRadioButtonControl, IReadableTextControl
    {
        public FlaUiRadioButtonControl(RadioButton inner) : base(inner)
        {
        }

        public string Text => ReadAutomationElementVisibleText(Inner) ?? string.Empty;

        public bool? IsChecked
        {
            get => TryRead(() => Inner.IsChecked);
            set => Inner.IsChecked = value == true;
        }
    }

    private sealed class FlaUiToggleButtonControl : FlaUiControlBase<ToggleButton>, IToggleButtonControl, IReadableTextControl
    {
        public FlaUiToggleButtonControl(ToggleButton inner) : base(inner)
        {
        }

        public string Text => ReadAutomationElementVisibleText(Inner) ?? string.Empty;

        public bool IsToggled => TryRead(() => Inner.IsToggled) == true;

        public void Toggle()
        {
            Inner.Toggle();
        }
    }

    private sealed class FlaUiSliderControl : FlaUiControlBase<Slider>, ISliderControl
    {
        public FlaUiSliderControl(Slider inner) : base(inner)
        {
        }

        public double Value
        {
            get => TryRead(() => Inner.Value);
            set => Inner.Value = value;
        }
    }

    private sealed class FlaUiProgressBarControl : FlaUiControlBase<ProgressBar>, IProgressBarControl
    {
        public FlaUiProgressBarControl(ProgressBar inner) : base(inner)
        {
        }

        public double Value => TryRead(() => Inner.Value);
    }

    private sealed class FlaUiCalendarControl : FlaUiControlBase<Calendar>, ICalendarControl
    {
        public FlaUiCalendarControl(Calendar inner) : base(inner)
        {
        }

        public IReadOnlyList<DateTime> SelectedDates => Inner.SelectedDates ?? Array.Empty<DateTime>();

        public void SelectDate(DateTime selectedDate)
        {
            Inner.SelectDate(selectedDate);
        }
    }

    private sealed class FlaUiDateTimePickerControl : FlaUiControlBase<DateTimePicker>, IDateTimePickerControl
    {
        public FlaUiDateTimePickerControl(DateTimePicker inner) : base(inner)
        {
        }

        public DateTime? SelectedDate
        {
            get => ReadSelectedDate();
            set
            {
                if (!TrySetSelectedDate(value))
                {
                    throw new InvalidOperationException("Unable to set the selected date for this DateTimePicker");
                }
            }
        }

        private DateTime? ReadSelectedDate()
        {
            var selectedDate = TryRead(() => Inner.SelectedDate);
            if (selectedDate.HasValue)
            {
                return selectedDate.Value;
            }

            var textInput = FindTextInput();
            if (textInput is null)
            {
                return null;
            }

            var text = TryRead(() => textInput.Text);
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var currentCultureDate))
            {
                return currentCultureDate;
            }

            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var invariantDate)
                ? invariantDate
                : null;
        }

        private bool TrySetSelectedDate(DateTime? value)
        {
            if (!value.HasValue)
            {
                try
                {
                    Inner.SelectedDate = null;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            try
            {
                Inner.SelectedDate = value;
                if (ReadSelectedDate()?.Date == value.Value.Date)
                {
                    return true;
                }
            }
            catch
            {
                // Fall back to text input below.
            }

            var textInput = FindTextInput();
            if (textInput is null)
            {
                return false;
            }

            var candidates = new[]
            {
                value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                value.Value.ToString("M/d/yyyy", CultureInfo.InvariantCulture),
                value.Value.ToShortDateString(),
                value.Value.ToString("d", CultureInfo.CurrentCulture)
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    textInput.Text = candidate;
                }
                catch
                {
                    continue;
                }

                if (ReadSelectedDate()?.Date == value.Value.Date)
                {
                    return true;
                }
            }

            return false;
        }

        private TextBox? FindTextInput()
        {
            var rootTextBox = TryRead(() => Inner.AsTextBox());
            if (rootTextBox is not null && TryRead(() => rootTextBox.IsAvailable))
            {
                return rootTextBox;
            }

            var descendant = Inner.FindAllDescendants()
                .FirstOrDefault(candidate => candidate.ControlType == ControlType.Edit);

            return descendant?.AsTextBox();
        }
    }

    private sealed class FlaUiSpinnerControl : FlaUiControlBase<Spinner>, ISpinnerControl
    {
        public FlaUiSpinnerControl(Spinner inner) : base(inner)
        {
        }

        public double Value
        {
            get => TryRead(() => Inner.Value);
            set => Inner.Value = value;
        }
    }

    private sealed class FlaUiTabControl : FlaUiControlBase<Tab>, ITabControl
    {
        public FlaUiTabControl(Tab inner) : base(inner)
        {
        }

        public IReadOnlyList<ITabItemControl> Items =>
            Inner.TabItems.Select(item => (ITabItemControl)new FlaUiTabItemControl(item)).ToArray();

        public void SelectTabItem(string itemText)
        {
            Inner.SelectTabItem(itemText);
        }
    }

    private sealed class FlaUiTabItemControl : FlaUiControlBase<TabItem>, ITabItemControl, IReadableTextControl
    {
        public FlaUiTabItemControl(TabItem inner) : base(inner)
        {
        }

        public string Text => ReadAutomationElementVisibleText(Inner) ?? string.Empty;

        public bool IsSelected => TryRead(() => Inner.IsSelected) == true;

        public void SelectTab()
        {
            try
            {
                Inner.Click();
            }
            catch
            {
                Inner.Select();
            }

            if (TryRead(() => Inner.IsSelected) != true)
            {
                Inner.Select();
            }
        }
    }

    private sealed class FlaUiTreeControl : FlaUiControlBase<Tree>, ITreeControl
    {
        public FlaUiTreeControl(Tree inner) : base(inner)
        {
        }

        public IReadOnlyList<ITreeItemControl> Items =>
            Inner.Items.Select(item => (ITreeItemControl)new FlaUiTreeItemControl(item)).ToArray();

        public ITreeItemControl? SelectedTreeItem
        {
            get
            {
                var selected = TryRead(() => Inner.SelectedTreeItem);
                return selected is null ? null : new FlaUiTreeItemControl(selected);
            }
        }
    }

    private sealed class FlaUiTreeItemControl : FlaUiControlBase<TreeItem>, ITreeItemControl, IReadableTextControl
    {
        private bool _selectedByInteraction;

        public FlaUiTreeItemControl(TreeItem inner) : base(inner)
        {
        }

        public bool IsSelected
        {
            get
            {
                if (_selectedByInteraction)
                {
                    return true;
                }

                if (TryRead(() => Inner.IsSelected) == true)
                {
                    return true;
                }

                try
                {
                    return Inner.Patterns.SelectionItem.IsSupported
                        && Inner.Patterns.SelectionItem.Pattern.IsSelected.Value;
                }
                catch
                {
                    return false;
                }
            }
            set
            {
                if (value)
                {
                    SelectNode();
                    return;
                }

                _selectedByInteraction = false;

                try
                {
                    Inner.IsSelected = false;
                }
                catch
                {
                    // Tree nodes without selection support cannot be force-unselected.
                }
            }
        }

        public string Text => TryRead(() => Inner.Text) ?? ReadAutomationElementVisibleText(Inner) ?? string.Empty;

        public IReadOnlyList<ITreeItemControl> Items =>
            Inner.Items.Select(item => (ITreeItemControl)new FlaUiTreeItemControl(item)).ToArray();

        public void Expand()
        {
            try
            {
                Inner.Expand();
            }
            catch
            {
                // Ignore expansion failures for leaf nodes.
            }
        }

        public void SelectNode()
        {
            if (TrySelectTreeItem(Inner))
            {
                _selectedByInteraction = true;
                return;
            }

            var normalizedTargetText = NormalizeLookupText(ReadAutomationElementText(Inner));
            foreach (var candidate in GetFallbackSelectionCandidates(normalizedTargetText))
            {
                if (TryActivateTreeSelectionCandidate(candidate))
                {
                    _selectedByInteraction = true;
                    return;
                }
            }
        }

        private IEnumerable<AutomationElement> GetFallbackSelectionCandidates(string normalizedTargetText)
        {
            return Inner.FindAllDescendants()
                .Where(candidate => candidate is not null && IsTreeSelectionCandidate(candidate))
                .OrderBy(candidate => GetTreeSelectionCandidatePriority(candidate, normalizedTargetText));
        }
    }

    private sealed class FlaUiGridControl : FlaUiControlBase<Grid>, IGridUserActionControl
    {
        public FlaUiGridControl(Grid inner) : base(inner)
        {
        }

        public IReadOnlyList<IGridRowControl> Rows =>
            Inner.Rows.Select(row => (IGridRowControl)new FlaUiGridRowControl(row)).ToArray();

        public IGridRowControl? GetRowByIndex(int index)
        {
            try
            {
                var row = Inner.GetRowByIndex(index);
                return row is null ? null : new FlaUiGridRowControl(row);
            }
            catch
            {
                var rows = Inner.Rows;
                return index >= 0 && index < rows.Length
                    ? new FlaUiGridRowControl(rows[index])
                    : null;
            }
        }

        public void OpenRow(int rowIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);

            Exception? rowException = null;
            var row = TryRead(() => Inner.GetRowByIndex(rowIndex));
            if (row is not null && TryDoubleClick(row, out rowException))
            {
                return;
            }

            var rows = TryRead(() => Inner.Rows) ?? Array.Empty<GridRow>();
            if (rowIndex >= rows.Length)
            {
                throw new InvalidOperationException($"Grid row {rowIndex} was not found in grid '{AutomationId}'.");
            }

            if (TryDoubleClick(rows[rowIndex], out var indexedRowException))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Grid row {rowIndex} in grid '{AutomationId}' could not be opened by double-click.",
                indexedRowException ?? rowException);
        }

        public void SortByColumn(string columnName)
        {
            ThrowUnsupportedUserAction(nameof(SortByColumn));
        }

        public void ScrollToEnd()
        {
            ThrowUnsupportedUserAction(nameof(ScrollToEnd));
        }

        public string CopyCell(int rowIndex, int columnIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);

            return GetRowByIndex(rowIndex)?.Cells.ElementAtOrDefault(columnIndex)?.Value
                   ?? string.Empty;
        }

        public void Export()
        {
            ThrowUnsupportedUserAction(nameof(Export));
        }

        private void ThrowUnsupportedUserAction(string actionName)
        {
            throw new System.NotSupportedException(
                $"Grid '{AutomationId}' does not support user action '{actionName}' in the FlaUI adapter.");
        }
    }

    private sealed class FlaUiVisualGridControl : IGridUserActionControl, IEditableGridControl
    {
        private readonly AutomationElement _searchRoot;
        private readonly IGridControl? _fallback;

        public FlaUiVisualGridControl(
            AutomationElement searchRoot,
            string automationId,
            IGridControl? fallback = null)
        {
            _searchRoot = searchRoot ?? throw new ArgumentNullException(nameof(searchRoot));
            _fallback = fallback;
            AutomationId = string.IsNullOrWhiteSpace(automationId)
                ? throw new ArgumentException("Automation id cannot be empty.", nameof(automationId))
                : automationId;
        }

        public string AutomationId { get; }

        public string Name => AutomationId;

        public bool IsEnabled => true;

        public IReadOnlyList<IGridRowControl> Rows =>
            ReadVisualRows();

        public IGridRowControl? GetRowByIndex(int index)
        {
            var row = ReadRows()
                .FirstOrDefault(candidate =>
                    ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row") == index);
            if (row is not null)
            {
                return new FlaUiVisualGridRowControl(row);
            }

            var cellRow = ReadCellRows()
                .FirstOrDefault(candidate => candidate.RowIndex == index);
            if (cellRow is not null && cellRow.Cells.Count > 0)
            {
                return new FlaUiVisualGridCellBackedRowControl(cellRow.Cells);
            }

            return _fallback?.GetRowByIndex(index);
        }

        public void EditCell(GridCellEditRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentOutOfRangeException.ThrowIfNegative(request.RowIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(request.ColumnIndex);
            ArgumentNullException.ThrowIfNull(request.Value);

            var cell = FindVisualCell(request.RowIndex, request.ColumnIndex)
                ?? throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] was not found in grid '{AutomationId}'.");

            if (request.CommitMode == GridCellEditCommitMode.Cancel)
            {
                return;
            }

            if (request.EditorKind == GridCellEditorKind.SearchPicker)
            {
                EditSearchPickerCell(cell, request);
                return;
            }

            if (_fallback is IEditableGridControl editableFallback)
            {
                editableFallback.EditCell(request);
                return;
            }

            throw new System.NotSupportedException(
                $"Visual grid '{AutomationId}' does not support '{request.EditorKind}' cell editing in the FlaUI adapter.");
        }

        public void OpenRow(int rowIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);

            var candidates = FindOpenRowTargets(rowIndex);
            Exception? lastException = null;
            foreach (var candidate in candidates)
            {
                if (TryDoubleClick(candidate, out lastException))
                {
                    return;
                }
            }

            if (_fallback is IGridUserActionControl actionFallback)
            {
                actionFallback.OpenRow(rowIndex);
                return;
            }

            var detail = lastException is null
                ? $"Visual grid row {rowIndex} was not found in grid '{AutomationId}'."
                : $"Visual grid row {rowIndex} in grid '{AutomationId}' could not be opened by double-click.";
            throw new InvalidOperationException(detail, lastException);
        }

        private AutomationElement? FindVisualCell(int rowIndex, int columnIndex)
        {
            var expectedAutomationId = $"{AutomationId}_Row{rowIndex}_Cell{columnIndex}";
            return FindAutomationDescendants(_searchRoot)
                .FirstOrDefault(candidate =>
                    string.Equals(
                        TryRead(() => candidate.AutomationId),
                        expectedAutomationId,
                        StringComparison.Ordinal));
        }

        private void EditSearchPickerCell(AutomationElement cell, GridCellEditRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SearchText))
            {
                throw new ArgumentException(
                    "Search text cannot be empty for a search-picker grid edit.",
                    nameof(request));
            }

            var editorElements = new[] { cell }
                .Concat(FindAutomationDescendants(cell))
                .ToArray();
            var searchInput = editorElements
                .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Edit);
            if (searchInput is null)
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose a ServerSearchComboBox input.");
            }

            new FlaUiTextBoxControl(searchInput.AsTextBox()).Enter(request.SearchText);

            var inputAutomationId = TryRead(() => searchInput.AutomationId);
            var editorAutomationId = TryGetEditorAutomationId(inputAutomationId)
                ?? throw new InvalidOperationException(
                    $"ServerSearchComboBox input '{inputAutomationId}' does not use the expected '<Root>_Input' automation contract.");
            var resultsAutomationId = $"{editorAutomationId}_Results";
            var results = WaitForProcessElementByAutomationId(resultsAutomationId, TimeSpan.FromMilliseconds(500));
            if (results is null)
            {
                var openButton = FindProcessElementByAutomationId($"{editorAutomationId}_OpenButton");
                if (openButton is not null)
                {
                    openButton.Click();
                    results = WaitForProcessElementByAutomationId(resultsAutomationId, TimeSpan.FromSeconds(5));
                }
            }

            if (results is null || TryRead(() => results.ControlType) != ControlType.List)
            {
                throw new InvalidOperationException(
                    $"ServerSearchComboBox results '{resultsAutomationId}' were not exposed as a ListBox for visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}'.");
            }

            new FlaUiListBoxControl(results.AsListBox()).SelectItem(request.Value);
        }

        private AutomationElement? WaitForProcessElementByAutomationId(string automationId, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            do
            {
                var element = FindProcessElementByAutomationId(automationId);
                if (element is not null && TryRead(() => element.IsAvailable))
                {
                    return element;
                }

                Thread.Sleep(50);
            }
            while (stopwatch.Elapsed < timeout);

            return null;
        }

        private AutomationElement? FindProcessElementByAutomationId(string automationId)
        {
            var condition = _searchRoot.Automation.ConditionFactory.ByAutomationId(automationId);
            var local = TryRead(() => _searchRoot.FindFirstDescendant(condition));
            if (local is not null && TryRead(() => local.IsAvailable))
            {
                return local;
            }

            var processId = TryRead(() => _searchRoot.FrameworkAutomationElement.ProcessId.ValueOrDefault);
            var desktop = TryRead(() => _searchRoot.Automation.GetDesktop());
            if (processId <= 0 || desktop is null)
            {
                return null;
            }

            var processRoots = TryRead(() => desktop.FindAllChildren(factory => factory.ByProcessId(processId)))
                ?? Array.Empty<AutomationElement>();
            foreach (var root in processRoots)
            {
                if (root is null || !TryRead(() => root.IsAvailable))
                {
                    continue;
                }

                var match = TryRead(() => root.FindFirstDescendant(condition));
                if (match is not null && TryRead(() => match.IsAvailable))
                {
                    return match;
                }
            }

            return null;
        }

        private static string? TryGetEditorAutomationId(string? inputAutomationId)
        {
            const string inputSuffix = "_Input";
            return !string.IsNullOrWhiteSpace(inputAutomationId)
                   && inputAutomationId.EndsWith(inputSuffix, StringComparison.Ordinal)
                ? inputAutomationId[..^inputSuffix.Length]
                : null;
        }

        public void SortByColumn(string columnName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

            if (_fallback is IGridUserActionControl actionFallback)
            {
                actionFallback.SortByColumn(columnName);
                return;
            }

            ThrowUnsupportedUserAction(nameof(SortByColumn));
        }

        public void ScrollToEnd()
        {
            if (_fallback is IGridUserActionControl actionFallback)
            {
                actionFallback.ScrollToEnd();
                return;
            }

            ThrowUnsupportedUserAction(nameof(ScrollToEnd));
        }

        public string CopyCell(int rowIndex, int columnIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);

            if (_fallback is IGridUserActionControl actionFallback)
            {
                return actionFallback.CopyCell(rowIndex, columnIndex);
            }

            ThrowUnsupportedUserAction(nameof(CopyCell));
            return string.Empty;
        }

        public void Export()
        {
            if (_fallback is IGridUserActionControl actionFallback)
            {
                actionFallback.Export();
                return;
            }

            ThrowUnsupportedUserAction(nameof(Export));
        }

        private AutomationElement[] FindOpenRowTargets(int rowIndex)
        {
            var targets = new List<AutomationElement>();
            var row = ReadRows()
                .FirstOrDefault(candidate =>
                    ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row") == rowIndex);
            if (row is not null)
            {
                targets.Add(row);
            }

            var cellRow = ReadCellRows()
                .FirstOrDefault(candidate => candidate.RowIndex == rowIndex);
            if (cellRow is not null)
            {
                targets.AddRange(cellRow.Cells);
            }

            return targets.ToArray();
        }

        private void ThrowUnsupportedUserAction(string actionName)
        {
            throw new System.NotSupportedException(
                $"Visual grid '{AutomationId}' does not support user action '{actionName}' in the FlaUI adapter.");
        }

        private IReadOnlyList<IGridRowControl> ReadVisualRows()
        {
            var rows = ReadRows()
                .Select(row => (IGridRowControl)new FlaUiVisualGridRowControl(row))
                .ToArray();
            if (rows.Length > 0)
            {
                return rows;
            }

            var cellRows = ReadCellRows()
                .Select(row => (IGridRowControl)new FlaUiVisualGridCellBackedRowControl(row.Cells))
                .ToArray();
            if (cellRows.Length > 0)
            {
                return cellRows;
            }

            return _fallback?.Rows ?? Array.Empty<IGridRowControl>();
        }

        private AutomationElement[] ReadRows()
        {
            var rowPrefix = $"{AutomationId}_Row";
            return FindAutomationDescendants(_searchRoot)
                .Where(candidate => IsVisualGridRow(candidate, rowPrefix))
                .OrderBy(candidate => ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row"))
                .ToArray();
        }

        private VisualGridCellRow[] ReadCellRows()
        {
            var cellPrefix = $"{AutomationId}_Row";
            return FindAutomationDescendants(_searchRoot)
                .Select(static candidate => new VisualGridCellCandidate(
                    candidate,
                    TryRead(() => candidate.AutomationId),
                    RowIndex: ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row"),
                    ColumnIndex: ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Cell")))
                .Where(candidate =>
                    candidate.AutomationId?.StartsWith(cellPrefix, StringComparison.Ordinal) == true
                    && candidate.RowIndex != int.MaxValue
                    && candidate.ColumnIndex != int.MaxValue
                    && HasExactVisualGridIndexSuffix(candidate.AutomationId, "_Cell"))
                .GroupBy(static candidate => candidate.RowIndex)
                .OrderBy(static group => group.Key)
                .Select(static group => new VisualGridCellRow(
                    group.Key,
                    group
                        .OrderBy(static candidate => candidate.ColumnIndex)
                        .Select(static candidate => candidate.Element)
                        .ToArray()))
                .ToArray();
        }
    }

    private sealed record VisualGridCellCandidate(
        AutomationElement Element,
        string? AutomationId,
        int RowIndex,
        int ColumnIndex);

    private sealed record VisualGridCellRow(
        int RowIndex,
        IReadOnlyList<AutomationElement> Cells);

    private static bool TryDoubleClick(AutomationElement element, out Exception? exception)
    {
        try
        {
            TryScrollIntoView(element);
            TryFocus(element);
            MoveMouseImmediatelyTo(element);
            Mouse.LeftDoubleClick();
            exception = null;
            return true;
        }
        catch (Exception mouseException)
        {
            try
            {
                if (TrySendDoubleClickToContainingWindow(element))
                {
                    exception = null;
                    return true;
                }
            }
            catch (Exception nativeException)
            {
                exception = new AggregateException(
                    "The standard and native double-click paths both failed.",
                    mouseException,
                    nativeException);
                return false;
            }

            exception = mouseException;
            return false;
        }
    }

    private static void MoveMouseImmediatelyTo(AutomationElement element)
    {
        if (element.TryGetClickablePoint(out var point))
        {
            Mouse.Position = point;
            return;
        }

        var bounds = element.BoundingRectangle;
        Mouse.Position = new System.Drawing.Point(
            bounds.Left + bounds.Width / 2,
            bounds.Top + bounds.Height / 2);
    }

    private static void TryScrollIntoView(AutomationElement element)
    {
        try
        {
            if (element.Patterns.ScrollItem.IsSupported)
            {
                element.Patterns.ScrollItem.Pattern.ScrollIntoView();
            }
        }
        catch
        {
            // Some visual bridge elements expose no scroll pattern; double-click can still work.
        }
    }

    private static void TryFocus(AutomationElement element)
    {
        try
        {
            element.Focus();
        }
        catch
        {
            // Focus is best-effort before the mouse gesture.
        }
    }

    private sealed class FlaUiVisualGridRowControl : IGridRowControl
    {
        private readonly AutomationElement _inner;

        public FlaUiVisualGridRowControl(AutomationElement inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<IGridCellControl> Cells =>
            ReadCells().Select(cell => (IGridCellControl)new FlaUiVisualGridCellControl(cell)).ToArray();

        private AutomationElement[] ReadCells()
        {
            var rowAutomationId = TryRead(() => _inner.AutomationId);
            if (string.IsNullOrWhiteSpace(rowAutomationId))
            {
                return Array.Empty<AutomationElement>();
            }

            var cellPrefix = $"{rowAutomationId}_Cell";
            return FindAutomationDescendants(_inner)
                .Where(candidate => IsVisualGridCell(candidate, cellPrefix))
                .OrderBy(candidate => ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Cell"))
                .ToArray();
        }
    }

    private sealed class FlaUiVisualGridCellBackedRowControl : IGridRowControl
    {
        private readonly IReadOnlyList<AutomationElement> _cells;

        public FlaUiVisualGridCellBackedRowControl(IReadOnlyList<AutomationElement> cells)
        {
            _cells = cells ?? throw new ArgumentNullException(nameof(cells));
        }

        public IReadOnlyList<IGridCellControl> Cells =>
            _cells.Select(cell => (IGridCellControl)new FlaUiVisualGridCellControl(cell)).ToArray();
    }

    private sealed class FlaUiVisualGridCellControl : IGridCellControl
    {
        private readonly AutomationElement _inner;

        public FlaUiVisualGridCellControl(AutomationElement inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Value => ReadVisualGridCellText(_inner) ?? string.Empty;
    }

    private sealed class FlaUiDataGridViewControl : FlaUiControlBase<DataGridView>, IGridControl
    {
        public FlaUiDataGridViewControl(DataGridView inner) : base(inner)
        {
        }

        public IReadOnlyList<IGridRowControl> Rows =>
            Inner.Rows.Select(row => (IGridRowControl)new FlaUiObjectGridRowControl(row)).ToArray();

        public IGridRowControl? GetRowByIndex(int index)
        {
            var rows = Inner.Rows;
            if (index < 0 || index >= rows.Length)
            {
                return null;
            }

            return new FlaUiObjectGridRowControl(rows[index]);
        }
    }

    private sealed class FlaUiGridRowControl : IGridRowControl
    {
        private readonly GridRow _inner;

        public FlaUiGridRowControl(GridRow inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<IGridCellControl> Cells =>
            _inner.Cells.Select(cell => (IGridCellControl)new FlaUiGridCellControl(cell)).ToArray();
    }

    private sealed class FlaUiGridCellControl : IGridCellControl
    {
        private readonly GridCell _inner;

        public FlaUiGridCellControl(GridCell inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Value =>
            ReadAutomationElementText(_inner)
            ?? ReadObjectText(TryRead(() => _inner.Value))
            ?? string.Empty;
    }

    private sealed class FlaUiObjectGridRowControl : IGridRowControl
    {
        private readonly object _inner;

        public FlaUiObjectGridRowControl(object inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<IGridCellControl> Cells
        {
            get
            {
                var cellsProperty = _inner.GetType().GetProperty("Cells");
                if (cellsProperty?.GetValue(_inner) is not System.Collections.IEnumerable cells)
                {
                    return Array.Empty<IGridCellControl>();
                }

                var result = new List<IGridCellControl>();
                foreach (var cell in cells)
                {
                    if (cell is not null)
                    {
                        result.Add(new FlaUiObjectGridCellControl(cell));
                    }
                }

                return result;
            }
        }
    }

    private sealed class FlaUiObjectGridCellControl : IGridCellControl
    {
        private readonly object _inner;

        public FlaUiObjectGridCellControl(object inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Value
        {
            get
            {
                var valueProperty = _inner.GetType().GetProperty("Value");
                return ReadObjectText(valueProperty?.GetValue(_inner))
                    ?? _inner.ToString()
                    ?? string.Empty;
            }
        }
    }

    private static string? ReadObjectText(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.GetType().GetProperty("Text")?.GetValue(value)?.ToString();
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var content = value.GetType().GetProperty("Content")?.GetValue(value)?.ToString();
        if (!string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        var display = value.ToString();
        return IsUsefulAutomationText(display) ? display : null;
    }

    private static AutomationElement[] FindAutomationDescendants(AutomationElement element)
    {
        return TryRead(() => element.FindAllDescendants()) ?? Array.Empty<AutomationElement>();
    }

    private static bool IsVisualGridRow(AutomationElement candidate, string rowPrefix)
    {
        var automationId = TryRead(() => candidate.AutomationId);
        return automationId?.StartsWith(rowPrefix, StringComparison.Ordinal) == true
            && !automationId.Contains("_Cell", StringComparison.Ordinal)
            && ParseVisualGridIndex(automationId, "_Row") != int.MaxValue;
    }

    private static bool IsVisualGridCell(AutomationElement candidate, string cellPrefix)
    {
        var automationId = TryRead(() => candidate.AutomationId);
        return automationId?.StartsWith(cellPrefix, StringComparison.Ordinal) == true
            && HasExactVisualGridIndexSuffix(automationId, "_Cell");
    }

    private static bool HasExactVisualGridIndexSuffix(string? automationId, string marker)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return false;
        }

        var markerIndex = automationId.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var digitStart = markerIndex + marker.Length;
        var digitEnd = digitStart;
        while (digitEnd < automationId.Length && char.IsDigit(automationId[digitEnd]))
        {
            digitEnd++;
        }

        return digitEnd > digitStart && digitEnd == automationId.Length;
    }

    private static int ParseVisualGridIndex(string? automationId, string marker)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return int.MaxValue;
        }

        var markerIndex = automationId.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return int.MaxValue;
        }

        var digitStart = markerIndex + marker.Length;
        var digitEnd = digitStart;
        while (digitEnd < automationId.Length && char.IsDigit(automationId[digitEnd]))
        {
            digitEnd++;
        }

        if (digitEnd == digitStart)
        {
            return int.MaxValue;
        }

        var digits = automationId[digitStart..digitEnd];
        return int.TryParse(digits, System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : int.MaxValue;
    }

    private static string? ReadVisualGridCellText(AutomationElement element)
    {
        var name = TryRead(() => element.Name);
        if (IsUsefulAutomationText(name))
        {
            return name;
        }

        return FindAutomationDescendants(element)
            .Select(static candidate => TryRead(() => candidate.Name))
            .FirstOrDefault(IsUsefulAutomationText);
    }

    private static string? ReadAutomationElementText(AutomationElement element)
    {
        if (element is null)
        {
            return null;
        }

        var name = TryRead(() => element.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        try
        {
            if (element.Patterns.Value.IsSupported)
            {
                var value = element.Patterns.Value.Pattern.Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // Ignore pattern access errors and continue with fallbacks.
        }

        var descendants = FindAutomationDescendants(element);
        var textChild = descendants
            .FirstOrDefault(candidate => candidate.ControlType == ControlType.Text);
        if (textChild is not null)
        {
            var textChildName = TryRead(() => textChild.Name);
            if (IsUsefulAutomationText(textChildName))
            {
                return textChildName;
            }
        }

        var namedDescendant = descendants
            .Select(static candidate => TryRead(() => candidate.Name))
            .FirstOrDefault(IsUsefulAutomationText);
        if (!string.IsNullOrWhiteSpace(namedDescendant))
        {
            return namedDescendant;
        }

        var automationId = TryRead(() => element.AutomationId);
        return string.IsNullOrWhiteSpace(automationId) ? name : automationId;
    }

    private static string? ReadAutomationElementVisibleText(AutomationElement element)
    {
        if (element is null)
        {
            return null;
        }

        var name = TryRead(() => element.Name);
        if (IsUsefulAutomationText(name))
        {
            return name;
        }

        try
        {
            if (element.Patterns.Value.IsSupported)
            {
                var value = element.Patterns.Value.Pattern.Value;
                if (IsUsefulAutomationText(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // Ignore pattern access errors and continue with text descendants.
        }

        var textChild = FindAutomationDescendants(element)
            .FirstOrDefault(candidate => candidate.ControlType == ControlType.Text);
        if (textChild is not null)
        {
            var textChildName = TryRead(() => textChild.Name);
            if (IsUsefulAutomationText(textChildName))
            {
                return textChildName;
            }
        }

        return FindAutomationDescendants(element)
            .Select(static candidate => TryRead(() => candidate.Name))
            .FirstOrDefault(IsUsefulAutomationText);
    }

    private static bool IsUsefulAutomationText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !value.StartsWith("Avalonia.Controls.", StringComparison.Ordinal)
            && !string.Equals(value, "TextBlock", StringComparison.Ordinal);
    }

    private static bool TrySelectTreeItem(TreeItem treeItem)
    {
        try
        {
            if (treeItem.Patterns.SelectionItem.IsSupported)
            {
                treeItem.Patterns.SelectionItem.Pattern.Select();
                return true;
            }
        }
        catch
        {
        }

        if (TrySendSpaceToFocusedTreeItem(treeItem))
        {
            return true;
        }

        try
        {
            treeItem.Select();
            return true;
        }
        catch
        {
        }

        return false;
    }

    private static bool TrySendSpaceToFocusedTreeItem(TreeItem treeItem)
    {
        try
        {
            treeItem.Focus();
            if (TryRead(() => treeItem.Properties.HasKeyboardFocus.Value) != true)
            {
                return false;
            }

            var windowHandle = FindNativeWindowHandle(treeItem);
            if (windowHandle == IntPtr.Zero)
            {
                return false;
            }

            SendMessage(
                windowHandle,
                WindowMessageKeyDown,
                new IntPtr(VirtualKeySpace),
                new IntPtr(SpaceKeyDownData));
            SendMessage(
                windowHandle,
                WindowMessageKeyUp,
                new IntPtr(VirtualKeySpace),
                new IntPtr(SpaceKeyUpData));
            return true;
        }
        catch
        {
        }

        return false;
    }

    private static bool TrySendDoubleClickToContainingWindow(AutomationElement element)
    {
        if (!element.TryGetClickablePoint(out var screenPoint))
        {
            var bounds = element.BoundingRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return false;
            }

            screenPoint = new System.Drawing.Point(
                bounds.Left + bounds.Width / 2,
                bounds.Top + bounds.Height / 2);
        }

        var windowHandle = FindNativeWindowHandle(element);
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var clientPoint = new NativePoint(screenPoint.X, screenPoint.Y);
        if (!ScreenToClient(windowHandle, ref clientPoint))
        {
            return false;
        }

        var packedPoint = new IntPtr(
            (clientPoint.X & 0xFFFF) | ((clientPoint.Y & 0xFFFF) << 16));
        SendMessage(
            windowHandle,
            WindowMessageLeftButtonDown,
            new IntPtr(MouseKeyLeftButton),
            packedPoint);
        SendMessage(windowHandle, WindowMessageLeftButtonUp, IntPtr.Zero, packedPoint);
        Thread.Sleep(50);
        SendMessage(
            windowHandle,
            WindowMessageLeftButtonDown,
            new IntPtr(MouseKeyLeftButton),
            packedPoint);
        SendMessage(windowHandle, WindowMessageLeftButtonUp, IntPtr.Zero, packedPoint);
        return true;
    }

    private static IntPtr FindNativeWindowHandle(AutomationElement element)
    {
        AutomationElement? current = element;
        while (current is not null)
        {
            var windowHandle = TryRead(
                () => current.FrameworkAutomationElement.NativeWindowHandle.ValueOrDefault);
            if (windowHandle != IntPtr.Zero)
            {
                return windowHandle;
            }

            current = TryRead(() => current.Parent);
        }

        return IntPtr.Zero;
    }

    private static bool TryActivateTreeSelectionCandidate(AutomationElement candidate)
    {
        return TryClickTreeSelectionCandidate(candidate)
            || TryInvokeTreeSelectionCandidate(candidate)
            || TrySelectTreeSelectionCandidate(candidate);
    }

    private static bool TryClickTreeSelectionCandidate(AutomationElement candidate)
    {
        try
        {
            candidate.Click();
            return true;
        }
        catch
        {
        }

        return false;
    }

    private static bool TryInvokeTreeSelectionCandidate(AutomationElement candidate)
    {
        try
        {
            if (candidate.Patterns.Invoke.IsSupported)
            {
                candidate.Patterns.Invoke.Pattern.Invoke();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TrySelectTreeSelectionCandidate(AutomationElement candidate)
    {
        try
        {
            if (candidate.Patterns.SelectionItem.IsSupported)
            {
                candidate.Patterns.SelectionItem.Pattern.Select();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsTreeSelectionCandidate(AutomationElement candidate)
    {
        return candidate.ControlType == ControlType.Text
            || candidate.ControlType == ControlType.Button
            || candidate.ControlType == ControlType.Custom
            || candidate.ControlType == ControlType.Pane
            || candidate.ControlType == ControlType.Group
            || candidate.ControlType == ControlType.TreeItem
            || candidate.ControlType == ControlType.ListItem;
    }

    private static int GetTreeSelectionCandidatePriority(AutomationElement candidate, string normalizedTargetText)
    {
        var candidateText = NormalizeLookupText(ReadAutomationElementText(candidate));
        if (!string.IsNullOrWhiteSpace(normalizedTargetText)
            && string.Equals(candidateText, normalizedTargetText, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return candidate.ControlType switch
        {
            ControlType.Text => 1,
            ControlType.Button => 2,
            ControlType.Custom => 3,
            ControlType.Pane => 4,
            ControlType.Group => 5,
            ControlType.TreeItem => 6,
            ControlType.ListItem => 7,
            _ => int.MaxValue
        };
    }

    private static string NormalizeLookupText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr windowHandle, ref NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }
}
