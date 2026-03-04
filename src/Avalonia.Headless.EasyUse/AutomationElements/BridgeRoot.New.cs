namespace Avalonia.Headless.EasyUse.AutomationElements;

public class AutomationElement : global::FlaUI.Core.AutomationElements.AutomationElement
{
    internal AutomationElement(global::Avalonia.Controls.Control control) : base(control)
    {
    }
}

public sealed class Window
{
    internal Window(global::FlaUI.Core.AutomationElements.Window inner)
    {
        Inner = inner;
    }

    internal global::FlaUI.Core.AutomationElements.Window Inner { get; }
}