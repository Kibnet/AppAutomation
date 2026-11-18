using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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
        return root;

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
