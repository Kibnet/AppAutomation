using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderStepValidator
{
    private readonly AppAutomationRecorderOptions _options;

    public RecorderStepValidator(AppAutomationRecorderOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public RecordedStep Validate(RecordedStep step, Control? source)
    {
        if (!step.CanPersist || step.ValidationStatus == RecorderValidationStatus.Invalid)
        {
            return step;
        }

        if (source is null)
        {
            return MarkInvalid(step, "Recorder lost the source control before validation.");
        }

        return SupportsAction(step, source)
            ? step
            : MarkInvalid(
                step,
                BuildUnsupportedActionMessage(step.ActionKind, source));
    }

    private bool SupportsAction(RecordedStep step, Control source)
    {
        return step.ActionKind switch
        {
            RecordedActionKind.CaptureCheckpoint or RecordedActionKind.AssertValue => true,
            RecordedActionKind.EnterText
                or RecordedActionKind.EnterSearch
                or RecordedActionKind.ClearSearch => source is TextBox,
            RecordedActionKind.SetSpinnerValue => source is TextBox or NumericUpDown,
            RecordedActionKind.SetTime => source is TimePicker,
            RecordedActionKind.SetExpanded => source is Expander,
            RecordedActionKind.SetColor => source is Control,
            RecordedActionKind.InvokeMenuItem => source is Menu or MenuItem,
            RecordedActionKind.InvokeContextMenuItem =>
                source.ContextMenu is not null || source.ContextFlyout is MenuFlyout,
            RecordedActionKind.ClickButton => source is Button and not ToggleButton,
            RecordedActionKind.SetChecked => source is CheckBox or RadioButton,
            RecordedActionKind.SetToggled => source is ToggleButton and not CheckBox and not RadioButton,
            RecordedActionKind.SelectComboItem => source is ComboBox or ListBox,
            RecordedActionKind.SelectListBoxItem => source is ListBox,
            RecordedActionKind.SetSliderValue => source is Slider,
            RecordedActionKind.SelectTabItem => source is TabItem,
            RecordedActionKind.SelectTreeItem => source is TreeView or TreeViewItem,
            RecordedActionKind.SetDate => SupportsDateAction(step, source),
            RecordedActionKind.WaitUntilTextEquals or RecordedActionKind.WaitUntilTextContains =>
                source is TextBox or TextBlock or Label or Button,
            RecordedActionKind.WaitUntilValueEquals => source is TextBox or NumericUpDown,
            RecordedActionKind.WaitUntilTimeEquals => source is TimePicker or TextBox,
            RecordedActionKind.WaitUntilIsExpanded => source is Expander,
            RecordedActionKind.WaitUntilColorEquals => source is Control,
            RecordedActionKind.WaitUntilIsChecked => source is CheckBox,
            RecordedActionKind.WaitUntilIsToggled => source is ToggleButton and not CheckBox and not RadioButton,
            RecordedActionKind.WaitUntilIsSelected => source is RadioButton or TabItem,
            RecordedActionKind.WaitUntilIsEnabled => true,
            RecordedActionKind.WaitUntilExists => true,
            RecordedActionKind.WaitUntilProgressAtLeast => source is ProgressBar,
            RecordedActionKind.WaitUntilListBoxContains or RecordedActionKind.WaitUntilHasItemsAtLeast => source is ListBox,
            RecordedActionKind.WaitUntilGridRowsAtLeast
                or RecordedActionKind.WaitUntilGridContainsRow
                or RecordedActionKind.WaitUntilGridCellEquals => true,
            RecordedActionKind.WaitUntilNotificationContains => true,
            RecordedActionKind.SearchAndSelect or RecordedActionKind.SearchAndSelectGridCell => true,
            RecordedActionKind.ApplySearchFromHistory => true,
            RecordedActionKind.SelectMultiItems
                or RecordedActionKind.CancelMultiSelection
                or RecordedActionKind.ApplyFilterSelection
                or RecordedActionKind.CancelFilterSelection => true,
            RecordedActionKind.OpenGridRow
                or RecordedActionKind.SortGridByColumn
                or RecordedActionKind.ScrollGridToEnd
                or RecordedActionKind.CopyGridCell
                or RecordedActionKind.ExportGrid => true,
            RecordedActionKind.SetDateRangeFilter
                or RecordedActionKind.SetNumericRangeFilter
                or RecordedActionKind.SelectExportFolder
                or RecordedActionKind.EditGridCellText
                or RecordedActionKind.EditGridCellNumber
                or RecordedActionKind.EditGridCellDate
                or RecordedActionKind.EditGridCellTime
                or RecordedActionKind.EditGridCellColor
                or RecordedActionKind.SelectGridCellComboItem => true,
            RecordedActionKind.ConfirmDialog
                or RecordedActionKind.CancelDialog
                or RecordedActionKind.DismissDialog
                or RecordedActionKind.DismissNotification
                or RecordedActionKind.OpenOrActivateShellPane
                or RecordedActionKind.ActivateShellPane => true,
            _ => false
        };
    }

    private bool SupportsDateAction(RecordedStep step, Control source)
    {
        return step.Control.ControlType switch
        {
            UiControlType.DateTimePicker =>
                source is DatePicker
                || RecorderDatePickerHintMatcher.IsConfiguredPart(
                    step.Control,
                    source,
                    _options.DatePickerHints),
            UiControlType.Calendar => source is Calendar,
            _ => false
        };
    }

    private static string BuildUnsupportedActionMessage(RecordedActionKind actionKind, Control source)
    {
        return $"Recorded action '{actionKind}' is not compatible with control '{source.GetType().Name}'."
            + " The stable locator resolves to a wrapper/composite control instead of the interactive part."
            + " Configure a composite recorder hint for this pattern or expose stable part locators on the real input/button.";
    }

    private static RecordedStep MarkInvalid(RecordedStep step, string message)
    {
        return step with
        {
            ValidationStatus = RecorderValidationStatus.Invalid,
            ValidationMessage = Combine(step.ValidationMessage, message),
            CanPersist = false
        };
    }

    private static string Combine(string? existing, string message)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return message;
        }

        if (string.Equals(existing, message, StringComparison.Ordinal))
        {
            return existing;
        }

        return $"{existing} {message}";
    }
}

