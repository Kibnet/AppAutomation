namespace AppAutomation.Abstractions;

/// <summary>
/// Describes a provider-neutral grid once for Recorder and runtime adapters.
/// </summary>
public sealed record GridAutomationDefinition
{
    private GridAutomationDefinition(
        string pagePropertyName,
        string captureLocatorValue,
        string runtimeLocatorValue,
        UiLocatorKind locatorKind)
    {
        PagePropertyName = NormalizeRequired(pagePropertyName, nameof(pagePropertyName));
        CaptureLocatorValue = NormalizeRequired(captureLocatorValue, nameof(captureLocatorValue));
        RuntimeLocatorValue = NormalizeRequired(runtimeLocatorValue, nameof(runtimeLocatorValue));
        CaptureLocatorKind = locatorKind;
        RuntimeLocatorKind = locatorKind;
    }

    /// <summary>Gets the generated Page property name for the logical grid.</summary>
    public string PagePropertyName { get; private init; }

    /// <summary>Gets the locator of the visible grid used while recording.</summary>
    public string CaptureLocatorValue { get; private init; }

    /// <summary>Gets the locator resolved by Headless and FlaUI during playback.</summary>
    public string RuntimeLocatorValue { get; private init; }

    /// <summary>Gets the capture locator strategy.</summary>
    public UiLocatorKind CaptureLocatorKind { get; private init; }

    /// <summary>Gets the runtime locator strategy.</summary>
    public UiLocatorKind RuntimeLocatorKind { get; private init; }

    /// <summary>Gets whether the runtime locator may fall back to Name.</summary>
    public bool RuntimeFallbackToName { get; private init; }

    /// <summary>Gets the configured logical columns. Empty means native provider metadata is required.</summary>
    public IReadOnlyList<GridColumnDefinition> Columns { get; private init; } = Array.Empty<GridColumnDefinition>();

    /// <summary>Gets the ordered logical columns that identify one row.</summary>
    public IReadOnlyList<string> RowIdentityColumns { get; private init; } = Array.Empty<string>();

    /// <summary>Gets the structural DataContext paths used by non-native cell presenters.</summary>
    public GridCellContextDefinition CellContext { get; private init; } = GridCellContextDefinition.Default;

    /// <summary>Creates a definition whose locators are AutomationIds.</summary>
    public static GridAutomationDefinition ByAutomationIds(
        string pagePropertyName,
        string captureAutomationId,
        string runtimeAutomationId)
    {
        return new GridAutomationDefinition(
            pagePropertyName,
            captureAutomationId,
            runtimeAutomationId,
            UiLocatorKind.AutomationId);
    }

    /// <summary>Creates a definition with independently selectable capture and runtime locator strategies.</summary>
    public static GridAutomationDefinition ByLocators(
        string pagePropertyName,
        string captureLocatorValue,
        UiLocatorKind captureLocatorKind,
        string runtimeLocatorValue,
        UiLocatorKind runtimeLocatorKind,
        bool runtimeFallbackToName = false)
    {
        return new GridAutomationDefinition(
            pagePropertyName,
            captureLocatorValue,
            runtimeLocatorValue,
            captureLocatorKind)
        {
            RuntimeLocatorKind = runtimeLocatorKind,
            RuntimeFallbackToName = runtimeFallbackToName
        };
    }

