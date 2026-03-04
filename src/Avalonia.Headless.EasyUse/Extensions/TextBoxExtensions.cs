using FlaUI.Core.AutomationElements;

namespace FlaUI.EasyUse.Extensions;

public static class TextBoxExtensions
{
    public static void EnterText(this TextBox textBox, string value, int timeoutMs = 5000)
    {
        if (!textBox.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"TextBox [{textBox.AutomationId}] is not enabled.");
        }

        textBox.Text = string.Empty;
        textBox.Text = value;
    }
}