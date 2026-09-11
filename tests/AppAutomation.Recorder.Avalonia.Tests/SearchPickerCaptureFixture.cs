using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal sealed class SearchPickerCaptureFixture : IDisposable
{
    private static readonly string[] DefaultItems = ["Search result", "Item 42"];
    private readonly StackPanel _root;

    private SearchPickerCaptureFixture(
        RecorderSession session,
        StackPanel root,
        IReadOnlyList<SearchPickerSurface> pickers,
        SearchPickerCaptureLogger logger)
    {
        Session = session;
        _root = root;
        Pickers = pickers;
        Logger = logger;

        foreach (var picker in pickers)
        {
            picker.Session = session;
        }
    }

    public RecorderSession Session { get; }

    public IReadOnlyList<SearchPickerSurface> Pickers { get; }

    public SearchPickerSurface Primary => Pickers[0];

    public SearchPickerSurface Secondary => Pickers[1];

    public SearchPickerCaptureLogger Logger { get; }

    public static SearchPickerCaptureFixture CreateListBox(
        string logicalLocator = "CustomerPicker",
        string initialSearchText = "",
        bool detachResultsOnSelection = false,
        bool configured = true)
    {
        return Create(
            new SearchPickerDefinition(
                logicalLocator,
                SearchPickerResultsKind.ListBox,
                initialSearchText,
                detachResultsOnSelection,
                configured));
    }

    public static SearchPickerCaptureFixture CreateComboBox(
        string logicalLocator = "CustomerPicker",
        string initialSearchText = "")
    {
        return Create(
            new SearchPickerDefinition(
                logicalLocator,
                SearchPickerResultsKind.ComboBox,
                initialSearchText));
    }

    public static SearchPickerCaptureFixture CreateMultiple()
    {
        return Create(
            new SearchPickerDefinition("CustomerPicker", SearchPickerResultsKind.ListBox, "customer"),
            new SearchPickerDefinition("ProductPicker", SearchPickerResultsKind.ListBox, "product"));
    }

    public void Start() => Session.Start();

    public void CloseWithoutSelection(SearchPickerSurface picker)
    {
        Session.CaptureButtonClickForTesting(picker.OpenButton);
        _root.Children.Remove(picker.Results);
    }

    public void Dispose() => Session.Dispose();

    private static SearchPickerCaptureFixture Create(params SearchPickerDefinition[] definitions)
    {
        var logger = new SearchPickerCaptureLogger();
        var options = new AppAutomationRecorderOptions
        {
            ShowOverlay = false,
            Logger = logger,
            DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false },
            Validation = new RecorderValidationOptions { CaptureInvalidSteps = false }
        };
        var root = new StackPanel();
        var pickers = new List<SearchPickerSurface>();

        foreach (var definition in definitions)
        {
            var picker = CreatePicker(definition, options, root);
            pickers.Add(picker);
        }

        var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            () => root,
            attachWindowHandlers: false);
        session.AttachInputHandlersForTesting();

        return new SearchPickerCaptureFixture(session, root, pickers, logger);
    }

    private static SearchPickerSurface CreatePicker(
        SearchPickerDefinition definition,
        AppAutomationRecorderOptions options,
        StackPanel root)
    {
        var pickerRoot = WithAutomationId(new StackPanel(), definition.LogicalLocator);
        var searchInput = WithAutomationId(
            new TextBox { Text = definition.InitialSearchText },
            $"{definition.LogicalLocator}_Input");
        var openButton = WithAutomationId(
            new Button { Content = "Open" },
            $"{definition.LogicalLocator}_OpenButton");
        var results = CreateResults(definition);

        pickerRoot.Children.Add(searchInput);
        pickerRoot.Children.Add(openButton);
        root.Children.Add(pickerRoot);
        root.Children.Add(results);

        if (definition.DetachResultsOnSelection)
        {
            AttachSynchronousDetach(results, () => root.Children.Remove(results));
        }

        if (definition.Configured)
        {
            options.SearchPickerHints.Add(new RecorderSearchPickerHint(
                definition.LogicalLocator,
                SearchPickerParts.ByAutomationIds(
                    $"{definition.LogicalLocator}_Input",
                    $"{definition.LogicalLocator}_Results",
                    expandButtonAutomationId: $"{definition.LogicalLocator}_OpenButton",
                    resultsKind: definition.ResultsKind)));
        }

        return new SearchPickerSurface(searchInput, openButton, results);
    }

    private static Control CreateResults(SearchPickerDefinition definition)
    {
        return definition.ResultsKind switch
        {
            SearchPickerResultsKind.ListBox => WithAutomationId(
                new ListBox { ItemsSource = DefaultItems },
                $"{definition.LogicalLocator}_Results"),
            SearchPickerResultsKind.ComboBox => WithAutomationId(
                new ComboBox { ItemsSource = DefaultItems },
                $"{definition.LogicalLocator}_Results"),
            _ => throw new InvalidOperationException(
                $"Unsupported search picker results kind '{definition.ResultsKind}'.")
        };
    }

    private static void AttachSynchronousDetach(Control results, Action detach)
    {
        switch (results)
        {
            case ListBox listBox:
                listBox.SelectionChanged += (_, _) => detach();
                break;
            case ComboBox comboBox:
                comboBox.SelectionChanged += (_, _) => detach();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(results));
        }
    }

    private static TControl WithAutomationId<TControl>(TControl control, string automationId)
        where TControl : Control
    {
        AutomationProperties.SetAutomationId(control, automationId);
        return control;
    }

    private sealed record SearchPickerDefinition(
        string LogicalLocator,
        SearchPickerResultsKind ResultsKind,
        string InitialSearchText,
        bool DetachResultsOnSelection = false,
        bool Configured = true);
}

internal sealed class SearchPickerSurface
{
    public SearchPickerSurface(TextBox searchInput, Button openButton, Control results)
    {
        SearchInput = searchInput;
        OpenButton = openButton;
        Results = results;
    }

    internal RecorderSession Session { get; set; } = null!;

    public TextBox SearchInput { get; }

    public Button OpenButton { get; }

    public Control Results { get; }

    public void TypeSearch(string text)
    {
        Session.RegisterKeyboardInputForTesting(SearchInput);
        SearchInput.Text = text;
    }

    public void SelectByPointer(string item)
    {
        Session.RegisterPointerInputForTesting(Results);
        Select(item);
    }

    public void SelectByKeyboard(string item)
    {
        Session.RegisterKeyboardInputForTesting(Results);
        Select(item);
    }

    private void Select(string item)
    {
        switch (Results)
        {
            case ListBox listBox:
                listBox.SelectedItem = item;
                break;
            case ComboBox comboBox:
                comboBox.SelectedItem = item;
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported search picker results control '{Results.GetType().Name}'.");
        }
    }
}

internal sealed class SearchPickerCaptureLogger : ILogger
{
    public List<SearchPickerCaptureLogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new SearchPickerCaptureLogEntry(eventId, formatter(state, exception)));
    }
}

internal sealed record SearchPickerCaptureLogEntry(EventId EventId, string Message);
