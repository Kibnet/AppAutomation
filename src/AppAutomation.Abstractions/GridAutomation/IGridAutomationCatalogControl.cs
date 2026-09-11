namespace AppAutomation.Abstractions;

/// <summary>Publishes the shared grid catalog fingerprint used to configure one logical runtime grid.</summary>
public interface IGridAutomationCatalogControl : IGridControl
{
    string GridAutomationFingerprint { get; }
}
