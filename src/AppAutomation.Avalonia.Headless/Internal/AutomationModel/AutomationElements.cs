using System.Collections;
using System.Globalization;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using AppAutomation.Avalonia.Headless.Internal.AutomationModel.Conditions;
using AppAutomation.Avalonia.Headless.Internal.AutomationModel.Definitions;

namespace AppAutomation.Avalonia.Headless.Internal.AutomationModel;

internal class AutomationElement
{
    internal AutomationElement(Control control)
    {
        Control = control ?? throw new ArgumentNullException(nameof(control));
    }

    internal Control Control { get; }

    public string AutomationId => Ui(() => AutomationProperties.GetAutomationId(Control) ?? string.Empty);

    public virtual string Name => Ui(() => ReadControlName(Control));

    public bool IsEnabled => Ui(() => Control.IsEnabled);

    public bool IsAvailable => true;

    public bool IsOffscreen => false;

    public ControlType ControlType => Ui(() => MapControlType(Control));

    public virtual void Click()
    {
        Ui(() =>
        {
            switch (Control)
            {
                case global::Avalonia.Controls.Primitives.ToggleButton toggleButton:
                    toggleButton.IsChecked = !(toggleButton.IsChecked ?? false);
                    break;
                case global::Avalonia.Controls.Button button:
                    button.RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
                    break;
                default:
                    throw new InvalidOperationException($"Control '{Control.GetType().Name}' does not support click interaction.");
            }

            return true;
        });
    }

    public AutomationElement[] FindAllDescendants()
    {
        return Ui(() => ControlTree.EnumerateDescendants(Control)
            .Select(WrapControl)
            .ToArray());
    }

    public AutomationElement[] FindAllDescendants(Func<ConditionFactory, PropertyCondition> conditionFactory)
    {
        ArgumentNullException.ThrowIfNull(conditionFactory);
        var condition = conditionFactory(new ConditionFactory());
        return Ui(() => ControlTree.EnumerateDescendants(Control)
            .Where(condition.Match)
            .Select(WrapControl)
            .ToArray());
    }

    public AutomationElement? FindFirstDescendant(PropertyCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return Ui(() =>
        {
            var match = ControlTree.EnumerateDescendants(Control).FirstOrDefault(condition.Match);
            return match is null ? null : WrapControl(match);
        });
    }

    public TextBox AsTextBox() => this as TextBox ?? new TextBox(RequireControl<global::Avalonia.Controls.TextBox>());

    public Button AsButton() => this as Button ?? new Button(RequireControl<global::Avalonia.Controls.Button>());

    public Label AsLabel() => this as Label ?? new Label(RequireControl<Control>());

    public ListBox AsListBox() => this as ListBox ?? new ListBox(RequireControl<global::Avalonia.Controls.ListBox>());

    public CheckBox AsCheckBox() => this as CheckBox ?? new CheckBox(RequireControl<global::Avalonia.Controls.CheckBox>());

    public ComboBox AsComboBox() => this as ComboBox ?? new ComboBox(RequireControl<global::Avalonia.Controls.ComboBox>());

    public RadioButton AsRadioButton() => this as RadioButton ?? new RadioButton(RequireControl<global::Avalonia.Controls.RadioButton>());

    public ToggleButton AsToggleButton() => this as ToggleButton ?? new ToggleButton(RequireControl<global::Avalonia.Controls.Primitives.ToggleButton>());

    public Slider AsSlider() => this as Slider ?? new Slider(RequireControl<global::Avalonia.Controls.Slider>());

    public ProgressBar AsProgressBar() => this as ProgressBar ?? new ProgressBar(RequireControl<global::Avalonia.Controls.ProgressBar>());

    public Calendar AsCalendar() => this as Calendar ?? new Calendar(RequireControl<global::Avalonia.Controls.Calendar>());

    public DateTimePicker AsDateTimePicker() => this as DateTimePicker ?? new DateTimePicker(RequireControl<global::Avalonia.Controls.DatePicker>());

    public Spinner AsSpinner()
    {
        if (this is Spinner spinner)
        {
            return spinner;
        }

        if (Control is global::Avalonia.Controls.TextBox textBox)
        {
            return new Spinner(textBox);
        }

        throw new InvalidOperationException($"Control '{Control.GetType().Name}' cannot be converted to Spinner.");
    }

