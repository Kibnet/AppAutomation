using AppAutomation.Abstractions;

namespace AppAutomation.Abstractions.Tests;

public sealed class RecordedValueGeneratorTests
{
    [Test]
    public async Task Series_UsesOneTimestampAndOneBasedOrdinals()
    {
        var series = RecordedValueGenerator.Start();

        var first = series.Create(1);
        var second = series.Create(2);

        await Assert.That(first).Matches("^Recorded_[0-9]{8}_[0-9]{9}_1$");
        await Assert.That(second).IsEqualTo(first[..^1] + "2");
    }

    [Test]
    public async Task Start_CreatesDifferentSeriesForConcurrentInvocations()
    {
        var values = await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => Task.Run(() => RecordedValueGenerator.Start().Create(1))));

        await Assert.That(values.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(values.Length);
    }

    [Test]
    public async Task Create_RejectsNonPositiveOrdinal()
    {
        var series = RecordedValueGenerator.Start();

        await Assert.That(() => series.Create(0)).Throws<ArgumentOutOfRangeException>();
    }
}
