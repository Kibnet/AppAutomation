using System.Globalization;

namespace AppAutomation.Abstractions;

/// <summary>
/// Represents a validated physical text representation of a canonical numeric grid value.
/// </summary>
public sealed record GridNumericInput
{
    public GridNumericInput(double canonicalValue, string text, string? cultureName = null)
    {
        if (!double.IsFinite(canonicalValue))
        {
            throw new ArgumentOutOfRangeException(nameof(canonicalValue), canonicalValue, "Numeric grid value must be finite.");
        }

        GridNumericText.ValidateCultureName(cultureName, nameof(cultureName));
        GridNumericText.ValidateInput(canonicalValue, text, nameof(text), cultureName);
        CanonicalValue = canonicalValue;
        Text = text;
        CultureName = cultureName;
    }

    /// <summary>The canonical numeric value represented by <see cref="Text"/>.</summary>
    public double CanonicalValue { get; }

    /// <summary>The text that should be entered through the physical editor.</summary>
    public string Text { get; }

    /// <summary>
    /// The culture used by <see cref="Text"/>. <see langword="null"/> means that the text must be
    /// resolved unambiguously; an empty string explicitly means invariant culture.
    /// </summary>
    public string? CultureName { get; }
}
