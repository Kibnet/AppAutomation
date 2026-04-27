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
    /// Configures a spinner-like editor that records through a text-box surface.
    /// </summary>
    public static AppAutomationRecorderOptions ConfigureSpinnerProxy(
        this AppAutomationRecorderOptions options,
        string logicalLocatorValue,
        string innerLocatorValue,
        UiLocatorKind logicalLocatorKind = UiLocatorKind.AutomationId,
        UiLocatorKind innerLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = false)
    {
        return options.ConfigureTextBoxProxy(
            logicalLocatorValue,
            innerLocatorValue,
            RecorderActionHint.SpinnerTextBox,
            logicalLocatorKind,
            innerLocatorKind,
            fallbackToName);
    }
}
