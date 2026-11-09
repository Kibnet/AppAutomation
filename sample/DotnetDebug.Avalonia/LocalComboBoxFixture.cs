using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;
using Eremex.AvaloniaUI.Controls.Editors;

namespace DotnetDebug.Avalonia;

public static class LocalComboBoxFixture
{
    public const string EnvironmentVariable = "APPAUTOMATION_LOCAL_COMBO_FIXTURE";

    public static Control CreateContent()
    {
        var editor = new ComboBoxEditor
        {
            ItemsSource = new[] { new Option("Вариант A", Choice.First), new Option("Вариант B", Choice.Second) },
            DisplayMember = nameof(Option.Name),
            ValueMember = nameof(Option.Value),
            EditorValue = Choice.First,
            IsTextEditable = true,
            Width = 250,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left
        };
        AutomationProperties.SetAutomationId(editor, "LocalChoice");
        var applied = new TextBlock();
        applied.Bind(TextBlock.TextProperty,
            new Binding(nameof(ComboBoxEditor.EditorValue)) { Source = editor, Mode = BindingMode.OneWay });
        AutomationProperties.SetAutomationId(applied, "LocalChoiceApplied");
        editor.TemplateApplied += (_, _) => RegisterParts();
        editor.LayoutUpdated += (_, _) => RegisterParts();
        return new StackPanel { Margin = new global::Avalonia.Thickness(24), Spacing = 12, Children = { editor, applied } };

        void RegisterParts()
        {
            if (editor.RealEditor is Control input)
                AutomationProperties.SetAutomationId(input, "LocalChoice_Input");
            var open = editor.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(static part => part.Name == "PART_PopupOpenButton");
            if (open is not null)
                AutomationProperties.SetAutomationId(open, "LocalChoice_OpenButton");
            if (editor.PopupContent is Control popup)
            {
                var list = popup as ListBox ?? popup.GetVisualDescendants().OfType<ListBox>()
                    .FirstOrDefault(static part => part.Name == "PART_ItemsSelector");
                if (list is not null)
                    AutomationProperties.SetAutomationId(list, "LocalChoice_Results");
            }
        }
    }

    public enum Choice { First, Second }

    public sealed record Option(string Name, Choice Value);

}
