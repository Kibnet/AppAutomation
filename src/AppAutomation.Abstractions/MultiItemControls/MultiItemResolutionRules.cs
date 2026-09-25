namespace AppAutomation.Abstractions;

internal sealed record MultiItemKeyCandidate<TItem>(TItem Item, string? Key)
    where TItem : class;

internal sealed record MultiItemKeyResolution<TItem>(
    TItem? Item,
    string? Key,
    string? Error,
    int MatchCount,
    bool IsTransient)
    where TItem : class
{
    public bool Success => Item is not null && Error is null;
}

internal sealed class MultiItemResolutionException : InvalidOperationException
{
    public MultiItemResolutionException(string message, bool isTransient)
        : base(message)
    {
        IsTransient = isTransient;
    }

    public bool IsTransient { get; }
}

internal static class MultiItemResolutionRules
{
    public static TResult ResolveWithRetry<TResult>(
        MultiItemControlDefinition definition,
        string itemKey,
        int timeoutMs,
        Func<TResult> resolve)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKey);
        ArgumentNullException.ThrowIfNull(resolve);

        var budget = UiOperationTimeoutBudget.Start(timeoutMs, "multi-item spinner resolution");
        MultiItemResolutionException? lastFailure = null;
        while (true)
        {
            try
            {
                _ = budget.RemainingMilliseconds;
            }
            catch (TimeoutException timeout) when (lastFailure is not null)
            {
                throw Timeout(definition, itemKey, lastFailure, timeout);
            }

            try
            {
                return resolve();
            }
            catch (MultiItemResolutionException exception) when (exception.IsTransient)
            {
                lastFailure = exception;
                try
                {
                    Thread.Sleep(Math.Min(50, budget.RemainingMilliseconds));
                }
                catch (TimeoutException timeout)
                {
                    throw Timeout(definition, itemKey, lastFailure, timeout);
                }
            }
        }
    }

    public static MultiItemKeyResolution<TItem> ResolveRequestedItem<TItem>(
        IReadOnlyList<MultiItemKeyCandidate<TItem>> candidates,
        string requestedKey)
        where TItem : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedKey);

        var keySetError = ValidateKeySet(candidates);
        if (keySetError is not null)
        {
            return keySetError;
        }

        var normalizedKey = requestedKey.Trim();
        var matches = candidates
            .Where(candidate => string.Equals(candidate.Key, normalizedKey, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1
            ? new MultiItemKeyResolution<TItem>(matches[0].Item, matches[0].Key, null, 1, false)
            : new MultiItemKeyResolution<TItem>(
                null,
                normalizedKey,
                $"Requested stable item key was not found among {candidates.Count} item container(s).",
                matches.Length,
                IsTransient: matches.Length == 0);
    }

    public static MultiItemKeyResolution<TItem> ResolveSelectedItem<TItem>(
        IReadOnlyList<MultiItemKeyCandidate<TItem>> candidates,
        TItem selectedItem)
        where TItem : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(selectedItem);

        var keySetError = ValidateKeySet(candidates);
        if (keySetError is not null)
        {
            return keySetError;
        }

        var match = candidates.SingleOrDefault(candidate => ReferenceEquals(candidate.Item, selectedItem));
        return match is not null
            ? new MultiItemKeyResolution<TItem>(match.Item, match.Key, null, 1, false)
            : new MultiItemKeyResolution<TItem>(
                null,
                null,
                "The selected repeated item does not expose a stable key.",
                0,
                IsTransient: false);
    }

    public static MultiItemResolutionException Failure(
        MultiItemControlDefinition definition,
        string itemKey,
        string partName,
        MultiItemRelativeLocator? locator,
        int matchCount,
        string reason,
        bool isTransient)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var locatorText = locator is null
            ? "item root"
            : $"{locator.Scope}/{locator.LocatorKind}:{locator.LocatorValue}";
        return new MultiItemResolutionException(
            $"Multi-item collection '{definition.PagePropertyName}' "
            + $"[{definition.RuntimeLocatorKind}:{definition.RuntimeLocatorValue}] could not resolve {partName} "
            + $"for item key '{itemKey}'. Locator: {locatorText}; matches={matchCount}. {reason} "
            + "Update the corresponding MultiItemControlCatalog declaration.",
            isTransient);
    }

    private static MultiItemKeyResolution<TItem>? ValidateKeySet<TItem>(
        IReadOnlyList<MultiItemKeyCandidate<TItem>> candidates)
        where TItem : class
    {
        var missingKeys = candidates.Count(static candidate => string.IsNullOrWhiteSpace(candidate.Key));
        if (missingKeys > 0)
        {
            return new MultiItemKeyResolution<TItem>(
                null,
                null,
                $"{missingKeys} item container(s) expose an empty stable key.",
                missingKeys,
                IsTransient: false);
        }

        var duplicate = candidates
            .GroupBy(static candidate => candidate.Key!.Trim(), StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            return new MultiItemKeyResolution<TItem>(
                null,
                duplicate.Key,
                $"Stable item key '{duplicate.Key}' is exposed by multiple item containers.",
                duplicate.Count(),
                IsTransient: false);
        }

        return null;
    }

    public static TimeoutException Timeout(
        MultiItemControlDefinition definition,
        string itemKey,
        Exception lastFailure,
        TimeoutException timeout)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(lastFailure);
        ArgumentNullException.ThrowIfNull(timeout);
        return new TimeoutException(
            $"Multi-item collection '{definition.PagePropertyName}' did not resolve Spinner for item key "
            + $"'{itemKey}' within the operation timeout. Last observed: {lastFailure.Message}",
            timeout);
    }
}
