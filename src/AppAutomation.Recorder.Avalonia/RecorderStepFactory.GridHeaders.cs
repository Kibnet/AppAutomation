using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using static AppAutomation.Recorder.Avalonia.RecorderSemanticValueModel;

namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderStepFactory
{
    private bool TryResolveGridHeaderSemanticValue(Control source, out SemanticValueCandidate candidate,
        out string error, out bool handled)
    {
        candidate = null!;
        error = string.Empty;
        handled = false;
        if (!TryResolveGridHint(source, out var hint, out var grid)) return false;
        var definition = FindGridDefinition(hint);
        var context = definition?.HeaderContext ?? new GridHeaderContextDefinition();
        var owner = source.GetVisualAncestors().OfType<Control>().Prepend(source)
            .TakeWhile(c => !ReferenceEquals(c, grid))
            .FirstOrDefault(c => GridHeaderMetadataReader.IsHeader(c, context));
        if (owner is null) return false;
        handled = true;
        var nodes = grid.GetVisualDescendants().OfType<Control>()
            .Where(c => c.IsEffectivelyVisible && GridHeaderMetadataReader.IsHeader(c, context))
            .Where(c => !c.GetVisualAncestors().OfType<Control>().TakeWhile(a => !ReferenceEquals(a, grid))
                .Any(GridCellMetadataExtractor.IsNativeDataGrid))
            .Select(c =>
            {
                var parts = c.GetVisualDescendants().OfType<TextBlock>()
                    .Where(static t => t.IsEffectivelyVisible && t.Name != "PART_ItemCountElement")
                    .Select(static t => t.Text).Where(static t => t is not null).ToArray();
                return new GridHeaderNode(c, c.TranslatePoint(default, grid)?.Y ?? double.NaN,
                    ExtractTextValue(c) ?? (parts.Length > 0 ? string.Join(" ", parts) : null),
                    context.RuntimeAddressProperty switch
                    {
                        GridRowAutomationProperty.AutomationId => AutomationProperties.GetAutomationId(c),
                        GridRowAutomationProperty.ItemStatus => AutomationProperties.GetItemStatus(c),
                        GridRowAutomationProperty.HelpText => AutomationProperties.GetHelpText(c),
                        _ => null
                    }, AutomationProperties.GetName(c));
            })
            .ToArray();
        var headers = GridHeaderMetadataReader.Read(nodes, context, grid);
        var header = headers.SingleOrDefault(h => ReferenceEquals(h.Source, owner));
        if (header?.Selector is not { } address)
        {
            error = $"Grid '{hint.TargetLocatorValue}' header: {header?.Error ?? "public header metadata is unavailable"}.";
            return false;
        }
        try
        {
            var text = GridHeaderMetadataReader.ResolveText(headers, address, hint.TargetLocatorValue);
            var logical = new GridHeaderSelector(address.Kind, address.Path.Select(p => address.Kind == GridHeaderKind.Band ? p
                : p with { Name = definition?.FindColumnBySourceField(p.Name)?.LogicalName ?? p.Name }).ToArray());
            var locator = _selectorResolver.Resolve(grid, UiControlType.Grid);
            if (!locator.Success || locator.Control is null) { error = locator.Message; return false; }
            candidate = new(locator.Control, RecorderValueKind.Text, RecorderValueAccessorKind.GridHeaderText,
                StringValue: text, Header: logical, DisplayText: text);
            return true;
        }
        catch (InvalidOperationException ex) { error = ex.Message; return false; }
    }
}
