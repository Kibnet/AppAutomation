using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
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
        return Resolve(source, controlType, includeSingleSelectAlias: true);
    }

    internal ResolvedControlResult ResolvePrimitiveSelection(Control? source, UiControlType controlType)
    {
        return Resolve(source, controlType, includeSingleSelectAlias: false);
    }

    private ResolvedControlResult Resolve(
        Control? source,
        UiControlType controlType,
        bool includeSingleSelectAlias)
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
                return CreateResolvedControl(
                    current,
                    controlType,
                    automationId.Trim(),
                    UiLocatorKind.AutomationId,
                    warning: null,
                    includeSingleSelectAlias);
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
                warning: "Using Name locator; prefer AutomationId for long-term stability.",
                includeSingleSelectAlias);
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
        string? warning,
        bool includeSingleSelectAlias = true)
    {
        if (TryResolveLocatorAlias(locatorValue, locatorKind, includeSingleSelectAlias, out var alias))
        {
            return CreateAliasedResolvedControl(control, locatorValue, locatorKind, warning, alias);
        }

        var effectiveControlType = controlType;
        var effectiveFallbackToName = locatorKind == UiLocatorKind.Name;
        var effectiveWarning = warning;
        if (TryResolveControlHint(locatorValue, locatorKind, out var hint))
        {
            if (hint.TargetControlType is { } targetControlType)
            {
                effectiveControlType = targetControlType;
                effectiveWarning = CombineMessage(
                    effectiveWarning,
                    $"Applied recorder control hint '{locatorKind}:{locatorValue}' as UiControlType.{targetControlType}.");
            }

            if (hint.FallbackToName is { } fallbackToName)
            {
                effectiveFallbackToName = fallbackToName;
            }
        }

        var validation = ValidateSelector(
            control,
            locatorValue,
            locatorKind,
            includeLogicalDescendants: effectiveControlType is UiControlType.Menu or UiControlType.MenuItem);
        return ResolvedControlResult.Created(
            new RecordedControlDescriptor(
                RecorderNaming.CreateControlPropertyName(locatorValue, effectiveControlType),
                effectiveControlType,
                locatorValue,
                locatorKind,
                effectiveFallbackToName,
                control.GetType().FullName ?? control.GetType().Name,
                effectiveWarning),
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
        var isConfiguredSpinnerProxy = RecorderSpinnerProxyConfiguration.IsConfigured(
            _options,
            targetLocatorValue,
            alias.TargetLocatorKind);
        var isConfiguredTimePicker = _options.TimePickerHints.Any(hint =>
            hint.LocatorKind == alias.TargetLocatorKind
            && string.Equals(hint.LocatorValue.Trim(), targetLocatorValue, StringComparison.Ordinal));
        var isConfiguredSingleSelect = _options.SingleSelectHints.Any(hint =>
            hint.LocatorKind == alias.TargetLocatorKind
            && string.Equals(hint.LocatorValue.Trim(), targetLocatorValue, StringComparison.Ordinal));

        return ResolvedControlResult.Created(
            new RecordedControlDescriptor(
                RecorderNaming.CreateControlPropertyName(targetLocatorValue, alias.TargetControlType),
                alias.TargetControlType,
                targetLocatorValue,
                alias.TargetLocatorKind,
                alias.FallbackToName,
                control.GetType().FullName ?? control.GetType().Name,
                isConfiguredSpinnerProxy || isConfiguredTimePicker || isConfiguredSingleSelect
                    ? warning
                    : CombineMessage(warning, aliasMessage)),
            message: validation.Message ?? aliasMessage,
            validationStatus: validation.Status,
            validationMessage: validation.Message,
            canPersist: validation.CanPersist);
    }

    internal ExistingControlResolutionResult ResolveExisting(RecordedControlDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var validation = ValidateSelector(
            descriptor.LocatorValue,
            descriptor.LocatorKind,
            includeLogicalDescendants: descriptor.ControlType is UiControlType.Menu or UiControlType.MenuItem);
        return new ExistingControlResolutionResult(
            validation.MatchedControl is not null,
            validation.MatchedControl,
            validation.Status,
            validation.Message,
            validation.CanPersist);
    }

    internal ExistingControlResolutionResult ResolveExisting(RecordedStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        var logicalResolution = ResolveExisting(step.Control);
        if (!logicalResolution.CanPersist)
        {
            return logicalResolution;
        }

        if (step.ActionKind == RecordedActionKind.SelectComboItem
            && TryResolveSingleSelectPart(step.Control, out var singleSelectHint))
        {
            var resultsMatches = FindMatches(
                singleSelectHint.Parts.ResultsLocator,
                singleSelectHint.Parts.LocatorKind);
            if (resultsMatches.Length == 0)
            {
                return new ExistingControlResolutionResult(
                    true,
                    null,
                    logicalResolution.ValidationStatus,
                    logicalResolution.ValidationMessage,
                    logicalResolution.CanPersist);
            }

            var resultsValidation = ValidateSelector(
                singleSelectHint.Parts.ResultsLocator,
                singleSelectHint.Parts.LocatorKind);
            return new ExistingControlResolutionResult(
                resultsValidation.MatchedControl is not null,
                resultsValidation.MatchedControl,
                MaxStatus(logicalResolution.ValidationStatus, resultsValidation.Status),
                CombineMessage(logicalResolution.ValidationMessage, resultsValidation.Message),
                resultsValidation.CanPersist);
        }

        if (RequiresTimePickerPartValidation(step.ActionKind)
            && TryResolveTimePickerPart(step.Control, out var timePickerLocator, out var timePickerLocatorKind))
        {
            var timePickerMatches = FindMatches(timePickerLocator, timePickerLocatorKind);
            if (timePickerMatches.Length == 0)
            {
                return new ExistingControlResolutionResult(
                    true,
                    null,
                    logicalResolution.ValidationStatus,
                    logicalResolution.ValidationMessage,
                    logicalResolution.CanPersist);
            }

            var timeValidation = ValidateSelector(timePickerLocator, timePickerLocatorKind);
            if (timeValidation.MatchedControl is null && timeValidation.CanPersist)
            {
                return logicalResolution;
            }

            return new ExistingControlResolutionResult(
                timeValidation.MatchedControl is not null,
                timeValidation.MatchedControl,
                MaxStatus(logicalResolution.ValidationStatus, timeValidation.Status),
                CombineMessage(logicalResolution.ValidationMessage, timeValidation.Message),
                timeValidation.CanPersist);
        }

        if (!RequiresSpinnerProxyValidation(step)
            || !TryResolveSpinnerProxyAlias(step.Control, out var proxyAlias))
        {
            return logicalResolution;
        }

        var interactiveValidation = ValidateSelector(
            proxyAlias.SourceLocatorValue.Trim(),
            proxyAlias.SourceLocatorKind);
        return new ExistingControlResolutionResult(
            interactiveValidation.MatchedControl is not null,
            interactiveValidation.MatchedControl,
            MaxStatus(logicalResolution.ValidationStatus, interactiveValidation.Status),
            CombineMessage(logicalResolution.ValidationMessage, interactiveValidation.Message),
            interactiveValidation.CanPersist);
    }

    private SelectorValidationResult ValidateSelector(
        Control expectedControl,
        string locatorValue,
        UiLocatorKind locatorKind,
        bool includeLogicalDescendants = false)
    {
        var validation = ValidateSelector(locatorValue, locatorKind, includeLogicalDescendants);
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

    private SelectorValidationResult ValidateSelector(
        string locatorValue,
        UiLocatorKind locatorKind,
        bool includeLogicalDescendants = false)
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

        var matches = FindMatches(locatorValue, locatorKind, includeLogicalDescendants);

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

    private Control[] FindMatches(
        string locatorValue,
        UiLocatorKind locatorKind,
        bool includeLogicalDescendants = false)
    {
        var root = _validationRootProvider?.Invoke();
        if (root is null)
        {
            return [];
        }

        IEnumerable<Control> candidates = root.GetVisualDescendants().OfType<Control>().Prepend(root);
        if (includeLogicalDescendants)
        {
            var attachedControls = candidates
                .Concat(root.GetLogicalDescendants().OfType<Control>())
                .Distinct()
                .ToArray();
            candidates = attachedControls
                .Concat(EnumerateDetachedMenuItems(attachedControls))
                .Distinct();
        }

        return candidates
            .Where(candidate => MatchesLocator(candidate, locatorValue, locatorKind))
            .ToArray();
    }

    private static IEnumerable<MenuItem> EnumerateDetachedMenuItems(IEnumerable<Control> controls)
    {
        foreach (var control in controls)
        {
            if (control is Menu menu)
            {
                foreach (var item in EnumerateMenuItems(menu.Items.OfType<MenuItem>()))
                {
                    yield return item;
                }
            }

            if (control.ContextMenu is { } contextMenu)
            {
                foreach (var item in EnumerateMenuItems(contextMenu.Items.OfType<MenuItem>()))
                {
                    yield return item;
                }
            }

            if (control.ContextFlyout is MenuFlyout menuFlyout)
            {
                foreach (var item in EnumerateMenuItems(menuFlyout.Items.OfType<MenuItem>()))
                {
                    yield return item;
                }
            }
        }
    }

    private static IEnumerable<MenuItem> EnumerateMenuItems(IEnumerable<MenuItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var descendant in EnumerateMenuItems(item.Items.OfType<MenuItem>()))
            {
                yield return descendant;
            }
        }
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
        bool includeSingleSelectAlias,
        out RecorderLocatorAlias alias)
    {
        alias = _options.LocatorAliases.FirstOrDefault(candidate =>
            candidate.SourceLocatorKind == locatorKind
            && string.Equals(candidate.SourceLocatorValue.Trim(), locatorValue, StringComparison.Ordinal))!;
        if (alias is not null && !string.IsNullOrWhiteSpace(alias.TargetLocatorValue))
        {
            return true;
        }

        var timePickerHint = _options.TimePickerHints.FirstOrDefault(candidate =>
            candidate.Parts.LocatorKind == locatorKind
            && (string.Equals(candidate.Parts.TimePickerLocator.Trim(), locatorValue, StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(candidate.Parts.InputLocator)
                    && string.Equals(candidate.Parts.InputLocator.Trim(), locatorValue, StringComparison.Ordinal))));
        if (timePickerHint is not null)
        {
            alias = new RecorderLocatorAlias(
                locatorValue,
                timePickerHint.LocatorValue,
                UiControlType.TimePicker,
                timePickerHint.Parts.LocatorKind,
                timePickerHint.LocatorKind,
                timePickerHint.FallbackToName);
            return true;
        }

        if (includeSingleSelectAlias)
        {
            var singleSelectHint = _options.SingleSelectHints.FirstOrDefault(candidate =>
                candidate.Parts.LocatorKind == locatorKind
                && string.Equals(candidate.Parts.ResultsLocator.Trim(), locatorValue, StringComparison.Ordinal));
            if (singleSelectHint is not null)
            {
                alias = new RecorderLocatorAlias(
                    locatorValue,
                    singleSelectHint.LocatorValue,
                    UiControlType.ComboBox,
                    singleSelectHint.Parts.LocatorKind,
                    singleSelectHint.LocatorKind,
                    singleSelectHint.FallbackToName);
                return true;
            }
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

    private bool TryResolveControlHint(
        string locatorValue,
        UiLocatorKind locatorKind,
        out RecorderControlHint hint)
    {
        hint = _options.ControlHints.FirstOrDefault(candidate =>
            candidate.LocatorKind == locatorKind
            && string.Equals(candidate.LocatorValue.Trim(), locatorValue, StringComparison.Ordinal))!;
        return hint is not null;
    }

    private bool TryResolveSpinnerProxyAlias(
        RecordedControlDescriptor descriptor,
        out RecorderLocatorAlias alias)
    {
        alias = null!;
        return descriptor.ControlType == UiControlType.Spinner
            && RecorderSpinnerProxyConfiguration.TryResolveAlias(
                _options,
                descriptor.LocatorValue,
                descriptor.LocatorKind,
                out alias);
    }

    private static bool RequiresSpinnerProxyValidation(RecordedStep step)
    {
        return step.ActionKind is RecordedActionKind.SetSpinnerValue
            or RecordedActionKind.WaitUntilValueEquals
            or RecordedActionKind.WaitUntilTextEquals
            or RecordedActionKind.WaitUntilTextContains
            || step.ValueAccessorKind == RecorderValueAccessorKind.NumericValue;
    }

    private bool TryResolveTimePickerPart(
        RecordedControlDescriptor descriptor,
        out string locatorValue,
        out UiLocatorKind locatorKind)
    {
        var hint = _options.TimePickerHints.FirstOrDefault(candidate =>
            candidate.LocatorKind == descriptor.LocatorKind
            && string.Equals(candidate.LocatorValue.Trim(), descriptor.LocatorValue.Trim(), StringComparison.Ordinal));
        locatorValue = hint?.Parts.TimePickerLocator ?? string.Empty;
        locatorKind = hint?.Parts.LocatorKind ?? UiLocatorKind.AutomationId;
        return hint is not null && !string.IsNullOrWhiteSpace(locatorValue);
    }

    private bool TryResolveSingleSelectPart(
        RecordedControlDescriptor descriptor,
        out RecorderSingleSelectHint hint)
    {
        hint = _options.SingleSelectHints.FirstOrDefault(candidate =>
            candidate.LocatorKind == descriptor.LocatorKind
            && string.Equals(candidate.LocatorValue.Trim(), descriptor.LocatorValue.Trim(), StringComparison.Ordinal))!;
        return hint is not null;
    }

    private static bool RequiresTimePickerPartValidation(RecordedActionKind actionKind)
    {
        return actionKind is RecordedActionKind.SetTime or RecordedActionKind.WaitUntilTimeEquals;
    }

    private static RecorderValidationStatus MaxStatus(
        RecorderValidationStatus left,
        RecorderValidationStatus right)
    {
        return (RecorderValidationStatus)Math.Max((int)left, (int)right);
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
