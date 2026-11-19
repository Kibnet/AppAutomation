using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Eremex.AvaloniaUI.Controls.DataControl;
using Eremex.AvaloniaUI.Controls.DataControl.Visuals;
using Eremex.AvaloniaUI.Controls.DataGrid;
using Eremex.AvaloniaUI.Controls.DataGrid.Visuals;
using Eremex.AvaloniaUI.Controls.Editors;

namespace DotnetDebug.Avalonia;

// Opt-in native table in the ordinary sample window; its rows contain no automation properties.
public static class GridPresentationFixture
{
    public const string NotificationRecoveryEnvironmentVariable = "APPAUTOMATION_NOTIFICATION_RECOVERY_FIXTURE";

    public sealed record Row
    {
        public string Product { get; set; } = string.Empty;
        public decimal Volume { get; set; }
        public int PositionNumber { get; set; }
        public string[] Components { get; set; } = Array.Empty<string>();
        public decimal? Rate { get; set; }
        public string Unit { get; set; } = string.Empty;
    }

    public static GridAutomationCatalog CreateCatalog() => new GridAutomationCatalog().Add(
        GridAutomationDefinition.ByAutomationIds("PresentationGrid", "PresentationGrid", "PresentationGrid")
            .WithColumns(GridColumnDefinition.ObjectKey("RowKey"),
                GridColumnDefinition.Auto("Product").AsValue(GridCellValueKind.Selection)
                    .EditWith(GridCellEditorKind.ComboBox, new GridCellEditorParts(
                        Input: new GridRelativeLocator("PresentationProduct_Input"),
                        Results: new GridRelativeLocator("PresentationProduct_Results", GridRelativeLocatorScope.DetachedPopup),
                        OpenButton: new GridRelativeLocator("PresentationProduct_Open", GridRelativeLocatorScope.GridRoot))),
                GridColumnDefinition.Auto("Volume").AsValue(GridCellValueKind.Number)
                    .EditWith(GridCellEditorKind.Number, new GridCellEditorParts(
                        Input: new GridRelativeLocator("PresentationVolume_Input"), UseKeyboardInput: true)),
                GridColumnDefinition.Auto("Components").ReadDisplayedText(),
                GridColumnDefinition.Auto("Rate").ReadDisplayedText(),
                GridColumnDefinition.Auto("Unit"))
            .IdentifyRowsBy("RowKey"));