    public Tab AsTab() => this as Tab ?? new Tab(RequireControl<global::Avalonia.Controls.TabControl>());

    public TabItem AsTabItem() => this as TabItem ?? new TabItem(RequireControl<global::Avalonia.Controls.TabItem>());

    public Tree AsTree() => this as Tree ?? new Tree(RequireControl<global::Avalonia.Controls.TreeView>());

    public TreeItem AsTreeItem() => this as TreeItem ?? new TreeItem(RequireControl<global::Avalonia.Controls.TreeViewItem>());

    public DataGridView AsDataGridView() => this as DataGridView ?? new DataGridView(RequireControl<global::Avalonia.Controls.DataGrid>());

    public GridRow AsGridRow()
    {
        throw new InvalidOperationException("Element conversion to GridRow is only available from Grid/DataGrid wrappers.");
    }

    public GridCell AsGridCell()
    {
        throw new InvalidOperationException("Element conversion to GridCell is only available from GridRow wrappers.");
    }

    public Grid AsGrid() => this as Grid ?? new Grid(RequireControl<global::Avalonia.Controls.DataGrid>());

    protected static T Ui<T>(Func<T> action)
    {
        return AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(action);
    }

    protected T RequireControl<T>() where T : Control
    {
        return Ui(() =>
        {
            if (Control is T typed)
            {
                return typed;
            }

            throw new InvalidOperationException($"Control '{Control.GetType().Name}' cannot be converted to '{typeof(T).Name}'.");
        });
    }

    internal static AutomationElement WrapControl(Control control)
    {
        return control switch
        {
            global::Avalonia.Controls.TextBox textBox => new TextBox(textBox),
            global::Avalonia.Controls.CheckBox checkBox => new CheckBox(checkBox),
            global::Avalonia.Controls.RadioButton radioButton => new RadioButton(radioButton),
            global::Avalonia.Controls.Primitives.ToggleButton toggleButton => new ToggleButton(toggleButton),
            global::Avalonia.Controls.Button button => new Button(button),
            global::Avalonia.Controls.ComboBox comboBox => new ComboBox(comboBox),
            global::Avalonia.Controls.ListBox listBox => new ListBox(listBox),
            global::Avalonia.Controls.Slider slider => new Slider(slider),
            global::Avalonia.Controls.ProgressBar progressBar => new ProgressBar(progressBar),
            global::Avalonia.Controls.DatePicker datePicker => new DateTimePicker(datePicker),
            global::Avalonia.Controls.Calendar calendar => new Calendar(calendar),
            global::Avalonia.Controls.TabControl tabControl => new Tab(tabControl),
            global::Avalonia.Controls.TabItem tabItem => new TabItem(tabItem),
            global::Avalonia.Controls.TreeView treeView => new Tree(treeView),
            global::Avalonia.Controls.TreeViewItem treeViewItem => new TreeItem(treeViewItem),
            global::Avalonia.Controls.DataGrid dataGrid => new DataGridView(dataGrid),
            _ when IsLabelLike(control) => new Label(control),
            _ => new AutomationElement(control)
        };
    }

    internal static string ReadControlName(Control control)
    {
        switch (control)
        {
            case global::Avalonia.Controls.TextBlock textBlock:
                return textBlock.Text ?? string.Empty;
            case global::Avalonia.Controls.Label label:
                return label.Content?.ToString() ?? string.Empty;
            case global::Avalonia.Controls.TabItem tabItem:
                return tabItem.Header?.ToString() ?? string.Empty;
            case global::Avalonia.Controls.TreeViewItem treeViewItem:
                return treeViewItem.Header?.ToString() ?? string.Empty;
        }

        var automationName = AutomationProperties.GetName(control);
        if (!string.IsNullOrWhiteSpace(automationName))
        {
            return automationName;
        }

        if (!string.IsNullOrWhiteSpace(control.Name))
        {
            return control.Name;
        }

        return string.Empty;
    }

    private static bool IsLabelLike(Control control)
    {
        return control is global::Avalonia.Controls.Label or global::Avalonia.Controls.TextBlock;
    }

