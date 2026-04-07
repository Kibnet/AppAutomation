using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderStepValidator
{
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

        return SupportsAction(step.ActionKind, source)
            ? step
            : MarkInvalid(
                step,
                $"Recorded action '{step.ActionKind}' is not compatible with control '{source.GetType().Name}'.");
    }

    private static bool SupportsAction(RecordedActionKind actionKind, Control source)
    {
        return actionKind switch
        {
            RecordedActionKind.EnterText or RecordedActionKind.SetSpinnerValue => source is TextBox,
            RecordedActionKind.ClickButton => source is Button and not ToggleButton,
            RecordedActionKind.SetChecked => source is CheckBox or RadioButton,
            RecordedActionKind.SetToggled => source is ToggleButton and not CheckBox and not RadioButton,
            RecordedActionKind.SelectComboItem => source is ComboBox,
            RecordedActionKind.SelectListBoxItem => source is ListBox,
            RecordedActionKind.SetSliderValue => source is Slider,
            RecordedActionKind.SelectTabItem => source is TabItem,
            RecordedActionKind.SelectTreeItem => source is TreeView or TreeViewItem,
            RecordedActionKind.SetDate => source is DatePicker or Calendar,
            RecordedActionKind.WaitUntilTextEquals or RecordedActionKind.WaitUntilTextContains =>
                source is TextBox or TextBlock or Label or Button,
            RecordedActionKind.WaitUntilIsChecked => source is CheckBox,
            RecordedActionKind.WaitUntilIsToggled => source is ToggleButton and not CheckBox and not RadioButton,
            RecordedActionKind.WaitUntilIsSelected => source is RadioButton or TabItem,
            RecordedActionKind.WaitUntilIsEnabled => true,
            _ => false
        };
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
