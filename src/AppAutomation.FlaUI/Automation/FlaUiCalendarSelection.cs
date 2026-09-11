using System.Diagnostics;
using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUiCalendar = FlaUI.Core.AutomationElements.Calendar;

namespace AppAutomation.FlaUI.Automation;

internal static class FlaUiCalendarSelection
{
    private static readonly TimeSpan SelectionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MonthChangeTimeout = TimeSpan.FromSeconds(2);
    private const int MaximumMonthNavigationDistance = 240;

    private static readonly CultureInfo[] DateCultures =
    [
        CultureInfo.CurrentCulture,
        CultureInfo.CurrentUICulture,
        CultureInfo.InvariantCulture,
        CultureInfo.GetCultureInfo("en-US"),
        CultureInfo.GetCultureInfo("ru-RU")
    ];

    public static void SelectDate(FlaUiCalendar calendar, DateTime selectedDate)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        try
        {
            calendar.SelectDate(selectedDate.Date);
            return;
        }
        catch (NotSupportedException)
        {
            SelectByVisibleCell(calendar, selectedDate.Date);
        }
    }

    private static void SelectByVisibleCell(FlaUiCalendar calendar, DateTime targetDate)
    {
        var operation = Stopwatch.StartNew();
        CalendarSnapshot? snapshot = null;

        while (operation.Elapsed < SelectionTimeout)
        {
            snapshot = Capture(calendar);
            if (TryFindTargetDay(snapshot, targetDate, out var targetCell))
            {
                Click(targetCell.Element, $"calendar day '{targetDate:yyyy-MM-dd}'");
                return;
            }

            if (snapshot.DisplayedMonth is not { } displayedMonth)
            {
                break;
            }

            var monthDistance = MonthDistance(displayedMonth, targetDate);
            if (monthDistance == 0)
            {
                break;
            }

            if (Math.Abs(monthDistance) > MaximumMonthNavigationDistance)
            {
                throw new InvalidOperationException(
                    $"Calendar date '{targetDate:yyyy-MM-dd}' is more than "
                    + $"{MaximumMonthNavigationDistance} months from displayed month '{displayedMonth:yyyy-MM}'.");
            }

            var navigationButton = FindMonthNavigationButton(snapshot, moveForward: monthDistance > 0)
                ?? throw new InvalidOperationException(
                    $"Calendar month navigation button was not found while selecting '{targetDate:yyyy-MM-dd}'.");
            Click(navigationButton, monthDistance > 0 ? "next calendar month" : "previous calendar month");
            WaitForDisplayedMonthChange(calendar, displayedMonth, operation);
        }

        var displayed = snapshot?.DisplayedMonth is { } month
            ? month.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            : "<unknown>";
        throw new InvalidOperationException(
            $"Visible calendar cell for '{targetDate:yyyy-MM-dd}' was not found. Displayed month: '{displayed}'.");
    }

    private static CalendarSnapshot Capture(FlaUiCalendar calendar)
    {
        var elements = SafeRead(() => calendar.FindAllDescendants()) ?? [];
        var visibleElements = elements
            .Where(IsVisible)
            .ToArray();
        var dayCells = ReadDayCells(visibleElements);
        return new CalendarSnapshot(
            visibleElements,
            dayCells,
            TryReadDisplayedMonth(visibleElements, dayCells));
    }

    private static DayCell[] ReadDayCells(IEnumerable<AutomationElement> elements)
    {
        var cells = elements
            .Where(IsDayCellControlType)
            .Select(element => TryCreateDayCell(element, out var cell) ? cell : null)
            .Where(static cell => cell is not null)
            .Cast<DayCell>()
            .OrderBy(static cell => cell.Bounds.Top)
            .ThenBy(static cell => cell.Bounds.Left)
            .ThenBy(static cell => DayCellControlTypePriority(cell.Element.ControlType))
            .ToArray();

        var uniqueCells = new List<DayCell>(cells.Length);
        foreach (var cell in cells)
        {
            var existingIndex = uniqueCells.FindIndex(existing => HasSameBounds(existing.Bounds, cell.Bounds));
            if (existingIndex < 0)
            {
                uniqueCells.Add(cell);
                continue;
            }

            if (DayCellControlTypePriority(cell.Element.ControlType)
                < DayCellControlTypePriority(uniqueCells[existingIndex].Element.ControlType))
            {
                uniqueCells[existingIndex] = cell;
            }
        }

        return uniqueCells
            .OrderBy(static cell => cell.Bounds.Top)
            .ThenBy(static cell => cell.Bounds.Left)
            .ToArray();
    }

    private static bool TryCreateDayCell(AutomationElement element, out DayCell? cell)
    {
        cell = null;
        int? dayNumber = null;
        foreach (var text in ReadAccessibleTexts(element))
        {
            if (TryReadDayNumber(text, out var parsedDay))
            {
                dayNumber = parsedDay;
                break;
            }
        }

        if (!dayNumber.HasValue)
        {
            return false;
        }

        var fullDate = TryReadFullDate(element);
        var day = fullDate?.Day ?? dayNumber.Value;

        var bounds = SafeRead(() => element.BoundingRectangle);
        if (bounds is not { Width: > 0, Height: > 0 })
        {
            return false;
        }

        cell = new DayCell(element, day, bounds, fullDate);
        return true;
    }

    private static bool TryFindTargetDay(
        CalendarSnapshot snapshot,
        DateTime targetDate,
        out DayCell targetCell)
    {
        var exactCell = snapshot.DayCells.FirstOrDefault(cell => cell.FullDate?.Date == targetDate.Date);
        if (exactCell is not null)
        {
            targetCell = exactCell;
            return true;
        }

        if (snapshot.DisplayedMonth is not { } displayedMonth
            || displayedMonth.Year != targetDate.Year
            || displayedMonth.Month != targetDate.Month)
        {
            targetCell = null!;
            return false;
        }

        var daysInMonth = DateTime.DaysInMonth(targetDate.Year, targetDate.Month);
        for (var startIndex = 0; startIndex < snapshot.DayCells.Count; startIndex++)
        {
            if (snapshot.DayCells[startIndex].Day != 1
                || startIndex + daysInMonth > snapshot.DayCells.Count)
            {
                continue;
            }

            var isDisplayedMonthRun = true;
            for (var day = 1; day <= daysInMonth; day++)
            {
                if (snapshot.DayCells[startIndex + day - 1].Day != day)
                {
                    isDisplayedMonthRun = false;
                    break;
                }
            }

            if (!isDisplayedMonthRun)
            {
                continue;
            }

            targetCell = snapshot.DayCells[startIndex + targetDate.Day - 1];
            return true;
        }

        targetCell = null!;
        return false;
    }

    private static DateTime? TryReadDisplayedMonth(
        IReadOnlyList<AutomationElement> elements,
        IReadOnlyList<DayCell> dayCells)
    {
        var dayElements = dayCells
            .Select(static cell => cell.Element)
            .ToHashSet();
        foreach (var text in elements
                     .Where(element => !dayElements.Contains(element))
                     .SelectMany(ReadAccessibleTexts))
        {
            if (!ContainsFourDigitYear(text))
            {
                continue;
            }

            foreach (var culture in DateCultures)
            {
                if (DateTime.TryParse(
                        text,
                        culture,
                        DateTimeStyles.AllowWhiteSpaces,
                        out var parsed))
                {
                    return new DateTime(parsed.Year, parsed.Month, 1);
                }
            }
        }

        var mostCommonMonth = dayCells
            .Where(static cell => cell.FullDate.HasValue)
            .GroupBy(static cell => (cell.FullDate!.Value.Year, cell.FullDate.Value.Month))
            .OrderByDescending(static group => group.Count())
            .FirstOrDefault();
        return mostCommonMonth is null
            ? null
            : new DateTime(mostCommonMonth.Key.Year, mostCommonMonth.Key.Month, 1);
    }

    private static DateTime? TryReadFullDate(AutomationElement element)
    {
        foreach (var text in ReadAccessibleTexts(element))
        {
            if (!ContainsFourDigitYear(text))
            {
                continue;
            }

            foreach (var culture in DateCultures)
            {
                if (DateTime.TryParse(
                        text,
                        culture,
                        DateTimeStyles.AllowWhiteSpaces,
                        out var parsed))
                {
                    return parsed.Date;
                }
            }
        }

        return null;
    }

    private static bool TryReadDayNumber(string value, out int day)
    {
        var number = 0;
        var digitCount = 0;
        foreach (var character in value.Append(' '))
        {
            if (char.IsDigit(character))
            {
                number = (number * 10) + (character - '0');
                digitCount++;
                continue;
            }

            if (digitCount is > 0 and <= 2 && number is >= 1 and <= 31)
            {
                day = number;
                return true;
            }

            number = 0;
            digitCount = 0;
        }

        day = 0;
        return false;
    }

    private static AutomationElement? FindMonthNavigationButton(
        CalendarSnapshot snapshot,
        bool moveForward)
    {
        if (snapshot.DayCells.Count == 0)
        {
            return null;
        }

        var firstDayTop = snapshot.DayCells.Min(static cell => cell.Bounds.Top);
        var candidates = snapshot.Elements
            .Where(static element => element.ControlType == ControlType.Button)
            .Where(element => !snapshot.DayCells.Any(cell => ReferenceEquals(cell.Element, element)))
            .Select(element => new
            {
                Element = element,
                Bounds = SafeRead(() => element.BoundingRectangle)
            })
            .Where(candidate => candidate.Bounds is { Width: > 0, Height: > 0 }
                && candidate.Bounds.Top < firstDayTop)
            .OrderBy(candidate => candidate.Bounds.Left)
            .ToArray();

        if (candidates.Length < 2)
        {
            return null;
        }

        return moveForward ? candidates[^1].Element : candidates[0].Element;
    }

    private static void WaitForDisplayedMonthChange(
        FlaUiCalendar calendar,
        DateTime previousMonth,
        Stopwatch operation)
    {
        var monthChange = Stopwatch.StartNew();
        while (monthChange.Elapsed < MonthChangeTimeout && operation.Elapsed < SelectionTimeout)
        {
            Thread.Sleep(50);
            var snapshot = Capture(calendar);
            if (snapshot.DisplayedMonth is { } displayedMonth
                && (displayedMonth.Year != previousMonth.Year || displayedMonth.Month != previousMonth.Month))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Calendar did not change displayed month from '{previousMonth:yyyy-MM}'.");
    }

    private static int MonthDistance(DateTime displayedMonth, DateTime targetDate)
    {
        return ((targetDate.Year - displayedMonth.Year) * 12) + targetDate.Month - displayedMonth.Month;
    }

    private static void Click(AutomationElement element, string description)
    {
        try
        {
            element.Click();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unable to click {description}.", ex);
        }
    }

    private static bool IsVisible(AutomationElement element)
    {
        return SafeRead(() => element.IsAvailable && element.IsEnabled && !element.IsOffscreen) == true;
    }

    private static bool IsDayCellControlType(AutomationElement element)
    {
        return element.ControlType is ControlType.Button
            or ControlType.DataItem
            or ControlType.ListItem
            or ControlType.Custom
            or ControlType.Text;
    }

    private static int DayCellControlTypePriority(ControlType controlType)
    {
        if (controlType == ControlType.Button)
        {
            return 0;
        }

        if (controlType is ControlType.DataItem or ControlType.ListItem)
        {
            return 1;
        }

        return 2;
    }

    private static IEnumerable<string> ReadAccessibleTexts(AutomationElement element)
    {
        var values = new[]
        {
            SafeRead(() => element.Name),
            SafeRead(() => element.HelpText),
            SafeRead(() => element.ItemStatus)
        };

        return values.Where(static value => !string.IsNullOrWhiteSpace(value)).Cast<string>();
    }

    private static bool ContainsFourDigitYear(string value)
    {
        var digitRun = 0;
        foreach (var character in value)
        {
            if (char.IsDigit(character))
            {
                digitRun++;
                if (digitRun == 4)
                {
                    return true;
                }
            }
            else
            {
                digitRun = 0;
            }
        }

        return false;
    }

    private static bool HasSameBounds(System.Drawing.Rectangle first, System.Drawing.Rectangle second)
    {
        return Math.Abs(first.Left - second.Left) <= 1
            && Math.Abs(first.Top - second.Top) <= 1
            && Math.Abs(first.Width - second.Width) <= 1
            && Math.Abs(first.Height - second.Height) <= 1;
    }

    private static T? SafeRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return default;
        }
    }

    private sealed record CalendarSnapshot(
        IReadOnlyList<AutomationElement> Elements,
        IReadOnlyList<DayCell> DayCells,
        DateTime? DisplayedMonth);

    private sealed record DayCell(
        AutomationElement Element,
        int Day,
        System.Drawing.Rectangle Bounds,
        DateTime? FullDate);
}
