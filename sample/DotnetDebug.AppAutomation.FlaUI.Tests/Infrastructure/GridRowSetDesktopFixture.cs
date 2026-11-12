using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AppAutomation.Abstractions;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;

// A stock Windows DataGrid publishes Selection, including virtualized row peers.
internal sealed class GridRowSetDesktopFixture : IDisposable
{
    private readonly Thread _thread;
    private Dispatcher? _dispatcher;
    private readonly DataGrid _grid;
    private int _stopping;
    private Exception? _closeError;

    public GridRowSetDesktopFixture(TimeSpan? startupTimeout = null, Action? beforeShow = null, bool compositeKey = false)
    {
        var ready = new TaskCompletionSource<(nint Handle, DataGrid Grid)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            System.Windows.Window? window = null;
            try
            {
                Volatile.Write(ref _dispatcher, Dispatcher.CurrentDispatcher);
                if (Volatile.Read(ref _stopping) != 0) return;
                var grid = new DataGrid
                {
                    AutoGenerateColumns = false, IsReadOnly = true, EnableRowVirtualization = true,
                    SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow,
                    ItemsSource = Enumerable.Range(1, 14).Select(number => new Item($"ROW-{number:D2}")).ToArray()
                };
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Key", Binding = new Binding(nameof(Item.Key)),
                    Width = compositeKey ? new DataGridLength(500) : DataGridLength.Auto
                });
                if (compositeKey)
                {
                    grid.Columns.Add(new DataGridTextColumn
                    {
                        Header = "Suffix", Binding = new Binding(nameof(Item.Suffix)), Width = new DataGridLength(500)
                    });
                }
                AutomationProperties.SetAutomationId(grid, "ArmComplexDataGridControl");
                window = new System.Windows.Window { Content = grid, Width = 420, Height = 200, Title = "Grid selection fixture" };
                beforeShow?.Invoke();
                if (Volatile.Read(ref _stopping) != 0) return;
                window.Show();
                grid.UpdateLayout();
                if (Volatile.Read(ref _stopping) != 0) return;
                ready.TrySetResult((new WindowInteropHelper(window).Handle, grid));
                if (Volatile.Read(ref _stopping) == 0) Dispatcher.Run();
            }
            catch (Exception error) { ready.TrySetException(error); }
            finally
            {
                try { if (window?.IsVisible == true) window.Close(); }
                catch (Exception error) { _closeError = error; }
            }
        }) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        UIA3Automation? automation = null;
        try
        {
            var (handle, grid) = ready.Task.WaitAsync(startupTimeout ?? TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            _grid = grid;
            automation = new UIA3Automation();
            Window = automation.FromHandle(handle).AsWindow();
            Automation = automation;
        }
        catch (Exception error)
        {
            try { Stop(automation); }
            catch (Exception cleanup) { error.Data["FixtureCleanup"] = cleanup; }
            throw;
        }
    }

    public UIA3Automation Automation { get; }
    public Window Window { get; }
    public string Selection => _grid.Dispatcher.Invoke(() =>
        string.Join(",", _grid.SelectedItems.Cast<Item>().Select(static item => item.Key).Order()));
    public bool IsRealized(int index) => _grid.Dispatcher.Invoke(() => _grid.ItemContainerGenerator.ContainerFromIndex(index) is not null);
    public void SelectExtra() => _grid.Dispatcher.Invoke(() => _grid.SelectedItems.Add(_grid.Items[2]));
    public void ScrollToLast() => _grid.Dispatcher.Invoke(() =>
    {
        _grid.ScrollIntoView(_grid.Items[^1]);
        _grid.UpdateLayout();
    });

    public void PublishSelection(params GridRowSelector[] rows) => _grid.Dispatcher.Invoke(() =>
        AutomationProperties.SetItemStatus(_grid, GridRowSelectionValue.ToAutomationValue(rows)));

    public double HorizontalOffset => _grid.Dispatcher.Invoke(() => FindScrollViewer(_grid).HorizontalOffset);

    private static ScrollViewer FindScrollViewer(System.Windows.DependencyObject root)
    {
        var children = new Queue<System.Windows.DependencyObject>();
        children.Enqueue(root);
        while (children.TryDequeue(out var current))
        {
            if (current is ScrollViewer viewer) return viewer;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
                children.Enqueue(VisualTreeHelper.GetChild(current, index));
        }
        throw new InvalidOperationException("The grid fixture does not have a scroll viewer.");
    }

    public void Dispose() => Stop(Automation);

    private void Stop(UIA3Automation? automation)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        try { automation?.Dispose(); }
        finally
        {
            if (Volatile.Read(ref _dispatcher) is { HasShutdownStarted: false } dispatcher)
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            if (!_thread.Join(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The grid selection fixture's dispatcher did not stop.");
            if (_closeError is { } closeError)
                throw new InvalidOperationException("The grid selection fixture's window did not close.", closeError);
        }
    }

    private sealed record Item(string Key)
    {
        public string Suffix => string.Empty;
        public override string ToString() => Key;
    }
}
