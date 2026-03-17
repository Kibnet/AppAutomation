namespace AppAutomation.Abstractions;

/// <summary>
/// Resolves UI controls from their definitions to typed control instances.
/// </summary>
/// <remarks>
/// <para>
/// This is the core abstraction that bridges page object control definitions to
/// platform-specific control implementations. Each automation adapter (FlaUI, Avalonia Headless, etc.)
/// provides its own implementation.
/// </para>
/// <para>
/// Use <see cref="UiControlResolverExtensions.WithAdapters"/> to add custom control adapters
/// that can transform or compose controls.
/// </para>
/// </remarks>
public interface IUiControlResolver
{
    /// <summary>
    /// Gets the runtime capabilities of the underlying automation platform.
    /// </summary>
    /// <value>A <see cref="UiRuntimeCapabilities"/> instance describing supported features.</value>
    UiRuntimeCapabilities Capabilities { get; }

    /// <summary>
    /// Resolves a control by its definition to the requested control type.
    /// </summary>
    /// <typeparam name="TControl">The control interface type to resolve to.</typeparam>
    /// <param name="definition">The control definition specifying how to locate the control.</param>
    /// <returns>The resolved control instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the control cannot be found or does not match the requested type.</exception>
    TControl Resolve<TControl>(UiControlDefinition definition)
        where TControl : class;
}
