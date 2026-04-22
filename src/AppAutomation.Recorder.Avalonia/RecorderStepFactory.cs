using System.Collections;
using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderStepFactory
{
    private readonly AppAutomationRecorderOptions _options;
    private readonly RecorderSelectorResolver _selectorResolver;
    private readonly RecorderStepValidator _stepValidator;
    private readonly IReadOnlyList<IRecorderAssertionExtractor> _assertionExtractors;

    public RecorderStepFactory(AppAutomationRecorderOptions options, Window? validationWindow = null)
        : this(
            options,
            validationWindow is null
                ? null
                : () => validationWindow.Content as Control)
    {
    }

    internal RecorderStepFactory(AppAutomationRecorderOptions options, Func<Control?>? validationRootProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _selectorResolver = new RecorderSelectorResolver(options, validationRootProvider);
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
        if (TryResolveActionHint(textBox, descriptor) == RecorderActionHint.SpinnerTextBox
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

    public StepCreationResult TryCreateSearchPickerStep(TextBox searchInput, ComboBox results)
    {
        ArgumentNullException.ThrowIfNull(searchInput);
        ArgumentNullException.ThrowIfNull(results);

        if (!TryResolveSearchPickerHint(searchInput, results, out var hint))
        {
            return StepCreationResult.Unsupported("Controls are not configured as a recorder search picker.");
        }

        var searchText = searchInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return StepCreationResult.Unsupported("Search picker search text is empty.");
        }

        var selectedText = results.SelectedItem?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return StepCreationResult.Unsupported("Search picker does not have a selected result to record.");
        }

        var warning = "Recorded composite search picker from configured parts.";
        var descriptor = new RecordedControlDescriptor(
            RecorderNaming.CreateControlPropertyName(hint.LocatorValue, UiControlType.SearchPicker),
            UiControlType.SearchPicker,
            hint.LocatorValue.Trim(),
            hint.LocatorKind,
            hint.FallbackToName,
            results.GetType().FullName ?? results.GetType().Name,
            warning);

        return CreateStep(
            results,
            new RecordedStep(
                RecordedActionKind.SearchAndSelect,
                descriptor,
                StringValue: searchText,
                Warning: warning,
                ItemValue: selectedText),
            warning);
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

        if (TryCreateGridAssertionStep(source, mode, out var gridResult))
        {
            return gridResult;
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

    private bool TryCreateGridAssertionStep(Control source, RecorderAssertionMode mode, out StepCreationResult result)
    {
        result = StepCreationResult.Unsupported("Recorder could not derive a supported grid assertion for this control.");
        if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Text))
        {
            return false;
        }

        if (!TryResolveGridHint(source, out var hint, out var gridSource))
        {
            return false;
        }

        var locatorResult = _selectorResolver.Resolve(gridSource, UiControlType.Grid);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            result = StepCreationResult.Unsupported(locatorResult.Message);
            return true;
        }

        if (!TryReadItemsSource(gridSource, out var items) || items.Count == 0)
        {
            result = StepCreationResult.Unsupported("Configured grid source does not expose a non-empty ItemsSource to record.");
            return true;
        }

        if (TryResolveGridCell(source, gridSource, hint, items, out var rowIndex, out var columnIndex, out var cellValue))
        {
            result = CreateStep(
                source,
                new RecordedStep(
                    RecordedActionKind.WaitUntilGridCellEquals,
                    locatorResult.Control,
                    StringValue: cellValue,
                    Warning: locatorResult.Control.Warning,
                    ValidationStatus: locatorResult.ValidationStatus,
                    ValidationMessage: locatorResult.ValidationMessage,
                    CanPersist: locatorResult.CanPersist,
                    RowIndex: rowIndex,
                    ColumnIndex: columnIndex),
                locatorResult.Message);
            return true;
        }

        result = CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.WaitUntilGridRowsAtLeast,
                locatorResult.Control,
                Warning: locatorResult.Control.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist,
                IntValue: items.Count),
            locatorResult.Message);
        return true;
    }

    private RecorderActionHint TryResolveActionHint(Control control, RecordedControlDescriptor descriptor)
    {
        if (TryResolveActionHint(descriptor.LocatorValue, descriptor.LocatorKind, out var actionHint))
        {
            return actionHint;
        }

        var automationId = AutomationProperties.GetAutomationId(control);
        if (!string.IsNullOrWhiteSpace(automationId)
            && TryResolveActionHint(automationId.Trim(), UiLocatorKind.AutomationId, out actionHint))
        {
            return actionHint;
        }

        if (TryGetLocator(control, UiLocatorKind.Name, out var nameLocator)
            && TryResolveActionHint(nameLocator, UiLocatorKind.Name, out actionHint))
        {
            return actionHint;
        }

        return !string.IsNullOrWhiteSpace(automationId)
               && automationId.Contains("Spinner", StringComparison.OrdinalIgnoreCase)
            ? RecorderActionHint.SpinnerTextBox
            : RecorderActionHint.None;
    }

    private bool TryResolveActionHint(
        string locatorValue,
        UiLocatorKind locatorKind,
        out RecorderActionHint actionHint)
    {
        var explicitHint = _options.ControlHints.FirstOrDefault(candidate =>
            candidate.LocatorKind == locatorKind
            && string.Equals(candidate.LocatorValue.Trim(), locatorValue, StringComparison.Ordinal));
        if (explicitHint is not null)
        {
            actionHint = explicitHint.ActionHint;
            return true;
        }

        actionHint = RecorderActionHint.None;
        return false;
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

    private bool TryResolveGridHint(Control source, out RecorderGridHint hint, out Control gridSource)
    {
        for (Control? current = source; current is not null; current = current.GetVisualParent() as Control)
        {
            if (current is Window)
            {
                break;
            }

            foreach (var candidate in _options.GridHints)
            {
                if (TryGetLocator(current, candidate.SourceLocatorKind, out var locatorValue)
                    && string.Equals(candidate.SourceLocatorValue.Trim(), locatorValue, StringComparison.Ordinal))
                {
                    hint = candidate;
                    gridSource = current;
                    return true;
                }
            }
        }

        hint = null!;
        gridSource = null!;
        return false;
    }

    private bool TryResolveSearchPickerHint(
        TextBox searchInput,
        ComboBox results,
        out RecorderSearchPickerHint hint)
    {
        foreach (var candidate in _options.SearchPickerHints)
        {
            var parts = candidate.Parts;
            if (TryGetLocator(searchInput, parts.LocatorKind, out var searchInputLocator)
                && TryGetLocator(results, parts.LocatorKind, out var resultsLocator)
                && string.Equals(parts.SearchInputLocator.Trim(), searchInputLocator, StringComparison.Ordinal)
                && string.Equals(parts.ResultsLocator.Trim(), resultsLocator, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(candidate.LocatorValue))
            {
                hint = candidate;
                return true;
            }
        }

        hint = null!;
        return false;
    }

    private static bool TryResolveGridCell(
        Control source,
        Control gridSource,
        RecorderGridHint hint,
        IReadOnlyList<object?> items,
        out int rowIndex,
        out int columnIndex,
        out string cellValue)
    {
        rowIndex = -1;
        columnIndex = -1;
        cellValue = string.Empty;

        if (hint.ColumnPropertyNames.Count == 0)
        {
            return false;
        }

        var observedText = ExtractTextValue(source)?.Trim();
        if (string.IsNullOrWhiteSpace(observedText))
        {
            return false;
        }

        var hasSourceColumnIndex = TryResolveGridColumnIndex(
            source,
            gridSource,
            hint.ColumnPropertyNames.Count,
            out var sourceColumnIndex);

        for (Control? current = source; current is not null && !ReferenceEquals(current, gridSource); current = current.GetVisualParent() as Control)
        {
            var dataContext = current.DataContext;
            if (dataContext is null || !TryFindItemIndex(items, dataContext, out rowIndex, out var item))
            {
                continue;
            }

            if (hasSourceColumnIndex)
            {
                if (!TryReadPropertyValue(item, hint.ColumnPropertyNames[sourceColumnIndex], out var sourceColumnValue)
                    || !string.Equals(sourceColumnValue, observedText, StringComparison.Ordinal))
                {
                    return false;
                }

                columnIndex = sourceColumnIndex;
                cellValue = sourceColumnValue;
                return true;
            }

            var matchedColumnIndex = -1;
            var matchedValue = string.Empty;
            var matchedColumnCount = 0;
            for (var candidateColumnIndex = 0; candidateColumnIndex < hint.ColumnPropertyNames.Count; candidateColumnIndex++)
            {
                if (!TryReadPropertyValue(item, hint.ColumnPropertyNames[candidateColumnIndex], out var candidateValue)
                    || !string.Equals(candidateValue, observedText, StringComparison.Ordinal))
                {
                    continue;
                }

                matchedColumnIndex = candidateColumnIndex;
                matchedValue = candidateValue;
                matchedColumnCount++;
            }

            if (matchedColumnCount == 1)
            {
                columnIndex = matchedColumnIndex;
                cellValue = matchedValue;
                return true;
            }

            if (matchedColumnCount > 1)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryResolveGridColumnIndex(
        Control source,
        Control gridSource,
        int columnCount,
        out int columnIndex)
    {
        for (Control? current = source; current is not null && !ReferenceEquals(current, gridSource); current = current.GetVisualParent() as Control)
        {
            if (TryParseVisualGridIndex(AutomationProperties.GetAutomationId(current), "_Cell", out var candidate)
                && candidate >= 0
                && candidate < columnCount)
            {
                columnIndex = candidate;
                return true;
            }
        }

        columnIndex = -1;
        return false;
    }

    private static bool TryParseVisualGridIndex(string? automationId, string marker, out int index)
    {
        index = -1;
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

        return digitEnd > digitStart
            && int.TryParse(
                automationId[digitStart..digitEnd],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out index);
    }

    private static bool TryReadItemsSource(Control control, out IReadOnlyList<object?> items)
    {
        var itemsSourceProperty = control.GetType().GetProperty("ItemsSource");
        var itemsValue = itemsSourceProperty?.GetValue(control);
        if (itemsValue is null)
        {
            var itemsProperty = control.GetType().GetProperty("Items");
            itemsValue = itemsProperty?.GetValue(control);
        }

        if (itemsValue is IEnumerable enumerable and not string)
        {
            items = enumerable.Cast<object?>().ToArray();
            return true;
        }

        items = Array.Empty<object?>();
        return false;
    }

    private static bool TryFindItemIndex(
        IReadOnlyList<object?> items,
        object dataContext,
        out int rowIndex,
        out object item)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var candidate = items[i];
            if (candidate is null)
            {
                continue;
            }

            if (ReferenceEquals(candidate, dataContext) || candidate.Equals(dataContext))
            {
                rowIndex = i;
                item = candidate;
                return true;
            }
        }

        rowIndex = -1;
        item = null!;
        return false;
    }

    private static bool TryReadPropertyValue(object item, string propertyName, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var property = item.GetType().GetProperty(propertyName.Trim());
        if (property is null)
        {
            return false;
        }

        value = property.GetValue(item)?.ToString() ?? string.Empty;
        return true;
    }

    private static bool TryGetLocator(Control control, UiLocatorKind locatorKind, out string locator)
    {
        locator = locatorKind switch
        {
            UiLocatorKind.AutomationId => AutomationProperties.GetAutomationId(control) ?? string.Empty,
            UiLocatorKind.Name => AutomationProperties.GetName(control) ?? control.Name ?? string.Empty,
            _ => string.Empty
        };

        locator = locator.Trim();
        return !string.IsNullOrWhiteSpace(locator);
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
        var validated = _stepValidator.Validate(step, source) with
        {
            StepId = step.StepId == Guid.Empty ? Guid.NewGuid() : step.StepId,
            LastValidationAt = DateTimeOffset.UtcNow
        };
        validated = validated with
        {
            ReviewState = ResolveReviewState(validated),
            FailureCode = ResolveFailureCode(validated),
            LastValidationAt = DateTimeOffset.UtcNow
        };
        return StepCreationResult.Created(validated, message);
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

    private static RecorderStepReviewState ResolveReviewState(RecordedStep step)
    {
        if (step.IsIgnored)
        {
            return RecorderStepReviewState.Ignored;
        }

        return step.ValidationStatus == RecorderValidationStatus.Valid && step.CanPersist
            ? RecorderStepReviewState.Active
            : RecorderStepReviewState.NeedsReview;
    }

    private static string? ResolveFailureCode(RecordedStep step)
    {
        return step.ValidationStatus switch
        {
            RecorderValidationStatus.Invalid when !step.CanPersist => "validation-invalid",
            RecorderValidationStatus.Warning => "validation-warning",
            _ => null
        };
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
