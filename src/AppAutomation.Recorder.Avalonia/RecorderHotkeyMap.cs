using Avalonia.Input;

namespace AppAutomation.Recorder.Avalonia;

internal enum RecorderCommandKind
{
    StartStop = 0,
    Save = 1,
    Export = 2,
    Clear = 3,
    CaptureAssertAuto = 4,
    CaptureAssertText = 5,
    CaptureAssertEnabled = 6,
    CaptureAssertChecked = 7,
    ToggleOverlayMinimize = 8
}

internal sealed class RecorderHotkeyMap
{
    private readonly Dictionary<RecorderCommandKind, RecorderShortcut> _shortcuts;

    private RecorderHotkeyMap(Dictionary<RecorderCommandKind, RecorderShortcut> shortcuts)
    {
        _shortcuts = shortcuts;
    }

    public static RecorderHotkeyMap Create(RecorderHotkeys hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        var shortcuts = new Dictionary<RecorderCommandKind, RecorderShortcut>();
        Add(shortcuts, RecorderCommandKind.StartStop, hotkeys.StartStop);
        Add(shortcuts, RecorderCommandKind.Save, hotkeys.Save);
        Add(shortcuts, RecorderCommandKind.Export, hotkeys.Export);
        Add(shortcuts, RecorderCommandKind.Clear, hotkeys.Clear);
        Add(shortcuts, RecorderCommandKind.CaptureAssertAuto, hotkeys.CaptureAssertAuto);
        Add(shortcuts, RecorderCommandKind.CaptureAssertText, hotkeys.CaptureAssertText);
        Add(shortcuts, RecorderCommandKind.CaptureAssertEnabled, hotkeys.CaptureAssertEnabled);
        Add(shortcuts, RecorderCommandKind.CaptureAssertChecked, hotkeys.CaptureAssertChecked);
        Add(shortcuts, RecorderCommandKind.ToggleOverlayMinimize, hotkeys.ToggleOverlayMinimize);
        return new RecorderHotkeyMap(shortcuts);
    }

    public bool TryGetCommand(Key key, KeyModifiers modifiers, out RecorderCommandKind command)
    {
        foreach (var entry in _shortcuts)
        {
            if (entry.Value.Matches(key, modifiers))
            {
                command = entry.Key;
                return true;
            }
        }

        command = default;
        return false;
    }

    public string BuildLegend()
    {
        return string.Join(
            "  |  ",
            _shortcuts
                .OrderBy(static entry => entry.Key)
                .Select(static entry => $"{entry.Value.DisplayText}: {Describe(entry.Key)}"));
    }

    private static void Add(
        IDictionary<RecorderCommandKind, RecorderShortcut> shortcuts,
        RecorderCommandKind command,
        string? gesture)
    {
        if (RecorderShortcut.TryParse(gesture, out var shortcut))
        {
            shortcuts[command] = shortcut;
        }
    }

    private static string Describe(RecorderCommandKind command)
    {
        return command switch
        {
            RecorderCommandKind.StartStop => "Start/Stop",
            RecorderCommandKind.Save => "Save",
            RecorderCommandKind.Export => "Export",
            RecorderCommandKind.Clear => "Clear",
            RecorderCommandKind.CaptureAssertAuto => "Assert Auto",
            RecorderCommandKind.CaptureAssertText => "Assert Text",
            RecorderCommandKind.CaptureAssertEnabled => "Assert Enabled",
            RecorderCommandKind.CaptureAssertChecked => "Assert Checked",
            RecorderCommandKind.ToggleOverlayMinimize => "Overlay",
            _ => command.ToString()
        };
    }
}

internal readonly record struct RecorderShortcut(Key Key, KeyModifiers Modifiers, string DisplayText)
{
    public bool Matches(Key key, KeyModifiers modifiers)
    {
        return Key == key && Modifiers == modifiers;
    }

    public static bool TryParse(string? text, out RecorderShortcut shortcut)
    {
        shortcut = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var modifiers = KeyModifiers.None;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var token = parts[index];
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= KeyModifiers.Control;
                continue;
            }

            if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= KeyModifiers.Shift;
                continue;
            }

            if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= KeyModifiers.Alt;
                continue;
            }

            if (token.Equals("Meta", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Win", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= KeyModifiers.Meta;
                continue;
            }

            return false;
        }

        if (!Enum.TryParse<Key>(parts[^1], ignoreCase: true, out var key))
        {
            return false;
        }

        shortcut = new RecorderShortcut(key, modifiers, text.Trim());
        return true;
    }
}
