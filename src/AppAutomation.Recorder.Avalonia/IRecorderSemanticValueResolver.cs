using AppAutomation.Abstractions;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

public interface IRecorderSemanticValueResolver
{
    /// <summary>
    /// Resolves a visual control to one logical semantic-value target.
    /// </summary>
    /// <remarks>
    /// Return <see cref="RecorderSemanticValueResolution.NotHandled"/> when the resolver does not own
    /// the control, <see cref="RecorderSemanticValueResolution.Resolved"/> for a valid target, or
    /// <see cref="RecorderSemanticValueResolution.Failed"/> when the control is recognized but its
    /// configuration is invalid. A failed result prevents primitive fallback.
    /// </remarks>
    RecorderSemanticValueResolution Resolve(Control source);
}

public enum RecorderSemanticValueResolutionKind
{
    NotHandled = 0,
    Resolved = 1,
    Failed = 2
}

public sealed record RecorderSemanticValueResolution
{
    private RecorderSemanticValueResolution(
        RecorderSemanticValueResolutionKind kind,
        RecorderSemanticValueTarget? target,
        string? errorMessage)
    {
        Kind = kind;
        Target = target;
        ErrorMessage = errorMessage;
    }

    public RecorderSemanticValueResolutionKind Kind { get; }

    public RecorderSemanticValueTarget? Target { get; }

    public string? ErrorMessage { get; }

    public static RecorderSemanticValueResolution NotHandled { get; } =
        new(RecorderSemanticValueResolutionKind.NotHandled, null, null);

    public static RecorderSemanticValueResolution Resolved(RecorderSemanticValueTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new RecorderSemanticValueResolution(
            RecorderSemanticValueResolutionKind.Resolved,
            target,
            null);
    }

    public static RecorderSemanticValueResolution Failed(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new RecorderSemanticValueResolution(
            RecorderSemanticValueResolutionKind.Failed,
            null,
            errorMessage.Trim());
    }
}

public sealed record RecorderSemanticValueTarget(
    string LocatorValue,
    UiControlType ControlType,
    RecorderValueKind ValueKind,
    RecorderValueAccessorKind ValueAccessorKind,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = false)
{
    public string? StringValue { get; init; }

    public bool? BoolValue { get; init; }

    public double? DoubleValue { get; init; }

    public DateTime? DateValue { get; init; }

    public TimeSpan? TimeValue { get; init; }

    public IReadOnlyList<string>? StringValues { get; init; }

    public RecorderSemanticGridValueTarget? GridContext { get; init; }
}

public sealed record RecorderSemanticGridValueTarget(
    IReadOnlyList<RecorderSemanticGridRowCondition> RowConditions,
    string TargetColumnName);

public sealed record RecorderSemanticGridRowCondition(string ColumnName, string Value);
