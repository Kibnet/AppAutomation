using System.Collections;
using static AppAutomation.Recorder.Avalonia.RecorderCaptureControlReader;
using static AppAutomation.Recorder.Avalonia.RecorderGridCaptureResolver;
using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using static AppAutomation.Recorder.Avalonia.RecorderSemanticValueModel;
using SemanticValueCandidate = AppAutomation.Recorder.Avalonia.RecorderSemanticValueModel.SemanticValueCandidate;
using GridValueContext = AppAutomation.Recorder.Avalonia.RecorderSemanticValueModel.GridValueContext;

namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderStepFactory
{
    internal const string NoGridActionHintMessage = "Recorder does not have a grid action hint for this source.";
    internal const string NoGridSearchPickerHintMessage = "Recorder does not have a grid search picker hint for this editor.";
    private readonly AppAutomationRecorderOptions _options;
    private readonly Func<Control?>? _validationRootProvider;
    private readonly Func<IReadOnlyList<RecordedStep>>? _recordedStepsProvider;
    private readonly RecorderSelectorResolver _selectorResolver;
    private readonly RecorderStepValidator _stepValidator;
    private readonly RecorderCaptureControlReader _controlReader;
    private readonly RecorderGridCaptureResolver _gridCapture;
    private readonly IReadOnlyList<IRecorderAssertionExtractor> _assertionExtractors;
    private readonly IReadOnlyList<RecorderDialogHint> _dialogHints;
    private static StepCreationResult CreateStepFromSnapshot(
        RecorderSemanticValueSnapshot snapshot,
        RecordedStep step,
        string message) =>
        RecorderSemanticValueModel.CreateStepFromSnapshot(
            snapshot,
            step,
            message,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

    private static bool TryNormalizeLiteralDateExpression(
        SemanticValueCandidate candidate,
        RecorderDateExpression? requested,
        out RecorderDateExpression? normalized,
        out string error) =>
        RecorderSemanticValueModel.TryNormalizeLiteralDateExpression(
            candidate,
            requested,
            DateTime.Today,
            out normalized,
            out error);

    public RecorderStepFactory(AppAutomationRecorderOptions options, Window? validationWindow = null)
        : this(
            options,
            validationWindow is null
                ? null
                : () => validationWindow.Content as Control)
    {
    }

    internal RecorderStepFactory(
        AppAutomationRecorderOptions options,
        Func<Control?>? validationRootProvider,
        Func<IReadOnlyList<RecordedStep>>? recordedStepsProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ = _options.FreezeGridHints();
        _validationRootProvider = validationRootProvider;
        _recordedStepsProvider = recordedStepsProvider;
        _selectorResolver = new RecorderSelectorResolver(options, validationRootProvider);
        _controlReader = new RecorderCaptureControlReader(_selectorResolver);
        _gridCapture = new RecorderGridCaptureResolver(options, _selectorResolver, _controlReader, validationRootProvider);
        _stepValidator = new RecorderStepValidator(options);
        _assertionExtractors = CreateAssertionExtractors(options);
        _dialogHints = options.DialogHints
            .Select(static hint => hint with { Parts = hint.Parts.NormalizeAndValidate() })
            .ToArray();
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

    public StepCreationResult TryCreateMenuItemStep(MenuItem? item)
    {
        if (item is null)
        {
            return StepCreationResult.Unsupported("Recorder does not support this menu target.");
        }

        if (item.Items.Count > 0)
        {
            return StepCreationResult.Unsupported("Opening a parent menu item does not create a recorded action.");
        }

        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(item))
            && IsDirectMenuItem(item))
        {
            var directLocator = _selectorResolver.Resolve(item, UiControlType.MenuItem);
            if (!directLocator.Success || directLocator.Control is null)
            {
                return StepCreationResult.Unsupported(directLocator.Message);
            }

            return CreateStep(
                item,
                new RecordedStep(
                    RecordedActionKind.InvokeMenuItem,
                    directLocator.Control,
                    Warning: directLocator.Control.Warning,
                    ValidationStatus: directLocator.ValidationStatus,
                    ValidationMessage: directLocator.ValidationMessage,
                    CanPersist: directLocator.CanPersist));
        }

        if (!TryBuildMenuPath(item, out var menu, out var path, out var message))
        {
            return StepCreationResult.Unsupported(message);
        }

        var menuLocator = _selectorResolver.Resolve(menu, UiControlType.Menu);
        if (!menuLocator.Success || menuLocator.Control is null)
        {
            return StepCreationResult.Unsupported(menuLocator.Message);
        }

        return CreateStep(
            item,
            new RecordedStep(
                RecordedActionKind.InvokeMenuItem,
                menuLocator.Control,
                Warning: menuLocator.Control.Warning,
                StringValues: path,
                ValidationStatus: menuLocator.ValidationStatus,
                ValidationMessage: menuLocator.ValidationMessage,
                CanPersist: menuLocator.CanPersist));
    }

    public StepCreationResult TryCreateContextMenuItemStep(
        MenuItem? item,
        Control? owner,
        out bool belongsToOwner)
    {
        belongsToOwner = false;
        if (item is null || owner is null)
        {
            return StepCreationResult.Unsupported(
                "Recorder could not associate the context-menu item with a stable owner.");
        }

        var itemRoots = EnumerateContextMenuItemRoots(owner).ToArray();
        foreach (var rootItems in itemRoots)
        {
            if (!TryFindMenuItemPath(rootItems, item, out var itemPath))
            {
                continue;
            }

            belongsToOwner = true;
            if (!TryValidateMenuPath(rootItems, itemPath, out var path, out var pathError))
            {
                return StepCreationResult.Unsupported(pathError);
            }

            var ownerLocator = _selectorResolver.Resolve(owner, ClassifyControlType(owner));
            if (!ownerLocator.Success || ownerLocator.Control is null)
            {
                return StepCreationResult.Unsupported(ownerLocator.Message);
            }

            return CreateStep(
                item,
                new RecordedStep(
                    RecordedActionKind.InvokeContextMenuItem,
                    ownerLocator.Control,
                    StringValues: path,
                    Warning: ownerLocator.Control.Warning,
                    ValidationStatus: ownerLocator.ValidationStatus,
                    ValidationMessage: ownerLocator.ValidationMessage,
                    CanPersist: ownerLocator.CanPersist));
        }

        return StepCreationResult.Unsupported(
            "The selected menu item does not belong to the pending context-menu owner.");
    }

    public bool BelongsToContextMenuOwner(MenuItem? item, Control? owner)
    {
        if (item is null || owner is null)
        {
            return false;
        }

        return EnumerateContextMenuItemRoots(owner)
            .Any(rootItems => TryFindMenuItemPath(rootItems, item, out _));
    }

    public StepCreationResult TryCreateTextEntryStep(TextBox textBox, string? replayText = null)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        if (IsPresenterTimeSource(textBox))
            return StepCreationResult.Unsupported("The configured presenter input is recorded only after its committed UI value is confirmed.");

        if (TryResolveSearchControlHint(textBox, out var searchHint))
        {
            var searchText = textBox.Text ?? string.Empty;
            var actionKind = string.IsNullOrEmpty(searchText)
                ? RecordedActionKind.ClearSearch
                : RecordedActionKind.EnterSearch;
            var warning = actionKind == RecordedActionKind.ClearSearch
                ? "Recorded search clear from configured SearchControl input."
                : "Recorded search input from configured SearchControl input.";
            var searchDescriptor = CreateCompositeDescriptor(
                searchHint.LocatorValue,
                UiControlType.Search,
                searchHint.LocatorKind,
                searchHint.FallbackToName,
                textBox,
                warning);
            return CreateStep(
                textBox,
                new RecordedStep(
                    actionKind,
                    searchDescriptor,
                    StringValue: actionKind == RecordedActionKind.EnterSearch ? searchText : null,
                    Warning: warning),
                warning);
        }

        var locatorResult = _selectorResolver.Resolve(textBox, UiControlType.TextBox);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        var descriptor = locatorResult.Control;
        var text = replayText ?? textBox.Text ?? string.Empty;
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

    internal string ReadTextEntryValue(TextBox textBox, string? capturedText) =>
        TryResolveSearchControlHint(textBox, out _)
            ? textBox.Text ?? string.Empty
            : RecorderNumericValueReader.ReadTextForReplay(textBox, capturedText);

    internal StepCreationResult TryCreateGeneratedTextEntryStep(
        TextBox textBox,
        string previewValue,
        RecorderGeneratedValueOption generatedValue,
        bool definesGeneratedValue)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(generatedValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(previewValue);

        if (!RecorderGeneratedValueFormats.TryGet(generatedValue.Kind, out var format)
            || generatedValue.Ordinal < 1 || generatedValue.Ordinal > format.MaximumOrdinal)
            return StepCreationResult.Unsupported("The generated value has an unsupported format or ordinal.");

        if (TryResolveSearchControlHint(textBox, out _))
        {
            return StepCreationResult.Unsupported(
                "Generated values can only be entered into a writable text field, not a SearchControl input.");
        }

        if (MatchesSearchPickerTextPart(textBox)
            || MatchesGridSearchPickerTextPart(textBox)
            || ShouldSuppressCompositeTextEntry(textBox))
        {
            return StepCreationResult.Unsupported(
                "Generated values are not supported for an internal part of a composite control.");
        }

        var locatorResult = _selectorResolver.Resolve(textBox, UiControlType.TextBox);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        var descriptor = locatorResult.Control;
        if (descriptor.ControlType != UiControlType.TextBox
            || TryResolveActionHint(textBox, descriptor) != RecorderActionHint.None)
        {
            return StepCreationResult.Unsupported(
                "Generated values can only be entered into a logical ITextBoxControl.");
        }

        return CreateStep(
            textBox,
            new RecordedStep(
                RecordedActionKind.EnterText,
                descriptor,
                StringValue: previewValue,
                Warning: descriptor.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist,
                GeneratedValueId: generatedValue.GeneratedValueId,
                GeneratedValueVariableName: generatedValue.VariableName,
                GeneratedValueOrdinal: generatedValue.Ordinal,
                DefinesGeneratedValue: definesGeneratedValue)
            { GeneratedValueKind = generatedValue.Kind },
            locatorResult.Message);
    }

    internal StepCreationResult TryCreateCopiedTextEntryStep(
        TextBox textBox,
        RecorderCopiedValueOption copiedValue)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(copiedValue);

        if (TryResolveSearchControlHint(textBox, out _))
        {
            var searchStep = TryCreateTextEntryStep(textBox);
            return searchStep.Step?.ActionKind == RecordedActionKind.EnterSearch
                ? searchStep with
                {
                    Step = searchStep.Step with { InputCopiedValueId = copiedValue.CopiedValueId }
                }
                : searchStep;
        }

        if (MatchesSearchPickerTextPart(textBox)
            || MatchesGridSearchPickerTextPart(textBox)
            || ShouldSuppressCompositeTextEntry(textBox))
        {
            return StepCreationResult.Unsupported(
                "Copied values are not supported for an internal part of a composite control.");
        }

        var locatorResult = _selectorResolver.Resolve(textBox, UiControlType.TextBox);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        var descriptor = locatorResult.Control;
        if (descriptor.ControlType != UiControlType.TextBox
            || TryResolveActionHint(textBox, descriptor) != RecorderActionHint.None)
        {
            return StepCreationResult.Unsupported(
                "Copied values can only be entered into a logical ITextBoxControl.");
        }

        return CreateStep(
            textBox,
            new RecordedStep(
                RecordedActionKind.EnterText,
                descriptor,
                StringValue: copiedValue.PreviewValue,
                Warning: descriptor.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist,
                InputCopiedValueId: copiedValue.CopiedValueId),
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

        var locatorResult = _selectorResolver.ResolvePrimitiveSelection(comboBox, UiControlType.ComboBox);
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

    public SingleSelectCaptureResult TryCreateSingleSelectStep(ComboBox results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return TryCreateSingleSelectStepCore(
            results,
            SingleSelectResultsKind.ComboBox,
            ExtractSelectionText(results.SelectedItem));
    }

    public SingleSelectCaptureResult TryCreateSingleSelectStep(ListBox results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return TryCreateSingleSelectStepCore(
            results,
            SingleSelectResultsKind.ListBox,
            ExtractSelectionText(results.SelectedItem));
    }

    public GridComboSelectionContextResolution ResolveGridComboSelectionContext(Control source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _gridCapture.TryResolveGridComboSelectionContext(source);
    }

    public bool IsGridCellContextPart(Control source, GridComboSelectionContext context) =>
        _gridCapture.IsGridCellContextPart(source, context);

    public bool IsCatalogGridCell(Control source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _gridCapture.TryResolveGridHint(source, out var hint, out _)
            && _gridCapture.FindGridDefinition(hint) is not null;
    }

    internal IReadOnlyList<Control> ReadMaterializedGridCellControls(Control source) =>
        _gridCapture.ReadMaterializedGridCellControls(source);

    internal IReadOnlyList<Control> ReadConfiguredGridRoots() => _gridCapture.ReadConfiguredGridRoots();

    internal bool IsConfiguredGridRoot(Control source) => _gridCapture.IsConfiguredGridRoot(source);

    internal bool IsConfiguredComboBoxFilterRoot(Control source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _options.ComboBoxFilterHints.Any(hint =>
            TryGetLocator(source, hint.Parts.LocatorKind, out var locatorValue)
            && string.Equals(hint.Parts.RootLocator, locatorValue, StringComparison.Ordinal));
    }

    public bool ShouldSuppressCatalogGridTextEntry(TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        return _gridCapture.TryGetCatalogGridEditorKind(textBox, out var editorKind)
            && editorKind is GridCellEditorKind.ComboBox
                or GridCellEditorKind.Date
                or GridCellEditorKind.Time
                or GridCellEditorKind.CheckBox;
    }

    public GridComboSelectionCaptureResult TryCreateGridComboSelectionStep(
        ComboBox results,
        GridComboSelectionContextResolution? preparedContext = null,
        string? capturedSearchText = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        return TryCreateGridComboSelectionStepCore(
            results,
            ExtractSelectionText(results.SelectedItem),
            preparedContext,
            capturedSearchText);
    }

    public GridComboSelectionCaptureResult TryCreateGridComboSelectionStep(
        ListBox results,
        GridComboSelectionContextResolution? preparedContext = null,
        string? capturedSearchText = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        return TryCreateGridComboSelectionStepCore(
            results,
            ExtractSelectionText(results.SelectedItem),
            preparedContext,
            capturedSearchText);
    }

    public ColorPickerCaptureResult TryCreateColorPickerStep(ComboBox palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return TryCreateColorPickerStepCore(
            palette,
            ColorPaletteKind.ComboBox,
            ExtractSelectionText(palette.SelectedItem));
    }

    public ColorPickerCaptureResult TryCreateColorPickerStep(ListBox palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return TryCreateColorPickerStepCore(
            palette,
            ColorPaletteKind.ListBox,
            ExtractSelectionText(palette.SelectedItem));
    }

    public ColorPickerCaptureResult TryCreateColorPickerStep(Control source, string color)
    {
        ArgumentNullException.ThrowIfNull(source);
        var matchingHints = _options.ColorPickerHints
            .Where(hint => IsColorPickerPart(source, hint))
            .ToArray();
        return CreateColorPickerCapture(source, matchingHints, color);
    }

    public bool IsColorPickerInput(TextBox input, RecorderColorPickerHint hint)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(hint);
        return MatchesAnyLocator(
            input,
            hint.Parts.LocatorKind,
            hint.Parts.CustomValueLocator,
            hint.Parts.CurrentValueLocator);
    }

    public bool IsColorPickerPart(Control? source, RecorderColorPickerHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        return EnumerateRelatedControls(source).Any(current =>
            MatchesLocator(current, hint.LocatorKind, hint.LocatorValue)
            || MatchesAnyLocator(
                current,
                hint.Parts.LocatorKind,
                hint.Parts.RootLocator,
                hint.Parts.CurrentValueLocator,
                hint.Parts.OpenButtonLocator,
                hint.Parts.PopupRootLocator,
                hint.Parts.PaletteLocator,
                hint.Parts.CustomValueLocator,
                hint.Parts.ConfirmButtonLocator,
                hint.Parts.CancelButtonLocator));
    }

    public bool TryResolveColorPickerButton(
        Control? source,
        out RecorderColorPickerHint hint,
        out bool isConfirm)
    {
        hint = null!;
        isConfirm = false;
        foreach (var current in EnumerateRelatedControls(source))
        {
            foreach (var candidate in _options.ColorPickerHints)
            {
                if (!string.IsNullOrWhiteSpace(candidate.Parts.ConfirmButtonLocator)
                    && MatchesLocator(current, candidate.Parts.LocatorKind, candidate.Parts.ConfirmButtonLocator))
                {
                    hint = candidate;
                    isConfirm = true;
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(candidate.Parts.CancelButtonLocator)
                    && MatchesLocator(current, candidate.Parts.LocatorKind, candidate.Parts.CancelButtonLocator))
                {
                    hint = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    public bool ShouldSuppressColorPickerButton(Control? source)
    {
        return EnumerateRelatedControls(source).Any(current => _options.ColorPickerHints.Any(hint =>
            !string.IsNullOrWhiteSpace(hint.Parts.OpenButtonLocator)
            && MatchesLocator(current, hint.Parts.LocatorKind, hint.Parts.OpenButtonLocator)));
    }

    public bool TryReadColorPickerValue(RecorderColorPickerHint hint, out string color)
    {
        ArgumentNullException.ThrowIfNull(hint);
        foreach (var locator in new[] { hint.Parts.CurrentValueLocator, hint.Parts.CustomValueLocator })
        {
            if (!string.IsNullOrWhiteSpace(locator)
                && _controlReader.TryFindControl(locator, hint.Parts.LocatorKind, out var valueControl)
                && ColorValue.TryNormalize(ExtractTextValue(valueControl), out color))
            {
                return true;
            }
        }

        color = string.Empty;
        return false;
    }

    public bool IsSingleSelectPair(TextBox input, Control results)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(results);
        return _options.SingleSelectHints.Any(hint =>
            IsSingleSelectInput(input, hint)
            && IsSingleSelectResults(results, hint));
    }

    public bool IsSingleSelectInput(TextBox input, RecorderSingleSelectHint hint)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(hint);
        return !string.IsNullOrWhiteSpace(hint.Parts.InputLocator)
            && MatchesLocator(input, hint.Parts.LocatorKind, hint.Parts.InputLocator);
    }

    public bool ShouldSuppressSingleSelectInput(TextBox input)
    {
        return _options.SingleSelectHints.Any(hint =>
            !hint.Parts.PersistInputText && IsSingleSelectInput(input, hint));
    }

    public bool IsSingleSelectPart(Control? source, RecorderSingleSelectHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        return EnumerateRelatedControls(source).Any(current =>
            MatchesLocator(current, hint.LocatorKind, hint.LocatorValue)
                || MatchesAnyLocator(
                    current,
                    hint.Parts.LocatorKind,
                    hint.Parts.RootLocator,
                    hint.Parts.ResultsLocator,
                    hint.Parts.InputLocator,
                    hint.Parts.OpenButtonLocator,
                    hint.Parts.SelectedValueLocator,
                    hint.Parts.PopupRootLocator,
                    hint.Parts.ConfirmButtonLocator,
                    hint.Parts.CancelButtonLocator));
    }

    public bool TryResolveSingleSelectButton(
        Control? source,
        out RecorderSingleSelectHint hint,
        out bool isConfirm)
    {
        hint = null!;
        isConfirm = false;
        if (source is null)
        {
            return false;
        }

        foreach (var current in EnumerateRelatedControls(source))
        {
            foreach (var candidate in _options.SingleSelectHints)
            {
                if (!string.IsNullOrWhiteSpace(candidate.Parts.ConfirmButtonLocator)
                    && MatchesLocator(current, candidate.Parts.LocatorKind, candidate.Parts.ConfirmButtonLocator))
                {
                    hint = candidate;
                    isConfirm = true;
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(candidate.Parts.CancelButtonLocator)
                    && MatchesLocator(current, candidate.Parts.LocatorKind, candidate.Parts.CancelButtonLocator))
                {
                    hint = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    public bool ShouldSuppressSingleSelectButton(Control? source)
    {
        return EnumerateRelatedControls(source).Any(current => _options.SingleSelectHints.Any(hint =>
            !string.IsNullOrWhiteSpace(hint.Parts.OpenButtonLocator)
            && MatchesLocator(current, hint.Parts.LocatorKind, hint.Parts.OpenButtonLocator)));
    }

    public bool ShouldSuppressGridComboSelectionButton(Control? source)
    {
        var related = EnumerateRelatedControls(source).ToArray();
        if (related.OfType<ComboBox>().Any(comboBox => ResolveGridComboSelectionContext(comboBox).IsConfigured)) return true;
        if (source is null || !_gridCapture.IsCatalogGridEditor(source, GridCellEditorKind.ComboBox)
            || !_gridCapture.TryResolveGridHint(source, out var hint, out _)
            || _gridCapture.FindGridDefinition(hint) is not { } definition) return false;
        return definition.Columns.Any(column => column.EditorKind == GridCellEditorKind.ComboBox
            && column.EditorParts?.OpenButton is { } part
            && related.Any(control => MatchesLocator(control, part.LocatorKind, part.LocatorValue)));
    }

    public StepCreationResult TryCreateSearchPickerStep(
        TextBox searchInput,
        ComboBox results,
        string? capturedSearchText = null)
    {
        ArgumentNullException.ThrowIfNull(searchInput);
        ArgumentNullException.ThrowIfNull(results);

        return TryCreateSearchPickerStepCore(
            searchInput,
            results,
            SearchPickerResultsKind.ComboBox,
            ExtractSelectionText(results.SelectedItem),
            capturedSearchText);
    }

    public StepCreationResult TryCreateSearchPickerStep(
        TextBox searchInput,
        ListBox results,
        string? capturedSearchText = null)
    {
        ArgumentNullException.ThrowIfNull(searchInput);
        ArgumentNullException.ThrowIfNull(results);

        return TryCreateSearchPickerStepCore(
            searchInput,
            results,
            SearchPickerResultsKind.ListBox,
            ExtractSelectionText(results.SelectedItem),
            capturedSearchText);
    }

    public SearchPickerSelectionCaptureResult TryCreateSearchPickerStep(
        ComboBox results,
        TextBox? pendingSearchInput,
        string? capturedSearchText)
    {
        ArgumentNullException.ThrowIfNull(results);

        return TryCreateSearchPickerSelectionCapture(
            results,
            SearchPickerResultsKind.ComboBox,
            ExtractSelectionText(results.SelectedItem),
            pendingSearchInput,
            capturedSearchText);
    }

    public SearchPickerSelectionCaptureResult TryCreateSearchPickerStep(
        ListBox results,
        TextBox? pendingSearchInput,
        string? capturedSearchText)
    {
        ArgumentNullException.ThrowIfNull(results);

        return TryCreateSearchPickerSelectionCapture(
            results,
            SearchPickerResultsKind.ListBox,
            ExtractSelectionText(results.SelectedItem),
            pendingSearchInput,
            capturedSearchText);
    }

    public StepCreationResult TryCreateSearchPickerStep(
        TextBox searchInput,
        Control results,
        string selectedText,
        TextBox? pendingSearchInput,
        string? capturedSearchText)
    {
        ArgumentNullException.ThrowIfNull(searchInput);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedText);

        var matchingHints = FindExplicitSearchPickerHints(searchInput, results).ToArray();
        if (matchingHints.Length == 0)
        {
            return StepCreationResult.Unsupported(
                "Controls are not configured as a recorder search picker selection source.");
        }

        if (matchingHints.Length > 1)
        {
            return StepCreationResult.Unsupported(
                $"Search picker selection source matches {matchingHints.Length} configured hints; "
                + "SearchInputLocator and ResultsLocator must identify one picker.");
        }

        var relatedCapturedSearchText = ReferenceEquals(pendingSearchInput, searchInput)
            ? capturedSearchText
            : null;
        return TryCreateConfiguredSearchPickerStep(
            searchInput,
            results,
            selectedText,
            relatedCapturedSearchText,
            matchingHints[0]);
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
                MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.OpenButtonLocator))
            || _options.MultiSelectHints.Any(hint =>
                MatchesAnyLocator(source, hint.Parts.LocatorKind, hint.Parts.OpenButtonLocator)
                || (MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.ItemsContainerLocator)
                    && !MatchesAnyLocator(source, hint.Parts.LocatorKind, hint.Parts.ApplyButtonLocator)
                    && !MatchesAnyLocator(source, hint.Parts.LocatorKind, hint.Parts.CancelButtonLocator)))
            || _options.ComboBoxFilterHints.Any(hint =>
                MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.OpenButtonLocator)
                || (!string.IsNullOrWhiteSpace(hint.Parts.ApplyButtonLocator)
                    && MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.ItemsContainerLocator)
                    && !MatchesAnyLocator(source, hint.Parts.LocatorKind, hint.Parts.ApplyButtonLocator)
                    && !MatchesAnyLocator(source, hint.Parts.LocatorKind, hint.Parts.CancelButtonLocator)))
            || _options.SearchControlHints.Any(hint =>
                MatchesAnyLocator(
                    source,
                    hint.Parts.LocatorKind,
                    hint.Parts.SearchButtonLocator,
                    hint.Parts.HistoryOpenButtonLocator))
            || _options.TimePickerHints.Any(hint =>
                !string.IsNullOrWhiteSpace(hint.Parts.OpenButtonLocator)
                && (RecorderTimePickerBinding.IsPresenterHint(hint)
                    ? TryResolvePresenterTimeBinding(source, out var timeBinding, out _) && Equals(timeBinding.Hint, hint)
                        && timeBinding.IsButton(source, hint.Parts.OpenButtonLocator)
                    : MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.OpenButtonLocator)))
            || _options.DatePickerHints.Any(hint =>
                !string.IsNullOrWhiteSpace(hint.Parts.OpenButtonLocator)
                && MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.OpenButtonLocator));
    }

    public StepCreationResult TryCreateSearchHistoryStep(Control? source)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported("Recorder does not have a SearchControl history hint for this interaction.");
        }

        var matches = FindSearchHistoryHints(source).ToArray();
        if (matches.Length == 0)
        {
            return StepCreationResult.Unsupported("Recorder does not have a SearchControl history hint for this interaction.");
        }

        if (matches.Length > 1)
        {
            return StepCreationResult.Unsupported(
                $"Recorder SearchControl history configuration is ambiguous for this interaction ({matches.Length} hints matched).");
        }

        var hint = matches[0];
        var value = FirstNonWhiteSpace(
            AutomationProperties.GetName(source),
            ExtractTextValue(source),
            source.DataContext?.ToString());
        if (string.IsNullOrWhiteSpace(value))
        {
            return StepCreationResult.Unsupported("Search history item does not expose a non-empty value.");
        }

        var warning = "Recorded SearchControl history selection from configured history results.";
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.Search,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning);
        return CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.ApplySearchFromHistory,
                descriptor,
                StringValue: value.Trim(),
                Warning: warning),
            warning);
    }

    public bool IsSearchHistoryAction(Control? source)
    {
        return source is not null && FindSearchHistoryHints(source).Any();
    }

    public bool IsSearchHistoryPair(TextBox input, Control historySource)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(historySource);

        return _options.SearchControlHints.Any(hint =>
            MatchesLocator(input, hint.Parts.LocatorKind, hint.Parts.SearchInputLocator)
            && MatchesLocator(historySource, hint.Parts.LocatorKind, hint.Parts.HistoryResultsLocator));
    }

    public StepCreationResult TryCreateComboBoxFilterStep(
        Control? source,
        IReadOnlyList<string>? capturedValues = null)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported("Recorder does not have a ComboBox filter hint for this interaction.");
        }

        var matchingActions = FindComboBoxFilterActions(source).ToArray();

        if (matchingActions.Length == 0)
        {
            return StepCreationResult.Unsupported("Recorder does not have a ComboBox filter hint for this interaction.");
        }

        if (matchingActions.Length > 1)
        {
            return StepCreationResult.Unsupported(
                $"Recorder ComboBox filter configuration is ambiguous for this interaction ({matchingActions.Length} actions matched).");
        }

        var (hint, actionKind) = matchingActions[0];
        IReadOnlyList<string> selectedValues;
        if (capturedValues is not null)
        {
            selectedValues = capturedValues.ToArray();
        }
        else if (!TryReadSelectionValues(
                     source,
                     ToMultiSelectParts(hint.Parts),
                     "combo-box filter",
                     out selectedValues,
                     out var message))
        {
            return StepCreationResult.Unsupported(message);
        }

        selectedValues = selectedValues.OrderBy(static value => value, StringComparer.Ordinal).ToArray();

        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.ComboBoxFilter,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning: null);

        return CreateStep(
            source,
            new RecordedStep(
                actionKind,
                descriptor,
                StringValues: selectedValues));
    }

    public bool IsComboBoxFilterAction(Control? source)
    {
        return source is not null
            && FindComboBoxFilterActions(source).Any();
    }

    public bool TryCaptureComboBoxFilterSelection(
        Control? source,
        out IReadOnlyList<string> selectedValues)
    {
        selectedValues = [];
        if (source is null)
        {
            return false;
        }

        var matchingActions = FindComboBoxFilterActions(source).ToArray();
        if (matchingActions.Length != 1)
        {
            return false;
        }

        if (!TryReadSelectionValues(
                source,
                ToMultiSelectParts(matchingActions[0].Hint.Parts),
                "combo-box filter",
                out var currentValues,
                out _))
        {
            return false;
        }

        selectedValues = currentValues
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        return true;
    }

    public StepCreationResult TryCreateMultiSelectStep(Control? source)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported("Recorder does not have a multi-select hint for this button.");
        }

        var matchingActions = FindMultiSelectActions(source).ToArray();

        if (matchingActions.Length == 0)
        {
            return StepCreationResult.Unsupported("Recorder does not have a multi-select hint for this commit button.");
        }

        if (matchingActions.Length > 1)
        {
            return StepCreationResult.Unsupported(
                $"Recorder multi-select configuration is ambiguous for this commit button ({matchingActions.Length} actions matched).");
        }

        var (hint, actionKind) = matchingActions[0];
        if (!TryReadSelectionValues(
                source,
                hint.Parts,
                "multi-select",
                out var selectedValues,
                out var message))
        {
            return StepCreationResult.Unsupported(message);
        }

        var warning = actionKind == RecordedActionKind.SelectMultiItems
            ? "Recorded multi-select Apply action from configured popup parts."
            : "Recorded multi-select Cancel action from configured popup parts.";
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.MultiSelect,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning);

        return CreateStep(
            source,
            new RecordedStep(
                actionKind,
                descriptor,
                Warning: warning,
                StringValues: selectedValues),
            warning);
    }

    public bool IsMultiSelectCommit(Control? source)
    {
        return source is not null
            && FindMultiSelectActions(source).Any();
    }

    public (bool IsConfigured, StepCreationResult Result) TryCreateDialogActionStep(Control? source)
    {
        if (source is null)
        {
            return (
                false,
                StepCreationResult.Unsupported("Recorder does not have a dialog hint for this button."));
        }

        if (!TryResolveDialogHint(
                source,
                out var hint,
                out var actionKind,
                out var buttonLocator,
                out var error,
                out var isConfigured))
        {
            return (isConfigured, StepCreationResult.Unsupported(error));
        }

        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.Dialog,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning: null);

        return (
            true,
            CreateStep(
                source,
                new RecordedStep(actionKind, descriptor, StringValue: buttonLocator)));
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

        if (!_controlReader.TryFindControl(hint.Parts.FolderPathInputLocator, hint.Parts.LocatorKind, out var folderInput)
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

        if (!_gridCapture.TryResolveGridEditHint(source, out var hint))
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
            GridCellEditorKind.Time => TryCreateGridEditTimeStep(source, descriptor, warning, hint),
            GridCellEditorKind.Color => TryCreateGridEditColorStep(source, descriptor, warning, hint),
            GridCellEditorKind.ComboBox => TryCreateGridEditComboStep(source, descriptor, warning, hint),
            GridCellEditorKind.SearchPicker => StepCreationResult.Unsupported(
                "Grid search picker edit is recorded through RecorderGridSearchPickerHint."),
            _ => StepCreationResult.Unsupported($"Unsupported grid edit hint '{hint.EditorKind}'.")
        };
    }

    public bool ShouldSuppressCompositeTextEntry(TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        return IsPresenterTimeSource(textBox)
            || MatchesDateRangeTextPart(textBox)
            || MatchesNumericRangeTextPart(textBox)
            || MatchesFolderExportPathPart(textBox)
            || MatchesTimePickerInputPart(textBox)
            || MatchesDatePickerValuePart(textBox)
            || ShouldSuppressSingleSelectInput(textBox)
            || _options.ComboBoxFilterHints.Any(hint => MatchesLocator(
                textBox,
                hint.Parts.LocatorKind,
                hint.Parts.RootLocator))
            || _options.ColorPickerHints.Any(hint => IsColorPickerInput(textBox, hint))
            || MatchesGridEditValuePart(textBox);
    }

    private bool TryResolveSearchControlHint(TextBox input, out RecorderSearchControlHint hint)
    {
        var matches = _options.SearchControlHints
            .Where(candidate => MatchesLocator(
                input,
                candidate.Parts.LocatorKind,
                candidate.Parts.SearchInputLocator))
            .ToArray();
        if (matches.Length == 1)
        {
            hint = matches[0];
            return true;
        }

        hint = null!;
        return false;
    }

    private IEnumerable<RecorderSearchControlHint> FindSearchHistoryHints(Control source)
    {
        return _options.SearchControlHints.Where(hint =>
            MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.HistoryResultsLocator));
    }

    public bool ShouldRetainPendingTextForCompositeSelection(TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        return MatchesSearchPickerTextPart(textBox)
            || MatchesGridSearchPickerTextPart(textBox)
            || _gridCapture.IsCatalogGridEditor(textBox, GridCellEditorKind.SearchPicker)
            || MatchesTimePickerInputPart(textBox)
            || _options.SingleSelectHints.Any(hint => IsSingleSelectInput(textBox, hint))
            || _options.ColorPickerHints.Any(hint => IsColorPickerInput(textBox, hint));
    }

    public bool IsCompositeSelectionPair(TextBox searchInput, Control results)
    {
        ArgumentNullException.ThrowIfNull(searchInput);
        ArgumentNullException.ThrowIfNull(results);

        return IsSingleSelectPair(searchInput, results)
            || IsCatalogGridSelectionPair(searchInput, results)
            || results switch
            {
                ComboBox => TryResolveSearchPickerHint(searchInput, results, SearchPickerResultsKind.ComboBox, out _)
                    || TryResolveGridSearchPickerHint(searchInput, results, SearchPickerResultsKind.ComboBox, out _),
                ListBox => TryResolveSearchPickerHint(searchInput, results, SearchPickerResultsKind.ListBox, out _)
                    || TryResolveGridSearchPickerHint(searchInput, results, SearchPickerResultsKind.ListBox, out _),
                _ => false
            };
    }

    public bool IsCompositeSelectedValue(TextBox searchInput, Control results, string? text)
    {
        ArgumentNullException.ThrowIfNull(searchInput);
        ArgumentNullException.ThrowIfNull(results);

        if (string.IsNullOrWhiteSpace(text) || !IsCompositeSelectionPair(searchInput, results))
        {
            return false;
        }

        var selectedText = results switch
        {
            ComboBox comboBox => ExtractSelectionText(comboBox.SelectedItem),
            ListBox listBox => ExtractSelectionText(listBox.SelectedItem),
            _ => null
        };

        return string.Equals(selectedText?.Trim(), text.Trim(), StringComparison.Ordinal);
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

        return MatchesGridEditValuePart(control)
            || _options.MultiSelectHints.Any(hint =>
                MatchesLocator(
                    control,
                    hint.Parts.LocatorKind,
                    hint.Parts.ItemsContainerLocator))
            || _options.ComboBoxFilterHints.Any(hint =>
                !string.IsNullOrWhiteSpace(hint.Parts.ApplyButtonLocator)
                && MatchesLocator(
                    control,
                    hint.Parts.LocatorKind,
                    hint.Parts.ItemsContainerLocator));
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

    public GridRowGestureCaptureResult TryCreateCatalogGridRowGestureStep(
        Control? source,
        bool openRow)
    {
        var resolution = _gridCapture.ResolveRowGesture(source);
        if (resolution.Context is not { } context)
            return new GridRowGestureCaptureResult(resolution.Handled,
                StepCreationResult.Unsupported(resolution.Error ?? NoGridActionHintMessage));

        var descriptor = CreateGridDescriptor(context.Definition, context.GridSource);
        var action = openRow ? RecordedActionKind.OpenGridRow : RecordedActionKind.SelectGridRow;
        var step = new RecordedStep(action, descriptor, RowIndex: context.RowIndex);
        var result = CreateCatalogGridStep(
            context.Source,
            step,
            warning: null,
            context.Hint,
            context.Definition,
            context.RowIndex,
            columnIndex: null,
            excludeTargetColumnFromIdentity: false,
            captureItems: context.Items);
        return new GridRowGestureCaptureResult(true, result);
    }

    public StepCreationResult TryCreateListBoxStep(ListBox listBox)
    {
        ArgumentNullException.ThrowIfNull(listBox);

        var selectedText = ExtractSelectionText(listBox.SelectedItem);
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return StepCreationResult.Unsupported("ListBox does not have a selected item to record.");
        }

        var locatorResult = _selectorResolver.ResolvePrimitiveSelection(listBox, UiControlType.ListBox);
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

    public bool ShouldSuppressCompositeTimeSelection(TimePicker timePicker)
    {
        ArgumentNullException.ThrowIfNull(timePicker);
        return MatchesGridEditValuePart(timePicker);
    }

    public StepCreationResult TryCreateSpinnerStep(NumericUpDown spinner)
    {
        ArgumentNullException.ThrowIfNull(spinner);

        if (spinner.Value is not { } value)
        {
            return StepCreationResult.Unsupported("Spinner does not have a numeric value.");
        }

        var locatorResult = _selectorResolver.Resolve(spinner, UiControlType.Spinner);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        return CreateStep(
            spinner,
            new RecordedStep(
                RecordedActionKind.SetSpinnerValue,
                locatorResult.Control,
                DoubleValue: decimal.ToDouble(value),
                Warning: locatorResult.Control.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            locatorResult.Message);
    }

    public StepCreationResult TryCreateExpanderStep(Expander expander)
    {
        ArgumentNullException.ThrowIfNull(expander);

        var locatorResult = _selectorResolver.Resolve(expander, UiControlType.Expander);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        return CreateStep(
            expander,
            new RecordedStep(
                RecordedActionKind.SetExpanded,
                locatorResult.Control,
                BoolValue: expander.IsExpanded,
                Warning: locatorResult.Control.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            locatorResult.Message);
    }

    public StepCreationResult TryCreateTimePickerStep(
        TimePicker timePicker,
        RecorderTimePickerHint? configuredHint = null)
    {
        ArgumentNullException.ThrowIfNull(timePicker);
        if (timePicker.SelectedTime is not { } selectedTime)
        {
            return StepCreationResult.Unsupported("TimePicker does not have a selected time.");
        }

        if (configuredHint is null)
        {
            var matchingHints = FindTimePickerHints(timePicker).ToArray();
            if (matchingHints.Length > 1)
            {
                return StepCreationResult.Unsupported(
                    $"TimePicker matches {matchingHints.Length} recorder hints; configure a unique time surface locator.");
            }

            configuredHint = matchingHints.SingleOrDefault();
        }
        if (configuredHint is not null)
        {
            var descriptor = CreateCompositeDescriptor(
                configuredHint.LocatorValue,
                UiControlType.TimePicker,
                configuredHint.LocatorKind,
                configuredHint.FallbackToName,
                timePicker,
                warning: null);
            return CreateStep(
                timePicker,
                new RecordedStep(
                    RecordedActionKind.SetTime,
                    descriptor,
                    TimeValue: selectedTime),
                "Recorded configured time picker selection.");
        }

        var locatorResult = _selectorResolver.Resolve(timePicker, UiControlType.TimePicker);
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        return CreateStep(
            timePicker,
            new RecordedStep(
                RecordedActionKind.SetTime,
                locatorResult.Control,
                Warning: locatorResult.Control.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist,
                TimeValue: selectedTime),
            locatorResult.Message);
    }

    public bool TryResolveTimePickerHint(TimePicker timePicker, out RecorderTimePickerHint hint)
    {
        var matches = FindTimePickerHints(timePicker).ToArray();
        hint = matches.Length == 1 ? matches[0] : null!;
        return matches.Length == 1;
    }

    public bool IsTimePickerInput(TextBox textBox, RecorderTimePickerHint hint)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(hint);
        if (RecorderTimePickerBinding.IsPresenterHint(hint))
            return TryResolvePresenterTimeBinding(textBox, out var binding, out _) && Equals(binding.Hint, hint)
                && binding.IsButton(textBox, hint.Parts.InputLocator);
        return !string.IsNullOrWhiteSpace(hint.Parts.InputLocator)
            && MatchesLocator(textBox, hint.Parts.LocatorKind, hint.Parts.InputLocator);
    }

    public bool IsTimePickerPart(Control? source, RecorderTimePickerHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        if (RecorderTimePickerBinding.IsPresenterHint(hint))
            return source is not null && TryResolvePresenterTimeBinding(source, out var binding, out _)
                && Equals(binding.Hint, hint);
        return source is not null
            && (MatchesLocator(source, hint.LocatorKind, hint.LocatorValue)
                || MatchesAnyLocator(
                    source,
                    hint.Parts.LocatorKind,
                    hint.Parts.RootLocator,
                    hint.Parts.TimePickerLocator,
                    hint.Parts.InputLocator,
                    hint.Parts.OpenButtonLocator,
                    hint.Parts.PopupRootLocator,
                    hint.Parts.ConfirmButtonLocator,
                    hint.Parts.CancelButtonLocator));
    }

    public bool TryResolveTimePickerButton(
        Control? source,
        out RecorderTimePickerHint hint,
        out bool isConfirm)
    {
        hint = null!;
        isConfirm = false;
        if (source is null)
        {
            return false;
        }

        foreach (var candidate in _options.TimePickerHints)
        {
            if (RecorderTimePickerBinding.IsPresenterHint(candidate)) continue;
            if (!string.IsNullOrWhiteSpace(candidate.Parts.ConfirmButtonLocator)
                && MatchesLocator(source, candidate.Parts.LocatorKind, candidate.Parts.ConfirmButtonLocator))
            {
                hint = candidate;
                isConfirm = true;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(candidate.Parts.CancelButtonLocator)
                && MatchesLocator(source, candidate.Parts.LocatorKind, candidate.Parts.CancelButtonLocator))
            {
                hint = candidate;
                return true;
            }
        }

        return false;
    }

    public StepCreationResult TryCreateDatePickerStep(DatePicker datePicker)
    {
        ArgumentNullException.ThrowIfNull(datePicker);

        if (datePicker.SelectedDate is not { } selectedDate)
        {
            return StepCreationResult.Unsupported("DatePicker does not have a selected date.");
        }

        var configuredResult = TryCreateConfiguredDatePickerStep(datePicker, selectedDate.Date);
        if (configuredResult is not null)
        {
            return configuredResult;
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

        return TryCreateCalendarStep(calendar, selectedDate);
    }

    private bool IsCatalogGridSelectionPair(TextBox input, Control results)
    {
        var resolution = _gridCapture.TryResolveGridComboSelectionContext(input);
        return resolution.Context is { } context
            && IsGridCellContextPart(results, context)
            && _gridCapture.IsCatalogGridEditor(input, GridCellEditorKind.SearchPicker);
    }

    public GridCellEditCaptureResult TryCreateGridCellEditStep(
        Control? source,
        GridCellEditCommitMode commitMode = GridCellEditCommitMode.Commit)
    {
        var resolution = _gridCapture.ResolveCellEdit(source);
        if (resolution.Context is not { } context)
            return new GridCellEditCaptureResult(resolution.Handled, StepCreationResult.Unsupported(resolution.Error!));

        var descriptor = CreateGridDescriptor(context.Definition, context.GridSource);
        var prototype = TryCreateCatalogGridEditPrototype(
            context.Source,
            descriptor,
            context.EditorKind,
            context.Metadata,
            context.LogicalColumnName,
            context.Column?.CultureName,
            commitMode,
            out var prototypeError);
        if (prototype is null)
        {
            return new GridCellEditCaptureResult(true, StepCreationResult.Unsupported(prototypeError));
        }

        var result = CreateGridStep(
            context.Source,
            prototype,
            warning: null,
            context.Definition.RuntimeLocatorValue,
            context.Definition.RuntimeLocatorKind,
            context.Metadata.RowIndex,
            prototype.ColumnIndex,
            excludeTargetColumnFromIdentity: true);
        return new GridCellEditCaptureResult(true, result);
    }

    internal StepCreationResult TryCreateCalendarStep(Calendar calendar, DateTime selectedDate)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        var configuredResult = TryCreateConfiguredDatePickerStep(calendar, selectedDate.Date);
        if (configuredResult is not null)
        {
            return configuredResult;
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

    private StepCreationResult? TryCreateConfiguredDatePickerStep(Control source, DateTime selectedDate)
    {
        var hints = FindDatePickerHints(source).ToArray();
        if (hints.Length == 0)
        {
            return null;
        }

        if (hints.Length > 1)
        {
            return StepCreationResult.Unsupported(
                $"Date selection matches {hints.Length} recorder hints; configure unique date-picker part locators.");
        }

        var hint = hints[0];
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.DateTimePicker,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning: null);
        var logicalValidation = _selectorResolver.ResolveExisting(descriptor);
        if (!logicalValidation.CanPersist)
        {
            return StepCreationResult.Unsupported(
                logicalValidation.ValidationMessage
                ?? $"Date-picker locator '{hint.LocatorKind}:{hint.LocatorValue}' is invalid.");
        }

        return CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.SetDate,
                descriptor,
                DateValue: selectedDate.Date,
                ValidationStatus: logicalValidation.ValidationStatus,
                ValidationMessage: logicalValidation.ValidationMessage,
                CanPersist: true),
            "Recorded configured date-picker selection.");
    }

    public StepCreationResult TryCreateAssertionStep(Control? source, RecorderAssertionMode mode)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported("No control is available for assertion capture.");
        }

        if (mode is RecorderAssertionMode.Auto or RecorderAssertionMode.Text && IsPresenterTimeSource(source))
        {
            if (!TryResolvePresenterTimeBinding(source, out var timeBinding, out var timeError))
                return StepCreationResult.Unsupported(timeError);
            return CreateStep(timeBinding.Root, CreateSemanticValueStep(RecordedActionKind.AssertValue,
                PresenterTimeCandidate(timeBinding), comparisonKind: RecorderComparisonKind.Equal, hasExpectedLiteral: true),
                "Added exact committed presenter time assertion.");
        }

        if (TryCreateGridAssertionStep(source, mode, out var gridResult))
        {
            return gridResult;
        }

        if (TryCreateNotificationAssertionStep(source, mode, out var notificationResult))
        {
            return notificationResult;
        }

        if (TryCreateColorPickerAssertionStep(source, mode, out var colorResult))
        {
            return colorResult;
        }

        var projectedSemanticResult = TryCreateProjectedSemanticAssertionStep(source, mode);
        if (projectedSemanticResult is not null)
        {
            return projectedSemanticResult;
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
                    IntValue: candidate.IntValue,
                    TimeValue: candidate.TimeValue),
                locatorResult.Message);
        }

        return StepCreationResult.Unsupported("Recorder could not derive a supported assertion for this control.");
    }

    internal StepCreationResult TryCreateAssertionStep(
        RecorderSemanticValueSnapshot? snapshot,
        RecorderAssertionMode mode)
    {
        if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Text))
        {
            return StepCreationResult.Unsupported(
                $"Semantic value snapshots do not support the {mode} assertion mode.");
        }

        if (snapshot is null)
        {
            return StepCreationResult.Unsupported(
                "The selected control does not expose a semantic value snapshot.");
        }

        var candidate = CreateCandidate(snapshot);
        if (!HasLiteral(candidate))
        {
            return StepCreationResult.Unsupported(
                $"{candidate.Control.ControlType} does not expose a committed value for a literal assertion.");
        }

        if (candidate.Control.ControlType == UiControlType.Notification)
        {
            if (string.IsNullOrWhiteSpace(candidate.StringValue))
            {
                return StepCreationResult.Unsupported("Notification text part does not expose text.");
            }

            return CreateStepFromSnapshot(
                snapshot,
                new RecordedStep(
                    RecordedActionKind.WaitUntilNotificationContains,
                    candidate.Control,
                    StringValue: candidate.StringValue.Trim()),
                "Added current notification text assertion.");
        }

        var step = CreateSemanticValueStep(
            RecordedActionKind.AssertValue,
            candidate,
            comparisonKind: candidate.Control.ControlType == UiControlType.ComboBoxFilter
                && candidate.ValueKind == RecorderValueKind.StringSet
                ? RecorderComparisonKind.Equivalent
                : RecorderComparisonKind.Equal,
            hasExpectedLiteral: true);
        return CreateStepFromSnapshot(snapshot, step, "Added current semantic value assertion.");
    }

    private StepCreationResult? TryCreateProjectedSemanticAssertionStep(
        Control source,
        RecorderAssertionMode mode)
    {
        if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Text))
        {
            return null;
        }

        if (!TryResolveSemanticValue(
                source,
                requireLiteral: false,
                out var candidate,
                out var error,
                out var isDefinitiveFailure))
        {
            return isDefinitiveFailure
                ? StepCreationResult.Unsupported(error)
                : null;
        }

        // Date text parts must use the committed SelectedDate surface. Other composite
        // controls retain their existing WaitUntil assertion generation contract.
        if (candidate.Control.ControlType != UiControlType.DateTimePicker)
        {
            return null;
        }

        var sourceControlType = ClassifyControlType(source);
        var directLocator = _selectorResolver.Resolve(source, sourceControlType);
        if (directLocator.Success
            && directLocator.Control is { } directControl
            && directControl.ControlType == sourceControlType
            && RefersToSameControl(directControl, candidate.Control))
        {
            return null;
        }

        if (!HasLiteral(candidate))
        {
            return StepCreationResult.Unsupported(
                $"{candidate.Control.ControlType} does not expose a committed value for a literal assertion.");
        }

        return CreateStep(
            source,
            CreateSemanticValueStep(
                RecordedActionKind.AssertValue,
                candidate,
                comparisonKind: RecorderComparisonKind.Equal,
                hasExpectedLiteral: true),
            "Added current semantic value assertion.");
    }

    private static bool RefersToSameControl(
        RecordedControlDescriptor left,
        RecordedControlDescriptor right)
    {
        return left.ControlType == right.ControlType
            && left.LocatorKind == right.LocatorKind
            && string.Equals(left.LocatorValue, right.LocatorValue, StringComparison.Ordinal);
    }

    internal bool TryDescribeSemanticValue(
        Control? source,
        out RecorderSemanticValueDescription? description,
        out string? error)
    {
        description = null;
        if (!TryResolveSemanticValue(source, requireLiteral: false, out var candidate, out error))
        {
            return false;
        }

        description = new RecorderSemanticValueDescription(
            candidate.ValueKind,
            $"{candidate.Control.ProposedPropertyName}Checkpoint",
            FormatLiteralText(candidate));
        return true;
    }

    internal bool TryCaptureSemanticValueSnapshot(
        Control? source,
        out RecorderSemanticValueSnapshot? snapshot,
        out string? error)
    {
        return TryCaptureSemanticValueSnapshot(source, out snapshot, out error, out _);
    }

    internal bool TryCaptureSemanticValueSnapshot(
        Control? source,
        out RecorderSemanticValueSnapshot? snapshot,
        out string? error,
        out bool isDefinitiveFailure)
    {
        snapshot = null;
        if (!TryResolveSemanticValue(
                source,
                requireLiteral: false,
                out var candidate,
                out error,
                out isDefinitiveFailure))
        {
            isDefinitiveFailure |= source is not null && !string.IsNullOrWhiteSpace(error)
                && _gridCapture.TryResolveGridHint(source, out _, out var failedGrid) && !ReferenceEquals(source, failedGrid);
            return false;
        }

        var captured = TryCreateSemanticValueSnapshot(source!, candidate, out snapshot, out error);
        if (!captured && (candidate.RepeatedItemKey is not null || candidate.GridContext is not null))
        {
            isDefinitiveFailure = true;
        }

        return captured;
    }

    internal bool TryCaptureConfiguredSemanticValueSnapshot(
        IReadOnlyList<Control> sources,
        out Control? resolvedSource,
        out RecorderSemanticValueSnapshot? snapshot,
        out string? error)
    {
        return TryCaptureConfiguredSemanticValueSnapshot(
            sources,
            out resolvedSource,
            out snapshot,
            out error,
            out _);
    }

    internal bool TryCaptureConfiguredSemanticValueSnapshot(
        IReadOnlyList<Control> sources,
        out Control? resolvedSource,
        out RecorderSemanticValueSnapshot? snapshot,
        out string? error,
        out bool isDefinitiveFailure)
    {
        ArgumentNullException.ThrowIfNull(sources);
        resolvedSource = null;
        snapshot = null;
        error = string.Empty;
        isDefinitiveFailure = false;
        var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var candidates = sources
            .Where(static source => source is not null)
            .Where(source => visited.Add(source))
            .ToArray();
        var resolvedCandidates = new List<(Control Source, RecorderSemanticValueSnapshot Snapshot)>();
        Control? firstFailedSource = null;
        string? firstError = null;
        Control? definitiveFailedSource = null;
        string? definitiveError = null;

        foreach (var source in candidates)
        {
            if (TryResolveSemanticValue(
                    source,
                    requireLiteral: false,
                    out var candidate,
                    out var resolverError,
                    out var resolverDefinitiveFailure))
            {
                if (TryCreateSemanticValueSnapshot(source, candidate, out var candidateSnapshot, out var snapshotError))
                {
                    resolvedCandidates.Add((source, candidateSnapshot!));
                    continue;
                }

                firstFailedSource ??= source;
                firstError ??= snapshotError;
                if (candidate.RepeatedItemKey is not null || candidate.GridContext is not null)
                {
                    definitiveFailedSource ??= source;
                    definitiveError ??= snapshotError;
                }
                continue;
            }

            if (!string.IsNullOrWhiteSpace(resolverError))
            {
                firstFailedSource ??= source;
                firstError ??= resolverError;
                if (resolverDefinitiveFailure)
                {
                    definitiveFailedSource ??= source;
                    definitiveError ??= resolverError;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(definitiveError))
        {
            resolvedSource = definitiveFailedSource;
            error = definitiveError;
            isDefinitiveFailure = true;
            return false;
        }

        if (resolvedCandidates.Count == 0)
        {
            resolvedSource = firstFailedSource;
            error = firstError ?? string.Empty;
            isDefinitiveFailure = firstFailedSource is not null
                && _gridCapture.TryResolveGridHint(firstFailedSource, out _, out var failedGrid)
                && !ReferenceEquals(firstFailedSource, failedGrid);
            return false;
        }

        var firstResolved = resolvedCandidates[0];
        var conflicting = resolvedCandidates
            .Skip(1)
            .FirstOrDefault(candidate => !RefersToSameSemanticTarget(
                firstResolved.Snapshot,
                candidate.Snapshot));
        if (conflicting != default)
        {
            error = "Candidate graph resolved multiple logical targets: "
                + $"'{DescribeSemanticTarget(firstResolved.Snapshot)}' and "
                + $"'{DescribeSemanticTarget(conflicting.Snapshot)}'.";
            isDefinitiveFailure = true;
            return false;
        }

        resolvedSource = firstResolved.Source;
        snapshot = firstResolved.Snapshot;
        error = string.Empty;
        return true;
    }


    private bool TryCreateSemanticValueSnapshot(
        Control source,
        SemanticValueCandidate candidate,
        out RecorderSemanticValueSnapshot? snapshot,
        out string? error)
    {
        snapshot = null;
        var readsPublishedDisplay = candidate.GridContext?.TargetColumnName is { } columnName
            && _options.GridAutomation.Any(definition => definition.RuntimeLocatorKind == candidate.Control.LocatorKind
                && string.Equals(definition.RuntimeLocatorValue, candidate.Control.LocatorValue, StringComparison.Ordinal)
                && definition.FindColumn(columnName)?.ReadsDisplayedText == true);
        if (candidate.GridContext is not null && !readsPublishedDisplay
            && ExtractTextValue(source) is { } displayedText)
            candidate = candidate with { DisplayText = displayedText };
        var prototypeResult = TryCreateSemanticSnapshotPrototype(source!, candidate);
        if (!prototypeResult.Success || prototypeResult.Step is null)
        {
            error = prototypeResult.Message;
            return false;
        }

        var description = new RecorderSemanticValueDescription(
            candidate.ValueKind,
            $"{candidate.Control.ProposedPropertyName}Checkpoint",
            FormatLiteralText(candidate));
        snapshot = new RecorderSemanticValueSnapshot(prototypeResult.Step, description);
        error = string.Empty;
        return true;
    }

    internal StepCreationResult TryCreateCheckpointStep(Control? source, string? variableName = null)
    {
        if (!TryResolveSemanticValue(source, requireLiteral: false, out var candidate, out var error))
        {
            return StepCreationResult.Unsupported(error);
        }

        var step = CreateSemanticValueStep(
            RecordedActionKind.CaptureCheckpoint,
            candidate,
            checkpointId: Guid.NewGuid(),
            checkpointVariableName: string.IsNullOrWhiteSpace(variableName)
                ? $"{candidate.Control.ProposedPropertyName}Checkpoint"
                : variableName.Trim());
        var result = candidate.GridContext is { } grid
            ? CreateGridStep(
                source!,
                step,
                warning: string.Empty,
                candidate.Control.LocatorValue,
                candidate.Control.LocatorKind,
                grid.RowIndex,
                grid.ColumnIndex,
                excludeTargetColumnFromIdentity: false)
            : CreateStep(source!, step, "Remembered semantic value for replay-time checkpoint.");
        return PreferCurrentTableRow(result);
    }

    internal StepCreationResult TryCreateCheckpointStep(
        RecorderSemanticValueSnapshot? snapshot,
        string? variableName = null)
    {
        if (snapshot is null)
        {
            return StepCreationResult.Unsupported("The selected control does not expose a semantic value snapshot.");
        }

        var candidate = CreateCandidate(snapshot);
        var step = CreateSemanticValueStep(
            RecordedActionKind.CaptureCheckpoint,
            candidate,
            checkpointId: Guid.NewGuid(),
            checkpointVariableName: string.IsNullOrWhiteSpace(variableName)
                ? snapshot.Description.SuggestedCheckpointName
                : variableName.Trim());
        return PreferCurrentTableRow(CreateStepFromSnapshot(
            snapshot,
            step,
            "Remembered semantic value for replay-time checkpoint."));
    }

    private static StepCreationResult PreferCurrentTableRow(StepCreationResult result)
    {
        return result.Success && result.Step is { GridCapturedRowPosition: >= 0 } checkpoint
            ? result with
            {
                Step = checkpoint with { GridRowSourceMode = RecorderGridRowSourceMode.CurrentTableRow }
            }
            : result;
    }

    internal StepCreationResult TryCreateCopiedValueStep(
        RecorderSemanticValueSnapshot? snapshot,
        RecorderCopiedValueOption copiedValue)
    {
        ArgumentNullException.ThrowIfNull(copiedValue);
        if (snapshot is null)
        {
            return StepCreationResult.Unsupported("The selected control does not expose a semantic value snapshot.");
        }

        if (!TryCreateTextCopySnapshot(snapshot, out snapshot))
        {
            return StepCreationResult.Unsupported("The selected value does not expose displayed text for copying.");
        }

        var candidate = CreateCandidate(snapshot);
        var step = CreateSemanticValueStep(RecordedActionKind.CaptureCopiedValue, candidate) with
        {
            CopiedValueId = copiedValue.CopiedValueId,
            CopiedValueVariableName = copiedValue.VariableName
        };
        return CreateStepFromSnapshot(snapshot, step, "Captured text for replay-time clipboard copy.");
    }

    internal static bool TryCreateTextCopySnapshot(RecorderSemanticValueSnapshot source,
        out RecorderSemanticValueSnapshot snapshot)
    {
        snapshot = source;
        if (source.Description.ValueKind is RecorderValueKind.Text or RecorderValueKind.GridCellText) return true;
        var step = source.Prototype;
        if (step.ValueAccessorKind != RecorderValueAccessorKind.GridCellValue || step.CapturedDisplayText is null) return false;
        snapshot = new(step with
        {
            ValueKind = RecorderValueKind.GridCellText,
            ValueAccessorKind = RecorderValueAccessorKind.GridCellText,
            StringValue = step.CapturedDisplayText,
            DoubleValue = null,
            BoolValue = null,
            DateValue = null,
            TimeValue = null,
            NumericType = null
        }, source.Description with { ValueKind = RecorderValueKind.GridCellText, CurrentValueText = step.CapturedDisplayText });
        return true;
    }

    internal StepCreationResult TryCreateCheckpointAssertionStep(
        Control? source,
        RecorderCheckpointOption checkpoint,
        RecorderComparisonKind comparisonKind = RecorderComparisonKind.Equal)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!TryResolveSemanticValue(source, requireLiteral: false, out var candidate, out var error))
        {
            return StepCreationResult.Unsupported(error);
        }

        if (candidate.ValueKind != checkpoint.ValueKind)
        {
            return StepCreationResult.Unsupported(
                $"Selected value is {candidate.ValueKind}, but checkpoint '{checkpoint.VariableName}' is {checkpoint.ValueKind}.");
        }

        if (!TryNormalizeCheckpointComparison(
                candidate.ValueKind,
                comparisonKind,
                out var comparison,
                out error))
        {
            return StepCreationResult.Unsupported(error);
        }
        var step = CreateSemanticValueStep(
            RecordedActionKind.AssertValue,
            candidate,
            comparisonKind: comparison,
            expectedCheckpointId: checkpoint.CheckpointId);
        return candidate.GridContext is { } grid
            ? CreateGridStep(
                source!,
                step,
                warning: string.Empty,
                candidate.Control.LocatorValue,
                candidate.Control.LocatorKind,
                grid.RowIndex,
                grid.ColumnIndex,
                excludeTargetColumnFromIdentity: false)
            : CreateStep(source!, step, $"Added assertion against checkpoint '{checkpoint.VariableName}'.");
    }

    internal StepCreationResult TryCreateCheckpointAssertionStep(
        RecorderSemanticValueSnapshot? snapshot,
        RecorderCheckpointOption checkpoint,
        RecorderComparisonKind comparisonKind = RecorderComparisonKind.Equal)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (snapshot is null)
        {
            return StepCreationResult.Unsupported("The selected control does not expose a semantic value snapshot.");
        }

        var candidate = CreateCandidate(snapshot);
        if (candidate.ValueKind != checkpoint.ValueKind)
        {
            return StepCreationResult.Unsupported(
                $"Selected value is {candidate.ValueKind}, but checkpoint '{checkpoint.VariableName}' is {checkpoint.ValueKind}.");
        }

        if (!TryNormalizeCheckpointComparison(
                candidate.ValueKind,
                comparisonKind,
                out var comparison,
                out var error))
        {
            return StepCreationResult.Unsupported(error);
        }
        var step = CreateSemanticValueStep(
            RecordedActionKind.AssertValue,
            candidate,
            comparisonKind: comparison,
            expectedCheckpointId: checkpoint.CheckpointId);
        return CreateStepFromSnapshot(snapshot, step, $"Added assertion against checkpoint '{checkpoint.VariableName}'.");
    }

    internal StepCreationResult TryCreateGeneratedValueAssertionStep(
        RecorderSemanticValueSnapshot? snapshot,
        RecorderGeneratedValueOption generatedValue,
        RecorderComparisonKind comparisonKind = RecorderComparisonKind.Equal)
    {
        ArgumentNullException.ThrowIfNull(generatedValue);
        if (snapshot is null)
        {
            return StepCreationResult.Unsupported("The selected control does not expose a semantic value snapshot.");
        }

        var candidate = CreateCandidate(snapshot);
        if (candidate.ValueKind is not RecorderValueKind.Text and not RecorderValueKind.GridCellText)
        {
            return StepCreationResult.Unsupported(
                $"Selected value is {candidate.ValueKind}, but generated value '{generatedValue.VariableName}' is text.");
        }

        if (comparisonKind is not RecorderComparisonKind.Equal and not RecorderComparisonKind.NotEqual)
        {
            return StepCreationResult.Unsupported(
                $"Generated text values do not support the {comparisonKind} comparison.");
        }

        var step = CreateSemanticValueStep(
            RecordedActionKind.AssertValue,
            candidate,
            comparisonKind: comparisonKind,
            expectedGeneratedValueId: generatedValue.GeneratedValueId);
        return CreateStepFromSnapshot(
            snapshot,
            step,
            $"Added assertion against generated value '{generatedValue.VariableName}'.");
    }

    internal static bool TryCreateNumericControlOperand(
        RecorderSemanticValueSnapshot? snapshot,
        out RecorderNumericOperand? operand,
        out string error)
    {
        operand = null;
        if (snapshot is null)
        {
            error = "The selected operand does not expose a readable semantic value.";
            return false;
        }

        if (RecorderNumericValueContract.TryCreateNumericTextSnapshot(snapshot, out var numericSnapshot))
        {
            snapshot = numericSnapshot;
        }

        var candidate = CreateCandidate(snapshot);
        if (!TryCreateNumericValueReference(candidate, out var gridValueReference, out error))
        {
            return false;
        }

        operand = RecorderNumericOperand.FromControl(
            candidate.Control,
            candidate.ValueAccessorKind,
            gridValueReference,
            candidate.RepeatedItemKey);
        error = string.Empty;
        return true;
    }

    internal StepCreationResult TryCreateCalculatedAssertionStep(
        RecorderSemanticValueSnapshot? snapshot,
        RecorderNumericExpectedExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (snapshot is null)
        {
            return StepCreationResult.Unsupported(
                "The selected control does not expose a semantic value snapshot.");
        }

        if (RecorderNumericValueContract.TryCreateNumericTextSnapshot(snapshot, out var numericSnapshot))
        {
            snapshot = numericSnapshot;
        }

        var candidate = CreateCandidate(snapshot);
        if (!TryCreateNumericValueReference(candidate, out _, out var error))
        {
            return StepCreationResult.Unsupported(error);
        }

        var step = CreateSemanticValueStep(
            RecordedActionKind.AssertValue,
            candidate,
            comparisonKind: RecorderComparisonKind.Equal,
            numericExpectedExpression: expression);
        return CreateStepFromSnapshot(snapshot, step, "Added calculated numeric assertion.");
    }

    private static bool TryCreateNumericValueReference(
        SemanticValueCandidate candidate,
        out RecorderGridValueReference? gridValueReference,
        out string error)
    {
        return RecorderNumericValueContract.TryCreateGridReference(
            candidate.Control,
            candidate.ValueKind,
            candidate.ValueAccessorKind,
            candidate.GridContext?.RowConditions,
            candidate.GridContext?.TargetColumnName,
            out gridValueReference,
            out error,
            candidate.RepeatedItemKey);
    }

    private static bool TryNormalizeCheckpointComparison(
        RecorderValueKind valueKind,
        RecorderComparisonKind requested,
        out RecorderComparisonKind normalized,
        out string error)
    {
        if (valueKind == RecorderValueKind.StringSet)
        {
            normalized = RecorderComparisonKind.Equivalent;
            error = requested is RecorderComparisonKind.Equal or RecorderComparisonKind.Equivalent
                ? string.Empty
                : "String-set checkpoints support equivalent comparison only.";
            return string.IsNullOrEmpty(error);
        }

        normalized = requested;
        error = requested is RecorderComparisonKind.Equal or RecorderComparisonKind.NotEqual
            ? string.Empty
            : $"Checkpoint comparison '{requested}' is not supported for {valueKind} values.";
        return string.IsNullOrEmpty(error);
    }

    internal StepCreationResult TryCreateLiteralAssertionStep(
        Control? source,
        string expectedText,
        RecorderComparisonKind comparisonKind = RecorderComparisonKind.Equal)
    {
        if (!TryResolveSemanticValue(source, requireLiteral: false, out var candidate, out var error))
        {
            return StepCreationResult.Unsupported(error);
        }

        if (!TryApplyLiteralText(candidate, expectedText, out candidate, out error))
        {
            return StepCreationResult.Unsupported(error);
        }

        if (candidate.ValueKind == RecorderValueKind.StringSet
            && comparisonKind == RecorderComparisonKind.Equal)
        {
            comparisonKind = RecorderComparisonKind.Equivalent;
        }

        var step = CreateSemanticValueStep(
            RecordedActionKind.AssertValue,
            candidate,
            comparisonKind: comparisonKind,
            hasExpectedLiteral: true);
        return candidate.GridContext is { } grid
            ? CreateGridStep(
                source!,
                step,
                warning: string.Empty,
                candidate.Control.LocatorValue,
                candidate.Control.LocatorKind,
                grid.RowIndex,
                grid.ColumnIndex,
                excludeTargetColumnFromIdentity: false)
            : CreateStep(source!, step, "Added literal value assertion.");
    }

    internal StepCreationResult TryCreateLiteralAssertionStep(
        RecorderSemanticValueSnapshot? snapshot,
        string expectedText,
        RecorderComparisonKind comparisonKind = RecorderComparisonKind.Equal,
        RecorderDateExpression? dateExpression = null)
    {
        if (snapshot is null)
        {
            return StepCreationResult.Unsupported("The selected control does not expose a semantic value snapshot.");
        }

        var candidate = CreateCandidate(snapshot);
        if (!TryApplyLiteralText(candidate, expectedText, out candidate, out var error))
        {
            return StepCreationResult.Unsupported(error);
        }

        if (candidate.ValueKind == RecorderValueKind.StringSet
            && comparisonKind == RecorderComparisonKind.Equal)
        {
            comparisonKind = RecorderComparisonKind.Equivalent;
        }

        if (!TryNormalizeLiteralDateExpression(
                candidate,
                dateExpression,
                out dateExpression,
                out error))
        {
            return StepCreationResult.Unsupported(error);
        }

        var step = CreateSemanticValueStep(
            RecordedActionKind.AssertValue,
            candidate,
            comparisonKind: comparisonKind,
            hasExpectedLiteral: true,
            dateExpression: dateExpression);
        return CreateStepFromSnapshot(snapshot, step, "Added literal value assertion.");
    }

    internal StepCreationResult TryCreateHasValueAssertionStep(
        RecorderSemanticValueSnapshot? snapshot)
    {
        return TryCreatePresenceAssertionStep(snapshot, expectEmpty: false);
    }

    internal StepCreationResult TryCreatePresenceAssertionStep(
        RecorderSemanticValueSnapshot? snapshot,
        bool expectEmpty)
    {
        if (snapshot is null)
        {
            return StepCreationResult.Unsupported("The selected control does not expose a semantic value snapshot.");
        }

        var candidate = CreateCandidate(snapshot);
        if (!RecorderValueAssertions.TryGetHasValueAssertionKind(candidate.ValueKind, out _))
        {
            return StepCreationResult.Unsupported(
                $"A has-value assertion is not meaningful for {candidate.ValueKind} values.");
        }

        var step = CreateSemanticValueStep(
            RecordedActionKind.AssertValue,
            candidate,
            comparisonKind: expectEmpty
                ? RecorderComparisonKind.IsEmpty
                : RecorderComparisonKind.HasValue);
        return CreateStepFromSnapshot(
            snapshot,
            step,
            expectEmpty ? "Added empty-value assertion." : "Added has-value assertion.");
    }

    internal StepCreationResult TryCreateEnabledAssertionStep(
        Control? source,
        RecorderSemanticValueSnapshot? valueSnapshot,
        bool expectedEnabled)
    {
        return TryCreateControlStateAssertionStep(
            source,
            valueSnapshot,
            expectedEnabled,
            RecorderValueAccessorKind.IsEnabled,
            "enabled");
    }

    internal StepCreationResult TryCreateReadOnlyAssertionStep(
        Control? source,
        RecorderSemanticValueSnapshot? valueSnapshot)
    {
        var controlType = valueSnapshot?.Prototype.Control.ControlType
            ?? (source is null ? UiControlType.AutomationElement : ClassifyControlType(source));
        if (controlType is not (UiControlType.TextBox or UiControlType.DateTimePicker))
        {
            return StepCreationResult.Unsupported(
                "Read-only assertions require a text input or date editor with a readable editability state.");
        }

        if (source is DatePicker)
        {
            return StepCreationResult.Unsupported(
                "Avalonia DatePicker does not expose a readable read-only state. "
                + "Register a date editor with a readable value part before recording this assertion.");
        }

        return TryCreateControlStateAssertionStep(
            source,
            valueSnapshot,
            expectedState: true,
            RecorderValueAccessorKind.IsReadOnly,
            "read-only");
    }

    private StepCreationResult TryCreateControlStateAssertionStep(
        Control? source,
        RecorderSemanticValueSnapshot? valueSnapshot,
        bool expectedState,
        RecorderValueAccessorKind accessorKind,
        string stateName)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported($"No control is available for {stateName}-state assertion capture.");
        }

        if (valueSnapshot is not null)
        {
            var step = new RecordedStep(
                RecordedActionKind.AssertValue,
                valueSnapshot.Prototype.Control,
                BoolValue: expectedState,
                ValueKind: RecorderValueKind.Boolean,
                ValueAccessorKind: accessorKind,
                ComparisonKind: RecorderComparisonKind.Equal,
                HasExpectedLiteral: true);
            return CreateStepFromSnapshot(valueSnapshot, step, $"Added {stateName}-state assertion.");
        }

        var locatorResult = _selectorResolver.Resolve(source, ClassifyControlType(source));
        if (!locatorResult.Success || locatorResult.Control is null)
        {
            return StepCreationResult.Unsupported(locatorResult.Message);
        }

        return CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.AssertValue,
                locatorResult.Control,
                BoolValue: expectedState,
                ValueKind: RecorderValueKind.Boolean,
                ValueAccessorKind: accessorKind,
                ComparisonKind: RecorderComparisonKind.Equal,
                HasExpectedLiteral: true,
                Warning: locatorResult.Control.Warning,
                ValidationStatus: locatorResult.ValidationStatus,
                ValidationMessage: locatorResult.ValidationMessage,
                CanPersist: locatorResult.CanPersist),
            $"Added {stateName}-state assertion.");
    }


    private StepCreationResult TryCreateSemanticSnapshotPrototype(
        Control source,
        SemanticValueCandidate candidate)
    {
        var step = CreateSemanticValueStep(RecordedActionKind.CaptureCheckpoint, candidate);
        StepCreationResult result;
        if (candidate.GridContext is not { } grid)
        {
            result = CreateStep(source, step, "Captured semantic value target.");
        }
        else if (grid.RowConditions is { Count: > 0 }
            && !string.IsNullOrWhiteSpace(grid.TargetColumnName))
        {
            result = CreateStep(
                source,
                step with
                {
                    GridRowConditions = grid.RowConditions,
                    GridTargetColumnName = grid.TargetColumnName,
                    CanCaptureGridRowKey = _options.GridAutomation.Any(definition =>
                        definition.RuntimeLocatorKind == candidate.Control.LocatorKind
                        && string.Equals(definition.RuntimeLocatorValue, candidate.Control.LocatorValue, StringComparison.Ordinal))
                },
                "Captured stable grid value target.");
        }
        else if (grid.RowIndex >= 0
            && (grid.ColumnIndex >= 0 || !string.IsNullOrWhiteSpace(grid.TargetColumnName)))
        {
            result = CreateGridStep(
                source,
                step,
                warning: string.Empty,
                candidate.Control.LocatorValue,
                candidate.Control.LocatorKind,
                grid.RowIndex,
                grid.ColumnIndex,
                excludeTargetColumnFromIdentity: false);
        }
        else
        {
            return StepCreationResult.Unsupported(
                "Grid value resolver must provide a stable row selector and target column.");
        }

        if (!result.Success || result.Step is null)
        {
            return result;
        }

        if (candidate.GridContext is not null
            && (result.Step.GridRowConditions is not { Count: > 0 }
                || string.IsNullOrWhiteSpace(result.Step.GridTargetColumnName)))
        {
            return StepCreationResult.Unsupported(
                "Configure RowIdentityColumnPropertyNames before using Check for a grid value that must survive sorting, insertion, or editor replacement.");
        }

        // A notification can share its root locator with other visible messages.
        // Validate the selected text within a matching notification instead of
        // validating the temporary checkpoint as a globally unique root.
        var validationStep = candidate.Control.ControlType == UiControlType.Notification
            ? result.Step with { ActionKind = RecordedActionKind.WaitUntilNotificationContains }
            : result.Step;
        var selectorValidation = _selectorResolver.ResolveExisting(validationStep, source);
        if (!selectorValidation.CanPersist)
        {
            return StepCreationResult.Unsupported(
                selectorValidation.ValidationMessage
                ?? "The logical semantic value target could not be validated.");
        }

        return StepCreationResult.Created(
            result.Step with
            {
                ValidationStatus = selectorValidation.ValidationStatus,
                ValidationMessage = selectorValidation.ValidationMessage,
                CanPersist = true
            },
            result.Message);
    }


    private bool TryResolveSemanticValue(
        Control? source,
        bool requireLiteral,
        out SemanticValueCandidate candidate,
        out string error)
    {
        return TryResolveSemanticValue(
            source,
            requireLiteral,
            out candidate,
            out error,
            out _);
    }

    private bool TryResolveSemanticValue(
        Control? source,
        bool requireLiteral,
        out SemanticValueCandidate candidate,
        out string error,
        out bool isDefinitiveFailure)
    {
        candidate = null!;
        isDefinitiveFailure = false;
        if (source is null)
        {
            error = "No control is available for value capture.";
            return false;
        }

        if (IsPresenterTimeSource(source))
        {
            isDefinitiveFailure = true;
            if (!TryResolvePresenterTimeBinding(source, out var binding, out error)) return false;
            candidate = PresenterTimeCandidate(binding);
            error = string.Empty;
            return true;
        }

        if (TryResolveMultiItemSpinnerSemanticValue(
                source,
                out candidate,
                out error,
                out var multiItemSpinnerConfigured))
        {
            return true;
        }

        if (multiItemSpinnerConfigured)
        {
            isDefinitiveFailure = true;
            return false;
        }

        if (_gridCapture.TryResolveGridHeaderSemanticValue(source, out candidate, out error, out var headerHandled)) return true;
        if (headerHandled) { isDefinitiveFailure = true; return false; }

        if (TryResolveConfiguredSemanticValue(
                source,
                requireLiteral,
                out candidate,
                out error,
                out var configuredResolverHandled))
        {
            return true;
        }

        if (configuredResolverHandled)
        {
            isDefinitiveFailure = true;
            return false;
        }

        if (_gridCapture.TryResolveGridSearchPickerSemanticValue(source, requireLiteral, out candidate, out error))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(error))
        {
            return false;
        }

        if (_gridCapture.TryResolveGridEditorSemanticValue(source, requireLiteral, out candidate, out error))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(error))
        {
            return false;
        }

        if (_gridCapture.TryResolveGridSemanticValue(source, requireLiteral, out candidate, out error))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(error))
        {
            return false;
        }

        if (TryResolveNotificationSemanticValue(source, requireLiteral, out candidate, out error))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(error))
        {
            isDefinitiveFailure = true;
            return false;
        }

        if (TryResolveDatePickerSemanticValue(source, requireLiteral, out candidate, out error))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(error))
        {
            return false;
        }

        var filterHints = _options.ComboBoxFilterHints
            .Where(hint => IsMultiSelectPart(source, hint.LocatorValue, hint.LocatorKind, ToMultiSelectParts(hint.Parts)))
            .ToArray();
        if (filterHints.Length > 0)
        {
            return TryCreateComboBoxFilterSemanticValue(
                source, filterHints, requireLiteral, out candidate, out error);
        }

        var multiSelectHints = _options.MultiSelectHints
            .Where(hint => IsMultiSelectPart(source, hint.LocatorValue, hint.LocatorKind, hint.Parts))
            .ToArray();
        if (multiSelectHints.Length > 0)
        {
            return TryCreateMultiSelectSemanticValue(
                source,
                multiSelectHints.Select(hint => (
                    hint.LocatorValue,
                    hint.LocatorKind,
                    hint.FallbackToName,
                    hint.Parts,
                    UiControlType.MultiSelect)).ToArray(),
                requireLiteral,
                out candidate,
                out error);
        }

        var searchControlHints = _options.SearchControlHints
            .Where(hint => IsSearchControlPart(source, hint))
            .ToArray();
        if (searchControlHints.Length > 0)
        {
            if (searchControlHints.Length != 1)
            {
                error = $"Value source matches {searchControlHints.Length} SearchControl hints; configure unique part locators.";
                return false;
            }

            var hint = searchControlHints[0];
            var text = _controlReader.TryFindControl(hint.Parts.SearchInputLocator, hint.Parts.LocatorKind, out var input)
                ? ExtractTextValue(input)
                : null;
            if (requireLiteral && text is null)
            {
                error = "SearchControl does not expose its current search text.";
                return false;
            }

            candidate = new SemanticValueCandidate(
                CreateCompositeDescriptor(hint.LocatorValue, UiControlType.Search, hint.LocatorKind, hint.FallbackToName, source, null),
                RecorderValueKind.Text,
                RecorderValueAccessorKind.Text,
                StringValue: text);
            error = string.Empty;
            return true;
        }

        var searchPickerHints = _options.SearchPickerHints
            .Where(hint => IsSearchPickerPart(source, hint))
            .ToArray();
        if (searchPickerHints.Length > 0)
        {
            if (searchPickerHints.Length != 1)
            {
                error = $"Value source matches {searchPickerHints.Length} SearchPicker hints; configure unique part locators.";
                return false;
            }

            var hint = searchPickerHints[0];
            var selectedText = _controlReader.TryFindControl(hint.Parts.ResultsLocator, hint.Parts.LocatorKind, out var results)
                ? results switch
                {
                    ComboBox comboBox => ExtractSelectionText(comboBox.SelectedItem),
                    ListBox listBox => ExtractSelectionText(listBox.SelectedItem),
                    _ => null
                }
                : null;
            if (requireLiteral && selectedText is null)
            {
                error = "SearchPicker does not expose a committed selected value for a literal assertion.";
                return false;
            }

            candidate = new SemanticValueCandidate(
                CreateCompositeDescriptor(hint.LocatorValue, UiControlType.SearchPicker, hint.LocatorKind, hint.FallbackToName, source, null),
                RecorderValueKind.Text,
                RecorderValueAccessorKind.SelectedItemText,
                StringValue: selectedText);
            error = string.Empty;
            return true;
        }

        var singleSelectHints = _options.SingleSelectHints
            .Where(hint => IsSingleSelectPart(source, hint))
            .ToArray();
        if (singleSelectHints.Length > 0)
        {
            if (singleSelectHints.Length != 1)
            {
                error = $"Value source matches {singleSelectHints.Length} single-select hints; configure unique part locators.";
                return false;
            }

            var hint = singleSelectHints[0];
            var selectedText = TryReadSingleSelectCommittedText(hint);
            if (requireLiteral && selectedText is null)
            {
                error = "Single-select editor does not expose a committed selected value for a literal assertion.";
                return false;
            }

            candidate = new SemanticValueCandidate(
                CreateCompositeDescriptor(hint.LocatorValue, UiControlType.ComboBox, hint.LocatorKind, hint.FallbackToName, source, null),
                RecorderValueKind.Text,
                RecorderValueAccessorKind.SelectedItemText,
                StringValue: selectedText);
            error = string.Empty;
            return true;
        }

        var colorHints = _options.ColorPickerHints.Where(hint => IsColorPickerPart(source, hint)).ToArray();
        if (colorHints.Length > 0)
        {
            if (colorHints.Length != 1)
            {
                error = $"Value source matches {colorHints.Length} color-picker hints; configure unique part locators.";
                return false;
            }

            var hint = colorHints[0];
            var hasColor = TryReadColorPickerValue(hint, out var color);
            if (requireLiteral && !hasColor)
            {
                error = "Color picker does not expose a committed canonical color.";
                return false;
            }

            candidate = new SemanticValueCandidate(
                CreateCompositeDescriptor(hint.LocatorValue, UiControlType.ColorPicker, hint.LocatorKind, hint.FallbackToName, source, null),
                RecorderValueKind.Color,
                RecorderValueAccessorKind.Color,
                StringValue: hasColor ? color : null);
            error = string.Empty;
            return true;
        }

        var timeHints = _options.TimePickerHints.Where(hint => IsTimePickerPart(source, hint)).ToArray();
        if (timeHints.Length > 0)
        {
            if (timeHints.Length != 1)
            {
                error = $"Value source matches {timeHints.Length} time-picker hints; configure unique part locators.";
                return false;
            }

            var hint = timeHints[0];
            var timeValue = _controlReader.TryFindControl(hint.Parts.TimePickerLocator, hint.Parts.LocatorKind, out var timeControl)
                && timeControl is TimePicker timePicker
                    ? timePicker.SelectedTime
                    : null;
            if (requireLiteral && timeValue is null)
            {
                error = "Time picker does not expose a selected time.";
                return false;
            }

            candidate = new SemanticValueCandidate(
                CreateCompositeDescriptor(hint.LocatorValue, UiControlType.TimePicker, hint.LocatorKind, hint.FallbackToName, source, null),
                RecorderValueKind.Time,
                RecorderValueAccessorKind.SelectedTime,
                TimeValue: timeValue);
            error = string.Empty;
            return true;
        }

        return TryResolvePrimitiveSemanticValue(source, requireLiteral, out candidate, out error);
    }

    private bool TryResolveNotificationSemanticValue(
        Control source,
        bool requireLiteral,
        out SemanticValueCandidate candidate,
        out string error)
    {
        candidate = null!;
        error = string.Empty;
        var resolution = ResolveNotificationTextHint(source);
        if (!resolution.IsConfigured)
        {
            return false;
        }

        if (!resolution.Success)
        {
            error = resolution.Error ?? "Configured notification does not expose a readable text part.";
            return false;
        }

        var text = ExtractTextValue(resolution.TextControl!);
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Notification text part does not expose readable text.";
            return false;
        }

        var hint = resolution.Hint!;
        candidate = new SemanticValueCandidate(
            CreateCompositeDescriptor(
                hint.LocatorValue,
                UiControlType.Notification,
                hint.LocatorKind,
                hint.FallbackToName,
                resolution.NotificationRoot!,
                warning: null),
            RecorderValueKind.Text,
            RecorderValueAccessorKind.Text,
            StringValue: text);
        return true;
    }

    private bool TryResolveDatePickerSemanticValue(
        Control source,
        bool requireLiteral,
        out SemanticValueCandidate candidate,
        out string error)
    {
        candidate = null!;
        error = string.Empty;
        var hints = _options.DatePickerHints
            .Where(hint => IsDatePickerPart(source, hint))
            .ToArray();
        if (hints.Length == 0)
        {
            return false;
        }

        if (hints.Length > 1)
        {
            error = $"Value source matches {hints.Length} date-picker hints; configure unique part locators.";
            return false;
        }

        var hint = hints[0];
        var selectedDate = TryReadDatePickerCommittedValue(hint);
        if (requireLiteral && selectedDate is null)
        {
            error = "Date picker does not expose a committed selected date.";
            return false;
        }

        candidate = new SemanticValueCandidate(
            CreateCompositeDescriptor(
                hint.LocatorValue,
                UiControlType.DateTimePicker,
                hint.LocatorKind,
                hint.FallbackToName,
                source,
                warning: null),
            RecorderValueKind.Date,
            RecorderValueAccessorKind.SelectedDate,
            DateValue: selectedDate);
        return true;
    }

    private DateTime? TryReadDatePickerCommittedValue(RecorderDatePickerHint hint)
    {
        if (!_controlReader.TryFindControl(hint.Parts.ValueLocator, hint.Parts.LocatorKind, out var valueControl))
        {
            return null;
        }

        return valueControl switch
        {
            DatePicker { SelectedDate: { } selectedDate } => selectedDate.DateTime.Date,
            TextBox textBox when TryParseDate(textBox.Text, out var value) => value.Date,
            TextBlock textBlock when TryParseDate(textBlock.Text, out var value) => value.Date,
            Label label when TryParseDate(label.Content?.ToString(), out var value) => value.Date,
            _ => null
        };
    }

    private static bool TryParseDate(string? text, out DateTime value)
    {
        var candidate = text?.Trim();
        return DateTime.TryParse(
                candidate,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out value)
            || DateTime.TryParse(
                candidate,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out value);
    }

    private bool TryResolveConfiguredSemanticValue(
        Control source,
        bool requireLiteral,
        out SemanticValueCandidate candidate,
        out string error,
        out bool resolverHandled)
    {
        candidate = null!;
        error = string.Empty;
        resolverHandled = false;
        foreach (var resolver in _options.SemanticValueResolvers)
        {
            var resolution = resolver.Resolve(source)
                ?? RecorderSemanticValueResolution.Failed(
                    $"Semantic value resolver '{resolver.GetType().Name}' returned no resolution result.");
            if (resolution.Kind == RecorderSemanticValueResolutionKind.NotHandled)
            {
                continue;
            }

            resolverHandled = true;
            if (resolution.Kind == RecorderSemanticValueResolutionKind.Failed)
            {
                error = string.IsNullOrWhiteSpace(resolution.ErrorMessage)
                    ? $"Semantic value resolver '{resolver.GetType().Name}' failed without a diagnostic."
                    : resolution.ErrorMessage.Trim();
                return false;
            }

            if (resolution.Kind != RecorderSemanticValueResolutionKind.Resolved
                || resolution.Target is not { } target)
            {
                error = $"Semantic value resolver '{resolver.GetType().Name}' returned an invalid resolution result.";
                return false;
            }

            if (!TryCreateConfiguredSemanticValueCandidate(source, target, out candidate, out error))
            {
                return false;
            }

            var timeControl = candidate.Control;
            if (timeControl.ControlType == UiControlType.TimePicker && _options.TimePickerHints.Any(hint =>
                    RecorderTimePickerBinding.IsPresenterHint(hint) && hint.LocatorKind == timeControl.LocatorKind
                    && hint.LocatorValue.Trim() == timeControl.LocatorValue))
            {
                if (!TryResolvePresenterTimeBinding(source, out var timeBinding, out error)) return false;
                if (timeBinding.Hint.LocatorKind != candidate.Control.LocatorKind
                    || timeBinding.Hint.LocatorValue.Trim() != candidate.Control.LocatorValue)
                {
                    error = "The semantic time target does not belong to the source's native presenter owner.";
                    return false;
                }
                candidate = PresenterTimeCandidate(timeBinding);
            }

            if (requireLiteral && !HasLiteral(candidate))
            {
                error = $"Semantic value resolver '{resolver.GetType().Name}' did not provide the current value required for a literal assertion.";
                return false;
            }

            return true;
        }

        return false;
    }

    private bool TryCreateConfiguredSemanticValueCandidate(
        Control source,
        RecorderSemanticValueTarget target,
        out SemanticValueCandidate candidate,
        out string error)
    {
        candidate = null!;
        if (string.IsNullOrWhiteSpace(target.LocatorValue))
        {
            error = "Semantic value target must provide a logical locator.";
            return false;
        }

        if (!IsValueAccessorCompatible(target.ValueKind, target.ValueAccessorKind))
        {
            error = $"Semantic value accessor '{target.ValueAccessorKind}' is not compatible with value kind '{target.ValueKind}'.";
            return false;
        }

        var configuredDefinition = _options.GridAutomation.FirstOrDefault(definition =>
            definition.RuntimeLocatorKind == target.LocatorKind
            && string.Equals(definition.RuntimeLocatorValue, target.LocatorValue.Trim(), StringComparison.Ordinal));
        if (target.ValueAccessorKind is RecorderValueAccessorKind.GridCellText or RecorderValueAccessorKind.GridCellValue
            && configuredDefinition?.Columns.Any(static column => column.IsObjectKey) == true)
        {
            error = "A custom semantic resolver cannot supply bare object-key row conditions. "
                + "Use the configured grid's actual cell metadata and published row ItemStatus for capture "
                + "(return NotHandled for this cell), then Remember its runtime row before recording dependent actions.";
            return false;
        }

        GridValueContext? gridContext = null;
        if (target.ValueAccessorKind is RecorderValueAccessorKind.GridCellText
            or RecorderValueAccessorKind.GridCellValue)
        {
            if (target.ControlType != UiControlType.Grid
                || target.GridContext is not { RowConditions.Count: > 0 } configuredGrid
                || string.IsNullOrWhiteSpace(configuredGrid.TargetColumnName)
                || configuredGrid.RowConditions.Any(static condition =>
                    string.IsNullOrWhiteSpace(condition.ColumnName)))
            {
                error = "Grid semantic value target must provide a logical Grid, a stable row selector, and a target column.";
                return false;
            }

            var duplicateColumn = configuredGrid.RowConditions
                .GroupBy(static condition => condition.ColumnName.Trim(), StringComparer.Ordinal)
                .FirstOrDefault(static group => group.Count() > 1);
            if (duplicateColumn is not null)
            {
                error = $"Grid semantic value target contains duplicate row-selector column '{duplicateColumn.Key}'.";
                return false;
            }

            gridContext = new GridValueContext(
                RowIndex: -1,
                ColumnIndex: -1,
                configuredGrid.RowConditions
                    .Select(static condition => new RecordedGridRowCondition(
                        condition.ColumnName.Trim(),
                        condition.Value ?? string.Empty))
                    .ToArray(),
                configuredGrid.TargetColumnName.Trim());
        }
        else if (target.GridContext is not null)
        {
            error = "Only grid-cell semantic values may provide a grid context.";
            return false;
        }

        candidate = new SemanticValueCandidate(
            CreateCompositeDescriptor(
                target.LocatorValue,
                target.ControlType,
                target.LocatorKind,
                target.FallbackToName,
                source,
                warning: null),
            target.ValueKind,
            target.ValueAccessorKind,
            StringValue: target.StringValue,
            BoolValue: target.BoolValue,
            DoubleValue: target.DoubleValue,
            DateValue: target.DateValue,
            TimeValue: target.TimeValue,
            StringValues: target.StringValues?.ToArray(),
            GridContext: gridContext);
        error = string.Empty;
        return true;
    }

    private static bool IsValueAccessorCompatible(
        RecorderValueKind valueKind,
        RecorderValueAccessorKind accessorKind)
    {
        return valueKind switch
        {
            RecorderValueKind.Text => accessorKind is RecorderValueAccessorKind.Text
                or RecorderValueAccessorKind.SelectedItemText
                or RecorderValueAccessorKind.GridCellValue,
            RecorderValueKind.Number => accessorKind is RecorderValueAccessorKind.NumericValue
                or RecorderValueAccessorKind.NumericText
                or RecorderValueAccessorKind.GridCellValue,
            RecorderValueKind.Boolean => accessorKind is RecorderValueAccessorKind.IsChecked
                or RecorderValueAccessorKind.IsToggled
                or RecorderValueAccessorKind.IsSelected
                or RecorderValueAccessorKind.IsExpanded
                or RecorderValueAccessorKind.IsEnabled
                or RecorderValueAccessorKind.IsReadOnly
                or RecorderValueAccessorKind.GridCellValue,
            RecorderValueKind.Date => accessorKind is RecorderValueAccessorKind.SelectedDate
                or RecorderValueAccessorKind.GridCellValue,
            RecorderValueKind.DateTime => accessorKind == RecorderValueAccessorKind.GridCellValue,
            RecorderValueKind.Time => accessorKind is RecorderValueAccessorKind.SelectedTime
                or RecorderValueAccessorKind.GridCellValue,
            RecorderValueKind.Color => accessorKind is RecorderValueAccessorKind.Color
                or RecorderValueAccessorKind.GridCellValue,
            RecorderValueKind.StringSet => accessorKind == RecorderValueAccessorKind.SelectedItems,
            RecorderValueKind.GridCellText => accessorKind is RecorderValueAccessorKind.GridCellText
                or RecorderValueAccessorKind.GridCellValue,
            _ => false
        };
    }

    private bool TryResolvePrimitiveSemanticValue(
        Control source,
        bool requireLiteral,
        out SemanticValueCandidate candidate,
        out string error)
    {
        candidate = null!;
        var controlType = source is TextBox textBox
            && RecorderSpinnerProxyConfiguration.IsInteractivePart(_options, textBox)
                ? UiControlType.Spinner
                : ClassifyControlType(source);
        var capabilities = RecorderAssertionCapabilities.Get(controlType);
        if (!capabilities.SupportsSemanticValue)
        {
            error = capabilities.RequiresConcreteTarget
                ? $"Control '{source.GetType().Name}' requires a concrete value target before value checks are available."
                : $"Control '{source.GetType().Name}' supports Exists and Enabled checks but does not expose a semantic value.";
            return false;
        }

        var locator = _selectorResolver.Resolve(source, controlType);
        if (!locator.Success || locator.Control is null)
        {
            error = locator.Message;
            return false;
        }

        var valueKind = capabilities.ValueKinds.Single();
        var accessorKind = capabilities.AccessorKinds.Single();
        var resolvedCandidate = accessorKind switch
        {
            RecorderValueAccessorKind.Text when source is TextBox or TextBlock or Label or Button =>
                new SemanticValueCandidate(
                    locator.Control,
                    valueKind,
                    accessorKind,
                    StringValue: ExtractTextValue(source)),
            RecorderValueAccessorKind.SelectedItemText when source is ComboBox comboBox =>
                new SemanticValueCandidate(
                    locator.Control,
                    valueKind,
                    accessorKind,
                    StringValue: ExtractSelectionText(comboBox.SelectedItem)),
            RecorderValueAccessorKind.SelectedItemText when source is ListBox listBox =>
                new SemanticValueCandidate(
                    locator.Control,
                    valueKind,
                    accessorKind,
                    StringValue: ExtractSelectionText(listBox.SelectedItem)),
            RecorderValueAccessorKind.NumericValue => new SemanticValueCandidate(
                locator.Control,
                valueKind,
                accessorKind,
                DoubleValue: RecorderNumericValueReader.TryReadNumericValue(source, out var numericValue) ? numericValue : null),
            RecorderValueAccessorKind.SelectedDate => new SemanticValueCandidate(
                locator.Control,
                valueKind,
                accessorKind,
                DateValue: TryReadDateValue(source, out var selectedDate) ? selectedDate.Date : null),
            RecorderValueAccessorKind.SelectedTime when source is TimePicker timePicker =>
                new SemanticValueCandidate(
                    locator.Control,
                    valueKind,
                    accessorKind,
                    TimeValue: timePicker.SelectedTime),
            RecorderValueAccessorKind.IsChecked when source is CheckBox checkBox =>
                new SemanticValueCandidate(locator.Control, valueKind, accessorKind, BoolValue: checkBox.IsChecked == true),
            RecorderValueAccessorKind.IsToggled when source is ToggleButton toggleButton =>
                new SemanticValueCandidate(locator.Control, valueKind, accessorKind, BoolValue: toggleButton.IsChecked == true),
            RecorderValueAccessorKind.IsSelected => source switch
            {
                RadioButton radioButton => new SemanticValueCandidate(
                    locator.Control,
                    valueKind,
                    accessorKind,
                    BoolValue: radioButton.IsChecked == true),
                TabItem tabItem => new SemanticValueCandidate(
                    locator.Control,
                    valueKind,
                    accessorKind,
                    BoolValue: tabItem.IsSelected),
                TreeViewItem treeItem => new SemanticValueCandidate(
                    locator.Control,
                    valueKind,
                    accessorKind,
                    BoolValue: treeItem.IsSelected),
                _ => null
            },
            RecorderValueAccessorKind.IsExpanded when source is Expander expander =>
                new SemanticValueCandidate(locator.Control, valueKind, accessorKind, BoolValue: expander.IsExpanded),
            _ => null
        };

        if (resolvedCandidate is null)
        {
            error = $"Control '{source.GetType().Name}' does not implement semantic accessor '{accessorKind}'.";
            return false;
        }

        candidate = resolvedCandidate;

        if (requireLiteral && !HasLiteral(candidate))
        {
            error = $"Control '{source.GetType().Name}' does not expose a current value for a literal assertion.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryCreateMultiSelectSemanticValue(
        Control source,
        IReadOnlyList<(string LocatorValue, UiLocatorKind LocatorKind, bool FallbackToName, MultiSelectParts Parts, UiControlType ControlType)> hints,
        bool requireLiteral,
        out SemanticValueCandidate candidate,
        out string error)
    {
        candidate = null!;
        if (hints.Count != 1)
        {
            error = $"Value source matches {hints.Count} multi-select hints; configure unique part locators.";
            return false;
        }

        var hint = hints[0];
        if (_controlReader.TryFindControl(hint.Parts.ItemsContainerLocator, hint.Parts.LocatorKind, out var itemsContainer)
            && itemsContainer.IsEffectivelyVisible)
        {
            error = "Apply or cancel the open multi-select popup before remembering or asserting its committed value.";
            return false;
        }

        if (requireLiteral)
        {
            error = "Use a checkpoint comparison for multi-select values; direct collection literals are not captured from a closed popup.";
            return false;
        }

        candidate = new SemanticValueCandidate(
            CreateCompositeDescriptor(
                hint.LocatorValue,
                hint.ControlType,
                hint.LocatorKind,
                hint.FallbackToName,
                source,
                warning: null),
            RecorderValueKind.StringSet,
            RecorderValueAccessorKind.SelectedItems);
        error = string.Empty;
        return true;
    }

    private bool TryCreateComboBoxFilterSemanticValue(
        Control source,
        IReadOnlyList<RecorderComboBoxFilterHint> hints,
        bool requireLiteral,
        out SemanticValueCandidate candidate,
        out string error)
    {
        candidate = null!;
        if (hints.Count != 1)
        {
            error = $"Value source matches {hints.Count} combo-box filter hints; configure unique part locators.";
            return false;
        }

        var hint = hints[0];
        var parts = hint.Parts;
        var root = FindFromRelatedControlTrees(source, parts.RootLocator, parts.LocatorKind)
            ?? FindFromValidationRoot(parts.RootLocator, parts.LocatorKind);
        if (root is null)
        {
            error = $"Combo-box filter root '{parts.RootLocator}' was not found.";
            return false;
        }

        var itemsContainer = FindFromRelatedControlTrees(source, parts.ItemsContainerLocator, parts.LocatorKind)
            ?? FindFromValidationRoot(parts.ItemsContainerLocator, parts.LocatorKind);
        var isPopupOpen = itemsContainer?.IsEffectivelyVisible == true;
        IReadOnlyList<string> selectedValues;
        if (isPopupOpen && !string.IsNullOrWhiteSpace(parts.ApplyButtonLocator))
        {
            if (!TryReadFilterRootSelection(root, allowSingleItemFallback: false, out var currentValues, out _)
                || !TryGetRecordedAppliedFilterValues(hint, out var appliedValues)
                || !currentValues.ToHashSet(StringComparer.Ordinal).SetEquals(appliedValues))
            {
                error = "Apply or cancel the open multi-select popup before asserting its committed value. "
                    + "The applied selection could not be independently confirmed.";
                return false;
            }

            selectedValues = appliedValues;
        }
        else if (isPopupOpen)
        {
            if (!TryReadSelectionValues(source, ToMultiSelectParts(parts), "combo-box filter",
                    out selectedValues, out error))
            {
                return false;
            }
        }
        else if (!TryReadFilterRootSelection(
                     root,
                     allowSingleItemFallback: string.IsNullOrWhiteSpace(parts.ApplyButtonLocator),
                     out selectedValues,
                     out error))
        {
            return false;
        }

        if (!TryReadFilterDisplayedText(source, root, parts, selectedValues, out var displayedText, out error))
        {
            return false;
        }

        if (requireLiteral && selectedValues.Count == 0)
        {
            error = $"Combo-box filter '{parts.RootLocator}' has no applied value to check.";
            return false;
        }

        candidate = new SemanticValueCandidate(
            CreateCompositeDescriptor(
                hint.LocatorValue,
                UiControlType.ComboBoxFilter,
                hint.LocatorKind,
                hint.FallbackToName,
                source,
                warning: null),
            RecorderValueKind.StringSet,
            RecorderValueAccessorKind.SelectedItems,
            StringValue: displayedText,
            StringValues: selectedValues.OrderBy(static value => value, StringComparer.Ordinal).ToArray());
        error = string.Empty;
        return true;
    }

    private bool TryGetRecordedAppliedFilterValues(
        RecorderComboBoxFilterHint hint,
        out IReadOnlyList<string> values)
    {
        var appliedStep = _recordedStepsProvider?.Invoke()
            .LastOrDefault(step => !step.IsIgnored
                && step.CanPersist
                && step.ActionKind == RecordedActionKind.ApplyFilterSelection
                && step.Control.ControlType == UiControlType.ComboBoxFilter
                && step.Control.LocatorKind == hint.LocatorKind
                && string.Equals(step.Control.LocatorValue, hint.LocatorValue, StringComparison.Ordinal));
        values = appliedStep?.StringValues ?? [];
        return appliedStep?.StringValues is not null;
    }

    private static bool TryReadFilterRootSelection(
        Control root,
        bool allowSingleItemFallback,
        out IReadOnlyList<string> selectedValues,
        out string error)
    {
        selectedValues = [];
        object? selection = root switch
        {
            ComboBox comboBox => comboBox.SelectedItem,
            ListBox listBox => listBox.SelectedItems,
            _ => null
        };
        if (selection is null)
        {
            GridPropertyValueReader.TryReadProperty(root, "SelectedItems", out selection);
            if (selection is null)
            {
                GridPropertyValueReader.TryReadProperty(root, "SelectedItem", out selection);
            }
        }

        if (selection is null)
        {
            error = $"Combo-box filter '{AutomationProperties.GetAutomationId(root)}' does not expose its applied selection on the logical root.";
            return false;
        }

        var rawItems = selection is IEnumerable enumerable and not string
            ? enumerable.Cast<object?>().ToArray()
            : [selection];
        if (rawItems.Length == 0 && allowSingleItemFallback)
        {
            var selectedItem = root is ListBox listBox ? listBox.SelectedItem : null;
            if (selectedItem is null)
            {
                GridPropertyValueReader.TryReadProperty(root, "SelectedItem", out selectedItem);
            }

            if (selectedItem is not null)
            {
                rawItems = [selectedItem];
            }
        }
        var captions = rawItems
            .Select(static item => item is Control control
                ? ReadRenderedSelectionCaption(control) ?? ReadSelectionDataCaption(item)
                : ReadSelectionDataCaption(item))
            .ToArray();
        if (captions.Any(string.IsNullOrWhiteSpace)
            || captions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != captions.Length)
        {
            error = "Applied combo-box filter selection has missing or duplicate visible item captions.";
            return false;
        }

        selectedValues = captions.Select(static text => text!.Trim()).ToArray();
        error = string.Empty;
        return true;
    }

    private bool TryReadFilterDisplayedText(
        Control source,
        Control root,
        ComboBoxFilterParts parts,
        IReadOnlyList<string> selectedValues,
        out string displayedText,
        out string error)
    {
        var displaySource = string.IsNullOrWhiteSpace(parts.DisplayValueLocator)
            ? root
            : FindFromRelatedControlTrees(source, parts.DisplayValueLocator, parts.LocatorKind)
                ?? FindFromValidationRoot(parts.DisplayValueLocator, parts.LocatorKind);
        if (displaySource is null)
        {
            displayedText = string.Empty;
            error = $"Combo-box filter display part '{parts.DisplayValueLocator}' was not found.";
            return false;
        }

        if (!TryReadFilterDisplayText(
                displaySource,
                parts.DisplayValueLocator ?? parts.RootLocator,
                out var text,
                out error))
        {
            displayedText = string.Empty;
            return false;
        }
        if (string.IsNullOrWhiteSpace(text)
            && string.IsNullOrWhiteSpace(parts.DisplayValueLocator)
            && selectedValues.Count == 1)
        {
            var matchingEditors = root.GetVisualDescendants()
                .OfType<TextBox>()
                .Where(static editor => editor.IsEffectivelyVisible)
                .Select(static editor => editor.Text?.Trim())
                .Where(value => string.Equals(value, selectedValues[0], StringComparison.Ordinal))
                .ToArray();
            if (matchingEditors.Length == 1)
            {
                text = matchingEditors[0];
            }
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            displayedText = string.Empty;
            error = $"Combo-box filter '{parts.RootLocator}' does not expose its displayed value. "
                + "Expose visible text or configure DisplayValueLocator.";
            return false;
        }

        displayedText = text.Trim();
        error = string.Empty;
        return true;
    }

    private static bool TryReadFilterDisplayText(
        Control source,
        string filterLocator,
        out string? text,
        out string error)
    {
        text = source switch
        {
            TextBox input when input.IsEffectivelyVisible => input.Text,
            TextBlock block when block.IsEffectivelyVisible => block.Text,
            Label label when label.IsEffectivelyVisible => label.Content?.ToString(),
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(text))
        {
            error = string.Empty;
            return true;
        }

        var visibleTexts = source.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(static block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text))
            .Select(static block => block.Text!.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (visibleTexts.Length > 1)
        {
            text = null;
            error = $"Combo-box filter display part '{filterLocator}' has multiple visible text values. "
                + "Configure DisplayValueLocator for the committed value.";
            return false;
        }

        text = visibleTexts.FirstOrDefault()
            ?? AutomationProperties.GetName(source)
            ?? (source as ContentControl)?.Content as string;
        error = string.Empty;
        return true;
    }

    private static bool IsMultiSelectPart(
        Control source,
        string locatorValue,
        UiLocatorKind locatorKind,
        MultiSelectParts parts)
    {
        return EnumerateRelatedControls(source).Any(current =>
            MatchesLocator(current, locatorKind, locatorValue)
            || MatchesAnyLocator(
                current,
                parts.LocatorKind,
                parts.RootLocator,
                parts.OpenButtonLocator,
                parts.ItemsContainerLocator,
                parts.ApplyButtonLocator,
                parts.CancelButtonLocator));
    }

    private static bool IsSearchPickerPart(Control source, RecorderSearchPickerHint hint)
    {
        return EnumerateRelatedControls(source).Any(current =>
            MatchesLocator(current, hint.LocatorKind, hint.LocatorValue)
            || MatchesAnyLocator(
                current,
                hint.Parts.LocatorKind,
                hint.Parts.SearchInputLocator,
                hint.Parts.ResultsLocator,
                hint.Parts.ApplyButtonLocator,
                hint.Parts.ExpandButtonLocator));
    }

    private static bool IsSearchControlPart(Control source, RecorderSearchControlHint hint)
    {
        return EnumerateRelatedControls(source).Any(current =>
            MatchesLocator(current, hint.LocatorKind, hint.LocatorValue)
            || MatchesAnyLocator(
                current,
                hint.Parts.LocatorKind,
                hint.Parts.SearchInputLocator,
                hint.Parts.HistoryResultsLocator,
                hint.Parts.SearchButtonLocator,
                hint.Parts.HistoryOpenButtonLocator,
                hint.Parts.HistoryRootLocator));
    }

    private string? TryReadSingleSelectCommittedText(RecorderSingleSelectHint hint)
    {
        foreach (var locator in new[] { hint.Parts.SelectedValueLocator, hint.Parts.RootLocator, hint.Parts.ResultsLocator })
        {
            if (string.IsNullOrWhiteSpace(locator)
                || !_controlReader.TryFindControl(locator, hint.Parts.LocatorKind, out var control))
            {
                continue;
            }

            var value = control switch
            {
                ComboBox comboBox => ExtractSelectionText(comboBox.SelectedItem),
                ListBox listBox => ExtractSelectionText(listBox.SelectedItem),
                _ => ExtractTextValue(control)
            };
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }


    private bool TryCreateColorPickerAssertionStep(
        Control source,
        RecorderAssertionMode mode,
        out StepCreationResult result)
    {
        result = StepCreationResult.Unsupported("Control is not configured as a recorder color picker.");
        if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Text))
        {
            return false;
        }

        var matchingHints = _options.ColorPickerHints
            .Where(hint => IsColorPickerPart(source, hint))
            .ToArray();
        if (matchingHints.Length == 0)
        {
            return false;
        }

        if (matchingHints.Length > 1)
        {
            result = StepCreationResult.Unsupported(
                $"Color picker source matches {matchingHints.Length} configured hints; locators must identify one editor.");
            return true;
        }

        var hint = matchingHints[0];
        if (!TryReadColorPickerValue(hint, out var color))
        {
            result = StepCreationResult.Unsupported(
                "Configured color picker does not expose a valid current #RRGGBB or #AARRGGBB value.");
            return true;
        }

        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.ColorPicker,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning: null);
        result = CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.WaitUntilColorEquals,
                descriptor,
                StringValue: color));
        return true;
    }

    private bool TryCreateGridAssertionStep(Control source, RecorderAssertionMode mode, out StepCreationResult result)
    {
        result = StepCreationResult.Unsupported("Recorder could not derive a supported grid assertion for this control.");
        if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Text))
        {
            return false;
        }

        if (!_gridCapture.TryResolveGridHint(source, out var hint, out var gridSource))
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

        if (_gridCapture.TryResolveGridCell(source, gridSource, hint, items, out var rowIndex, out var columnIndex, out var cellValue))
        {
            result = CreateGridStep(
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
                locatorResult.Message,
                hint.TargetLocatorValue,
                hint.TargetLocatorKind,
                rowIndex,
                columnIndex,
                excludeTargetColumnFromIdentity: true);
            return true;
        }

        if (hint.RowIdentityColumnPropertyNames is { Count: > 0 }
            && string.IsNullOrWhiteSpace(ExtractTextValue(source))
            && TryResolveGridRow(source, gridSource, items, out rowIndex, out _))
        {
            result = CreateGridStep(
                source,
                new RecordedStep(
                    RecordedActionKind.WaitUntilGridContainsRow,
                    locatorResult.Control,
                    Warning: locatorResult.Control.Warning,
                    ValidationStatus: locatorResult.ValidationStatus,
                    ValidationMessage: locatorResult.ValidationMessage,
                    CanPersist: locatorResult.CanPersist,
                    RowIndex: rowIndex),
                locatorResult.Message,
                hint.TargetLocatorValue,
                hint.TargetLocatorKind,
                rowIndex,
                columnIndex: null,
                excludeTargetColumnFromIdentity: false);
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
        if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Text or RecorderAssertionMode.Exists))
        {
            return false;
        }

        var resolution = ResolveNotificationTextHint(source);
        if (!resolution.IsConfigured)
        {
            return false;
        }

        if (!resolution.Success)
        {
            result = StepCreationResult.Unsupported(
                resolution.Error ?? "Recorder could not resolve the configured notification instance.");
            return true;
        }

        var hint = resolution.Hint!;
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.Notification,
            hint.LocatorKind,
            hint.FallbackToName,
            resolution.NotificationRoot!,
            warning: null);
        if (mode == RecorderAssertionMode.Exists)
        {
            result = CreateStep(
                resolution.NotificationRoot!,
                new RecordedStep(RecordedActionKind.WaitUntilExists, descriptor));
            return true;
        }

        var text = ExtractTextValue(resolution.TextControl!);
        if (string.IsNullOrWhiteSpace(text))
        {
            result = StepCreationResult.Unsupported("Notification text part does not expose text.");
            return true;
        }

        result = CreateStep(
            resolution.NotificationRoot!,
            new RecordedStep(
                RecordedActionKind.WaitUntilNotificationContains,
                descriptor,
                StringValue: text.Trim()));
        return true;
    }

    private StepCreationResult TryCreateOpenGridRowStep(
        Control source,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridActionHint hint)
    {
        if (!_gridCapture.TryResolveGridRowIndex(source, hint, out var rowIndex))
        {
            return StepCreationResult.Unsupported("Grid open-row action requires a row index from the hint or grid row/cell context.");
        }

        return CreateGridStep(
            source,
            new RecordedStep(
                RecordedActionKind.OpenGridRow,
                descriptor,
                Warning: warning,
                RowIndex: rowIndex),
            warning,
            hint.TargetGridLocatorValue,
            hint.TargetGridLocatorKind,
            rowIndex,
            columnIndex: null,
            excludeTargetColumnFromIdentity: false);
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
        if (!_gridCapture.TryResolveGridCellIndexes(source, hint, out var rowIndex, out var columnIndex))
        {
            return StepCreationResult.Unsupported("Grid copy-cell action requires row and column indexes from the hint or grid cell context.");
        }

        return CreateGridStep(
            source,
            new RecordedStep(
                RecordedActionKind.CopyGridCell,
                descriptor,
                Warning: warning,
                RowIndex: rowIndex,
                ColumnIndex: columnIndex),
            warning,
            hint.TargetGridLocatorValue,
            hint.TargetGridLocatorKind,
            rowIndex,
            columnIndex,
            excludeTargetColumnFromIdentity: true);
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
            TextBlock or Label => UiControlType.Label,
            _ when control is Button => ClassifyControlType(control),
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
            ProgressBar => UiControlType.ProgressBar,
            NumericUpDown => UiControlType.Spinner,
            TimePicker => UiControlType.TimePicker,
            Expander => UiControlType.Expander,
            DatePicker => UiControlType.DateTimePicker,
            Calendar => UiControlType.Calendar,
            TabItem => UiControlType.TabItem,
            TreeView => UiControlType.Tree,
            TreeViewItem => UiControlType.TreeItem,
            TextBlock or Label => UiControlType.Label,
            _ => UiControlType.AutomationElement
        };
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
        string? selectedText,
        string? capturedSearchText)
    {
        if (TryResolveGridSearchPickerHint(searchInput, results, resultsKind, out var gridHint))
        {
            return TryCreateGridSearchPickerStep(
                searchInput,
                results,
                selectedText,
                capturedSearchText,
                gridHint);
        }

        if (_gridCapture.TryResolveGridHint(searchInput, out _, out _))
        {
            return StepCreationResult.Unsupported(NoGridSearchPickerHintMessage);
        }

        if (!TryResolveSearchPickerHint(searchInput, results, resultsKind, out var hint))
        {
            return StepCreationResult.Unsupported("Controls are not configured as a recorder search picker.");
        }

        return TryCreateConfiguredSearchPickerStep(
            searchInput,
            results,
            selectedText,
            capturedSearchText,
            hint);
    }

    private SearchPickerSelectionCaptureResult TryCreateSearchPickerSelectionCapture(
        Control results,
        SearchPickerResultsKind resultsKind,
        string? selectedText,
        TextBox? pendingSearchInput,
        string? capturedSearchText)
    {
        var matchingHints = FindSearchPickerHints(results, resultsKind).ToArray();
        if (matchingHints.Length == 0)
        {
            return new SearchPickerSelectionCaptureResult(
                IsConfigured: false,
                HasSelection: false,
                SearchInput: null,
                StepCreationResult.Unsupported("Control is not configured as a recorder search picker result."));
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return new SearchPickerSelectionCaptureResult(
                IsConfigured: true,
                HasSelection: false,
                SearchInput: null,
                StepCreationResult.Unsupported("Search picker does not have a selected result to record."));
        }

        if (matchingHints.Length > 1)
        {
            return new SearchPickerSelectionCaptureResult(
                IsConfigured: true,
                HasSelection: true,
                SearchInput: null,
                StepCreationResult.Unsupported(
                    $"Search picker results match {matchingHints.Length} configured hints; ResultsLocator must identify one picker."));
        }

        var hint = matchingHints[0];
        if (!_controlReader.TryFindControl(hint.Parts.SearchInputLocator, hint.Parts.LocatorKind, out var control)
            || control is not TextBox searchInput)
        {
            return new SearchPickerSelectionCaptureResult(
                IsConfigured: true,
                HasSelection: true,
                SearchInput: null,
                StepCreationResult.Unsupported(
                    $"Configured search picker input '{hint.Parts.SearchInputLocator}' could not be resolved as a TextBox."));
        }

        var relatedCapturedSearchText = ReferenceEquals(pendingSearchInput, searchInput)
            ? capturedSearchText
            : null;
        var result = TryCreateConfiguredSearchPickerStep(
            searchInput,
            results,
            selectedText,
            relatedCapturedSearchText,
            hint);

        return new SearchPickerSelectionCaptureResult(
            IsConfigured: true,
            HasSelection: true,
            searchInput,
            result);
    }

    private StepCreationResult TryCreateConfiguredSearchPickerStep(
        TextBox searchInput,
        Control results,
        string? selectedText,
        string? capturedSearchText,
        RecorderSearchPickerHint hint)
    {
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return StepCreationResult.Unsupported("Search picker does not have a selected result to record.");
        }

        var searchText = ResolveSearchPickerSearchText(
            capturedSearchText,
            searchInput.Text,
            selectedText);
        if (searchText is null)
        {
            return StepCreationResult.Unsupported("Search picker search text is empty.");
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

    private IEnumerable<RecorderSearchPickerHint> FindSearchPickerHints(
        Control results,
        SearchPickerResultsKind resultsKind)
    {
        return _options.SearchPickerHints.Where(candidate =>
            candidate.Parts.ResultsKind == resultsKind
            && !string.IsNullOrWhiteSpace(candidate.LocatorValue)
            && TryGetLocator(results, candidate.Parts.LocatorKind, out var resultsLocator)
            && string.Equals(
                candidate.Parts.ResultsLocator.Trim(),
                resultsLocator,
                StringComparison.Ordinal));
    }

    private IEnumerable<RecorderSearchPickerHint> FindExplicitSearchPickerHints(
        TextBox searchInput,
        Control results)
    {
        return _options.SearchPickerHints.Where(candidate =>
            !string.IsNullOrWhiteSpace(candidate.LocatorValue)
            && TryGetLocator(searchInput, candidate.Parts.LocatorKind, out var searchInputLocator)
            && TryGetLocator(results, candidate.Parts.LocatorKind, out var resultsLocator)
            && string.Equals(
                candidate.Parts.SearchInputLocator.Trim(),
                searchInputLocator,
                StringComparison.Ordinal)
            && string.Equals(
                candidate.Parts.ResultsLocator.Trim(),
                resultsLocator,
                StringComparison.Ordinal));
    }

    private static string? ResolveSearchPickerSearchText(
        string? capturedSearchText,
        string? currentSearchText,
        string? selectedText)
    {
        if (!string.IsNullOrWhiteSpace(capturedSearchText))
        {
            return capturedSearchText.Trim();
        }

        if (!string.IsNullOrWhiteSpace(currentSearchText))
        {
            return currentSearchText.Trim();
        }

        return string.IsNullOrWhiteSpace(selectedText) ? null : selectedText.Trim();
    }

    private StepCreationResult TryCreateGridSearchPickerStep(
        TextBox searchInput,
        Control results,
        string? selectedText,
        string? capturedSearchText,
        RecorderGridSearchPickerHint hint)
    {
        var searchText = (capturedSearchText ?? searchInput.Text)?.Trim();
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return StepCreationResult.Unsupported("Grid search picker search text is empty.");
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return StepCreationResult.Unsupported("Grid search picker does not have a selected result to record.");
        }

        if (_gridCapture.ResolveGridSearchPickerContext(searchInput, hint) is not { } context)
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

        return CreateGridStep(
            context.UsesObjectKey ? searchInput : results,
            new RecordedStep(
                RecordedActionKind.SearchAndSelectGridCell,
                descriptor,
                StringValue: searchText,
                Warning: warning,
                RowIndex: context.RowIndex,
                ColumnIndex: context.ColumnIndex,
                ItemValue: selectedText.Trim()),
            warning,
            hint.TargetGridLocatorValue,
            hint.TargetGridLocatorKind,
            context.RowIndex,
            context.ColumnIndex,
            excludeTargetColumnFromIdentity: true);
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
        out RecordedActionKind actionKind,
        out string? buttonLocator,
        out string error,
        out bool isConfigured)
    {
        var matches = new List<(RecorderDialogHint Hint, RecordedActionKind ActionKind, string? ButtonLocator)>();
        foreach (var candidate in _dialogHints)
        {
            var parts = candidate.Parts;
            if (!string.IsNullOrWhiteSpace(parts.ConfirmButtonLocator)
                && MatchesLocator(source, parts.LocatorKind, parts.ConfirmButtonLocator))
            {
                matches.Add((candidate, RecordedActionKind.ConfirmDialog, null));
            }

            if (!string.IsNullOrWhiteSpace(parts.CancelButtonLocator)
                && MatchesLocator(source, parts.LocatorKind, parts.CancelButtonLocator))
            {
                matches.Add((candidate, RecordedActionKind.CancelDialog, null));
            }

            if (!string.IsNullOrWhiteSpace(parts.DismissButtonLocator)
                && MatchesLocator(source, parts.LocatorKind, parts.DismissButtonLocator))
            {
                matches.Add((candidate, RecordedActionKind.DismissDialog, null));
            }

            foreach (var locator in parts.ButtonLocators)
            {
                if (MatchesLocator(source, parts.LocatorKind, locator))
                {
                    matches.Add((candidate, RecordedActionKind.InvokeDialogButton, locator));
                }
            }
        }

        isConfigured = matches.Count > 0;
        if (matches.Count == 1)
        {
            (hint, actionKind, buttonLocator) = matches[0];
            error = string.Empty;
            return true;
        }

        hint = null!;
        actionKind = default;
        buttonLocator = null;
        error = matches.Count == 0
            ? "Recorder does not have a dialog hint for this button."
            : "The selected dialog button matches more than one configured action. Register each dialog button locator exactly once.";
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

    private NotificationTextHintResolution ResolveNotificationTextHint(Control source)
    {
        var relatedControls = EnumerateRelatedControls(source).ToArray();
        var matches = new List<NotificationTextHintResolution>();

        foreach (var candidate in _options.NotificationHints)
        {
            var textControl = relatedControls.FirstOrDefault(control =>
                HasExactLocator(control, candidate.Parts.LocatorKind, candidate.Parts.TextLocator));
            if (textControl is null)
            {
                continue;
            }

            var notificationRoot = EnumerateRelatedControls(textControl)
                .Skip(1)
                .FirstOrDefault(control => HasExactLocator(control, candidate.LocatorKind, candidate.LocatorValue));
            if (notificationRoot is not null)
            {
                matches.Add(NotificationTextHintResolution.Matched(candidate, textControl, notificationRoot));
            }
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            return NotificationTextHintResolution.Invalid(
                $"Notification text belongs to {matches.Count} configured hints; locator configuration must identify one notification.");
        }

        return NotificationTextHintResolution.NotConfigured();
    }

    internal bool IsConfiguredNotificationText(Control source) =>
        ResolveNotificationTextHint(source) is { IsConfigured: true, TextControl: not null };

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
            && _controlReader.TryFindControl(hint.Parts.ActivePaneLabelLocator!, hint.Parts.LocatorKind, out var control))
        {
            return ExtractTextValue(control);
        }

        return null;
    }

    private GridComboSelectionCaptureResult TryCreateGridComboSelectionStepCore(
        Control results,
        string? selectedText,
        GridComboSelectionContextResolution? preparedContext,
        string? capturedSearchText)
    {
        var currentContext = _gridCapture.TryResolveGridComboSelectionContext(results);
        var effectiveContext = preparedContext?.Context is { } prepared
            && IsGridCellContextPart(results, prepared)
                ? preparedContext
                : currentContext.IsConfigured
                    ? currentContext
                    : preparedContext ?? currentContext;
        if (!effectiveContext.IsConfigured)
        {
            return new GridComboSelectionCaptureResult(
                IsConfigured: false,
                HasSelection: false,
                Context: null,
                StepCreationResult.Unsupported("Control is not a selection editor inside a configured grid cell."));
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return new GridComboSelectionCaptureResult(
                IsConfigured: true,
                HasSelection: false,
                effectiveContext.Context,
                StepCreationResult.Unsupported("Grid cell selection editor does not have a selected item to record."));
        }

        if (effectiveContext.Context is not { } context)
        {
            return new GridComboSelectionCaptureResult(
                IsConfigured: true,
                HasSelection: true,
                Context: null,
                StepCreationResult.Unsupported(
                    effectiveContext.Error
                    ?? "Configured grid selection editor does not expose an unambiguous row and column context."));
        }

        var hint = context.GridHint;
        var validationSource = _controlReader.TryFindControl(
                hint.TargetLocatorValue,
                hint.TargetLocatorKind,
                out var logicalGrid)
            ? logicalGrid
            : context.GridSource;
        var descriptor = new RecordedControlDescriptor(
            RecorderNaming.CreateControlPropertyName(hint.TargetLocatorValue, UiControlType.Grid),
            UiControlType.Grid,
            hint.TargetLocatorValue.Trim(),
            hint.TargetLocatorKind,
            hint.FallbackToName,
            validationSource.GetType().FullName ?? validationSource.GetType().Name,
            Warning: null);
        var captureContext = _gridCapture.ResolveSelectionCaptureContext(context, validationSource, out var captureError);
        if (captureContext is null)
            return new GridComboSelectionCaptureResult(true, true, context, StepCreationResult.Unsupported(captureError!));
        var rowIndex = captureContext.RowIndex;
        var isSearchPicker = captureContext.IsSearchPicker;
        var searchText = string.IsNullOrWhiteSpace(capturedSearchText)
            ? selectedText.Trim()
            : capturedSearchText.Trim();
        var prototype = new RecordedStep(
            isSearchPicker
                ? RecordedActionKind.SearchAndSelectGridCell
                : RecordedActionKind.SelectGridCellComboItem,
            descriptor,
            StringValue: isSearchPicker ? searchText : selectedText.Trim(),
            RowIndex: rowIndex,
            ColumnIndex: context.ColumnIndex,
            ItemValue: isSearchPicker ? selectedText.Trim() : null,
            GridCellEditCommitMode: GridCellEditCommitMode.Commit)
        {
            GridTargetColumnName = context.LogicalColumnName
        };
        var result = CreateGridStep(
            captureContext.Source,
            prototype,
            null,
            hint.TargetLocatorValue,
            hint.TargetLocatorKind,
            rowIndex,
            context.ColumnIndex,
            excludeTargetColumnFromIdentity: true);

        return new GridComboSelectionCaptureResult(
            IsConfigured: true,
            HasSelection: true,
            context,
            result);
    }

    private StepCreationResult CreateGridStep(
        Control source,
        RecordedStep step,
        string? warning,
        string targetGridLocatorValue,
        UiLocatorKind targetGridLocatorKind,
        int rowIndex,
        int? columnIndex,
        bool excludeTargetColumnFromIdentity) =>
        CreateResolvedGridStep(source, step, warning,
            _gridCapture.CaptureRow(source, step.GridTargetColumnName, targetGridLocatorValue, targetGridLocatorKind,
                rowIndex, columnIndex, excludeTargetColumnFromIdentity));

    private StepCreationResult CreateCatalogGridStep(
        Control source,
        RecordedStep step,
        string? warning,
        RecorderGridHint hint,
        GridAutomationDefinition definition,
        int rowIndex,
        int? columnIndex,
        bool excludeTargetColumnFromIdentity,
        IReadOnlyList<object?>? captureItems = null) =>
        CreateResolvedGridStep(source, step, warning,
            _gridCapture.CaptureCatalogRow(source, step.GridTargetColumnName, hint, definition,
                rowIndex, columnIndex, excludeTargetColumnFromIdentity, captureItems));

    private StepCreationResult CreateResolvedGridStep(
        Control source, RecordedStep step, string? warning, RecorderGridRowCaptureResult resolution)
    {
        if (resolution.Error is not null)
            return StepCreationResult.Unsupported(resolution.Error);
        if (resolution.Capture is not { } capture)
            return CreateStep(source, step, warning);

        var capturedStep = step with
        {
            GridRowConditions = capture.Conditions,
            GridTargetColumnName = capture.ColumnName
        };
        if (capture.IsCatalog)
        {
            capturedStep = capturedStep with
            {
                GridCapturedRowPosition = capture.ViewPosition,
                CanCaptureGridRowKey = true,
                RowIndex = null,
                ColumnIndex = null
            };
        }
        return CreateStep(source, capturedStep, warning);
    }

    private static RecordedControlDescriptor CreateGridDescriptor(
        GridAutomationDefinition definition,
        Control gridSource)
    {
        return new RecordedControlDescriptor(
            definition.PagePropertyName,
            UiControlType.Grid,
            definition.RuntimeLocatorValue,
            definition.RuntimeLocatorKind,
            definition.RuntimeFallbackToName,
            gridSource.GetType().FullName ?? gridSource.GetType().Name,
            Warning: null);
    }

    private static RecordedStep? TryCreateCatalogGridEditPrototype(
        Control source,
        RecordedControlDescriptor descriptor,
        GridCellEditorKind editorKind,
        RecorderGridCellMetadata metadata,
        string logicalColumnName,
        string? cultureName,
        GridCellEditCommitMode commitMode,
        out string error)
    {
        var common = new RecordedStep(
            RecordedActionKind.EditGridCellText,
            descriptor,
            RowIndex: metadata.RowIndex,
            ColumnIndex: -1,
            GridCellEditCommitMode: commitMode)
        {
            GridTargetColumnName = logicalColumnName
        };

        switch (editorKind)
        {
            case GridCellEditorKind.Text:
                if (FindRelatedControl<TextBox>(source) is not { } textBox)
                {
                    error = $"Grid column '{logicalColumnName}' does not expose a TextBox editor.";
                    return null;
                }

                error = string.Empty;
                return common with
                {
                    ActionKind = RecordedActionKind.EditGridCellText,
                    StringValue = textBox.Text ?? string.Empty
                };

            case GridCellEditorKind.Number:
                var numberValueSource = FindRelatedControl<NumericUpDown>(source) as Control
                    ?? FindRelatedControl<TextBox>(source);
                if (numberValueSource is null
                    || !RecorderNumericValueReader.TryReadNumericValue(
                        numberValueSource,
                        source,
                        cultureName,
                        out var number,
                        out var numericInputText))
                {
                    error = $"Grid column '{logicalColumnName}' does not expose a numeric editor value.";
                    return null;
                }

                error = string.Empty;
                return common with
                {
                    ActionKind = RecordedActionKind.EditGridCellNumber,
                    DoubleValue = number,
                    NumericInputText = numericInputText,
                    NumericInputCultureName = numericInputText is null ? null : cultureName
                };

            case GridCellEditorKind.Date:
                var dateSource = FindRelatedControl<DatePicker>(source) as Control
                    ?? FindRelatedControl<Calendar>(source) as Control
                    ?? FindRelatedControl<TextBox>(source);
                if (dateSource is null || !TryReadDateValue(dateSource, out var date))
                {
                    error = $"Grid column '{logicalColumnName}' does not expose a selected date.";
                    return null;
                }

                error = string.Empty;
                return common with
                {
                    ActionKind = RecordedActionKind.EditGridCellDate,
                    DateValue = date.Date
                };

            case GridCellEditorKind.Time:
                if (FindRelatedControl<TimePicker>(source) is not { SelectedTime: { } time })
                {
                    error = $"Grid column '{logicalColumnName}' does not expose a selected time.";
                    return null;
                }

                error = string.Empty;
                return common with
                {
                    ActionKind = RecordedActionKind.EditGridCellTime,
                    TimeValue = time
                };

            case GridCellEditorKind.ComboBox:
                var selectedText = FindRelatedControl<ComboBox>(source) is { } comboBox
                    ? ExtractSelectionText(comboBox.SelectedItem)
                    : FindRelatedControl<ListBox>(source) is { } listBox
                        ? ExtractSelectionText(listBox.SelectedItem)
                        : null;
                if (string.IsNullOrWhiteSpace(selectedText))
                {
                    error = $"Grid column '{logicalColumnName}' does not expose a selected combo-box item.";
                    return null;
                }

                error = string.Empty;
                return common with
                {
                    ActionKind = RecordedActionKind.SelectGridCellComboItem,
                    StringValue = selectedText.Trim()
                };

            case GridCellEditorKind.CheckBox:
                if (FindRelatedControl<CheckBox>(source) is not { } checkBox)
                {
                    error = $"Grid column '{logicalColumnName}' does not expose a CheckBox editor.";
                    return null;
                }

                error = string.Empty;
                return common with
                {
                    ActionKind = RecordedActionKind.SetGridCellChecked,
                    BoolValue = checkBox.IsChecked == true
                };

            case GridCellEditorKind.Color:
                var colorText = ExtractTextValue(source) ?? metadata.DisplayText;
                if (!ColorValue.TryNormalize(colorText, out var color))
                {
                    error = $"Grid column '{logicalColumnName}' does not expose a valid color value.";
                    return null;
                }

                error = string.Empty;
                return common with
                {
                    ActionKind = RecordedActionKind.EditGridCellColor,
                    StringValue = color
                };

            case GridCellEditorKind.SearchPicker:
                error = $"Grid column '{logicalColumnName}' search selection is completed from its results list.";
                return null;

            default:
                error = $"Grid column '{logicalColumnName}' uses unsupported editor kind '{editorKind}'.";
                return null;
        }
    }

    private static T? FindRelatedControl<T>(Control source)
        where T : Control
    {
        return EnumerateRelatedControls(source).OfType<T>().FirstOrDefault();
    }

    private StepCreationResult TryCreateGridEditTextStep(
        Control source,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridEditHint hint)
    {
        if (!_controlReader.TryFindControl(hint.ValueLocatorValue, hint.ValueLocatorKind, out var valueControl)
            || valueControl is not TextBox textBox)
        {
            return StepCreationResult.Unsupported("Grid text edit hint value locator was not found or is not a TextBox.");
        }

        return CreateGridStep(
            source,
            new RecordedStep(
                RecordedActionKind.EditGridCellText,
                descriptor,
                StringValue: textBox.Text ?? string.Empty,
                Warning: warning,
                RowIndex: hint.RowIndex,
                ColumnIndex: hint.ColumnIndex,
                GridCellEditCommitMode: hint.CommitMode),
            warning,
            hint.TargetGridLocatorValue,
            hint.TargetGridLocatorKind,
            hint.RowIndex,
            hint.ColumnIndex,
            excludeTargetColumnFromIdentity: true);
    }

    private StepCreationResult TryCreateGridEditNumberStep(
        Control source,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridEditHint hint)
    {
        if (!_controlReader.TryFindControl(hint.ValueLocatorValue, hint.ValueLocatorKind, out var valueControl)
            || !RecorderNumericValueReader.TryReadNumericValue(
                valueControl,
                valueControl,
                hint.NumericInputCultureName,
                out var value,
                out var numericInputText))
        {
            return StepCreationResult.Unsupported("Grid numeric edit hint value locator does not expose a numeric value.");
        }

        return CreateGridStep(
            source,
            new RecordedStep(
                RecordedActionKind.EditGridCellNumber,
                descriptor,
                DoubleValue: value,
                Warning: warning,
                RowIndex: hint.RowIndex,
                ColumnIndex: hint.ColumnIndex,
                GridCellEditCommitMode: hint.CommitMode)
            {
                NumericInputText = numericInputText,
                NumericInputCultureName = numericInputText is null
                    ? null
                    : hint.NumericInputCultureName
            },
            warning,
            hint.TargetGridLocatorValue,
            hint.TargetGridLocatorKind,
            hint.RowIndex,
            hint.ColumnIndex,
            excludeTargetColumnFromIdentity: true);
    }

    private StepCreationResult TryCreateGridEditDateStep(
        Control source,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridEditHint hint)
    {
        if (!_controlReader.TryFindControl(hint.ValueLocatorValue, hint.ValueLocatorKind, out var valueControl)
            || !TryReadDateValue(valueControl, out var value))
        {
            return StepCreationResult.Unsupported("Grid date edit hint value locator does not expose a date value.");
        }

        return CreateGridStep(
            source,
            new RecordedStep(
                RecordedActionKind.EditGridCellDate,
                descriptor,
                DateValue: value,
                Warning: warning,
                RowIndex: hint.RowIndex,
                ColumnIndex: hint.ColumnIndex,
                GridCellEditCommitMode: hint.CommitMode),
            warning,
            hint.TargetGridLocatorValue,
            hint.TargetGridLocatorKind,
            hint.RowIndex,
            hint.ColumnIndex,
            excludeTargetColumnFromIdentity: true);
    }

    private SingleSelectCaptureResult TryCreateSingleSelectStepCore(
        Control results,
        SingleSelectResultsKind resultsKind,
        string? selectedText)
    {
        var matchingHints = _options.SingleSelectHints
            .Where(hint => hint.Parts.ResultsKind == resultsKind && IsSingleSelectResults(results, hint))
            .ToArray();
        if (matchingHints.Length == 0)
        {
            return new SingleSelectCaptureResult(
                IsConfigured: false,
                HasSelection: false,
                Hint: null,
                StepCreationResult.Unsupported("Control is not configured as a recorder single-selection result."));
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return new SingleSelectCaptureResult(
                IsConfigured: true,
                HasSelection: false,
                matchingHints.Length == 1 ? matchingHints[0] : null,
                StepCreationResult.Unsupported("Single-selection editor does not have a selected item to record."));
        }

        if (matchingHints.Length > 1)
        {
            return new SingleSelectCaptureResult(
                IsConfigured: true,
                HasSelection: true,
                Hint: null,
                StepCreationResult.Unsupported(
                    $"Single-selection results match {matchingHints.Length} configured hints; ResultsLocator must identify one editor."));
        }

        var hint = matchingHints[0];
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.ComboBox,
            hint.LocatorKind,
            hint.FallbackToName,
            results,
            warning: null);
        var logicalValidation = _selectorResolver.ResolveExisting(descriptor);
        if (!logicalValidation.CanPersist)
        {
            return new SingleSelectCaptureResult(
                IsConfigured: true,
                HasSelection: true,
                hint,
                StepCreationResult.Unsupported(
                    logicalValidation.ValidationMessage
                    ?? $"Single-selection locator '{hint.LocatorKind}:{hint.LocatorValue}' is invalid."));
        }

        var result = CreateStep(
            results,
            new RecordedStep(
                RecordedActionKind.SelectComboItem,
                descriptor,
                StringValue: selectedText.Trim()),
            message: null);

        return new SingleSelectCaptureResult(
            IsConfigured: true,
            HasSelection: true,
            hint,
            result);
    }

    private ColorPickerCaptureResult TryCreateColorPickerStepCore(
        Control palette,
        ColorPaletteKind paletteKind,
        string? selectedColor)
    {
        var matchingHints = _options.ColorPickerHints
            .Where(hint => hint.Parts.PaletteKind == paletteKind
                && !string.IsNullOrWhiteSpace(hint.Parts.PaletteLocator)
                && MatchesLocator(palette, hint.Parts.LocatorKind, hint.Parts.PaletteLocator))
            .ToArray();
        return CreateColorPickerCapture(palette, matchingHints, selectedColor);
    }

    private ColorPickerCaptureResult CreateColorPickerCapture(
        Control source,
        RecorderColorPickerHint[] matchingHints,
        string? color)
    {
        if (matchingHints.Length == 0)
        {
            return new ColorPickerCaptureResult(
                IsConfigured: false,
                HasCandidateValue: false,
                HasColor: false,
                Hint: null,
                StepCreationResult.Unsupported("Control is not configured as a recorder color picker."));
        }

        if (!ColorValue.TryNormalize(color, out var canonical))
        {
            return new ColorPickerCaptureResult(
                IsConfigured: true,
                HasCandidateValue: !string.IsNullOrWhiteSpace(color),
                HasColor: false,
                matchingHints.Length == 1 ? matchingHints[0] : null,
                StepCreationResult.Unsupported(
                    $"Color picker selection '{color}' is not a valid #RRGGBB or #AARRGGBB value."));
        }

        if (matchingHints.Length > 1)
        {
            return new ColorPickerCaptureResult(
                IsConfigured: true,
                HasCandidateValue: true,
                HasColor: true,
                Hint: null,
                StepCreationResult.Unsupported(
                    $"Color picker source matches {matchingHints.Length} configured hints; locators must identify one editor."));
        }

        var hint = matchingHints[0];
        var descriptor = CreateCompositeDescriptor(
            hint.LocatorValue,
            UiControlType.ColorPicker,
            hint.LocatorKind,
            hint.FallbackToName,
            source,
            warning: null);
        var logicalValidation = _selectorResolver.ResolveExisting(descriptor);
        if (!logicalValidation.CanPersist)
        {
            return new ColorPickerCaptureResult(
                IsConfigured: true,
                HasCandidateValue: true,
                HasColor: true,
                hint,
                StepCreationResult.Unsupported(
                    logicalValidation.ValidationMessage
                    ?? $"Color-picker locator '{hint.LocatorKind}:{hint.LocatorValue}' is invalid."));
        }

        var result = CreateStep(
            source,
            new RecordedStep(
                RecordedActionKind.SetColor,
                descriptor,
                StringValue: canonical),
            message: null);
        return new ColorPickerCaptureResult(true, true, true, hint, result);
    }

    private StepCreationResult TryCreateGridEditTimeStep(
        Control source,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridEditHint hint)
    {
        if (!_controlReader.TryFindControl(hint.ValueLocatorValue, hint.ValueLocatorKind, out var valueControl)
            || valueControl is not TimePicker { SelectedTime: { } value })
        {
            return StepCreationResult.Unsupported("Grid time edit hint value locator does not expose a selected time.");
        }

        return CreateGridStep(
            source,
            new RecordedStep(
                RecordedActionKind.EditGridCellTime,
                descriptor,
                Warning: warning,
                RowIndex: hint.RowIndex,
                ColumnIndex: hint.ColumnIndex,
                GridCellEditCommitMode: hint.CommitMode,
                TimeValue: value),
            warning,
            hint.TargetGridLocatorValue,
            hint.TargetGridLocatorKind,
            hint.RowIndex,
            hint.ColumnIndex,
            excludeTargetColumnFromIdentity: true);
    }

    private StepCreationResult TryCreateGridEditColorStep(
        Control source,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridEditHint hint)
    {
        if (!_controlReader.TryFindControl(hint.ValueLocatorValue, hint.ValueLocatorKind, out var valueControl)
            || !ColorValue.TryNormalize(ExtractTextValue(valueControl), out var color))
        {
            return StepCreationResult.Unsupported(
                "Grid color edit hint value locator does not expose a valid #RRGGBB or #AARRGGBB value.");
        }

        return CreateGridStep(
            source,
            new RecordedStep(
                RecordedActionKind.EditGridCellColor,
                descriptor,
                StringValue: color,
                Warning: warning,
                RowIndex: hint.RowIndex,
                ColumnIndex: hint.ColumnIndex,
                GridCellEditCommitMode: hint.CommitMode),
            warning,
            hint.TargetGridLocatorValue,
            hint.TargetGridLocatorKind,
            hint.RowIndex,
            hint.ColumnIndex,
            excludeTargetColumnFromIdentity: true);
    }

    private StepCreationResult TryCreateGridEditComboStep(
        Control source,
        RecordedControlDescriptor descriptor,
        string warning,
        RecorderGridEditHint hint)
    {
        if (!_controlReader.TryFindControl(hint.ValueLocatorValue, hint.ValueLocatorKind, out var valueControl))
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

        return CreateGridStep(
            source,
            new RecordedStep(
                RecordedActionKind.SelectGridCellComboItem,
                descriptor,
                StringValue: selectedText.Trim(),
                Warning: warning,
                RowIndex: hint.RowIndex,
                ColumnIndex: hint.ColumnIndex,
                GridCellEditCommitMode: hint.CommitMode),
            warning,
            hint.TargetGridLocatorValue,
            hint.TargetGridLocatorKind,
            hint.RowIndex,
            hint.ColumnIndex,
            excludeTargetColumnFromIdentity: true);
    }

    private bool TryReadDateRangeValues(
        DateRangeFilterParts parts,
        out DateTime? from,
        out DateTime? to,
        out string message)
    {
        if (!_controlReader.TryFindControl(parts.FromLocator, parts.LocatorKind, out var fromControl)
            || !_controlReader.TryFindControl(parts.ToLocator, parts.LocatorKind, out var toControl))
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
        if (!_controlReader.TryFindControl(parts.FromLocator, parts.LocatorKind, out var fromControl)
            || !_controlReader.TryFindControl(parts.ToLocator, parts.LocatorKind, out var toControl))
        {
            from = null;
            to = null;
            message = "Numeric range filter endpoints were not found.";
            return false;
        }

        var hasFrom = RecorderNumericValueReader.TryReadNumericValue(fromControl, out var fromValue);
        var hasTo = RecorderNumericValueReader.TryReadNumericValue(toControl, out var toValue);
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

    private bool TryReadSelectionValues(
        Control source,
        MultiSelectParts parts,
        string controlDescription,
        out IReadOnlyList<string> selectedValues,
        out string message)
    {
        var editorRoot = FindFromRelatedControlTrees(source, parts.RootLocator, parts.LocatorKind)
            ?? FindFromValidationRoot(parts.RootLocator, parts.LocatorKind);
        if (editorRoot is null)
        {
            selectedValues = [];
            message = $"Recorder could not resolve {controlDescription} editor root '{parts.RootLocator}'.";
            return false;
        }

        var itemsContainer = FindFromRelatedControlTrees(
                source,
                parts.ItemsContainerLocator,
                parts.LocatorKind)
            ?? FindFromValidationRoot(parts.ItemsContainerLocator, parts.LocatorKind);
        if (itemsContainer is null)
        {
            selectedValues = [];
            message = $"Recorder could not resolve {controlDescription} items container '{parts.ItemsContainerLocator}'.";
            return false;
        }

        if (!TryReadSelectionItemSnapshots(itemsContainer, controlDescription, out var items, out message))
        {
            selectedValues = [];
            return false;
        }

        if (items.Any(static item => item.IsSelected && string.IsNullOrWhiteSpace(item.Text)))
        {
            selectedValues = [];
            message = $"Recorder {controlDescription} item does not expose a readable UI caption. "
                + "Expose visible text or AutomationProperties.Name on the item.";
            return false;
        }

        var selected = items
            .Where(static item => item.IsSelected)
            .Select(static item => item.Text)
            .ToArray();
        if (selected.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length)
        {
            selectedValues = [];
            message = $"Recorder {controlDescription} action contains duplicate selected item text.";
            return false;
        }

        selectedValues = selected;
        message = string.Empty;
        return true;
    }

    private static bool TryReadSelectionItemSnapshots(
        Control itemsContainer,
        string controlDescription,
        out IReadOnlyList<SelectionItemSnapshot> items,
        out string message)
    {
        var visibleItems = ReadSelectionItemSnapshots(itemsContainer);
        if (itemsContainer is not ItemsControl itemsControl
            || itemsControl.ItemCount <= visibleItems.Count)
        {
            items = visibleItems;
            message = string.Empty;
            return true;
        }

        var allItems = new Dictionary<string, SelectionItemSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var index in GetSelectionTraversalIndexes(itemsControl))
        {
            try
            {
                itemsControl.ScrollIntoView(index);
                itemsControl.UpdateLayout();
            }
            catch (Exception exception)
            {
                items = [];
                message =
                    $"Recorder could not scroll {controlDescription} item at index {index}: {exception.Message}";
                return false;
            }

            var itemContainer = itemsControl.ContainerFromIndex(index);
            if (itemContainer is null)
            {
                items = [];
                message =
                    $"Recorder could not realize {controlDescription} item at index {index} while traversing the scrollable list.";
                return false;
            }

            var itemSnapshots = ReadSelectionItemSnapshots(itemContainer);
            if (itemSnapshots.Count == 0)
            {
                items = [];
                message =
                    $"Recorder could not resolve a selectable {controlDescription} item at index {index}.";
                return false;
            }

            foreach (var item in itemSnapshots)
            {
                if (!allItems.TryAdd(item.Text, item))
                {
                    items = [];
                    message = $"Recorder {controlDescription} action contains duplicate item text.";
                    return false;
                }
            }
        }

        items = allItems.Values.ToArray();
        message = string.Empty;
        return true;
    }

    private static IEnumerable<int> GetSelectionTraversalIndexes(ItemsControl itemsControl)
    {
        var realizedIndexes = Enumerable
            .Range(0, itemsControl.ItemCount)
            .Where(index => itemsControl.ContainerFromIndex(index) is not null)
            .ToArray();
        var distanceToStart = realizedIndexes.Length == 0
            ? 0
            : realizedIndexes[0];
        var distanceToEnd = realizedIndexes.Length == 0
            ? itemsControl.ItemCount - 1
            : itemsControl.ItemCount - 1 - realizedIndexes[^1];

        return distanceToStart <= distanceToEnd
            ? Enumerable.Range(0, itemsControl.ItemCount)
            : Enumerable.Range(0, itemsControl.ItemCount)
                .Select(index => itemsControl.ItemCount - 1 - index);
    }

    private static IReadOnlyList<SelectionItemSnapshot> ReadSelectionItemSnapshots(Control root)
    {
        var checkBoxItems = EnumerateDescendantControls(root)
            .OfType<CheckBox>()
            .Select(checkBox => new SelectionItemSnapshot(
                ReadSelectionItemText(checkBox),
                checkBox.IsChecked == true))
            .Where(static item => item.IsSelected || !string.IsNullOrWhiteSpace(item.Text))
            .ToArray();
        if (checkBoxItems.Length > 0)
        {
            return checkBoxItems;
        }

        var listBox = EnumerateDescendantControls(root).OfType<ListBox>().FirstOrDefault();
        if (listBox is not null)
        {
            var selectedItems = (listBox.SelectedItems?.Cast<object?>()
                    ?? (listBox.SelectedItem is null ? [] : [listBox.SelectedItem]))
                .ToArray();
            return listBox.Items
                .Select((item, index) => new SelectionItemSnapshot(
                    ReadRenderedSelectionCaption(listBox.ContainerFromIndex(index))
                        ?? ReadSelectionDataCaption(item)
                        ?? string.Empty,
                    selectedItems.Any(selected => ReferenceEquals(selected, item) || Equals(selected, item))))
                .Where(static item => item.IsSelected || !string.IsNullOrWhiteSpace(item.Text))
                .ToArray();
        }

        var comboBox = EnumerateDescendantControls(root).OfType<ComboBox>().FirstOrDefault();
        if (comboBox is null)
        {
            return [];
        }

        return comboBox.Items
            .Select((item, index) => new SelectionItemSnapshot(
                ReadRenderedSelectionCaption(comboBox.ContainerFromIndex(index))
                    ?? ReadSelectionDataCaption(item)
                    ?? string.Empty,
                ReferenceEquals(comboBox.SelectedItem, item) || Equals(comboBox.SelectedItem, item)))
            .Where(static item => item.IsSelected || !string.IsNullOrWhiteSpace(item.Text))
            .ToArray();
    }

    private static Control? FindFromRelatedControlTrees(
        Control source,
        string locatorValue,
        UiLocatorKind locatorKind)
    {
        foreach (var relatedControl in EnumerateRelatedControls(source))
        {
            var match = EnumerateDescendantControls(relatedControl)
                .FirstOrDefault(candidate => HasExactLocator(candidate, locatorKind, locatorValue));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private Control? FindFromValidationRoot(string locatorValue, UiLocatorKind locatorKind)
    {
        return _validationRootProvider?.Invoke() is { } root
            ? EnumerateDescendantControls(root)
                .FirstOrDefault(candidate => HasExactLocator(candidate, locatorKind, locatorValue))
            : null;
    }

    private static string ReadSelectionItemText(CheckBox checkBox)
    {
        return (ReadRenderedSelectionCaption(checkBox)
                ?? ReadSelectionDataCaption(checkBox.Content)
                ?? string.Empty)
            .Trim();
    }

    private static string? ReadSelectionDataCaption(object? item)
    {
        if (item is null)
        {
            return null;
        }

        if (item is string or Control || item.GetType().IsEnum)
        {
            return ExtractSelectionText(item);
        }

        foreach (var propertyName in new[] { "Header", "Title", "Text", "Name" })
        {
            if (TryReadPropertyValue(item, propertyName, out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? ReadRenderedSelectionCaption(Control? control)
    {
        if (control is null)
        {
            return null;
        }

        var text = string.Join(" ", control.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(static block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text))
            .Select(static block => block.Text!.Trim()));
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return AutomationProperties.GetName(control) switch
        {
            { } name when !string.IsNullOrWhiteSpace(name) => name.Trim(),
            _ => control is ContentControl { Content: string caption } && !string.IsNullOrWhiteSpace(caption)
                ? caption.Trim()
                : null
        };
    }

    private sealed record SelectionItemSnapshot(string Text, bool IsSelected);

    private IEnumerable<(RecorderComboBoxFilterHint Hint, RecordedActionKind ActionKind)> FindComboBoxFilterActions(
        Control source)
    {
        foreach (var hint in _options.ComboBoxFilterHints)
        {
            if (!string.IsNullOrWhiteSpace(hint.Parts.ApplyButtonLocator)
                && MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.ApplyButtonLocator))
            {
                yield return (hint, RecordedActionKind.ApplyFilterSelection);
            }

            if (!string.IsNullOrWhiteSpace(hint.Parts.CancelButtonLocator)
                && MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.CancelButtonLocator))
            {
                yield return (hint, RecordedActionKind.CancelFilterSelection);
            }

            if (string.IsNullOrWhiteSpace(hint.Parts.ApplyButtonLocator)
                && MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.ItemsContainerLocator))
            {
                yield return (hint, RecordedActionKind.ApplyFilterSelection);
            }
        }
    }

    private IEnumerable<(RecorderMultiSelectHint Hint, RecordedActionKind ActionKind)> FindMultiSelectActions(
        Control source)
    {
        foreach (var hint in _options.MultiSelectHints)
        {
            if (!string.IsNullOrWhiteSpace(hint.Parts.ApplyButtonLocator)
                && MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.ApplyButtonLocator))
            {
                yield return (hint, RecordedActionKind.SelectMultiItems);
            }

            if (!string.IsNullOrWhiteSpace(hint.Parts.CancelButtonLocator)
                && MatchesLocator(source, hint.Parts.LocatorKind, hint.Parts.CancelButtonLocator))
            {
                yield return (hint, RecordedActionKind.CancelMultiSelection);
            }
        }
    }

    private static MultiSelectParts ToMultiSelectParts(ComboBoxFilterParts parts)
    {
        return new MultiSelectParts(
            parts.RootLocator,
            parts.OpenButtonLocator,
            parts.ItemsContainerLocator,
            parts.ApplyButtonLocator,
            parts.CancelButtonLocator,
            parts.LocatorKind,
            parts.FallbackToName,
            parts.ItemsKind);
    }

    private bool MatchesTimePickerInputPart(TextBox textBox)
    {
        return _options.TimePickerHints.Any(hint => IsTimePickerInput(textBox, hint));
    }

    private bool MatchesDatePickerValuePart(TextBox textBox)
    {
        return _options.DatePickerHints.Any(hint =>
            MatchesLocator(textBox, hint.Parts.LocatorKind, hint.Parts.ValueLocator));
    }

    private IEnumerable<RecorderTimePickerHint> FindTimePickerHints(TimePicker timePicker)
    {
        return _options.TimePickerHints.Where(hint =>
            !RecorderTimePickerBinding.IsPresenterHint(hint)
            && MatchesLocator(timePicker, hint.Parts.LocatorKind, hint.Parts.TimePickerLocator));
    }

    private IEnumerable<RecorderDatePickerHint> FindDatePickerHints(Control source)
    {
        return _options.DatePickerHints.Where(hint => IsDatePickerPart(source, hint));
    }

    private static bool IsDatePickerPart(Control source, RecorderDatePickerHint hint)
    {
        return RecorderDatePickerHintMatcher.IsPart(source, hint);
    }

    private static bool IsSingleSelectResults(Control results, RecorderSingleSelectHint hint)
    {
        return (hint.Parts.ResultsKind switch
        {
            SingleSelectResultsKind.ComboBox => results is ComboBox,
            SingleSelectResultsKind.ListBox => results is ListBox,
            _ => false
        })
            && MatchesLocator(results, hint.Parts.LocatorKind, hint.Parts.ResultsLocator);
    }

    private static bool MatchesAnyLocator(Control source, UiLocatorKind locatorKind, params string?[] locatorValues)
    {
        return locatorValues.Any(locatorValue => !string.IsNullOrWhiteSpace(locatorValue) && MatchesLocator(source, locatorKind, locatorValue!));
    }

    private static bool TryBuildMenuPath(
        MenuItem leaf,
        out Menu menu,
        out IReadOnlyList<string> path,
        out string message)
    {
        var items = new List<MenuItem>();
        Control? current = leaf;
        menu = null!;
        while (current is not null)
        {
            if (current is MenuItem item)
            {
                items.Add(item);
            }
            else if (current is Menu owner)
            {
                menu = owner;
                break;
            }

            current = GetMenuParent(current);
        }

        if (menu is null || items.Count == 0)
        {
            path = [];
            message = "Recorder could not find the owning menu for this menu item.";
            return false;
        }

        items.Reverse();
        var captions = new string[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var caption = ReadMenuCaption(items[index]);
            if (string.IsNullOrWhiteSpace(caption))
            {
                path = [];
                message = "A menu item in the selected path does not expose visible caption text.";
                return false;
            }

            captions[index] = caption;
            var siblings = index == 0
                ? menu.Items.OfType<MenuItem>()
                : items[index - 1].Items.OfType<MenuItem>();
            var duplicateCount = siblings.Count(sibling =>
                string.Equals(ReadMenuCaption(sibling), caption, StringComparison.Ordinal));
            if (duplicateCount > 1)
            {
                path = [];
                message = $"Menu item caption '{caption}' is ambiguous among siblings ({duplicateCount} matches).";
                return false;
            }
        }

        path = captions;
        message = string.Empty;
        return true;
    }

    private static IEnumerable<MenuItem[]> EnumerateContextMenuItemRoots(Control owner)
    {
        if (owner.ContextMenu is { } contextMenu)
        {
            yield return contextMenu.Items.OfType<MenuItem>().ToArray();
        }

        if (owner.ContextFlyout is MenuFlyout menuFlyout)
        {
            yield return menuFlyout.Items.OfType<MenuItem>().ToArray();
        }
    }

    private static bool TryFindMenuItemPath(
        IReadOnlyList<MenuItem> items,
        MenuItem target,
        out IReadOnlyList<MenuItem> path)
    {
        foreach (var item in items)
        {
            if (ReferenceEquals(item, target))
            {
                path = [item];
                return true;
            }

            if (TryFindMenuItemPath(item.Items.OfType<MenuItem>().ToArray(), target, out var childPath))
            {
                path = new[] { item }.Concat(childPath).ToArray();
                return true;
            }
        }

        path = [];
        return false;
    }

    private static bool TryValidateMenuPath(
        IReadOnlyList<MenuItem> rootItems,
        IReadOnlyList<MenuItem> itemPath,
        out IReadOnlyList<string> captions,
        out string message)
    {
        var values = new string[itemPath.Count];
        IReadOnlyList<MenuItem> siblings = rootItems;
        for (var index = 0; index < itemPath.Count; index++)
        {
            var caption = ReadMenuCaption(itemPath[index]);
            if (string.IsNullOrWhiteSpace(caption))
            {
                captions = [];
                message = "A context-menu item in the selected path does not expose visible caption text.";
                return false;
            }

            var duplicateCount = siblings.Count(sibling =>
                string.Equals(ReadMenuCaption(sibling), caption, StringComparison.Ordinal));
            if (duplicateCount > 1)
            {
                captions = [];
                message =
                    $"Context-menu item caption '{caption}' is ambiguous among siblings ({duplicateCount} matches).";
                return false;
            }

            values[index] = caption;
            siblings = itemPath[index].Items.OfType<MenuItem>().ToArray();
        }

        captions = values;
        message = string.Empty;
        return true;
    }

    private static bool IsDirectMenuItem(MenuItem item)
    {
        return GetMenuParent(item) is Menu;
    }

    private static Control? GetMenuParent(Control control)
    {
        if (control is ILogical { LogicalParent: Control logicalParent })
        {
            return logicalParent;
        }

        return control.GetVisualParent() as Control;
    }

    private static string ReadMenuCaption(MenuItem item)
    {
        return MenuPathValue.TryGetVisibleCaption(item.Header, AutomationProperties.GetName(item))
            ?? string.Empty;
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
            new TimePickerAssertionExtractor(options),
            new ExpanderAssertionExtractor(),
            new SpinnerAssertionExtractor(options),
            new TextAssertionExtractor(),
            new CheckedAssertionExtractor(),
            new EnabledAssertionExtractor(),
            new ExistsAssertionExtractor(),
            .. options.AssertionExtractors
        ];
    }

    private sealed class SpinnerAssertionExtractor : IRecorderAssertionExtractor
    {
        private readonly AppAutomationRecorderOptions _options;

        public SpinnerAssertionExtractor(AppAutomationRecorderOptions options)
        {
            _options = options;
        }

        public bool TryCreate(Control control, RecorderAssertionMode mode, out RecorderAssertionCandidate? candidate)
        {
            candidate = null;
            if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Text))
            {
                return false;
            }

            double? value = control switch
            {
                NumericUpDown { Value: { } numericValue } => decimal.ToDouble(numericValue),
                TextBox textBox when RecorderSpinnerProxyConfiguration.IsInteractivePart(_options, textBox)
                    && double.TryParse(
                        textBox.Text?.Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsedValue) => parsedValue,
                _ => null
            };

            if (value is null)
            {
                return false;
            }

            candidate = new RecorderAssertionCandidate(
                UiControlType.Spinner,
                RecordedActionKind.WaitUntilValueEquals,
                DoubleValue: value);
            return true;
        }
    }

    private sealed class ExpanderAssertionExtractor : IRecorderAssertionExtractor
    {
        public bool TryCreate(Control control, RecorderAssertionMode mode, out RecorderAssertionCandidate? candidate)
        {
            candidate = null;
            if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Checked)
                || control is not Expander expander)
            {
                return false;
            }

            candidate = new RecorderAssertionCandidate(
                UiControlType.Expander,
                RecordedActionKind.WaitUntilIsExpanded,
                BoolValue: expander.IsExpanded);
            return true;
        }
    }

    private sealed class TimePickerAssertionExtractor : IRecorderAssertionExtractor
    {
        private readonly AppAutomationRecorderOptions _options;

        public TimePickerAssertionExtractor(AppAutomationRecorderOptions options)
        {
            _options = options;
        }

        public bool TryCreate(Control control, RecorderAssertionMode mode, out RecorderAssertionCandidate? candidate)
        {
            candidate = null;
            if (mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Text))
            {
                return false;
            }

            var selectedTime = control switch
            {
                TimePicker { SelectedTime: { } value } => value,
                TextBox textBox when MatchesConfiguredInput(textBox)
                    && TryParseTime(textBox.Text, out var value) => value,
                _ => (TimeSpan?)null
            };
            if (selectedTime is null)
            {
                return false;
            }

            candidate = new RecorderAssertionCandidate(
                UiControlType.TimePicker,
                RecordedActionKind.WaitUntilTimeEquals)
            {
                TimeValue = selectedTime.Value
            };
            return true;
        }

        private bool MatchesConfiguredInput(TextBox textBox)
        {
            return _options.TimePickerHints.Any(hint =>
                !string.IsNullOrWhiteSpace(hint.Parts.InputLocator)
                && MatchesLocator(textBox, hint.Parts.LocatorKind, hint.Parts.InputLocator));
        }

        private static bool TryParseTime(string? text, out TimeSpan value)
        {
            return TimeSpan.TryParse(text?.Trim(), System.Globalization.CultureInfo.CurrentCulture, out value)
                || TimeSpan.TryParse(text?.Trim(), System.Globalization.CultureInfo.InvariantCulture, out value);
        }
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

    private sealed record NotificationTextHintResolution(
        bool IsConfigured,
        RecorderNotificationHint? Hint,
        Control? TextControl,
        Control? NotificationRoot,
        string? Error)
    {
        public bool Success => Hint is not null && TextControl is not null && NotificationRoot is not null;

        public static NotificationTextHintResolution NotConfigured()
        {
            return new NotificationTextHintResolution(false, null, null, null, null);
        }

        public static NotificationTextHintResolution Matched(
            RecorderNotificationHint hint,
            Control textControl,
            Control notificationRoot)
        {
            return new NotificationTextHintResolution(true, hint, textControl, notificationRoot, null);
        }

        public static NotificationTextHintResolution Invalid(string error)
        {
            return new NotificationTextHintResolution(true, null, null, null, error);
        }
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

    private sealed class ExistsAssertionExtractor : IRecorderAssertionExtractor
    {
        public bool TryCreate(Control control, RecorderAssertionMode mode, out RecorderAssertionCandidate? candidate)
        {
            candidate = null;
            if (mode is not RecorderAssertionMode.Exists)
            {
                return false;
            }

            candidate = new RecorderAssertionCandidate(
                ClassifyControlType(control),
                RecordedActionKind.WaitUntilExists);
            return true;
        }
    }
}
