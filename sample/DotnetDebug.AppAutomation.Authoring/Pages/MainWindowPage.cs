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
[UiControl("EremexDemoDataGrid", UiControlType.AutomationElement, "EremexDemoDataGrid")]
[UiControl("EremexDemoDataGridAutomationBridge", UiControlType.Grid, "EremexDemoDataGridAutomationBridge")]
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
[UiControl("ArmDesktopTabItem", UiControlType.TabItem, "ArmDesktopTabItem")]
[UiControl("ArmCopyTextBox", UiControlType.TextBox, "ArmCopyTextBox")]
[UiControl("ArmCopyButton", UiControlType.Button, "ArmCopyButton")]
[UiControl("ArmCopyResultLabel", UiControlType.Label, "ArmCopyResultLabel")]
[UiControl("ArmSearchInput", UiControlType.TextBox, "ArmSearchInput")]
[UiControl("ArmSearchResults", UiControlType.ComboBox, "ArmSearchResults")]
[UiControl("ArmSearchFuzzyToggle", UiControlType.CheckBox, "ArmSearchFuzzyToggle")]
[UiControl("ArmSearchApplyButton", UiControlType.Button, "ArmSearchApplyButton")]
[UiControl("ArmSearchClearButton", UiControlType.Button, "ArmSearchClearButton")]
[UiControl("ArmSearchStatusLabel", UiControlType.Label, "ArmSearchStatusLabel")]
[UiControl("ArmServerPickerInput", UiControlType.TextBox, "ArmServerPickerInput")]
[UiControl("ArmServerPickerResults", UiControlType.ComboBox, "ArmServerPickerResults")]
[UiControl("ArmServerPickerOpenButton", UiControlType.Button, "ArmServerPickerOpenButton")]
[UiControl("ArmServerPickerClearButton", UiControlType.Button, "ArmServerPickerClearButton")]
[UiControl("ArmServerPickerStatusLabel", UiControlType.Label, "ArmServerPickerStatusLabel")]
[UiControl("ArmEremexDataGridHost", UiControlType.AutomationElement, "ArmEremexDataGridHost")]
[UiControl("ArmEremexDataGridControl", UiControlType.AutomationElement, "ArmEremexDataGridControl")]
[UiControl("ArmGridAutomationBridge", UiControlType.Grid, "ArmGridAutomationBridge")]
[UiControl("ArmGridRow0ValueEditor", UiControlType.TextBox, "ArmGridAutomationBridge_Row0_Cell1")]
[UiControl("ArmGridBuildButton", UiControlType.Button, "ArmGridBuildButton")]
[UiControl("ArmGridOpenButton", UiControlType.Button, "ArmGridOpenButton")]
[UiControl("ArmGridSortButton", UiControlType.Button, "ArmGridSortButton")]
[UiControl("ArmGridLoadMoreButton", UiControlType.Button, "ArmGridLoadMoreButton")]
[UiControl("ArmGridCopyButton", UiControlType.Button, "ArmGridCopyButton")]
[UiControl("ArmGridExportButton", UiControlType.Button, "ArmGridExportButton")]
[UiControl("ArmGridEditValueInput", UiControlType.TextBox, "ArmGridEditValueInput")]
[UiControl("ArmGridCommitEditButton", UiControlType.Button, "ArmGridCommitEditButton")]
[UiControl("ArmGridCancelEditButton", UiControlType.Button, "ArmGridCancelEditButton")]
[UiControl("ArmGridStatusLabel", UiControlType.Label, "ArmGridStatusLabel")]
[UiControl("ArmDateRangeFrom", UiControlType.DateTimePicker, "ArmDateRangeFrom")]
[UiControl("ArmDateRangeTo", UiControlType.DateTimePicker, "ArmDateRangeTo")]
[UiControl("ArmDateRangeOpenButton", UiControlType.Button, "ArmDateRangeOpenButton")]
[UiControl("ArmDateRangeApplyButton", UiControlType.Button, "ArmDateRangeApplyButton")]
[UiControl("ArmDateRangeCancelButton", UiControlType.Button, "ArmDateRangeCancelButton")]
[UiControl("ArmDateRangeStatusLabel", UiControlType.Label, "ArmDateRangeStatusLabel")]
[UiControl("ArmNumericRangeFrom", UiControlType.TextBox, "ArmNumericRangeFrom")]
[UiControl("ArmNumericRangeTo", UiControlType.TextBox, "ArmNumericRangeTo")]
[UiControl("ArmNumericRangeOpenButton", UiControlType.Button, "ArmNumericRangeOpenButton")]
[UiControl("ArmNumericRangeApplyButton", UiControlType.Button, "ArmNumericRangeApplyButton")]
[UiControl("ArmNumericRangeCancelButton", UiControlType.Button, "ArmNumericRangeCancelButton")]
[UiControl("ArmNumericRangeStatusLabel", UiControlType.Label, "ArmNumericRangeStatusLabel")]
[UiControl("ArmDialogMessage", UiControlType.Label, "ArmDialogMessage")]
[UiControl("ArmDialogConfirmButton", UiControlType.Button, "ArmDialogConfirmButton")]
[UiControl("ArmDialogCancelButton", UiControlType.Button, "ArmDialogCancelButton")]
[UiControl("ArmDialogDismissButton", UiControlType.Button, "ArmDialogDismissButton")]
[UiControl("ArmDialogResultLabel", UiControlType.Label, "ArmDialogResultLabel")]
[UiControl("ArmNotificationText", UiControlType.Label, "ArmNotificationText")]
[UiControl("ArmNotificationDismissButton", UiControlType.Button, "ArmNotificationDismissButton")]
[UiControl("ArmNotificationStatusLabel", UiControlType.Label, "ArmNotificationStatusLabel")]
[UiControl("ArmFolderExportOpenButton", UiControlType.Button, "ArmFolderExportOpenButton")]
[UiControl("ArmFolderExportPathInput", UiControlType.TextBox, "ArmFolderExportPathInput")]
[UiControl("ArmFolderExportSelectButton", UiControlType.Button, "ArmFolderExportSelectButton")]
[UiControl("ArmFolderExportCancelButton", UiControlType.Button, "ArmFolderExportCancelButton")]
[UiControl("ArmFolderExportStatusLabel", UiControlType.Label, "ArmFolderExportStatusLabel")]
[UiControl("ArmShellNavigationList", UiControlType.ListBox, "ArmShellNavigationList")]
[UiControl("ArmShellPaneTabs", UiControlType.Tab, "ArmShellPaneTabs")]
[UiControl("ArmShellActivePaneLabel", UiControlType.Label, "ArmShellActivePaneLabel")]
[UiControl("ArmLoadingProgressBar", UiControlType.ProgressBar, "ArmLoadingProgressBar")]
[UiControl("ArmReloadButton", UiControlType.Button, "ArmReloadButton")]
[UiControl("ArmLoadingStatusLabel", UiControlType.Label, "ArmLoadingStatusLabel")]
[UiControl("ArmStatusExpanderToggle", UiControlType.ToggleButton, "ArmStatusExpanderToggle")]
[UiControl("ArmStatusLabel", UiControlType.Label, "ArmStatusLabel")]
[UiControl("ArmMetadataToggle", UiControlType.ToggleButton, "ArmMetadataToggle")]
[UiControl("ArmMetadataStatusLabel", UiControlType.Label, "ArmMetadataStatusLabel")]
[UiControl("ArmApprovalToggle", UiControlType.ToggleButton, "ArmApprovalToggle")]
[UiControl("ArmApprovalStatusLabel", UiControlType.Label, "ArmApprovalStatusLabel")]
[UiControl("ArmCrudAddButton", UiControlType.Button, "ArmCrudAddButton")]
[UiControl("ArmCrudEditButton", UiControlType.Button, "ArmCrudEditButton")]
[UiControl("ArmCrudDeleteButton", UiControlType.Button, "ArmCrudDeleteButton")]
[UiControl("ArmSaveButton", UiControlType.Button, "ArmSaveButton")]
[UiControl("ArmSaveCloseButton", UiControlType.Button, "ArmSaveCloseButton")]
[UiControl("ArmCloseButton", UiControlType.Button, "ArmCloseButton")]
[UiControl("ArmActionStatusLabel", UiControlType.Label, "ArmActionStatusLabel")]
public sealed partial class MainWindowPage : UiPage
{
    private static UiControlDefinition HistoryOperationPickerDefinition { get; } =
        new("HistoryOperationPicker", UiControlType.AutomationElement, "HistoryOperationPicker", UiLocatorKind.AutomationId, FallbackToName: false);

