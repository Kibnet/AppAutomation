using AppAutomation.Abstractions;

namespace AppAutomation.Recorder.Avalonia;

internal enum RecorderGeneratedValueKind
{
    Text = 0,
    OrganizationInn = 1,
    RussianPhoneNumber = 3,
    Email = 4
}

internal sealed record RecorderGeneratedValueFormat(
    string Label,
    string VariableName,
    string FactoryMethod,
    int MaximumOrdinal,
    Func<RecordedValueSeries, int, string> Create);

internal static class RecorderGeneratedValueFormats
{
    private static readonly Dictionary<RecorderGeneratedValueKind, RecorderGeneratedValueFormat> Formats = new()
    {
        [RecorderGeneratedValueKind.Text] = new("Text", "generatedValue", nameof(RecordedValueSeries.Create), int.MaxValue,
            static (series, ordinal) => series.Create(ordinal)),
        [RecorderGeneratedValueKind.OrganizationInn] = new("INN", "generatedInn", nameof(RecordedValueSeries.CreateOrganizationInn), 100_000,
            static (series, ordinal) => series.CreateOrganizationInn(ordinal)),
        [RecorderGeneratedValueKind.RussianPhoneNumber] = new("Russian phone", "generatedPhone", nameof(RecordedValueSeries.CreateRussianPhoneNumber), 10_000_000,
            static (series, ordinal) => series.CreateRussianPhoneNumber(ordinal)),
        [RecorderGeneratedValueKind.Email] = new("Email", "generatedEmail", nameof(RecordedValueSeries.CreateEmail), int.MaxValue,
            static (series, ordinal) => series.CreateEmail(ordinal))
    };

    public static RecorderGeneratedValueFormat Get(RecorderGeneratedValueKind kind) =>
        TryGet(kind, out var format) ? format : throw new ArgumentOutOfRangeException(nameof(kind));

    public static bool TryGet(RecorderGeneratedValueKind kind, out RecorderGeneratedValueFormat format) =>
        Formats.TryGetValue(kind, out format!);
}
