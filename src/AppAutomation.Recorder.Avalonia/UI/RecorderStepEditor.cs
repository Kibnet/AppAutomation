using System.Globalization;
using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AppAutomation.Recorder.Avalonia.UI;

internal sealed class RecorderStepEditor : Border
{
    private static readonly bool[] BooleanOptions = [true, false];
    private static readonly string[] GridRowSourceOptions = ["Current table row", "Recorded stable key"];

    private readonly RecorderStepEditDraft _sourceDraft;
    private readonly TextBlock _validation;
    private readonly TextBlock _codePreview;
    private readonly Button _applyButton;
    private TextBox? _variableName;
    private TextBox? _stringValue;
    private TextBox? _itemValue;
    private TextBox? _integerValue;
    private TextBox? _numberValue;
    private TextBox? _secondNumberValue;
    private ComboBox? _booleanValue;
    private TextBox? _dateValue;
    private TextBox? _secondDateValue;
    private TextBox? _timeValue;
    private TextBox? _stringValues;
    private ComboBox? _comparison;
    private ComboBox? _expectedSource;
    private TextBox? _expectedLiteral;
    private ComboBox? _expectedCheckpoint;
    private ComboBox? _expectedGeneratedValue;
    private StackPanel? _calculatedFields;
    private ComboBox? _calculatedOperation;
    private NumericOperandFields? _leftOperand;
    private NumericOperandFields? _rightOperand;
    private ComboBox? _gridRowMode;
    private readonly List<TextBox> _additionalTextInputs = [];
    private readonly List<ComboBox> _additionalComboInputs = [];
    private readonly List<GridRowConditionFields> _gridRowConditionFields = [];

    public RecorderStepEditor(
        RecorderStepEditDraft draft,
        IBrush textBrush,
        IBrush mutedBrush,
        IBrush dangerBrush,
        Func<RecorderStepEditDraft, RecorderStepEditPreviewResult> preview,
        Action<RecorderStepEditDraft> apply,
        Action cancel,
        Action<RecorderStepRetargetRole> retarget)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(cancel);
        ArgumentNullException.ThrowIfNull(retarget);

        _sourceDraft = draft;
        Name = "RecorderStepEditor";
        BorderThickness = new Thickness(1, 0, 0, 0);
        BorderBrush = mutedBrush;
        Padding = new Thickness(10, 8, 4, 4);

        var fields = new StackPanel { Spacing = 7 };
        fields.Children.Add(new TextBlock
        {
            Text = $"Edit {DescribeKind(draft.EditKind)}",
            FontWeight = FontWeight.SemiBold,
            Foreground = textBrush
        });

        if (draft.EditKind is RecorderStepEditKind.Checkpoint
            or RecorderStepEditKind.GeneratedValue
            or RecorderStepEditKind.CopiedValue)
        {
            _variableName = AddTextField(fields, "Variable name", "RecorderStepEditVariableName", draft.VariableName);
        }

