using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppAutomation.Abstractions;

public abstract class UiPage
{
    protected UiPage(IUiControlResolver resolver, ILogger? logger = null)
    {
        Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        Logger = logger ?? NullLogger.Instance;
    }

    protected IUiControlResolver Resolver { get; }

    protected ILogger Logger { get; }

    internal IUiControlResolver ResolverInternal => Resolver;

    internal ILogger LoggerInternal => Logger;

    public UiRuntimeCapabilities Capabilities => Resolver.Capabilities;

    protected TControl Resolve<TControl>(UiControlDefinition definition)
        where TControl : class
    {
        ArgumentNullException.ThrowIfNull(definition);
        Logger.LogDebug("Resolving control {ControlType} with locator {LocatorKind}={LocatorValue}",
            typeof(TControl).Name, definition.LocatorKind, definition.LocatorValue);
        var control = Resolver.Resolve<TControl>(definition);
        Logger.LogDebug("Resolved control {ControlType} successfully", typeof(TControl).Name);
        return control;
    }
}
