using System.Collections;
using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderStepFactory
{
    internal const string NoGridActionHintMessage = "Recorder does not have a grid action hint for this source.";
    internal const string NoGridSearchPickerHintMessage = "Recorder does not have a grid search picker hint for this editor.";

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

        return TryCreateSearchPickerStepCore(
            searchInput,
            results,
            SearchPickerResultsKind.ComboBox,
            ExtractSelectionText(results.SelectedItem));
    }

    public StepCreationResult TryCreateSearchPickerStep(TextBox searchInput, ListBox results)
    {
        ArgumentNullException.ThrowIfNull(searchInput);
        ArgumentNullException.ThrowIfNull(results);

        return TryCreateSearchPickerStepCore(
            searchInput,
            results,
            SearchPickerResultsKind.ListBox,
            ExtractSelectionText(results.SelectedItem));
    }

    public bool ShouldSuppressSearchPickerButton(Control? source)
    {
        return source is not null
            && (TryResolveSearchPickerButton(source, out _)
                || TryResolveGridSearchPickerButton(source, out _));
    }

    public bool ShouldSuppressCompositeWorkflowButton(Control? source)
    {
        if (source is null)
        {
            return false;
        }

        return _options.DateRangeFilterHints.Any(hint =>
                !string.IsNullOrWhiteSpace(hint.Parts.OpenButtonLocator)
                && MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.OpenButtonLocator))
            || _options.NumericRangeFilterHints.Any(hint =>
                !string.IsNullOrWhiteSpace(hint.Parts.OpenButtonLocator)
                && MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.OpenButtonLocator))
            || _options.FolderExportHints.Any(hint =>
                MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.OpenButtonLocator));
    }

    public StepCreationResult TryCreateDialogActionStep(Control? source)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported("Recorder does not have a dialog hint for this button.");
        }

        if (!TryResolveDialogHint(source, out var hint, out var actionKind))
        {
            return StepCreationResult.Unsupported("Recorder does not have a dialog hint for this button.");
        }

        var warning = $"Recorded dialog action '{actionKind}' from configured parts.";
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.Dialog,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning);

        return CreateStep(
            source,
            new RecordedStep(actionKind, descriptor, Warning: warning),
            warning);
    }

    public StepCreationResult TryCreateNotificationActionStep(Control? source)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported("Recorder does not have a notification hint for this button.");
        }

        if (!TryResolveNotificationHint(source, out var hint))
        {
            return StepCreationResult.Unsupported("Recorder does not have a notification hint for this button.");
        }

        var warning = "Recorded notification dismiss action from configured parts.";
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.Notification,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning);

        return CreateStep(
            source,
            new RecordedStep(RecordedActionKind.DismissNotification, descriptor, Warning: warning),
            warning);
    }

    public StepCreationResult TryCreateDateRangeFilterStep(Control? source)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported("Recorder does not have a date range filter hint for this button.");
        }

        if (!TryResolveDateRangeFilterHint(source, out var hint, out var commitMode))
        {
            return StepCreationResult.Unsupported("Recorder does not have a date range filter hint for this button.");
        }

        if (!TryReadDateRangeValues(hint.Parts, out var from, out var to, out var message))
        {
            return StepCreationResult.Unsupported(message);
        }

        var warning = "Recorded date range filter action from configured parts.";
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.DateRangeFilter,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning);

        return CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.SetDateRangeFilter,
                descriptor,
                DateValue: from,
                Warning: warning,
                SecondDateValue: to,
                FilterCommitMode: commitMode),
            warning);
    }

    public StepCreationResult TryCreateNumericRangeFilterStep(Control? source)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported("Recorder does not have a numeric range filter hint for this button.");
        }

        if (!TryResolveNumericRangeFilterHint(source, out var hint, out var commitMode))
        {
            return StepCreationResult.Unsupported("Recorder does not have a numeric range filter hint for this button.");
        }

        if (!TryReadNumericRangeValues(hint.Parts, out var from, out var to, out var message))
        {
            return StepCreationResult.Unsupported(message);
        }

        var warning = "Recorded numeric range filter action from configured parts.";
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.NumericRangeFilter,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning);

        return CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.SetNumericRangeFilter,
                descriptor,
                DoubleValue: from,
                Warning: warning,
                SecondDoubleValue: to,
                FilterCommitMode: commitMode),
            warning);
    }

    public StepCreationResult TryCreateFolderExportStep(Control? source)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported("Recorder does not have a folder export hint for this button.");
        }

        if (!TryResolveFolderExportHint(source, out var hint, out var commitMode))
        {
            return StepCreationResult.Unsupported("Recorder does not have a folder export hint for this button.");
        }

        if (!TryFindControl(hint.Parts.FolderPathInputLocator, hint.Parts.LocatorKind, out var folderInput)
            || folderInput is not TextBox textBox)
        {
            return StepCreationResult.Unsupported("Folder export path input was not found or is not a TextBox.");
        }

        var folderPath = textBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return StepCreationResult.Unsupported("Folder export path is empty.");
        }

        var warning = "Recorded folder export action from configured parts.";
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.FolderExport,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning);

        return CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.SelectExportFolder,
                descriptor,
                StringValue: folderPath,
                Warning: warning,
                FolderExportCommitMode: commitMode),
            warning);
    }

    public StepCreationResult TryCreateGridEditStep(Control? source)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported("Recorder does not have a grid edit hint for this source.");
        }

        if (!TryResolveGridEditHint(source, out var hint))
        {
            return StepCreationResult.Unsupported("Recorder does not have a grid edit hint for this source.");
        }

        if (hint.RowIndex < 0 || hint.ColumnIndex < 0)
        {
            return StepCreationResult.Unsupported("Grid edit hint requires non-negative row and column indexes.");
        }

        var warning = $"Recorded grid cell edit action '{hint.EditorKind}' from configured hint.";
        var descriptor = new RecordedControlDescriptor(
            RecorderNaming.CreateControlPropertyName(hint.TargetGridLocatorValue, UiControlType.Grid),
            UiControlType.Grid,
            hint.TargetGridLocatorValue.Trim(),
            hint.TargetGridLocatorKind,
            hint.TargetFallbackToName,
            source.GetType().FullName ?? source.GetType().Name,
            warning);

        return hint.EditorKind switch
        {
            GridCellEditorKind.Text => TryCreateGridEditTextStep(source, descriptor, warning, hint),
            GridCellEditorKind.Number => TryCreateGridEditNumberStep(source, descriptor, warning, hint),
            GridCellEditorKind.Date => TryCreateGridEditDateStep(source, descriptor, warning, hint),
            GridCellEditorKind.ComboBox => TryCreateGridEditComboStep(source, descriptor, warning, hint),
            GridCellEditorKind.SearchPicker => StepCreationResult.Unsupported(
                "Grid search picker edit is recorded through RecorderGridSearchPickerHint."),
            _ => StepCreationResult.Unsupported($"Unsupported grid edit hint '{hint.EditorKind}'.")
        };
    }

    public bool ShouldSuppressCompositeTextEntry(TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        return MatchesDateRangeTextPart(textBox)
            || MatchesNumericRangeTextPart(textBox)
            || MatchesFolderExportPathPart(textBox)
            || MatchesGridEditValuePart(textBox);
    }

    public bool ShouldRetainPendingTextForCompositeSelection(TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        return MatchesSearchPickerTextPart(textBox)
            || MatchesGridSearchPickerTextPart(textBox);
    }

    public bool IsCompositeSelectionPair(TextBox searchInput, Control results)
    {
        ArgumentNullException.ThrowIfNull(searchInput);
        ArgumentNullException.ThrowIfNull(results);

        return results switch
        {
            ComboBox => TryResolveSearchPickerHint(searchInput, results, SearchPickerResultsKind.ComboBox, out _)
                || TryResolveGridSearchPickerHint(searchInput, results, SearchPickerResultsKind.ComboBox, out _),
            ListBox => TryResolveSearchPickerHint(searchInput, results, SearchPickerResultsKind.ListBox, out _)
                || TryResolveGridSearchPickerHint(searchInput, results, SearchPickerResultsKind.ListBox, out _),
            _ => false
        };
    }

    public bool ShouldSuppressCompositeDateSelection(DatePicker datePicker)
    {
        ArgumentNullException.ThrowIfNull(datePicker);

        return MatchesDateRangeDatePart(datePicker)
            || MatchesGridEditValuePart(datePicker);
    }

    public bool ShouldSuppressCompositeSelection(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return MatchesGridEditValuePart(control);
    }

    public StepCreationResult TryCreateShellNavigationStep(Control source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!TryResolveShellNavigationHint(source, out var hint, out var actionKind))
        {
            return StepCreationResult.Unsupported("Recorder does not have a shell navigation hint for this selection.");
        }

        var paneName = TryReadShellPaneName(source, hint, actionKind);
        if (string.IsNullOrWhiteSpace(paneName))
        {
            return StepCreationResult.Unsupported(
                actionKind == RecordedActionKind.ActivateShellPane
                && !CanReadShellPaneNameFromSource(source)
                && string.IsNullOrWhiteSpace(hint.Parts.ActivePaneLabelLocator)
                    ? "Shell navigation activation capture requires ActivePaneLabelLocator when pane tabs are recorded from a non-tab capture surface."
                    : "Shell navigation selection does not expose a stable pane name.");
        }

        var warning = $"Recorded shell navigation action '{actionKind}' from configured parts.";
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.ShellNavigation,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning);

        return CreateStep(
            source,
            new RecordedStep(actionKind, descriptor, StringValue: paneName.Trim(), Warning: warning),
            warning);
    }

    public StepCreationResult TryCreateGridActionStep(Control? source)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported(NoGridActionHintMessage);
        }

        if (!TryResolveGridActionHint(source, out var hint, out var matchedSource))
        {
            return StepCreationResult.Unsupported(NoGridActionHintMessage);
        }

        if (string.IsNullOrWhiteSpace(hint.TargetGridLocatorValue))
        {
            return StepCreationResult.Unsupported("Grid action hint target grid locator is empty.");
        }

        var warning = $"Recorded grid user action '{hint.ActionKind}' from configured hint.";
        var descriptor = new RecordedControlDescriptor(
            RecorderNaming.CreateControlPropertyName(hint.TargetGridLocatorValue, UiControlType.Grid),
            UiControlType.Grid,
            hint.TargetGridLocatorValue.Trim(),
            hint.TargetGridLocatorKind,
            hint.TargetFallbackToName,
            source.GetType().FullName ?? source.GetType().Name,
            warning);

        return hint.ActionKind switch
        {
            RecorderGridUserActionKind.OpenRow =>
                TryCreateOpenGridRowStep(source, descriptor, warning, hint),
            RecorderGridUserActionKind.SortByColumn =>
                TryCreateSortGridByColumnStep(source, matchedSource, descriptor, warning, hint),
            RecorderGridUserActionKind.ScrollToEnd =>
                CreateStep(
                    source,
                    new RecordedStep(RecordedActionKind.ScrollGridToEnd, descriptor, Warning: warning),
                    warning),
            RecorderGridUserActionKind.CopyCell =>
                TryCreateCopyGridCellStep(source, descriptor, warning, hint),
            RecorderGridUserActionKind.Export =>
                CreateStep(
                    source,
                    new RecordedStep(RecordedActionKind.ExportGrid, descriptor, Warning: warning),
                    warning),
            _ => StepCreationResult.Unsupported($"Unsupported grid action hint '{hint.ActionKind}'.")
        };
    }

    public StepCreationResult TryCreateListBoxStep(ListBox listBox)
    {
        ArgumentNullException.ThrowIfNull(listBox);

        var selectedText = ExtractSelectionText(listBox.SelectedItem);
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

        if (TryCreateNotificationAssertionStep(source, mode, out var notificationResult))
        {
            return notificationResult;
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
                    CanPersist: locatorResult.CanPersist,
                    IntValue: candidate.IntValue),
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

    private bool TryCreateNotificationAssertionStep(Control source, RecorderAssertionMode mode, out StepCreationResult result)
    {
        result = StepCreationResult.Unsupported("Recorder could not derive a supported notification assertion for this control.");
        if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Text))
        {
            return false;
        }

        if (!TryResolveNotificationTextHint(source, out var hint))
        {
            return false;
        }

        if (!TryFindControl(hint.Parts.TextLocator, hint.Parts.LocatorKind, out var textControl))
        {
            result = StepCreationResult.Unsupported("Notification text part was not found.");
            return true;
        }

        var text = ExtractTextValue(textControl);
        if (string.IsNullOrWhiteSpace(text))
        {
            result = StepCreationResult.Unsupported("Notification text part does not expose text.");
            return true;
        }

        var warning = "Recorded notification text assertion from configured parts.";
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.Notification,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning);
        result = CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.WaitUntilNotificationContains,
                descriptor,
                StringValue: text.Trim(),
                Warning: warning),
            warning);
        return true;
    }

    private StepCreationResult TryCreateOpenGridRowStep(
        Control source,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridActionHint hint)
    {
        if (!TryResolveGridRowIndex(source, hint, out var rowIndex))
        {
            return StepCreationResult.Unsupported("Grid open-row action requires a row index from the hint or grid row/cell context.");
        }

        return CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.OpenGridRow,
                descriptor,
                Warning: warning,
                RowIndex: rowIndex),
            warning);
    }

    private StepCreationResult TryCreateSortGridByColumnStep(
        Control source,
        Control matchedSource,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridActionHint hint)
    {
        var columnName = FirstNonWhiteSpace(
            hint.ColumnName,
            ExtractTextValue(source),
            ExtractTextValue(matchedSource));
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return StepCreationResult.Unsupported("Grid sort action requires a column name from the hint or source text.");
        }

        return CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.SortGridByColumn,
                descriptor,
                StringValue: columnName.Trim(),
                Warning: warning),
            warning);
    }

    private StepCreationResult TryCreateCopyGridCellStep(
        Control source,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridActionHint hint)
    {
        if (!TryResolveGridCellIndexes(source, hint, out var rowIndex, out var columnIndex))
        {
            return StepCreationResult.Unsupported("Grid copy-cell action requires row and column indexes from the hint or grid cell context.");
        }

        return CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.CopyGridCell,
                descriptor,
                Warning: warning,
                RowIndex: rowIndex,
                ColumnIndex: columnIndex),
            warning);
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

    private bool TryResolveGridActionHint(
        Control source,
        out RecorderGridActionHint hint,
        out Control matchedSource)
    {
        foreach (var current in EnumerateRelatedControls(source))
        {
            foreach (var candidate in _options.GridActionHints)
            {
                if (TryGetLocator(current, candidate.SourceLocatorKind, out var locatorValue)
                    && string.Equals(candidate.SourceLocatorValue.Trim(), locatorValue, StringComparison.Ordinal))
                {
                    hint = candidate;
                    matchedSource = current;
                    return true;
                }
            }
        }

        hint = null!;
        matchedSource = null!;
        return false;
    }

    private StepCreationResult TryCreateSearchPickerStepCore(
        TextBox searchInput,
        Control results,
        SearchPickerResultsKind resultsKind,
        string? selectedText)
    {
        if (TryResolveGridSearchPickerHint(searchInput, results, resultsKind, out var gridHint))
        {
            return TryCreateGridSearchPickerStep(searchInput, results, selectedText, gridHint);
        }

        if (TryResolveGridHint(searchInput, out _, out _))
        {
            return StepCreationResult.Unsupported(NoGridSearchPickerHintMessage);
        }

        if (!TryResolveSearchPickerHint(searchInput, results, resultsKind, out var hint))
        {
            return StepCreationResult.Unsupported("Controls are not configured as a recorder search picker.");
        }

        var searchText = searchInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return StepCreationResult.Unsupported("Search picker search text is empty.");
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return StepCreationResult.Unsupported("Search picker does not have a selected result to record.");
        }

        var warning = "Recorded composite search picker from configured parts.";
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.SearchPicker,
            hint.LocatorKind,
            hint.FallbackToName,
            results,
            warning);

        return CreateStep(
            results,
            new RecordedStep(
                RecordedActionKind.SearchAndSelect,
                descriptor,
                StringValue: searchText,
                Warning: warning,
                ItemValue: selectedText.Trim()),
            warning);
    }

    private StepCreationResult TryCreateGridSearchPickerStep(
        TextBox searchInput,
        Control results,
        string? selectedText,
        RecorderGridSearchPickerHint hint)
    {
        var searchText = searchInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return StepCreationResult.Unsupported("Grid search picker search text is empty.");
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return StepCreationResult.Unsupported("Grid search picker does not have a selected result to record.");
        }

        if (!TryResolveGridSearchPickerContext(searchInput, hint, out var rowIndex, out var columnIndex))
        {
            return StepCreationResult.Unsupported(
                "Grid search picker requires row and column context. Configure RecorderGridSearchPickerHint column metadata or expose stable row context on the editor.");
        }

        var warning = "Recorded grid search picker from configured hint.";
        var descriptor = new RecordedControlDescriptor(
            RecorderNaming.CreateControlPropertyName(hint.TargetGridLocatorValue, UiControlType.Grid),
            UiControlType.Grid,
            hint.TargetGridLocatorValue.Trim(),
            hint.TargetGridLocatorKind,
            hint.TargetFallbackToName,
            results.GetType().FullName ?? results.GetType().Name,
            warning);

        return CreateStep(
            results,
            new RecordedStep(
                RecordedActionKind.SearchAndSelectGridCell,
                descriptor,
                StringValue: searchText,
                Warning: warning,
                RowIndex: rowIndex,
                ColumnIndex: columnIndex,
                ItemValue: selectedText.Trim()),
            warning);
    }

    private bool TryResolveSearchPickerHint(
        TextBox searchInput,
        Control results,
        SearchPickerResultsKind resultsKind,
        out RecorderSearchPickerHint hint)
    {
        foreach (var candidate in _options.SearchPickerHints)
        {
            var parts = candidate.Parts;
            if (TryGetLocator(searchInput, parts.LocatorKind, out var searchInputLocator)
                && parts.ResultsKind == resultsKind
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

    private bool TryResolveGridSearchPickerHint(
        TextBox searchInput,
        Control results,
        SearchPickerResultsKind resultsKind,
        out RecorderGridSearchPickerHint hint)
    {
        foreach (var candidate in _options.GridSearchPickerHints)
        {
            var parts = candidate.Parts;
            if (TryGetLocator(searchInput, parts.LocatorKind, out var searchInputLocator)
                && parts.ResultsKind == resultsKind
                && TryGetLocator(results, parts.LocatorKind, out var resultsLocator)
                && string.Equals(parts.SearchInputLocator.Trim(), searchInputLocator, StringComparison.Ordinal)
                && string.Equals(parts.ResultsLocator.Trim(), resultsLocator, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(candidate.SourceLocatorValue)
                && !string.IsNullOrWhiteSpace(candidate.TargetGridLocatorValue)
                && MatchesLocator(searchInput, candidate.SourceLocatorKind, candidate.SourceLocatorValue))
            {
                hint = candidate;
                return true;
            }
        }

        hint = null!;
        return false;
    }

    private bool MatchesSearchPickerTextPart(TextBox textBox)
    {
        return _options.SearchPickerHints.Any(hint =>
            TryGetLocator(textBox, hint.Parts.LocatorKind, out var locatorValue)
            && string.Equals(hint.Parts.SearchInputLocator.Trim(), locatorValue, StringComparison.Ordinal));
    }

    private bool MatchesGridSearchPickerTextPart(TextBox textBox)
    {
        return _options.GridSearchPickerHints.Any(hint =>
            TryGetLocator(textBox, hint.Parts.LocatorKind, out var locatorValue)
            && string.Equals(hint.Parts.SearchInputLocator.Trim(), locatorValue, StringComparison.Ordinal));
    }

    private bool TryResolveSearchPickerButton(Control source, out RecorderSearchPickerHint hint)
    {
        foreach (var candidate in _options.SearchPickerHints)
        {
            var parts = candidate.Parts;
            if (MatchesAnyLocator(source, parts.LocatorKind, parts.ApplyButtonLocator, parts.ExpandButtonLocator)
                && !string.IsNullOrWhiteSpace(candidate.LocatorValue))
            {
                hint = candidate;
                return true;
            }
        }

        hint = null!;
        return false;
    }

    private bool TryResolveGridSearchPickerButton(Control source, out RecorderGridSearchPickerHint hint)
    {
        foreach (var candidate in _options.GridSearchPickerHints)
        {
            var parts = candidate.Parts;
            if (MatchesAnyLocator(source, parts.LocatorKind, parts.ApplyButtonLocator, parts.ExpandButtonLocator)
                && MatchesLocator(source, candidate.SourceLocatorKind, candidate.SourceLocatorValue)
                && !string.IsNullOrWhiteSpace(candidate.TargetGridLocatorValue))
            {
                hint = candidate;
                return true;
            }
        }

        hint = null!;
        return false;
    }

    private bool TryResolveDialogHint(
        Control source,
        out RecorderDialogHint hint,
        out RecordedActionKind actionKind)
    {
        foreach (var candidate in _options.DialogHints)
        {
            var parts = candidate.Parts;
            if (MatchesLocator(source, parts.LocatorKind, parts.ConfirmButtonLocator))
            {
                hint = candidate;
                actionKind = RecordedActionKind.ConfirmDialog;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(parts.CancelButtonLocator)
                && MatchesLocator(source, parts.LocatorKind, parts.CancelButtonLocator))
            {
                hint = candidate;
                actionKind = RecordedActionKind.CancelDialog;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(parts.DismissButtonLocator)
                && MatchesLocator(source, parts.LocatorKind, parts.DismissButtonLocator))
            {
                hint = candidate;
                actionKind = RecordedActionKind.DismissDialog;
                return true;
            }
        }

        hint = null!;
        actionKind = default;
        return false;
    }

    private bool TryResolveNotificationHint(Control source, out RecorderNotificationHint hint)
    {
        foreach (var candidate in _options.NotificationHints)
        {
            var dismissLocator = candidate.Parts.DismissButtonLocator;
            if (!string.IsNullOrWhiteSpace(dismissLocator)
                && MatchesLocator(source, candidate.Parts.LocatorKind, dismissLocator))
            {
                hint = candidate;
                return true;
            }
        }

        hint = null!;
        return false;
    }

    private bool TryResolveNotificationTextHint(Control source, out RecorderNotificationHint hint)
    {
        foreach (var candidate in _options.NotificationHints)
        {
            if (MatchesLocator(source, candidate.Parts.LocatorKind, candidate.Parts.TextLocator))
            {
                hint = candidate;
                return true;
            }
        }

        hint = null!;
        return false;
    }

    private bool TryResolveDateRangeFilterHint(
        Control source,
        out RecorderDateRangeFilterHint hint,
        out FilterPopupCommitMode commitMode)
    {
        foreach (var candidate in _options.DateRangeFilterHints)
        {
            var parts = candidate.Parts;
            if (MatchesLocator(source, parts.LocatorKind, parts.ApplyButtonLocator))
            {
                hint = candidate;
                commitMode = FilterPopupCommitMode.Apply;
                return true;
            }

            if (MatchesLocator(source, parts.LocatorKind, parts.CancelButtonLocator))
            {
                hint = candidate;
                commitMode = FilterPopupCommitMode.Cancel;
                return true;
            }
        }

        hint = null!;
        commitMode = default;
        return false;
    }

    private bool TryResolveNumericRangeFilterHint(
        Control source,
        out RecorderNumericRangeFilterHint hint,
        out FilterPopupCommitMode commitMode)
    {
        foreach (var candidate in _options.NumericRangeFilterHints)
        {
            var parts = candidate.Parts;
            if (MatchesLocator(source, parts.LocatorKind, parts.ApplyButtonLocator))
            {
                hint = candidate;
                commitMode = FilterPopupCommitMode.Apply;
                return true;
            }

            if (MatchesLocator(source, parts.LocatorKind, parts.CancelButtonLocator))
            {
                hint = candidate;
                commitMode = FilterPopupCommitMode.Cancel;
                return true;
            }
        }

        hint = null!;
        commitMode = default;
        return false;
    }

    private bool TryResolveFolderExportHint(
        Control source,
        out RecorderFolderExportHint hint,
        out FolderExportCommitMode commitMode)
    {
        foreach (var candidate in _options.FolderExportHints)
        {
            var parts = candidate.Parts;
            if (MatchesLocator(source, parts.LocatorKind, parts.SelectButtonLocator))
            {
                hint = candidate;
                commitMode = FolderExportCommitMode.Select;
                return true;
            }

            if (MatchesLocator(source, parts.LocatorKind, parts.CancelButtonLocator))
            {
                hint = candidate;
                commitMode = FolderExportCommitMode.Cancel;
                return true;
            }
        }

        hint = null!;
        commitMode = default;
        return false;
    }

    private bool TryResolveGridEditHint(Control source, out RecorderGridEditHint hint)
    {
        foreach (var current in EnumerateRelatedControls(source))
        {
            foreach (var candidate in _options.GridEditHints)
            {
                if (TryGetLocator(current, candidate.SourceLocatorKind, out var locatorValue)
                    && string.Equals(candidate.SourceLocatorValue.Trim(), locatorValue, StringComparison.Ordinal))
                {
                    hint = candidate;
                    return true;
                }
            }
        }

        hint = null!;
        return false;
    }

    private bool TryResolveShellNavigationHint(
        Control source,
        out RecorderShellNavigationHint hint,
        out RecordedActionKind actionKind)
    {
        foreach (var candidate in _options.ShellNavigationHints)
        {
            var parts = candidate.Parts;
            var navigationCaptureLocator = FirstNonWhiteSpace(candidate.NavigationCaptureLocator, parts.NavigationLocator);
            var navigationCaptureLocatorKind = candidate.NavigationCaptureLocatorKind ?? parts.LocatorKind;
            if (!string.IsNullOrWhiteSpace(navigationCaptureLocator)
                && MatchesLocator(source, navigationCaptureLocatorKind, navigationCaptureLocator)
                && (UsesCustomNavigationCapture(candidate) || MatchesShellNavigationSource(source, parts.NavigationKind)))
            {
                hint = candidate;
                actionKind = RecordedActionKind.OpenOrActivateShellPane;
                return true;
            }

            var paneTabsCaptureLocator = FirstNonWhiteSpace(candidate.PaneTabsCaptureLocator, parts.PaneTabsLocator);
            var paneTabsCaptureLocatorKind = candidate.PaneTabsCaptureLocatorKind ?? parts.LocatorKind;
            if (!string.IsNullOrWhiteSpace(paneTabsCaptureLocator)
                && MatchesLocator(source, paneTabsCaptureLocatorKind, paneTabsCaptureLocator)
                && (UsesCustomPaneTabsCapture(candidate) || source is TabControl))
            {
                hint = candidate;
                actionKind = RecordedActionKind.ActivateShellPane;
                return true;
            }
        }

        hint = null!;
        actionKind = default;
        return false;
    }

    private string? TryReadShellPaneName(
        Control source,
        RecorderShellNavigationHint hint,
        RecordedActionKind actionKind)
    {
        var paneName = source switch
        {
            ListBox listBox => ExtractSelectionText(listBox.SelectedItem),
            TreeView treeView => ExtractTreeSelectionText(treeView.SelectedItem),
            TabControl tabControl => ExtractTabSelectionText(tabControl),
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(paneName))
        {
            return paneName;
        }

        if (actionKind == RecordedActionKind.ActivateShellPane
            && !string.IsNullOrWhiteSpace(hint.Parts.ActivePaneLabelLocator)
            && TryFindControl(hint.Parts.ActivePaneLabelLocator!, hint.Parts.LocatorKind, out var control))
        {
            return ExtractTextValue(control);
        }

        return null;
    }

    private bool TryResolveGridSearchPickerContext(
        Control searchInput,
        RecorderGridSearchPickerHint hint,
        out int rowIndex,
        out int columnIndex)
    {
        rowIndex = -1;
        columnIndex = -1;

        if (!TryResolveGridSearchPickerGridSource(hint, out var gridHint, out var gridSource))
        {
            return false;
        }

        if (TryReadItemsSource(gridSource, out var items)
            && TryResolveGridRow(searchInput, gridSource, items, out rowIndex, out _))
        {
            // Row resolved from the live grid context.
        }
        else if (!TryResolveGridRowIndexFromAutomationId(searchInput, out rowIndex))
        {
            return false;
        }

        if (hint.ColumnIndex is >= 0)
        {
            columnIndex = hint.ColumnIndex.Value;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(hint.ColumnName))
        {
            columnIndex = FindColumnIndex(gridHint.ColumnPropertyNames, hint.ColumnName);
            return columnIndex >= 0;
        }

        if (TryResolveGridColumnIndex(searchInput, gridSource, gridHint.ColumnPropertyNames.Count, out columnIndex))
        {
            return true;
        }

        return TryResolveGridColumnIndexFromAutomationId(searchInput, out columnIndex);
    }

    private bool TryResolveGridSearchPickerGridSource(
        RecorderGridSearchPickerHint hint,
        out RecorderGridHint gridHint,
        out Control gridSource)
    {
        foreach (var candidate in _options.GridHints)
        {
            if (candidate.TargetLocatorKind == hint.TargetGridLocatorKind
                && string.Equals(candidate.TargetLocatorValue.Trim(), hint.TargetGridLocatorValue.Trim(), StringComparison.Ordinal)
                && TryFindControl(candidate.SourceLocatorValue, candidate.SourceLocatorKind, out gridSource))
            {
                gridHint = candidate;
                return true;
            }
        }

        gridHint = null!;
        gridSource = null!;
        return false;
    }

    private static int FindColumnIndex(IReadOnlyList<string> columnNames, string columnName)
    {
        for (var i = 0; i < columnNames.Count; i++)
        {
            if (string.Equals(columnNames[i], columnName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private bool TryResolveGridRowIndex(Control source, RecorderGridActionHint hint, out int rowIndex)
    {
        if (hint.RowIndex is >= 0)
        {
            rowIndex = hint.RowIndex.Value;
            return true;
        }

        if (TryResolveGridHint(source, out _, out var gridSource)
            && TryReadItemsSource(gridSource, out var items)
            && TryResolveGridRow(source, gridSource, items, out rowIndex, out _))
        {
            return true;
        }

        return TryResolveGridRowIndexFromAutomationId(source, out rowIndex);
    }

    private bool TryResolveGridCellIndexes(
        Control source,
        RecorderGridActionHint hint,
        out int rowIndex,
        out int columnIndex)
    {
        rowIndex = hint.RowIndex is >= 0 ? hint.RowIndex.Value : -1;
        columnIndex = hint.ColumnIndex is >= 0 ? hint.ColumnIndex.Value : -1;
        if (rowIndex >= 0 && columnIndex >= 0)
        {
            return true;
        }

        if (TryResolveGridHint(source, out var gridHint, out var gridSource)
            && TryReadItemsSource(gridSource, out var items)
            && TryResolveGridCell(source, gridSource, gridHint, items, out var resolvedRowIndex, out var resolvedColumnIndex, out _))
        {
            if (rowIndex < 0)
            {
                rowIndex = resolvedRowIndex;
            }

            if (columnIndex < 0)
            {
                columnIndex = resolvedColumnIndex;
            }

            return rowIndex >= 0 && columnIndex >= 0;
        }

        TryResolveGridRowIndexFromAutomationId(source, out var parsedRowIndex);
        TryResolveGridColumnIndexFromAutomationId(source, out var parsedColumnIndex);
        if (rowIndex < 0)
        {
            rowIndex = parsedRowIndex;
        }

        if (columnIndex < 0)
        {
            columnIndex = parsedColumnIndex;
        }

        return rowIndex >= 0 && columnIndex >= 0;
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

    private static bool TryResolveGridRow(
        Control source,
        Control gridSource,
        IReadOnlyList<object?> items,
        out int rowIndex,
        out object item)
    {
        for (Control? current = source; current is not null && !ReferenceEquals(current, gridSource); current = current.GetVisualParent() as Control)
        {
            var dataContext = current.DataContext;
            if (dataContext is not null && TryFindItemIndex(items, dataContext, out rowIndex, out item))
            {
                return true;
            }
        }

        rowIndex = -1;
        item = null!;
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

    private static bool TryResolveGridRowIndexFromAutomationId(Control source, out int rowIndex)
    {
        foreach (var current in EnumerateRelatedControls(source))
        {
            if (TryParseVisualGridIndex(AutomationProperties.GetAutomationId(current), "_Row", out rowIndex))
            {
                return true;
            }
        }

        rowIndex = -1;
        return false;
    }

    private static bool TryResolveGridColumnIndexFromAutomationId(Control source, out int columnIndex)
    {
        foreach (var current in EnumerateRelatedControls(source))
        {
            if (TryParseVisualGridIndex(AutomationProperties.GetAutomationId(current), "_Cell", out columnIndex))
            {
                return true;
            }
        }

        columnIndex = -1;
        return false;
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

    private StepCreationResult TryCreateGridEditTextStep(
        Control source,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridEditHint hint)
    {
        if (!TryFindControl(hint.ValueLocatorValue, hint.ValueLocatorKind, out var valueControl)
            || valueControl is not TextBox textBox)
        {
            return StepCreationResult.Unsupported("Grid text edit hint value locator was not found or is not a TextBox.");
        }

        return CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.EditGridCellText,
                descriptor,
                StringValue: textBox.Text ?? string.Empty,
                Warning: warning,
                RowIndex: hint.RowIndex,
                ColumnIndex: hint.ColumnIndex,
                GridCellEditCommitMode: hint.CommitMode),
            warning);
    }

    private StepCreationResult TryCreateGridEditNumberStep(
        Control source,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridEditHint hint)
    {
        if (!TryFindControl(hint.ValueLocatorValue, hint.ValueLocatorKind, out var valueControl)
            || !TryReadNumericValue(valueControl, out var value))
        {
            return StepCreationResult.Unsupported("Grid numeric edit hint value locator does not expose a numeric value.");
        }

        return CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.EditGridCellNumber,
                descriptor,
                DoubleValue: value,
                Warning: warning,
                RowIndex: hint.RowIndex,
                ColumnIndex: hint.ColumnIndex,
                GridCellEditCommitMode: hint.CommitMode),
            warning);
    }

    private StepCreationResult TryCreateGridEditDateStep(
        Control source,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridEditHint hint)
    {
        if (!TryFindControl(hint.ValueLocatorValue, hint.ValueLocatorKind, out var valueControl)
            || !TryReadDateValue(valueControl, out var value))
        {
            return StepCreationResult.Unsupported("Grid date edit hint value locator does not expose a date value.");
        }

        return CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.EditGridCellDate,
                descriptor,
                DateValue: value,
                Warning: warning,
                RowIndex: hint.RowIndex,
                ColumnIndex: hint.ColumnIndex,
                GridCellEditCommitMode: hint.CommitMode),
            warning);
    }

    private StepCreationResult TryCreateGridEditComboStep(
        Control source,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridEditHint hint)
    {
        if (!TryFindControl(hint.ValueLocatorValue, hint.ValueLocatorKind, out var valueControl))
        {
            return StepCreationResult.Unsupported("Grid combo edit hint value locator was not found.");
        }

        var selectedText = valueControl switch
        {
            ComboBox comboBox => ExtractSelectionText(comboBox.SelectedItem),
            ListBox listBox => ExtractSelectionText(listBox.SelectedItem),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return StepCreationResult.Unsupported("Grid combo edit hint value locator does not have a selected item.");
        }

        return CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.SelectGridCellComboItem,
                descriptor,
                StringValue: selectedText.Trim(),
                Warning: warning,
                RowIndex: hint.RowIndex,
                ColumnIndex: hint.ColumnIndex,
                GridCellEditCommitMode: hint.CommitMode),
            warning);
    }

    private bool TryReadDateRangeValues(
        DateRangeFilterParts parts,
        out DateTime? from,
        out DateTime? to,
        out string message)
    {
        if (!TryFindControl(parts.FromLocator, parts.LocatorKind, out var fromControl)
            || !TryFindControl(parts.ToLocator, parts.LocatorKind, out var toControl))
        {
            from = null;
            to = null;
            message = "Date range filter endpoints were not found.";
            return false;
        }

        var hasFrom = TryReadDateValue(fromControl, out var fromValue);
        var hasTo = TryReadDateValue(toControl, out var toValue);
        from = hasFrom ? fromValue : null;
        to = hasTo ? toValue : null;
        if (from.HasValue || to.HasValue)
        {
            message = string.Empty;
            return true;
        }

        message = "Date range filter endpoints do not expose date values.";
        return false;
    }

    private bool TryReadNumericRangeValues(
        NumericRangeFilterParts parts,
        out double? from,
        out double? to,
        out string message)
    {
        if (!TryFindControl(parts.FromLocator, parts.LocatorKind, out var fromControl)
            || !TryFindControl(parts.ToLocator, parts.LocatorKind, out var toControl))
        {
            from = null;
            to = null;
            message = "Numeric range filter endpoints were not found.";
            return false;
        }

        var hasFrom = TryReadNumericValue(fromControl, out var fromValue);
        var hasTo = TryReadNumericValue(toControl, out var toValue);
        from = hasFrom ? fromValue : null;
        to = hasTo ? toValue : null;
        if (from.HasValue || to.HasValue)
        {
            message = string.Empty;
            return true;
        }

        message = "Numeric range filter endpoints do not expose numeric values.";
        return false;
    }

    private static bool TryReadDateValue(Control control, out DateTime value)
    {
        switch (control)
        {
            case DatePicker datePicker when datePicker.SelectedDate is { } selectedDate:
                value = selectedDate.Date;
                return true;
            case Calendar calendar when calendar.SelectedDate is { } selectedDate:
                value = selectedDate.Date;
                return true;
            case TextBox textBox:
                return DateTime.TryParse(
                    textBox.Text,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeLocal,
                    out value)
                    || DateTime.TryParse(textBox.Text, out value);
            default:
                value = default;
                return false;
        }
    }

    private static bool TryReadNumericValue(Control control, out double value)
    {
        if (control is TextBox textBox)
        {
            return double.TryParse(
                textBox.Text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        var valueProperty = control.GetType().GetProperty("Value");
        var propertyValue = valueProperty?.GetValue(control);
        switch (propertyValue)
        {
            case double doubleValue:
                value = doubleValue;
                return true;
            case decimal decimalValue:
                value = (double)decimalValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            default:
                value = default;
                return false;
        }
    }

    private bool MatchesDateRangeTextPart(TextBox textBox)
    {
        return _options.DateRangeFilterHints.Any(hint =>
            hint.Parts.EditorKind == FilterValueEditorKind.TextBox
            && MatchesAnyLocator(textBox, hint.Parts.LocatorKind, hint.Parts.FromLocator, hint.Parts.ToLocator));
    }

    private bool MatchesDateRangeDatePart(DatePicker datePicker)
    {
        return _options.DateRangeFilterHints.Any(hint =>
            hint.Parts.EditorKind == FilterValueEditorKind.DateTimePicker
            && MatchesAnyLocator(datePicker, hint.Parts.LocatorKind, hint.Parts.FromLocator, hint.Parts.ToLocator));
    }

    private bool MatchesNumericRangeTextPart(TextBox textBox)
    {
        return _options.NumericRangeFilterHints.Any(hint =>
            hint.Parts.EditorKind is FilterValueEditorKind.TextBox or FilterValueEditorKind.Spinner
            && MatchesAnyLocator(textBox, hint.Parts.LocatorKind, hint.Parts.FromLocator, hint.Parts.ToLocator));
    }

    private bool MatchesFolderExportPathPart(TextBox textBox)
    {
        return _options.FolderExportHints.Any(hint =>
            MatchesLocator(textBox, hint.Parts.LocatorKind, hint.Parts.FolderPathInputLocator));
    }

    private bool MatchesGridEditValuePart(Control control)
    {
        return _options.GridEditHints.Any(hint =>
            MatchesLocator(control, hint.ValueLocatorKind, hint.ValueLocatorValue));
    }

    private RecordedControlDescriptor CreateCompositeDescriptor(
        string locatorValue,
        UiControlType controlType,
        UiLocatorKind locatorKind,
        bool fallbackToName,
        Control source,
        string warning)
    {
        return new RecordedControlDescriptor(
            RecorderNaming.CreateControlPropertyName(locatorValue, controlType),
            controlType,
            locatorValue.Trim(),
            locatorKind,
            fallbackToName,
            source.GetType().FullName ?? source.GetType().Name,
            warning);
    }

    private bool TryFindControl(string locatorValue, UiLocatorKind locatorKind, out Control control)
    {
        control = null!;
        var descriptor = new RecordedControlDescriptor(
            "TemporaryLookup",
            UiControlType.AutomationElement,
            locatorValue.Trim(),
            locatorKind,
            FallbackToName: locatorKind == UiLocatorKind.Name,
            AvaloniaTypeName: typeof(Control).FullName ?? nameof(Control),
            Warning: null);
        var resolved = _selectorResolver.ResolveExisting(descriptor);
        if (resolved.MatchedControl is null)
        {
            return false;
        }

        control = resolved.MatchedControl;
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

    private static bool MatchesLocator(Control source, UiLocatorKind locatorKind, string locatorValue)
    {
        if (string.IsNullOrWhiteSpace(locatorValue))
        {
            return false;
        }

        foreach (var current in EnumerateRelatedControls(source))
        {
            if (TryGetLocator(current, locatorKind, out var currentLocator)
                && string.Equals(currentLocator, locatorValue.Trim(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAnyLocator(Control source, UiLocatorKind locatorKind, params string?[] locatorValues)
    {
        return locatorValues.Any(locatorValue => !string.IsNullOrWhiteSpace(locatorValue) && MatchesLocator(source, locatorKind, locatorValue!));
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
    }

    private static IEnumerable<Control> EnumerateRelatedControls(Control? control)
    {
        if (control is null)
        {
            yield break;
        }

        var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<Control>();
        queue.Enqueue(control);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;

            if (current.GetVisualParent() is Control visualParent)
            {
                queue.Enqueue(visualParent);
            }

            if (current is ILogical { LogicalParent: Control logicalParent })
            {
                queue.Enqueue(logicalParent);
            }

            if (current is StyledElement { TemplatedParent: Control templatedParent })
            {
                queue.Enqueue(templatedParent);
            }
        }
    }

    private static string? ExtractTreeSelectionText(object? selectedItem)
    {
        return selectedItem switch
        {
            TreeViewItem treeViewItem when !string.IsNullOrWhiteSpace(treeViewItem.Header?.ToString()) => treeViewItem.Header?.ToString(),
            TreeViewItem treeViewItem when !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(treeViewItem)) => AutomationProperties.GetAutomationId(treeViewItem),
            Control control when !string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)) => AutomationProperties.GetName(control),
            _ => ExtractSelectionText(selectedItem)
        };
    }

    private static string? ExtractTabSelectionText(TabControl tabControl)
    {
        if (tabControl.SelectedItem is TabItem tabItem)
        {
            return FirstNonWhiteSpace(
                tabItem.Header?.ToString(),
                ExtractTextValue(tabItem),
                AutomationProperties.GetAutomationId(tabItem),
                tabItem.Name);
        }

        return ExtractSelectionText(tabControl.SelectedItem);
    }

    private static string? ExtractSelectionText(object? selectedItem)
    {
        return selectedItem switch
        {
            null => null,
            string value => value,
            TabItem tabItem => FirstNonWhiteSpace(
                tabItem.Header?.ToString(),
                ExtractTextValue(tabItem),
                AutomationProperties.GetAutomationId(tabItem),
                tabItem.Name),
            Control control => FirstNonWhiteSpace(
                ExtractTextValue(control),
                AutomationProperties.GetAutomationId(control),
                control.Name),
            _ when TryReadPropertyValue(selectedItem, "Header", out var header) && !string.IsNullOrWhiteSpace(header) => header,
            _ when TryReadPropertyValue(selectedItem, "Title", out var title) && !string.IsNullOrWhiteSpace(title) => title,
            _ when TryReadPropertyValue(selectedItem, "Text", out var text) && !string.IsNullOrWhiteSpace(text) => text,
            _ when TryReadPropertyValue(selectedItem, "Name", out var name) && !string.IsNullOrWhiteSpace(name) => name,
            _ => selectedItem?.ToString()
        };
    }

    private static bool CanReadShellPaneNameFromSource(Control source)
    {
        return source is ListBox or TreeView or TabControl;
    }

    private static bool UsesCustomNavigationCapture(RecorderShellNavigationHint hint)
    {
        return !string.IsNullOrWhiteSpace(hint.NavigationCaptureLocator)
            || hint.NavigationCaptureLocatorKind is not null;
    }

    private static bool UsesCustomPaneTabsCapture(RecorderShellNavigationHint hint)
    {
        return !string.IsNullOrWhiteSpace(hint.PaneTabsCaptureLocator)
            || hint.PaneTabsCaptureLocatorKind is not null;
    }

    private static bool MatchesShellNavigationSource(Control source, ShellNavigationSourceKind navigationKind)
    {
        return navigationKind switch
        {
            ShellNavigationSourceKind.Tree => source is TreeView,
            ShellNavigationSourceKind.ListBox => source is ListBox,
            ShellNavigationSourceKind.Tab => source is TabControl,
            _ => false
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
            new ProgressAssertionExtractor(),
            new ListBoxAssertionExtractor(),
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

    private sealed class ProgressAssertionExtractor : IRecorderAssertionExtractor
    {
        public bool TryCreate(Control control, RecorderAssertionMode mode, out RecorderAssertionCandidate? candidate)
        {
            candidate = null;
            if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Text)
                || control is not ProgressBar progressBar)
            {
                return false;
            }

            candidate = new RecorderAssertionCandidate(
                UiControlType.ProgressBar,
                RecordedActionKind.WaitUntilProgressAtLeast,
                DoubleValue: progressBar.Value);
            return true;
        }
    }

    private sealed class ListBoxAssertionExtractor : IRecorderAssertionExtractor
    {
        public bool TryCreate(Control control, RecorderAssertionMode mode, out RecorderAssertionCandidate? candidate)
        {
            candidate = null;
            if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Text)
                || control is not ListBox listBox)
            {
                return false;
            }

            var selectedText = ExtractSelectionText(listBox.SelectedItem);
            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                candidate = new RecorderAssertionCandidate(
                    UiControlType.ListBox,
                    RecordedActionKind.WaitUntilListBoxContains,
                    StringValue: selectedText.Trim());
                return true;
            }

            var itemCount = listBox.ItemCount;
            if (itemCount > 0)
            {
                candidate = new RecorderAssertionCandidate(
                    UiControlType.ListBox,
                    RecordedActionKind.WaitUntilHasItemsAtLeast,
                    IntValue: itemCount);
                return true;
            }

            return false;
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
