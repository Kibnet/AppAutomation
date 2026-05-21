using System.Globalization;
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
    CaptureAssertExists = 8,
    // Kept so existing hotkey settings files with this key still deserialize.
    ToggleOverlayMinimize = 9
}

internal sealed class RecorderHotkeyMap
{
    private readonly Dictionary<RecorderCommandKind, RecorderShortcut> _shortcuts;

    private RecorderHotkeyMap(Dictionary<RecorderCommandKind, RecorderShortcut> shortcuts)
    {
        _shortcuts = shortcuts;
    }

    public IReadOnlyDictionary<RecorderCommandKind, RecorderShortcut> Shortcuts => _shortcuts;

    public static RecorderHotkeyMap Create(RecorderHotkeys hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        return Create(CreateGestureMap(hotkeys));
    }

    public static RecorderHotkeyMap Create(IReadOnlyDictionary<RecorderCommandKind, string?> gestures)
    {
        ArgumentNullException.ThrowIfNull(gestures);

        var shortcuts = new Dictionary<RecorderCommandKind, RecorderShortcut>();
        foreach (var command in EnumerateCommands())
        {
            if (gestures.TryGetValue(command, out var gesture))
            {
                Add(shortcuts, command, gesture);
            }
        }

        return new RecorderHotkeyMap(shortcuts);
    }

    public static IReadOnlyDictionary<RecorderCommandKind, string?> CreateGestureMap(RecorderHotkeys hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        return new Dictionary<RecorderCommandKind, string?>
        {
            [RecorderCommandKind.StartStop] = hotkeys.StartStop,
            [RecorderCommandKind.Save] = hotkeys.Save,
            [RecorderCommandKind.Export] = hotkeys.Export,
            [RecorderCommandKind.Clear] = hotkeys.Clear,
            [RecorderCommandKind.CaptureAssertAuto] = hotkeys.CaptureAssertAuto,
            [RecorderCommandKind.CaptureAssertText] = hotkeys.CaptureAssertText,
            [RecorderCommandKind.CaptureAssertEnabled] = hotkeys.CaptureAssertEnabled,
            [RecorderCommandKind.CaptureAssertChecked] = hotkeys.CaptureAssertChecked,
            [RecorderCommandKind.CaptureAssertExists] = hotkeys.CaptureAssertExists
        };
    }

    public static IReadOnlyList<RecorderCommandKind> EnumerateCommands() =>
        Enum.GetValues<RecorderCommandKind>()
            .Where(static command => command != RecorderCommandKind.ToggleOverlayMinimize)
            .OrderBy(static command => command)
            .ToArray();

    public bool TryGetCommand(Key key, KeyModifiers modifiers, out RecorderCommandKind command) =>
        TryGetCommand(key, physicalKey: PhysicalKey.None, modifiers, out command);

    public bool TryGetCommand(Key key, PhysicalKey physicalKey, KeyModifiers modifiers, out RecorderCommandKind command)
    {
        if (key is Key.System)
        {
            modifiers |= KeyModifiers.Alt;
        }

        var qwertyKey = physicalKey.ToQwertyKey();
        foreach (var entry in _shortcuts)
        {
            if (entry.Value.Matches(key, modifiers)
                || qwertyKey is not Key.None && entry.Value.Matches(qwertyKey, modifiers))
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

    public string GetDisplayText(RecorderCommandKind command) =>
        _shortcuts.TryGetValue(command, out var shortcut)
            ? shortcut.DisplayText
            : string.Empty;

    public static string DescribeCommand(RecorderCommandKind command) => Describe(command);

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
            RecorderCommandKind.CaptureAssertExists => "Assert Exists",
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

    public string NormalizedText
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(KeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(KeyModifiers.Shift))
            {
                parts.Add("Shift");
            }

            if (Modifiers.HasFlag(KeyModifiers.Alt))
            {
                parts.Add("Alt");
            }

            if (Modifiers.HasFlag(KeyModifiers.Meta))
            {
                parts.Add("Meta");
            }

            parts.Add(FormatKey(Key));
            return string.Join("+", parts);
        }
    }

    private static string FormatKey(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString(CultureInfo.InvariantCulture);
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return $"NumPad{(int)key - (int)Key.NumPad0}";
        }

        if (TryFormatSymbolKey(key, out var symbol))
        {
            return symbol;
        }

        return key.ToString();
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

        if (!TryParseKey(parts[^1], out var key, out var implicitModifiers))
        {
            return false;
        }

