using AppAutomation.Abstractions;
using System.Diagnostics;
using AppAutomation.FlaUI.Extensions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using CultureInfo = System.Globalization.CultureInfo;
using DateTimeStyles = System.Globalization.DateTimeStyles;
using System.Text;

namespace AppAutomation.FlaUI.Automation;

public sealed class FlaUiControlResolver : IUiControlResolver, IUiArtifactCollector
{
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
            UiControlType.Grid => new FlaUiGridControl(FindElement(definition).AsGrid()),
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

        var normalized = locatorValue.Trim().ToLowerInvariant();
        return _window.FindAllDescendants()
            .FirstOrDefault(candidate =>
            {
                if (!candidate.IsAvailable)
                {
                    return false;
                }

                var automationId = TryRead(() => candidate.AutomationId)?.ToLowerInvariant();
                return automationId is not null && (automationId == normalized || automationId.StartsWith(normalized, StringComparison.Ordinal));
            });
    }

    private AutomationElement? SearchByName(string locatorValue)
    {
        var direct = _window.FindAllDescendants(factory => factory.ByName(locatorValue));
        if (direct.Length > 0)
        {
            return direct.FirstOrDefault(candidate => candidate?.IsAvailable == true);
        }

        var normalized = locatorValue.Trim().ToLowerInvariant();
        return _window.FindAllDescendants()
            .FirstOrDefault(candidate =>
            {
                if (!candidate.IsAvailable)
                {
                    return false;
                }

                var name = TryRead(() => candidate.Name)?.ToLowerInvariant();
                return name is not null && (name == normalized || name.Contains(normalized, StringComparison.Ordinal));
            });
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

    private abstract class FlaUiControlBase<TControl> : IUiControl
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

    private sealed class FlaUiControl : FlaUiControlBase<AutomationElement>
    {
        public FlaUiControl(AutomationElement inner) : base(inner)
        {
        }
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

    private sealed class FlaUiButtonControl : FlaUiControlBase<Button>, IButtonControl
    {
        public FlaUiButtonControl(Button inner) : base(inner)
        {
        }

        public void Invoke()
        {
            Inner.Invoke();
        }
    }

    private sealed class FlaUiLabelControl : FlaUiControlBase<Label>, ILabelControl
    {
        public FlaUiLabelControl(Label inner) : base(inner)
        {
        }

        public string Text => TryRead(() => Inner.Text) ?? Name;
    }

    private sealed class FlaUiListBoxControl : FlaUiControlBase<ListBox>, IListBoxControl
    {
        public FlaUiListBoxControl(ListBox inner) : base(inner)
        {
        }

        public IReadOnlyList<IListBoxItem> Items =>
            ReadItems();

        private IReadOnlyList<IListBoxItem> ReadItems()
        {
            try
            {
                var directItems = Inner.Items
                    .Select(item => (IListBoxItem)new FlaUiListBoxItem(item.Text, item.Name))
                    .ToArray();
                if (directItems.Length > 0)
                {
                    return directItems;
                }
            }
            catch
            {
                // fallback to descendant scan
            }

            var descendants = Inner
                .FindAllDescendants()
                .Where(node => node != Inner)
                .ToArray();

            var listItemElements = descendants
                .Where(node => node.ControlType == ControlType.ListItem)
                .Select(node => (IListBoxItem)new FlaUiListBoxItem(node.Name, node.Name))
                .Where(static item => !string.IsNullOrWhiteSpace(item.Text) || !string.IsNullOrWhiteSpace(item.Name))
                .ToArray();
            if (listItemElements.Length > 0)
            {
                return listItemElements;
            }

            var textElements = descendants
                .Where(node => node.ControlType == ControlType.Text)
                .Select(node => (IListBoxItem)new FlaUiListBoxItem(node.Name, node.Name))
                .Where(static item => !string.IsNullOrWhiteSpace(item.Text) || !string.IsNullOrWhiteSpace(item.Name))
                .ToArray();
            if (textElements.Length > 0)
            {
                return textElements;
            }

            return descendants
                .Select(node => (IListBoxItem)new FlaUiListBoxItem(node.Name, node.Name))
                .Where(static item => !string.IsNullOrWhiteSpace(item.Text) || !string.IsNullOrWhiteSpace(item.Name))
                .ToArray();
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

    private sealed class FlaUiCheckBoxControl : FlaUiControlBase<CheckBox>, ICheckBoxControl
    {
        public FlaUiCheckBoxControl(CheckBox inner) : base(inner)
        {
        }

        public bool? IsChecked
        {
            get => TryRead(() => Inner.IsChecked);
            set => Inner.IsChecked = value == true;
        }
    }

    private sealed class FlaUiComboBoxControl : FlaUiControlBase<ComboBox>, IComboBoxControl
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

            foreach (var item in Inner.Items)
            {
                if (item is not null && !items.Contains(item))
                {
                    items.Add(item);
                }
            }

            foreach (var candidate in Inner.FindAllDescendants())
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

    private sealed class FlaUiRadioButtonControl : FlaUiControlBase<RadioButton>, IRadioButtonControl
    {
        public FlaUiRadioButtonControl(RadioButton inner) : base(inner)
        {
        }

        public bool? IsChecked
        {
            get => TryRead(() => Inner.IsChecked);
            set => Inner.IsChecked = value == true;
        }
    }

    private sealed class FlaUiToggleButtonControl : FlaUiControlBase<ToggleButton>, IToggleButtonControl
    {
        public FlaUiToggleButtonControl(ToggleButton inner) : base(inner)
        {
        }

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

    private sealed class FlaUiTabItemControl : FlaUiControlBase<TabItem>, ITabItemControl
    {
        public FlaUiTabItemControl(TabItem inner) : base(inner)
        {
        }

        public bool IsSelected => TryRead(() => Inner.IsSelected) == true;

        public void SelectTab()
        {
            Inner.Select();
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

    private sealed class FlaUiTreeItemControl : FlaUiControlBase<TreeItem>, ITreeItemControl
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

        public string Text => TryRead(() => Inner.Text) ?? ReadAutomationElementText(Inner) ?? Name;

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
            try
            {
                Inner.Select();
                _selectedByInteraction = true;
                return;
            }
            catch
            {
            }

            try
            {
                Inner.Click();
                _selectedByInteraction = true;
                return;
            }
            catch
            {
            }

            if (TryRead(() => Inner.Patterns.SelectionItem.IsSupported) == true)
            {
                Inner.Patterns.SelectionItem.Pattern.Select();
                _selectedByInteraction = true;
            }
        }
    }

    private sealed class FlaUiGridControl : FlaUiControlBase<Grid>, IGridControl
    {
        public FlaUiGridControl(Grid inner) : base(inner)
        {
        }

        public IReadOnlyList<IGridRowControl> Rows =>
            Inner.Rows.Select(row => (IGridRowControl)new FlaUiGridRowControl(row)).ToArray();

        public IGridRowControl? GetRowByIndex(int index)
        {
            var row = Inner.GetRowByIndex(index);
            return row is null ? null : new FlaUiGridRowControl(row);
        }
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

        public string Value => TryRead(() => _inner.Value) ?? string.Empty;
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
                return valueProperty?.GetValue(_inner)?.ToString() ?? _inner.ToString() ?? string.Empty;
            }
        }
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

        var textChild = element.FindAllDescendants()
            .FirstOrDefault(candidate => candidate.ControlType == ControlType.Text);
        if (textChild is not null)
        {
            var textChildName = TryRead(() => textChild.Name);
            if (!string.IsNullOrWhiteSpace(textChildName))
            {
                return textChildName;
            }
        }

        var automationId = TryRead(() => element.AutomationId);
        return string.IsNullOrWhiteSpace(automationId) ? name : automationId;
    }

    private static string NormalizeLookupText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
