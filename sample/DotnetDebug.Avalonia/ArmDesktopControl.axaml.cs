using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Eremex.AvaloniaUI.Controls.Editors;
using Eremex.AvaloniaUI.Controls.Utils;

namespace DotnetDebug.Avalonia;

public partial class ArmDesktopControl : UserControl
{
    public ArmDesktopControl()
    {
        InitializeComponent();
        DataContext = this;
        ArmServerSearchPicker.CurrentSelected = ArmServerItems[1];
        ArmStatusExpander.PropertyChanged += OnArmStatusExpanderPropertyChanged;
        ArmMetadataToggle.PropertyChanged += OnArmMetadataTogglePropertyChanged;
        ArmApprovalToggle.PropertyChanged += OnArmApprovalTogglePropertyChanged;
        ArmDateRangeFrom.SelectedDate = new DateTimeOffset(new DateTime(2026, 4, 1));
        ArmDateRangeTo.SelectedDate = new DateTimeOffset(new DateTime(2026, 4, 30));
        ArmShellNavigationList.SelectedIndex = 0;
        ArmShellPaneTabs.SelectedIndex = 0;
        SelectedArmStatusFilterItems.Add(ArmStatusFilterItems[0]);
        SelectedArmStatusFilterItems.CollectionChanged += (_, _) => UpdateArmStatusFilterLabel();
        UpdateArmStatusFilterLabel();
    }

    public ObservableCollection<MultiSelectItemViewModel> ArmStatusFilterItems { get; } =
    [
        new("Open"),
        new("Pending"),
        new("Closed"),
        new("Archived")
    ];

    public ObservableCollection<MultiSelectItemViewModel> SelectedArmStatusFilterItems { get; } = [];

    public string[] ArmServerItems { get; } =
    [
        "Product 42 extended",
        "Service Contract",
        "Product 42",
        "Product  42 rolled",
        "Warehouse North",
        "Customer Archive"
    ];

    public string[] ArmShellPanes { get; } =
    [
        "Customers",
        "Orders",
        "Reports"
    ];

    private void OnArmCopyClick(object? sender, RoutedEventArgs e)
    {
        ArmCopyResultLabel.Content = $"Copied: {ArmCopyTextBox.Text ?? string.Empty}";
    }

    private void OnArmServerPickerClearClick(object? sender, RoutedEventArgs e)
    {
        ArmServerSearchPicker.SearchText = string.Empty;
        ArmServerSearchPicker.CurrentSelected = null;
        ArmServerSearchPicker.IsPopupOpen = false;
        ArmServerPickerStatusLabel.Content = "Server picker cleared";
    }

    private void OnArmServerPickerSelected(object? sender, object? selected)
    {
        if (selected is not null)
        {
            ArmServerPickerStatusLabel.Content = $"Server selected: {selected}";
        }
    }

    private void OnUnitQuantityReorderClick(object? sender, RoutedEventArgs e)
    {
        if (UnitQuantityCollection.Items.Count < 2)
        {
            return;
        }

        var lastIndex = UnitQuantityCollection.Items.Count - 1;
        var last = UnitQuantityCollection.Items[lastIndex];
        UnitQuantityCollection.Items.RemoveAt(lastIndex);
        UnitQuantityCollection.Items.Insert(0, last);
    }

