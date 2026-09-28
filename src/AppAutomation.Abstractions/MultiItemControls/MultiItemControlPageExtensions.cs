using System.Globalization;
using System.Linq.Expressions;

namespace AppAutomation.Abstractions;

public static partial class UiPageExtensions
{
    /// <summary>Sets a Spinner value in the collection item identified by a stable key.</summary>
    public static TSelf SetMultiItemSpinnerValue<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IMultiItemControlCollection>> selector,
        string itemKey,
        double value,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKey);
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Spinner value must be finite.");
        }

        var budget = UiOperationTimeoutBudget.Start(timeoutMs, "multi-item spinner");
        var collection = Resolve(selector, page);
        var normalizedKey = itemKey.Trim();
        var spinner = collection.ResolveSpinner(normalizedKey, budget.RemainingMilliseconds);
        if (!IsReady(spinner))
        {
            WaitUntil(
                page,
                selector,
                () =>
                {
                    spinner = collection.ResolveSpinner(
                        normalizedKey,
                        Math.Max(1, budget.RemainingMilliseconds));
                    return IsReady(spinner);
                },
                budget.RemainingMilliseconds,
                $"Spinner for item '{itemKey}' in collection '{collection.AutomationId}' is not enabled.",
                expectedValue: "IsEnabled=true",
                lastObservedValueFactory: () => $"IsEnabled={spinner.IsEnabled}");
        }

        spinner!.Value = value;

        WaitUntil(
            page,
            selector,
            () =>
            {
                var current = collection.ResolveSpinner(normalizedKey, Math.Max(1, budget.RemainingMilliseconds));
                return SpinnerValuesEqual(current.Value, value);
            },
            budget.RemainingMilliseconds,
            $"Spinner for item '{itemKey}' in collection '{collection.AutomationId}' did not reach expected value.",
            expectedValue: value.ToString(CultureInfo.InvariantCulture),
            lastObservedValueFactory: () =>
            {
                var current = collection.ResolveSpinner(normalizedKey, Math.Max(1, budget.RemainingMilliseconds));
                return current.Value.ToString(CultureInfo.InvariantCulture);
            });
        return page;

        static bool IsReady(ISpinnerControl candidate) => candidate.IsEnabled;
    }
}
