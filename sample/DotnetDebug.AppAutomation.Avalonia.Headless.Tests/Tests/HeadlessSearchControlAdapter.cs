using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using DotnetDebug.Avalonia;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

internal sealed class HeadlessSearchControlAdapter : IUiControlAdapter
{
    private const string PropertyName = "ArmTableSearch";
    private readonly ISearchControl _control;

    public HeadlessSearchControlAdapter(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        _control = new HeadlessSearchControl(mainWindow);
    }

    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        return requestedType == typeof(ISearchControl)
            && string.Equals(definition.PropertyName, PropertyName, StringComparison.Ordinal);
    }

    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        return _control;
    }

    private sealed class HeadlessSearchControl : ISearchControl
    {
        private readonly Window _mainWindow;

        public HeadlessSearchControl(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public string AutomationId => PropertyName;

        public string Name => PropertyName;

        public bool IsEnabled => Read(static control => control.IsEnabled);

        public string Text => Read(static control => control.SearchText);

        public IReadOnlyList<string> HistoryItems => Read(static control => control.HistoryItems.ToArray());

        public bool IsHistoryOpen => Read(static control => control.IsHistoryOpen);

        public void EnterSearch(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            Write(control => control.SearchText = value);
        }

        public void ClearSearch() => Write(control => control.SearchText = string.Empty);

        public void OpenHistory() => Write(static control => control.OpenHistoryIfAvailable());

        public void ApplySearchFromHistory(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            Write(control => control.ApplyHistoryItem(value));
        }

        private TResult Read<TResult>(Func<SearchHistoryControl, TResult> read)
        {
            return HeadlessRuntime.Dispatch(() => read(GetControl()));
        }

        private void Write(Action<SearchHistoryControl> write)
        {
            HeadlessRuntime.Dispatch(() =>
            {
                write(GetControl());
                return true;
            });
        }

        private SearchHistoryControl GetControl()
        {
            return _mainWindow.GetVisualDescendants().OfType<SearchHistoryControl>().SingleOrDefault()
                ?? _mainWindow.GetLogicalDescendants().OfType<SearchHistoryControl>().Single();
        }
    }
}
