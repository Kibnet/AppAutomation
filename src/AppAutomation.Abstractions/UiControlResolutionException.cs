namespace AppAutomation.Abstractions;

/// <summary>A provider's classified failure to resolve a control.</summary>
public sealed class UiControlResolutionException : InvalidOperationException
{
    public UiControlResolutionException(
        UiControlResolutionFailure failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public UiControlResolutionFailure Failure { get; }

    /// <summary>Only missing or detached controls can become resolvable without changing configuration.</summary>
    public bool IsTransient => Failure is UiControlResolutionFailure.NotFound or UiControlResolutionFailure.Detached;
}
