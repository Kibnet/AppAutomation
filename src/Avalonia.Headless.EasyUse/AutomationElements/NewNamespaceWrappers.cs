namespace Avalonia.Headless.EasyUse.AutomationElements;

public class TextBox : global::FlaUI.Core.AutomationElements.TextBox
{
    internal TextBox(global::Avalonia.Controls.TextBox textBox) : base(textBox)
    {
    }

    internal TextBox(global::FlaUI.Core.AutomationElements.TextBox inner) : base((global::Avalonia.Controls.TextBox)inner.Control)
    {
    }
}

public class Button : global::FlaUI.Core.AutomationElements.Button
{
    internal Button(global::Avalonia.Controls.Button button) : base(button)
    {
    }

    internal Button(global::FlaUI.Core.AutomationElements.Button inner) : base((global::Avalonia.Controls.Button)inner.Control)
    {
    }
}

public class Label : global::FlaUI.Core.AutomationElements.Label
{
    internal Label(global::Avalonia.Controls.Control control) : base(control)
    {
    }

    internal Label(global::FlaUI.Core.AutomationElements.Label inner) : base(inner.Control)
    {
    }
}

public class ListBox : global::FlaUI.Core.AutomationElements.ListBox
{
    internal ListBox(global::Avalonia.Controls.ListBox listBox) : base(listBox)
    {
    }

    internal ListBox(global::FlaUI.Core.AutomationElements.ListBox inner) : base((global::Avalonia.Controls.ListBox)inner.Control)
    {
    }
}

public sealed class ListBoxItem
{
    internal ListBoxItem(global::FlaUI.Core.AutomationElements.ListBoxItem inner)
    {
        Text = inner.Text;
        Name = inner.Name;
    }

    public string? Text { get; }

    public string? Name { get; }
}

public class CheckBox : global::FlaUI.Core.AutomationElements.CheckBox
{
    internal CheckBox(global::Avalonia.Controls.CheckBox checkBox) : base(checkBox)
    {
    }

    internal CheckBox(global::FlaUI.Core.AutomationElements.CheckBox inner) : base((global::Avalonia.Controls.CheckBox)inner.Control)
    {
    }
}

public class ComboBox : global::FlaUI.Core.AutomationElements.ComboBox
{
    internal ComboBox(global::Avalonia.Controls.ComboBox comboBox) : base(comboBox)
    {
    }

    internal ComboBox(global::FlaUI.Core.AutomationElements.ComboBox inner) : base((global::Avalonia.Controls.ComboBox)inner.Control)
    {
    }
}

public sealed class ComboBoxItem
{
    internal ComboBoxItem(global::FlaUI.Core.AutomationElements.ComboBoxItem inner)
    {
        Text = inner.Text;
        Name = inner.Name;
    }

    public string Text { get; }

    public string Name { get; }
}

public class RadioButton : global::FlaUI.Core.AutomationElements.RadioButton
{
    internal RadioButton(global::Avalonia.Controls.RadioButton radioButton) : base(radioButton)
    {
    }

    internal RadioButton(global::FlaUI.Core.AutomationElements.RadioButton inner) : base((global::Avalonia.Controls.RadioButton)inner.Control)
    {
    }
}

public class ToggleButton : global::FlaUI.Core.AutomationElements.ToggleButton
{
    internal ToggleButton(global::Avalonia.Controls.Primitives.ToggleButton toggleButton) : base(toggleButton)
    {
    }

    internal ToggleButton(global::FlaUI.Core.AutomationElements.ToggleButton inner)
        : base((global::Avalonia.Controls.Primitives.ToggleButton)inner.Control)
    {
    }
}

public class Slider : global::FlaUI.Core.AutomationElements.Slider
{
    internal Slider(global::Avalonia.Controls.Slider slider) : base(slider)
    {
    }

    internal Slider(global::FlaUI.Core.AutomationElements.Slider inner) : base((global::Avalonia.Controls.Slider)inner.Control)
    {
    }
}

