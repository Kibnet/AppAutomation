using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace DotnetDebug.Avalonia;

// A small drawn surface in the existing sample window. Its ordinary hit handler does not use UIA.
public static class NamedItemSurfaceFixture
{
    public const string EnvironmentVariable = "APPAUTOMATION_NAMED_ITEMS_FIXTURE";

    internal static TabItem CreateTab()
    {
        var result = new TextBlock { Text = "None" };
        AutomationProperties.SetAutomationId(result, "OpenedEquipment");
        var viewport = new ScrollViewer { Height = 80, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var count = 0;
        var surface = new EquipmentSurface(key =>
        {
            result.Text = $"{key}:{++count}";
            viewport.Content = null;
        });
        viewport.Content = surface;

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        AddAction("ReverseEquipment", "Reverse equipment", () =>
        {
            surface.Reverse();
            viewport.Content = surface;
        });
        AddAction("DuplicateEquipment", "Duplicate name", surface.DuplicateName);
        AddAction("OmitEquipmentPeer", "Incomplete publication", surface.OmitPeer);

        return new TabItem
        {
            Header = "Named items",
            Content = new StackPanel { Margin = new Thickness(12), Spacing = 8, Children = { actions, viewport, result } }
        };

        void AddAction(string id, string caption, Action action)
        {
            var button = new Button { Content = caption };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += (_, _) => action();
            actions.Children.Add(button);
        }
    }

    private sealed record Equipment(string Key, string Name);

    private sealed class EquipmentSurface : Control
    {
        private const double RowHeight = 32;
        private const double HeaderWidth = 164;
        private readonly List<Equipment> _items =
            [new("line-laser", "Laser 02"), new("line-press", "Press 05"),
             new("line-bender", "Bender 01"), new("line-saw", "Saw 08")];
        private long _revision;
        private bool _omitPeer;

        public EquipmentSurface(Action<string> open)
        {
            Width = 300;
            AutomationProperties.SetAutomationId(this, "EquipmentSurface");
            Publish();
            DoubleTapped += (_, args) =>
            {
                var point = args.GetPosition(this);
                var index = _items.FindIndex(item => HeaderBounds(item).Contains(point));
                if (index < 0)
                    return;
                args.Handled = true;
                open(_items[index].Key);
            };
        }

        public override void Render(DrawingContext context)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                var row = new Rect(0, i * RowHeight, Bounds.Width, RowHeight);
                context.FillRectangle(i % 2 == 0 ? Brushes.Gainsboro : Brushes.WhiteSmoke, row);
                context.DrawText(new FormattedText(_items[i].Name, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.Black), new Point(8, row.Top + 7));
                context.FillRectangle(Brushes.SteelBlue, new Rect(HeaderWidth + 12, row.Top + 12, 100, 8));
            }
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new SurfacePeer(this);

        public void Reverse()
        {
            _items.Reverse();
            Publish();
        }

        public void DuplicateName()
        {
            _items.Add(new Equipment("line-other-laser", "Laser 02"));
            Publish();
        }

        public void OmitPeer()
        {
            _omitPeer = true;
            Publish();
        }

        private void Publish()
        {
            Height = _items.Count * RowHeight;
            AutomationProperties.SetItemStatus(this, NamedItemSurfaceMetadata.Format(_items.Count, _revision++));
            if (ControlAutomationPeer.FromElement(this) is SurfacePeer peer)
                peer.UpdateChildren();
            InvalidateVisual();
        }

        private Rect HeaderBounds(Equipment item)
        {
            var index = _items.IndexOf(item);
            return index >= 0 ? new Rect(0, index * RowHeight, HeaderWidth, RowHeight) : default;
        }

        private Rect VisibleHeader(Equipment item)
        {
            if (!IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { } window
                || this.TransformToVisual(window) is not { } transform)
                return default;
            var visible = HeaderBounds(item).TransformToAABB(transform).Intersect(new Rect(window.ClientSize));
            foreach (var ancestor in this.GetVisualAncestors().Where(visual => visual.ClipToBounds))
            {
                if (ancestor.TransformToVisual(window) is not { } clipTransform)
                    return default;
                visible = visible.Intersect(new Rect(ancestor.Bounds.Size).TransformToAABB(clipTransform));
            }
            return visible;
        }

        private sealed class SurfacePeer(EquipmentSurface owner) : ControlAutomationPeer(owner)
        {
            private readonly Dictionary<Equipment, LinePeer> _peers = [];

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

            protected override IReadOnlyList<AutomationPeer> GetChildrenCore() => owner._items
                .Take(owner._items.Count - (owner._omitPeer ? 1 : 0))
                .Select(item => _peers.TryGetValue(item, out var peer) ? peer : _peers[item] = new LinePeer(owner, item))
                .ToArray();

            public void UpdateChildren() => InvalidateChildren();
        }

        private sealed class LinePeer(EquipmentSurface owner, Equipment item) : AutomationPeer
        {
            private AutomationPeer? _parent;

            protected override void BringIntoViewCore()
            {
                owner.BringIntoView(owner.HeaderBounds(item));
                TopLevel.GetTopLevel(owner)?.UpdateLayout();
            }
            protected override string? GetAcceleratorKeyCore() => null;
            protected override string? GetAccessKeyCore() => null;
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
            protected override string GetAutomationIdCore() => item.Key;
            protected override Rect GetBoundingRectangleCore() => owner.VisibleHeader(item);
            protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => [];
            protected override string GetClassNameCore() => "EquipmentLine";
            protected override AutomationPeer? GetLabeledByCore() => null;
            protected override string GetNameCore() => item.Name;
            protected override AutomationPeer? GetParentCore() => _parent;
            protected override bool HasKeyboardFocusCore() => false;
            protected override bool IsContentElementCore() => true;
            protected override bool IsControlElementCore() => true;
            protected override bool IsEnabledCore() => owner.IsEffectivelyEnabled;
            protected override bool IsKeyboardFocusableCore() => false;
            protected override bool IsOffscreenCore()
            {
                var visible = owner.VisibleHeader(item);
                return visible.Width <= 0 || visible.Height <= 0;
            }
            protected override void SetFocusCore() { }
            protected override bool ShowContextMenuCore() => false;
            protected override bool TrySetParent(AutomationPeer? parent)
            {
                _parent = parent;
                return true;
            }
        }
    }
}
