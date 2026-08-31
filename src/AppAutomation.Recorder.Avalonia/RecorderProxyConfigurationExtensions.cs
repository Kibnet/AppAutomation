using AppAutomation.Abstractions;

namespace AppAutomation.Recorder.Avalonia;

public static class RecorderProxyConfigurationExtensions
{
    /// <summary>
    /// Configures a logical control locator that should capture through a typed inner control locator.
    /// </summary>
    public static AppAutomationRecorderOptions ConfigureProxy(
        this AppAutomationRecorderOptions options,
        string logicalLocatorValue,
        string innerLocatorValue,
        UiControlType targetControlType,
        RecorderActionHint actionHint = RecorderActionHint.None,
        UiLocatorKind logicalLocatorKind = UiLocatorKind.AutomationId,
        UiLocatorKind innerLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalLocatorValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(innerLocatorValue);

        options.ControlHints.Add(new RecorderControlHint(
            logicalLocatorValue.Trim(),
            actionHint,
            targetControlType,
            logicalLocatorKind,
            fallbackToName));

        options.LocatorAliases.Add(new RecorderLocatorAlias(
            innerLocatorValue.Trim(),
            logicalLocatorValue.Trim(),
            targetControlType,
            innerLocatorKind,
            logicalLocatorKind,
            fallbackToName));

        return options;
    }

    /// <summary>
    /// Configures a text-box proxy mapping.
    /// </summary>
    public static AppAutomationRecorderOptions ConfigureTextBoxProxy(
        this AppAutomationRecorderOptions options,
        string logicalLocatorValue,
        string innerLocatorValue,
        RecorderActionHint actionHint = RecorderActionHint.None,
        UiLocatorKind logicalLocatorKind = UiLocatorKind.AutomationId,
        UiLocatorKind innerLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = false)
    {
        return options.ConfigureProxy(
            logicalLocatorValue,
            innerLocatorValue,
            UiControlType.TextBox,
            actionHint,
            logicalLocatorKind,
            innerLocatorKind,
            fallbackToName);
    }

    /// <summary>
    /// Configures a button proxy mapping.
    /// </summary>
    public static AppAutomationRecorderOptions ConfigureButtonProxy(
        this AppAutomationRecorderOptions options,
        string logicalLocatorValue,
        string innerLocatorValue,
        UiLocatorKind logicalLocatorKind = UiLocatorKind.AutomationId,
        UiLocatorKind innerLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = false)
    {
        return options.ConfigureProxy(
            logicalLocatorValue,
            innerLocatorValue,
            UiControlType.Button,
            RecorderActionHint.None,
            logicalLocatorKind,
            innerLocatorKind,
            fallbackToName);
    }

    /// <summary>
    /// Configures a label proxy mapping.
    /// </summary>
    public static AppAutomationRecorderOptions ConfigureLabelProxy(
        this AppAutomationRecorderOptions options,
        string logicalLocatorValue,
        string innerLocatorValue,
        UiLocatorKind logicalLocatorKind = UiLocatorKind.AutomationId,
        UiLocatorKind innerLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = false)
    {
        return options.ConfigureProxy(
            logicalLocatorValue,
            innerLocatorValue,
            UiControlType.Label,
            RecorderActionHint.None,
            logicalLocatorKind,
            innerLocatorKind,
            fallbackToName);
    }

    /// <summary>
    /// Configures a list-box proxy mapping.
    /// </summary>
    public static AppAutomationRecorderOptions ConfigureListBoxProxy(
        this AppAutomationRecorderOptions options,
        string logicalLocatorValue,
        string innerLocatorValue,
        UiLocatorKind logicalLocatorKind = UiLocatorKind.AutomationId,
        UiLocatorKind innerLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = false)
    {
        return options.ConfigureProxy(
            logicalLocatorValue,
            innerLocatorValue,
            UiControlType.ListBox,
            RecorderActionHint.None,
            logicalLocatorKind,
            innerLocatorKind,
            fallbackToName);
    }

