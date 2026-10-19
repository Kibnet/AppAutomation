using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class NumericDisplayValueReaderTests
{
    [Test]
    public async Task TypedParsing_PreservesDecimalPrecisionAndRejectsFractionalInteger()
    {
        await Assert.That(NumericText.ParseDecimal("1\u00a0200,25 руб.")).IsEqualTo(1200.25m);
        await Assert.That(NumericText.ParseInteger("10\u202f001")).IsEqualTo(10001L);
        await Assert.That(NumericText.ParseDouble("182")).IsEqualTo(182d);
        await Assert.That(() => NumericText.ParseInteger("1,5")).Throws<InvalidOperationException>();
        await Assert.That(() => NumericText.ParseDecimal("RU260408-5")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TypedParsing_AcceptsMultiwordDisplayedUnitWithoutDiscardingTheNumber()
    {
        await Assert.That(NumericText.ParseDecimal("1485 за шт")).IsEqualTo(1485m);
        await Assert.That(NumericText.ParseDecimal("1\u00a0485,25 за шт")).IsEqualTo(1485.25m);
        await Assert.That(() => NumericText.ParseDecimal("1485 за шт 2"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("500", 500L)]
    [Arguments("1\u00a0200", 1200L)]
    [Arguments("10\u202f001", 10001L)]
    [Arguments("-10 001", -10001L)]
    public async Task IntegerParsing_AcceptsGroupedAndUngroupedValues(string text, long expected)
    {
        await Assert.That(NumericText.ParseInteger(text)).IsEqualTo(expected);
    }

    [Test]
    public async Task TypedParsing_RejectsAmbiguousAndOverflowingValues()
    {
        await Assert.That(() => NumericText.ParseDecimal("1,234")).Throws<InvalidOperationException>();
        await Assert.That(() => NumericText.ParseInteger("9 223 372 036 854 775 808"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => NumericText.ParseDecimal("999999999999999999999999999999999999"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => NumericText.ParseDouble("NaN")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DecimalReader_UsesCurrentFormattedText()
    {
        var control = new DisplayedTextControl { Text = "1\u00a0200,25 руб." };
        await Assert.That(NumericDisplayValueReader.ReadDecimal(control)).IsEqualTo(1200.25m);
    }

    [Test]
    public async Task DecimalPromotion_RejectsMissingNonFiniteAndOverflowingQuantity()
    {
        await Assert.That(NumericValue.ToDecimal(1000d)).IsEqualTo(1000m);
        await Assert.That(() => NumericValue.ToDecimal((double?)null)).Throws<InvalidOperationException>();
        await Assert.That(() => NumericValue.ToDecimal(double.NaN)).Throws<InvalidOperationException>();
        await Assert.That(() => NumericValue.ToDecimal(double.MaxValue)).Throws<InvalidOperationException>();
        await Assert.That(() => NumericValue.ToDecimal(1e-29)).Throws<InvalidOperationException>();
    }
    [Test]
    [Arguments("500", 500d)]
    [Arguments("1\u00a0200 руб.", 1200d)]
    [Arguments("10\u202f001 руб.", 10001d)]
    [Arguments("-1 200,5 руб.", -1200.5d)]
    [Arguments("0,25 руб.", 0.25d)]
    public async Task Read_InterpretsWholeFormattedNumber(string text, double expected)
    {
        var control = new DisplayedTextControl { Text = text };
        await Assert.That(NumericDisplayValueReader.Read(control)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("RU260408-5")]
    [Arguments("08.04.2026")]
    [Arguments("1 200 из 3")]
    [Arguments("1,234")]
    [Arguments("NaN")]
    [Arguments("9 007 199 254 740 993 руб.")]
    public async Task Read_RejectsMissingInvalidOrAmbiguousValue(string text)
    {
        var control = new DisplayedTextControl { Text = text };
        await Assert.That(() => NumericDisplayValueReader.Read(control))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Read_UsesCurrentDisplayInsteadOfRecordedText()
    {
        var control = new DisplayedTextControl { Text = "500 руб." };
        var before = NumericDisplayValueReader.Read(control);
        control.Text = "1\u00a0200 руб.";
        var after = NumericDisplayValueReader.Read(control);

        await Assert.That(before).IsEqualTo(500d);
        await Assert.That(after).IsEqualTo(1200d);
    }

    [Test]
    public async Task Read_ReportsControlAndDisplayedTextWhenNumberIsUnavailable()
    {
        var control = new DisplayedTextControl { Text = "not a total" };

        var error = Assert.Throws<InvalidOperationException>(() => NumericDisplayValueReader.Read(control));

        await Assert.That(error.Message).Contains("DisplayedTotal");
        await Assert.That(error.Message).Contains("not a total");
    }

    private sealed class DisplayedTextControl : ITextBoxControl
    {
        public string AutomationId => "DisplayedTotal";
        public string Name => "Displayed total";
        public bool IsEnabled => true;
        public string Text { get; set; } = string.Empty;
        public void Enter(string value) => Text = value;
    }
}
