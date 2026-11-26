using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace AppAutomation.Avalonia;

// Shared by the two in-process readers so Recorder and Headless see the same logical children.
internal static class NamedItemSurfacePeers
{
    public static NamedItemSurfaceSnapshot<AutomationPeer> Read(Control surface, Action? check = null)
    {
        check?.Invoke();
        var root = ControlAutomationPeer.CreatePeerForElement(surface);
        var metadata = root.GetItemStatus();
        var items = root.GetChildren()
            .Where(peer => peer.GetAutomationControlType() == AutomationControlType.ListItem)
            .Select(peer =>
            {
                check?.Invoke();
                return new NamedItemSurfaceItem<AutomationPeer>(peer.GetAutomationId() ?? "", peer.GetName() ?? "", peer);
            });
        return new NamedItemSurfaceSnapshot<AutomationPeer>(root.GetAutomationId() ?? "", metadata, items);
    }

    public static bool IsPublished(Control surface) =>
        NamedItemSurfaceMetadata.IsPublished(AutomationProperties.GetItemStatus(surface));

    public static Rect VisibleBounds(AutomationPeer item) =>
        item.IsEnabled() && !item.IsOffscreen() ? item.GetBoundingRectangle() : default;

    public static bool OwnsHit(Control surface, AutomationPeer item, object? hit) =>
        ReferenceEquals(hit, surface) || item is ControlAutomationPeer control && ReferenceEquals(hit, control.Owner);
}
