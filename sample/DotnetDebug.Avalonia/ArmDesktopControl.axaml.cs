using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Eremex.AvaloniaUI.Controls.Editors;

namespace DotnetDebug.Avalonia;

public partial class ArmDesktopControl : UserControl
{
    public ArmDesktopControl()
    {
        InitializeComponent();
        DataContext = this;
        ArmStatusExpander.PropertyChanged += OnArmStatusExpanderPropertyChanged;
        ArmMetadataToggle.PropertyChanged += OnArmMetadataTogglePropertyChanged;
        ArmApprovalToggle.PropertyChanged += OnArmApprovalTogglePropertyChanged;
        BuildArmRows(3);
        ArmDateRangeFrom.SelectedDate = new DateTimeOffset(new DateTime(2026, 4, 1));
        ArmDateRangeTo.SelectedDate = new DateTimeOffset(new DateTime(2026, 4, 30));
        ArmShellNavigationList.SelectedIndex = 0;
        ArmShellPaneTabs.SelectedIndex = 0;
        SelectedArmStatusFilterItems.Add(ArmStatusFilterItems[0]);
        SelectedArmStatusFilterItems.CollectionChanged += (_, _) => UpdateArmStatusFilterLabel();
        UpdateArmStatusFilterLabel();
    }

    public ObservableCollection<ArmDesktopGridRowViewModel> ArmGridRows { get; } = new();

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
        "Product 42",
        "Service Contract",
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

    private void OnArmGridBuildClick(object? sender, RoutedEventArgs e)
    {
        BuildArmRows(3);
        ArmGridStatusLabel.Content = "Grid rows: 3";
    }

    private void OnArmGridOpenClick(object? sender, RoutedEventArgs e)
    {
        ArmGridStatusLabel.Content = ArmGridRows.Count == 0
            ? "Grid open: no rows"
            : $"Grid opened: {ArmGridRows[0].Key}";
    }

    private void OnArmGridRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ArmDesktopGridRowViewModel row })
        {
            ArmGridStatusLabel.Content = $"Grid opened: {row.Key}";
            e.Handled = true;
        }
    }

    private void OnArmGridSortClick(object? sender, RoutedEventArgs e)
    {
        var sorted = ArmGridRows.OrderByDescending(row => row.Value, StringComparer.Ordinal).ToArray();
        ArmGridRows.Clear();
        foreach (var row in sorted)
        {
            ArmGridRows.Add(row);
        }

        ArmGridStatusLabel.Content = "Grid sorted by value";
    }

    private void OnArmGridLoadMoreClick(object? sender, RoutedEventArgs e)
    {
        var nextIndex = ArmGridRows.Count;
        ArmGridRows.Add(CreateRow(nextIndex));
        ArmGridRows.Add(CreateRow(nextIndex + 1));
        ArmGridStatusLabel.Content = $"Grid rows: {ArmGridRows.Count}";
    }

    private void OnArmGridCopyClick(object? sender, RoutedEventArgs e)
    {
        ArmGridStatusLabel.Content = "Grid copied";
    }

    private void OnArmGridExportClick(object? sender, RoutedEventArgs e)
    {
        ArmGridStatusLabel.Content = "Grid export requested";
    }

    private void OnArmGridCommitEditClick(object? sender, RoutedEventArgs e)
    {
        if (ArmGridRows.Count == 0)
        {
            ArmGridStatusLabel.Content = "Grid edit: no rows";
            return;
        }

        ArmGridRows[0].Value = ArmGridEditValueInput.Text ?? string.Empty;
        ArmGridStatusLabel.Content = $"Grid edit committed: {ArmGridRows[0].Value}";
    }

    private void OnArmGridCancelEditClick(object? sender, RoutedEventArgs e)
    {
        if (ArmGridRows.Count > 0)
        {
            ArmGridRows[0].Value = "Value-1";
            ArmGridEditValueInput.Text = ArmGridRows[0].Value;
        }

        ArmGridStatusLabel.Content = "Grid edit canceled";
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

    private void BuildArmRows(int count)
    {
        ArmGridRows.Clear();
        for (var index = 0; index < count; index++)
        {
            ArmGridRows.Add(CreateRow(index));
        }

        ArmGridEditValueInput.Text = ArmGridRows.FirstOrDefault()?.Value ?? string.Empty;
    }

    private static ArmDesktopGridRowViewModel CreateRow(int index)
    {
        var state = index % 2 == 0 ? "Open" : "Pending";
        return new ArmDesktopGridRowViewModel(index, $"Value-{index + 1}", state);
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
