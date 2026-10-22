using AppAutomation.Abstractions;

namespace DotnetDebug.AppAutomation.Configuration;

/// <summary>
/// Provides the single grid automation catalog consumed by the sample Recorder and runtimes.
/// </summary>
public static class SampleGridAutomation
{
    public static GridAutomationCatalog CreateMultilineFixtureCatalog(bool useCommitTarget, bool commitTargetOnLeft = false) =>
        new GridAutomationCatalog().AddRange(CreateCatalog(useKeyboardInputForNumbers: true).Select(definition =>
        {
            if (definition.PagePropertyName != "ArmComplexDataGridControl") return definition;
            var columns = definition.Columns.Select(column => column.LogicalName == "Value"
                    ? column.EditWith(GridCellEditorKind.Text, new GridCellEditorParts(
                        Input: new GridRelativeLocator("ArmGridMultilineEditor_Input"),
                        CommitTarget: useCommitTarget
                            ? new GridRelativeLocator("ArmGridRowCommitTarget", GridRelativeLocatorScope.Row)
                            : null))
                    : column);
            if (commitTargetOnLeft)
                columns = columns.OrderBy(column => column.LogicalName == "IsApproved" ? 0 : column.LogicalName == "Value" ? 2 : 1);
            return definition.WithColumns(columns.ToArray());
        }));

    public static GridAutomationCatalog CreateRecorderCatalog() => CreateCatalog(useKeyboardInputForNumbers: true);

    public static GridAutomationCatalog CreateHeadlessCatalog() => CreateCatalog(useKeyboardInputForNumbers: true);

    public static GridAutomationCatalog CreateFlaUiCatalog(bool useKeyboardInputForNumbers = true) =>
        CreateCatalog(useKeyboardInputForNumbers);

    private static GridAutomationCatalog CreateCatalog(bool useKeyboardInputForNumbers)
    {
        return new GridAutomationCatalog()
            .Add(
                GridAutomationDefinition.ByAutomationIds(
                        "DemoDataGrid",
                        "DemoDataGrid",
                        "DemoDataGrid")
                    .WithColumns(
                        GridColumnDefinition.Auto("Row"),
                        GridColumnDefinition.Auto("Value").AsValue(GridCellValueKind.Number),
                        GridColumnDefinition.Auto("Parity"))
                    .IdentifyRowsBy("Row"))
            .Add(
                GridAutomationDefinition.ByAutomationIds(
                        "ArmComplexDataGridControl",
                        "ArmComplexDataGridControl",
                        "ArmComplexDataGridControl")
                    .WithColumns(
                        GridColumnDefinition.Auto("Key")
                            .ReadIdentityFromRow(GridRowAutomationProperty.ItemStatus),
                        GridColumnDefinition.Auto("Value")
                            .AsValue(GridCellValueKind.Text)
                            .EditWith(GridCellEditorKind.Text),
                        GridColumnDefinition.Auto("RequiredAmount")
                            .AtRuntime("Required")
                            .AsValue(GridCellValueKind.Number)
                            .EditWith(
                                 GridCellEditorKind.Number,
                                 new GridCellEditorParts(
                                     Input: new GridRelativeLocator("ArmGridRequiredEditor_Input"),
                                     CommitTarget: new GridRelativeLocator(
                                         "ArmGridRowCommitTarget",
                                         GridRelativeLocatorScope.Row),
                                     UseKeyboardInput: useKeyboardInputForNumbers)),
                        GridColumnDefinition.Auto("IsApproved")
                            .AtRuntime("Approved")
                            .AsValue(GridCellValueKind.Boolean)
                            .EditWith(
                                GridCellEditorKind.CheckBox,
                                new GridCellEditorParts(
                                    Input: new GridRelativeLocator("ArmGridApprovedEditor"))),
                        GridColumnDefinition.Auto("State")
                            .AsValue(GridCellValueKind.Selection)
                            .EditWith(
                                GridCellEditorKind.ComboBox,
                                new GridCellEditorParts(
                                    Input: new GridRelativeLocator("ArmGridStateEditor"))),
                        GridColumnDefinition.Auto("Product")
                            .AsValue(GridCellValueKind.Reference)
                            .EditWith(
                                GridCellEditorKind.SearchPicker,
                                new GridCellEditorParts(
                                    Input: new GridRelativeLocator("ArmGridProductEditor_Input"),
                                    Results: new GridRelativeLocator(
                                        "ArmGridProductEditor_Results",
                                        GridRelativeLocatorScope.DetachedPopup),
                                    OpenButton: new GridRelativeLocator("ArmGridProductEditor_OpenButton"))),
                        GridColumnDefinition.Auto("ScheduledDate")
                            .AtRuntime("Date")
                            .AsValue(GridCellValueKind.Date)
                            .EditWith(
                                GridCellEditorKind.Date,
                                new GridCellEditorParts(
                                    Input: new GridRelativeLocator("ArmGridDateEditor_Input"),
                                    Results: new GridRelativeLocator(
                                        "ArmGridDateEditor_Calendar",
                                        GridRelativeLocatorScope.DetachedPopup),
                                    OpenButton: new GridRelativeLocator("ArmGridDateEditor_OpenButton"))),
                        GridColumnDefinition.Auto("ScheduledTime")
                            .AtRuntime("Time")
                            .AsValue(GridCellValueKind.Time)
                            .EditWith(
                                GridCellEditorKind.Time,
                                new GridCellEditorParts(
                                    Input: new GridRelativeLocator("ArmGridTimeEditor"))))
                    .IdentifyRowsBy("Key"));
    }
}
