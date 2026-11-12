using System;
using System.Globalization;
using System.Linq;
using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Eremex.AvaloniaUI.Controls.Editors;

namespace DotnetDebug.Avalonia;

internal static class ClosedFilterSelectionFixture
{
    internal static Control CreateContent()
    {
        string[] values = ["Open", "Alpha; Beta", "Archived"];
        var editor = new ComboBoxEditor
        {
            ItemsSource = values, SelectedItems = values[..2].ToList(), SelectionMode = ItemSelectionMode.Multiple,
            IsTextEditable = false, DisplayTextConverter = new ConstantCaption(), Width = 240
        };
        AutomationProperties.SetAutomationId(editor, "ArmStatusFilter");
        AutomationProperties.SetControlTypeOverride(editor, AutomationControlType.ComboBox);
        AutomationProperties.SetAccessibilityView(editor, AccessibilityView.Control);
        return new StackPanel
        {
            Children =
            {
                editor,
                Button("PublishSelection", Publish),
                Button("ChangeSelection", () => { editor.SelectedItems = new[] { values[2] }; Publish(); }),
                Button("ClearSelection", () => { editor.SelectedItems = Array.Empty<string>(); Publish(); }),
                Button("RemoveSelectionPublication", () => editor.ClearValue(AutomationProperties.ItemStatusProperty))
            }
        };

        void Publish() => AutomationProperties.SetItemStatus(editor,
            MultiSelectSelectionValue.ToAutomationValue(editor.SelectedItems.Cast<string>()));

        static Button Button(string id, Action click)
        {
            var button = new Button { Content = id };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += (_, _) => click();
            return button;
        }
    }

    private sealed class ConstantCaption : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => "Archived";
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
