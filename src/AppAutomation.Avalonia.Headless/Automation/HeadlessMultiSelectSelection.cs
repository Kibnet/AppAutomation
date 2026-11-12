using System.Collections;
using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Internal.AutomationModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;

namespace AppAutomation.Avalonia.Headless.Automation;

internal static class HeadlessMultiSelectSelection
{
    public static string[]? ReadClosed(Control editor)
    {
        if (!GridPropertyValueReader.TryReadProperty(editor, "IsPopupOpen", out var popupOpen) || popupOpen is not bool isOpen
            || !GridPropertyValueReader.TryReadProperty(editor, "SelectionMode", out var mode) || !SupportsMultiple(mode))
            return null;

        if (isOpen || !editor.IsAttachedToVisualTree())
            throw new UiControlResolutionException(UiControlResolutionFailure.Detached,
                $"Multi-select '{AutomationProperties.GetAutomationId(editor)}' is not attached with its popup closed.");
        if (!GridPropertyValueReader.TryReadProperty(editor, "SelectedItems", out var selection))
            return null;

        // Read the native editor's selection collection, not its aggregate text or binding source.
        if (selection is null) return null;
        if (selection is not IEnumerable items || selection is string)
            throw UnreadableSelection(editor);

        GridPropertyValueReader.TryReadProperty(editor, "ItemTemplate", out var template);
        GridPropertyValueReader.TryReadProperty(editor, "DisplayMember", out var member);
        var displayMember = member as string;
        return items.Cast<object?>().Select(item =>
        {
            var effectiveTemplate = item is not null and not Control && string.IsNullOrWhiteSpace(displayMember)
                ? editor.FindDataTemplate(item, template as IDataTemplate)
                : template as IDataTemplate;
            var caption = item is null ? null : ListItemCaptionReader.ReadItemCaption(item, effectiveTemplate, displayMember);
            return !string.IsNullOrWhiteSpace(caption) ? caption.Trim() : throw UnreadableSelection(editor);
        }).ToArray();
    }

    private static bool SupportsMultiple(object? mode) => mode is SelectionMode avaloniaMode
        ? (avaloniaMode & SelectionMode.Multiple) != 0
        : mode?.ToString() is "Multiple" or "Extended";

    private static NotSupportedException UnreadableSelection(Control editor) =>
        new($"Multi-select '{AutomationProperties.GetAutomationId(editor)}' does not expose captions for its complete committed selection.");
}
