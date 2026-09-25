using AppAutomation.Abstractions;

namespace DotnetDebug.AppAutomation.Configuration;

/// <summary>Provides the shared repeated-control catalog used by both sample runtimes.</summary>
public static class SampleMultiItemAutomation
{
    public static MultiItemControlCatalog CreateCatalog() =>
        new MultiItemControlCatalog().Add(
            MultiItemControlDefinition.ByAutomationIds(
                    "UnitQuantityEditors",
                    "UnitQuantityCollection",
                    "UnitQuantityCollection")
                .WithItems(
                    MultiItemRelativeLocator.ByAutomationId(
                        "UnitQuantityItem",
                        MultiItemRelativeLocatorScope.CollectionRoot),
                    RepeatedItemKeyDefinition.FromItem(UiAutomationValueProperty.ItemStatus))
                .WithSpinner(new MultiItemSpinnerParts(
                    MultiItemRelativeLocator.ByAutomationId(
                        "QuantityEditor",
                        MultiItemRelativeLocatorScope.ItemRoot),
                    MultiItemRelativeLocator.ByAutomationId(
                        "QuantityEditor_Input",
                        MultiItemRelativeLocatorScope.ControlRoot))));
}
