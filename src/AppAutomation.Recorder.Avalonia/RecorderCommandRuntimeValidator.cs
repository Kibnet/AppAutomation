using AppAutomation.Abstractions;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderCommandRuntimeValidator
{
    private readonly RecorderValidationOptions _options;

    public RecorderCommandRuntimeValidator(AppAutomationRecorderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Validation;
    }

    public RecordedStep Validate(RecordedStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        var targets = GetSelectedTargets();
        if (!_options.ValidateRuntimeTargets || targets.Count == 0)
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

    private static IEnumerable<RecorderRuntimeValidationFinding> ValidateTarget(
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

    private static IEnumerable<RecorderRuntimeValidationFinding> ValidateAction(
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
            RecordedActionKind.WaitUntilIsChecked => ValidateControlType(step, target, UiControlType.CheckBox)
                .Concat(RequireBool(step, target)),
            RecordedActionKind.WaitUntilIsToggled => ValidateControlType(step, target, UiControlType.ToggleButton)
                .Concat(RequireBool(step, target)),
            RecordedActionKind.WaitUntilIsSelected => ValidateControlType(step, target, [UiControlType.RadioButton, UiControlType.TabItem])
                .Concat(RequireBool(step, target)),
            RecordedActionKind.WaitUntilIsEnabled => RequireBool(step, target),
            RecordedActionKind.WaitUntilGridRowsAtLeast => ValidateGridAction(step, target)
                .Concat(RequireNonNegativeInt(step.IntValue, target, "grid row count")),
            RecordedActionKind.WaitUntilGridCellEquals => ValidateGridAction(step, target)
                .Concat(RequireNonNegativeInt(step.RowIndex, target, "grid row index"))
                .Concat(RequireNonNegativeInt(step.ColumnIndex, target, "grid column index"))
                .Concat(RequireString(step, target, allowEmpty: true, "grid cell value")),
            RecordedActionKind.SearchAndSelect => ValidateControlType(step, target, UiControlType.SearchPicker)
                .Concat(RequireString(step, target, allowEmpty: false, "search text"))
                .Concat(RequireItemValue(step, target)),
            RecordedActionKind.OpenGridRow => ValidateGridUserAction(step, target)
                .Concat(RequireNonNegativeInt(step.RowIndex, target, "grid row index")),
            RecordedActionKind.SortGridByColumn => ValidateGridUserAction(step, target)
                .Concat(RequireString(step, target, allowEmpty: false, "grid column name")),
            RecordedActionKind.ScrollGridToEnd => ValidateGridUserAction(step, target),
            RecordedActionKind.CopyGridCell => ValidateGridUserAction(step, target)
                .Concat(RequireNonNegativeInt(step.RowIndex, target, "grid row index"))
                .Concat(RequireNonNegativeInt(step.ColumnIndex, target, "grid column index")),
            RecordedActionKind.ExportGrid => ValidateGridUserAction(step, target),
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

    private static IEnumerable<RecorderRuntimeValidationFinding> ValidateSpinnerAction(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        foreach (var finding in ValidateControlType(step, target, UiControlType.TextBox))
        {
            yield return finding;
        }

        foreach (var finding in RequireDouble(step, target))
        {
            yield return finding;
        }

        yield return Warning(
            target,
            "spinner-textbox-fallback",
            "Spinner action is generated through a text-box fallback; verify the application exposes a writable spinner text part.");
    }

    private static IEnumerable<RecorderRuntimeValidationFinding> ValidateTextReadableAssertion(
        RecordedStep step,
        RecorderRuntimeValidationTarget target)
    {
        foreach (var finding in ValidateControlType(
                     step,
                     target,
                     [UiControlType.TextBox, UiControlType.Label, UiControlType.Button, UiControlType.AutomationElement]))
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
