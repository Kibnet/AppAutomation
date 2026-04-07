using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderStepFactory
{
    private readonly AppAutomationRecorderOptions _options;
    private readonly RecorderSelectorResolver _selectorResolver;
    private readonly RecorderStepValidator _stepValidator;
    private readonly IReadOnlyList<IRecorderAssertionExtractor> _assertionExtractors;

    public RecorderStepFactory(AppAutomationRecorderOptions options, Window? validationWindow = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _selectorResolver = new RecorderSelectorResolver(options, validationWindow);
        _stepValidator = new RecorderStepValidator();
        _assertionExtractors = CreateAssertionExtractors(options);
    }

    public StepCreationResult TryCreateButtonStep(Control? source)
    {
        var control = source switch
        {
            CheckBox checkBox => checkBox,
            RadioButton radioButton => radioButton,
            ToggleButton toggleButton => toggleButton,
            Button button => button,
            _ => null
        };

        if (control is null)
        {
            return StepCreationResult.Unsupported("Recorder does not support this click target.");
        }

        var locatorResult = _selectorResolver.Resolve(control, ClassifyControlType(control));
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        var descriptor = locatorResult.Control;
        var step = control switch
        {
            CheckBox checkBox => new RecordedStep(
                RecordedActionKind.SetChecked,
                descriptor,
                BoolValue: checkBox.IsChecked == true,
                Warning: descriptor.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            RadioButton radioButton => new RecordedStep(
                RecordedActionKind.SetChecked,
                descriptor,
                BoolValue: radioButton.IsChecked == true,
                Warning: descriptor.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            ToggleButton toggleButton when control is not CheckBox && control is not RadioButton => new RecordedStep(
                RecordedActionKind.SetToggled,
                descriptor,
                BoolValue: toggleButton.IsChecked == true,
                Warning: descriptor.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            _ => new RecordedStep(
                RecordedActionKind.ClickButton,
                descriptor,
                Warning: descriptor.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist)
        };

        return CreateStep(control, step, locatorResult.Message);
    }

    public StepCreationResult TryCreateTextEntryStep(TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        var locatorResult = _selectorResolver.Resolve(textBox, UiControlType.TextBox);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        var descriptor = locatorResult.Control;
        var text = textBox.Text ?? string.Empty;
        if (TryResolveActionHint(textBox, descriptor.LocatorValue) == RecorderActionHint.SpinnerTextBox
            && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var numericValue))
        {
            return CreateStep(
                textBox,
                new RecordedStep(
                    RecordedActionKind.SetSpinnerValue,
                    descriptor,
                    DoubleValue: numericValue,
                    Warning: descriptor.Warning,
                    ValidationStatus: locatorResult.ValidationStatus,
                    ValidationMessage: locatorResult.ValidationMessage,
                    CanPersist: locatorResult.CanPersist),
                locatorResult.Message);
        }

        return CreateStep(
            textBox,
            new RecordedStep(
                RecordedActionKind.EnterText,
                descriptor,
                StringValue: text,
                Warning: descriptor.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            locatorResult.Message);
    }

    public StepCreationResult TryCreateComboBoxStep(ComboBox comboBox)
    {
        ArgumentNullException.ThrowIfNull(comboBox);

        var selectedText = comboBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return StepCreationResult.Unsupported("ComboBox does not have a selected item to record.");
        }

        var locatorResult = _selectorResolver.Resolve(comboBox, UiControlType.ComboBox);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        return CreateStep(
            comboBox,
            new RecordedStep(
                RecordedActionKind.SelectComboItem,
                locatorResult.Control,
                StringValue: selectedText.Trim(),
                Warning: locatorResult.Control.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            locatorResult.Message);
    }

    public StepCreationResult TryCreateListBoxStep(ListBox listBox)
    {
        ArgumentNullException.ThrowIfNull(listBox);

        var selectedText = listBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return StepCreationResult.Unsupported("ListBox does not have a selected item to record.");
        }

        var locatorResult = _selectorResolver.Resolve(listBox, UiControlType.ListBox);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        return CreateStep(
            listBox,
            new RecordedStep(
                RecordedActionKind.SelectListBoxItem,
                locatorResult.Control,
                StringValue: selectedText.Trim(),
                Warning: locatorResult.Control.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            locatorResult.Message);
    }

    public StepCreationResult TryCreateTabSelectionStep(TabControl tabControl)
    {
        ArgumentNullException.ThrowIfNull(tabControl);

        if (tabControl.SelectedItem is not TabItem selectedItem)
        {
            return StepCreationResult.Unsupported("TabControl does not expose a selected TabItem.");
        }

        var locatorResult = _selectorResolver.Resolve(selectedItem, UiControlType.TabItem);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        return CreateStep(
            selectedItem,
            new RecordedStep(
                RecordedActionKind.SelectTabItem,
                locatorResult.Control,
                Warning: locatorResult.Control.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            locatorResult.Message);
    }

    public StepCreationResult TryCreateTreeSelectionStep(TreeView treeView)
    {
        ArgumentNullException.ThrowIfNull(treeView);

        var locatorResult = _selectorResolver.Resolve(treeView, UiControlType.Tree);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        var selectedText = ExtractTreeSelectionText(treeView.SelectedItem);
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return StepCreationResult.Unsupported("TreeView selection does not expose a stable item text.");
        }

        return CreateStep(
            treeView,
            new RecordedStep(
                RecordedActionKind.SelectTreeItem,
                locatorResult.Control,
                StringValue: selectedText,
                Warning: locatorResult.Control.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            locatorResult.Message);
    }

    public StepCreationResult TryCreateSliderStep(Slider slider)
    {
        ArgumentNullException.ThrowIfNull(slider);

        var locatorResult = _selectorResolver.Resolve(slider, UiControlType.Slider);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        return CreateStep(
            slider,
            new RecordedStep(
                RecordedActionKind.SetSliderValue,
                locatorResult.Control,
                DoubleValue: slider.Value,
                Warning: locatorResult.Control.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            locatorResult.Message);
    }

    public StepCreationResult TryCreateDatePickerStep(DatePicker datePicker)
    {
        ArgumentNullException.ThrowIfNull(datePicker);

        if (datePicker.SelectedDate is not { } selectedDate)
        {
            return StepCreationResult.Unsupported("DatePicker does not have a selected date.");
        }

        var locatorResult = _selectorResolver.Resolve(datePicker, UiControlType.DateTimePicker);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        return CreateStep(
            datePicker,
            new RecordedStep(
                RecordedActionKind.SetDate,
                locatorResult.Control,
                DateValue: selectedDate.Date,
                Warning: locatorResult.Control.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            locatorResult.Message);
    }

    public StepCreationResult TryCreateCalendarStep(Calendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (calendar.SelectedDate is not { } selectedDate)
        {
            return StepCreationResult.Unsupported("Calendar does not have a selected date.");
        }

        var locatorResult = _selectorResolver.Resolve(calendar, UiControlType.Calendar);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        return CreateStep(
            calendar,
            new RecordedStep(
                RecordedActionKind.SetDate,
                locatorResult.Control,
                DateValue: selectedDate.Date,
                Warning: locatorResult.Control.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            locatorResult.Message);
    }

    public StepCreationResult TryCreateAssertionStep(Control? source, RecorderAssertionMode mode)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported("No control is available for assertion capture.");
        }

        foreach (var extractor in _assertionExtractors)
        {
            if (!extractor.TryCreate(source, mode, out var candidate) || candidate is null)
            {
                continue;
            }

            var locatorResult = _selectorResolver.Resolve(source, candidate.ControlType);
            if (!locatorResult.Success || locatorResult.Control is null)
            {
                return StepCreationResult.Unsupported(locatorResult.Message);
            }

            return CreateStep(
                source,
                new RecordedStep(
                    candidate.ActionKind,
                    locatorResult.Control,
                    StringValue: candidate.StringValue,
                    BoolValue: candidate.BoolValue,
                    DoubleValue: candidate.DoubleValue,
                    DateValue: candidate.DateValue,
                    Warning: CombineMessage(locatorResult.Control.Warning, candidate.Warning),
                    ValidationStatus: locatorResult.ValidationStatus,
                    ValidationMessage: locatorResult.ValidationMessage,
                    CanPersist: locatorResult.CanPersist),
                locatorResult.Message);
        }

        return StepCreationResult.Unsupported("Recorder could not derive a supported assertion for this control.");
    }

    private RecorderActionHint TryResolveActionHint(Control control, string locatorValue)
    {
        var explicitHint = _options.ControlHints
            .FirstOrDefault(candidate => string.Equals(candidate.LocatorValue, locatorValue, StringComparison.Ordinal));
        if (explicitHint is not null)
        {
            return explicitHint.ActionHint;
        }

        var automationId = AutomationProperties.GetAutomationId(control);
        return !string.IsNullOrWhiteSpace(automationId)
               && automationId.Contains("Spinner", StringComparison.OrdinalIgnoreCase)
            ? RecorderActionHint.SpinnerTextBox
            : RecorderActionHint.None;
    }

    private static UiControlType? ClassifyTextAssertionType(Control control)
    {
        return control switch
        {
            TextBox => UiControlType.TextBox,
            TextBlock or Label or Button => UiControlType.Label,
            _ => null
        };
    }

    private static UiControlType ClassifyControlType(Control control)
    {
        return control switch
        {
            CheckBox => UiControlType.CheckBox,
            RadioButton => UiControlType.RadioButton,
            ToggleButton => UiControlType.ToggleButton,
            Button => UiControlType.Button,
            TextBox => UiControlType.TextBox,
            ComboBox => UiControlType.ComboBox,
            ListBox => UiControlType.ListBox,
            Slider => UiControlType.Slider,
            DatePicker => UiControlType.DateTimePicker,
            Calendar => UiControlType.Calendar,
            TabItem => UiControlType.TabItem,
            TreeView => UiControlType.Tree,
            TreeViewItem => UiControlType.TreeItem,
            TextBlock or Label => UiControlType.Label,
            _ => UiControlType.AutomationElement
        };
    }

    private static string? ExtractTextValue(Control control)
    {
        return control switch
        {
            TextBox textBox => textBox.Text,
            TextBlock textBlock => textBlock.Text,
            Label label => label.Content?.ToString(),
            Button button => button.Content?.ToString(),
            _ => AutomationProperties.GetName(control)
        };
    }

    private static string? ExtractTreeSelectionText(object? selectedItem)
    {
        return selectedItem switch
        {
            TreeViewItem treeViewItem when !string.IsNullOrWhiteSpace(treeViewItem.Header?.ToString()) => treeViewItem.Header?.ToString(),
            TreeViewItem treeViewItem when !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(treeViewItem)) => AutomationProperties.GetAutomationId(treeViewItem),
            Control control when !string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)) => AutomationProperties.GetName(control),
            _ => selectedItem?.ToString()
        };
    }

    private StepCreationResult CreateStep(Control source, RecordedStep step, string? message = null)
    {
        return StepCreationResult.Created(_stepValidator.Validate(step, source), message);
    }

    private static string? CombineMessage(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return string.IsNullOrWhiteSpace(right) ? null : right;
        }

        if (string.IsNullOrWhiteSpace(right) || string.Equals(left, right, StringComparison.Ordinal))
        {
            return left;
        }

        return $"{left} {right}";
    }

    private static IReadOnlyList<IRecorderAssertionExtractor> CreateAssertionExtractors(AppAutomationRecorderOptions options)
    {
        return
        [
            new TextAssertionExtractor(),
            new CheckedAssertionExtractor(),
            new EnabledAssertionExtractor(),
            .. options.AssertionExtractors
        ];
    }

    private sealed class TextAssertionExtractor : IRecorderAssertionExtractor
    {
        public bool TryCreate(Control control, RecorderAssertionMode mode, out RecorderAssertionCandidate? candidate)
        {
            candidate = null;
            if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Text))
            {
                return false;
            }

            var controlType = ClassifyTextAssertionType(control);
            if (controlType is null)
            {
                return false;
            }

            var text = ExtractTextValue(control);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            candidate = new RecorderAssertionCandidate(
                controlType.Value,
                RecordedActionKind.WaitUntilTextEquals,
                StringValue: text.Trim());
            return true;
        }
    }

    private sealed class CheckedAssertionExtractor : IRecorderAssertionExtractor
    {
        public bool TryCreate(Control control, RecorderAssertionMode mode, out RecorderAssertionCandidate? candidate)
        {
            candidate = control switch
            {
                _ when mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Checked) => null,
                CheckBox checkBox => new RecorderAssertionCandidate(
                    UiControlType.CheckBox,
                    RecordedActionKind.WaitUntilIsChecked,
                    BoolValue: checkBox.IsChecked == true),
                RadioButton radioButton => new RecorderAssertionCandidate(
                    UiControlType.RadioButton,
                    RecordedActionKind.WaitUntilIsSelected,
                    BoolValue: radioButton.IsChecked == true),
                ToggleButton toggleButton when control is not CheckBox && control is not RadioButton => new RecorderAssertionCandidate(
                    UiControlType.ToggleButton,
                    RecordedActionKind.WaitUntilIsToggled,
                    BoolValue: toggleButton.IsChecked == true),
                TabItem tabItem => new RecorderAssertionCandidate(
                    UiControlType.TabItem,
                    RecordedActionKind.WaitUntilIsSelected,
                    BoolValue: tabItem.IsSelected),
                _ => null
            };

            return candidate is not null;
        }
    }

    private sealed class EnabledAssertionExtractor : IRecorderAssertionExtractor
    {
        public bool TryCreate(Control control, RecorderAssertionMode mode, out RecorderAssertionCandidate? candidate)
        {
            candidate = null;
            if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Enabled))
            {
                return false;
            }

            candidate = new RecorderAssertionCandidate(
                ClassifyControlType(control),
                RecordedActionKind.WaitUntilIsEnabled,
                BoolValue: control.IsEnabled);
            return true;
        }
    }
}
