using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Controls;
using DotnetDebug.Avalonia;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

internal sealed class HeadlessMultiSelectControlAdapter : IUiControlAdapter
{
    private const string PropertyName = "MultiSelection";
    private readonly IMultiSelectControl _control;

    public HeadlessMultiSelectControlAdapter(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        _control = new HeadlessMultiSelectControl(mainWindow);
    }

    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);

        return requestedType == typeof(IMultiSelectControl)
            && string.Equals(definition.PropertyName, PropertyName, StringComparison.Ordinal);
    }

    public object Resolve(
        Type requestedType,
        UiControlDefinition definition,
        IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);
        return _control;
    }

    private sealed class HeadlessMultiSelectControl : IMultiSelectControl
    {
        private readonly Window _mainWindow;
        private string[] _pendingItems = [];

        public HeadlessMultiSelectControl(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public string AutomationId => PropertyName;

        public string Name => "Choose categories";

        public bool IsEnabled => true;

        public IReadOnlyList<string> Items => ReadViewModel(
            static viewModel => viewModel.MultiSelectItems
                .Select(static item => item.Name)
                .ToArray());

        public IReadOnlyList<string> SelectedItems => IsOpen
            ? _pendingItems.ToArray()
            : ReadViewModel(
                static viewModel => viewModel.SelectedMultiSelectItems
                    .Select(static item => item.Name)
                    .ToArray());

        public bool IsOpen { get; private set; }

        public void Open()
        {
            _pendingItems = SelectedItems.ToArray();
            IsOpen = true;
        }

        public void SetSelectedItems(IReadOnlyCollection<string> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            var requested = values.Select(static value => value.Trim()).ToArray();
            var available = Items;
            var missing = requested
                .Where(value => !available.Contains(value, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Multi-select items were not found: [{string.Join(", ", missing)}].");
            }

            _pendingItems = requested;
        }

        public void Apply()
        {
            HeadlessRuntime.Dispatch(() =>
            {
                var viewModel = GetViewModel();
                var selected = viewModel.MultiSelectItems
                    .Where(item => _pendingItems.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                viewModel.SelectedMultiSelectItems.Clear();
                foreach (var item in selected)
                {
                    viewModel.SelectedMultiSelectItems.Add(item);
                }
            });
            IsOpen = false;
        }

        public void Cancel()
        {
            _pendingItems = [];
            IsOpen = false;
        }

        private TResult ReadViewModel<TResult>(Func<MainWindowViewModel, TResult> read)
        {
            return HeadlessRuntime.Dispatch(() => read(GetViewModel()));
        }

        private MainWindowViewModel GetViewModel()
        {
            return _mainWindow.DataContext as MainWindowViewModel
                ?? throw new InvalidOperationException(
                    "The sample main window does not expose its multi-select view model.");
        }
    }
}
