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
    private enum ComputeMode
    {
        Gcd,
        Lcm,
        Min
    }

    private static readonly char[] InputSeparators = [' ', '\t', '\r', '\n', ',', ';'];
    private readonly List<string> _computationHistory = [];
    private string _historyFilter = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        ModeLabel.Content = "Mode: GCD | Absolute: Off | Steps: Off";
    }

    private void OnCalculateClick(object? sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
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
            string resultText;
            IReadOnlyList<string> stepLines;

            switch (computeMode)
            {
                case ComputeMode.Gcd:
                    var gcdComputation = GcdCalculator.ComputeGcdWithSteps(numbers);
                    resultText = $"GCD = {gcdComputation.Result}";
                    stepLines = BuildGcdStepLines(gcdComputation);
                    break;
                case ComputeMode.Lcm:
                    var lcmResult = ComputeLcmWithSteps(numbers, out stepLines);
                    resultText = $"LCM = {lcmResult}";
                    break;
                case ComputeMode.Min:
                    var minResult = ComputeMinWithSteps(numbers, out stepLines);
                    resultText = $"MIN = {minResult}";
                    break;
                default:
                    throw new InvalidOperationException("Unsupported operation mode.");
            }

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

    private static string BuildHistoryEntry(ComputeMode mode, IReadOnlyList<long> numbers, string resultText, bool useAbsoluteValues, bool showSteps)
    {
        return $"{mode.ToString().ToUpperInvariant()} | Input: {string.Join(' ', numbers)} | Result: {resultText} | Absolute: {(useAbsoluteValues ? "On" : "Off")} | Steps: {(showSteps ? "On" : "Off")}";
    }

    private void ApplyCurrentHistoryFilter()
    {
        var historyToShow = string.IsNullOrWhiteSpace(_historyFilter)
            ? _computationHistory.ToList()
            : _computationHistory.Where(item => item.Contains(_historyFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        HistoryList.ItemsSource = historyToShow;
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
