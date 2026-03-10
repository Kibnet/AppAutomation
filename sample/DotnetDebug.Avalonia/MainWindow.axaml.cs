using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DotnetDebug;

namespace DotnetDebug.Avalonia;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    private enum ComputeMode
    {
        Gcd,
        Lcm,
        Min
    }

    private enum SeriesMode
    {
        Arithmetic,
        Reversed,
        Fibonacci
    }

    private static readonly char[] InputSeparators = [' ', '\t', '\r', '\n', ',', ';'];

    private readonly List<string> _computationHistory = [];
    private string _historyFilter = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        UpdateModeLabel(ComputeMode.Gcd, useAbsoluteValues: false, showSteps: false);
        StartDatePicker.SelectedDate = DateTime.Today;
        EndDatePicker.SelectedDate = DateTime.Today.AddDays(3);
        MixModeCombo.SelectedIndex = 0;
    }

    private void OnCalculateClick(object? sender, RoutedEventArgs e)
    {
        ClearMathError();
        ResultText.Text = string.Empty;
        StepsList.ItemsSource = Array.Empty<string>();

        var useAbsoluteValues = UseAbsoluteValuesCheck.IsChecked == true;
        var showSteps = ShowStepsCheck.IsChecked == true;
        var computeMode = ResolveComputeMode();

        UpdateModeLabel(computeMode, useAbsoluteValues, showSteps);

        if (!TryParseNumbers(NumbersInput.Text, useAbsoluteValues, out var numbers, out var errorMessage))
        {
            ErrorText.Text = errorMessage;
            return;
        }

        try
        {
            IReadOnlyList<string> stepLines;
            var resultText = computeMode switch
            {
                ComputeMode.Gcd => BuildMathResultGcd(numbers, out stepLines),
                ComputeMode.Lcm => BuildMathResultLcm(numbers, out stepLines),
                _ => BuildMathResultMin(numbers, out stepLines)
            };

            ResultText.Text = resultText;
            StepsList.ItemsSource = showSteps ? stepLines : Array.Empty<string>();

            _computationHistory.Add(BuildHistoryEntry(computeMode, numbers, resultText, useAbsoluteValues, showSteps));
            ApplyCurrentHistoryFilter();
        }
        catch (ArgumentException ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void OnApplyHistoryFilterClick(object? sender, RoutedEventArgs e)
    {
        _historyFilter = HistoryFilterInput.Text?.Trim() ?? string.Empty;
        ApplyCurrentHistoryFilter();
    }

    private void OnClearHistoryClick(object? sender, RoutedEventArgs e)
    {
        _computationHistory.Clear();
        _historyFilter = string.Empty;
        HistoryFilterInput.Text = string.Empty;
        ApplyCurrentHistoryFilter();
    }

    private void OnBuildSeriesClick(object? sender, RoutedEventArgs e)
    {
        ClearSeriesError();
        SeriesResult.Content = string.Empty;
        SeriesList.ItemsSource = Array.Empty<string>();
        SeriesProgressBar.Value = 0;

        var mode = ResolveSeriesMode();
        var count = ParseInputAsNonNegativeInt(MixCountSpinner.Text, defaultValue: 8);
        var speed = Math.Max(1, (int)Math.Round(MixSpeedSlider.Value));
        var includeDetails = MixShowDetailsCheck.IsChecked == true;
        var useAdvanced = MixAdvancedToggle.IsChecked == true;
        var descending = MixDirectionDescendingRadio.IsChecked == true;

        var seedNumbers = ParseOptionalNumbers(MixInput.Text);
        var seed = seedNumbers.Count > 0 ? seedNumbers[0] : 1;
        var secondSeed = seedNumbers.Count > 1 ? seedNumbers[1] : Math.Min(seed, 0) + 1;

        var values = BuildSeries(mode, seed, secondSeed, count, speed, descending);
        var lines = BuildSeriesLines(values, includeDetails, useAdvanced);

        SeriesList.ItemsSource = includeDetails ? lines : Array.Empty<string>();
        SeriesResult.Content = $"Series[{mode}] count={values.Count} max={values.Max()} min={values.Min()}";
        SeriesProgressBar.Value = 100;
    }

    private void OnClearSeriesClick(object? sender, RoutedEventArgs e)
    {
        MixInput.Text = string.Empty;
        MixResultTextClear();
        MixCountSpinner.Text = "8";
        MixSpeedSlider.Value = 5;
        MixShowDetailsCheck.IsChecked = false;
        MixAdvancedToggle.IsChecked = false;
        MixDirectionAscendingRadio.IsChecked = true;
        MixModeCombo.SelectedIndex = 0;
    }

    private void OnDateDiffClick(object? sender, RoutedEventArgs e)
    {
        DateErrorText.Text = string.Empty;
        DateResult.Content = string.Empty;
        DateDiffList.ItemsSource = Array.Empty<string>();

        if (StartDatePicker.SelectedDate is not { } start || EndDatePicker.SelectedDate is not { } end)
        {
            DateErrorText.Text = "Both dates must be selected.";
            return;
        }

        var startDate = start.Date;
        var endDate = end.Date;
        if (endDate < startDate)
        {
            DateErrorText.Text = "End date should be greater than or equal to start date.";
            return;
        }

        var span = endDate - startDate;
        var weeks = span.Days / 7;

        DateResult.Content = $"Date difference = {span.Days} days ({weeks} weeks)";
        DateDiffList.ItemsSource = new List<string>
        {
            $"Start: {startDate:yyyy-MM-dd}",
            $"End: {endDate:yyyy-MM-dd}",
            $"Days: {span.Days}",
            $"Weeks: {weeks}",
            $"Leap: {(DateTime.IsLeapYear(startDate.Year) ? "Start year is leap" : "Start year is not leap")}"
        };
    }

    private void OnHierarchyTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateHierarchySelection();
    }

    private void OnClearTreeSelectionClick(object? sender, RoutedEventArgs e)
    {
        DemoTree.SelectedItem = null;
        UpdateHierarchySelection();
    }

    private void OnBuildGridClick(object? sender, RoutedEventArgs e)
    {
        var requestedRows = ParseInputAsNonNegativeInt(DataGridRowsInput.Text, defaultValue: 8);
        _viewModel.BuildGrid(requestedRows);
    }

    private void OnClearGridClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ClearGrid();
    }

    private void OnSelectGridRowClick(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DataGridSelectRowInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var selectedIndex))
        {
            _viewModel.DataGridErrorText = "Invalid row index.";
            return;
        }

        _viewModel.SelectRowByIndex(selectedIndex);
    }

    private void OnReadCalendarClick(object? sender, RoutedEventArgs e)
    {
        CalendarErrorText.Text = string.Empty;
        UpdateCalendarResultLabel();
    }

    private void OnSetCalendarDateClick(object? sender, RoutedEventArgs e)
    {
        CalendarErrorText.Text = string.Empty;
        if (!DateTime.TryParse(CalendarDateInput.Text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedDate)
            && !DateTime.TryParse(CalendarDateInput.Text, out parsedDate))
        {
            CalendarErrorText.Text = "Invalid date format. Use yyyy-MM-dd.";
            return;
        }

        DemoCalendar.SelectedDate = parsedDate.Date;
        UpdateCalendarResultLabel();
    }

    private void OnClearCalendarDateClick(object? sender, RoutedEventArgs e)
    {
        CalendarErrorText.Text = string.Empty;
        DemoCalendar.SelectedDate = null;
        UpdateCalendarResultLabel();
    }

    private void UpdateHierarchySelection()
    {
        var selectedText = ResolveTreeSelectionText(DemoTree.SelectedItem as TreeViewItem);
        if (selectedText is null)
        {
            HierarchyResultLabel.Content = "No node selected";
            HierarchySelectionList.ItemsSource = Array.Empty<string>();
            return;
        }

        var path = ResolveTreePath(DemoTree.SelectedItem as TreeViewItem);
        HierarchyResultLabel.Content = selectedText;
        var items = BuildHierarchyRows(selectedText, path);
        HierarchySelectionList.ItemsSource = items;
    }

    private static string? ResolveTreeSelectionText(TreeViewItem? selected)
    {
        var header = selected?.Header;
        if (header is null)
        {
            return null;
        }

        return header.ToString();
    }

    private static IReadOnlyList<string> BuildHierarchyRows(string selectedText, IEnumerable<string> path)
    {
        var rows = new List<string> { selectedText, $"Path length: {path.Count()}" };

        var parentPath = path as string[] ?? path.ToArray();
        rows.AddRange(parentPath.Select((node, index) => $"{new string(' ', index * 2)}{node}"));

        return rows;
    }

    private void ClearMathError()
    {
        ErrorText.Text = string.Empty;
    }

    private void ClearSeriesError()
    {
        SeriesErrorText.Text = string.Empty;
    }

    private void UpdateCalendarResultLabel()
    {
        CalendarResultLabel.Content = DemoCalendar.SelectedDate is { } date
            ? $"Selected: {date:yyyy-MM-dd}"
            : "No date selected";
    }

    private void MixResultTextClear()
    {
        SeriesResult.Content = string.Empty;
        SeriesList.ItemsSource = Array.Empty<string>();
        SeriesProgressBar.Value = 0;
    }

    private static string BuildMathResultGcd(IReadOnlyList<long> numbers, out IReadOnlyList<string> stepLines)
    {
        var gcdComputation = GcdCalculator.ComputeGcdWithSteps(numbers);
        stepLines = BuildGcdStepLines(gcdComputation);
        return $"GCD = {gcdComputation.Result}";
    }

    private static string BuildMathResultLcm(IReadOnlyList<long> numbers, out IReadOnlyList<string> stepLines)
    {
        var lcm = ComputeLcmWithSteps(numbers, out var steps);
        stepLines = steps;
        return $"LCM = {lcm}";
    }

    private static string BuildMathResultMin(IReadOnlyList<long> numbers, out IReadOnlyList<string> stepLines)
    {
        var min = ComputeMinWithSteps(numbers, out var steps);
        stepLines = steps;
        return $"MIN = {min}";
    }

    private static IReadOnlyList<string> BuildSeriesLines(IReadOnlyList<long> values, bool includeDetails, bool useAdvanced)
    {
        if (!includeDetails)
        {
            return Array.Empty<string>();
        }

        var lines = new List<string> { useAdvanced ? "Advanced mode on" : "Advanced mode off" };
        for (var i = 0; i < values.Count; i++)
        {
            lines.Add($"v{i + 1}: {values[i]}");
        }

        return lines;
    }

    private static List<long> ParseOptionalNumbers(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var values = input.Split(InputSeparators, StringSplitOptions.RemoveEmptyEntries);

        var parsed = new List<long>();
        foreach (var value in values)
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                parsed.Add(number);
            }
        }

        return parsed;
    }

    private static IReadOnlyList<long> BuildSeries(
        SeriesMode mode,
        long seed,
        long secondSeed,
        int count,
        int speed,
        bool descending)
    {
        var values = mode switch
        {
            SeriesMode.Reversed => BuildArithmeticSeries(seed, speed, count),
            SeriesMode.Fibonacci => BuildFibonacciSeries(seed, secondSeed, speed, count),
            _ => BuildArithmeticSeries(seed, speed, count)
        };

        if (descending)
        {
            var descendingValues = values.ToArray();
            Array.Reverse(descendingValues);
            return descendingValues;
        }

        return values;
    }

    private static List<long> BuildArithmeticSeries(long seed, int step, int count)
    {
        var list = new List<long>();
        var current = seed;
        for (var i = 0; i < count; i++)
        {
            list.Add(current);
            current += step;
        }

        return list;
    }

    private static List<long> BuildFibonacciSeries(long seed, long secondSeed, int speed, int count)
    {
        var list = new List<long> { seed, secondSeed };
        if (count <= 2)
        {
            return list.Take(count).ToList();
        }

        while (list.Count < count)
        {
            var prev = list[^1];
            var prevPrev = list[^2];
            var next = prev + prevPrev + speed;
            list.Add(next);
        }

        return list.Take(count).ToList();
    }

    private ComputeMode ResolveComputeMode()
    {
        var selectedText = OperationCombo.SelectedItem as string ?? OperationCombo.SelectedItem?.ToString();

        return selectedText?.Trim().ToUpperInvariant() switch
        {
            "LCM" => ComputeMode.Lcm,
            "MIN" => ComputeMode.Min,
            _ => ComputeMode.Gcd
        };
    }

    private SeriesMode ResolveSeriesMode()
    {
        var selectedText = MixModeCombo.SelectedItem as string ?? MixModeCombo.SelectedItem?.ToString() ?? "Arithmetic";

        return selectedText.Trim() switch
        {
            "Reversed" => SeriesMode.Reversed,
            "Fibonacci" => SeriesMode.Fibonacci,
            _ => SeriesMode.Arithmetic
        };
    }

    private static IEnumerable<string> ResolveTreePath(TreeViewItem? node)
    {
        if (node is null)
        {
            return Array.Empty<string>();
        }

        var path = new List<string>();
        var parent = node.Parent as TreeViewItem;
        while (parent is not null)
        {
            path.Add(parent.Header?.ToString() ?? "");
            parent = parent.Parent as TreeViewItem;
        }

        path.Reverse();
        path.Add(node.Header?.ToString() ?? string.Empty);
        return path.Where(static segment => !string.IsNullOrWhiteSpace(segment));
    }

    private static string BuildHistoryEntry(ComputeMode mode, IReadOnlyList<long> numbers, string resultText, bool useAbsoluteValues, bool showSteps)
    {
        return $"{mode.ToString().ToUpperInvariant()} | Input: {string.Join(' ', numbers)} | Result: {resultText} | Absolute: {(useAbsoluteValues ? "On" : "Off")} | Steps: {(showSteps ? "On" : "Off")}";
    }

    private static int ParseInputAsNonNegativeInt(string? text, int defaultValue)
    {
        if (int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return Math.Max(1, value);
        }

        return defaultValue;
    }

    private void ApplyCurrentHistoryFilter()
    {
        var historyToShow = string.IsNullOrWhiteSpace(_historyFilter)
            ? _computationHistory.ToList()
            : _computationHistory.Where(item => item.Contains(_historyFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        HistoryList.ItemsSource = historyToShow;
    }

    private void UpdateModeLabel(ComputeMode mode, bool useAbsoluteValues, bool showSteps)
    {
        var modeText = mode switch
        {
            ComputeMode.Lcm => "LCM",
            ComputeMode.Min => "MIN",
            _ => "GCD"
        };

        ModeLabel.Content = $"Mode: {modeText} | Absolute: {(useAbsoluteValues ? "On" : "Off")} | Steps: {(showSteps ? "On" : "Off")}";
    }

    private static long ComputeMinWithSteps(IReadOnlyList<long> numbers, out IReadOnlyList<string> stepLines)
    {
        var lines = new List<string>();
        var min = numbers[0];

        lines.Add($"Input: {string.Join(", ", numbers)}");
        for (var i = 1; i < numbers.Count; i++)
        {
            var current = numbers[i];
            if (current < min)
            {
                lines.Add($"Step {i}: min changed from {min} to {current}");
                min = current;
            }
            else
            {
                lines.Add($"Step {i}: keep {min} over {current}");
            }
        }

        lines.Add($"Minimum = {min}");
        stepLines = lines;
        return min;
    }

    private static long ComputeLcmWithSteps(IReadOnlyList<long> numbers, out IReadOnlyList<string> stepLines)
    {
        if (numbers.Count == 0)
        {
            throw new ArgumentException("At least one number is required.", nameof(numbers));
        }

        if (numbers.All(number => number == 0))
        {
            throw new ArgumentException("LCM is undefined for all zeros.");
        }

        var lines = new List<string>();
        var current = numbers[0];
        lines.Add($"Input: {string.Join(", ", numbers)}");

        for (var i = 1; i < numbers.Count; i++)
        {
            var next = numbers[i];
            lines.Add($"Step {i}: lcm({current}, {next})");

            if (current == 0 || next == 0)
            {
                lines.Add("  One value is 0, so lcm is 0.");
                current = 0;
                continue;
            }

            var pairGcd = GcdCalculator.ComputeGcd(current, next);
            if (pairGcd == 0)
            {
                throw new ArgumentException("LCM is undefined for all zeros.");
            }

            var pairLcm = checked(Math.Abs(current / pairGcd) * Math.Abs(next));
            lines.Add($"  gcd({current}, {next}) = {pairGcd}");
            lines.Add($"  lcm({current}, {next}) = {pairLcm}");
            current = pairLcm;
        }

        stepLines = lines;
        return current;
    }

    private static bool TryParseNumbers(string? input, bool useAbsoluteValues, out IReadOnlyList<long> numbers, out string errorMessage)
    {
        var parsed = new List<long>();
        errorMessage = string.Empty;
        numbers = parsed;

        if (string.IsNullOrWhiteSpace(input))
        {
            errorMessage = "Provide at least one integer.";
            numbers = Array.Empty<long>();
            return false;
        }

        var tokens = input.Split(InputSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                errorMessage = $"Invalid integer: {token}";
                numbers = Array.Empty<long>();
                return false;
            }

            parsed.Add(useAbsoluteValues ? Math.Abs(value) : value);
        }

        if (parsed.Count == 0)
        {
            errorMessage = "Provide at least one integer.";
            numbers = Array.Empty<long>();
            return false;
        }

        numbers = parsed;
        return true;
    }

    private static IReadOnlyList<string> BuildGcdStepLines(GcdComputationResult computation)
    {
        var lines = new List<string>();

        for (var i = 0; i < computation.PairComputations.Count; i++)
        {
            var pair = computation.PairComputations[i];
            lines.Add($"Step {i + 1}: gcd({pair.Left}, {pair.Right}) = {pair.Result}");

            if (pair.Steps.Count == 0)
            {
                lines.Add("  No Euclidean divisions required.");
                continue;
            }

            foreach (var step in pair.Steps)
            {
                lines.Add($"  {step.Dividend} = {step.Divisor} * {step.Quotient} + {step.Remainder}");
            }
        }

        return lines;
    }

}
