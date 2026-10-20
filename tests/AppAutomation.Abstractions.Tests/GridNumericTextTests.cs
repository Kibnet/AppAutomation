using System.Globalization;
using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class GridNumericTextTests
{
    [Test]
    [Arguments("500", "en-US", 500d)]
    [Arguments("1 200", "en-US", 1200d)]
    [Arguments("1\u00A0200", "ru-RU", 1200d)]
    [Arguments("10\u202F001", "fr-FR", 10001d)]
    [Arguments("-1\u00A0200,50", "ru-RU", -1200.5d)]
    public async Task ParsesLocalizedGroupingAndFractions(
        string text,
        string cultureName,
        double expected)
    {
        var parsed = GridNumericText.TryParse(
            text,
            CultureInfo.GetCultureInfo(cultureName),
            out var actual);

        using (Assert.Multiple())
        {
            await Assert.That(parsed).IsTrue();
            await Assert.That(actual).IsEqualTo(expected);
        }
    }

    [Test]
    [Arguments("1\u00A0000", 1000d)]
    [Arguments("10\u00A0001", 10001d)]
    [Arguments("-1\u00A0234,5", -1234.5d)]
    [Arguments("12,5", 12.5d)]
    [Arguments("-1,25", -1.25d)]
    public async Task UnspecifiedCulture_UsesExplicitWhitespaceGrouping(string text, double expected)
    {
        var parsed = GridNumericText.TryParse(text, cultureName: null, out var actual, out _);

        await Assert.That(parsed).IsTrue();
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task RejectsInvalidAndMismatchingInput()
    {
        using (Assert.Multiple())
        {
            await Assert.That(GridNumericText.TryParse(
                    "not a number",
                    cultureName: null,
                    out _,
                    out _))
                .IsFalse();
            await Assert.That(GridNumericText.Represents("1\u00A0200", 999d)).IsFalse();
            await Assert.That(GridNumericText.Represents("1.0000000000005", 1d)).IsFalse();
            await Assert.That(GridNumericText.TryParseUnambiguous(
                    "1,234",
                    out _,
                    out var diagnostic))
                .IsFalse();
            await Assert.That(diagnostic).Contains("culture-ambiguous");
        }
    }

    [Test]
    public async Task ExistingGridParsing_KeepsUnspecifiedEnUsGrouping()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            var parsed = GridNumericText.TryParse("1,234", cultureName: null, out var value, out _);
            var decimalParsed = GridNumericText.TryParseDecimal("1,234", cultureName: null, out var decimalValue, out _);
            using (Assert.Multiple())
            {
                await Assert.That(parsed).IsTrue();
                await Assert.That(value).IsEqualTo(1234d);
                await Assert.That(decimalParsed).IsTrue();
                await Assert.That(decimalValue).IsEqualTo(1234m);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Test]
    public async Task DistinguishesExplicitInvariantFromUnspecifiedCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            var invariantParsed = GridNumericText.TryParseDecimal(
                "1.234",
                CultureInfo.InvariantCulture.Name,
                out var invariantValue,
                out var invariantDiagnostic);
            var unspecifiedParsed = GridNumericText.TryParseDecimal(
                "1.234",
                cultureName: null,
                out _,
                out var unspecifiedDiagnostic);

            using (Assert.Multiple())
            {
                await Assert.That(invariantParsed).IsTrue();
                await Assert.That(invariantValue).IsEqualTo(1.234m);
                await Assert.That(invariantDiagnostic).IsNull();
                await Assert.That(unspecifiedParsed).IsFalse();
                await Assert.That(unspecifiedDiagnostic).Contains("culture-ambiguous");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
