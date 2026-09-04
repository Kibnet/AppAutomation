using AppAutomation.Abstractions;

namespace DotnetDebug.AppAutomation.Configuration;

/// <summary>
/// Provides the single grid automation catalog consumed by the sample Recorder and runtimes.
/// </summary>
public static class SampleGridAutomation
{
    public static GridAutomationCatalog CreateRecorderCatalog() => CreateCatalog();

    public static GridAutomationCatalog CreateHeadlessCatalog() => CreateCatalog();

    public static GridAutomationCatalog CreateFlaUiCatalog() => CreateCatalog();

    private static GridAutomationCatalog CreateCatalog()
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
                        "GridComboAutomationBridge",
                        "GridComboAutomationBridge",
                        "GridComboAutomationBridge")
                    .WithColumns(
                        GridColumnDefinition.Auto("Key"),
                        GridColumnDefinition.Auto("State")
                            .AsValue(GridCellValueKind.Selection)
                            .EditWith(GridCellEditorKind.ComboBox))
                    .IdentifyRowsBy("Key"))
            .Add(
                GridAutomationDefinition.ByAutomationIds(
                        "SearchPickerGridAutomationBridge",
                        "SearchPickerGridAutomationBridge",
                        "SearchPickerGridAutomationBridge")
                    .WithColumns(
                        GridColumnDefinition.Auto("Key"),
                        GridColumnDefinition.Auto("SelectedValue")
                            .AsValue(GridCellValueKind.Reference)
                            .EditWith(
                                GridCellEditorKind.SearchPicker,
                                new GridCellEditorParts(
                                    Input: new GridRelativeLocator("SearchPickerGridEditor_Input"),
                                    Results: new GridRelativeLocator(
                                        "SearchPickerGridEditor_Results",
                                        GridRelativeLocatorScope.DetachedPopup),
                                    OpenButton: new GridRelativeLocator("SearchPickerGridEditor_OpenButton"))))
                    .IdentifyRowsBy("Key"));
    }
}
