using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppAutomation.Abstractions;

/// <summary>
/// Base class for page objects that represent a window or view in the application under test.
/// </summary>
/// <remarks>
/// <para>
/// Page objects encapsulate UI control access and provide a clean API for test automation.
/// Derive from this class to create page-specific types with control properties.
/// </para>
/// <para>
/// Use the source generator with <see cref="UiControlAttribute"/> to automatically generate
/// control properties, or manually call <see cref="Resolve{TControl}"/> to resolve controls.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [UiControl("UserNameInput", UiControlType.TextBox, "txtUserName")]
/// [UiControl("SubmitButton", UiControlType.Button, "btnSubmit")]
/// public partial class LoginPage : UiPage
/// {
///     public LoginPage(IUiControlResolver resolver) : base(resolver) { }
///     // Source-generated properties: UserNameInput, SubmitButton
/// }
/// 
/// // Usage in tests:
/// loginPage.EnterText(p => p.UserNameInput, "testuser")
///          .ClickButton(p => p.SubmitButton);
/// </code>
/// </example>
public abstract class UiPage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UiPage"/> class.
    /// </summary>
    /// <param name="resolver">The control resolver used to locate UI controls.</param>
    /// <param name="logger">Optional logger for diagnostic output. If <see langword="null"/>, a null logger is used.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver"/> is <see langword="null"/>.</exception>
    protected UiPage(IUiControlResolver resolver, ILogger? logger = null)
    {
        Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        Logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets the control resolver used to locate UI controls on this page.
    /// </summary>
    /// <value>The <see cref="IUiControlResolver"/> instance.</value>
    protected IUiControlResolver Resolver { get; }

    /// <summary>
    /// Gets the logger for diagnostic output.
    /// </summary>
    /// <value>The <see cref="ILogger"/> instance.</value>
    protected ILogger Logger { get; }

    /// <summary>
    /// Gets the resolver for internal framework use.
    /// </summary>
    internal IUiControlResolver ResolverInternal => Resolver;

    /// <summary>
    /// Gets the logger for internal framework use.
    /// </summary>
    internal ILogger LoggerInternal => Logger;

    /// <summary>
    /// Gets the runtime capabilities of the underlying automation platform.
    /// </summary>
    /// <value>A <see cref="UiRuntimeCapabilities"/> instance describing supported features.</value>
    public UiRuntimeCapabilities Capabilities => Resolver.Capabilities;

    /// <summary>
    /// Resolves a UI control using the specified definition.
    /// </summary>
    /// <typeparam name="TControl">The control interface type to resolve (e.g., <see cref="IButtonControl"/>).</typeparam>
    /// <param name="definition">The control definition specifying how to locate the control.</param>
    /// <returns>The resolved control instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the control cannot be found or resolved.</exception>
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
