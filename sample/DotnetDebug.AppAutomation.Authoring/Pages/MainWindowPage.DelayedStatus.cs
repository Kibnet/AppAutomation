using AppAutomation.Abstractions;

namespace DotnetDebug.AppAutomation.Authoring.Pages;

public sealed partial class MainWindowPage
{
    private static UiControlDefinition ShowDelayedStatusButtonDefinition { get; } =
        new("ShowDelayedStatusButton", UiControlType.Button, "ShowDelayedStatusButton", UiLocatorKind.AutomationId, FallbackToName: false);

    private static UiControlDefinition DelayedStatusLabelDefinition { get; } =
        new("DelayedStatusLabel", UiControlType.Label, "DelayedStatusLabel", UiLocatorKind.AutomationId, FallbackToName: false);

    public IButtonControl ShowDelayedStatusButton => Resolve<IButtonControl>(ShowDelayedStatusButtonDefinition);

    public ILabelControl DelayedStatusLabel => Resolve<ILabelControl>(DelayedStatusLabelDefinition);
}
