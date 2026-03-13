using AppAutomation.Abstractions;

namespace DotnetDebug.AppAutomation.Authoring.Pages;

[UiControl("NumbersInput", UiControlType.TextBox, "NumbersInput")]
[UiControl("CalculateButton", UiControlType.Button, "CalculateButton")]
[UiControl("MainTabs", UiControlType.Tab, "MainTabs")]
[UiControl("MathTabItem", UiControlType.TabItem, "MathTabItem")]
[UiControl("ControlMixTabItem", UiControlType.TabItem, "ControlMixTabItem")]
[UiControl("DateTimeTabItem", UiControlType.TabItem, "DateTimeTabItem")]
[UiControl("HierarchyTabItem", UiControlType.TabItem, "HierarchyTabItem")]
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
[UiControl("DataGridTabItem", UiControlType.TabItem, "DataGridTabItem")]
[UiControl("DemoDataGrid", UiControlType.Grid, "DemoDataGrid")]
[UiControl("DataGridRowsInput", UiControlType.TextBox, "DataGridRowsInput")]
[UiControl("BuildGridButton", UiControlType.Button, "BuildGridButton")]
[UiControl("ClearGridButton", UiControlType.Button, "ClearGridButton")]
[UiControl("DataGridSelectRowInput", UiControlType.TextBox, "DataGridSelectRowInput")]
[UiControl("SelectGridRowButton", UiControlType.Button, "SelectGridRowButton")]
[UiControl("GridResultLabel", UiControlType.Label, "GridResultLabel")]
[UiControl("GridSelectionLabel", UiControlType.Label, "GridSelectionLabel")]
[UiControl("DataGridErrorText", UiControlType.Label, "DataGridErrorText")]
[UiControl("CalendarTabItem", UiControlType.TabItem, "CalendarTabItem")]
[UiControl("DemoCalendar", UiControlType.Calendar, "DemoCalendar")]
[UiControl("CalendarReadButton", UiControlType.Button, "CalendarReadButton")]
[UiControl("CalendarDateInput", UiControlType.TextBox, "CalendarDateInput")]
[UiControl("SetCalendarDateButton", UiControlType.Button, "SetCalendarDateButton")]
[UiControl("ClearCalendarDateButton", UiControlType.Button, "ClearCalendarDateButton")]
[UiControl("CalendarResultLabel", UiControlType.Label, "CalendarResultLabel")]
[UiControl("CalendarErrorText", UiControlType.Label, "CalendarErrorText")]
[UiControl("StartDatePicker", UiControlType.DateTimePicker, "StartDatePicker")]
[UiControl("EndDatePicker", UiControlType.DateTimePicker, "EndDatePicker")]
[UiControl("DateDiffButton", UiControlType.Button, "DateDiffButton")]
[UiControl("DateResult", UiControlType.Label, "DateResult")]
[UiControl("DateDiffList", UiControlType.ListBox, "DateDiffList")]
[UiControl("DateErrorText", UiControlType.Label, "DateErrorText")]
[UiControl("DemoTree", UiControlType.Tree, "DemoTree")]
[UiControl("HierarchyResultLabel", UiControlType.Label, "HierarchyResultLabel")]
[UiControl("HierarchySelectionList", UiControlType.ListBox, "HierarchySelectionList")]
[UiControl("HierarchyClearSelectionButton", UiControlType.Button, "HierarchyClearSelectionButton")]
public sealed partial class MainWindowPage : UiPage
{
    private static UiControlDefinition HistoryOperationPickerDefinition { get; } =
        new("HistoryOperationPicker", UiControlType.AutomationElement, "HistoryOperationPicker", UiLocatorKind.AutomationId, FallbackToName: false);

    public MainWindowPage(IUiControlResolver resolver) : base(resolver)
    {
    }

    public ISearchPickerControl HistoryOperationPicker => Resolve<ISearchPickerControl>(HistoryOperationPickerDefinition);
}
