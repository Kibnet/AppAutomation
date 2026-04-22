using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderSelectorResolver
{
    private readonly AppAutomationRecorderOptions _options;
    private readonly Func<Control?>? _validationRootProvider;

    public RecorderSelectorResolver(
        AppAutomationRecorderOptions options,
        Window? validationWindow = null,
        Control? validationRoot = null)
        : this(
            options,
            validationRoot is not null
                ? (() => validationRoot)
                : validationWindow is not null
                    ? () => validationWindow.Content as Control
                    : null)
    {
    }

    internal RecorderSelectorResolver(
        AppAutomationRecorderOptions options,
        Func<Control?>? validationRootProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _validationRootProvider = validationRootProvider;
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
        if (TryResolveLocatorAlias(locatorValue, locatorKind, out var alias))
        {
            return CreateAliasedResolvedControl(control, locatorValue, locatorKind, warning, alias);
        }

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

    private ResolvedControlResult CreateAliasedResolvedControl(
        Control control,
        string sourceLocatorValue,
        UiLocatorKind sourceLocatorKind,
        string? warning,
        RecorderLocatorAlias alias)
    {
        var targetLocatorValue = alias.TargetLocatorValue.Trim();
        var validation = ValidateSelector(targetLocatorValue, alias.TargetLocatorKind);
        var aliasMessage =
            $"Mapped recorder locator '{sourceLocatorKind}:{sourceLocatorValue}' to stable locator '{alias.TargetLocatorKind}:{targetLocatorValue}'.";

        return ResolvedControlResult.Created(
            new RecordedControlDescriptor(
                RecorderNaming.CreateControlPropertyName(targetLocatorValue, alias.TargetControlType),
                alias.TargetControlType,
                targetLocatorValue,
                alias.TargetLocatorKind,
                alias.FallbackToName,
                control.GetType().FullName ?? control.GetType().Name,
                CombineMessage(warning, aliasMessage)),
            message: validation.Message ?? aliasMessage,
            validationStatus: validation.Status,
            validationMessage: validation.Message,
            canPersist: validation.CanPersist);
    }

    internal ExistingControlResolutionResult ResolveExisting(RecordedControlDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var validation = ValidateSelector(descriptor.LocatorValue, descriptor.LocatorKind);
        return new ExistingControlResolutionResult(
            validation.MatchedControl is not null,
            validation.MatchedControl,
            validation.Status,
            validation.Message,
            validation.CanPersist);
    }

    private SelectorValidationResult ValidateSelector(Control expectedControl, string locatorValue, UiLocatorKind locatorKind)
    {
        var validation = ValidateSelector(locatorValue, locatorKind);
        if (!validation.Success)
        {
            return validation;
        }

        if (!ReferenceEquals(validation.MatchedControl, expectedControl))
        {
            return new SelectorValidationResult(
                RecorderValidationStatus.Invalid,
                $"Selector '{locatorKind}:{locatorValue}' re-resolved a different control than the captured owner.",
                false,
                validation.MatchedControl);
        }

        return validation;
    }

    private SelectorValidationResult ValidateSelector(string locatorValue, UiLocatorKind locatorKind)
    {
        var baseStatus = locatorKind == UiLocatorKind.Name
            ? RecorderValidationStatus.Warning
            : RecorderValidationStatus.Valid;
        var baseMessage = locatorKind == UiLocatorKind.Name
            ? "Using Name locator; prefer AutomationId for long-term stability."
            : null;

        var root = _validationRootProvider?.Invoke();
        if (!_options.Validation.ValidateSelectors || root is not Control)
        {
            return new SelectorValidationResult(baseStatus, baseMessage, true, null);
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
                false,
                null);
        }

        if (matches.Length > 1)
        {
            return new SelectorValidationResult(
                RecorderValidationStatus.Invalid,
                $"Selector '{locatorKind}:{locatorValue}' is ambiguous and matched {matches.Length} controls.",
                false,
                null);
        }

        return new SelectorValidationResult(baseStatus, baseMessage, true, matches[0]);
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

    private bool TryResolveLocatorAlias(
        string locatorValue,
        UiLocatorKind locatorKind,
        out RecorderLocatorAlias alias)
    {
        alias = _options.LocatorAliases.FirstOrDefault(candidate =>
            candidate.SourceLocatorKind == locatorKind
            && string.Equals(candidate.SourceLocatorValue.Trim(), locatorValue, StringComparison.Ordinal))!;
        if (alias is not null && !string.IsNullOrWhiteSpace(alias.TargetLocatorValue))
        {
            return true;
        }

        var gridHint = _options.GridHints.FirstOrDefault(candidate =>
            candidate.SourceLocatorKind == locatorKind
            && string.Equals(candidate.SourceLocatorValue.Trim(), locatorValue, StringComparison.Ordinal));
        if (gridHint is null || string.IsNullOrWhiteSpace(gridHint.TargetLocatorValue))
        {
            return false;
        }

        alias = new RecorderLocatorAlias(
            gridHint.SourceLocatorValue,
            gridHint.TargetLocatorValue,
            UiControlType.Grid,
            gridHint.SourceLocatorKind,
            gridHint.TargetLocatorKind,
            gridHint.FallbackToName);
        return true;
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

    private sealed record SelectorValidationResult(
        RecorderValidationStatus Status,
        string? Message,
        bool CanPersist,
        Control? MatchedControl)
    {
        public bool Success => MatchedControl is not null || !CanPersist;
    }

    internal sealed record ExistingControlResolutionResult(
        bool Success,
        Control? MatchedControl,
        RecorderValidationStatus ValidationStatus,
        string? ValidationMessage,
        bool CanPersist);
}
