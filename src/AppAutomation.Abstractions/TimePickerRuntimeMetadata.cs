using System.Globalization;
using System.Text.Json;

namespace AppAutomation.Abstractions;

/// <summary>A projection of the actual owner UI state, not a business-model identifier.</summary>
public sealed record TimePickerOwnerState(string OwnerId, long Epoch, bool IsOpen, bool IsReadOnly,
    int Clock, bool UseSeconds, int MinuteStep = 1, int SecondStep = 1)
{
    internal IReadOnlyList<(TimePickerComponentKind Kind, int Value)> GetComponentValues(TimeSpan value)
    {
        var hour = Clock == 12 ? (value.Hours % 12 == 0 ? 12 : value.Hours % 12) : value.Hours;
        var components = new List<(TimePickerComponentKind Kind, int Value)>
        {
            (TimePickerComponentKind.Hour, hour), (TimePickerComponentKind.Minute, value.Minutes)
        };
        if (UseSeconds) components.Add((TimePickerComponentKind.Second, value.Seconds));
        if (Clock == 12) components.Add((TimePickerComponentKind.Period, value.Hours >= 12 ? 1 : 0));
        return components;
    }

    internal (int Minimum, int Maximum, int Step) GetComponentRange(TimePickerComponentKind kind) => kind switch
    {
        TimePickerComponentKind.Hour => (Clock == 12 ? 1 : 0, Clock == 12 ? 12 : 23, 1),
        TimePickerComponentKind.Minute => (0, 59, MinuteStep),
        TimePickerComponentKind.Second => (0, 59, SecondStep),
        TimePickerComponentKind.Period => (0, 1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal void ValidateWrite(TimeSpan value, TimePickerParts parts)
    {
        ValidateValue(value, parts);
        if (IsReadOnly) throw new InvalidOperationException("The time editor is read-only.");
    }

    internal void ValidateValue(TimeSpan value, TimePickerParts parts)
    {
        TimePickerRuntimeMetadata.Validate(this);
        if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(value), "The presenter only accepts time within one day.");
        if (value.Ticks % (UseSeconds ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute) != 0
            || value.Minutes % MinuteStep != 0 || UseSeconds && value.Seconds % SecondStep != 0)
            throw new NotSupportedException("The requested time cannot be represented by the presenter's precision and increments.");
        if (parts.PresenterParts is not { } panels || UseSeconds && panels.Seconds is null || Clock == 12 && panels.Period is null)
            throw new NotSupportedException("Required presenter component parts are missing.");
    }
}

/// <summary>The owner and retained opening epoch of a real presenter.</summary>
public sealed record TimePickerPresenterState(string OwnerId, long Epoch);

/// <summary>A canonical projection of a real numeric picker panel.</summary>
public sealed record TimePickerComponentState(string OwnerId, long Epoch, TimePickerComponentKind Component,
    int Minimum, int Maximum, int Step, int SelectedValue);

/// <summary>
/// Encodes opt-in time-editor UI state in the real peer's ItemStatus. It does not set any UI or model value.
/// Keep the owner token for one control lifetime and retain the last epoch after closing the popup.
/// </summary>
public static class TimePickerRuntimeMetadata
{
    private const string Prefix = "AppAutomation.TimePicker/";
    private const string Version = Prefix + "1:";

    public static string ToAutomationValue(TimePickerOwnerState state)
    {
        Validate(state);
        return Version + JsonSerializer.Serialize(new { role = "root", ownerId = state.OwnerId, epoch = state.Epoch,
            isOpen = state.IsOpen, isReadOnly = state.IsReadOnly, clock = state.Clock, useSeconds = state.UseSeconds,
            minuteStep = state.MinuteStep, secondStep = state.SecondStep });
    }

    public static string ToAutomationValue(TimePickerPresenterState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateIdentity(state.OwnerId, state.Epoch);
        return Version + JsonSerializer.Serialize(new { role = "presenter", ownerId = state.OwnerId, epoch = state.Epoch });
    }

    public static string ToAutomationValue(TimePickerComponentState state)
    {
        Validate(state);
        return Version + JsonSerializer.Serialize(new { role = Role(state.Component), ownerId = state.OwnerId,
            epoch = state.Epoch, min = state.Minimum, max = state.Maximum, step = state.Step, selectedValue = state.SelectedValue });
    }

    internal static TimePickerOwnerState ReadOwner(string? value)
    {
        using var document = Read(value);
        var root = document.RootElement;
        RequireRole(root, "root");
        var state = new TimePickerOwnerState(Text(root, "ownerId"), root.GetProperty("epoch").GetInt64(),
            root.GetProperty("isOpen").GetBoolean(), root.GetProperty("isReadOnly").GetBoolean(),
            root.GetProperty("clock").GetInt32(), root.GetProperty("useSeconds").GetBoolean(),
            root.GetProperty("minuteStep").GetInt32(), root.GetProperty("secondStep").GetInt32());
        Validate(state);
        return state;
    }

    internal static TimePickerPresenterState ReadPresenter(string? value)
    {
        using var document = Read(value);
        var root = document.RootElement;
        RequireRole(root, "presenter");
        var state = new TimePickerPresenterState(Text(root, "ownerId"), root.GetProperty("epoch").GetInt64());
        ValidateIdentity(state.OwnerId, state.Epoch);
        return state;
    }

    internal static TimePickerComponentState ReadComponent(string? value, TimePickerComponentKind component)
    {
        using var document = Read(value);
        var root = document.RootElement;
        RequireRole(root, Role(component));
        var state = new TimePickerComponentState(Text(root, "ownerId"), root.GetProperty("epoch").GetInt64(), component,
            root.GetProperty("min").GetInt32(), root.GetProperty("max").GetInt32(), root.GetProperty("step").GetInt32(),
            root.GetProperty("selectedValue").GetInt32());
        Validate(state);
        return state;
    }

    internal static TimeSpan? ReadCommittedValue(string? value)
    {
        if (value is null or "") return null;
        return TimeSpan.TryParseExact(value, "c", CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw new InvalidDataException("The committed time value must use invariant TimeSpan 'c' format.");
    }

    internal static void RequireIdentity(string ownerId, long epoch, string actualOwner, long actualEpoch)
    {
        if (!string.Equals(ownerId, actualOwner, StringComparison.Ordinal) || epoch != actualEpoch)
            throw new InvalidOperationException("The time-picker part belongs to another owner or opening epoch.");
    }

    internal static void Validate(TimePickerOwnerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateIdentity(state.OwnerId, state.Epoch);
        if (state.Clock is not (12 or 24) || state.MinuteStep is < 1 or > 59 || state.SecondStep is < 1 or > 59)
            throw new InvalidDataException("Invalid time-picker clock or increments.");
    }

    internal static void Validate(TimePickerComponentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateIdentity(state.OwnerId, state.Epoch);
        if (!Enum.IsDefined(state.Component) || state.Minimum > state.Maximum || state.Step <= 0
            || state.Step > (long)state.Maximum - state.Minimum + 1
            || state.SelectedValue < state.Minimum || state.SelectedValue > state.Maximum
            || ((long)state.SelectedValue - state.Minimum) % state.Step != 0
            || state.Minimum < 0
            || state.Component == TimePickerComponentKind.Hour && state.Maximum > 23
            || state.Component is TimePickerComponentKind.Minute or TimePickerComponentKind.Second && state.Maximum > 59
            || state.Component == TimePickerComponentKind.Period && (state.Minimum != 0 || state.Maximum != 1 || state.Step != 1))
            throw new InvalidDataException("Invalid time-picker component range, increment or selected value.");
    }

    private static void ValidateIdentity(string ownerId, long epoch)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || epoch < 0)
            throw new InvalidDataException("A time-picker snapshot requires a nonempty UI owner token and a nonnegative retained epoch.");
    }

    private static JsonDocument Read(string? value)
    {
        if (value is null || !value.StartsWith(Version, StringComparison.Ordinal))
            throw new NotSupportedException("Missing or unsupported time-picker UIA metadata version.");
        var document = JsonDocument.Parse(value[Version.Length..]);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || document.RootElement.EnumerateObject().GroupBy(static property => property.Name, StringComparer.Ordinal).Any(static group => group.Count() > 1))
        {
            document.Dispose();
            throw new InvalidDataException("Invalid or duplicate time-picker metadata fields.");
        }
        return document;
    }

    private static string Text(JsonElement root, string name) => root.GetProperty(name).GetString()
        ?? throw new InvalidDataException($"Missing time-picker metadata '{name}'.");
    private static void RequireRole(JsonElement root, string expected)
    {
        if (Text(root, "role") != expected) throw new InvalidDataException($"Expected time-picker metadata role '{expected}'.");
    }
    private static string Role(TimePickerComponentKind component) => component switch
    {
        TimePickerComponentKind.Hour => "hour", TimePickerComponentKind.Minute => "minute",
        TimePickerComponentKind.Second => "second", TimePickerComponentKind.Period => "period",
        _ => throw new ArgumentOutOfRangeException(nameof(component))
    };
}
