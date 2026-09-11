namespace AppAutomation.Abstractions;

/// <summary>
/// Adds stable row addressing and semantic column metadata to one runtime grid.
/// </summary>
public sealed class GridAutomationAdapter : IUiControlAdapter
{
    private readonly GridAutomationDefinition _definition;
    private readonly string _catalogFingerprint;

    public GridAutomationAdapter(GridAutomationDefinition definition)
        : this(
            definition,
            new GridAutomationCatalog()
                .Add(definition ?? throw new ArgumentNullException(nameof(definition)))
                .Fingerprint)
    {
    }

    internal GridAutomationAdapter(
        GridAutomationDefinition definition,
        string catalogFingerprint)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _catalogFingerprint = string.IsNullOrWhiteSpace(catalogFingerprint)
            ? throw new ArgumentException("Grid catalog fingerprint cannot be empty.", nameof(catalogFingerprint))
            : catalogFingerprint;
    }

    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);

        return (requestedType == typeof(IGridControl)
                || requestedType == typeof(IAddressableGridControl))
            && string.Equals(
                definition.PropertyName,
                _definition.PagePropertyName,
                StringComparison.Ordinal);
    }

    public object Resolve(
        Type requestedType,
        UiControlDefinition definition,
        IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        var runtimeDefinition = definition with
        {
            LocatorValue = _definition.RuntimeLocatorValue,
            LocatorKind = _definition.RuntimeLocatorKind,
            FallbackToName = _definition.RuntimeFallbackToName
        };
        var grid = innerResolver.Resolve<IGridControl>(runtimeDefinition);
        var columns = GridAutomationColumnResolver.Resolve(grid, _definition);

        return grid switch
        {
            IGridUserActionControl actionGrid when grid is IEditableGridControl editableGrid =>
                new ConfiguredActionEditableGridControl(
                    grid,
                    actionGrid,
                    editableGrid,
                    _definition,
                    columns,
                    _catalogFingerprint),
            IGridUserActionControl actionGrid =>
                new ConfiguredActionGridControl(
                    grid,
                    actionGrid,
                    _definition,
                    columns,
                    _catalogFingerprint),
            IEditableGridControl editableGrid =>
                new ConfiguredEditableGridControl(
                    grid,
                    editableGrid,
                    _definition,
                    columns,
                    _catalogFingerprint),
            _ => new ConfiguredGridControl(
                grid,
                editableGrid: null,
                actionGrid: null,
                _definition,
                columns,
                _catalogFingerprint)
        };
    }
}
