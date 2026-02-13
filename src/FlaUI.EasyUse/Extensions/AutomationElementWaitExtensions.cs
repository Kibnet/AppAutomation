using FlaUI.Core.AutomationElements;
using System.Linq;
using FlaUI.EasyUse.Waiting;

namespace FlaUI.EasyUse.Extensions;

public static class AutomationElementWaitExtensions
{
    public static bool WaitUntilEnabled(this AutomationElement element, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => element.IsEnabled,
            static value => value,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilClickable(this AutomationElement element, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => element.IsEnabled && !element.IsOffscreen,
            static value => value,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilNameEquals(this AutomationElement element, string expectedText, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => element.Name ?? string.Empty,
            actual => string.Equals(actual, expectedText, StringComparison.Ordinal),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilNameContains(this AutomationElement element, string expectedPart, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => element.Name ?? string.Empty,
            actual => actual.Contains(expectedPart, StringComparison.Ordinal),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilHasItems(this ListBox listBox, int minCount, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => listBox.Items.Length,
            actual => actual >= minCount,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilHasItemContaining(this ListBox listBox, string expectedText, int timeoutMs = 5000)
    {
        ArgumentNullException.ThrowIfNull(listBox);

        var waitResult = UiWait.TryUntil(
            () =>
                listBox.Items
                    .Select(item => item.Text ?? item.Name ?? string.Empty)
                    .ToArray(),
            items => items.Any(item => item.Contains(expectedText, StringComparison.Ordinal)),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }
}
