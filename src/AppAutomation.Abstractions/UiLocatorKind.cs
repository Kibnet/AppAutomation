namespace AppAutomation.Abstractions;

/// <summary>
/// Specifies the strategy used to locate UI controls.
/// </summary>
public enum UiLocatorKind
{
    /// <summary>
    /// Locate controls by their automation identifier (AutomationId property).
    /// This is the most reliable locator strategy for controls with stable IDs.
    /// </summary>
    AutomationId = 0,

    /// <summary>
    /// Locate controls by their display name (Name property).
    /// Use this when controls do not have automation IDs set.
    /// </summary>
    Name = 1
}
