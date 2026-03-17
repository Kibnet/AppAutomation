using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.Abstractions;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.TestHost;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests;

/// <summary>
/// Advanced test scenarios demonstrating various testing patterns with the AppAutomation framework.
/// This test class showcases:
/// 1. Data-driven tests using TUnit's [Arguments] attribute
/// 2. Method data sources for dynamic test data
/// 3. Wait patterns with timeout/polling configuration
/// 4. Page composition patterns using multiple page objects
/// </summary>
/// <remarks>
/// These tests serve as educational examples for teams adopting the framework.
/// Each test pattern includes detailed comments explaining the approach.
/// </remarks>
public sealed class AdvancedScenarioTests
{
    // ============================================================================
    // DATA-DRIVEN TESTS: Using [Arguments] Attribute
    // ============================================================================
    // Data-driven tests allow running the same test logic with different inputs.
    // TUnit's [Arguments] attribute provides inline test data for parameterized tests.
    // Benefits:
    // - Single test method tests multiple scenarios
    // - Each argument set appears as a separate test case in results
    // - Easier to add new test cases without code duplication
    // ============================================================================

    /// <summary>
    /// Data-driven test for arithmetic operations using inline [Arguments].
    /// Each argument tuple represents: (input numbers, operation, expected result).
    /// </summary>
    /// <remarks>
    /// Pattern: Use [Arguments] for small, fixed sets of test data that are
    /// unlikely to change frequently. The data is readable inline with the test.
    /// </remarks>
    [Test]
    [Arguments("12 18", "GCD", "GCD = 6")]
    [Arguments("4 6 8", "LCM", "LCM = 24")]
    [Arguments("5 3 8", "MAX", "MAX = 8")]
    [Arguments("5 3 8", "MIN", "MIN = 3")]
    [Arguments("10 20 30", "SUM", "SUM = 60")]
    [NotInParallel("DesktopUi")]
    public async Task Calculate_DataDriven_ProducesExpectedResult(
        string inputNumbers,
        string operation,
        string expectedResult)
    {
        // Arrange: Launch the headless session and create the page object
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));

        // Act: Enter the numbers, select the operation, and calculate
        page
            .EnterText(p => p.NumbersInput, inputNumbers)
            .SelectComboItem(p => p.OperationCombo, operation)
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, expectedResult);

        // Assert: Verify the result matches the expected value
        await Assert.That(page.ResultText.Text).IsEqualTo(expectedResult);
    }

    // ============================================================================
    // DATA-DRIVEN TESTS: Using [MethodDataSource] for Dynamic Data
    // ============================================================================
    // For more complex test data or data that needs computation, use [MethodDataSource].
    // This allows generating test data programmatically.
    // ============================================================================

    /// <summary>
    /// Provides test data for date difference calculations.
    /// Each tuple represents: (days in past for start, days in future for end, expected difference).
    /// </summary>
    /// <remarks>
    /// Pattern: Use [MethodDataSource] when:
    /// - Test data requires computation (e.g., dates relative to today)
    /// - Data comes from external sources
    /// - You need to generate large sets of test data
    /// </remarks>
    public static IEnumerable<(int StartDaysAgo, int EndDaysFromNow, int ExpectedDays)> DateDifferenceTestData()
    {
        yield return (7, 0, 7);    // 7 days ago to today = 7 days
        yield return (0, 7, 7);    // Today to 7 days from now = 7 days
        yield return (14, 0, 14);  // 2 weeks ago to today = 14 days
        yield return (30, 0, 30);  // 30 days ago to today = 30 days
        yield return (1, 0, 1);    // Yesterday to today = 1 day
    }

    /// <summary>
    /// Data-driven test for date difference calculations using method data source.
    /// Demonstrates dynamic test data generation and the DateTimePage composition.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(DateDifferenceTestData))]
    [NotInParallel("DesktopUi")]
    public async Task DateDifference_DataDriven_CalculatesCorrectly(
        int startDaysAgo,
        int endDaysFromNow,
        int expectedDays)
    {
        // Arrange
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var resolver = new HeadlessControlResolver(session.MainWindow);
        var mainPage = new MainWindowPage(resolver);

        // Navigate to the DateTime tab first using the main page
        mainPage.SelectTabItem(p => p.DateTimeTabItem);

        // Create a focused DateTimePage for date operations (page composition pattern)
        var dateTimePage = new DateTimePage(resolver);

        // Act: Calculate the date difference
        var startDate = DateTime.Today.AddDays(-startDaysAgo);
        var endDate = DateTime.Today.AddDays(endDaysFromNow);

        dateTimePage
            .SetDate(p => p.StartDate, startDate)
            .SetDate(p => p.EndDate, endDate)
            .ClickButton(p => p.CalculateDifference)
            .WaitUntilNameContains(p => p.Result, $"{expectedDays} days");

        // Assert
        await Assert.That(dateTimePage.Result.Text).Contains($"{expectedDays} days");
    }

    // ============================================================================
    // WAIT PATTERNS: Polling and Timeout Configuration
    // ============================================================================
    // UI testing requires waiting for asynchronous operations to complete.
    // The framework provides multiple approaches to handle this:
    // 1. Built-in waits in extension methods (e.g., WaitUntilNameEquals)
    // 2. Explicit UiWait calls for custom conditions
    // 3. WaitUntil/RetryUntil in test base class
    // ============================================================================

    /// <summary>
    /// Demonstrates explicit timeout configuration for slow operations.
    /// When operations take longer than default timeouts, configure custom values.
    /// </summary>
    /// <remarks>
    /// Pattern: Configure timeouts based on expected operation duration.
    /// Default is typically 5000ms, but some operations (animations, network calls)
    /// may need longer timeouts. Always prefer explicit timeouts over arbitrary sleeps.
    /// </remarks>
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task WaitPattern_ExplicitTimeout_HandlesSlowOperations()
    {
        // Arrange
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));

        // Extended timeout for operations that might be slow
        // The series builder has simulated delays based on slider speed
        const int extendedTimeoutMs = 15000;

        // Act: Navigate to Control Mix tab and run a series generation
        page
            .SelectTabItem(p => p.ControlMixTabItem)
            .SelectComboItem(p => p.MixModeCombo, "Fibonacci")
            .SetSpinnerValue(p => p.MixCountSpinner, 5)
            .EnterText(p => p.MixInput, "1 1")
            .ClickButton(p => p.MixRunButton);

        // Wait for progress to complete with extended timeout
        // This demonstrates using explicit timeout parameters in wait methods
        page.WaitUntilProgressAtLeast(p => p.SeriesProgressBar, 100, extendedTimeoutMs);

        // Assert: Verify the series was generated
        await Assert.That(page.SeriesResult.Text).Contains("Series[Fibonacci]");
    }

    /// <summary>
    /// Demonstrates using UiWait.TryUntil for non-throwing condition checks.
    /// Useful when you want to check a condition without failing the test on timeout.
    /// </summary>
    /// <remarks>
    /// Pattern: Use TryUntil when:
    /// - You need to check if a condition is met within a time window
    /// - You want to handle the timeout case gracefully
    /// - You're implementing retry logic or conditional test flows
    /// </remarks>
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task WaitPattern_TryUntil_ReturnsResultWithoutThrowing()
    {
        // Arrange
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));

        // Act: Perform a calculation
        page
            .EnterText(p => p.NumbersInput, "15 25")
            .SelectComboItem(p => p.OperationCombo, "GCD")
            .ClickButton(p => p.CalculateButton);

        // Use TryUntil to wait for the result without throwing
        // This returns a result object containing success status and the observed value
        var waitOptions = new UiWaitOptions
        {
            Timeout = TimeSpan.FromSeconds(5),
            PollInterval = TimeSpan.FromMilliseconds(100)
        };

        var result = UiWait.TryUntil(
            valueFactory: () => page.ResultText.Text,
            condition: text => text == "GCD = 5",
            options: waitOptions);

        // Assert: Verify the wait was successful and got the expected value
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("GCD = 5");
        await Assert.That(result.ElapsedTime).IsLessThan(waitOptions.Timeout);
    }

    /// <summary>
    /// Demonstrates custom polling intervals for responsive UI testing.
    /// Shorter poll intervals make tests more responsive but consume more resources.
    /// </summary>
    /// <remarks>
    /// Pattern: Balance poll interval based on:
    /// - Expected response time of the UI operation
    /// - Resource consumption vs. test speed
    /// - Default is 100ms which works well for most scenarios
    /// - Use shorter intervals (25-50ms) for fast animations
    /// - Use longer intervals (200-500ms) for slow background operations
    /// </remarks>
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task WaitPattern_CustomPollInterval_ResponsiveChecking()
    {
        // Arrange
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));

        // Act: Perform a quick calculation
        page
            .EnterText(p => p.NumbersInput, "100 50")
            .SelectComboItem(p => p.OperationCombo, "GCD")
            .ClickButton(p => p.CalculateButton);

        // Configure custom wait options with faster polling for responsive UI
        var fastPollOptions = new UiWaitOptions
        {
            Timeout = TimeSpan.FromSeconds(3),
            PollInterval = TimeSpan.FromMilliseconds(50)  // Faster polling
        };

        // Wait with custom options
        var result = UiWait.Until(
            valueFactory: () => page.ResultText.Text,
            condition: static text => text == "GCD = 50",
            options: fastPollOptions,
            timeoutMessage: "Expected result 'GCD = 50' was not displayed");

        // Assert
        await Assert.That(result).IsEqualTo("GCD = 50");
    }

    // ============================================================================
    // PAGE COMPOSITION: Using Multiple Page Objects Together
    // ============================================================================
    // The page composition pattern allows creating focused page objects for
    // different sections of your application. This improves maintainability
    // and makes tests more readable.
    // ============================================================================

    /// <summary>
    /// Demonstrates navigating between pages using page composition pattern.
    /// Multiple page objects share the same resolver but provide focused interfaces.
    /// </summary>
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task PageComposition_MultiplePageObjects_ShareResolver()
    {
        // Arrange
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var resolver = new HeadlessControlResolver(session.MainWindow);

        // Create multiple page objects sharing the same resolver
        var mainPage = new MainWindowPage(resolver);
        var controlMixPage = new ControlMixPage(resolver);
        var dateTimePage = new DateTimePage(resolver);

        // Act Part 1: Use MainWindowPage for math calculation
        mainPage
            .EnterText(p => p.NumbersInput, "12 18")
            .SelectComboItem(p => p.OperationCombo, "GCD")
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, "GCD = 6");

        // Act Part 2: Navigate to ControlMix tab and use ControlMixPage
        mainPage.SelectTabItem(p => p.ControlMixTabItem);
        controlMixPage
            .EnterText(p => p.SeedInput, "1 2")
            .SelectComboItem(p => p.ModeCombo, "Fibonacci")
            .SetSpinnerValue(p => p.MixCountSpinner, 5)
            .ClickButton(p => p.BuildButton)
            .WaitUntilProgressAtLeast(p => p.ProgressBar, 100, 15000);

        // Act Part 3: Navigate to DateTime tab and use DateTimePage
        mainPage.SelectTabItem(p => p.DateTimeTabItem);
        dateTimePage
            .CalculateDateDifference(DateTime.Today.AddDays(-3), DateTime.Today)
            .WaitUntilNameContains(p => p.Result, "3 days");

        // Assert: Verify results from different pages
        using (Assert.Multiple())
        {
            await Assert.That(mainPage.ResultText.Text).IsEqualTo("GCD = 6");
            await Assert.That(controlMixPage.ResultLabel.Text).Contains("Fibonacci");
            await Assert.That(dateTimePage.Result.Text).Contains("3 days");
        }
    }

    // ============================================================================
    // ERROR VALIDATION: Data-driven negative test cases
    // ============================================================================
    // Testing error scenarios is as important as testing happy paths.
    // Data-driven tests work well for validating multiple error conditions.
    // ============================================================================

    /// <summary>
    /// Data-driven test for input validation error messages.
    /// Tests various invalid inputs and their expected error messages.
    /// </summary>
    [Test]
    [Arguments("abc def", "Invalid integer: abc")]
    [Arguments("12.5 18", "Invalid integer: 12.5")]
    [Arguments("", "Input must not be empty")]
    [NotInParallel("DesktopUi")]
    public async Task Validation_InvalidInput_ShowsExpectedError(
        string invalidInput,
        string expectedErrorContains)
    {
        // Arrange
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));

        // Act: Enter invalid input and attempt calculation
        page
            .EnterText(p => p.NumbersInput, invalidInput)
            .SelectComboItem(p => p.OperationCombo, "GCD")
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameContains(p => p.ErrorText, expectedErrorContains);

        // Assert: Verify error message is displayed and result is empty
        using (Assert.Multiple())
        {
            await Assert.That(page.ErrorText.Text).Contains(expectedErrorContains);
            await Assert.That(page.ResultText.Text).IsEqualTo(string.Empty);
        }
    }
}
