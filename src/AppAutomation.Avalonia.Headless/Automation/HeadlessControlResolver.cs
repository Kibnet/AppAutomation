using AppAutomation.Abstractions;
using AvaloniaWindow = Avalonia.Controls.Window;
using AppAutomation.Avalonia.Headless.Internal.AutomationModel;
using AppAutomation.Avalonia.Headless.Internal.AutomationModel.Conditions;
using System.Text;

namespace AppAutomation.Avalonia.Headless.Automation;

public sealed class HeadlessControlResolver : IUiControlResolver, IUiArtifactCollector
{
    private readonly Window _window;
    private readonly ConditionFactory _conditionFactory;

    public HeadlessControlResolver(AvaloniaWindow window)
    {
        _window = new Window(window ?? throw new ArgumentNullException(nameof(window)));
        _conditionFactory = new ConditionFactory();
    }

    public UiRuntimeCapabilities Capabilities { get; } = new(
        AdapterId: "avalonia-headless",
        SupportsGridCellAccess: false,
        SupportsCalendarRangeSelection: false,
        SupportsTreeNodeExpansionState: false,
        SupportsRawNativeHandles: false,
        SupportsScreenshots: false);

    public TControl Resolve<TControl>(UiControlDefinition definition)
        where TControl : class
    {
        ArgumentNullException.ThrowIfNull(definition);

        object resolved = definition.ControlType switch
        {
            UiControlType.TextBox => new HeadlessTextBoxControl(FindElement(definition).AsTextBox()),
            UiControlType.Button => new HeadlessButtonControl(FindElement(definition).AsButton()),
            UiControlType.Label => new HeadlessLabelControl(FindElement(definition).AsLabel()),
            UiControlType.ListBox => new HeadlessListBoxControl(FindElement(definition).AsListBox()),
            UiControlType.CheckBox => new HeadlessCheckBoxControl(FindElement(definition).AsCheckBox()),
            UiControlType.ComboBox => new HeadlessComboBoxControl(FindElement(definition).AsComboBox()),
            UiControlType.RadioButton => new HeadlessRadioButtonControl(FindElement(definition).AsRadioButton()),
            UiControlType.ToggleButton => new HeadlessToggleButtonControl(FindElement(definition).AsToggleButton()),
            UiControlType.Slider => new HeadlessSliderControl(FindElement(definition).AsSlider()),
            UiControlType.ProgressBar => new HeadlessProgressBarControl(FindElement(definition).AsProgressBar()),
            UiControlType.Calendar => new HeadlessCalendarControl(FindElement(definition).AsCalendar()),
            UiControlType.DateTimePicker => new HeadlessDateTimePickerControl(FindElement(definition).AsDateTimePicker()),
            UiControlType.Spinner => new HeadlessSpinnerControl(FindElement(definition).AsSpinner()),
            UiControlType.Tab => new HeadlessTabControl(FindElement(definition).AsTab()),
            UiControlType.TabItem => new HeadlessTabItemControl(FindElement(definition).AsTabItem()),
            UiControlType.Tree => new HeadlessTreeControl(FindElement(definition).AsTree()),
            UiControlType.TreeItem => new HeadlessTreeItemControl(FindElement(definition).AsTreeItem()),
            UiControlType.DataGridView or UiControlType.Grid => new HeadlessGridControl(FindGrid(definition)),
            UiControlType.DataGridViewRow or UiControlType.GridRow => new HeadlessGridRowControl(FindGridRow(definition)),
            UiControlType.DataGridViewCell or UiControlType.GridCell => new HeadlessGridCellControl(FindGridCell(definition)),
            _ => new HeadlessUiControl(FindElement(definition))
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

        var logicalTree = BuildLogicalTreeSnapshot();
        var controlState = BuildControlStateSnapshot(failureContext.LocatorValue, failureContext.LocatorKind);

        IReadOnlyList<UiFailureArtifact> artifacts =
        [
            new UiFailureArtifact(
                Kind: "logical-tree",
                LogicalName: "logical-tree",
                RelativePath: "artifacts/ui-failures/avalonia-headless/logical-tree.txt",
                ContentType: "text/plain",
                IsRequiredByContract: true,
                InlineTextPreview: logicalTree),
            new UiFailureArtifact(
                Kind: "control-state",
                LogicalName: "control-state",
                RelativePath: "artifacts/ui-failures/avalonia-headless/control-state.txt",
                ContentType: "text/plain",
                IsRequiredByContract: true,
                InlineTextPreview: controlState)
        ];

        return ValueTask.FromResult(artifacts);
    }

    private Grid FindGrid(UiControlDefinition definition)
    {
        return definition.ControlType == UiControlType.DataGridView
            ? FindElement(definition).AsDataGridView()
            : FindElement(definition).AsGrid();
    }

    private GridRow FindGridRow(UiControlDefinition definition)
    {
        return FindElement(definition).AsGridRow();
    }

    private GridCell FindGridCell(UiControlDefinition definition)
    {
        return FindElement(definition).AsGridCell();
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

        throw new InvalidOperationException(
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
        var normalized = locatorValue.Trim();
        return _window.FindAllDescendants()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.AutomationId, normalized, StringComparison.Ordinal));
    }