    private static UiControlDefinition ArmSearchPickerDefinition { get; } =
        new("ArmSearchPicker", UiControlType.AutomationElement, "ArmSearchPicker", UiLocatorKind.AutomationId, FallbackToName: false);

    private static UiControlDefinition ArmServerSearchPickerDefinition { get; } =
        new("ArmServerSearchPicker", UiControlType.AutomationElement, "ArmServerSearchPicker", UiLocatorKind.AutomationId, FallbackToName: false);

    private static UiControlDefinition ArmDateRangeFilterDefinition { get; } =
        new("ArmDateRangeFilter", UiControlType.AutomationElement, "ArmDateRangeFilter", UiLocatorKind.AutomationId, FallbackToName: false);

    private static UiControlDefinition ArmNumericRangeFilterDefinition { get; } =
        new("ArmNumericRangeFilter", UiControlType.AutomationElement, "ArmNumericRangeFilter", UiLocatorKind.AutomationId, FallbackToName: false);

    private static UiControlDefinition ArmDialogDefinition { get; } =
        new("ArmDialog", UiControlType.AutomationElement, "ArmDialog", UiLocatorKind.AutomationId, FallbackToName: false);

    private static UiControlDefinition ArmNotificationDefinition { get; } =
        new("ArmNotification", UiControlType.AutomationElement, "ArmNotification", UiLocatorKind.AutomationId, FallbackToName: false);

