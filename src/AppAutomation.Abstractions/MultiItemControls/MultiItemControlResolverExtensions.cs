namespace AppAutomation.Abstractions;

public static partial class UiControlResolverExtensions
{
    /// <summary>Applies one shared repeated-control catalog to the runtime resolver.</summary>
    public static IUiControlResolver WithMultiItemControls(
        this IUiControlResolver innerResolver,
        MultiItemControlCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentNullException.ThrowIfNull(catalog);

        var adapters = catalog
            .Select(static definition => (IUiControlAdapter)new MultiItemControlAdapter(definition))
            .ToArray();
        return adapters.Length == 0 ? innerResolver : innerResolver.WithAdapters(adapters);
    }
}
