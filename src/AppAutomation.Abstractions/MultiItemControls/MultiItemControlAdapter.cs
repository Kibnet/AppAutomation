namespace AppAutomation.Abstractions;

/// <summary>Adapts one generated Page property to a provider-scoped repeated-control collection.</summary>
public sealed class MultiItemControlAdapter : IUiControlAdapter
{
    private readonly MultiItemControlDefinition _definition;

    public MultiItemControlAdapter(MultiItemControlDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _definition.Validate();
    }

    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        return requestedType == typeof(IMultiItemControlCollection)
            && definition.ControlType == UiControlType.MultiItemControlCollection
            && string.Equals(definition.PropertyName, _definition.PagePropertyName, StringComparison.Ordinal);
    }

    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        if (innerResolver is not IMultiItemControlRuntimeResolver runtimeResolver)
        {
            throw new NotSupportedException(
                $"Runtime adapter '{innerResolver.Capabilities.AdapterId}' does not support multi-item controls.");
        }

        return new ConfiguredMultiItemControlCollection(_definition, runtimeResolver);
    }

    private sealed class ConfiguredMultiItemControlCollection : IMultiItemControlCollection
    {
        private readonly MultiItemControlDefinition _definition;
        private readonly IMultiItemControlRuntimeResolver _runtimeResolver;

        public ConfiguredMultiItemControlCollection(
            MultiItemControlDefinition definition,
            IMultiItemControlRuntimeResolver runtimeResolver)
        {
            _definition = definition;
            _runtimeResolver = runtimeResolver;
        }

        public string AutomationId => _definition.RuntimeLocatorValue;

        public string Name => _definition.PagePropertyName;

        public bool IsEnabled => _runtimeResolver.ReadMultiItemCollectionState(_definition).IsEnabled;

        public bool IsAvailable => _runtimeResolver.ReadMultiItemCollectionState(_definition).IsAvailable;

        public ISpinnerControl ResolveSpinner(string itemKey, int timeoutMs)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemKey);
            return _runtimeResolver.ResolveMultiItemSpinner(_definition, itemKey.Trim(), timeoutMs);
        }
    }
}
