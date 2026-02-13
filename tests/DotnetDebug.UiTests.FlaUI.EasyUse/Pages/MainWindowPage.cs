using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.EasyUse.PageObjects;

namespace DotnetDebug.UiTests.FlaUI.EasyUse.Pages;

[UiControl("NumbersInput", UiControlType.TextBox, "NumbersInput")]
[UiControl("CalculateButton", UiControlType.Button, "CalculateButton")]
[UiControl("HistoryFilterInput", UiControlType.TextBox, "HistoryFilterInput")]
[UiControl("OperationCombo", UiControlType.ComboBox, "OperationCombo")]
[UiControl("UseAbsoluteValuesCheck", UiControlType.CheckBox, "UseAbsoluteValuesCheck")]
[UiControl("ShowStepsCheck", UiControlType.CheckBox, "ShowStepsCheck")]
[UiControl("ApplyFilterButton", UiControlType.Button, "ApplyFilterButton")]
[UiControl("ClearHistoryButton", UiControlType.Button, "ClearHistoryButton")]
[UiControl("ModeLabel", UiControlType.Label, "ModeLabel")]
[UiControl("HistoryList", UiControlType.ListBox, "HistoryList")]
[UiControl("ResultText", UiControlType.Label, "ResultText")]
[UiControl("ErrorText", UiControlType.Label, "ErrorText")]
[UiControl("StepsList", UiControlType.ListBox, "StepsList")]
public sealed partial class MainWindowPage(Window window, ConditionFactory conditionFactory)
    : UiPage(window, conditionFactory)
{
}
