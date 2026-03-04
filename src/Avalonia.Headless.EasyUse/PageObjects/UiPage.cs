namespace Avalonia.Headless.EasyUse.PageObjects;

public abstract class UiPage : global::FlaUI.EasyUse.PageObjects.UiPage
{
    protected UiPage(global::FlaUI.Core.AutomationElements.Window window, global::FlaUI.Core.Conditions.ConditionFactory conditionFactory)
        : base(window, conditionFactory)
    {
    }
}