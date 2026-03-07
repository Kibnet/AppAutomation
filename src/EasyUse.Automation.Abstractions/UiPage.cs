namespace EasyUse.Automation.Abstractions;

public abstract class UiPage
{
    protected UiPage(IUiControlResolver resolver)
    {
        Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    protected IUiControlResolver Resolver { get; }

    internal IUiControlResolver ResolverInternal => Resolver;

    public UiRuntimeCapabilities Capabilities => Resolver.Capabilities;

    protected TControl Resolve<TControl>(UiControlDefinition definition)
        where TControl : class
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Resolver.Resolve<TControl>(definition);
    }
}
