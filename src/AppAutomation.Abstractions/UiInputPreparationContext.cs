namespace AppAutomation.Abstractions;

internal sealed record UiRegisteredNotification(UiControlDefinition RootDefinition, NotificationControlParts Parts);

// Resolver-call transport only. Providers capture the immutable snapshot before dispatch or queuing input.
internal sealed class UiInputPreparationContext
{
    private static readonly AsyncLocal<UiInputPreparationContext?> Ambient = new();
    private readonly UiInputPreparationContextSource _source;
    private readonly IReadOnlyList<UiControlDefinition> _definitions;
    private readonly IReadOnlyList<AdapterLayer> _layers;
    private readonly bool _recoveryDisabled;

    internal UiInputPreparationContext(UiInputPreparationContextSource source,
        IReadOnlyList<UiControlDefinition> definitions)
        : this(source, definitions, [], recoveryDisabled: false)
    {
    }

    private UiInputPreparationContext(UiInputPreparationContextSource source,
        IReadOnlyList<UiControlDefinition> definitions, IReadOnlyList<AdapterLayer> layers, bool recoveryDisabled)
    {
        _source = source;
        _definitions = definitions;
        _layers = layers;
        _recoveryDisabled = recoveryDisabled;
        Notifications = recoveryDisabled ? [] : BindNotifications(definitions, layers);
    }

    internal IReadOnlyList<UiRegisteredNotification> Notifications { get; }

    internal static UiInputPreparationContext? Capture() => Ambient.Value;

    internal static IDisposable Enter(UiInputPreparationContext? context)
    {
        var previous = Ambient.Value;
        Ambient.Value = context;
        return new ContextScope(previous);
    }

    internal static UiInputPreparationContext? ForOperation(UiInputPreparationContext? captured)
    {
        var current = Ambient.Value;
        return captured is not null && !captured._recoveryDisabled && current?._recoveryDisabled != true
            && (current is null || captured.HasSameOwner(current))
            ? captured
            : null;
    }

    internal bool HasSameOwner(UiInputPreparationContext other) => ReferenceEquals(_source, other._source);

    internal UiInputPreparationContext WithoutRecovery() => _recoveryDisabled
        ? this : new(_source, _definitions, _layers, recoveryDisabled: true);

    internal UiInputPreparationContext WithAdapters(object resolver, IReadOnlyList<IUiControlAdapter> adapters,
        UiControlDefinition? notificationDefinition = null)
    {
        var definitions = _definitions;
        if (notificationDefinition is not null && !definitions.Contains(notificationDefinition))
        {
            _source.AddDefinition(notificationDefinition);
            definitions = Array.AsReadOnly(definitions.Append(notificationDefinition).ToArray());
        }

        var layers = _layers;
        if (!layers.Any(layer => ReferenceEquals(layer.Resolver, resolver)))
        {
            layers = Array.AsReadOnly(layers.Append(new AdapterLayer(resolver, adapters)).ToArray());
        }

        return new UiInputPreparationContext(_source, definitions, layers, _recoveryDisabled);
    }

    private static IReadOnlyList<UiRegisteredNotification> BindNotifications(
        IReadOnlyList<UiControlDefinition> definitions, IReadOnlyList<AdapterLayer> layers)
    {
        var adapters = layers.SelectMany(static layer => layer.Adapters).ToArray();
        var notifications = adapters.OfType<NotificationControlAdapter>().ToArray();
        var result = new List<UiRegisteredNotification>();
        foreach (var propertyName in notifications.Select(static adapter => adapter.PropertyName).Distinct(StringComparer.Ordinal))
        {
            var roots = definitions.Where(definition => definition.PropertyName == propertyName).Distinct().ToArray();
            if (roots.Length == 0)
            {
                roots = notifications.Where(adapter => adapter.PropertyName == propertyName)
                    .Select(static adapter => adapter.RootDefinition).OfType<UiControlDefinition>().Distinct().ToArray();
            }

            UiRegisteredNotification? registration = null;
            var ambiguous = false;
            foreach (var root in roots)
            {
                var effective = adapters.FirstOrDefault(adapter => adapter.CanResolve(typeof(INotificationControl), root));
                if (effective is not NotificationControlAdapter notification)
                {
                    ambiguous = true;
                    break;
                }

                var candidate = new UiRegisteredNotification(notification.RootDefinition ?? root, notification.Parts);
                if (registration is not null && registration != candidate)
                {
                    ambiguous = true;
                    break;
                }
                registration = candidate;
            }

            if (!ambiguous && registration is not null && !result.Contains(registration)) result.Add(registration);
        }

        // Conflicting declarations of the same physical root cannot authorize an arbitrary dismiss part.
        var conflictingRoots = result.GroupBy(static registration => RootAddress.From(registration.RootDefinition))
            .Where(static group => group.Select(static registration => registration.Parts).Distinct().Count() > 1)
            .Select(static group => group.Key).ToHashSet();
        return Array.AsReadOnly(result.Where(registration => !conflictingRoots.Contains(RootAddress.From(registration.RootDefinition))).ToArray());
    }

    private sealed record RootAddress(string LocatorValue, UiLocatorKind LocatorKind, bool FallbackToName, UiControlScope? Scope)
    {
        internal static RootAddress From(UiControlDefinition definition) =>
            new(definition.LocatorValue, definition.LocatorKind, definition.FallbackToName, definition.Scope);
    }

    private sealed record AdapterLayer(object Resolver, IReadOnlyList<IUiControlAdapter> Adapters);

    private sealed class ContextScope(UiInputPreparationContext? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            Ambient.Value = previous;
            _disposed = true;
        }
    }
}

// Late discovery replaces immutable metadata for future calls of this Page/chain only.
internal sealed class UiInputPreparationContextSource
{
    private readonly object _sync = new();
    private IReadOnlyList<UiControlDefinition> _definitions;

    internal UiInputPreparationContextSource(IEnumerable<UiControlDefinition> definitions) =>
        _definitions = Array.AsReadOnly(definitions.Distinct().ToArray());

    internal UiInputPreparationContext Snapshot()
    {
        lock (_sync) return new UiInputPreparationContext(this, _definitions);
    }

    internal void AddDefinition(UiControlDefinition definition)
    {
        lock (_sync)
        {
            if (!_definitions.Contains(definition))
                _definitions = Array.AsReadOnly(_definitions.Append(definition).ToArray());
        }
    }
}
