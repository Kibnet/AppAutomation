using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal sealed class ColorPickerCaptureFixture : IDisposable
{
    private static readonly string[] PaletteColors = ["#FF336699", "#80224466"];
    private readonly RecorderColorPickerSelectionSource _selectionSource;

    private ColorPickerCaptureFixture(
        RecorderSession session,
        StackPanel pickerRoot,
        TextBox currentValue,
        TextBox customValue,
        SelectingItemsControl palette,
        Border popup,
        Button confirm,
        Button cancel,
        RecorderColorPickerSelectionSource selectionSource)
    {
        Session = session;
        PickerRoot = pickerRoot;
        CurrentValue = currentValue;
        CustomValue = customValue;
        Palette = palette;
        Popup = popup;
        Confirm = confirm;
        Cancel = cancel;
        _selectionSource = selectionSource;
    }

    public RecorderSession Session { get; }

    public StackPanel PickerRoot { get; }

    public TextBox CurrentValue { get; }

    public TextBox CustomValue { get; }

    public SelectingItemsControl Palette { get; }

    public Border Popup { get; }

    public Button Confirm { get; }

    public Button Cancel { get; }

    public RecorderStepJournalEntry OnlyStep => Session.StepJournal.Single();

    public static ColorPickerCaptureFixture Create(
        ColorPickerCommitMode commitMode = ColorPickerCommitMode.Confirm,
        ColorPaletteKind paletteKind = ColorPaletteKind.ListBox)
    {
        var root = new StackPanel();
        var pickerRoot = WithId(new StackPanel(), "AccentColor");
        var current = WithId(new TextBox { Text = "#FF000000" }, "AccentColorValue");
        var custom = WithId(new TextBox(), "AccentColorCustom");
        var palette = paletteKind == ColorPaletteKind.ComboBox
            ? (SelectingItemsControl)new ComboBox { ItemsSource = PaletteColors }
            : new ListBox { ItemsSource = PaletteColors };
        WithId(palette, "AccentColorPalette");
        var confirm = WithId(new Button { Content = "Apply" }, "AccentColorConfirm");
        var cancel = WithId(new Button { Content = "Cancel" }, "AccentColorCancel");
        var popup = WithId(new Border
        {
            Child = new StackPanel
            {
                Children = { custom, palette, confirm, cancel }
            }
        }, "AccentColorPopup");
        pickerRoot.Children.Add(current);
        pickerRoot.Children.Add(popup);
        root.Children.Add(pickerRoot);

        var selectionSource = new RecorderColorPickerSelectionSource();
        var options = new AppAutomationRecorderOptions
        {
            ShowOverlay = false,
            DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false }
        };
        options.ColorPickerHints.Add(new RecorderColorPickerHint(
            "AccentColor",
            ColorPickerParts.ByAutomationIds(
                "AccentColor",
                "AccentColorValue",
                popupRootAutomationId: "AccentColorPopup",
                paletteAutomationId: "AccentColorPalette",
                customValueAutomationId: "AccentColorCustom",
                confirmButtonAutomationId: "AccentColorConfirm",
                cancelButtonAutomationId: "AccentColorCancel",
                paletteKind: paletteKind,
                commitMode: commitMode)));
        options.ColorPickerSelectionSources.Add(selectionSource);

        var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            () => root,
            attachWindowHandlers: false);
        session.AttachInputHandlersForTesting();
        return new ColorPickerCaptureFixture(
            session,
            pickerRoot,
            current,
            custom,
            palette,
            popup,
            confirm,
            cancel,
            selectionSource);
    }

    public static ColorPickerCaptureFixture CreateInvalidSemanticPalette(RecorderCaptureTestLogger logger)
    {
        var root = new StackPanel();
        var palette = WithId(new ListBox { ItemsSource = new[] { "Item 42" } }, "AccentColorPalette");
        root.Children.Add(palette);
        var options = new AppAutomationRecorderOptions
        {
            ShowOverlay = false,
            Logger = logger,
            DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false }
        };
        options.ColorPickerHints.Add(new RecorderColorPickerHint(
            "AccentColor",
            ColorPickerParts.ByAutomationIds(
                "AccentColor",
                "AccentColorValue",
                paletteAutomationId: "AccentColorPalette",
                paletteKind: ColorPaletteKind.ListBox,
                commitMode: ColorPickerCommitMode.Immediate)));
        var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            () => root,
            attachWindowHandlers: false);
        session.AttachInputHandlersForTesting();
        return new ColorPickerCaptureFixture(
            session,
            root,
            WithId(new TextBox(), "AccentColorValue"),
            WithId(new TextBox(), "AccentColorCustom"),
            palette,
            WithId(new Border(), "AccentColorPopup"),
            WithId(new Button(), "AccentColorConfirm"),
            WithId(new Button(), "AccentColorCancel"),
            new RecorderColorPickerSelectionSource());
    }

    public void Start() => Session.Start();

    public void SelectPalette(string color, bool keyboard = false)
    {
        if (keyboard)
        {
            Session.RegisterKeyboardInputForTesting(Palette);
        }
        else
        {
            Session.RegisterPointerInputForTesting(Palette);
        }

        Palette.SelectedItem = color;
    }

    public void EnterCustomValue(string color)
    {
        Session.RegisterKeyboardInputForTesting(CustomValue);
        CustomValue.Text = color;
    }

    public void Apply() => Session.CaptureButtonClickForTesting(Confirm);

    public void Dismiss() => Session.CaptureButtonClickForTesting(Cancel);

    public void ConfirmCustomSelection(string color)
    {
        _selectionSource.ConfirmSelection(PickerRoot, color);
    }

    public void DetachPopup() => PickerRoot.Children.Remove(Popup);

    public void CaptureAssertion() =>
        Session.CaptureAssertionForTesting(PickerRoot, RecorderAssertionMode.Text);

    public void Dispose() => Session.Dispose();

    private static T WithId<T>(T control, string automationId)
        where T : Control
    {
        AutomationProperties.SetAutomationId(control, automationId);
        return control;
    }
}
