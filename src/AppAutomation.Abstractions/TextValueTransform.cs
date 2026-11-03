namespace AppAutomation.Abstractions;

public enum TextValueTransformKind { AfterFirstSeparator }

/// <summary>Декларативное преобразование прочитанного текста. Отсутствие дескриптора сохраняет текст.</summary>
public sealed record TextValueTransform(string Separator, bool TrimLeadingSpaces = true)
{
    public TextValueTransformKind Kind { get; init; }

    public string Apply(string? value) => Kind == TextValueTransformKind.AfterFirstSeparator
        ? TextValue.AfterFirstSeparator(value, Separator, TrimLeadingSpaces)
        : throw new InvalidOperationException($"Unsupported text transformation '{Kind}'.");
}
