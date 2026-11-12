using System;
using System.Linq;
using System.Globalization;
using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Eremex.AvaloniaUI.Controls.DataControl;
using Eremex.AvaloniaUI.Controls.DataControl.Visuals;
using Eremex.AvaloniaUI.Controls.DataGrid;
using Eremex.AvaloniaUI.Controls.DataGrid.Visuals;
using Eremex.AvaloniaUI.Controls.Editors;

namespace DotnetDebug.Avalonia;

public partial class MainWindow
{
    private void InitializeNativeGridRuntimeFixture()
    {
        var mode = Environment.GetEnvironmentVariable("APPAUTOMATION_NATIVE_GRID_RUNTIME_FIXTURE");
        if (mode is not ("1" or "enter" or "selection" or "metadata" or "row-set-gestures")
            || ArmComplexDataGridControl.Parent is not Grid parent) return;
        if (Environment.GetEnvironmentVariable("APPAUTOMATION_NATIVE_GRID_CULTURE") is { Length: > 0 } culture)
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        var grid = new DataGridControl
        {
            Height = ArmComplexDataGridControl.Height,
            Margin = ArmComplexDataGridControl.Margin,
            ClipToBounds = true,
            ShowGroupPanel = false,
            AllowEditing = true,
            ItemsSource = Enumerable.Range(0, mode == "row-set-gestures" ? 18 : 16)
                .Select(index => new ArmDesktopGridRowViewModel(index, $"Value-{index}", "Pending", "Red")).ToArray()
        };
        AutomationProperties.SetAutomationId(grid, mode == "row-set-gestures" ? "GridRowSetGestureTable" : "ArmComplexDataGridControl");
        AutomationProperties.SetAccessibilityView(grid, AccessibilityView.Control);
        AutomationProperties.SetControlTypeOverride(grid, AutomationControlType.DataGrid);
        if (mode == "enter")
            grid.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.Enter),
                Command = new NativeGridEnterCommand(() => _viewModel.GridOpenMarker = "Opened by Enter")
            });
        grid.Styles.Add(new Style(selector => selector.OfType<DataGridRowControl>())
        {
            Setters =
            {
                new Setter(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.DataItem),
                new Setter(AutomationProperties.AccessibilityViewProperty, AccessibilityView.Control),
                new Setter(AutomationProperties.ItemStatusProperty, new Binding("Key"))
            }
        });
        grid.Styles.Add(new Style(selector => selector.OfType<ColumnHeaderControl>())
        {
            Setters = { new Setter(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.HeaderItem),
                new Setter(AutomationProperties.AccessibilityViewProperty, AccessibilityView.Control) }
        });
        grid.Styles.Add(new Style(selector => selector.OfType<CellControl>())
        {
            Setters =
            {
                new Setter(AutomationProperties.AccessibilityViewProperty,
                    mode == "metadata" ? AccessibilityView.Raw : AccessibilityView.Control),
                new Setter(AutomationProperties.AutomationIdProperty, new Binding("Column.FieldName")),
                new Setter(AutomationProperties.NameProperty, new Binding("Value")
                {
                    StringFormat = "{0}", ConverterCulture = CultureInfo.CurrentCulture
                })
            }
        });
        var fields = new[] { "Key", "State", "Product", "Value", "RequiredAmount", "Color",
            "ScheduledDate", "ScheduledTime", "IsApproved", "IsProductEditable", "Index" };
        if (mode == "selection") (fields[0], fields[1]) = (fields[1], fields[0]);
        if (mode == "row-set-gestures") fields = ["Key", "State", "Product", "Value"];
        foreach (var field in fields)
            grid.Columns.Add(new GridColumn
            {
                FieldName = field,
                Header = field,
                Width = new GridLength(130),
                ReadOnly = field == "Key",
                EditorProperties = field switch
                {
                    "State" => new ComboBoxEditorProperties { ItemsSource = new[] { "Pending", "Closed" } },
                    "RequiredAmount" => new SpinEditorProperties
                    {
                        Minimum = -10000,
                        DisplayFormatString = "N2",
                        MaskCulture = CultureInfo.CurrentCulture
                    },
                    "Value" => new TextEditorProperties { AcceptsReturn = true },
                    "IsApproved" => new CheckEditorProperties(),
                    _ => null
                }
            });
        grid.ShownEditor += (_, args) =>
        {
            if (mode == "selection") _viewModel.GridOpenMarker = "Editor activated by selection";
            if (args.Editor is SpinEditor spin)
            {
                spin.TemplateApplied += (_, _) => RegisterNumberInput();
                spin.LayoutUpdated += (_, _) => RegisterNumberInput();
                RegisterNumberInput();
                void RegisterNumberInput()
                {
                    if (spin.RealEditor is Control input)
                        AutomationProperties.SetAutomationId(input, "GridNumber_Input");
                }
            }
            if (args.Editor is not ComboBoxEditor editor) return;
            editor.TemplateApplied += (_, _) => RegisterParts();
            editor.LayoutUpdated += (_, _) => RegisterParts();
            RegisterParts();

            void RegisterParts()
            {
                if (editor.RealEditor is Control input) AutomationProperties.SetAutomationId(input, "GridCombo_Input");
                var open = editor.GetVisualDescendants().OfType<Control>()
                    .FirstOrDefault(control => control.Name == "PART_PopupOpenButton");
                if (open is not null) AutomationProperties.SetAutomationId(open, "GridCombo_Open");
                if (editor.PopupContent is Control popup)
                {
                    var list = popup.GetVisualDescendants().OfType<ListBox>().FirstOrDefault(part => part.Name == "PART_ItemsSelector");
                    if (list is not null) AutomationProperties.SetAutomationId(list, "GridCombo_Results");
                }
            }
        };
        if (mode == "row-set-gestures")
        {
            // Keep the ordinary table and its bindings intact. The extra section is below the existing content.
            var tabContent = (Grid)DataGridTabItem.Content!;
            var section = ConfigureNativeRowSetFixture(grid);
            Grid.SetRow(section, tabContent.RowDefinitions.Count);
            tabContent.RowDefinitions[2].Height = GridLength.Auto;
            tabContent.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            tabContent.Children.Add(section);
            DataGridTabItem.Content = null;
            DataGridTabItem.Content = new ScrollViewer
            {
                Content = tabContent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return;
        }

        parent.Children.Remove(ArmComplexDataGridControl);
        Grid.SetRow(grid, Grid.GetRow(ArmComplexDataGridControl));
        Grid.SetColumn(grid, Grid.GetColumn(ArmComplexDataGridControl));
        Grid.SetRowSpan(grid, Grid.GetRowSpan(ArmComplexDataGridControl));
        Grid.SetColumnSpan(grid, Grid.GetColumnSpan(ArmComplexDataGridControl));
        parent.Children.Add(grid);
    }

    private static Control ConfigureNativeRowSetFixture(DataGridControl grid)
    {
        grid.SelectionMode = RowSelectionMode.Multiple;
        grid.Width = 720;
        grid.HorizontalAlignment = HorizontalAlignment.Left;
        var items = grid.ItemsSource!.Cast<ArmDesktopGridRowViewModel>().ToArray();
        var view = items;
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 6 };
        var actual = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(actual, "GridSelectionSummary");
        var gestures = new System.Text.StringBuilder();
        grid.Styles.Add(new Style(selector => selector.OfType<DataGridRowControl>())
        {
            Setters = { new Setter(InputElement.IsEnabledProperty, new Binding(nameof(ArmDesktopGridRowViewModel.IsProductEditable))) }
        });
        AddButton("SeedGridSelection", "Seed selection", () => { grid.ClearSelection(); grid.SelectRow(0); grid.SelectRow(1); });
        AddButton("ChangeGridView", "Filter and reverse", () =>
        {
            grid.ClearSelection();
            view = items.SkipLast(1).Reverse().ToArray();
            grid.ItemsSource = view;
            Publish();
        });
        AddButton("WithholdGridSelection", "Withhold selection", () => AutomationProperties.SetItemStatus(grid, null));
        AddButton("DisableGestureRow", "Toggle test row", () => items[12].IsProductEditable = !items[12].IsProductEditable);
        grid.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed)
                RecordGesture($"{e.KeyModifiers}:{e.ClickCount};");
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        grid.ShownEditor += (_, _) => RecordGesture("Editor;");
        grid.SelectionChanged += (_, _) => Publish();
        Publish();
        return new StackPanel
        {
            Spacing = 8, Margin = new Thickness(0, 8, 0, 12),
            Children =
            {
                new TextBlock { Text = "Multiple row selection (Eremex)", FontSize = 14, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Ctrl adds individual rows; Shift selects a range." },
                grid, tools, actual
            }
        };

        void RecordGesture(string gesture)
        {
            gestures.Append(gesture);
            AutomationProperties.SetHelpText(grid, gestures.ToString());
        }

        void Publish()
        {
            var selected = (grid.SelectedItems?.OfType<ArmDesktopGridRowViewModel>() ?? []).ToArray();
            actual.Text = string.Join(",", selected.Select(static item => item.Key).Order());
            AutomationProperties.SetItemStatus(grid, GridRowSelectionValue.ToAutomationValue(
                selected.Select(static item => GridRowSelector.ByCell("Key", item.Key)),
                view.Select(static item => GridRowSelector.ByCell("Key", item.Key))));
        }

        void AddButton(string id, string caption, Action action)
        {
            var button = new Button { Content = caption };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += (_, _) => action();
            tools.Children.Add(button);
        }
    }

    private sealed class NativeGridEnterCommand(Action execute) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
