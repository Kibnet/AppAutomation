using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderSelectorResolver
{
    private readonly AppAutomationRecorderOptions _options;

    public RecorderSelectorResolver(AppAutomationRecorderOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public StepCreationResult Resolve(Control? source, UiControlType controlType)
    {
        if (source is null)
        {
            return StepCreationResult.Unsupported("Event source control was not found.");
        }

        Control? nameFallback = null;

        for (Control? current = source; current is not null; current = current.GetVisualParent() as Control)
        {
            if (current is Window)
            {
                break;
            }

            var automationId = AutomationProperties.GetAutomationId(current);
            if (!string.IsNullOrWhiteSpace(automationId))
            {
                return CreateResolvedStep(current, controlType, automationId.Trim(), UiLocatorKind.AutomationId, warning: null);
            }

            if (nameFallback is null && _options.AllowNameLocators && TryGetNameLocator(current, out _))
            {
                nameFallback = current;
            }
        }

        if (nameFallback is not null && TryGetNameLocator(nameFallback, out var locatorName))
        {
            return CreateResolvedStep(
                nameFallback,
                controlType,
                locatorName,
                UiLocatorKind.Name,
                warning: "Using Name locator; prefer AutomationId for long-term stability.");
        }

        return StepCreationResult.Unsupported(
            _options.AllowNameLocators
                ? "Control does not expose a stable AutomationId or Name locator."
                : "Control does not expose a stable AutomationId locator.");
    }

    private static StepCreationResult CreateResolvedStep(
        Control control,
        UiControlType controlType,
        string locatorValue,
        UiLocatorKind locatorKind,
        string? warning)
    {
        return StepCreationResult.Created(
            new RecordedStep(
                RecordedActionKind.ClickButton,
                new RecordedControlDescriptor(
                    RecorderNaming.CreateControlPropertyName(locatorValue, controlType),
                    controlType,
                    locatorValue,
                    locatorKind,
                    FallbackToName: false,
                    control.GetType().FullName ?? control.GetType().Name,
                    warning)));
    }

    private static bool TryGetNameLocator(Control control, out string locator)
    {
        locator = AutomationProperties.GetName(control) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(locator))
        {
            locator = locator.Trim();
            return true;
        }

        locator = control.Name ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(locator))
        {
            locator = locator.Trim();
            return true;
        }

        locator = string.Empty;
        return false;
    }
}
