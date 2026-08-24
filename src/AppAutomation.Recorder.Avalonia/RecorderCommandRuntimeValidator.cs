using AppAutomation.Abstractions;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderCommandRuntimeValidator
{
    private readonly AppAutomationRecorderOptions _recorderOptions;
    private readonly RecorderValidationOptions _options;

    public RecorderCommandRuntimeValidator(AppAutomationRecorderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _recorderOptions = options;
        _options = options.Validation;
    }

    public RecordedStep Validate(RecordedStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        var targets = GetSelectedTargets();
        if (!_options.ValidateRuntimeTargets
            || targets.Count == 0
            || !step.CanPersist
            || step.ValidationStatus == RecorderValidationStatus.Invalid)
        {
            return step with { RuntimeValidationFindings = Array.Empty<RecorderRuntimeValidationFinding>() };
        }

        var findings = targets
            .SelectMany(target => ValidateTarget(step, target))
            .ToArray();

        var blockedTargets = findings
            .Where(static finding => finding.BlocksTarget)
            .Select(static finding => finding.Target)
            .Distinct()
            .ToHashSet();
        var allSelectedTargetsBlocked = targets.All(blockedTargets.Contains);
        var hasRuntimeSurfaceFindings = findings.Any(static finding => finding.ShouldSurface);

        var validationStatus = step.ValidationStatus;
        if (allSelectedTargetsBlocked)
        {
            validationStatus = RecorderValidationStatus.Invalid;
        }
        else if (hasRuntimeSurfaceFindings && validationStatus == RecorderValidationStatus.Valid)
        {
            validationStatus = RecorderValidationStatus.Warning;
        }

        var validationMessage = CombineMessage(
            step.ValidationMessage,
            BuildRuntimeValidationMessage(findings));

        return step with
        {
            ValidationStatus = validationStatus,
            ValidationMessage = validationMessage,
            CanPersist = step.CanPersist && !allSelectedTargetsBlocked,
            RuntimeValidationFindings = findings
        };
    }

    private IReadOnlyList<RecorderRuntimeValidationTarget> GetSelectedTargets()
    {
        var targets = new List<RecorderRuntimeValidationTarget>();
        if ((_options.RuntimeTargets & RecorderRuntimeValidationTargets.Headless) != 0)
        {
            targets.Add(RecorderRuntimeValidationTarget.Headless);
        }

        if ((_options.RuntimeTargets & RecorderRuntimeValidationTargets.FlaUI) != 0)
        {
            targets.Add(RecorderRuntimeValidationTarget.FlaUI);
        }

        return targets;
    }

    private IEnumerable<RecorderRuntimeValidationFinding> ValidateTarget(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        if (!IsSupportedLocatorKind(step.Control.LocatorKind))
        {
            yield return Invalid(
                target,
                "locator-kind-unsupported",
                $"Locator kind '{step.Control.LocatorKind}' is not supported by recorder runtime validation.");
            yield break;
        }

        var actionFindings = ValidateAction(step, target).ToArray();
        if (actionFindings.Length == 0)
        {
            yield return Info(target, "target-supported", $"Recorded action '{step.ActionKind}' is supported by {target} readiness validation.");
            yield break;
        }

        foreach (var finding in actionFindings)
        {
            yield return finding;
        }
    }

    private IEnumerable<RecorderRuntimeValidationFinding> ValidateAction(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        return step.ActionKind switch
        {
            RecordedActionKind.EnterText => ValidateTextAction(step, target),
            RecordedActionKind.ClickButton => ValidateControlType(step, target, UiControlType.Button),
            RecordedActionKind.SetChecked => ValidateControlType(step, target, [UiControlType.CheckBox, UiControlType.RadioButton])
                .Concat(RequireBool(step, target)),
            RecordedActionKind.SetToggled => ValidateControlType(step, target, UiControlType.ToggleButton)
                .Concat(RequireBool(step, target)),
            RecordedActionKind.SelectComboItem => ValidateControlType(step, target, UiControlType.ComboBox)
                .Concat(RequireString(step, target, allowEmpty: false, "selected item text")),
            RecordedActionKind.SelectListBoxItem => ValidateControlType(step, target, UiControlType.ListBox)
                .Concat(RequireString(step, target, allowEmpty: false, "selected item text")),
            RecordedActionKind.SetSliderValue => ValidateControlType(step, target, UiControlType.Slider)
                .Concat(RequireDouble(step, target)),
            RecordedActionKind.SetSpinnerValue => ValidateSpinnerAction(step, target),
            RecordedActionKind.SelectTabItem => ValidateControlType(step, target, UiControlType.TabItem),
            RecordedActionKind.SelectTreeItem => ValidateControlType(step, target, UiControlType.Tree)
                .Concat(RequireString(step, target, allowEmpty: false, "tree item text")),
            RecordedActionKind.SetDate => ValidateControlType(step, target, [UiControlType.DateTimePicker, UiControlType.Calendar])
                .Concat(RequireDate(step, target)),
            RecordedActionKind.WaitUntilTextEquals or RecordedActionKind.WaitUntilTextContains => ValidateTextReadableAssertion(step, target),
            RecordedActionKind.WaitUntilValueEquals => ValidateSpinnerValueAssertion(step, target),
            RecordedActionKind.WaitUntilIsChecked => ValidateControlType(step, target, UiControlType.CheckBox)
                .Concat(RequireBool(step, target)),
            RecordedActionKind.WaitUntilIsToggled => ValidateControlType(step, target, UiControlType.ToggleButton)
                .Concat(RequireBool(step, target)),
            RecordedActionKind.WaitUntilIsSelected => ValidateControlType(step, target, [UiControlType.RadioButton, UiControlType.TabItem])
                .Concat(RequireBool(step, target)),
            RecordedActionKind.WaitUntilIsEnabled => RequireBool(step, target),
            RecordedActionKind.WaitUntilExists => Enumerable.Empty<RecorderRuntimeValidationFinding>(),
            RecordedActionKind.WaitUntilGridRowsAtLeast => ValidateGridAction(step, target)
                .Concat(RequireNonNegativeInt(step.IntValue, target, "grid row count")),
            RecordedActionKind.WaitUntilGridContainsRow => ValidateGridAction(step, target)
                .Concat(RequireNamedGridRow(step, target)),
            RecordedActionKind.WaitUntilGridCellEquals => ValidateGridAction(step, target)
                .Concat(RequireGridCoordinates(step, target, requireTargetColumn: true))
                .Concat(RequireString(step, target, allowEmpty: true, "grid cell value")),
            RecordedActionKind.WaitUntilProgressAtLeast => ValidateControlType(step, target, UiControlType.ProgressBar)
                .Concat(RequireDouble(step, target)),
            RecordedActionKind.WaitUntilListBoxContains => ValidateControlType(step, target, UiControlType.ListBox)
                .Concat(RequireString(step, target, allowEmpty: false, "list item text")),
            RecordedActionKind.WaitUntilHasItemsAtLeast => ValidateControlType(step, target, UiControlType.ListBox)
                .Concat(RequireNonNegativeInt(step.IntValue, target, "list item count")),
            RecordedActionKind.WaitUntilNotificationContains => ValidateControlType(step, target, UiControlType.Notification)
                .Concat(RequireString(step, target, allowEmpty: false, "notification text")),
            RecordedActionKind.SearchAndSelect => ValidateControlType(step, target, UiControlType.SearchPicker)
                .Concat(RequireString(step, target, allowEmpty: false, "search text"))
                .Concat(RequireItemValue(step, target)),
            RecordedActionKind.EnterSearch => ValidateControlType(step, target, UiControlType.Search)
                .Concat(RequireString(step, target, allowEmpty: false, "search text")),
            RecordedActionKind.ClearSearch => ValidateControlType(step, target, UiControlType.Search),
            RecordedActionKind.ApplySearchFromHistory => ValidateControlType(step, target, UiControlType.Search)
                .Concat(RequireString(step, target, allowEmpty: false, "search history item")),
            RecordedActionKind.SearchAndSelectGridCell => ValidateGridUserAction(step, target)
                .Concat(RequireGridCoordinates(step, target, requireTargetColumn: true))
                .Concat(RequireString(step, target, allowEmpty: false, "search text"))
                .Concat(RequireItemValue(step, target)),
            RecordedActionKind.OpenGridRow => ValidateGridUserAction(step, target)
                .Concat(RequireGridCoordinates(step, target, requireTargetColumn: false)),
            RecordedActionKind.SortGridByColumn => ValidateGridUserAction(step, target)
                .Concat(RequireString(step, target, allowEmpty: false, "grid column name")),
            RecordedActionKind.ScrollGridToEnd => ValidateGridUserAction(step, target),
            RecordedActionKind.CopyGridCell => ValidateGridUserAction(step, target)
                .Concat(RequireGridCoordinates(step, target, requireTargetColumn: true)),
            RecordedActionKind.ExportGrid => ValidateGridUserAction(step, target),
            RecordedActionKind.SetDateRangeFilter => ValidateControlType(step, target, UiControlType.DateRangeFilter)
                .Concat(RequireAtLeastOneDateBound(step, target)),
            RecordedActionKind.SetNumericRangeFilter => ValidateControlType(step, target, UiControlType.NumericRangeFilter)
                .Concat(RequireAtLeastOneNumericBound(step, target)),
            RecordedActionKind.SelectExportFolder => ValidateControlType(step, target, UiControlType.FolderExport)
                .Concat(RequireString(step, target, allowEmpty: false, "folder path")),
            RecordedActionKind.EditGridCellText => ValidateGridUserAction(step, target)
                .Concat(RequireGridCellEditIndexes(step, target))
                .Concat(RequireString(step, target, allowEmpty: true, "grid cell text value")),
            RecordedActionKind.EditGridCellNumber => ValidateGridUserAction(step, target)
                .Concat(RequireGridCellEditIndexes(step, target))
                .Concat(RequireDouble(step, target)),
            RecordedActionKind.EditGridCellDate => ValidateGridUserAction(step, target)
                .Concat(RequireGridCellEditIndexes(step, target))
                .Concat(RequireDate(step, target)),
            RecordedActionKind.SelectGridCellComboItem => ValidateGridUserAction(step, target)
                .Concat(RequireGridCellEditIndexes(step, target))
                .Concat(RequireString(step, target, allowEmpty: false, "grid combo item text")),
            RecordedActionKind.SelectMultiItems
                or RecordedActionKind.CancelMultiSelection => ValidateMultiSelectAction(step, target),
            RecordedActionKind.ApplyFilterSelection
                or RecordedActionKind.CancelFilterSelection => ValidateComboBoxFilterAction(step, target),
            RecordedActionKind.ConfirmDialog
                or RecordedActionKind.CancelDialog
                or RecordedActionKind.DismissDialog => ValidateControlType(step, target, UiControlType.Dialog),
            RecordedActionKind.DismissNotification => ValidateControlType(step, target, UiControlType.Notification),
            RecordedActionKind.OpenOrActivateShellPane
                or RecordedActionKind.ActivateShellPane => ValidateControlType(step, target, UiControlType.ShellNavigation)
                    .Concat(RequireString(step, target, allowEmpty: false, "shell pane name")),
            _ => [Invalid(target, "action-unsupported", $"Recorded action '{step.ActionKind}' is not supported by {target}.")]
        };
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> ValidateTextAction(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        foreach (var finding in ValidateControlType(step, target, UiControlType.TextBox))
        {
            yield return finding;
        }

        if (step.StringValue is null)
        {
            yield return Invalid(target, "payload-missing-string", "Text entry payload is missing.");
        }
    }

    private IEnumerable<RecorderRuntimeValidationFinding> ValidateSpinnerAction(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        foreach (var finding in ValidateControlType(step, target, [UiControlType.Spinner, UiControlType.TextBox]))
        {
            yield return finding;
        }

        foreach (var finding in RequireDouble(step, target))
        {
            yield return finding;
        }

        if (step.Control.ControlType == UiControlType.TextBox
            && !RecorderSpinnerProxyConfiguration.IsConfigured(
                _recorderOptions,
                step.Control.LocatorValue,
                step.Control.LocatorKind))
        {
            yield return Warning(
                target,
                "spinner-textbox-fallback",
                "Spinner action is generated through a text-box fallback; verify the application exposes a writable spinner text part.");
        }
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> ValidateTextReadableAssertion(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        foreach (var finding in ValidateControlType(
                     step,
                     target,
                     [
                         UiControlType.AutomationElement,
                         UiControlType.TextBox,
                         UiControlType.Label,
                         UiControlType.Button,
                         UiControlType.ListBox,
                         UiControlType.CheckBox,
                         UiControlType.ComboBox,
                         UiControlType.RadioButton,
                         UiControlType.ToggleButton,
                         UiControlType.Tab,
                         UiControlType.Tree,
                         UiControlType.TreeItem,
                         UiControlType.DataGridView,
                         UiControlType.TabItem,
                         UiControlType.Grid,
                         UiControlType.SearchPicker,
                         UiControlType.Search,
                         UiControlType.Dialog,
                         UiControlType.Notification,
                         UiControlType.FolderExport,
                         UiControlType.ShellNavigation
                     ]))
        {
            yield return finding;
        }

        foreach (var finding in RequireString(step, target, allowEmpty: false, "expected text"))
        {
            yield return finding;
        }
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> ValidateGridAction(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        return ValidateControlType(step, target, [UiControlType.Grid, UiControlType.DataGridView]);
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> ValidateMultiSelectAction(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        return ValidateSelectionSetAction(
            step,
            target,
            UiControlType.MultiSelect,
            "payload-invalid-multi-select-values",
            "Multi-select action requires distinct non-empty item texts.",
            "multi-select-adapter-required",
            "Multi-select action requires registered composite parts or a consumer IMultiSelectControl adapter.");
    }

    private IEnumerable<RecorderRuntimeValidationFinding> ValidateComboBoxFilterAction(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        return ValidateSelectionSetAction(
            step,
            target,
            UiControlType.ComboBoxFilter,
            "payload-invalid-combo-box-filter-values",
            "Combo-box filter action requires distinct non-empty item texts.",
            "combo-box-filter-adapter-required",
            "Combo-box filter action requires registered composite parts or a consumer IComboBoxFilterControl adapter.",
            includeAdapterWarning: !IsConfiguredComboBoxFilter(step));
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> ValidateSelectionSetAction(
        RecordedStep step,
        RecorderRuntimeValidationTarget target,
        UiControlType controlType,
        string invalidPayloadCode,
        string invalidPayloadMessage,
        string adapterWarningCode,
        string adapterWarningMessage,
        bool includeAdapterWarning = true)
    {
        foreach (var finding in ValidateControlType(step, target, controlType))
        {
            yield return finding;
        }

        var values = step.StringValues?.Select(static value => value?.Trim() ?? string.Empty).ToArray() ?? [];
        if (values.Any(string.IsNullOrWhiteSpace)
            || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
        {
            yield return Invalid(target, invalidPayloadCode, invalidPayloadMessage);
        }

        if (includeAdapterWarning)
        {
            yield return Warning(target, adapterWarningCode, adapterWarningMessage);
        }
    }

    private IEnumerable<RecorderRuntimeValidationFinding> ValidateSpinnerValueAssertion(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        foreach (var finding in ValidateControlType(step, target, UiControlType.Spinner))
        {
            yield return finding;
        }

        foreach (var finding in RequireDouble(step, target))
        {
            yield return finding;
        }
    }

    private bool IsConfiguredComboBoxFilter(RecordedStep step)
    {
        return _recorderOptions.ComboBoxFilterHints.Any(hint =>
            hint.LocatorKind == step.Control.LocatorKind
            && string.Equals(
                hint.LocatorValue.Trim(),
                step.Control.LocatorValue.Trim(),
                StringComparison.Ordinal));
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> ValidateGridUserAction(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        foreach (var finding in ValidateControlType(step, target, UiControlType.Grid))
        {
            yield return finding;
        }

        yield return Warning(
            target,
            "grid-user-action-adapter-required",
            "Grid user action requires a runtime grid action adapter; plain grid row/cell access is not enough.");
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> ValidateControlType(
        RecordedStep step,
        RecorderRuntimeValidationTarget target,
        UiControlType expected)
    {
        return ValidateControlType(step, target, [expected]);
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> ValidateControlType(
        RecordedStep step,
        RecorderRuntimeValidationTarget target,
        IReadOnlyList<UiControlType> expected)
    {
        if (expected.Contains(step.Control.ControlType))
        {
            return [];
        }

        var expectedText = string.Join(", ", expected.Select(static value => $"UiControlType.{value}"));
        return
        [
            Invalid(
                target,
                "control-type-mismatch",
                $"Recorded action '{step.ActionKind}' requires {expectedText}, but captured UiControlType.{step.Control.ControlType}.")
        ];
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> RequireBool(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        return step.BoolValue.HasValue
            ? []
            : [Invalid(target, "payload-missing-bool", $"Recorded action '{step.ActionKind}' requires a boolean payload.")];
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> RequireDouble(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        return step.DoubleValue.HasValue
            ? []
            : [Invalid(target, "payload-missing-double", $"Recorded action '{step.ActionKind}' requires a numeric payload.")];
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> RequireDate(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        return step.DateValue.HasValue
            ? []
            : [Invalid(target, "payload-missing-date", $"Recorded action '{step.ActionKind}' requires a date payload.")];
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> RequireAtLeastOneDateBound(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        return step.DateValue.HasValue || step.SecondDateValue.HasValue
            ? []
            : [Invalid(target, "payload-missing-date", $"Recorded action '{step.ActionKind}' requires at least one date bound.")];
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> RequireAtLeastOneNumericBound(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        return step.DoubleValue.HasValue || step.SecondDoubleValue.HasValue
            ? []
            : [Invalid(target, "payload-missing-double", $"Recorded action '{step.ActionKind}' requires at least one numeric bound.")];
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> RequireGridCellEditIndexes(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        return RequireGridCoordinates(step, target, requireTargetColumn: true);
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> RequireGridCoordinates(
        RecordedStep step,
        RecorderRuntimeValidationTarget target,
        bool requireTargetColumn)
    {
        if (step.GridRowConditions is null)
        {
            return requireTargetColumn
                ? RequireNonNegativeInt(step.RowIndex, target, "grid row index")
                    .Concat(RequireNonNegativeInt(step.ColumnIndex, target, "grid column index"))
                : RequireNonNegativeInt(step.RowIndex, target, "grid row index");
        }

        var findings = new List<RecorderRuntimeValidationFinding>();
        if (step.GridRowConditions.Count == 0
            || step.GridRowConditions.Any(static condition => string.IsNullOrWhiteSpace(condition.ColumnName)))
        {
            findings.Add(Invalid(target, "payload-missing-grid-row-selector", "Named grid action requires at least one row condition with a column name."));
        }

        if (step.GridRowConditions
            .Select(static condition => condition.ColumnName)
            .Distinct(StringComparer.Ordinal)
            .Count() != step.GridRowConditions.Count)
        {
            findings.Add(Invalid(target, "payload-duplicate-grid-row-column", "Named grid row selector contains duplicate column names."));
        }

        if (requireTargetColumn && string.IsNullOrWhiteSpace(step.GridTargetColumnName))
        {
            findings.Add(Invalid(target, "payload-missing-grid-target-column", "Named grid cell action requires a target column name."));
        }

        findings.Add(Warning(
            target,
            "grid-column-metadata-adapter-required",
            "Named grid action requires stable runtime column metadata registered with WithGridColumns or supplied by the grid control."));

        return findings;
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> RequireNamedGridRow(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        if (step.GridRowConditions is null)
        {
            return
            [
                Invalid(
                    target,
                    "payload-missing-grid-row-selector",
                    "Named grid row assertion requires at least one row condition.")
            ];
        }

        return RequireGridCoordinates(step, target, requireTargetColumn: false);
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> RequireString(
        RecordedStep step,
        RecorderRuntimeValidationTarget target,
        bool allowEmpty,
        string payloadName)
    {
        if (step.StringValue is not null && (allowEmpty || !string.IsNullOrWhiteSpace(step.StringValue)))
        {
            return [];
        }

        return
        [
            Invalid(
                target,
                "payload-missing-string",
                $"Recorded action '{step.ActionKind}' requires {payloadName}.")
        ];
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> RequireItemValue(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        return !string.IsNullOrWhiteSpace(step.ItemValue)
            ? []
            : [Invalid(target, "payload-missing-item", $"Recorded action '{step.ActionKind}' requires selected item text.")];
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> RequireNonNegativeInt(
        int? value,
        RecorderRuntimeValidationTarget target,
        string payloadName)
    {
        return value is >= 0
            ? []
            : [Invalid(target, "payload-missing-index", $"Recorded action requires non-negative {payloadName}.")];
    }

    private static RecorderRuntimeValidationFinding Invalid(
        RecorderRuntimeValidationTarget target,
        string code,
        string message)
    {
        return new RecorderRuntimeValidationFinding(
            target,
            RecorderRuntimeValidationSeverity.Invalid,
            $"{FormatTargetPrefix(target)}-{code}",
            message,
            BlocksTarget: true);
    }

    private static RecorderRuntimeValidationFinding Warning(
        RecorderRuntimeValidationTarget target,
        string code,
        string message)
    {
        return new RecorderRuntimeValidationFinding(
            target,
            RecorderRuntimeValidationSeverity.Warning,
            $"{FormatTargetPrefix(target)}-{code}",
            message,
            BlocksTarget: false);
    }

    private static RecorderRuntimeValidationFinding Info(
        RecorderRuntimeValidationTarget target,
        string code,
        string message)
    {
        return new RecorderRuntimeValidationFinding(
            target,
            RecorderRuntimeValidationSeverity.Info,
            $"{FormatTargetPrefix(target)}-{code}",
            message,
            BlocksTarget: false);
    }

    private static bool IsSupportedLocatorKind(UiLocatorKind locatorKind)
    {
        return locatorKind is UiLocatorKind.AutomationId or UiLocatorKind.Name;
    }

    private static string BuildRuntimeValidationMessage(IReadOnlyList<RecorderRuntimeValidationFinding> findings)
    {
        var surfaced = findings
            .Where(static finding => finding.ShouldSurface)
            .Select(static finding =>
            {
                var result = finding.BlocksTarget ? "failed" : "warning";
                return $"{finding.Target} validation {result}: {finding.Code}.";
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return string.Join(" ", surfaced);
    }

    private static string FormatTargetPrefix(RecorderRuntimeValidationTarget target)
    {
        return target switch
        {
            RecorderRuntimeValidationTarget.Headless => "headless",
            RecorderRuntimeValidationTarget.FlaUI => "flaui",
            _ => target.ToString().ToLowerInvariant()
        };
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
}