    private AutomationElement? SearchByName(string locatorValue)
    {
        var normalized = locatorValue.Trim();
        return _window.FindAllDescendants()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, normalized, StringComparison.Ordinal)
                || string.Equals(candidate.Name, normalized, StringComparison.OrdinalIgnoreCase));
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

    private string BuildControlStateSnapshot(string? locatorValue, UiLocatorKind? locatorKind)
    {
        if (string.IsNullOrWhiteSpace(locatorValue) || locatorKind is null)
        {
            return "No locator context available.";
        }

        var element = locatorKind.Value switch
        {
            UiLocatorKind.AutomationId => SearchByAutomationId(locatorValue),
            UiLocatorKind.Name => SearchByName(locatorValue),
            _ => null
        };

        if (element is null)
        {
            return $"Element [{locatorKind}:{locatorValue}] was not found during artifact collection.";
        }

        var builder = new StringBuilder();
        builder.Append("ControlType=").Append(TryRead(() => element.ControlType.ToString()) ?? "<unknown>").AppendLine();
        builder.Append("AutomationId=").Append(TryRead(() => element.AutomationId) ?? string.Empty).AppendLine();
        builder.Append("Name=").Append(TryRead(() => element.Name) ?? string.Empty).AppendLine();
        builder.Append("IsEnabled=").Append(TryRead(() => element.IsEnabled) is bool isEnabled ? isEnabled.ToString() : "<unknown>");
        return builder.ToString();
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

    private abstract class HeadlessControlBase<TControl> : IUiControl
        where TControl : AutomationElement
    {
        protected HeadlessControlBase(TControl inner)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        protected TControl Inner { get; }

        public string AutomationId => Inner.AutomationId ?? string.Empty;

        public string Name => Inner.Name ?? string.Empty;

        public bool IsEnabled => Inner.IsEnabled;
    }

    private sealed class HeadlessUiControl : HeadlessControlBase<AutomationElement>
    {
        public HeadlessUiControl(AutomationElement inner) : base(inner)
        {
        }
    }

    private sealed class HeadlessTextBoxControl : HeadlessControlBase<TextBox>, ITextBoxControl
    {
        public HeadlessTextBoxControl(TextBox inner) : base(inner)
        {
        }

        public string Text
        {
            get => Inner.Text ?? string.Empty;
            set => Inner.Text = value;
        }

        public void Enter(string value)
        {
            Inner.Enter(value);
        }
    }

    private sealed class HeadlessButtonControl : HeadlessControlBase<Button>, IButtonControl
    {
        public HeadlessButtonControl(Button inner) : base(inner)
        {
        }

        public void Invoke()
        {
            Inner.Invoke();
        }
    }

    private sealed class HeadlessLabelControl : HeadlessControlBase<Label>, ILabelControl
    {
        public HeadlessLabelControl(Label inner) : base(inner)
        {
        }

        public string Text => Inner.Text ?? Name;
    }

    private sealed class HeadlessListBoxControl : HeadlessControlBase<ListBox>, ISelectableListBoxControl
    {
        public HeadlessListBoxControl(ListBox inner) : base(inner)
        {
        }

        public IReadOnlyList<IListBoxItem> Items =>
            Inner.Items.Select(item => (IListBoxItem)new HeadlessListBoxItem(item)).ToArray();

        public string? SelectedItemText => Inner.SelectedItemText;

        public void SelectItem(string itemText)
        {
            Inner.SelectItem(itemText);
        }
    }

    private sealed class HeadlessListBoxItem : IListBoxItem
    {
        private readonly ListBoxItem _inner;

        public HeadlessListBoxItem(ListBoxItem inner)
        {
            _inner = inner;
        }

        public string? Text => _inner.Text;

        public string? Name => _inner.Name;
    }

    private sealed class HeadlessCheckBoxControl : HeadlessControlBase<CheckBox>, ICheckBoxControl
    {
        public HeadlessCheckBoxControl(CheckBox inner) : base(inner)
        {
        }

        public bool? IsChecked
        {
            get => Inner.IsChecked;
            set => Inner.IsChecked = value;
        }
    }

    private sealed class HeadlessComboBoxControl : HeadlessControlBase<ComboBox>, IComboBoxControl
    {
        public HeadlessComboBoxControl(ComboBox inner) : base(inner)
        {
        }

        public IReadOnlyList<IComboBoxItem> Items =>
            Inner.Items.Select(item => (IComboBoxItem)new HeadlessComboBoxItem(item)).ToArray();

        public IComboBoxItem? SelectedItem => Inner.SelectedItem switch
        {
            ComboBoxItem comboBoxItem => new HeadlessComboBoxItem(comboBoxItem),
            null => null,
            _ => new HeadlessComboBoxTextItem(Inner.SelectedItem?.ToString() ?? string.Empty, Inner.SelectedItem?.ToString() ?? string.Empty)
        };

        public int SelectedIndex
        {
            get => Inner.SelectedIndex;
            set => Inner.SelectedIndex = value;
        }

        public void SelectByIndex(int index)
        {
            Inner.Select(index);
        }

        public void Expand()
        {
            Inner.Expand();
        }
    }

    private sealed class HeadlessComboBoxItem : IComboBoxItem
    {
        private readonly ComboBoxItem _inner;

        public HeadlessComboBoxItem(ComboBoxItem inner)
        {
            _inner = inner;
        }

        public string Text => _inner.Text ?? string.Empty;

        public string Name => _inner.Name ?? Text;
    }

    private sealed record HeadlessComboBoxTextItem(string Text, string Name) : IComboBoxItem;

    private sealed class HeadlessRadioButtonControl : HeadlessControlBase<RadioButton>, IRadioButtonControl
    {
        public HeadlessRadioButtonControl(RadioButton inner) : base(inner)
        {
        }

        public bool? IsChecked
        {
            get => Inner.IsChecked;
            set => Inner.IsChecked = value;
        }
    }

    private sealed class HeadlessToggleButtonControl : HeadlessControlBase<ToggleButton>, IToggleButtonControl
    {
        public HeadlessToggleButtonControl(ToggleButton inner) : base(inner)
        {
        }

        public bool IsToggled => Inner.IsToggled;

        public void Toggle()
        {
            Inner.Toggle();
        }
    }

    private sealed class HeadlessSliderControl : HeadlessControlBase<Slider>, ISliderControl
    {
        public HeadlessSliderControl(Slider inner) : base(inner)
        {
        }

        public double Value
        {
            get => Inner.Value;
            set => Inner.Value = value;
        }
    }

    private sealed class HeadlessProgressBarControl : HeadlessControlBase<ProgressBar>, IProgressBarControl
    {
        public HeadlessProgressBarControl(ProgressBar inner) : base(inner)
        {
        }

        public double Value => Inner.Value;
    }

    private sealed class HeadlessCalendarControl : HeadlessControlBase<Calendar>, ICalendarControl
    {
        public HeadlessCalendarControl(Calendar inner) : base(inner)
        {
        }

        public IReadOnlyList<DateTime> SelectedDates => Inner.SelectedDates ?? Array.Empty<DateTime>();

        public void SelectDate(DateTime selectedDate)
        {
            Inner.SelectDate(selectedDate);
        }
    }

    private sealed class HeadlessDateTimePickerControl : HeadlessControlBase<DateTimePicker>, IDateTimePickerControl
    {
        public HeadlessDateTimePickerControl(DateTimePicker inner) : base(inner)
        {
        }

        public DateTime? SelectedDate
        {
            get => Inner.SelectedDate;
            set => Inner.SelectedDate = value;
        }
    }

    private sealed class HeadlessSpinnerControl : HeadlessControlBase<Spinner>, ISpinnerControl
    {
        public HeadlessSpinnerControl(Spinner inner) : base(inner)
        {
        }

        public double Value
        {
            get => Inner.Value;
            set => Inner.Value = value;
        }
    }

    private sealed class HeadlessTabControl : HeadlessControlBase<Tab>, ITabControl
    {
        public HeadlessTabControl(Tab inner) : base(inner)
        {
        }

        public IReadOnlyList<ITabItemControl> Items =>
            Inner.Items.Select(item => (ITabItemControl)new HeadlessTabItemControl(item)).ToArray();

        public void SelectTabItem(string itemText)
        {
            Inner.SelectTabItem(itemText);
        }
    }

    private sealed class HeadlessTabItemControl : HeadlessControlBase<TabItem>, ITabItemControl
    {
        public HeadlessTabItemControl(TabItem inner) : base(inner)
        {
        }

        public bool IsSelected => Inner.IsSelected;

        public void SelectTab()
        {
            Inner.Select();
        }
    }

    private sealed class HeadlessTreeControl : HeadlessControlBase<Tree>, ITreeControl
    {
        public HeadlessTreeControl(Tree inner) : base(inner)
        {
        }

        public IReadOnlyList<ITreeItemControl> Items =>
            Inner.Items.Select(item => (ITreeItemControl)new HeadlessTreeItemControl(item)).ToArray();

        public ITreeItemControl? SelectedTreeItem => Inner.SelectedTreeItem is null
            ? null
            : new HeadlessTreeItemControl(Inner.SelectedTreeItem);
    }

    private sealed class HeadlessTreeItemControl : HeadlessControlBase<TreeItem>, ITreeItemControl
    {
        public HeadlessTreeItemControl(TreeItem inner) : base(inner)
        {
        }

        public bool IsSelected
        {
            get => Inner.IsSelected;
            set => Inner.IsSelected = value;
        }

        public string Text => Inner.Text ?? Name;

        public IReadOnlyList<ITreeItemControl> Items =>
            Inner.Items.Select(item => (ITreeItemControl)new HeadlessTreeItemControl(item)).ToArray();

        public void Expand()
        {
            Inner.Expand();
        }

        public void SelectNode()
        {
            Inner.Select();
        }
    }

    private sealed class HeadlessGridControl : HeadlessControlBase<Grid>, IGridControl
    {
        public HeadlessGridControl(Grid inner) : base(inner)
        {
        }

        public IReadOnlyList<IGridRowControl> Rows =>
            Inner.Rows.Select(row => (IGridRowControl)new HeadlessGridRowControl(row)).ToArray();

        public IGridRowControl? GetRowByIndex(int index)
        {
            var row = Inner.GetRowByIndex(index);
            return row is null ? null : new HeadlessGridRowControl(row);
        }
    }

    private sealed class HeadlessGridRowControl : IGridRowControl
    {
        private readonly GridRow _inner;

        public HeadlessGridRowControl(GridRow inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<IGridCellControl> Cells =>
            _inner.Cells.Select(cell => (IGridCellControl)new HeadlessGridCellControl(cell)).ToArray();
    }

    private sealed class HeadlessGridCellControl : IGridCellControl
    {
        private readonly GridCell _inner;

        public HeadlessGridCellControl(GridCell inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Value => _inner.Value ?? string.Empty;
    }
}
