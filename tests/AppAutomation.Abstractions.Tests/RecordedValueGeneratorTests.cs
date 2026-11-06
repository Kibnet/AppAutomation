using AppAutomation.Abstractions;
using System.Globalization;
using System.Net.Mail;

namespace AppAutomation.Abstractions.Tests;

public sealed class RecordedValueGeneratorTests
{
    [Test]
    [Arguments("organization", 100_000, "ru-RU")]
    [Arguments("phone", 10_000_000, "ru-RU")]
    [Arguments("email", int.MaxValue, "ru-RU")]
    [Arguments("organization", 100_000, "ar-SA")]
    [Arguments("phone", 10_000_000, "ar-SA")]
    [Arguments("email", int.MaxValue, "ar-SA")]
    public async Task StructuredValuesRemainStableValidAndBounded(string format, int maximum, string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var series = RecordedValueGenerator.Start();
            string Create(int ordinal) => format switch
            {
                "organization" => series.CreateOrganizationInn(ordinal),
                "phone" => series.CreateRussianPhoneNumber(ordinal),
                _ => series.CreateEmail(ordinal)
            };
            var values = Enumerable.Range(1, 30).Select(Create).ToArray();
            await Assert.That(values.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(values.Length);
            await Assert.That(Create(1)).IsEqualTo(values[0]);
            await Assert.That(Create(maximum)).IsNotEmpty();
            await Assert.That(() => Create(0)).Throws<ArgumentOutOfRangeException>();
            await Assert.That(() => Create(-1)).Throws<ArgumentOutOfRangeException>();
            if (maximum < int.MaxValue)
                await Assert.That(() => Create(maximum + 1)).Throws<ArgumentOutOfRangeException>();
            foreach (var value in values)
            {
                if (format == "organization")
                {
                    await Assert.That(value).Matches("^[0-9]{10}$");
                    await Assert.That(ValidInn(value)).IsTrue();
                    await Assert.That(ValidInn(value[..^1] + ((value[^1] - '0' + 1) % 10).ToString(CultureInfo.InvariantCulture))).IsFalse();
                }
                else if (format == "phone")
                    await Assert.That(value).Matches("^\\+7999[0-9]{7}$");
                else
                {
                    var email = new MailAddress(value);
                    await Assert.That(email.Host).IsEqualTo("example.ru");
                    await Assert.That(value).Matches("^[a-z0-9]{4}_[a-z0-9]{6}_[1-9][0-9]*@example\\.ru$");
                    await Assert.That(email.User.Length <= 64).IsTrue();
                }
            }
            // Published-format vectors keep the independent verifier honest.
            await Assert.That(ValidInn("7707083893")).IsTrue();
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static bool ValidInn(string value)
    {
        int Digit(int[] weights) => weights.Select((weight, index) => weight * (value[index] - '0')).Sum() % 11 % 10;
        return value.Length == 10 && value[9] - '0' == Digit([2, 4, 10, 3, 5, 9, 4, 6, 8]);
    }

    [Test]
    public async Task Series_UsesOneRandomIdentifierAndOneBasedOrdinals()
    {
        var series = RecordedValueGenerator.Start();

        var first = series.Create(1);
        var second = series.Create(2);

        await Assert.That(first).Matches("^Recorded_[0-9]{8}_[A-Za-z0-9]{6}_1$");
        await Assert.That(first.Length).IsEqualTo(26);
        await Assert.That(series.Create(1)).IsEqualTo(first);
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
