namespace AppAutomation.Abstractions;

public static partial class UiControlResolverExtensions
{
    /// <summary>
    /// Applies one shared grid automation catalog to the runtime resolver.
    /// </summary>
    public static IUiControlResolver WithGridAutomation(
        this IUiControlResolver innerResolver,
        GridAutomationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentNullException.ThrowIfNull(catalog);

        var adapters = catalog
            .Select(definition => (IUiControlAdapter)new GridAutomationAdapter(definition, catalog.Fingerprint))
            .ToArray();
        return adapters.Length == 0
            ? innerResolver
            : innerResolver.WithAdapters(adapters);
    }
}