    private static UiControlDefinition ArmFolderExportDefinition { get; } =
        new("ArmFolderExport", UiControlType.AutomationElement, "ArmFolderExport", UiLocatorKind.AutomationId, FallbackToName: false);

    private static UiControlDefinition ArmShellNavigationDefinition { get; } =
        new("ArmShellNavigation", UiControlType.AutomationElement, "ArmShellNavigation", UiLocatorKind.AutomationId, FallbackToName: false);

    public MainWindowPage(IUiControlResolver resolver) : base(resolver)
    {
    }

    public ISearchPickerControl HistoryOperationPicker => Resolve<ISearchPickerControl>(HistoryOperationPickerDefinition);

    public ISearchPickerControl ArmSearchPicker => Resolve<ISearchPickerControl>(ArmSearchPickerDefinition);

    public ISearchPickerControl ArmServerSearchPicker => Resolve<ISearchPickerControl>(ArmServerSearchPickerDefinition);

    public IDateRangeFilterControl ArmDateRangeFilter => Resolve<IDateRangeFilterControl>(ArmDateRangeFilterDefinition);

    public INumericRangeFilterControl ArmNumericRangeFilter => Resolve<INumericRangeFilterControl>(ArmNumericRangeFilterDefinition);

    public IDialogControl ArmDialog => Resolve<IDialogControl>(ArmDialogDefinition);

    public INotificationControl ArmNotification => Resolve<INotificationControl>(ArmNotificationDefinition);

    public IFolderExportControl ArmFolderExport => Resolve<IFolderExportControl>(ArmFolderExportDefinition);

    public IShellNavigationControl ArmShellNavigation => Resolve<IShellNavigationControl>(ArmShellNavigationDefinition);
}
