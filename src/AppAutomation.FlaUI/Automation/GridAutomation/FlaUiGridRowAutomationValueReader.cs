using AppAutomation.Abstractions;
using FlaUI.Core.AutomationElements;

namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal static class FlaUiGridRowAutomationValueReader
{
    public static string? Read(
        AutomationElement row,
        GridRowAutomationProperty property)
    {
        ArgumentNullException.ThrowIfNull(row);
        return property switch
        {
            GridRowAutomationProperty.AutomationId => TryRead(() => row.AutomationId),
            GridRowAutomationProperty.Name => TryRead(() => row.Name),
            GridRowAutomationProperty.HelpText => TryRead(() => row.HelpText),
            GridRowAutomationProperty.ItemStatus => TryRead(() => row.ItemStatus),
            _ => throw new InvalidOperationException(
                $"Unsupported grid row automation property '{property}'.")
        };
    }

    private static T? TryRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return default;
        }
    }
}
