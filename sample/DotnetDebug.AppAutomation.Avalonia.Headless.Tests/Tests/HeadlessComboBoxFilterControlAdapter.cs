using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using DotnetDebug.Avalonia;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

internal sealed class HeadlessComboBoxFilterControlAdapter : IUiControlAdapter
{
    private const string PropertyName = "ArmStatusFilter";
    private readonly IComboBoxFilterControl _control;

    public HeadlessComboBoxFilterControlAdapter(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        _control = new HeadlessComboBoxFilterControl(mainWindow);
    }

    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);

        return requestedType == typeof(IComboBoxFilterControl)
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

    private sealed class HeadlessComboBoxFilterControl : IComboBoxFilterControl
    {
        private readonly Window _mainWindow;
        private string[] _pendingItems = [];

        public HeadlessComboBoxFilterControl(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public string AutomationId => PropertyName;

        public string Name => "Statuses";

        public bool IsEnabled => ReadControl(static control => control.IsEnabled);

        public IReadOnlyList<string> Items => ReadControl(
            static control => control.ArmStatusFilterItems
                .Select(static item => item.Name)
                .ToArray());

        public IReadOnlyList<string> SelectedItems => IsOpen
            ? _pendingItems.ToArray()
            : ReadControl(
                static control => control.SelectedArmStatusFilterItems
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
            var missing = requested
                .Where(value => !Items.Contains(value, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Combo-box filter items were not found: [{string.Join(", ", missing)}].");
            }

            _pendingItems = requested;
        }

        public void Apply()
        {
            HeadlessRuntime.Dispatch(() =>
            {
                var control = GetControl();
                var selected = control.ArmStatusFilterItems
                    .Where(item => _pendingItems.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                control.SelectedArmStatusFilterItems.Clear();
                foreach (var item in selected)
                {
                    control.SelectedArmStatusFilterItems.Add(item);
                }
            });
            IsOpen = false;
        }

        public void Cancel()
        {
            _pendingItems = [];
            IsOpen = false;
        }

        private TResult ReadControl<TResult>(Func<ArmDesktopControl, TResult> read)
        {
            return HeadlessRuntime.Dispatch(() => read(GetControl()));
        }

        private ArmDesktopControl GetControl()
        {
            return _mainWindow.GetVisualDescendants().OfType<ArmDesktopControl>().SingleOrDefault()
                ?? _mainWindow.GetLogicalDescendants().OfType<ArmDesktopControl>().Single();
        }
    }
}
