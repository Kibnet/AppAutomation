using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderSelectorResolver
{
    private readonly AppAutomationRecorderOptions _options;
    private readonly Control? _validationRoot;

    public RecorderSelectorResolver(
        AppAutomationRecorderOptions options,
        Window? validationWindow = null,
        Control? validationRoot = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _validationRoot = validationRoot ?? validationWindow?.Content as Control;
    }

    public ResolvedControlResult Resolve(Control? source, UiControlType controlType)
    {
        if (source is null)
        {
            return ResolvedControlResult.Unsupported("Event source control was not found.");
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
                return CreateResolvedControl(current, controlType, automationId.Trim(), UiLocatorKind.AutomationId, warning: null);
            }

            if (nameFallback is null && _options.AllowNameLocators && TryGetNameLocator(current, out _))
            {
                nameFallback = current;
            }
        }

        if (nameFallback is not null && TryGetNameLocator(nameFallback, out var locatorName))
        {
            return CreateResolvedControl(
                nameFallback,
                controlType,
                locatorName,
                UiLocatorKind.Name,
                warning: "Using Name locator; prefer AutomationId for long-term stability.");
        }

        return ResolvedControlResult.Unsupported(
            _options.AllowNameLocators
                ? "Control does not expose a stable AutomationId or Name locator."
                : "Control does not expose a stable AutomationId locator.");
    }

    private ResolvedControlResult CreateResolvedControl(
        Control control,
        UiControlType controlType,
        string locatorValue,
        UiLocatorKind locatorKind,
        string? warning)
    {
        var validation = ValidateSelector(control, locatorValue, locatorKind);
        return ResolvedControlResult.Created(
            new RecordedControlDescriptor(
                RecorderNaming.CreateControlPropertyName(locatorValue, controlType),
                controlType,
                locatorValue,
                locatorKind,
                FallbackToName: locatorKind == UiLocatorKind.Name,
                control.GetType().FullName ?? control.GetType().Name,
                warning),
            message: validation.Message,
            validationStatus: validation.Status,
            validationMessage: validation.Message,
            canPersist: validation.CanPersist);
    }

    private SelectorValidationResult ValidateSelector(Control expectedControl, string locatorValue, UiLocatorKind locatorKind)
    {
        var baseStatus = locatorKind == UiLocatorKind.Name
            ? RecorderValidationStatus.Warning
            : RecorderValidationStatus.Valid;
        var baseMessage = locatorKind == UiLocatorKind.Name
            ? "Using Name locator; prefer AutomationId for long-term stability."
            : null;

        if (!_options.Validation.ValidateSelectors || _validationRoot is not Control root)
        {
            return new SelectorValidationResult(baseStatus, baseMessage, true);
        }

        var matches = root
            .GetVisualDescendants()
            .OfType<Control>()
            .Prepend(root)
            .Where(candidate => MatchesLocator(candidate, locatorValue, locatorKind))
            .ToArray();

        if (matches.Length == 0)
        {
            return new SelectorValidationResult(
                RecorderValidationStatus.Invalid,
                $"Selector '{locatorKind}:{locatorValue}' could not be re-resolved in the current visual tree.",
                false);
        }

        if (matches.Length > 1)
        {
            return new SelectorValidationResult(
                RecorderValidationStatus.Invalid,
                $"Selector '{locatorKind}:{locatorValue}' is ambiguous and matched {matches.Length} controls.",
                false);
        }

        if (!ReferenceEquals(matches[0], expectedControl))
        {
            return new SelectorValidationResult(
                RecorderValidationStatus.Invalid,
                $"Selector '{locatorKind}:{locatorValue}' re-resolved a different control than the captured owner.",
                false);
        }

        return new SelectorValidationResult(baseStatus, baseMessage, true);
    }

    private static bool MatchesLocator(Control candidate, string locatorValue, UiLocatorKind locatorKind)
    {
        return locatorKind switch
        {
            UiLocatorKind.AutomationId => string.Equals(
                AutomationProperties.GetAutomationId(candidate)?.Trim(),
                locatorValue,
                StringComparison.Ordinal),
            UiLocatorKind.Name => TryGetNameLocator(candidate, out var candidateName)
                && string.Equals(candidateName, locatorValue, StringComparison.Ordinal),
            _ => false
        };
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

    private sealed record SelectorValidationResult(
        RecorderValidationStatus Status,
        string? Message,
        bool CanPersist);
}