    public static Control CreateContent()
    {
        var displayCulture = Environment.GetEnvironmentVariable("APPAUTOMATION_NATIVE_GRID_CULTURE") is { Length: > 0 } cultureName
            ? CultureInfo.GetCultureInfo(cultureName) : CultureInfo.CurrentCulture;
        var original = Enumerable.Range(0, 32).Select(index => index < 2 ? new Row()
            : new Row { Product = "First", Volume = index + 1, PositionNumber = index + 1 }).ToArray();
        var grid = new DataGridControl
        {
            Width = 780, Height = 240, ShowGroupPanel = false,
            ItemsSource = new ObservableCollection<Row>(original), SelectionMode = RowSelectionMode.Multiple, AllowEditing = true
        };
        AutomationProperties.SetAutomationId(grid, "PresentationGrid");
        AutomationProperties.SetAccessibilityView(grid, AccessibilityView.Control);
        AutomationProperties.SetControlTypeOverride(grid, AutomationControlType.DataGrid);
        grid.Styles.Add(new Style(s => s.OfType<DataGridRowControl>())
        {
            Setters =
            {
                new Setter(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.DataItem),
                new Setter(AutomationProperties.AccessibilityViewProperty, AccessibilityView.Control),
                new Setter(GridRowObjectKeyPublication.IsEnabledProperty, true)
            }
        });
        grid.Styles.Add(new Style(s => s.OfType<ColumnHeaderControl>())
        {
            Setters = { new Setter(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.HeaderItem),
                new Setter(AutomationProperties.AccessibilityViewProperty, AccessibilityView.Control) }
        });
        grid.Styles.Add(new Style(s => s.OfType<CellControl>())
        {
            Setters =
            {
                new Setter(AutomationProperties.AccessibilityViewProperty, AccessibilityView.Control),
                new Setter(AutomationProperties.AutomationIdProperty, new Binding("Column.FieldName")),
                new Setter(AutomationProperties.NameProperty, new MultiBinding
                {
                    Converter = new CellTextConverter(),
                    Bindings = { new Binding(), new Binding("Row"), new Binding("Column.FieldName"), new Binding("Value") }
                })
            }
        });
        foreach (var field in new[] { "Unit", "Product", "Volume", "Components", "Rate" })
        {
            var column = new GridColumn
            {
                FieldName = field, Header = field,
                Width = new GridLength(field is "Components" or "Rate" ? 190 : 120),
                ReadOnly = field is not ("Product" or "Volume"),
                EditorProperties = field switch
                {
                    "Product" => new ComboBoxEditorProperties { ItemsSource = new[] { "First", "Second", "Third" } },
                    "Volume" => new SpinEditorProperties { Minimum = -10000 },
                    _ => null
                }
            };
            if (field == "Components")
                column.CellTemplate = new FuncDataTemplate<CellData>((_, _) =>
                {
                    var items = new ItemsControl
                    {
                        ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text })
                    };
                    items.Bind(ItemsControl.ItemsSourceProperty, new Binding("Value"));
                    return items;
                });
            grid.Columns.Add(column);
        }
        grid.CustomColumnDisplayText += (_, args) =>
        {
            if (args.Column?.FieldName == "Rate" && args.SourceItemIndex >= 0
                && grid.GetSourceItem(args.SourceItemIndex) is Row row)
                args.DisplayText = row.Rate is { } rate
                    ? $"{rate.ToString("N1", displayCulture)} {row.Unit}/hour" : string.Empty;
        };
        grid.ShownEditor += (_, args) =>
        {
            if (args.Editor is ComboBoxEditor combo)
            {
                combo.LayoutUpdated += (_, _) => RegisterCombo(combo);
                RegisterCombo(combo);
            }
            if (args.Editor is SpinEditor spin)
            {
                spin.LayoutUpdated += (_, _) =>
                {
                    if (spin.RealEditor is Control input) AutomationProperties.SetAutomationId(input, "PresentationVolume_Input");
                };
            }
        };
        var opened = new TextBlock { Text = "None" };
        AutomationProperties.SetAutomationId(opened, "PresentationOpenedRow");
        grid.AddHandler(InputElement.DoubleTappedEvent, (_, args) =>
        {
            if (args.Source is not Control source) return;
            var row = source.GetVisualAncestors().OfType<Control>().Prepend(source)
                .Select(control => control.DataContext is CellData cell ? cell.Row : control.DataContext)
                .OfType<Row>().FirstOrDefault();
            if (row is not null) opened.Text = GridRowObjectKey.Get(row);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        var root = new StackPanel { Margin = new Thickness(12), Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(new TextBlock { Text = "Object rows and displayed text" });
        root.Children.Add(grid);
        root.Children.Add(opened);
        var buttons = new WrapPanel();
        root.Children.Add(buttons);
        AddButton("PopulateFirst", () =>
        {
            original[0].Components = ["Alpha", "Beta"];
            original[0].Rate = 12.345m;
            original[0].Unit = "kg";
            grid.RefreshData();
        });
        AddButton("ReverseRows", () => grid.ItemsSource = new ObservableCollection<Row>(CurrentRows().Reverse()));
        var hideFirst = false;
        grid.CustomRowFilter += (_, args) =>
        {
            if (hideFirst && ReferenceEquals(grid.GetSourceItem(args.SourceItemIndex), original[0])) args.Visible = false;
        };
        AddButton("FilterFirstRow", () => { hideFirst = true; grid.RefreshData(); });
        AddButton("RestoreRows", () => { hideFirst = false; grid.ItemsSource = new ObservableCollection<Row>(original); grid.RefreshData(); });
        AddButton("CloneRows", () =>
        {
            grid.ItemsSource = new ObservableCollection<Row>();
            grid.Dispatcher.RunJobs();
            grid.UpdateLayout();
            grid.ItemsSource = new ObservableCollection<Row>(original.Select(row => row with { }));
        });
        AddButton("DuplicateFirstRow", () => grid.ItemsSource = new ObservableCollection<Row>(original.Concat([original[0]])));
        AddButton("AddEmptyRow", () => ((ObservableCollection<Row>)grid.ItemsSource!).Add(new Row()));
        grid.SelectionChanged += (_, _) => Publish();
        grid.LayoutUpdated += (_, _) => Publish();
        return Environment.GetEnvironmentVariable(NotificationRecoveryEnvironmentVariable) == "1"
            ? new NotificationRecoverySurface(root, grid, original)
            : root;

        Row[] CurrentRows() => grid.ItemsSource!.Cast<Row>().ToArray();
        void AddButton(string id, Action action)
        {
            var button = new Button { Content = id, Margin = new Thickness(0, 0, 5, 0) };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += (_, _) => { action(); Publish(); };
            buttons.Children.Add(button);
        }

        void Publish()
        {
            GridRowSelector Key(object row) => GridRowSelector.ByCell("RowKey", GridRowObjectKey.Get(row));
            var view = Enumerable.Range(0, grid.VisibleRowCount).Select(grid.GetSourceItemByVisibleRowIndex)
                .Where(item => item is Row).Select(Key);
            var selected = (grid.SelectedItems?.Cast<object>() ?? []).Select(Key);
            var publication = GridRowSelectionValue.ToAutomationValue(selected, view);
            if (AutomationProperties.GetItemStatus(grid) != publication)
                AutomationProperties.SetItemStatus(grid, publication);
        }
    }

    // The notification stand extends the existing native table in its normal MainWindow tab.
    public sealed class NotificationRecoverySurface : global::Avalonia.Controls.Grid
    {
        private readonly DataGridControl _grid;
        private readonly Row[] _original;
        private readonly Canvas _overlay = new();
        private readonly Dictionary<string, TextBlock> _markers = new(StringComparer.Ordinal);
        private readonly Dictionary<Border, string> _visible = new();
        private readonly List<string> _history = new();
        private readonly HashSet<PointerPressedEventArgs> _pressEvents = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<PointerReleasedEventArgs> _releaseEvents = new(ReferenceEqualityComparer.Instance);
        private readonly TextBox _mode = new() { Width = 420, PlaceholderText = "Recovery fixture modes, separated by ;" };
        private readonly StackPanel _inputHost = new();
        private TextBox _input = null!;
        private int _down;
        private int _firstClickCount;
        private int _up;
        private int _open;
        private int _dismiss;
        private int _heartbeat;
        private int _downBeforeAsyncClose;
        private int _otherPresses;
        private int _inputDown;
        private int _oldInputDown;
        private string _lastDownKey = "None";
        private bool _asyncClosePending;
        private TaskCompletionSource<bool>? _asyncClose;
        private bool _late;
        private bool _afterDown;
        private int _successive;

        public NotificationRecoverySurface(StackPanel content, DataGridControl grid, Row[] original)
        {
            _grid = grid;
            _original = original;
            Children.Add(content);
            Children.Add(_overlay);
            var buttons = new WrapPanel();
            content.Children.Add(buttons);
            AddButton("RecoveryShowSuccess", () => ShowToast("success"));
            AddButton("RecoveryShowBackground", () =>
            {
                _successive = Mode("successive") ? 2 : Mode("continuous") ? int.MaxValue : 0;
                ShowToast("background");
            });
            AddButton("RecoveryShowOther", () => ShowToast("other", blocking: false));
            AddButton("RecoveryArmLate", () => _late = true);
            AddButton("RecoveryArmAfterDown", () => _afterDown = true);
            AddButton("RecoveryPulseHeartbeat", () => { _heartbeat++; RefreshMarkers(); });
            AddButton("RecoveryCompleteAsyncClose", () => _asyncClose?.TrySetResult(true));
            AutomationProperties.SetAutomationId(_mode, "RecoveryMode");
            content.Children.Add(_mode);
            content.Children.Add(_inputHost);
            ReplaceInput();
            var markers = new WrapPanel();
            content.Children.Add(markers);
            foreach (var name in new[] { "Down", "Up", "Open", "Dismiss", "Visible", "History", "Heartbeat",
                         "Pending", "DownBeforeAsyncClose", "OtherPresses", "InputDown", "OldInputDown", "LastDownKey", "FirstClickCount" })
            {
                var label = new TextBlock { Margin = new Thickness(4), Text = name == "History" ? "" : "0" };
                AutomationProperties.SetAutomationId(label, "Recovery" + name);
                _markers.Add(name, label);
                markers.Children.Add(label);
            }
            grid.AddHandler(InputElement.PointerPressedEvent, (_, args) =>
            {
                if (FindRow(args.Source) is not { } row || !_pressEvents.Add(args)) return;
                if (_down == 0) _firstClickCount = args.ClickCount;
                _down++;
                _lastDownKey = GridRowObjectKey.Get(row);
                if (_asyncClosePending) _downBeforeAsyncClose++;
                RefreshMarkers();
                if (_afterDown)
                {
                    _afterDown = false;
                    ShowToast("after-down");
                    if (Mode("throw-after-down"))
                        throw new InvalidOperationException("The fixture original Down handler failed after its observable effect.");
                }
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            grid.AddHandler(InputElement.PointerReleasedEvent, (_, args) =>
            {
                if (FindRow(args.Source) is null || !_releaseEvents.Add(args)) return;
                _up++;
                RefreshMarkers();
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            grid.AddHandler(InputElement.DoubleTappedEvent, (_, args) =>
            {
                if (FindRow(args.Source) is null) return;
                _open++;
                RefreshMarkers();
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            grid.AddHandler(InputElement.PointerMovedEvent, (_, _) => ShowLateToast(), RoutingStrategies.Tunnel, handledEventsToo: true);

            void AddButton(string id, Action action)
            {
                var button = new Button { Content = id, Margin = new Thickness(4) };
                AutomationProperties.SetAutomationId(button, id);
                button.Click += (_, _) => action();
                buttons.Children.Add(button);
            }
        }

        private bool Mode(string value) => (_mode.Text ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(value, StringComparer.Ordinal);

        private void ShowLateToast()
        {
            if (!_late) return;
            _late = false;
            ShowToast("late-background");
        }

        private void ReplaceInput()
        {
            _inputHost.Children.Clear();
            var created = new TextBox { Width = 300, Text = "Locator target" };
            _input = created;
            AutomationProperties.SetAutomationId(_input, "RecoveryLocatorTarget");
            _input.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
            {
                _inputDown++;
                if (!ReferenceEquals(_input, created)) _oldInputDown++;
                RefreshMarkers();
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            _input.AddHandler(InputElement.PointerMovedEvent, (_, _) => ShowLateToast(), RoutingStrategies.Tunnel, handledEventsToo: true);
            _inputHost.Children.Add(_input);
        }

        private void ShowToast(string message, bool blocking = true)
        {
            var inputTarget = Mode("input-target");
            var target = inputTarget ? (Control)_input : _grid.GetVisualDescendants().OfType<DataGridRowControl>()
                .First(control => ReferenceEquals(control.DataContext, _original[0]));
            var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), this)
                ?? throw new InvalidOperationException("The fixture row has no position in its normal window.");
            var wholeRow = blocking;
            var rowStart = target.TranslatePoint(default, this)
                ?? throw new InvalidOperationException("The fixture row has no start position in its normal window.");
            var toast = new Border { Width = wholeRow ? target.Bounds.Width + 16 : 280, Height = Math.Max(80, target.Bounds.Height + 16), Background = global::Avalonia.Media.Brushes.LightYellow,
                BorderBrush = global::Avalonia.Media.Brushes.Black, BorderThickness = new Thickness(1) };
            var toastLeft = wholeRow ? Math.Max(0, rowStart.X) : blocking ? Math.Max(0, point.X - toast.Width / 2) : 570;
            var toastTop = blocking ? Math.Max(0, point.Y - toast.Height / 2) : 0;
            AutomationProperties.SetAutomationId(toast, Mode("unknown") && blocking ? "RecoveryUnknownOverlay" : "RecoveryToast");
            AutomationProperties.SetAccessibilityView(toast, AccessibilityView.Control);
            AutomationProperties.SetControlTypeOverride(toast, AutomationControlType.Pane);
            AutomationProperties.SetItemStatus(toast, "instance-" + (_history.Count + 1).ToString(CultureInfo.InvariantCulture));
            var text = new TextBlock { Text = message, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center,
                Foreground = global::Avalonia.Media.Brushes.Black };
            AutomationProperties.SetAutomationId(text, "RecoveryToastText");
            var physicalDismiss = blocking && Mode("physical-dismiss");
            Button close = physicalDismiss ? new PhysicalDismissButton() : new Button();
            close.Content = "Close";
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Top;
            close.Margin = new Thickness(4);
            if (physicalDismiss)
            {
                var cell = target.GetVisualDescendants().OfType<CellControl>().First(control =>
                    control.DataContext is CellData { Column.FieldName: "Unit" } data && ReferenceEquals(data.Row, _original[0]));
                var cellPoint = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), this)
                    ?? throw new InvalidOperationException("The fixture Unit cell has no position in its normal window.");
                close.Width = Math.Min(72, cell.Bounds.Width);
                close.Height = 32;
                close.MinWidth = 0;
                close.MinHeight = 0;
                close.Padding = default;
                close.HorizontalAlignment = HorizontalAlignment.Left;
                close.Margin = new Thickness(cellPoint.X - toastLeft - toast.BorderThickness.Left - close.Width / 2,
                    cellPoint.Y - toastTop - toast.BorderThickness.Top - close.Height / 2, 0, 0);
                AutomationProperties.SetControlTypeOverride(close, AutomationControlType.Button);
                AutomationProperties.SetName(close, "Close");
            }
            AutomationProperties.SetAutomationId(close, "RecoveryToastClose");
            close.IsEnabled = !Mode("disabled");
            close.IsVisible = !Mode("hidden");
            close.Click += (_, _) => DismissToast(toast);
            var body = new global::Avalonia.Controls.Grid();
            body.Children.Add(text);
            if (!Mode("missing")) body.Children.Add(close);
            if (Mode("ambiguous"))
            {
                var duplicate = new Button { Content = "Second close", HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top };
                AutomationProperties.SetAutomationId(duplicate, "RecoveryToastClose");
                duplicate.Click += (_, _) => DismissToast(toast);
                body.Children.Add(duplicate);
            }
            if (Mode("blocked-close")) body.Children.Add(new Border
            {
                Width = 80, Height = 40, Background = global::Avalonia.Media.Brushes.Red,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top
            });
            toast.Child = body;
            toast.AddHandler(InputElement.PointerPressedEvent, (_, _) => { _otherPresses++; RefreshMarkers(); },
                RoutingStrategies.Tunnel, handledEventsToo: true);
            Canvas.SetLeft(toast, toastLeft);
            Canvas.SetTop(toast, toastTop);
            _overlay.Children.Add(toast);
            _visible.Add(toast, message);
            _history.Add(message);
            RefreshMarkers();
            TopLevel.GetTopLevel(this)?.UpdateLayout();
        }

        private void DismissToast(Border toast)
        {
            _dismiss++;
            RefreshMarkers();
            if (Mode("no-progress")) return;
            if (Mode("async"))
            {
                _asyncClosePending = true;
                _asyncClose = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                RefreshMarkers();
                _ = CompleteDeferredClose(_asyncClose.Task, RemoveToast);
            }
            else RemoveToast();

            void RemoveToast()
            {
                _visible.Remove(toast);
                _overlay.Children.Remove(toast);
                if (Mode("reorder")) _grid.ItemsSource = new ObservableCollection<Row>(_grid.ItemsSource!.Cast<Row>().Reverse());
                if (Mode("recreate"))
                {
                    var rows = _grid.ItemsSource!.Cast<Row>().ToArray();
                    _grid.ItemsSource = new ObservableCollection<Row>();
                    _grid.UpdateLayout();
                    _grid.ItemsSource = new ObservableCollection<Row>(rows);
                }
                if (Mode("remove-target")) ((ObservableCollection<Row>)_grid.ItemsSource!).Remove(_original[0]);
                if (Mode("duplicate-target")) ((ObservableCollection<Row>)_grid.ItemsSource!).Add(_original[0]);
                if (Mode("replace-input")) ReplaceInput();
                if (_successive > 0)
                {
                    _successive--;
                    ShowToast("background-next");
                }
                RefreshMarkers();
                TopLevel.GetTopLevel(this)?.UpdateLayout();
            }
        }

        private async Task CompleteDeferredClose(Task gate, Action removeToast)
        {
            await gate.ConfigureAwait(false);
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                removeToast();
                _asyncClosePending = false;
                RefreshMarkers();
            });
        }

        private void RefreshMarkers()
        {
            _markers["Down"].Text = _down.ToString(CultureInfo.InvariantCulture);
            _markers["Up"].Text = _up.ToString(CultureInfo.InvariantCulture);
            _markers["Open"].Text = _open.ToString(CultureInfo.InvariantCulture);
            _markers["Dismiss"].Text = _dismiss.ToString(CultureInfo.InvariantCulture);
            _markers["Visible"].Text = _visible.Count.ToString(CultureInfo.InvariantCulture);
            _markers["History"].Text = string.Join("|", _history);
            _markers["Heartbeat"].Text = _heartbeat.ToString(CultureInfo.InvariantCulture);
            _markers["Pending"].Text = _asyncClosePending ? "1" : "0";
            _markers["DownBeforeAsyncClose"].Text = _downBeforeAsyncClose.ToString(CultureInfo.InvariantCulture);
            _markers["OtherPresses"].Text = _otherPresses.ToString(CultureInfo.InvariantCulture);
            _markers["InputDown"].Text = _inputDown.ToString(CultureInfo.InvariantCulture);
            _markers["OldInputDown"].Text = _oldInputDown.ToString(CultureInfo.InvariantCulture);
            _markers["LastDownKey"].Text = _lastDownKey;
            _markers["FirstClickCount"].Text = _firstClickCount.ToString(CultureInfo.InvariantCulture);
        }

        private sealed class PhysicalDismissButton : Button
        {
            protected override Type StyleKeyOverride => typeof(Button);

            // Keep the ordinary Button Click handler, but require the native physical dismiss path.
            protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);
        }

        private static Row? FindRow(object? source) => source is Control control
            ? control.GetVisualAncestors().OfType<Control>().Prepend(control)
                .Select(candidate => candidate.DataContext is CellData cell ? cell.Row : candidate.DataContext)
                .OfType<Row>().FirstOrDefault()
            : null;
    }

    private static void RegisterCombo(ComboBoxEditor editor)
    {
        if (editor.RealEditor is Control input) AutomationProperties.SetAutomationId(input, "PresentationProduct_Input");
        var open = editor.GetVisualDescendants().OfType<Control>().FirstOrDefault(part => part.Name == "PART_PopupOpenButton");
        if (open is not null) AutomationProperties.SetAutomationId(open, "PresentationProduct_Open");
        if (editor.PopupContent is Control popup
            && popup.GetVisualDescendants().OfType<ListBox>().FirstOrDefault(part => part.Name == "PART_ItemsSelector") is { } list)
            AutomationProperties.SetAutomationId(list, "PresentationProduct_Results");
    }

    private sealed class CellTextConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count != 4 || values[0] is not CellData { DataControl: DataGridControl grid, Row: Row row } cell
                || !ReferenceEquals(values[1], row) || values[2] is not string field
                || field != cell.Column?.FieldName || !Equals(values[3], cell.Value)) return string.Empty;
            if (field == "Components") return string.Join(Environment.NewLine, row.Components);
            var visibleIndex = Enumerable.Range(0, grid.VisibleRowCount)
                .FirstOrDefault(index => ReferenceEquals(grid.GetSourceItemByVisibleRowIndex(index), row), -1);
            var rowIndex = visibleIndex < 0 ? -1 : grid.GetRowIndexByVisibleRowIndex(visibleIndex);
            return rowIndex < 0 ? string.Empty : grid.GetCellDisplayText(rowIndex, field) ?? string.Empty;
        }
    }
}
