using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal sealed class CustomSearchPickerCaptureFixture : IDisposable
{
    private readonly StackPanel _root;

    private CustomSearchPickerCaptureFixture(
        RecorderSession session,
        StackPanel root,
        IReadOnlyList<CustomSearchPickerSurface> pickers,
        SearchPickerCaptureLogger logger)
    {
        Session = session;
        _root = root;
        Pickers = pickers;
        Logger = logger;
    }

    public RecorderSession Session { get; }

    public IReadOnlyList<CustomSearchPickerSurface> Pickers { get; }

    public CustomSearchPickerSurface Primary => Pickers[0];

    public CustomSearchPickerSurface Secondary => Pickers[1];

    public SearchPickerCaptureLogger Logger { get; }

    public static CustomSearchPickerCaptureFixture Create(
        bool configured = true,
        bool duplicateHint = false)
    {
        return Create(
            new CustomSearchPickerDefinition("LocationPicker", configured, duplicateHint));
    }

    public static CustomSearchPickerCaptureFixture CreateMultiple()
    {
        return Create(
            new CustomSearchPickerDefinition("LocationPicker"),
            new CustomSearchPickerDefinition("SecondaryPicker"));
    }

    public void Start() => Session.Start();

    public void Stop() => Session.Stop();

    public void DetachResults(CustomSearchPickerSurface picker)
    {
        _root.Children.Remove(picker.ResultsRoot);
    }

    public void Dispose() => Session.Dispose();

    private static CustomSearchPickerCaptureFixture Create(
        params CustomSearchPickerDefinition[] definitions)
    {
        var logger = new SearchPickerCaptureLogger();
        var selectionSource = new RecorderSearchPickerSelectionSource();
        var options = new AppAutomationRecorderOptions
        {
            ShowOverlay = false,
            Logger = logger,
            DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false },
            Validation = new RecorderValidationOptions { CaptureInvalidSteps = false }
        };
        options.SearchPickerSelectionSources.Add(selectionSource);

        var root = new StackPanel();
        var pickers = definitions
            .Select(definition => CreatePicker(definition, options, selectionSource, root))
            .ToArray();
        var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            () => root,
            attachWindowHandlers: false);
        session.AttachInputHandlersForTesting();

        foreach (var picker in pickers)
        {
            picker.Session = session;
        }

        return new CustomSearchPickerCaptureFixture(session, root, pickers, logger);
    }

    private static CustomSearchPickerSurface CreatePicker(
        CustomSearchPickerDefinition definition,
        AppAutomationRecorderOptions options,
        RecorderSearchPickerSelectionSource selectionSource,
        StackPanel root)
    {
        var pickerRoot = WithAutomationId(new StackPanel(), definition.LogicalLocator);
        var searchInput = WithAutomationId(
            new TextBox(),
            $"{definition.LogicalLocator}_Input");
        var openButton = WithAutomationId(
            new Button { Content = "Open" },
            $"{definition.LogicalLocator}_OpenButton");
        var resultsRoot = WithAutomationId(
            new Border { Child = new TextBlock { Text = "Search result" } },
            $"{definition.LogicalLocator}_Results");

        pickerRoot.Children.Add(searchInput);
        pickerRoot.Children.Add(openButton);
        root.Children.Add(pickerRoot);
        root.Children.Add(resultsRoot);

        if (definition.Configured)
        {
            AddHint(options, definition.LogicalLocator);
            if (definition.DuplicateHint)
            {
                AddHint(options, definition.LogicalLocator);
            }
        }

        return new CustomSearchPickerSurface(
            searchInput,
            openButton,
            resultsRoot,
            selectionSource);
    }

    private static void AddHint(AppAutomationRecorderOptions options, string logicalLocator)
    {
        options.SearchPickerHints.Add(new RecorderSearchPickerHint(
            logicalLocator,
            SearchPickerParts.ByAutomationIds(
                $"{logicalLocator}_Input",
                $"{logicalLocator}_Results",
                expandButtonAutomationId: $"{logicalLocator}_OpenButton",
                resultsKind: SearchPickerResultsKind.ListBox)));
    }

    private static TControl WithAutomationId<TControl>(TControl control, string automationId)
        where TControl : Control
    {
        AutomationProperties.SetAutomationId(control, automationId);
        return control;
    }

    private sealed record CustomSearchPickerDefinition(
        string LogicalLocator,
        bool Configured = true,
        bool DuplicateHint = false);
}

internal sealed class CustomSearchPickerSurface
{
    private readonly RecorderSearchPickerSelectionSource _selectionSource;

    public CustomSearchPickerSurface(
        TextBox searchInput,
        Button openButton,
        Control resultsRoot,
        RecorderSearchPickerSelectionSource selectionSource)
    {
        SearchInput = searchInput;
        OpenButton = openButton;
        ResultsRoot = resultsRoot;
        _selectionSource = selectionSource;
    }

    internal RecorderSession Session { get; set; } = null!;

    public TextBox SearchInput { get; }

    public Button OpenButton { get; }

    public Control ResultsRoot { get; }

    public void TypeSearch(string text)
    {
        Session.RegisterKeyboardInputForTesting(SearchInput);
        SearchInput.Text = text;
    }

    public void ConfirmSelection(string selectedValue)
    {
        _selectionSource.ConfirmSelection(SearchInput, ResultsRoot, selectedValue);
    }
}
