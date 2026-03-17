using AppAutomation.Abstractions;

namespace DotnetDebug.AppAutomation.Authoring.Pages;

/// <summary>
/// Page object for the DateTime tab, demonstrating the page composition pattern.
/// This secondary page object complements <see cref="MainWindowPage"/> by providing
/// focused access to date/time functionality without duplicating all main window controls.
/// </summary>
/// <remarks>
/// <para>
/// Page Composition Pattern:
/// Instead of having one monolithic page object with all controls, we split related
/// functionality into separate page objects. Each page object represents a logical
/// "view" or "section" of the application, sharing the same underlying resolver.
/// </para>
/// <para>
/// Usage pattern: Navigate from MainWindowPage to DateTimePage when working with
/// date-related operations. Both pages can coexist and share the same resolver instance.
/// </para>
/// <example>
/// <code>
/// // Navigate to DateTime tab and use DateTimePage for focused date operations
/// mainPage.SelectTabItem(p => p.DateTimeTabItem);
/// var dateTimePage = new DateTimePage(resolver);
/// dateTimePage
///     .SetDate(p => p.StartDate, DateTime.Today.AddDays(-7))
///     .SetDate(p => p.EndDate, DateTime.Today)
///     .ClickButton(p => p.CalculateDifference)
///     .WaitUntilNameContains(p => p.Result, "7 days");
/// </code>
/// </example>
/// </remarks>
[UiControl("DateTimeTabItem", UiControlType.TabItem, "DateTimeTabItem")]
[UiControl("MainTabs", UiControlType.Tab, "MainTabs")]
[UiControl("StartDatePicker", UiControlType.DateTimePicker, "StartDatePicker")]
[UiControl("EndDatePicker", UiControlType.DateTimePicker, "EndDatePicker")]
[UiControl("DateDiffButton", UiControlType.Button, "DateDiffButton")]
[UiControl("DateResult", UiControlType.Label, "DateResult")]
[UiControl("DateDiffList", UiControlType.ListBox, "DateDiffList")]
[UiControl("DateErrorText", UiControlType.Label, "DateErrorText")]
[UiControl("CalendarTabItem", UiControlType.TabItem, "CalendarTabItem")]
[UiControl("DemoCalendar", UiControlType.Calendar, "DemoCalendar")]
[UiControl("CalendarReadButton", UiControlType.Button, "CalendarReadButton")]
[UiControl("CalendarDateInput", UiControlType.TextBox, "CalendarDateInput")]
[UiControl("SetCalendarDateButton", UiControlType.Button, "SetCalendarDateButton")]
[UiControl("ClearCalendarDateButton", UiControlType.Button, "ClearCalendarDateButton")]
[UiControl("CalendarResultLabel", UiControlType.Label, "CalendarResultLabel")]
[UiControl("CalendarErrorText", UiControlType.Label, "CalendarErrorText")]
public sealed partial class DateTimePage : UiPage
{
    public DateTimePage(IUiControlResolver resolver) : base(resolver)
    {
    }

    // ============================================================================
    // Semantic Aliases (Convenience Properties)
    // These provide more intuitive names for common operations while maintaining
    // the underlying control access. This pattern improves test readability.
    // ============================================================================

    /// <summary>
    /// Convenience alias for <see cref="StartDatePicker"/> with semantic naming.
    /// </summary>
    public IDateTimePickerControl StartDate => StartDatePicker;

    /// <summary>
    /// Convenience alias for <see cref="EndDatePicker"/> with semantic naming.
    /// </summary>
    public IDateTimePickerControl EndDate => EndDatePicker;

    /// <summary>
    /// Convenience alias for <see cref="DateDiffButton"/> with semantic naming.
    /// </summary>
    public IButtonControl CalculateDifference => DateDiffButton;

    /// <summary>
    /// Convenience alias for <see cref="DateResult"/> with semantic naming.
    /// </summary>
    public ILabelControl Result => DateResult;

    /// <summary>
    /// Convenience alias for <see cref="DateDiffList"/> with semantic naming.
    /// </summary>
    public IListBoxControl DifferenceDetails => DateDiffList;

    /// <summary>
    /// Convenience alias for <see cref="DateErrorText"/> with semantic naming.
    /// </summary>
    public ILabelControl ErrorMessage => DateErrorText;

    /// <summary>
    /// Convenience alias for <see cref="DemoCalendar"/> with semantic naming.
    /// </summary>
    public ICalendarControl Calendar => DemoCalendar;

    // ============================================================================
    // Helper Methods
    // Page objects can include helper methods that encapsulate common workflows.
    // ============================================================================

    /// <summary>
    /// Calculates the date difference between two dates using the date pickers.
    /// This method demonstrates encapsulating a common workflow in a page object.
    /// </summary>
    /// <param name="startDate">The start date for the calculation.</param>
    /// <param name="endDate">The end date for the calculation.</param>
    /// <returns>This page instance for fluent chaining.</returns>
    public DateTimePage CalculateDateDifference(DateTime startDate, DateTime endDate)
    {
        return this
            .SetDate(p => p.StartDate, startDate)
            .SetDate(p => p.EndDate, endDate)
            .ClickButton(p => p.CalculateDifference);
    }
}
