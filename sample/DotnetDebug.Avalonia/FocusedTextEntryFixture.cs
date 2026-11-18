using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;

namespace DotnetDebug.Avalonia;

internal static class FocusedTextEntryFixture
{
    internal static Control CreateContent()
    {
        var input = new TextBox { Text = "1.00" };
        var other = new TextBox();
        var accepted = new TextBlock { Text = "1" };
        AutomationProperties.SetAutomationId(input, "PrecisionInput");
        AutomationProperties.SetAutomationId(other, "OtherInput");
        AutomationProperties.SetAutomationId(accepted, "AcceptedValue");
        decimal amount = 1m;
        input.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty && input.IsFocused
                && decimal.TryParse(input.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                amount = parsed;
                accepted.Text = amount.ToString(CultureInfo.InvariantCulture);
            }
        };
        input.LostFocus += (_, _) => input.Text = amount.ToString("F2", CultureInfo.InvariantCulture);
        return new StackPanel { Children = { input, other, accepted } };
    }
}