public class ProgressBar : global::FlaUI.Core.AutomationElements.ProgressBar
{
    internal ProgressBar(global::Avalonia.Controls.ProgressBar progressBar) : base(progressBar)
    {
    }

    internal ProgressBar(global::FlaUI.Core.AutomationElements.ProgressBar inner) : base((global::Avalonia.Controls.ProgressBar)inner.Control)
    {
    }
}

public class Calendar : global::FlaUI.Core.AutomationElements.Calendar
{
    internal Calendar(global::Avalonia.Controls.Calendar calendar) : base(calendar)
    {
    }

    internal Calendar(global::FlaUI.Core.AutomationElements.Calendar inner) : base((global::Avalonia.Controls.Calendar)inner.Control)
    {
    }
}

public class DateTimePicker : global::FlaUI.Core.AutomationElements.DateTimePicker
{
    internal DateTimePicker(global::Avalonia.Controls.DatePicker datePicker) : base(datePicker)
    {
    }

    internal DateTimePicker(global::FlaUI.Core.AutomationElements.DateTimePicker inner) : base((global::Avalonia.Controls.DatePicker)inner.Control)
    {
    }
}

public class Spinner : global::FlaUI.Core.AutomationElements.Spinner
{
    internal Spinner(global::Avalonia.Controls.TextBox textBox) : base(textBox)
    {
    }

    internal Spinner(global::FlaUI.Core.AutomationElements.Spinner inner) : base((global::Avalonia.Controls.TextBox)inner.Control)
    {
    }
}

public class Tab : global::FlaUI.Core.AutomationElements.Tab
{
    internal Tab(global::Avalonia.Controls.TabControl tab) : base(tab)
    {
    }

    internal Tab(global::FlaUI.Core.AutomationElements.Tab inner) : base((global::Avalonia.Controls.TabControl)inner.Control)
    {
    }
}

public class TabItem : global::FlaUI.Core.AutomationElements.TabItem
{
    internal TabItem(global::Avalonia.Controls.TabItem tabItem) : base(tabItem)
    {
    }

    internal TabItem(global::FlaUI.Core.AutomationElements.TabItem inner) : base((global::Avalonia.Controls.TabItem)inner.Control)
    {
    }
}

public class Tree : global::FlaUI.Core.AutomationElements.Tree
{
    internal Tree(global::Avalonia.Controls.TreeView tree) : base(tree)
    {
    }

    internal Tree(global::FlaUI.Core.AutomationElements.Tree inner) : base((global::Avalonia.Controls.TreeView)inner.Control)
    {
    }
}

public class TreeItem : global::FlaUI.Core.AutomationElements.TreeItem
{
    internal TreeItem(global::Avalonia.Controls.TreeViewItem treeItem) : base(treeItem)
    {
    }

    internal TreeItem(global::FlaUI.Core.AutomationElements.TreeItem inner) : base((global::Avalonia.Controls.TreeViewItem)inner.Control)
    {
    }
}

public class DataGridView : global::FlaUI.Core.AutomationElements.DataGridView
{
    internal DataGridView(global::Avalonia.Controls.DataGrid grid) : base(grid)
    {
    }

    internal DataGridView(global::FlaUI.Core.AutomationElements.DataGridView inner) : base((global::Avalonia.Controls.DataGrid)inner.Control)
    {
    }
}

public class Grid : global::FlaUI.Core.AutomationElements.Grid
{
    internal Grid(global::Avalonia.Controls.DataGrid grid) : base(grid)
    {
    }

    internal Grid(global::FlaUI.Core.AutomationElements.Grid inner) : base((global::Avalonia.Controls.DataGrid)inner.Control)
    {
    }
}

public class GridRow : global::FlaUI.Core.AutomationElements.GridRow
{
    internal GridRow(object? item) : base(item)
    {
    }
}

public class GridCell : global::FlaUI.Core.AutomationElements.GridCell
{
    internal GridCell(string value) : base(value)
    {
    }
}