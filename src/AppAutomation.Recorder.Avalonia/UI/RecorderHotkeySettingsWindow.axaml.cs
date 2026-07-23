using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace AppAutomation.Recorder.Avalonia.UI;

internal sealed partial class RecorderHotkeySettingsWindow : Window
{
    private readonly RecorderHotkeys _defaults;
    private readonly Dictionary<RecorderCommandKind, TextBox> _editors = new();
    private StackPanel? _hotkeyRowsPanel;
    private TextBlock? _errorText;
    private Button? _saveButton;
    private Button? _cancelButton;
    private Button? _resetButton;
    private TextBox? _activeEditor;

    public RecorderHotkeySettingsWindow(RecorderHotkeySettings currentSettings, RecorderHotkeys defaults)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        ArgumentNullException.ThrowIfNull(defaults);

        _defaults = defaults;
        AvaloniaXamlLoader.Load(this);
        ApplyThemeResources(RecorderOverlay.ResolveOverlayTheme(null));
        InitializeControls();
        RenderRows(currentSettings);
    }

    public RecorderHotkeySettingsWindowResult? Result { get; private set; }

    private void InitializeControls()
    {
        _hotkeyRowsPanel = this.FindControl<StackPanel>("HotkeyRowsPanel");
        _errorText = this.FindControl<TextBlock>("ErrorText");
        _saveButton = this.FindControl<Button>("SaveButton");
        _cancelButton = this.FindControl<Button>("CancelButton");
        _resetButton = this.FindControl<Button>("ResetButton");

        if (_saveButton is not null)
        {
            _saveButton.Click += OnSaveClick;
        }

        if (_cancelButton is not null)
        {
            _cancelButton.Click += (_, _) => Close(null);
        }

        if (_resetButton is not null)
        {
            _resetButton.Click += OnResetClick;
        }

        AddHandler(
            InputElement.KeyDownEvent,
            OnWindowShortcutKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.TextInputEvent,
            OnWindowShortcutTextInput,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void RenderRows(RecorderHotkeySettings currentSettings)
    {
        if (_hotkeyRowsPanel is null)
        {
            return;
        }

        _hotkeyRowsPanel.Children.Clear();
        _editors.Clear();
        foreach (var command in RecorderHotkeyMap.EnumerateCommands())
        {
            currentSettings.Gestures.TryGetValue(command, out var gesture);
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("190,*"),
                ColumnSpacing = 10
            };

            var label = new TextBlock
            {
                Text = RecorderHotkeyMap.DescribeCommand(command),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = GetBrush("RecorderText")
            };
            var editor = new TextBox
            {
                Text = gesture ?? string.Empty,
                PlaceholderText = "Press shortcut",
                Tag = command
            };
            editor.AddHandler(
                InputElement.KeyDownEvent,
                OnShortcutEditorKeyDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            editor.AddHandler(
                InputElement.TextInputEvent,
                OnShortcutEditorTextInput,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            editor.GotFocus += (_, _) => _activeEditor = editor;
            editor.LostFocus += (_, _) =>
            {
                if (ReferenceEquals(_activeEditor, editor))
                {
                    _activeEditor = null;
                }
            };

            Grid.SetColumn(editor, 1);
            row.Children.Add(label);
            row.Children.Add(editor);
            _hotkeyRowsPanel.Children.Add(row);
            _editors[command] = editor;
        }
    }

    internal static bool TryCaptureShortcut(
        Key key,
        KeyModifiers modifiers,
        out string? gesture)
    {
        return TryCaptureShortcut(key, PhysicalKey.None, modifiers, out gesture);
    }

    internal static bool TryCaptureShortcut(
        Key key,
        PhysicalKey physicalKey,
        KeyModifiers modifiers,
        out string? gesture)
    {
        gesture = null;
        if (key is Key.System)
        {
            modifiers |= KeyModifiers.Alt;
        }

        var shortcutKey = ResolveShortcutKey(key, physicalKey);
        if (shortcutKey is Key.None)
        {
            return false;
        }

        if (shortcutKey is Key.Back or Key.Delete && modifiers == KeyModifiers.None)
        {
            return true;
        }

        if (IsModifierKey(shortcutKey))
        {
            return false;
        }

        var shortcut = new RecorderShortcut(shortcutKey, modifiers, DisplayText: string.Empty);
        gesture = shortcut.NormalizedText;
        return true;
    }

    private static Key ResolveShortcutKey(Key key, PhysicalKey physicalKey)
    {
        var qwertyKey = physicalKey.ToQwertyKey();
        if (qwertyKey is not Key.None && (key is Key.None || IsTextShortcutKey(qwertyKey)))
        {
            return qwertyKey;
        }

        if (key is Key.System)
        {
            return Key.None;
        }

        return key;
    }

    private static bool IsTextShortcutKey(Key key) =>
        key is >= Key.A and <= Key.Z
            or >= Key.D0 and <= Key.D9
            or >= Key.NumPad0 and <= Key.NumPad9;

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftShift
            or Key.RightShift
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LWin
            or Key.RWin;
    }

    private void OnShortcutEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox editor)
        {
            return;
        }

        CaptureShortcut(editor, e);
    }

    private void OnShortcutEditorTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is not TextBox editor)
        {
            return;
        }

        CaptureTextShortcut(editor, e);
    }

    private void OnWindowShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        var editor = FindActiveEditor();
        if (editor is null)
        {
            return;
        }

        CaptureShortcut(editor, e);
    }

    private void OnWindowShortcutTextInput(object? sender, TextInputEventArgs e)
    {
        var editor = FindActiveEditor();
        if (editor is null)
        {
            return;
        }

        CaptureTextShortcut(editor, e);
    }

    private void CaptureShortcut(TextBox editor, KeyEventArgs e)
    {
        if (!TryCaptureShortcut(e.Key, e.PhysicalKey, e.KeyModifiers, out var gesture))
        {
            return;
        }

        e.Handled = true;
        editor.Text = gesture ?? string.Empty;
        editor.CaretIndex = editor.Text.Length;
        if (_errorText is not null)
        {
            _errorText.Text = string.Empty;
        }
    }

    private void CaptureTextShortcut(TextBox editor, TextInputEventArgs e)
    {
        if (!RecorderShortcut.TryCreateFromText(e.Text, out var shortcut))
        {
            return;
        }

        e.Handled = true;
        editor.Text = shortcut.NormalizedText;
        editor.CaretIndex = editor.Text.Length;
        if (_errorText is not null)
        {
            _errorText.Text = string.Empty;
        }
    }

    private TextBox? FindActiveEditor()
    {
        if (_activeEditor is not null && _editors.ContainsValue(_activeEditor))
        {
            return _activeEditor;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused is TextBox focusedEditor && _editors.ContainsValue(focusedEditor)
            ? focusedEditor
            : null;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var settings = ReadSettingsFromEditors();
        var validation = settings.Validate();
        if (!validation.IsValid)
        {
            ShowError(validation.ErrorMessage);
            return;
        }

        Result = new RecorderHotkeySettingsWindowResult(settings, ResetToDefaults: false);
        Close(Result);
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        var settings = RecorderHotkeySettings.CreateEffective(_defaults, overrides: null);
        Result = new RecorderHotkeySettingsWindowResult(settings, ResetToDefaults: true);
        Close(Result);
    }

    private RecorderHotkeySettings ReadSettingsFromEditors()
    {
        var gestures = new Dictionary<RecorderCommandKind, string?>();
        foreach (var entry in _editors)
        {
            gestures[entry.Key] = entry.Value.Text;
        }

        return RecorderHotkeySettings.FromGestures(gestures);
    }

    private void ShowError(string message)
    {
        if (_errorText is not null)
        {
            _errorText.Text = message;
        }
    }

    private IBrush GetBrush(string key)
    {
        return this.TryFindResource(key, out var value) && value is IBrush brush
            ? brush
            : Brushes.Gray;
    }

    private void ApplyThemeResources(RecorderOverlayTheme theme)
    {
        var palette = RecorderOverlay.GetPalette(theme);
        Resources["RecorderOverlayBackground"] = new SolidColorBrush(palette.OverlayBackground);
        Resources["RecorderText"] = new SolidColorBrush(palette.Text);
        Resources["RecorderDanger"] = new SolidColorBrush(palette.Danger);
        RequestedThemeVariant = theme == RecorderOverlayTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}

internal sealed record RecorderHotkeySettingsWindowResult(
    RecorderHotkeySettings Settings,
    bool ResetToDefaults);
