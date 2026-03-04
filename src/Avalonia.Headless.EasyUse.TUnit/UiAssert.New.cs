namespace Avalonia.Headless.EasyUse.TUnit;

public static class UiAssert
{
    public static Task TextEqualsAsync(
        Func<string> actualFactory,
        string expected,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return global::FlaUI.EasyUse.TUnit.UiAssert.TextEqualsAsync(actualFactory, expected, timeout, cancellationToken);
    }

    public static Task TextContainsAsync(
        Func<string> actualFactory,
        string expectedPart,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return global::FlaUI.EasyUse.TUnit.UiAssert.TextContainsAsync(actualFactory, expectedPart, timeout, cancellationToken);
    }

    public static Task NumberAtLeastAsync(
        Func<int> actualFactory,
        int expectedMin,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return global::FlaUI.EasyUse.TUnit.UiAssert.NumberAtLeastAsync(actualFactory, expectedMin, timeout, cancellationToken);
    }
}