namespace AppAutomation.TUnit;

/// <summary>
/// Represents a UI test session that manages the lifecycle of an application under test.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface to create session types for different automation scenarios.
/// Sessions are responsible for launching the application, providing access to the control
/// resolver, and cleaning up resources when disposed.
/// </para>
/// <para>
/// Common implementations include desktop application sessions (using FlaUI) and headless
/// sessions (using Avalonia Headless).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class DesktopSession : IUiTestSession
/// {
///     private readonly Process _process;
///     public IUiControlResolver Resolver { get; }
///     
///     public void Dispose()
///     {
///         _process?.Kill();
///         _process?.Dispose();
///     }
/// }
/// </code>
/// </example>
public interface IUiTestSession : IDisposable
{
}
