using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderStepFactory
{
    internal StepCreationResult TryCreateNamedItemStep(Control surface, string itemName)
    {
        if (string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(surface)))
            return StepCreationResult.Unsupported("The named surface must publish its own stable AutomationId.");

        var locator = _selectorResolver.Resolve(surface, UiControlType.NamedItemSurface);
        if (!locator.Success || locator.Control is null)
            return StepCreationResult.Unsupported(locator.Message);

        return CreateStep(surface, new RecordedStep(
            RecordedActionKind.DoubleClickNamedItem,
            locator.Control,
            StringValue: itemName,
            Warning: locator.Control.Warning,
            ValidationStatus: locator.ValidationStatus,
            ValidationMessage: locator.ValidationMessage,
            CanPersist: locator.CanPersist));
    }
}
