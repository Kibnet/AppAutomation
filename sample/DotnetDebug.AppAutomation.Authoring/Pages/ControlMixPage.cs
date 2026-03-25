using AppAutomation.Abstractions;

namespace DotnetDebug.AppAutomation.Authoring.Pages;

/// <summary>
/// Page object for the Control Mix tab, demonstrating multi-page navigation patterns.
/// This secondary page object complements <see cref="MainWindowPage"/> by providing
/// focused access to the series builder functionality.
/// </summary>
/// <remarks>
/// <para>
/// Usage pattern: Navigate from MainWindowPage to ControlMixPage when interacting
/// with series generation features. Both pages share the same underlying window
/// but provide context-specific control access.
/// </para>
/// <example>
/// <code>
/// // Navigate from MainWindowPage to ControlMixPage
/// mainPage.SelectTabItem(p => p.ControlMixTabItem);
/// var controlMixPage = new ControlMixPage(resolver);
/// controlMixPage
///     .EnterText(p => p.SeedInput, "1 2 3")
///     .SelectComboItem(p => p.ModeCombo, "Fibonacci")
///     .ClickButton(p => p.BuildButton);
/// </code>
/// </example>
/// </remarks>
[UiControl("MixInput", UiControlType.TextBox, "MixInput")]
[UiControl("MixModeCombo", UiControlType.ComboBox, "MixModeCombo")]
[UiControl("MixShowDetailsCheck", UiControlType.CheckBox, "MixShowDetailsCheck")]
[UiControl("MixAdvancedToggle", UiControlType.ToggleButton, "MixAdvancedToggle")]
[UiControl("MixDirectionAscendingRadio", UiControlType.RadioButton, "MixDirectionAscendingRadio")]
[UiControl("MixDirectionDescendingRadio", UiControlType.RadioButton, "MixDirectionDescendingRadio")]
[UiControl("MixCountSpinner", UiControlType.TextBox, "MixCountSpinner")]
[UiControl("MixSpeedSlider", UiControlType.Slider, "MixSpeedSlider")]
[UiControl("MixRunButton", UiControlType.Button, "MixRunButton")]
[UiControl("MixClearButton", UiControlType.Button, "MixClearButton")]
[UiControl("SeriesProgressBar", UiControlType.ProgressBar, "SeriesProgressBar")]
[UiControl("SeriesResult", UiControlType.Label, "SeriesResult")]
[UiControl("SeriesList", UiControlType.ListBox, "SeriesList")]
[UiControl("SeriesErrorText", UiControlType.Label, "SeriesErrorText")]
[UiControl("ControlMixTabItem", UiControlType.TabItem, "ControlMixTabItem")]
[UiControl("MainTabs", UiControlType.Tab, "MainTabs")]
public sealed partial class ControlMixPage : UiPage
{
    public ControlMixPage(IUiControlResolver resolver) : base(resolver)
    {
    }

    /// <summary>
    /// Convenience alias for <see cref="MixInput"/> to match the semantic naming "SeedInput".
    /// </summary>
    public ITextBoxControl SeedInput => MixInput;

    /// <summary>
    /// Convenience alias for <see cref="MixModeCombo"/> for semantic naming.
    /// </summary>
    public IComboBoxControl ModeCombo => MixModeCombo;

    /// <summary>
    /// Convenience alias for <see cref="MixRunButton"/> for semantic naming.
    /// </summary>
    public IButtonControl BuildButton => MixRunButton;

    /// <summary>
    /// Convenience alias for <see cref="MixClearButton"/> for semantic naming.
    /// </summary>
    public IButtonControl ClearButton => MixClearButton;

    /// <summary>
    /// Convenience alias for <see cref="SeriesResult"/> for semantic naming.
    /// </summary>
    public ILabelControl ResultLabel => SeriesResult;

    /// <summary>
    /// Convenience alias for <see cref="SeriesProgressBar"/> for semantic naming.
    /// </summary>
    public IProgressBarControl ProgressBar => SeriesProgressBar;

    /// <summary>
    /// Convenience alias for <see cref="SeriesList"/> for semantic naming.
    /// </summary>
    public IListBoxControl OutputList => SeriesList;
}
