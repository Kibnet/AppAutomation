using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace DotnetDebug.Avalonia;

public partial class ArmDesktopControl : UserControl
{
    public ArmDesktopControl()
    {
        InitializeComponent();
        DataContext = this;
        ArmStatusExpanderToggle.PropertyChanged += OnArmStatusExpanderPropertyChanged;
        ArmMetadataToggle.PropertyChanged += OnArmMetadataTogglePropertyChanged;
        ArmApprovalToggle.PropertyChanged += OnArmApprovalTogglePropertyChanged;
        BuildArmRows(3);
        ArmDateRangeFrom.SelectedDate = new DateTimeOffset(new DateTime(2026, 4, 1));
        ArmDateRangeTo.SelectedDate = new DateTimeOffset(new DateTime(2026, 4, 30));
        ArmShellNavigationList.SelectedIndex = 0;
        ArmShellPaneTabs.SelectedIndex = 0;
    }

    public ObservableCollection<ArmDesktopGridRowViewModel> ArmGridRows { get; } = new();

    public string[] ArmSearchItems { get; } =
    [
        "Customer Alpha",
        "Customer Beta",
        "Order 1001",
        "Invoice 2026"
    ];

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

    private void OnArmSearchApplyClick(object? sender, RoutedEventArgs e)
    {
        var query = ArmSearchInput.Text ?? string.Empty;
        var selected = ArmSearchResults.SelectedItem?.ToString() ?? "<none>";
        ArmSearchStatusLabel.Content = $"Search applied: {query}; selected={selected}";
    }

    private void OnArmSearchClearClick(object? sender, RoutedEventArgs e)
    {
        ArmSearchInput.Text = string.Empty;
        ArmSearchResults.SelectedIndex = -1;
        ArmSearchStatusLabel.Content = "Search cleared";
    }

    private void OnArmSearchResultsChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ArmSearchResults.SelectedItem is not null)
        {
            ArmSearchStatusLabel.Content = $"Search selected: {ArmSearchResults.SelectedItem}";
        }
    }

    private void OnArmServerPickerOpenClick(object? sender, RoutedEventArgs e)
    {
        ArmServerPickerStatusLabel.Content = "Server picker opened";
    }

    private void OnArmServerPickerClearClick(object? sender, RoutedEventArgs e)
    {
        ArmServerPickerInput.Text = string.Empty;
        ArmServerPickerResults.SelectedIndex = -1;
        ArmServerPickerStatusLabel.Content = "Server picker cleared";
    }

    private void OnArmServerPickerResultsChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ArmServerPickerResults.SelectedItem is not null)
        {
            ArmServerPickerStatusLabel.Content = $"Server selected: {ArmServerPickerResults.SelectedItem}";
        }
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

    private void OnArmStatusExpanderClick(object? sender, RoutedEventArgs e)
    {
        UpdateArmStatusExpanderLabel();
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
        if (e.Property == ToggleButton.IsCheckedProperty)
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
        ArmStatusLabel.Content = $"Status expanded: {ArmStatusExpanderToggle.IsChecked == true}";
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