    private void OnUnitQuantityInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox input)
        {
            return;
        }

        var committedValue = ReferenceEquals(input, UnitQuantityAInput)
            ? UnitQuantityACommittedValue
            : ReferenceEquals(input, UnitQuantityBInput)
                ? UnitQuantityBCommittedValue
                : UnitQuantityCCommittedValue;
        committedValue.Content = input.Text ?? string.Empty;
    }

    private void OnArmAccentColorOpenClick(object? sender, RoutedEventArgs e)
    {
        ArmAccentColorCustomValue.Text = ArmAccentColorValue.Text;
        ArmAccentColorPopup.IsVisible = true;
    }

    private void OnArmAccentColorConfirmClick(object? sender, RoutedEventArgs e)
    {
        ArmAccentColorValue.Text = ArmAccentColorCustomValue.Text;
        ArmAccentColorPopup.IsVisible = false;
    }

    private void OnArmAccentColorCancelClick(object? sender, RoutedEventArgs e)
    {
        ArmAccentColorCustomValue.Text = ArmAccentColorValue.Text;
        ArmAccentColorPopup.IsVisible = false;
    }

    private void OnNestedMenuItemClick(object? sender, RoutedEventArgs e)
    {
        MenuStatusLabel.Content = "Menu: snapshot exported";
    }

    private void OnRefreshMenuItemClick(object? sender, RoutedEventArgs e)
    {
        MenuStatusLabel.Content = "Menu: refreshed";
    }

    private void OnContextPinMenuItemClick(object? sender, RoutedEventArgs e)
    {
        ContextMenuStatusLabel.Content = "Context: pinned";
    }

    private void OnContextSummaryMenuItemClick(object? sender, RoutedEventArgs e)
    {
        ContextMenuStatusLabel.Content = "Context: summary exported";
    }

    private void OnArmStatusFilterLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is PopupEditor popupEditor)
        {
            MultiSelectEditorAutomation.Apply(popupEditor, "ArmStatusFilter");
        }
    }

    private void UpdateArmStatusFilterLabel()
    {
        var selected = SelectedArmStatusFilterItems
            .Select(static item => item.Name)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        ArmStatusFilterStatusLabel.Content = selected.Length == 0
            ? "Filter: none"
            : $"Filter: {string.Join(", ", selected)}";
    }

    private void OnArmDateRangeOpenClick(object? sender, RoutedEventArgs e)
    {
        ArmDateRangeStatusLabel.Content = "Date range opened";
    }

    private void OnArmDateRangeApplyClick(object? sender, RoutedEventArgs e)
    {
        ArmDateRangeStatusLabel.Content =
            $"Date filter: {FormatDate(ArmDateRangeFrom.SelectedDate)}..{FormatDate(ArmDateRangeTo.SelectedDate)}";
    }

    private void OnArmDateRangeCancelClick(object? sender, RoutedEventArgs e)
    {
        ArmDateRangeStatusLabel.Content = "Date filter canceled";
    }

    private void OnArmNumericRangeOpenClick(object? sender, RoutedEventArgs e)
    {
        ArmNumericRangeStatusLabel.Content = "Numeric range opened";
    }

    private void OnArmNumericRangeApplyClick(object? sender, RoutedEventArgs e)
    {
        ArmNumericRangeStatusLabel.Content =
            $"Numeric filter: {ArmNumericRangeFrom.Text ?? string.Empty}..{ArmNumericRangeTo.Text ?? string.Empty}";
    }

    private void OnArmNumericRangeCancelClick(object? sender, RoutedEventArgs e)
    {
        ArmNumericRangeStatusLabel.Content = "Numeric filter canceled";
    }

    private void OnArmDialogConfirmClick(object? sender, RoutedEventArgs e)
    {
        ArmDialogResultLabel.Content = "Dialog confirmed";
    }

    private void OnArmDialogCancelClick(object? sender, RoutedEventArgs e)
    {
        ArmDialogResultLabel.Content = "Dialog canceled";
    }

    private void OnArmDialogDismissClick(object? sender, RoutedEventArgs e)
    {
        ArmDialogResultLabel.Content = "Dialog dismissed";
    }

    private void OnArmNotificationDismissClick(object? sender, RoutedEventArgs e)
    {
        ArmNotificationText.IsEnabled = false;
        ArmNotificationDismissButton.IsEnabled = false;
        ArmNotificationStatusLabel.Content = "Notification dismissed";
    }

    private void OnArmFolderExportOpenClick(object? sender, RoutedEventArgs e)
    {
        ArmFolderExportStatusLabel.Content = "Export folder dialog opened";
    }

    private void OnArmFolderExportSelectClick(object? sender, RoutedEventArgs e)
    {
        var path = ArmFolderExportPathInput.Text ?? string.Empty;
        ArmFolderExportStatusLabel.Content = $"Export folder selected: {path}";
        ArmNotificationText.Content = "Export ready";
        ArmNotificationText.IsEnabled = true;
        ArmNotificationDismissButton.IsEnabled = true;
    }

    private void OnArmFolderExportCancelClick(object? sender, RoutedEventArgs e)
    {
        ArmFolderExportStatusLabel.Content = "Export folder canceled";
    }

    private void OnArmShellNavigationChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ArmShellPaneTabs is null || ArmShellActivePaneLabel is null)
        {
            return;
        }

        var pane = ArmShellNavigationList.SelectedItem switch
        {
            string value => value,
            ListBoxItem item => item.Content?.ToString(),
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(pane))
        {
            ActivatePane(pane);
        }
    }

    private void OnArmShellPaneChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ArmShellActivePaneLabel is null)
        {
            return;
        }

        if (ArmShellPaneTabs.SelectedItem is TabItem tab && tab.Header is not null)
        {
            ArmShellActivePaneLabel.Content = tab.Header.ToString();
        }
    }

    private void OnArmReloadClick(object? sender, RoutedEventArgs e)
    {
        ArmLoadingProgressBar.Value = 100;
        ArmLoadingStatusLabel.Content = "Reloaded: 100%";
    }

    private void OnArmMetadataToggleClick(object? sender, RoutedEventArgs e)
    {
        UpdateArmMetadataLabel();
    }

    private void OnArmApprovalToggleClick(object? sender, RoutedEventArgs e)
    {
        UpdateArmApprovalLabel();
    }

    private void OnArmCrudAddClick(object? sender, RoutedEventArgs e)
    {
        ArmActionStatusLabel.Content = "CRUD: added";
    }

    private void OnArmCrudEditClick(object? sender, RoutedEventArgs e)
    {
        ArmActionStatusLabel.Content = "CRUD: edited";
    }

    private void OnArmCrudDeleteClick(object? sender, RoutedEventArgs e)
    {
        ArmActionStatusLabel.Content = "CRUD: deleted";
    }

    private void OnArmSaveClick(object? sender, RoutedEventArgs e)
    {
        ArmActionStatusLabel.Content = "Action: saved";
    }

    private void OnArmSaveCloseClick(object? sender, RoutedEventArgs e)
    {
        ArmActionStatusLabel.Content = "Action: saved and closed";
    }

    private void OnArmCloseClick(object? sender, RoutedEventArgs e)
    {
        ArmActionStatusLabel.Content = "Action: closed";
    }

    private void ActivatePane(string pane)
    {
        if (ArmShellPaneTabs is null || ArmShellActivePaneLabel is null)
        {
            return;
        }

        ArmShellActivePaneLabel.Content = pane;
        var paneIndex = Array.IndexOf(ArmShellPanes, pane);
        if (paneIndex >= 0)
        {
            ArmShellPaneTabs.SelectedIndex = paneIndex;
        }
    }

    private void OnArmStatusExpanderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Expander.IsExpandedProperty)
        {
            UpdateArmStatusExpanderLabel();
        }
    }

    private void OnArmMetadataTogglePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ToggleButton.IsCheckedProperty)
        {
            UpdateArmMetadataLabel();
        }
    }

    private void OnArmApprovalTogglePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ToggleButton.IsCheckedProperty)
        {
            UpdateArmApprovalLabel();
        }
    }

    private void UpdateArmStatusExpanderLabel()
    {
        ArmStatusLabel.Content = $"Status expanded: {ArmStatusExpander.IsExpanded}";
    }

    private void UpdateArmMetadataLabel()
    {
        ArmMetadataStatusLabel.Content = $"Metadata visible: {ArmMetadataToggle.IsChecked == true}";
    }

    private void UpdateArmApprovalLabel()
    {
        ArmApprovalStatusLabel.Content = ArmApprovalToggle.IsChecked == true ? "Approval: approved" : "Approval: pending";
    }

    private static string FormatDate(DateTimeOffset? value)
    {
        return value?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none";
    }
}
