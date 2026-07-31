using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Eremex.AvaloniaUI.Controls.Editors;

namespace DotnetDebug.Avalonia;

internal static class MultiSelectEditorAutomation
{
    public static void Apply(PopupEditor popupEditor, string automationId)
    {
        ApplyTemplatesRecursively(popupEditor);
        ApplyEditorPartIds(popupEditor, automationId);
        ApplyPopupPartIds(popupEditor, automationId);
        Dispatcher.UIThread.Post(
            () => ApplyEditorPartIds(popupEditor, automationId),
            DispatcherPriority.Loaded);
        popupEditor.PopupOpened += (_, _) => Dispatcher.UIThread.Post(
            () => ApplyPopupPartIds(popupEditor, automationId),
            DispatcherPriority.Loaded);
    }

    private static void ApplyEditorPartIds(PopupEditor popupEditor, string automationId)
    {
        ApplyTemplatesRecursively(popupEditor);
        if (popupEditor.RealEditor is Control input)
        {
            AutomationProperties.SetAutomationId(input, $"{automationId}_Input");
        }

        var openButton = popupEditor
                             .GetVisualDescendants()
                             .OfType<Control>()
                             .FirstOrDefault(static control => control.Name == "PART_PopupOpenButton")
                         ?? popupEditor
                             .GetLogicalDescendants()
                             .OfType<Control>()
                             .FirstOrDefault(static control => control.Name == "PART_PopupOpenButton");
        if (openButton is not null)
        {
            AutomationProperties.SetAutomationId(openButton, $"{automationId}_OpenButton");
        }
    }

    private static void ApplyPopupPartIds(PopupEditor popupEditor, string automationId)
    {
        if (popupEditor.PopupContent is not Control popupContent)
        {
            return;
        }

        ApplyTemplatesRecursively(popupContent);
        if (TopLevel.GetTopLevel(popupContent) is not Control popupRoot)
        {
            return;
        }

        ApplyTemplatesRecursively(popupRoot);
        var listBoxes = EnumerateControls(popupRoot).OfType<ListBox>().ToArray();
        var results = listBoxes
            .FirstOrDefault(static listBox => EnumerateControls(listBox).OfType<CheckBox>().Any())
            ?? listBoxes.FirstOrDefault()
            ?? popupContent;
        AutomationProperties.SetAutomationId(results, $"{automationId}_Results");

        foreach (var button in EnumerateControls(popupRoot).OfType<Button>())
        {
            var text = button.Content?.ToString();
            if (IsApplyButtonText(text))
            {
                AutomationProperties.SetAutomationId(button, $"{automationId}_ApplyButton");
            }
            else if (IsCancelButtonText(text))
            {
                AutomationProperties.SetAutomationId(button, $"{automationId}_CancelButton");
            }
        }
    }

    private static bool IsApplyButtonText(string? text)
    {
        return string.Equals(text, "OK", StringComparison.Ordinal)
            || string.Equals(text, "ОК", StringComparison.Ordinal);
    }

    private static bool IsCancelButtonText(string? text)
    {
        return string.Equals(text, "Cancel", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Отмена", StringComparison.Ordinal);
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;

        foreach (var control in root.GetVisualDescendants().OfType<Control>())
        {
            yield return control;
        }
    }

    private static void ApplyTemplatesRecursively(Control control)
    {
        if (control is TemplatedControl templatedControl)
        {
            templatedControl.ApplyTemplate();
        }

        foreach (var child in control.GetVisualChildren().OfType<Control>())
        {
            ApplyTemplatesRecursively(child);
        }
    }
}
