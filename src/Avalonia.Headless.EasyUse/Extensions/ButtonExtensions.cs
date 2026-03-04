using FlaUI.Core.AutomationElements;

namespace FlaUI.EasyUse.Extensions;

public static class ButtonExtensions
{
    public static void ClickButton(this Button button, int timeoutMs = 5000)
    {
        if (!button.WaitUntilClickable(timeoutMs))
        {
            throw new TimeoutException($"Button [{button.AutomationId}] is not clickable.");
        }

        button.Invoke();
    }
}