    private static ControlType MapControlType(Control control)
    {
        return control switch
        {
            global::Avalonia.Controls.TextBox => ControlType.Edit,
            global::Avalonia.Controls.CheckBox => ControlType.CheckBox,
            global::Avalonia.Controls.RadioButton => ControlType.RadioButton,
            global::Avalonia.Controls.Primitives.ToggleButton => ControlType.Button,
            global::Avalonia.Controls.Button => ControlType.Button,
            global::Avalonia.Controls.TextBlock => ControlType.Text,
            global::Avalonia.Controls.Label => ControlType.Text,
            global::Avalonia.Controls.ListBox => ControlType.List,
            global::Avalonia.Controls.ComboBox => ControlType.ComboBox,
            global::Avalonia.Controls.Slider => ControlType.Slider,
            global::Avalonia.Controls.ProgressBar => ControlType.ProgressBar,
            global::Avalonia.Controls.Calendar => ControlType.Calendar,
            global::Avalonia.Controls.DatePicker => ControlType.DatePicker,
            global::Avalonia.Controls.TabControl => ControlType.Tab,
            global::Avalonia.Controls.TabItem => ControlType.TabItem,
            global::Avalonia.Controls.TreeView => ControlType.Tree,
            global::Avalonia.Controls.TreeViewItem => ControlType.TreeItem,
            global::Avalonia.Controls.DataGrid => ControlType.DataGrid,
            _ => ControlType.Custom
        };
    }
}

internal sealed class Window : AutomationElement
{
    internal Window(global::Avalonia.Controls.Window window) : base(window)
    {
    }

    internal global::Avalonia.Controls.Window Native => (global::Avalonia.Controls.Window)Control;
}

internal class TextBox : AutomationElement
{
    internal TextBox(global::Avalonia.Controls.TextBox textBox) : base(textBox)
    {
    }

    private global::Avalonia.Controls.TextBox Native => (global::Avalonia.Controls.TextBox)Control;

    public string Text
    {
        get => Ui(() => Native.Text ?? string.Empty);
        set => Ui(() =>
        {
            Native.Text = value;
            return true;
        });
    }

    public void Enter(string value)
    {
        Text = value;
    }
}

internal class Button : AutomationElement
{
    internal Button(global::Avalonia.Controls.Button button) : base(button)
    {
    }

    private global::Avalonia.Controls.Button Native => (global::Avalonia.Controls.Button)Control;

    public void Invoke()
    {
        Ui(() =>
        {
            Native.RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
            return true;
        });
    }
}

internal class Label : AutomationElement
{
    internal Label(Control control) : base(control)
    {
    }

    public string Text => Ui(() =>
    {
        return Control switch
        {
            global::Avalonia.Controls.TextBlock textBlock => textBlock.Text ?? string.Empty,
            global::Avalonia.Controls.Label label => label.Content?.ToString() ?? string.Empty,
            _ => ReadControlName(Control)
        };
    });
}

internal sealed class ListBoxItem
{
    internal ListBoxItem(object? item)
    {
        Item = item;
    }

    private object? Item { get; }

    public string? Text => Item?.ToString();

    public string? Name => Text;
}

internal class ListBox : AutomationElement
{
    internal ListBox(global::Avalonia.Controls.ListBox listBox) : base(listBox)
    {
    }

    private global::Avalonia.Controls.ListBox Native => (global::Avalonia.Controls.ListBox)Control;

    public ListBoxItem[] Items => Ui(() =>
    {
        var values = ReadItems(Native.Items);
        return values.Select(item => new ListBoxItem(item)).ToArray();
    });

    public string? SelectedItemText => Ui(() => Native.SelectedItem?.ToString());

    public void SelectItem(string itemText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemText);

        Ui(() =>
        {
            var normalizedTarget = NormalizeLookupText(itemText);
            var values = ReadItems(Native.Items);
            var match = values.FirstOrDefault(candidate =>
                string.Equals(
                    NormalizeLookupText(candidate?.ToString()),
                    normalizedTarget,
                    StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new InvalidOperationException($"ListBox item '{itemText}' was not found.");
            }

            Native.SelectedItem = match;
            return true;
        });
    }

