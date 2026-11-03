using System;
using System.Linq;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
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
        if (mode is not ("1" or "enter" or "selection" or "metadata")
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
            ItemsSource = Enumerable.Range(0, 16)
                .Select(index => new ArmDesktopGridRowViewModel(index, $"Value-{index}", "Pending", "Red")).ToArray()
        };
        AutomationProperties.SetAutomationId(grid, "ArmComplexDataGridControl");
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
        parent.Children.Remove(ArmComplexDataGridControl);
        Grid.SetRow(grid, Grid.GetRow(ArmComplexDataGridControl));
        Grid.SetColumn(grid, Grid.GetColumn(ArmComplexDataGridControl));
        Grid.SetRowSpan(grid, Grid.GetRowSpan(ArmComplexDataGridControl));
        Grid.SetColumnSpan(grid, Grid.GetColumnSpan(ArmComplexDataGridControl));
        parent.Children.Add(grid);
    }

    private sealed class NativeGridEnterCommand(Action execute) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
