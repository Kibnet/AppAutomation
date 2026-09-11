namespace AppAutomation.Abstractions;

/// <summary>Distinguishes temporary absence from an ambiguous control configuration.</summary>
public enum UiControlResolutionFailure
{
    NotFound,
    Detached,
    Ambiguous,
    TypeMismatch
}