    private static IReadOnlyList<object?> ReadItems(IEnumerable? enumerable)
    {
        if (enumerable is null)
        {
            return Array.Empty<object?>();
        }

        return enumerable.Cast<object?>().ToArray();
    }

    private static string NormalizeLookupText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}

internal class CheckBox : AutomationElement
{
    internal CheckBox(global::Avalonia.Controls.CheckBox checkBox) : base(checkBox)
    {
    }

    private global::Avalonia.Controls.CheckBox Native => (global::Avalonia.Controls.CheckBox)Control;

    public bool? IsChecked
    {
        get => Ui(() => Native.IsChecked);
        set => Ui(() =>
        {
            Native.IsChecked = value;
            return true;
        });
    }
}

internal sealed class ComboBoxItem
{
    internal ComboBoxItem(object? item)
    {
        Item = item;
    }

    private object? Item { get; }

    public string Text => Item?.ToString() ?? string.Empty;

    public string Name => Text;
}

internal class ComboBox : AutomationElement
{
    internal ComboBox(global::Avalonia.Controls.ComboBox comboBox) : base(comboBox)
    {
    }

    private global::Avalonia.Controls.ComboBox Native => (global::Avalonia.Controls.ComboBox)Control;

    public ComboBoxItem[] Items => Ui(() =>
    {
        var values = Native.Items?.Cast<object?>().ToArray() ?? Array.Empty<object?>();
        return values.Select(item => new ComboBoxItem(item)).ToArray();
    });

    public object? SelectedItem => Ui(() =>
    {
        var selected = Native.SelectedItem;
        return selected is null ? null : new ComboBoxItem(selected);
    });

    public int SelectedIndex
    {
        get => Ui(() => Native.SelectedIndex);
        set => Ui(() =>
        {
            Native.SelectedIndex = value;
            return true;
        });
    }

    public void Select(int index)
    {
        SelectedIndex = index;
    }

    public void Expand()
    {
        // no-op in headless mode
    }
}

internal class RadioButton : AutomationElement
{
    internal RadioButton(global::Avalonia.Controls.RadioButton radioButton) : base(radioButton)
    {
    }

    private global::Avalonia.Controls.RadioButton Native => (global::Avalonia.Controls.RadioButton)Control;

    public bool? IsChecked
    {
        get => Ui(() => Native.IsChecked);
        set => Ui(() =>
        {
            Native.IsChecked = value;
            return true;
        });
    }
}

internal class ToggleButton : AutomationElement
{
    internal ToggleButton(global::Avalonia.Controls.Primitives.ToggleButton toggleButton) : base(toggleButton)
    {
    }

    private global::Avalonia.Controls.Primitives.ToggleButton Native => (global::Avalonia.Controls.Primitives.ToggleButton)Control;

    public bool IsToggled
    {
        get => Ui(() => Native.IsChecked == true);
    }

    public void Toggle()
    {
        Ui(() =>
        {
            Native.IsChecked = !(Native.IsChecked ?? false);
            return true;
        });
    }
}

internal class Slider : AutomationElement
{
    internal Slider(global::Avalonia.Controls.Slider slider) : base(slider)
    {
    }

    private global::Avalonia.Controls.Slider Native => (global::Avalonia.Controls.Slider)Control;

    public double Value
    {
        get => Ui(() => Native.Value);
        set => Ui(() =>
        {
            Native.Value = value;
            return true;
        });
    }
}

internal class ProgressBar : AutomationElement
{
    internal ProgressBar(global::Avalonia.Controls.ProgressBar progressBar) : base(progressBar)
    {
    }

    private global::Avalonia.Controls.ProgressBar Native => (global::Avalonia.Controls.ProgressBar)Control;

    public double Value => Ui(() => Native.Value);
}

internal class Calendar : AutomationElement
{
    internal Calendar(global::Avalonia.Controls.Calendar calendar) : base(calendar)
    {
    }

    private global::Avalonia.Controls.Calendar Native => (global::Avalonia.Controls.Calendar)Control;

