namespace AppAutomation.Abstractions;

/// <summary>
/// Declares a UI control property on a <see cref="UiPage"/>-derived class for source generation.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute to page classes to automatically generate control properties.
/// The source generator creates typed properties that resolve controls using the specified locator.
/// </para>
/// <para>
/// Multiple <see cref="UiControlAttribute"/> instances can be applied to a single class
/// to declare multiple controls.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [UiControl("UserNameInput", UiControlType.TextBox, "txtUserName")]
/// [UiControl("PasswordInput", UiControlType.TextBox, "txtPassword")]
/// [UiControl("LoginButton", UiControlType.Button, "btnLogin")]
/// public partial class LoginPage : UiPage
/// {
///     public LoginPage(IUiControlResolver resolver) : base(resolver) { }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class UiControlAttribute(string propertyName, UiControlType controlType, string locatorValue) : Attribute
{
    /// <summary>
    /// Gets the name of the property to generate.
    /// </summary>
    /// <value>The property name that will be generated on the page class.</value>
    public string PropertyName { get; } = propertyName;

    /// <summary>
    /// Gets the type of UI control.
    /// </summary>
    /// <value>The <see cref="UiControlType"/> determining which control interface to use.</value>
    public UiControlType ControlType { get; } = controlType;

    /// <summary>
    /// Gets the locator value used to find the control.
    /// </summary>
    /// <value>The automation ID or name used to locate the control.</value>
    public string LocatorValue { get; } = locatorValue;

    /// <summary>
    /// Gets or sets the type of locator to use.
    /// </summary>
    /// <value>The <see cref="UiLocatorKind"/>. Defaults to <see cref="UiLocatorKind.AutomationId"/>.</value>
    public UiLocatorKind LocatorKind { get; init; } = UiLocatorKind.AutomationId;

    /// <summary>
    /// Gets or sets whether to fall back to name-based lookup if the primary locator fails.
    /// </summary>
    /// <value><see langword="true"/> to enable fallback; otherwise, <see langword="false"/>. Defaults to <see langword="true"/>.</value>
    public bool FallbackToName { get; init; } = true;
}