        if (draft.EditKind == RecorderStepEditKind.Checkpoint)
        {
            AddTargetField(fields, "Value source", draft.ControlName, "Change source", () => retarget(RecorderStepRetargetRole.ValueSource));
            fields.Children.Add(new TextBlock
            {
                Name = "RecorderStepEditCurrentPreview",
                Text = $"Current preview: {draft.CurrentPreview}",
                Foreground = mutedBrush,
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (draft.EditKind == RecorderStepEditKind.Assertion)
        {
            AddTargetField(fields, "Actual target", draft.ControlName, "Change target", () => retarget(RecorderStepRetargetRole.AssertionTarget));
            BuildAssertionFields(fields, draft, retarget);
        }
        else if (draft.EditKind is not (RecorderStepEditKind.GeneratedValue or RecorderStepEditKind.CopiedValue))
        {
            BuildPayloadFields(fields, draft);
        }

        if (draft.GridRowConditions is { Count: > 0 })
        {
            BuildGridRowFields(fields, draft);
        }

        _codePreview = new TextBlock
        {
            Name = "RecorderStepEditCodePreview",
            Text = draft.GeneratedPreview,
            FontFamily = "Cascadia Mono, Consolas",
            Foreground = mutedBrush,
            TextWrapping = TextWrapping.Wrap
        };
        fields.Children.Add(_codePreview);

        _validation = new TextBlock
        {
            Name = "RecorderStepEditValidation",
            Foreground = dangerBrush,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        fields.Children.Add(_validation);

        _applyButton = new Button
        {
            Name = "RecorderStepEditApply",
            Content = "Apply",
            Padding = new Thickness(10, 4)
        };
        var cancelButton = new Button
        {
            Name = "RecorderStepEditCancel",
            Content = "Cancel",
            Padding = new Thickness(10, 4)
        };
        _applyButton.Click += (_, _) =>
        {
            if (!TryBuildDraft(out var updatedDraft, out var error))
            {
                ShowValidation(error);
                return;
            }

            apply(updatedDraft!);
        };
        cancelButton.Click += (_, _) => cancel();
        fields.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Children = { _applyButton, cancelButton }
        });

        Child = fields;
        WireLivePreview(preview);
    }

    public void ShowValidation(string message)
    {
        _validation.Text = message;
        _validation.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    public bool TryBuildDraftForRetarget(
        RecorderStepRetargetRole role,
        out RecorderStepEditDraft? draft,
        out string error) =>
        TryBuildDraft(out draft, out error, role);

    private void WireLivePreview(Func<RecorderStepEditDraft, RecorderStepEditPreviewResult> preview)
    {
        var deferredPreview = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };

        void RefreshPreview()
        {
            deferredPreview.Stop();
            if (!TryBuildDraft(out var draft, out var error) || draft is null)
            {
                ShowValidation(error);
                _applyButton.IsEnabled = false;
                return;
            }

            var result = preview(draft);
            if (!result.Success)
            {
                ShowValidation(result.Message);
                _applyButton.IsEnabled = false;
                return;
            }

            _codePreview.Text = result.Preview;
            ShowValidation(string.Empty);
            _applyButton.IsEnabled = true;
        }

        void SchedulePreview()
        {
            deferredPreview.Stop();
            if (!TryBuildDraft(out _, out var error))
            {
                ShowValidation(error);
                _applyButton.IsEnabled = false;
                return;
            }

            ShowValidation(string.Empty);
            _applyButton.IsEnabled = true;
            deferredPreview.Start();
        }

        deferredPreview.Tick += (_, _) => RefreshPreview();
        DetachedFromVisualTree += (_, _) => deferredPreview.Stop();

        foreach (var input in new[]
                 {
                     _variableName,
                     _stringValue,
                     _itemValue,
                     _integerValue,
                     _numberValue,
                     _secondNumberValue,
                     _dateValue,
                     _secondDateValue,
                     _timeValue,
                     _stringValues,
                     _expectedLiteral
                 }.Where(static input => input is not null))
        {
            input!.TextChanged += (_, _) => SchedulePreview();
        }

        foreach (var input in _additionalTextInputs)
        {
            input.TextChanged += (_, _) => SchedulePreview();
        }

        foreach (var input in new[]
                 {
                     _booleanValue,
                     _comparison,
                     _expectedSource,
                     _expectedCheckpoint,
                     _expectedGeneratedValue
                 }.Where(static input => input is not null))
        {
            input!.SelectionChanged += (_, _) => RefreshPreview();
        }

        foreach (var input in _additionalComboInputs)
        {
            input.SelectionChanged += (_, _) => RefreshPreview();
        }

        RefreshPreview();
    }

    private void BuildAssertionFields(
        StackPanel fields,
        RecorderStepEditDraft draft,
        Action<RecorderStepRetargetRole> retarget)
    {
        var comparisons = Enum.GetValues<RecorderComparisonKind>()
            .Where(comparison => draft.ValueKind is { } valueKind
                && RecorderScenarioGraphValidator.SupportsComparison(valueKind, comparison))
            .ToArray();
        _comparison = AddComboField(
            fields,
            "Comparison",
            "RecorderStepEditComparison",
            comparisons,
            draft.ComparisonKind);

        var selectedComparison = _comparison.SelectedItem is RecorderComparisonKind comparison
            ? comparison
            : comparisons[0];
        var sources = RecorderStepEditService.GetExpectedSources(draft, selectedComparison);

        _expectedSource = AddComboField(
            fields,
            "Expected source",
            "RecorderStepEditExpectedSource",
            sources,
            draft.ExpectedSource == RecorderExpectedValueSourceKind.None
                ? RecorderExpectedValueSourceKind.Literal
                : draft.ExpectedSource);
        _expectedLiteral = AddTextField(
            fields,
            "Expected value",
            "RecorderStepEditExpectedLiteral",
            FormatLiteral(draft));
        _expectedCheckpoint = AddOptionField(
            fields,
            "Checkpoint",
            "RecorderStepEditExpectedCheckpoint",
            draft.CompatibleCheckpoints.Select(option => new RecorderStepEditOption(option.VariableName, option.CheckpointId)).ToArray(),
            draft.ExpectedCheckpointId);
        _expectedGeneratedValue = AddOptionField(
            fields,
            "Generated value",
            "RecorderStepEditExpectedGeneratedValue",
            draft.CompatibleGeneratedValues.Select(option => new RecorderStepEditOption(option.VariableName, option.GeneratedValueId)).ToArray(),
            draft.ExpectedGeneratedValueId);
        if (draft.ValueKind == RecorderValueKind.Number)
        {
            _calculatedFields = BuildCalculatedFields(draft, retarget);
            fields.Children.Add(_calculatedFields);
        }

        _comparison.SelectionChanged += (_, _) =>
        {
            RefreshExpectedSourceOptions();
            RefreshAssertionFieldVisibility();
        };
        _expectedSource.SelectionChanged += (_, _) => RefreshAssertionFieldVisibility();
        RefreshAssertionFieldVisibility();
    }

    private StackPanel BuildCalculatedFields(
        RecorderStepEditDraft draft,
        Action<RecorderStepRetargetRole> retarget)
    {
        var expression = draft.NumericExpectedExpression
            ?? new RecorderNumericExpectedExpression(
                RecorderArithmeticOperation.Add,
                RecorderNumericOperand.FromLiteral(0),
                RecorderNumericOperand.FromLiteral(0));
        _calculatedOperation = new ComboBox
        {
            Name = "RecorderStepEditCalculatedOperation",
            ItemsSource = Enum.GetValues<RecorderArithmeticOperation>(),
            SelectedItem = expression.Operation,
            MinWidth = 180
        };
        _additionalComboInputs.Add(_calculatedOperation);
        _leftOperand = BuildNumericOperandFields(
            "Left operand",
            "RecorderStepEditCalculatedLeft",
            expression.Left,
            draft.CompatibleCheckpoints,
            () => retarget(RecorderStepRetargetRole.CalculatedLeftOperand));
        _rightOperand = BuildNumericOperandFields(
            "Right operand",
            "RecorderStepEditCalculatedRight",
            expression.Right,
            draft.CompatibleCheckpoints,
            () => retarget(RecorderStepRetargetRole.CalculatedRightOperand));
        return new StackPanel
        {
            Spacing = 5,
            Children =
            {
                _leftOperand.Content,
                CreateField("Operation", _calculatedOperation),
                _rightOperand.Content
            }
        };
    }

    private NumericOperandFields BuildNumericOperandFields(
        string label,
        string namePrefix,
        RecorderNumericOperand operand,
        IReadOnlyList<RecorderCheckpointOption> checkpoints,
        Action selectControl)
    {
        var numericCheckpoints = checkpoints
            .Where(static checkpoint => checkpoint.ValueKind == RecorderValueKind.Number)
            .Select(static checkpoint => new RecorderStepEditOption(checkpoint.VariableName, checkpoint.CheckpointId))
            .ToArray();
        var source = new ComboBox
        {
            Name = $"{namePrefix}Source",
            ItemsSource = Enum.GetValues<RecorderNumericOperandKind>(),
            SelectedItem = operand.Kind,
            MinWidth = 180
        };
        var literal = new TextBox
        {
            Name = $"{namePrefix}Literal",
            Text = operand.LiteralValue?.ToString("R", CultureInfo.InvariantCulture) ?? "0",
            MinWidth = 180
        };
        var selectedCheckpoint = operand.CheckpointId is { } checkpointId
            ? numericCheckpoints.FirstOrDefault(option => option.Id == checkpointId)
            : null;
        var checkpoint = new ComboBox
        {
            Name = $"{namePrefix}Checkpoint",
            ItemsSource = numericCheckpoints,
            SelectedItem = selectedCheckpoint,
            MinWidth = 220
        };
        if (checkpoint.SelectedIndex < 0 && numericCheckpoints.Length > 0)
        {
            checkpoint.SelectedIndex = 0;
        }

        var select = new Button
        {
            Name = $"{namePrefix}Select",
            Content = "Select UI value",
            Padding = new Thickness(8, 3)
        };
        select.Click += (_, _) => selectControl();
        var selectedControl = new TextBlock
        {
            Name = $"{namePrefix}Selected",
            Text = operand.Kind == RecorderNumericOperandKind.Control && operand.Control is not null
                ? $"Selected: {operand.Control.ProposedPropertyName}"
                : "No UI value selected",
            TextWrapping = TextWrapping.Wrap
        };
        var content = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = label },
                source,
                literal,
                checkpoint,
                select,
                selectedControl
            }
        };
        var result = new NumericOperandFields(
            content,
            source,
            literal,
            checkpoint,
            selectedControl,
            operand.Kind == RecorderNumericOperandKind.Control ? operand : null);
        void RefreshVisibility()
        {
            var kind = source.SelectedItem is RecorderNumericOperandKind selectedKind
                ? selectedKind
                : RecorderNumericOperandKind.Literal;
            literal.IsVisible = kind == RecorderNumericOperandKind.Literal;
            checkpoint.IsVisible = kind == RecorderNumericOperandKind.Checkpoint;
            select.IsVisible = kind == RecorderNumericOperandKind.Control;
            selectedControl.IsVisible = kind == RecorderNumericOperandKind.Control;
        }

        source.SelectionChanged += (_, _) => RefreshVisibility();
        RefreshVisibility();
        _additionalTextInputs.Add(literal);
        _additionalComboInputs.Add(source);
        _additionalComboInputs.Add(checkpoint);
        return result;
    }

    private void BuildGridRowFields(StackPanel fields, RecorderStepEditDraft draft)
    {
        fields.Children.Add(new TextBlock
        {
            Text = "Row selector",
            FontWeight = FontWeight.SemiBold
        });

        if (draft.EditKind == RecorderStepEditKind.Checkpoint
            && draft.GridCapturedRowPosition is >= 0)
        {
            _gridRowMode = AddComboField(
                fields,
                "Row source",
                "RecorderStepEditGridRowMode",
                GridRowSourceOptions,
                draft.GridRowSourceMode == RecorderGridRowSourceMode.CurrentTableRow
                    ? "Current table row"
                    : "Recorded stable key");
            var currentRowWarning = new TextBlock
            {
                Text = $"Current table row: position {draft.GridCapturedRowPosition.Value + 1}. "
                    + "Sorting or filtering before this checkpoint can change which item is at this position. "
                    + "Later steps use the runtime stable key.",
                TextWrapping = TextWrapping.Wrap
            };
            fields.Children.Add(currentRowWarning);
            void RefreshCurrentRowWarning() => currentRowWarning.IsVisible = string.Equals(
                _gridRowMode.SelectedItem as string,
                "Current table row",
                StringComparison.Ordinal);
            _gridRowMode.SelectionChanged += (_, _) => RefreshCurrentRowWarning();
            RefreshCurrentRowWarning();
            _additionalComboInputs.Add(_gridRowMode);
        }
        else if (draft.EditKind == RecorderStepEditKind.Checkpoint)
        {
            fields.Children.Add(new TextBlock
            {
                Text = "Current table row is unavailable: Recorder could not prove the row's displayed position. "
                    + "The recorded stable key or an earlier UI value can still be used.",
                TextWrapping = TextWrapping.Wrap
            });
        }

        var recordedKeyFields = new StackPanel { Spacing = 4 };
        fields.Children.Add(recordedKeyFields);

        for (var index = 0; index < draft.GridRowConditions!.Count; index++)
        {
            var condition = draft.GridRowConditions[index];
            var keyCheckpoint = draft.EditKind == RecorderStepEditKind.Checkpoint
                && draft.GridRowConditions.Any(row => string.Equals(
                    row.ColumnName,
                    draft.GridTargetColumnName,
                    StringComparison.Ordinal));
            var variableSourceLabel = keyCheckpoint ? "Previous UI value" : "Variable";
            var sourceOptions = draft.AvailableGridRowVariables.Count > 0
                || condition.ValueReference is not null
                ? new[] { "Recorded value", variableSourceLabel }
                : ["Recorded value"];
            var source = AddComboField(
                recordedKeyFields,
                condition.ColumnName,
                $"RecorderStepEditRowSource{index}",
                sourceOptions,
                condition.ValueReference is null ? "Recorded value" : variableSourceLabel);
            var recorded = new TextBlock
            {
                Text = $"Recorded: {condition.Value}",
                TextWrapping = TextWrapping.Wrap
            };
            recordedKeyFields.Children.Add(recorded);
            var selected = draft.AvailableGridRowVariables
                .FirstOrDefault(option => option.Reference == condition.ValueReference);
            var variable = new ComboBox
            {
                Name = $"RecorderStepEditRowVariable{index}",
                ItemsSource = draft.AvailableGridRowVariables,
                SelectedItem = selected,
                MinWidth = 220
            };
            recordedKeyFields.Children.Add(CreateField("Variable", variable));
            if (draft.AvailableGridRowVariables.Count == 0)
            {
                recordedKeyFields.Children.Add(new TextBlock
                {
                    Text = keyCheckpoint
                        ? "Record this row key from an independent UI control before the checkpoint. "
                          + "A row position cannot identify the same item after sorting."
                        : "Record a text value before this step to use it as a row key.",
                    TextWrapping = TextWrapping.Wrap
                });
            }
            void RefreshVisibility()
            {
                var useVariable = string.Equals(
                    source.SelectedItem as string,
                    variableSourceLabel,
                    StringComparison.Ordinal);
                SetFieldVisible(variable, useVariable);
                variable.IsEnabled = draft.AvailableGridRowVariables.Count > 0;
                recorded.IsVisible = !useVariable;
            }

            source.SelectionChanged += (_, _) => RefreshVisibility();
            RefreshVisibility();
            _gridRowConditionFields.Add(new GridRowConditionFields(
                condition,
                source,
                variable,
                variableSourceLabel));
            _additionalComboInputs.Add(source);
            _additionalComboInputs.Add(variable);
        }

        if (_gridRowMode is not null)
        {
            void RefreshRowSource() => recordedKeyFields.IsVisible = !string.Equals(
                _gridRowMode.SelectedItem as string,
                "Current table row",
                StringComparison.Ordinal);
            _gridRowMode.SelectionChanged += (_, _) => RefreshRowSource();
            RefreshRowSource();
        }
    }

    private void BuildPayloadFields(StackPanel fields, RecorderStepEditDraft draft)
    {
        if (draft.StringValue is not null)
        {
            _stringValue = AddTextField(fields, "Value", "RecorderStepEditStringValue", draft.StringValue);
        }

        if (draft.ItemValue is not null)
        {
            _itemValue = AddTextField(fields, "Item", "RecorderStepEditItemValue", draft.ItemValue);
        }

        if (draft.IntValue.HasValue)
        {
            _integerValue = AddTextField(
                fields,
                "Minimum count",
                "RecorderStepEditIntegerValue",
                draft.IntValue.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (draft.DoubleValue.HasValue
            || draft.ActionKind == RecordedActionKind.SetNumericRangeFilter)
        {
            _numberValue = AddTextField(
                fields,
                draft.ActionKind == RecordedActionKind.SetNumericRangeFilter ? "From" : "Value",
                "RecorderStepEditNumberValue",
                draft.NumericInputText
                    ?? draft.DoubleValue?.ToString("R", CultureInfo.InvariantCulture)
                    ?? string.Empty);
        }

        if (draft.SecondDoubleValue.HasValue
            || draft.ActionKind == RecordedActionKind.SetNumericRangeFilter)
        {
            _secondNumberValue = AddTextField(
                fields,
                draft.ActionKind == RecordedActionKind.SetNumericRangeFilter ? "To" : "Second value",
                "RecorderStepEditSecondNumberValue",
                draft.SecondDoubleValue?.ToString("R", CultureInfo.InvariantCulture)
                    ?? string.Empty);
        }

        if (draft.BoolValue.HasValue)
        {
            _booleanValue = AddComboField(
                fields,
                "Value",
                "RecorderStepEditBooleanValue",
                BooleanOptions,
                draft.BoolValue.Value);
        }

        if (draft.DateValue.HasValue
            || draft.ActionKind == RecordedActionKind.SetDateRangeFilter)
        {
            _dateValue = AddTextField(
                fields,
                draft.ActionKind == RecordedActionKind.SetDateRangeFilter ? "From" : "Date",
                "RecorderStepEditDateValue",
                draft.DateValue.HasValue
                    ? RecorderValueCodec.FormatDate(draft.DateValue.Value, draft.DateExpression)
                    : string.Empty);
        }

        if (draft.SecondDateValue.HasValue
            || draft.ActionKind == RecordedActionKind.SetDateRangeFilter)
        {
            _secondDateValue = AddTextField(
                fields,
                draft.ActionKind == RecordedActionKind.SetDateRangeFilter ? "To" : "Second date",
                "RecorderStepEditSecondDateValue",
                draft.SecondDateValue.HasValue
                    ? RecorderValueCodec.FormatDate(draft.SecondDateValue.Value, draft.SecondDateExpression)
                    : string.Empty);
        }

        if (draft.TimeValue.HasValue)
        {
            _timeValue = AddTextField(
                fields,
                "Time",
                "RecorderStepEditTimeValue",
                draft.TimeValue.Value.ToString("c", CultureInfo.InvariantCulture));
        }

        if (draft.StringValues is not null)
        {
            _stringValues = AddTextField(
                fields,
                "Values (one per line)",
                "RecorderStepEditStringValues",
                string.Join(Environment.NewLine, draft.StringValues),
                acceptsReturn: true);
        }
    }

    private bool TryBuildDraft(
        out RecorderStepEditDraft? draft,
        out string error,
        RecorderStepRetargetRole? pendingRetargetRole = null)
    {
        draft = _sourceDraft with
        {
            VariableName = _variableName?.Text ?? _sourceDraft.VariableName,
            StringValue = _stringValue?.Text ?? _sourceDraft.StringValue,
            ItemValue = _itemValue?.Text ?? _sourceDraft.ItemValue,
            BoolValue = _booleanValue?.SelectedItem as bool? ?? _sourceDraft.BoolValue,
            StringValues = _stringValues is null
                ? _sourceDraft.StringValues
                : _stringValues.Text?
                    .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray()
        };

        var currentTableRowSelected = _gridRowMode is not null
            && string.Equals(
                _gridRowMode.SelectedItem as string,
                "Current table row",
                StringComparison.Ordinal);
        if (_gridRowConditionFields.Count > 0)
        {
            var rowConditions = new List<RecordedGridRowCondition>(_gridRowConditionFields.Count);
            foreach (var field in _gridRowConditionFields)
            {
                if (!currentTableRowSelected && string.Equals(
                    field.Source.SelectedItem as string,
                    field.VariableSourceLabel,
                    StringComparison.Ordinal))
                {
                    if (field.Variable.SelectedItem is not RecorderGridRowVariableOption option)
                    {
                        error = $"Choose an earlier text variable for row column '{field.Condition.ColumnName}'.";
                        return false;
                    }

                    rowConditions.Add(field.Condition with { ValueReference = option.Reference });
                }
                else
                {
                    rowConditions.Add(field.Condition with { ValueReference = null });
                }
            }

            draft = draft with { GridRowConditions = rowConditions };
        }

        if (_gridRowMode is not null)
        {
            draft = draft with
            {
                GridRowSourceMode = currentTableRowSelected
                    ? RecorderGridRowSourceMode.CurrentTableRow
                    : RecorderGridRowSourceMode.RecordedStableKey
            };
            if (draft.GridRowSourceMode == RecorderGridRowSourceMode.CurrentTableRow)
            {
                draft = draft with
                {
                    GridRowConditions = draft.GridRowConditions?
                        .Select(static condition => condition with { ValueReference = null })
                        .ToArray()
                };
            }
        }

        if (_variableName is not null
            && !RecorderNaming.TryValidateExactVariableName(draft.VariableName, out error))
        {
            return false;
        }

        if (_integerValue is not null)
        {
            var integerText = _integerValue.Text?.Trim();
            if (!int.TryParse(integerText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var integerValue)
                && !int.TryParse(integerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out integerValue)
                || integerValue < 0)
            {
                error = "Enter a non-negative whole number.";
                return false;
            }

            draft = draft with { IntValue = integerValue };
        }

        if (!TryParseOptionalNumber(_numberValue, _sourceDraft.DoubleValue, out var number, out error)
            || !TryParseOptionalNumber(_secondNumberValue, _sourceDraft.SecondDoubleValue, out var secondNumber, out error))
        {
            return false;
        }

        draft = draft with
        {
            DoubleValue = number,
            NumericInputText = ResolveNumericInputText(number),
            SecondDoubleValue = secondNumber
        };
        if (!TryParseOptionalDate(_dateValue, _sourceDraft.DateValue, out var date, out var dateExpression, out error)
            || !TryParseOptionalDate(_secondDateValue, _sourceDraft.SecondDateValue, out var secondDate, out var secondDateExpression, out error))
        {
            return false;
        }

        draft = draft with
        {
            DateValue = date,
            DateExpression = _dateValue is null ? _sourceDraft.DateExpression : dateExpression,
            SecondDateValue = secondDate,
            SecondDateExpression = _secondDateValue is null ? _sourceDraft.SecondDateExpression : secondDateExpression
        };

        var time = default(TimeSpan);
        if (_timeValue is not null
            && !RecorderValueCodec.TryParseTimeOfDay(_timeValue.Text, out time))
        {
            error = "Enter a valid time of day.";
            return false;
        }

        if (_timeValue is not null)
        {
            draft = draft with { TimeValue = time };
        }

        if (_sourceDraft.EditKind != RecorderStepEditKind.Assertion)
        {
            error = string.Empty;
            return true;
        }

        if (_comparison?.SelectedItem is not RecorderComparisonKind comparison)
        {
            error = "Choose an assertion comparison.";
            return false;
        }

        var presenceComparison = comparison is RecorderComparisonKind.HasValue or RecorderComparisonKind.IsEmpty;
        var expectedSource = presenceComparison
            ? RecorderExpectedValueSourceKind.None
            : _expectedSource?.SelectedItem as RecorderExpectedValueSourceKind?
                ?? RecorderExpectedValueSourceKind.Literal;
        draft = draft with
        {
            ComparisonKind = comparison,
            ExpectedSource = expectedSource,
            ExpectedCheckpointId = expectedSource == RecorderExpectedValueSourceKind.Checkpoint
                ? (_expectedCheckpoint?.SelectedItem as RecorderStepEditOption)?.Id
                : null,
            ExpectedGeneratedValueId = expectedSource == RecorderExpectedValueSourceKind.GeneratedValue
                ? (_expectedGeneratedValue?.SelectedItem as RecorderStepEditOption)?.Id
                : null
        };

        if (expectedSource == RecorderExpectedValueSourceKind.Calculated)
        {
            if (!TryBuildCalculatedExpression(pendingRetargetRole, out var expression, out error))
            {
                return false;
            }

            draft = draft with { NumericExpectedExpression = expression };
        }

        if (expectedSource == RecorderExpectedValueSourceKind.Literal
            && !TryApplyExpectedLiteral(draft, _expectedLiteral?.Text ?? string.Empty, out draft, out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryBuildCalculatedExpression(
        RecorderStepRetargetRole? pendingRetargetRole,
        out RecorderNumericExpectedExpression? expression,
        out string error)
    {
        expression = null;
        if (_calculatedOperation?.SelectedItem is not RecorderArithmeticOperation operation
            || _leftOperand is null
            || _rightOperand is null)
        {
            error = "Configure the calculated expected value.";
            return false;
        }

        var sourceExpression = _sourceDraft.NumericExpectedExpression;
        if (!TryBuildNumericOperand(
                _leftOperand,
                pendingRetargetRole == RecorderStepRetargetRole.CalculatedLeftOperand,
                sourceExpression?.Left,
                out var left,
                out error)
            || !TryBuildNumericOperand(
                _rightOperand,
                pendingRetargetRole == RecorderStepRetargetRole.CalculatedRightOperand,
                sourceExpression?.Right,
                out var right,
                out error))
        {
            return false;
        }

        if (RecorderValueCodec.DividesByLiteralZero(operation, right))
        {
            error = "Cannot divide by a literal zero.";
            return false;
        }

        expression = new RecorderNumericExpectedExpression(operation, left!, right!);
        error = string.Empty;
        return true;
    }

    private static bool TryBuildNumericOperand(
        NumericOperandFields fields,
        bool allowPendingControlSelection,
        RecorderNumericOperand? fallbackOperand,
        out RecorderNumericOperand? operand,
        out string error)
    {
        var kind = fields.Source.SelectedItem is RecorderNumericOperandKind selectedKind
            ? selectedKind
            : RecorderNumericOperandKind.Literal;
        var input = new RecorderNumericOperandInput(
            kind,
            fields.Literal.Text,
            (fields.Checkpoint.SelectedItem as RecorderStepEditOption)?.Id,
            fields.ControlOperand);
        var valid = RecorderValueCodec.TryCreateNumericOperand(
            input,
            allowPendingControlSelection,
            fallbackOperand,
            out operand,
            out var validationError);
        error = validationError ?? string.Empty;
        return valid;
    }

    private string? ResolveNumericInputText(double? value)
    {
        if (_numberValue is null
            || _sourceDraft.ActionKind != RecordedActionKind.EditGridCellNumber
            || !value.HasValue)
        {
            return _sourceDraft.NumericInputText;
        }

        var inputText = _numberValue.Text?.Trim();
        if (string.IsNullOrEmpty(inputText))
        {
            return null;
        }

        return GridNumericText.PreserveNonCanonicalInput(
            inputText,
            value.Value,
            _sourceDraft.NumericInputCultureName);
    }

    private static bool TryApplyExpectedLiteral(
        RecorderStepEditDraft source,
        string value,
        out RecorderStepEditDraft? draft,
        out string error)
    {
        draft = source;
        switch (source.ValueKind)
        {
            case RecorderValueKind.Number:
                if (!RecorderValueCodec.TryParseFiniteNumber(value, out var number))
                {
                    error = "Enter a finite numeric expected value.";
                    return false;
                }

                draft = source with { DoubleValue = number };
                break;
            case RecorderValueKind.Boolean:
                if (!bool.TryParse(value, out var boolean))
                {
                    error = "Enter true or false.";
                    return false;
                }

                draft = source with { BoolValue = boolean };
                break;
            case RecorderValueKind.Date:
                if (!RecorderValueCodec.TryParseDate(
                        value,
                        out var date,
                        out var dateExpression,
                        out error))
                {
                    return false;
                }

                draft = source with { DateValue = date, DateExpression = dateExpression };
                break;
            case RecorderValueKind.Time:
                if (!RecorderValueCodec.TryParseTimeOfDay(value, out var time))
                {
                    error = "Enter a valid time of day.";
                    return false;
                }

                draft = source with { TimeValue = time };
                break;
            case RecorderValueKind.StringSet:
                draft = source with
                {
                    StringValues = value
                        .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToArray()
                };
                break;
            default:
                draft = source with { StringValue = value };
                break;
        }

        error = string.Empty;
        return true;
    }

    private void RefreshAssertionFieldVisibility()
    {
        if (_comparison is null || _expectedSource is null)
        {
            return;
        }

        var presence = _comparison.SelectedItem is RecorderComparisonKind.HasValue or RecorderComparisonKind.IsEmpty;
        SetFieldVisible(_expectedSource, !presence);
        var expectedSource = _expectedSource.SelectedItem as RecorderExpectedValueSourceKind?
            ?? RecorderExpectedValueSourceKind.Literal;
        SetFieldVisible(
            _expectedLiteral,
            !presence && expectedSource == RecorderExpectedValueSourceKind.Literal);
        SetFieldVisible(
            _expectedCheckpoint,
            !presence && expectedSource == RecorderExpectedValueSourceKind.Checkpoint);
        SetFieldVisible(
            _expectedGeneratedValue,
            !presence && expectedSource == RecorderExpectedValueSourceKind.GeneratedValue);
        if (_calculatedFields is not null)
        {
            _calculatedFields.IsVisible = !presence
                && expectedSource == RecorderExpectedValueSourceKind.Calculated;
        }
    }

    private void RefreshExpectedSourceOptions()
    {
        if (_comparison?.SelectedItem is not RecorderComparisonKind comparison
            || _expectedSource is null)
        {
            return;
        }

        var selected = _expectedSource.SelectedItem as RecorderExpectedValueSourceKind?;
        var sources = RecorderStepEditService.GetExpectedSources(_sourceDraft, comparison);
        _expectedSource.ItemsSource = sources;
        _expectedSource.SelectedItem = selected.HasValue && sources.Contains(selected.Value)
            ? selected.Value
            : sources[0];
    }

    private static TextBox AddTextField(
        StackPanel fields,
        string label,
        string name,
        string? value,
        bool acceptsReturn = false)
    {
        var input = new TextBox
        {
            Name = name,
            Text = value ?? string.Empty,
            MinWidth = 260,
            AcceptsReturn = acceptsReturn
        };
        fields.Children.Add(CreateField(label, input));
        return input;
    }

    private static ComboBox AddComboField<T>(
        StackPanel fields,
        string label,
        string name,
        IReadOnlyList<T> options,
        object? selected)
    {
        var input = new ComboBox
        {
            Name = name,
            ItemsSource = options,
            MinWidth = 220,
            SelectedItem = selected
        };
        if (input.SelectedIndex < 0 && options.Count > 0)
        {
            input.SelectedIndex = 0;
        }

        fields.Children.Add(CreateField(label, input));
        return input;
    }

    private static ComboBox AddOptionField(
        StackPanel fields,
        string label,
        string name,
        IReadOnlyList<RecorderStepEditOption> options,
        Guid? selectedId)
    {
        var selected = selectedId.HasValue
            ? options.FirstOrDefault(option => option.Id == selectedId.Value)
            : null;
        return AddComboField(fields, label, name, options, selected);
    }

    private static StackPanel CreateField(string label, Control input) =>
        new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = label },
                input
            }
        };

    private static void SetFieldVisible(Control? input, bool isVisible)
    {
        if (input?.Parent is Control field)
        {
            field.IsVisible = isVisible;
            return;
        }

        if (input is not null)
        {
            input.IsVisible = isVisible;
        }
    }

    private static void AddTargetField(
        StackPanel fields,
        string label,
        string target,
        string buttonText,
        Action retarget)
    {
        var button = new Button
        {
            Name = buttonText == "Change source"
                ? "RecorderStepEditChangeSource"
                : "RecorderStepEditChangeTarget",
            Content = buttonText,
            Padding = new Thickness(8, 3)
        };
        button.Click += (_, _) => retarget();
        fields.Children.Add(new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = label },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Name = "RecorderStepEditTarget",
                            Text = target,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        button
                    }
                }
            }
        });
    }

    private bool TryParseOptionalNumber(
        TextBox? input,
        double? fallback,
        out double? value,
        out string error)
    {
        if (input is null)
        {
            value = fallback;
            error = string.Empty;
            return true;
        }

        if (ReferenceEquals(input, _numberValue)
            && _sourceDraft.ActionKind == RecordedActionKind.EditGridCellNumber)
        {
            if (GridNumericText.TryParse(
                    input.Text,
                    _sourceDraft.NumericInputCultureName,
                    out var gridNumber,
                    out var diagnostic))
            {
                value = gridNumber;
                error = string.Empty;
                return true;
            }

            value = null;
            error = diagnostic ?? "Enter a valid number.";
            return false;
        }

        if (_sourceDraft.ActionKind == RecordedActionKind.SetNumericRangeFilter
            && string.IsNullOrWhiteSpace(input.Text))
        {
            value = null;
            error = string.Empty;
            return true;
        }

        if (!RecorderValueCodec.TryParseFiniteNumber(input.Text, out var parsed))
        {
            value = null;
            error = "Enter a finite number.";
            return false;
        }

        value = parsed;
        error = string.Empty;
        return true;
    }

    private bool TryParseOptionalDate(
        TextBox? input,
        DateTime? fallback,
        out DateTime? value,
        out RecorderDateExpression? expression,
        out string error)
    {
        if (input is null)
        {
            value = fallback;
            expression = null;
            error = string.Empty;
            return true;
        }

        if (_sourceDraft.ActionKind == RecordedActionKind.SetDateRangeFilter
            && string.IsNullOrWhiteSpace(input.Text))
        {
            value = null;
            expression = null;
            error = string.Empty;
            return true;
        }

        if (!RecorderValueCodec.TryParseDate(
                input.Text,
                out var parsed,
                out expression,
                out error))
        {
            value = null;
            return false;
        }

        value = parsed;
        error = string.Empty;
        return true;
    }

    private static string FormatLiteral(RecorderStepEditDraft draft) =>
        draft.ValueKind switch
        {
            RecorderValueKind.Number => draft.DoubleValue?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
            RecorderValueKind.Boolean => draft.BoolValue?.ToString().ToLowerInvariant() ?? string.Empty,
            RecorderValueKind.Date when draft.DateValue.HasValue => RecorderValueCodec.FormatDate(draft.DateValue.Value, draft.DateExpression),
            RecorderValueKind.Time => draft.TimeValue?.ToString("c", CultureInfo.InvariantCulture) ?? string.Empty,
            RecorderValueKind.StringSet => string.Join(Environment.NewLine, draft.StringValues ?? Array.Empty<string>()),
            _ => draft.StringValue ?? string.Empty
        };

    private static string DescribeKind(RecorderStepEditKind kind) => kind switch
    {
        RecorderStepEditKind.Checkpoint => "checkpoint",
        RecorderStepEditKind.Assertion => "assertion",
        RecorderStepEditKind.GeneratedValue => "generated value",
        RecorderStepEditKind.CopiedValue => "copied value",
        _ => "recorded step"
    };

    private sealed record RecorderStepEditOption(string DisplayName, Guid Id)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record NumericOperandFields(
        StackPanel Content,
        ComboBox Source,
        TextBox Literal,
        ComboBox Checkpoint,
        TextBlock SelectedControl,
        RecorderNumericOperand? ControlOperand);

    private sealed record GridRowConditionFields(
        RecordedGridRowCondition Condition,
        ComboBox Source,
        ComboBox Variable,
        string VariableSourceLabel);
}