internal static class RecorderDatePickerHintMatcher
{
    public static bool IsConfiguredPart(
        RecordedControlDescriptor descriptor,
        Control source,
        IEnumerable<RecorderDatePickerHint> hints)
    {
        if (descriptor.ControlType != UiControlType.DateTimePicker)
        {
            return false;
        }

        var matchingHints = hints
            .Where(hint =>
                hint.LocatorKind == descriptor.LocatorKind
                && string.Equals(
                    hint.LocatorValue.Trim(),
                    descriptor.LocatorValue.Trim(),
                    StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matchingHints.Length == 1 && IsPart(source, matchingHints[0]);
    }

    public static bool IsPart(Control? source, RecorderDatePickerHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);

        return EnumerateRelatedControls(source).Any(current =>
            HasLocator(current, hint.LocatorKind, hint.LocatorValue)
            || HasAnyLocator(
                current,
                hint.Parts.LocatorKind,
                hint.Parts.RootLocator,
                hint.Parts.ValueLocator,
                hint.Parts.OpenButtonLocator,
                hint.Parts.CalendarLocator,
                hint.Parts.PopupRootLocator));
    }

    private static bool HasAnyLocator(Control source, UiLocatorKind locatorKind, params string?[] locatorValues)
    {
        return locatorValues.Any(locatorValue =>
            !string.IsNullOrWhiteSpace(locatorValue)
            && HasLocator(source, locatorKind, locatorValue!));
    }

    private static bool HasLocator(Control source, UiLocatorKind locatorKind, string locatorValue)
    {
        if (string.IsNullOrWhiteSpace(locatorValue))
        {
            return false;
        }

        var actualLocator = locatorKind switch
        {
            UiLocatorKind.AutomationId => AutomationProperties.GetAutomationId(source),
            UiLocatorKind.Name => AutomationProperties.GetName(source) ?? source.Name,
            _ => null
        };
        return string.Equals(actualLocator?.Trim(), locatorValue.Trim(), StringComparison.Ordinal);
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
}