    /// <summary>Returns a definition with validated logical columns.</summary>
    public GridAutomationDefinition WithColumns(params GridColumnDefinition[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var validated = ValidateColumns(columns);
        if (RowIdentityColumns.Count > 0)
        {
            var knownColumns = validated
                .Select(static column => column.LogicalName)
                .ToHashSet(StringComparer.Ordinal);
            var missing = RowIdentityColumns.Where(identity => !knownColumns.Contains(identity)).ToArray();
            if (missing.Length > 0)
            {
                throw new ArgumentException(
                    $"Grid row identity columns are not configured: {string.Join(", ", missing)}.",
                    nameof(columns));
            }
        }

        return this with { Columns = validated };
    }

    /// <summary>Returns a definition with an ordered single or composite row identity.</summary>
    public GridAutomationDefinition IdentifyRowsBy(params string[] logicalColumnNames)
    {
        ArgumentNullException.ThrowIfNull(logicalColumnNames);
        if (logicalColumnNames.Length == 0)
        {
            throw new ArgumentException("At least one grid row identity column is required.", nameof(logicalColumnNames));
        }

        var identities = NormalizeDistinct(logicalColumnNames, nameof(logicalColumnNames));
        if (Columns.Count > 0)
        {
            var knownColumns = Columns.Select(static column => column.LogicalName).ToHashSet(StringComparer.Ordinal);
            var missing = identities.Where(identity => !knownColumns.Contains(identity)).ToArray();
            if (missing.Length > 0)
            {
                throw new ArgumentException(
                    $"Grid row identity columns are not configured: {string.Join(", ", missing)}.",
                    nameof(logicalColumnNames));
            }
        }

        return this with { RowIdentityColumns = identities };
    }

    /// <summary>Returns a definition with provider-specific structural paths and no provider dependency.</summary>
    public GridAutomationDefinition WithCellContext(GridCellContextDefinition context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return this with { CellContext = context };
    }

    /// <summary>Finds a configured column by its logical generated-code name.</summary>
    public GridColumnDefinition? FindColumn(string logicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        return Columns.FirstOrDefault(column =>
            string.Equals(column.LogicalName, logicalName.Trim(), StringComparison.Ordinal));
    }

    /// <summary>Finds a configured column by the provider's source FieldName.</summary>
    public GridColumnDefinition? FindColumnBySourceField(string sourceFieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFieldName);
        return Columns.FirstOrDefault(column =>
            string.Equals(column.SourceFieldName, sourceFieldName.Trim(), StringComparison.Ordinal));
    }

    private static IReadOnlyList<GridColumnDefinition> ValidateColumns(
        IReadOnlyList<GridColumnDefinition> columns)
    {
        if (columns.Count == 0)
        {
            throw new ArgumentException("At least one grid column is required.", nameof(columns));
        }

        if (columns.Any(static column => column is null))
        {
            throw new ArgumentException("Grid columns cannot contain null entries.", nameof(columns));
        }

        var logicalDuplicates = columns
            .GroupBy(static column => column.LogicalName, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (logicalDuplicates is not null)
        {
            throw new ArgumentException(
                $"Grid logical column '{logicalDuplicates.Key}' is duplicated.",
                nameof(columns));
        }

        var sourceDuplicates = columns
            .GroupBy(static column => column.SourceFieldName, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (sourceDuplicates is not null)
        {
            throw new ArgumentException(
                $"Grid source field '{sourceDuplicates.Key}' is mapped more than once.",
                nameof(columns));
        }

        var crossCollision = columns
            .SelectMany((sourceColumn, sourceIndex) => columns
                .Select((logicalColumn, logicalIndex) =>
                    (sourceColumn, sourceIndex, logicalColumn, logicalIndex)))
            .FirstOrDefault(candidate =>
                candidate.sourceIndex != candidate.logicalIndex
                && string.Equals(
                    candidate.sourceColumn.SourceFieldName,
                    candidate.logicalColumn.LogicalName,
                    StringComparison.Ordinal));
        if (crossCollision.sourceColumn is not null)
        {
            throw new ArgumentException(
                $"Grid name '{crossCollision.sourceColumn.SourceFieldName}' is both the source field of logical column "
                + $"'{crossCollision.sourceColumn.LogicalName}' and the logical name of source field "
                + $"'{crossCollision.logicalColumn.SourceFieldName}'. Logical and source column names must not cross-collide.",
                nameof(columns));
        }

        return Array.AsReadOnly(columns.ToArray());
    }

    private static IReadOnlyList<string> NormalizeDistinct(IEnumerable<string> values, string parameterName)
    {
        var normalized = values
            .Select(value => NormalizeRequired(value, parameterName))
            .ToArray();
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw new ArgumentException("Grid names must be distinct.", parameterName);
        }

        return Array.AsReadOnly(normalized);
    }

    internal static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