    /// <summary>
    /// Configures a combo-box proxy mapping.
    /// </summary>
    public static AppAutomationRecorderOptions ConfigureComboBoxProxy(
        this AppAutomationRecorderOptions options,
        string logicalLocatorValue,
        string innerLocatorValue,
        UiLocatorKind logicalLocatorKind = UiLocatorKind.AutomationId,
        UiLocatorKind innerLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = false)
    {
        return options.ConfigureProxy(
            logicalLocatorValue,
            innerLocatorValue,
            UiControlType.ComboBox,
            RecorderActionHint.None,
            logicalLocatorKind,
            innerLocatorKind,
            fallbackToName);
    }

    /// <summary>
    /// Configures a date-time picker proxy mapping.
    /// </summary>
    public static AppAutomationRecorderOptions ConfigureDateTimePickerProxy(
        this AppAutomationRecorderOptions options,
        string logicalLocatorValue,
        string innerLocatorValue,
        UiLocatorKind logicalLocatorKind = UiLocatorKind.AutomationId,
        UiLocatorKind innerLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = false)
    {
        return options.ConfigureProxy(
            logicalLocatorValue,
            innerLocatorValue,
            UiControlType.DateTimePicker,
            RecorderActionHint.None,
            logicalLocatorKind,
            innerLocatorKind,
            fallbackToName);
    }

    /// <summary>
    /// Configures a logical date-time picker whose committed value and popup calendar are separate parts.
    /// </summary>
    public static AppAutomationRecorderOptions ConfigureDateTimePickerProxy(
        this AppAutomationRecorderOptions options,
        string logicalLocatorValue,
        DatePickerParts parts,
        UiLocatorKind logicalLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalLocatorValue);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.RootLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.ValueLocator);

        var logicalLocator = logicalLocatorValue.Trim();
        options.ControlHints.Add(new RecorderControlHint(
            logicalLocator,
            RecorderActionHint.None,
            UiControlType.DateTimePicker,
            logicalLocatorKind,
            fallbackToName));
        options.DatePickerHints.Add(new RecorderDatePickerHint(
            logicalLocator,
            parts,
            logicalLocatorKind,
            fallbackToName));

        AddDatePickerAlias(options, logicalLocator, logicalLocatorKind, fallbackToName, parts.ValueLocator, parts.LocatorKind);
        if (!string.IsNullOrWhiteSpace(parts.CalendarLocator))
        {
            AddDatePickerAlias(options, logicalLocator, logicalLocatorKind, fallbackToName, parts.CalendarLocator, parts.LocatorKind);
        }

        return options;
    }

    private static void AddDatePickerAlias(
        AppAutomationRecorderOptions options,
        string logicalLocatorValue,
        UiLocatorKind logicalLocatorKind,
        bool fallbackToName,
        string sourceLocatorValue,
        UiLocatorKind sourceLocatorKind)
    {
        if (sourceLocatorKind == logicalLocatorKind
            && string.Equals(sourceLocatorValue.Trim(), logicalLocatorValue, StringComparison.Ordinal))
        {
            return;
        }

        options.LocatorAliases.Add(new RecorderLocatorAlias(
            sourceLocatorValue.Trim(),
            logicalLocatorValue,
            UiControlType.DateTimePicker,
            sourceLocatorKind,
            logicalLocatorKind,
            fallbackToName));
    }

    /// <summary>
    /// Configures a logical spinner that records through a writable text-box surface.
    /// </summary>
    public static AppAutomationRecorderOptions ConfigureSpinnerProxy(
        this AppAutomationRecorderOptions options,
        string logicalLocatorValue,
        string innerLocatorValue,
        UiLocatorKind logicalLocatorKind = UiLocatorKind.AutomationId,
        UiLocatorKind innerLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = false)
    {
        return options.ConfigureProxy(
            logicalLocatorValue,
            innerLocatorValue,
            UiControlType.Spinner,
            RecorderActionHint.SpinnerTextBox,
            logicalLocatorKind,
            innerLocatorKind,
            fallbackToName);
    }
}
