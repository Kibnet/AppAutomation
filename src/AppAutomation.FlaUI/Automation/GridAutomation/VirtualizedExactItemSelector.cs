using System.Diagnostics;

namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal static class VirtualizedExactItemSelector
{
    public static void Select<TCandidate, TContainer>(
        string expectedText,
        TimeSpan timeout,
        Func<IReadOnlyList<TCandidate>> readCandidates,
        Func<TCandidate, string?> readCandidateText,
        Func<TCandidate, TContainer> resolveContainer,
        Func<TContainer, string?> readContainerText,
        Func<TContainer, string> getContainerIdentity,
        Action<TContainer> scrollIntoView,
        Func<TContainer, bool> trySelect,
        Action<TContainer> click)
        where TContainer : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedText);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(readCandidates);
        ArgumentNullException.ThrowIfNull(readCandidateText);
        ArgumentNullException.ThrowIfNull(resolveContainer);
        ArgumentNullException.ThrowIfNull(readContainerText);
        ArgumentNullException.ThrowIfNull(getContainerIdentity);
        ArgumentNullException.ThrowIfNull(scrollIntoView);
        ArgumentNullException.ThrowIfNull(trySelect);
        ArgumentNullException.ThrowIfNull(click);

        var normalizedTarget = Normalize(expectedText);
        var stopwatch = Stopwatch.StartNew();
        string? previousSignature = null;
        string? scrolledContainerIdentity = null;
        var snapshotCount = 0;
        var lastCandidateCount = 0;
        var lastMatchCount = 0;
        var lastCandidateTexts = string.Empty;
        long lastSnapshotElapsedMilliseconds = 0;
        do
        {
            var snapshot = ReadSnapshot();
            var matches = snapshot.Matches;
            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Search-picker item '{expectedText}' is ambiguous in the open grid editor.");
            }

            if (matches.Length == 1
                && string.Equals(previousSignature, snapshot.Signature, StringComparison.Ordinal))
            {
                var match = matches[0];
                var matchIdentity = getContainerIdentity(match);
                if (!string.Equals(scrolledContainerIdentity, matchIdentity, StringComparison.Ordinal))
                {
                    scrollIntoView(match);
                    scrolledContainerIdentity = matchIdentity;
                    previousSignature = null;
                }
                else if (IsExactMatch(match))
                {
                    if (trySelect(match))
                    {
                        return;
                    }

                    var finalSnapshot = ReadSnapshot();
                    if (finalSnapshot.Matches.Length > 1)
                    {
                        throw new InvalidOperationException(
                            $"Search-picker item '{expectedText}' is ambiguous in the open grid editor.");
                    }

                    if (finalSnapshot.Matches.Length == 1
                        && string.Equals(snapshot.Signature, finalSnapshot.Signature, StringComparison.Ordinal)
                        && IsExactMatch(finalSnapshot.Matches[0]))
                    {
                        click(finalSnapshot.Matches[0]);
                        return;
                    }

                    previousSignature = finalSnapshot.Signature;
                    scrolledContainerIdentity = null;
                }
            }
            else
            {
                previousSignature = snapshot.Signature;
            }

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                Thread.Sleep((int)Math.Min(25, remaining.TotalMilliseconds));
            }
        }
        while (stopwatch.Elapsed < timeout);

        throw new InvalidOperationException(
            $"Search-picker item '{expectedText}' was not found in the open grid editor within {timeout.TotalMilliseconds:0} ms. "
            + $"Snapshots: {snapshotCount}; last candidates: {lastCandidateCount}; "
            + $"last exact matches: {lastMatchCount}; last texts: [{lastCandidateTexts}]; "
            + $"last snapshot: {lastSnapshotElapsedMilliseconds} ms.");

        SelectionSnapshot<TContainer> ReadSnapshot()
        {
            var snapshotStartedAt = stopwatch.ElapsedMilliseconds;
            var candidates = readCandidates();
            var candidateValues = candidates
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Text = readCandidateText(candidate)
                })
                .ToArray();
            var containers = candidateValues
                .Select(static candidate => candidate.Candidate)
                .Select(resolveContainer)
                .DistinctBy(getContainerIdentity)
                .ToArray();
            var matches = candidateValues
                .Where(candidate => string.Equals(
                    Normalize(candidate.Text),
                    normalizedTarget,
                    StringComparison.OrdinalIgnoreCase))
                .Select(static candidate => candidate.Candidate)
                .Select(resolveContainer)
                .DistinctBy(getContainerIdentity)
                .Where(IsExactMatch)
                .Take(2)
                .ToArray();
            snapshotCount++;
            lastCandidateCount = candidates.Count;
            lastMatchCount = matches.Length;
            lastCandidateTexts = string.Join(
                ", ",
                candidateValues
                    .Select(static candidate => Normalize(candidate.Text))
                    .Where(static text => text.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .Take(10));
            lastSnapshotElapsedMilliseconds = stopwatch.ElapsedMilliseconds - snapshotStartedAt;
            var signature = string.Join(
                '\u001e',
                containers.Select(container => string.Join(
                    '\u001f',
                    getContainerIdentity(container),
                    Normalize(readContainerText(container)))));
            return new SelectionSnapshot<TContainer>(signature, matches);
        }

        bool IsExactMatch(TContainer container)
        {
            return string.Equals(
                Normalize(readContainerText(container)),
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private sealed record SelectionSnapshot<TContainer>(string Signature, TContainer[] Matches);
}
