namespace AppAutomation.Abstractions;

public sealed record UiFailureArtifact(
    string Kind,
    string LogicalName,
    string RelativePath,
    string ContentType,
    bool IsRequiredByContract,
    string? InlineTextPreview = null);

public sealed record UiFailureContext(
    string OperationName,
    string AdapterId,
    TimeSpan Timeout,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    UiRuntimeCapabilities Capabilities,
    IReadOnlyList<UiFailureArtifact> Artifacts,
    string? PageTypeFullName = null,
    string? ControlPropertyName = null,
    string? LocatorValue = null,
    UiLocatorKind? LocatorKind = null,
    string? ExpectedValue = null,
    string? LastObservedValue = null);

public sealed class UiOperationException : Exception
{
    public UiOperationException(string message, UiFailureContext failureContext, Exception? innerException = null)
        : base(message, innerException)
    {
        FailureContext = failureContext ?? throw new ArgumentNullException(nameof(failureContext));
    }

    public UiFailureContext FailureContext { get; }
}

public interface IUiArtifactCollector
{
    ValueTask<IReadOnlyList<UiFailureArtifact>> CollectAsync(
        UiFailureContext failureContext,
        CancellationToken cancellationToken = default);
}
