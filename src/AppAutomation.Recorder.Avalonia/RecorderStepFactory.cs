using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderStepFactory
{
    private readonly AppAutomationRecorderOptions _options;
    private readonly RecorderSelectorResolver _selectorResolver;

    public RecorderStepFactory(AppAutomationRecorderOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _selectorResolver = new RecorderSelectorResolver(options);
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
        if (!locatorResult.Success || locatorResult.Step is null)
        {
            return locatorResult;
        }

        var descriptor = locatorResult.Step.Control;

        return control switch
        {
            CheckBox checkBox => StepCreationResult.Created(
                new RecordedStep(RecordedActionKind.SetChecked, descriptor, BoolValue: checkBox.IsChecked == true, Warning: descriptor.Warning)),
            RadioButton radioButton => StepCreationResult.Created(
                new RecordedStep(RecordedActionKind.SetChecked, descriptor, BoolValue: radioButton.IsChecked == true, Warning: descriptor.Warning)),
            ToggleButton toggleButton and not CheckBox and not RadioButton => StepCreationResult.Created(
                new RecordedStep(RecordedActionKind.SetToggled, descriptor, BoolValue: toggleButton.IsChecked == true, Warning: descriptor.Warning)),
            _ => StepCreationResult.Created(new RecordedStep(RecordedActionKind.ClickButton, descriptor, Warning: descriptor.Warning))
        };
    }

    public StepCreationResult TryCreateTextEntryStep(TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        var locatorResult = _selectorResolver.Resolve(textBox, UiControlType.TextBox);
        if (!locatorResult.Success || locatorResult.Step is null)
        {
            return locatorResult;
        }

        var descriptor = locatorResult.Step.Control;
        var text = textBox.Text ?? string.Empty;
        if (TryResolveActionHint(textBox, descriptor.LocatorValue) == RecorderActionHint.SpinnerTextBox
            && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var numericValue))
        {
            return StepCreationResult.Created(
                new RecordedStep(RecordedActionKind.SetSpinnerValue, descriptor, DoubleValue: numericValue, Warning: descriptor.Warning));
        }

        return StepCreationResult.Created(
            new RecordedStep(RecordedActionKind.EnterText, descriptor, StringValue: text, Warning: descriptor.Warning));
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
        if (!locatorResult.Success || locatorResult.Step is null)
        {
            return locatorResult;
        }

        return StepCreationResult.Created(
            new RecordedStep(RecordedActionKind.SelectComboItem, locatorResult.Step.Control, StringValue: selectedText.Trim(), Warning: locatorResult.Step.Control.Warning));
    }

    public StepCreationResult TryCreateTabSelectionStep(TabControl tabControl)
    {
        ArgumentNullException.ThrowIfNull(tabControl);

        if (tabControl.SelectedItem is not TabItem selectedItem)
        {
            return StepCreationResult.Unsupported("TabControl does not expose a selected TabItem.");
        }

        var locatorResult = _selectorResolver.Resolve(selectedItem, UiControlType.TabItem);
        if (!locatorResult.Success || locatorResult.Step is null)
        {
            return locatorResult;
        }

        return StepCreationResult.Created(
            new RecordedStep(RecordedActionKind.SelectTabItem, locatorResult.Step.Control, Warning: locatorResult.Step.Control.Warning));
    }

    public StepCreationResult TryCreateTreeSelectionStep(TreeView treeView)
    {
        ArgumentNullException.ThrowIfNull(treeView);

        var locatorResult = _selectorResolver.Resolve(treeView, UiControlType.Tree);
        if (!locatorResult.Success || locatorResult.Step is null)
        {
            return locatorResult;
        }

        var selectedText = ExtractTreeSelectionText(treeView.SelectedItem);
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return StepCreationResult.Unsupported("TreeView selection does not expose a stable item text.");
        }

        return StepCreationResult.Created(
            new RecordedStep(RecordedActionKind.SelectTreeItem, locatorResult.Step.Control, StringValue: selectedText, Warning: locatorResult.Step.Control.Warning));
    }

    public StepCreationResult TryCreateSliderStep(Slider slider)
    {
        ArgumentNullException.ThrowIfNull(slider);

        var locatorResult = _selectorResolver.Resolve(slider, UiControlType.Slider);
        if (!locatorResult.Success || locatorResult.Step is null)
        {
            return locatorResult;
        }

        return StepCreationResult.Created(
            new RecordedStep(RecordedActionKind.SetSliderValue, locatorResult.Step.Control, DoubleValue: slider.Value, Warning: locatorResult.Step.Control.Warning));
    }

    public StepCreationResult TryCreateDatePickerStep(DatePicker datePicker)
    {
        ArgumentNullException.ThrowIfNull(datePicker);

        if (datePicker.SelectedDate is not { } selectedDate)
        {
            return StepCreationResult.Unsupported("DatePicker does not have a selected date.");
        }

        var locatorResult = _selectorResolver.Resolve(datePicker, UiControlType.DateTimePicker);
        if (!locatorResult.Success || locatorResult.Step is null)
        {
            return locatorResult;
        }

        return StepCreationResult.Created(
            new RecordedStep(RecordedActionKind.SetDate, locatorResult.Step.Control, DateValue: selectedDate.Date, Warning: locatorResult.Step.Control.Warning));
    }

    public StepCreationResult TryCreateCalendarStep(Calendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (calendar.SelectedDate is not { } selectedDate)
        {
            return StepCreationResult.Unsupported("Calendar does not have a selected date.");
        }

        var locatorResult = _selectorResolver.Resolve(calendar, UiControlType.Calendar);
        if (!locatorResult.Success || locatorResult.Step is null)
        {
            return locatorResult;
        }

        return StepCreationResult.Created(
            new RecordedStep(RecordedActionKind.SetDate, locatorResult.Step.Control, DateValue: selectedDate.Date, Warning: locatorResult.Step.Control.Warning));
    }

    public StepCreationResult TryCreateAssertionStep(Control? source, RecorderAssertionMode mode)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported("No control is available for assertion capture.");
        }

        if (mode is RecorderAssertionMode.Auto or RecorderAssertionMode.Text)
        {
            var textResult = TryCreateTextAssertion(source);
            if (textResult.Success || mode == RecorderAssertionMode.Text)
            {
                return textResult;
            }
        }

        if (mode is RecorderAssertionMode.Auto or RecorderAssertionMode.Checked)
        {
            var checkedResult = TryCreateCheckedAssertion(source);
            if (checkedResult.Success || mode == RecorderAssertionMode.Checked)
            {
                return checkedResult;
            }
        }

        if (mode is RecorderAssertionMode.Auto or RecorderAssertionMode.Enabled)
        {
            return TryCreateEnabledAssertion(source);
        }

        return StepCreationResult.Unsupported("Recorder could not derive a supported assertion for this control.");
    }

    private StepCreationResult TryCreateTextAssertion(Control source)
    {
        var controlType = ClassifyTextAssertionType(source);
        if (controlType is null)
        {
            return StepCreationResult.Unsupported("Control does not expose recordable text.");
        }

        var text = ExtractTextValue(source);
        if (string.IsNullOrWhiteSpace(text))
        {
            return StepCreationResult.Unsupported("Control text is empty, so a text assertion would not add value.");
        }

        var locatorResult = _selectorResolver.Resolve(source, controlType.Value);
        if (!locatorResult.Success || locatorResult.Step is null)
        {
            return locatorResult;
        }

        return StepCreationResult.Created(
            new RecordedStep(RecordedActionKind.WaitUntilTextEquals, locatorResult.Step.Control, StringValue: text.Trim(), Warning: locatorResult.Step.Control.Warning));
    }

    private StepCreationResult TryCreateCheckedAssertion(Control source)
    {
        switch (source)
        {
            case CheckBox checkBox:
            {
                var locatorResult = _selectorResolver.Resolve(checkBox, UiControlType.CheckBox);
                if (!locatorResult.Success || locatorResult.Step is null)
                {
                    return locatorResult;
                }

                return StepCreationResult.Created(
                    new RecordedStep(RecordedActionKind.WaitUntilIsChecked, locatorResult.Step.Control, BoolValue: checkBox.IsChecked == true, Warning: locatorResult.Step.Control.Warning));
            }
            case RadioButton radioButton:
            {
                var locatorResult = _selectorResolver.Resolve(radioButton, UiControlType.RadioButton);
                if (!locatorResult.Success || locatorResult.Step is null)
                {
                    return locatorResult;
                }

                return StepCreationResult.Created(
                    new RecordedStep(RecordedActionKind.WaitUntilIsSelected, locatorResult.Step.Control, BoolValue: radioButton.IsChecked == true, Warning: locatorResult.Step.Control.Warning));
            }
            case ToggleButton toggleButton:
            {
                var locatorResult = _selectorResolver.Resolve(toggleButton, UiControlType.ToggleButton);
                if (!locatorResult.Success || locatorResult.Step is null)
                {
                    return locatorResult;
                }

                return StepCreationResult.Created(
                    new RecordedStep(RecordedActionKind.WaitUntilIsToggled, locatorResult.Step.Control, BoolValue: toggleButton.IsChecked == true, Warning: locatorResult.Step.Control.Warning));
            }
            case TabItem tabItem:
            {
                var locatorResult = _selectorResolver.Resolve(tabItem, UiControlType.TabItem);
                if (!locatorResult.Success || locatorResult.Step is null)
                {
                    return locatorResult;
                }

                return StepCreationResult.Created(
                    new RecordedStep(RecordedActionKind.WaitUntilIsSelected, locatorResult.Step.Control, BoolValue: tabItem.IsSelected, Warning: locatorResult.Step.Control.Warning));
            }
            default:
                return StepCreationResult.Unsupported("Checked assertion is not supported for this control.");
        }
    }

    private StepCreationResult TryCreateEnabledAssertion(Control source)
    {
        var locatorResult = _selectorResolver.Resolve(source, ClassifyControlType(source));
        if (!locatorResult.Success || locatorResult.Step is null)
        {
            return locatorResult;
        }

        return StepCreationResult.Created(
            new RecordedStep(RecordedActionKind.WaitUntilIsEnabled, locatorResult.Step.Control, BoolValue: source.IsEnabled, Warning: locatorResult.Step.Control.Warning));
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
}
