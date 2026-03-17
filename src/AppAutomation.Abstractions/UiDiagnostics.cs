namespace AppAutomation.Abstractions;

/// <summary>
/// Represents a diagnostic artifact collected during a UI operation failure.
/// </summary>
/// <remarks>
/// Artifacts can include screenshots, HTML snapshots, element trees, or other
/// diagnostic information that helps debug test failures.
/// </remarks>
/// <param name="Kind">The type of artifact (e.g., "screenshot", "element-tree", "html-snapshot").</param>
/// <param name="LogicalName">A human-readable name for the artifact.</param>
/// <param name="RelativePath">The relative path where the artifact is stored.</param>
/// <param name="ContentType">The MIME type of the artifact content.</param>
/// <param name="IsRequiredByContract">Indicates if this artifact type is required by the adapter contract.</param>
/// <param name="InlineTextPreview">Optional inline preview of text-based artifacts.</param>
public sealed record UiFailureArtifact(
    string Kind,
    string LogicalName,
    string RelativePath,
    string ContentType,
    bool IsRequiredByContract,
    string? InlineTextPreview = null);

/// <summary>
/// Contains detailed context about a UI operation failure for diagnostics.
/// </summary>
/// <remarks>
/// This record captures all relevant information about a failed UI operation,
/// including timing, locator details, expected vs. actual values, and collected artifacts.
/// It is attached to <see cref="UiOperationException"/> to provide rich debugging information.
/// </remarks>
/// <param name="OperationName">The name of the operation that failed (e.g., "EnterText", "ClickButton").</param>
/// <param name="AdapterId">The identifier of the automation adapter being used.</param>
/// <param name="Timeout">The timeout duration configured for the operation.</param>
/// <param name="StartedAtUtc">The UTC timestamp when the operation started.</param>
/// <param name="FinishedAtUtc">The UTC timestamp when the operation finished (or timed out).</param>
/// <param name="Capabilities">The runtime capabilities of the adapter.</param>
/// <param name="Artifacts">Collection of diagnostic artifacts collected during failure.</param>
/// <param name="PageTypeFullName">The fully qualified type name of the page object.</param>
/// <param name="ControlPropertyName">The name of the control property being accessed.</param>
/// <param name="LocatorValue">The locator value used to find the control.</param>
/// <param name="LocatorKind">The type of locator used.</param>
/// <param name="ExpectedValue">The expected value or state.</param>
/// <param name="LastObservedValue">The last actual value observed before failure.</param>
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

/// <summary>
/// Exception thrown when a UI automation operation fails.
/// </summary>
/// <remarks>
/// <para>
/// This exception provides rich diagnostic information through the <see cref="FailureContext"/>
/// property, including timing information, expected vs. actual values, and collected artifacts
/// such as screenshots.
/// </para>
/// <para>
/// Test frameworks can use the failure context to generate detailed test reports.
/// </para>
/// </remarks>
public sealed class UiOperationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UiOperationException"/> class.
    /// </summary>
    /// <param name="message">The error message describing the failure.</param>
    /// <param name="failureContext">The detailed failure context with diagnostic information.</param>
    /// <param name="innerException">The inner exception that caused this failure, if any.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failureContext"/> is <see langword="null"/>.</exception>
    public UiOperationException(string message, UiFailureContext failureContext, Exception? innerException = null)
        : base(message, innerException)
    {
        FailureContext = failureContext ?? throw new ArgumentNullException(nameof(failureContext));
    }

    /// <summary>
    /// Gets the detailed failure context with diagnostic information.
    /// </summary>
    /// <value>A <see cref="UiFailureContext"/> containing timing, locator, and artifact information.</value>
    public UiFailureContext FailureContext { get; }
}

/// <summary>
/// Interface for collecting diagnostic artifacts during UI operation failures.
/// </summary>
/// <remarks>
/// Implement this interface on a control resolver to enable automatic artifact collection
/// (e.g., screenshots, element trees) when operations fail.
/// </remarks>
public interface IUiArtifactCollector
{
    /// <summary>
    /// Collects diagnostic artifacts for a failed operation.
    /// </summary>
    /// <param name="failureContext">The context of the failed operation.</param>
    /// <param name="cancellationToken">Token to cancel artifact collection.</param>
    /// <returns>A collection of artifacts, or an empty collection if none could be collected.</returns>
    ValueTask<IReadOnlyList<UiFailureArtifact>> CollectAsync(
        UiFailureContext failureContext,
        CancellationToken cancellationToken = default);
}
