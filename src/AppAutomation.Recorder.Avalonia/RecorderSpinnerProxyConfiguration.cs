using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

internal static class RecorderSpinnerProxyConfiguration
{
    public static bool IsInteractivePart(AppAutomationRecorderOptions options, Control control)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(control);

        return options.LocatorAliases.Any(alias =>
            alias.TargetControlType == UiControlType.Spinner
            && SourceLocatorMatches(control, alias)
            && IsConfigured(options, alias.TargetLocatorValue, alias.TargetLocatorKind));
    }

    public static bool IsConfigured(
        AppAutomationRecorderOptions options,
        string logicalLocatorValue,
        UiLocatorKind logicalLocatorKind)
    {
        return TryResolveAlias(options, logicalLocatorValue, logicalLocatorKind, out _);
    }

    public static bool TryResolveAlias(
        AppAutomationRecorderOptions options,
        string logicalLocatorValue,
        UiLocatorKind logicalLocatorKind,
        out RecorderLocatorAlias alias)
    {
        ArgumentNullException.ThrowIfNull(options);

        alias = null!;
        if (string.IsNullOrWhiteSpace(logicalLocatorValue))
        {
            return false;
        }

        var normalizedLocatorValue = logicalLocatorValue.Trim();
        var hasSpinnerHint = options.ControlHints.Any(candidate =>
            candidate.ActionHint == RecorderActionHint.SpinnerTextBox
            && candidate.TargetControlType == UiControlType.Spinner
            && candidate.LocatorKind == logicalLocatorKind
            && string.Equals(candidate.LocatorValue.Trim(), normalizedLocatorValue, StringComparison.Ordinal));
        if (!hasSpinnerHint)
        {
            return false;
        }

        alias = options.LocatorAliases.FirstOrDefault(candidate =>
            candidate.TargetControlType == UiControlType.Spinner
            && candidate.TargetLocatorKind == logicalLocatorKind
            && string.Equals(candidate.TargetLocatorValue.Trim(), normalizedLocatorValue, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(candidate.SourceLocatorValue))!;
        return alias is not null;
    }

    private static bool SourceLocatorMatches(Control control, RecorderLocatorAlias alias)
    {
        var actual = alias.SourceLocatorKind switch
        {
            UiLocatorKind.AutomationId => AutomationProperties.GetAutomationId(control),
            UiLocatorKind.Name => AutomationProperties.GetName(control) ?? control.Name,
            _ => null
        };

        return !string.IsNullOrWhiteSpace(actual)
            && string.Equals(actual.Trim(), alias.SourceLocatorValue.Trim(), StringComparison.Ordinal);
    }
}
