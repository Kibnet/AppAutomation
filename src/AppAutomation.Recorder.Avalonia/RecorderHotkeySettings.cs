using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderHotkeySettings
{
    public Dictionary<RecorderCommandKind, string?> Gestures { get; init; } = new();

    public static RecorderHotkeySettings FromGestures(IReadOnlyDictionary<RecorderCommandKind, string?> gestures)
    {
        ArgumentNullException.ThrowIfNull(gestures);

        return new RecorderHotkeySettings
        {
            Gestures = gestures.ToDictionary(static entry => entry.Key, static entry => Normalize(entry.Value))
        };
    }

    public static RecorderHotkeySettings CreateEffective(
        RecorderHotkeys defaults,
        RecorderHotkeySettings? overrides)
    {
        var gestures = RecorderHotkeyMap.CreateGestureMap(defaults)
            .ToDictionary(static entry => entry.Key, static entry => Normalize(entry.Value));

        if (overrides?.Gestures is { } overrideGestures)
        {
            foreach (var entry in overrideGestures)
            {
                gestures[entry.Key] = Normalize(entry.Value);
            }
        }

        return FromGestures(gestures);
    }

    public RecorderHotkeySettings CreateOverridesAgainst(RecorderHotkeys defaults)
    {
        var defaultGestures = RecorderHotkeyMap.CreateGestureMap(defaults);
        var overrides = new Dictionary<RecorderCommandKind, string?>();
        foreach (var command in RecorderHotkeyMap.EnumerateCommands())
        {
            Gestures.TryGetValue(command, out var gesture);
            defaultGestures.TryGetValue(command, out var defaultGesture);
            if (!string.Equals(Normalize(gesture), Normalize(defaultGesture), StringComparison.Ordinal))
            {
                overrides[command] = Normalize(gesture);
            }
        }

        return FromGestures(overrides);
    }

    public RecorderHotkeyMap ToMap() => RecorderHotkeyMap.Create(Gestures);

    public RecorderHotkeyValidationResult Validate()
    {
        var parsed = new Dictionary<RecorderCommandKind, RecorderShortcut>();
        var errors = new List<string>();
        foreach (var command in RecorderHotkeyMap.EnumerateCommands())
        {
            Gestures.TryGetValue(command, out var gesture);
            if (string.IsNullOrWhiteSpace(gesture))
            {
                continue;
            }

            if (!RecorderShortcut.TryParse(gesture, out var shortcut))
            {
                errors.Add($"{RecorderHotkeyMap.DescribeCommand(command)} has invalid shortcut '{gesture}'.");
                continue;
            }

            parsed[command] = shortcut;
        }

        foreach (var group in parsed
                     .GroupBy(static entry => entry.Value.NormalizedText, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1))
        {
            var commands = string.Join(", ", group.Select(static entry => RecorderHotkeyMap.DescribeCommand(entry.Key)));
            errors.Add($"Shortcut '{group.Key}' is assigned to multiple commands: {commands}.");
        }

        return errors.Count == 0
            ? RecorderHotkeyValidationResult.Success
            : RecorderHotkeyValidationResult.Failure(errors);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

internal sealed class RecorderHotkeySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;

    public RecorderHotkeySettingsStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath) ? GetDefaultFilePath() : filePath;
    }

    public string FilePath => _filePath;

    public async Task<RecorderHotkeySettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<RecorderHotkeySettings>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryLoad(out RecorderHotkeySettings? settings, out string? error)
    {
        settings = null;
        error = null;
        if (!File.Exists(_filePath))
        {
            return true;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            settings = JsonSerializer.Deserialize<RecorderHotkeySettings>(json, JsonOptions);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            error = exception.Message;
            return false;
        }
    }

    public async Task SaveAsync(RecorderHotkeySettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public void Reset()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    public static string GetDefaultFilePath()
    {
        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(applicationData))
        {
            applicationData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(applicationData, "AppAutomation", "Recorder", "hotkeys.json");
    }
}

internal sealed record RecorderHotkeyValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static RecorderHotkeyValidationResult Success { get; } = new(true, Array.Empty<string>());

    public static RecorderHotkeyValidationResult Failure(IReadOnlyList<string> errors) => new(false, errors);

    public string ErrorMessage => string.Join(Environment.NewLine, Errors);
}