        modifiers |= implicitModifiers;
        shortcut = new RecorderShortcut(key, modifiers, NormalizedTextFor(key, modifiers));
        return true;
    }

    internal static bool TryCreateFromText(string? text, out RecorderShortcut shortcut)
    {
        shortcut = default;
        if (string.IsNullOrEmpty(text) || text.Length != 1)
        {
            return false;
        }

        return TryMapTextCharacter(text[0], out var key, out var modifiers)
            && Create(key, modifiers, out shortcut);
    }

    private static bool Create(Key key, KeyModifiers modifiers, out RecorderShortcut shortcut)
    {
        shortcut = new RecorderShortcut(key, modifiers, NormalizedTextFor(key, modifiers));
        return true;
    }

    private static bool TryParseKey(string text, out Key key, out KeyModifiers implicitModifiers)
    {
        implicitModifiers = KeyModifiers.None;
        if (text.Length == 1 && text[0] >= '0' && text[0] <= '9')
        {
            key = (Key)((int)Key.D0 + text[0] - '0');
            return true;
        }

        if (text.Length == 1 && TryMapTextCharacter(text[0], out key, out implicitModifiers))
        {
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out key);
    }

    private static string NormalizedTextFor(Key key, KeyModifiers modifiers) =>
        new RecorderShortcut(key, modifiers, DisplayText: string.Empty).NormalizedText;

    private static bool TryFormatSymbolKey(Key key, out string symbol)
    {
        symbol = key switch
        {
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemTilde => "`",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemQuotes => "'",
            _ => string.Empty
        };

        return symbol.Length > 0;
    }

    private static bool TryMapTextCharacter(char character, out Key key, out KeyModifiers modifiers)
    {
        modifiers = KeyModifiers.None;
        if (character >= 'a' && character <= 'z')
        {
            key = (Key)((int)Key.A + character - 'a');
            return true;
        }

        if (character >= 'A' && character <= 'Z')
        {
            key = (Key)((int)Key.A + character - 'A');
            return true;
        }

        if (character >= '0' && character <= '9')
        {
            key = (Key)((int)Key.D0 + character - '0');
            return true;
        }

        if (TryMapRussianCharacter(character, out key))
        {
            return true;
        }

        return TryMapSymbolCharacter(character, out key, out modifiers);
    }

    private static bool TryMapRussianCharacter(char character, out Key key)
    {
        key = char.ToLowerInvariant(character) switch
        {
            'й' => Key.Q,
            'ц' => Key.W,
            'у' => Key.E,
            'к' => Key.R,
            'е' => Key.T,
            'н' => Key.Y,
            'г' => Key.U,
            'ш' => Key.I,
            'щ' => Key.O,
            'з' => Key.P,
            'х' => Key.OemOpenBrackets,
            'ъ' => Key.OemCloseBrackets,
            'ф' => Key.A,
            'ы' => Key.S,
            'в' => Key.D,
            'а' => Key.F,
            'п' => Key.G,
            'р' => Key.H,
            'о' => Key.J,
            'л' => Key.K,
            'д' => Key.L,
            'ж' => Key.OemSemicolon,
            'э' => Key.OemQuotes,
            'я' => Key.Z,
            'ч' => Key.X,
            'с' => Key.C,
            'м' => Key.V,
            'и' => Key.B,
            'т' => Key.N,
            'ь' => Key.M,
            'б' => Key.OemComma,
            'ю' => Key.OemPeriod,
            'ё' => Key.OemTilde,
            _ => Key.None
        };

        return key is not Key.None;
    }

    private static bool TryMapSymbolCharacter(char character, out Key key, out KeyModifiers modifiers)
    {
        modifiers = KeyModifiers.None;
        (key, modifiers) = character switch
        {
            '-' => (Key.OemMinus, KeyModifiers.None),
            '_' => (Key.OemMinus, KeyModifiers.Shift),
            '=' => (Key.OemPlus, KeyModifiers.None),
            '+' => (Key.OemPlus, KeyModifiers.Shift),
            ',' => (Key.OemComma, KeyModifiers.None),
            '<' => (Key.OemComma, KeyModifiers.Shift),
            '.' => (Key.OemPeriod, KeyModifiers.None),
            '>' => (Key.OemPeriod, KeyModifiers.Shift),
            '/' => (Key.OemQuestion, KeyModifiers.None),
            '?' => (Key.OemQuestion, KeyModifiers.Shift),
            ';' => (Key.OemSemicolon, KeyModifiers.None),
            ':' => (Key.OemSemicolon, KeyModifiers.Shift),
            '`' => (Key.OemTilde, KeyModifiers.None),
            '~' => (Key.OemTilde, KeyModifiers.Shift),
            '[' => (Key.OemOpenBrackets, KeyModifiers.None),
            '{' => (Key.OemOpenBrackets, KeyModifiers.Shift),
            ']' => (Key.OemCloseBrackets, KeyModifiers.None),
            '}' => (Key.OemCloseBrackets, KeyModifiers.Shift),
            '\\' => (Key.OemPipe, KeyModifiers.None),
            '|' => (Key.OemPipe, KeyModifiers.Shift),
            '\'' => (Key.OemQuotes, KeyModifiers.None),
            '"' => (Key.OemQuotes, KeyModifiers.Shift),
            '!' => (Key.D1, KeyModifiers.Shift),
            '@' => (Key.D2, KeyModifiers.Shift),
            '#' => (Key.D3, KeyModifiers.Shift),
            '$' => (Key.D4, KeyModifiers.Shift),
            '%' => (Key.D5, KeyModifiers.Shift),
            '^' => (Key.D6, KeyModifiers.Shift),
            '&' => (Key.D7, KeyModifiers.Shift),
            '*' => (Key.D8, KeyModifiers.Shift),
            '(' => (Key.D9, KeyModifiers.Shift),
            ')' => (Key.D0, KeyModifiers.Shift),
            _ => (Key.None, KeyModifiers.None)
        };

        return key is not Key.None;
    }
}
