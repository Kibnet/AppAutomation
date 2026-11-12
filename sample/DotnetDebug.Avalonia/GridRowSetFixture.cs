using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;

namespace DotnetDebug.Avalonia;

public static class GridRowSetFixture
{
    public static Control CreateContent(bool includeFilteredDuplicate = false)
    {
        var rows = Enumerable.Range(1, 14).Select(number => new Item($"ROW-{number:D2}"));
        var duplicate = includeFilteredDuplicate ? new Item("ROW-01") : null;
        var grid = new DataGrid
        {
            Height = 168,
            Width = 340,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Extended,
            ItemsSource = (duplicate is null ? rows : rows.Append(duplicate)).ToArray()
        };
        if (duplicate is not null) grid.CollectionView.Filter = item => !ReferenceEquals(item, duplicate);
        grid.Columns.Add(new DataGridTextColumn { Header = "Key", Binding = new Binding(nameof(Item.Key)) });
        AutomationProperties.SetAutomationId(grid, "ArmComplexDataGridControl");
        var selection = new TextBlock { Text = "None" };
        AutomationProperties.SetAutomationId(selection, "GridSelectionSummary");
        grid.SelectionChanged += (_, _) => selection.Text = grid.SelectedItems.Count == 0 ? "None"
            : string.Join(",", grid.SelectedItems.Cast<Item>().Select(static item => item.Key).Order());
        return new StackPanel { Children = { grid, selection } };
    }

    private sealed record Item(string Key)
    {
        public override string ToString() => Key;
    }
}
