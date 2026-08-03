using AppAutomation.Abstractions;

namespace AppAutomation.Recorder.Avalonia;

internal static class RecorderSpinnerProxyConfiguration
{
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
            && candidate.TargetControlType == UiControlType.TextBox
            && candidate.LocatorKind == logicalLocatorKind
            && string.Equals(candidate.LocatorValue.Trim(), normalizedLocatorValue, StringComparison.Ordinal));
        if (!hasSpinnerHint)
        {
            return false;
        }

        alias = options.LocatorAliases.FirstOrDefault(candidate =>
            candidate.TargetControlType == UiControlType.TextBox
            && candidate.TargetLocatorKind == logicalLocatorKind
            && string.Equals(candidate.TargetLocatorValue.Trim(), normalizedLocatorValue, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(candidate.SourceLocatorValue))!;
        return alias is not null;
    }
}