    public DateTime[] SelectedDates => Ui(() =>
    {
        if (Native.SelectedDate is { } selected)
        {
            return [selected.Date];
        }

        return Array.Empty<DateTime>();
    });

    public void SelectDate(DateTime date)
    {
        Ui(() =>
        {
            Native.SelectedDate = date.Date;
            return true;
        });
    }
}

internal class DateTimePicker : AutomationElement
{
    internal DateTimePicker(global::Avalonia.Controls.DatePicker datePicker) : base(datePicker)
    {
    }

    private global::Avalonia.Controls.DatePicker Native => (global::Avalonia.Controls.DatePicker)Control;

    public DateTime? SelectedDate
    {
        get => Ui(() => Native.SelectedDate?.Date);
        set => Ui(() =>
        {
            Native.SelectedDate = value?.Date;
            return true;
        });
    }
}

internal class Spinner : AutomationElement
{
    internal Spinner(global::Avalonia.Controls.TextBox textBox) : base(textBox)
    {
    }

    private global::Avalonia.Controls.TextBox Native => (global::Avalonia.Controls.TextBox)Control;

    public double Value
    {
        get => Ui(() =>
        {
            var text = Native.Text ?? string.Empty;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        });
        set => Ui(() =>
        {
            Native.Text = value.ToString(CultureInfo.InvariantCulture);
            return true;
        });
    }
}

internal class Tab : AutomationElement
{
    internal Tab(global::Avalonia.Controls.TabControl tab) : base(tab)
    {
    }

    private global::Avalonia.Controls.TabControl Native => (global::Avalonia.Controls.TabControl)Control;

    public TabItem[] Items => Ui(() =>
    {
        var items = Native.Items?.OfType<global::Avalonia.Controls.TabItem>() ?? Enumerable.Empty<global::Avalonia.Controls.TabItem>();
        return items.Select(item => new TabItem(item)).ToArray();
    });

    public void SelectTabItem(string itemText)
    {
        Ui(() =>
        {
            var target = Native.Items?.OfType<global::Avalonia.Controls.TabItem>()
                .FirstOrDefault(item => string.Equals(item.Header?.ToString(), itemText, StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                throw new InvalidOperationException($"Tab item '{itemText}' was not found.");
            }

            Native.SelectedItem = target;
            target.IsSelected = true;
            return true;
        });
    }
}

internal class TabItem : AutomationElement
{
    internal TabItem(global::Avalonia.Controls.TabItem tabItem) : base(tabItem)
    {
    }

    private global::Avalonia.Controls.TabItem Native => (global::Avalonia.Controls.TabItem)Control;

    public bool IsSelected => Ui(() => Native.IsSelected);

    public override string Name => Ui(() => Native.Header?.ToString() ?? base.Name);

    public void Select()
    {
        Ui(() =>
        {
            if (Native.Parent is global::Avalonia.Controls.TabControl parent)
            {
                parent.SelectedItem = Native;
                Native.IsSelected = true;
                return true;
            }

            Native.IsSelected = true;
            return true;
        });
    }
}

internal class Tree : AutomationElement
{
    internal Tree(global::Avalonia.Controls.TreeView tree) : base(tree)
    {
    }

    private global::Avalonia.Controls.TreeView Native => (global::Avalonia.Controls.TreeView)Control;

    public TreeItem[] Items => Ui(() =>
    {
        var roots = ControlTree.EnumerateDescendants(Native)
            .OfType<global::Avalonia.Controls.TreeViewItem>()
            .Where(item => item.Parent == Native)
            .Select(item => new TreeItem(item))
            .ToArray();

        if (roots.Length > 0)
        {
            return roots;
        }

        return ControlTree.EnumerateDescendants(Native)
            .OfType<global::Avalonia.Controls.TreeViewItem>()
            .Select(item => new TreeItem(item))
            .ToArray();
    });

    public TreeItem? SelectedTreeItem => Ui(() =>
    {
        if (Native.SelectedItem is global::Avalonia.Controls.TreeViewItem selected)
        {
            return new TreeItem(selected);
        }

        return null;
    });
}

internal class TreeItem : AutomationElement
{
    internal TreeItem(global::Avalonia.Controls.TreeViewItem item) : base(item)
    {
    }

    private global::Avalonia.Controls.TreeViewItem Native => (global::Avalonia.Controls.TreeViewItem)Control;

    public bool IsSelected
    {
        get => Ui(() => Native.IsSelected);
        set => Ui(() =>
        {
            Native.IsSelected = value;
            return true;
        });
    }

    public string Text => Ui(() => Native.Header?.ToString() ?? string.Empty);

    public override string Name => Text;

    public TreeItem[] Items => Ui(() =>
    {
        return ControlTree.EnumerateDescendants(Native)
            .OfType<global::Avalonia.Controls.TreeViewItem>()
            .Where(child => child.Parent == Native)
            .Select(child => new TreeItem(child))
            .ToArray();
    });

    public void Expand()
    {
        Ui(() =>
        {
            Native.IsExpanded = true;
            return true;
        });
    }

    public void Select()
    {
        Ui(() =>
        {
            Native.IsSelected = true;
            if (Native.Parent is global::Avalonia.Controls.TreeView tree)
            {
                tree.SelectedItem = Native;
            }

            return true;
        });
    }
}

internal class Grid : AutomationElement
{
    internal Grid(global::Avalonia.Controls.DataGrid grid) : base(grid)
    {
    }

    private global::Avalonia.Controls.DataGrid Native => (global::Avalonia.Controls.DataGrid)Control;

    public GridRow[] Rows => Ui(() => ReadRows(Native));

    public GridRow? GetRowByIndex(int index)
    {
        return Ui(() =>
        {
            var rows = ReadRows(Native);
            if (index < 0 || index >= rows.Length)
            {
                return null;
            }

            return rows[index];
        });
    }

    private static GridRow[] ReadRows(global::Avalonia.Controls.DataGrid dataGrid)
    {
        if (dataGrid.ItemsSource is not IEnumerable source)
        {
            return Array.Empty<GridRow>();
        }

        var columnBindings = dataGrid.Columns
            .OfType<DataGridBoundColumn>()
            .Select(static column => column.Binding)
            .ToArray();

        var rows = new List<GridRow>();
        foreach (var item in source)
        {
            rows.Add(new GridRow(item, columnBindings));
        }

        return rows.ToArray();
    }
}

internal class DataGridView : Grid
{
    internal DataGridView(global::Avalonia.Controls.DataGrid grid) : base(grid)
    {
    }
}

internal class GridRow
{
    internal GridRow(object? item, IReadOnlyList<IBinding?> columnBindings)
    {
        Item = item;
        ColumnBindings = columnBindings;
    }

    private object? Item { get; }

    private IReadOnlyList<IBinding?> ColumnBindings { get; }

    public GridCell[] Cells
    {
        get
        {
            if (Item is null)
            {
                return Array.Empty<GridCell>();
            }

            if (Item is string value)
            {
                return [new GridCell(value)];
            }

            if (ColumnBindings.Count > 0)
            {
                return ColumnBindings
                    .Select(binding => new GridCell(ReadBoundValue(Item, binding)))
                    .ToArray();
            }

            var properties = Item
                .GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead)
                .OrderBy(property => property.MetadataToken)
                .ToArray();

            if (properties.Length == 0)
            {
                return [new GridCell(Item.ToString() ?? string.Empty)];
            }

            return properties
                .Select(property =>
                {
                    var propertyValue = property.GetValue(Item);
                    return new GridCell(propertyValue?.ToString() ?? string.Empty);
                })
                .ToArray();
        }
    }

    private static string ReadBoundValue(object item, IBinding? binding)
    {
        if (binding is Binding { Path: { } path } && !string.IsNullOrWhiteSpace(path))
        {
            return ReadPropertyPath(item, path);
        }

        return item.ToString() ?? string.Empty;
    }

    private static string ReadPropertyPath(object item, string path)
    {
        object? current = item;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is null)
            {
                return string.Empty;
            }

            var property = current
                .GetType()
                .GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanRead)
            {
                return string.Empty;
            }

            current = property.GetValue(current);
        }

        return current?.ToString() ?? string.Empty;
    }
}

internal class GridCell
{
    internal GridCell(string value)
    {
        Value = value;
    }

    public string Value { get; }
}
