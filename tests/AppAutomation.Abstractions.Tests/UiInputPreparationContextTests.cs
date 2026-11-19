using System.Collections.Concurrent;
using System.Text.Json;
using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class UiInputPreparationContextTests
{
    [Test]
    public async Task AttributeMetadataReachesOpaqueForwardingResolverBeforeNotificationResolve()
    {
        var provider = new CapturingResolver();
        var parts = NotificationControlParts.ByAutomationIds("NeutralText", "NeutralDismiss");
        var page = new AttributePage(new OpaqueForwardingResolver(provider.WithNotification("Toast", parts)));

        _ = page.Action;

        await Assert.That(provider.CapturedContext).IsNotNull();
        await Assert.That(provider.CapturedContext!.Notifications.Count).IsEqualTo(1);
        await Assert.That(provider.CapturedContext.Notifications[0].RootDefinition.LocatorValue).IsEqualTo("NeutralToastRoot");
        await Assert.That(provider.CapturedContext.Notifications[0].Parts).IsEqualTo(parts);
        await Assert.That(provider.ResolveCount).IsEqualTo(1);
        await Assert.That(UiInputPreparationContext.Capture()).IsNull();
    }

    [Test]
    public async Task FullRootRegistrationIsEagerAndUsesTheSameScopedRootForOrdinaryNotificationReads()
    {
        var provider = new CapturingResolver();
        var root = Root("Toast", "ConfiguredRoot", "OwningScope");
        var parts = Parts("Configured");
        var page = new ManualPage(provider.WithNotification(root, parts), Root("Toast", "UnusedRoot", "OtherScope"));

        _ = page.Action;
        var registration = provider.CapturedContext!.Notifications.Single();
        await Assert.That(registration.RootDefinition).IsEqualTo(root);
        await Assert.That(registration.Parts).IsEqualTo(parts);
        await Assert.That(page.Toast.Text).IsEqualTo("Notification text");
        await Assert.That(provider.ReadNotificationRoot).IsEqualTo(root);
        await Assert.That(UiInputPreparationContext.Capture()).IsNull();
    }

    [Test]
    public async Task OldStringRegistrationDiscoversRootsOnlyOnResolveAndKeepsCapturedAndOtherPageSnapshotsUnchanged()
    {
        var provider = new CapturingResolver();
        var resolver = provider.WithNotification("Toast", Parts("Late"));
        var page = new ManualPage(resolver, Root("Toast", "LateRoot", "ScopeA"));
        var otherPage = new ManualPage(resolver, Root("Toast", "DifferentRoot", "ScopeB"));
        var oldControl = (Button)page.Action;
        await Assert.That(oldControl.Context!.Notifications.Count).IsEqualTo(0);

        _ = page.Toast;
        _ = page.Action;
        await Assert.That(provider.CapturedContext!.Notifications.Single().RootDefinition.LocatorValue).IsEqualTo("LateRoot");
        await Assert.That(oldControl.Context.Notifications.Count).IsEqualTo(0);

        _ = otherPage.Action;
        await Assert.That(provider.CapturedContext!.Notifications.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task EffectiveFirstMatchPrecedenceIncludesCustomShadowingAcrossNestedLayers(bool customIsOuter)
    {
        var provider = new CapturingResolver();
        var root = Root("Toast", "RegisteredRoot", "Scope");
        IUiControlResolver resolver = customIsOuter
            ? provider.WithNotification(root, Parts("BuiltIn")).WithAdapters(new NotificationShadow())
            : provider.WithAdapters(new NotificationShadow()).WithNotification(root, Parts("BuiltIn"));
        var page = new ManualPage(new OpaqueForwardingResolver(resolver), root);

        _ = page.Action;

        await Assert.That(provider.CapturedContext!.Notifications.Count).IsEqualTo(customIsOuter ? 0 : 1);
    }

    [Test]
    public async Task ConflictingDismissPartsForTheSameScopedRootDoNotAuthorizeEitherAlias()
    {
        var provider = new CapturingResolver();
        var resolver = provider
            .WithNotification(Root("ToastA", "SharedRoot", "ScopeA"), Parts("First"))
            .WithNotification(Root("ToastB", "SharedRoot", "ScopeA"), Parts("Second"))
            .WithNotification(Root("ToastC", "SharedRoot", "ScopeB"), Parts("Third"));
        var page = new ManualPage(resolver, Root("ToastA", "SharedRoot", "ScopeA"));

        _ = page.Action;

        await Assert.That(provider.CapturedContext!.Notifications.Count).IsEqualTo(1);
        await Assert.That(provider.CapturedContext.Notifications[0].RootDefinition.Scope!.LocatorValue).IsEqualTo("ScopeB");
    }

    [Test]
    public async Task DeferredPartsRetainTheirCompositeContextAndRejectAnUnrelatedActivePageContext()
    {
        var provider = new CapturingResolver();
        var page = new AttributePage(new OpaqueForwardingResolver(provider.WithNotification("Toast", Parts("A")))
            .WithAdapters(new DeferredLabelAdapter()));
        var deferred = page.Deferred;
        await Assert.That(provider.ResolveCount).IsEqualTo(0);
        await Assert.That(deferred.Text).IsEqualTo("Child text");
        var capturedA = provider.CapturedContext!;
        await Assert.That(capturedA.Notifications.Single().RootDefinition.LocatorValue).IsEqualTo("NeutralToastRoot");

        var pageB = new ManualPage(provider.WithNotification(Root("Toast", "OtherRoot", "ScopeB"), Parts("B")),
            Root("Toast", "OtherRoot", "ScopeB"));
        _ = pageB.Action;
        var capturedB = provider.CapturedContext!;
        using (UiInputPreparationContext.Enter(capturedB))
        {
            await Assert.That(UiInputPreparationContext.ForOperation(capturedA)).IsNull();
            await Assert.That(UiInputPreparationContext.ForOperation(null)).IsNull();
            _ = deferred.Text;
            await Assert.That(provider.CapturedContext!.Notifications.Count).IsEqualTo(0);
            await Assert.That(ReferenceEquals(UiInputPreparationContext.Capture(), capturedB)).IsTrue();
        }
        await Assert.That(ReferenceEquals(UiInputPreparationContext.ForOperation(capturedA), capturedA)).IsTrue();
        var disabled = capturedA.WithoutRecovery();
        await Assert.That(UiInputPreparationContext.ForOperation(disabled)).IsNull();
        using (UiInputPreparationContext.Enter(disabled))
        {
            await Assert.That(UiInputPreparationContext.ForOperation(capturedA)).IsNull();
        }
        await Assert.That(UiInputPreparationContext.Capture()).IsNull();
    }

    [Test]
    public async Task ParallelOpaquePointerCallsCaptureSeparatePageContextsBeforeAsyncWork()
    {
        var provider = new CapturingResolver();
        var rootA = Root("Toast", "RootA", "ScopeA");
        var rootB = Root("Toast", "RootB", "ScopeB");
        var pageA = new ManualPage(new OpaqueForwardingResolver(provider.WithNotification(rootA, Parts("A"))), rootA);
        var pageB = new ManualPage(new OpaqueForwardingResolver(provider.WithNotification(rootB, Parts("B"))), rootB);
        var target = new UiControlDefinition("Action", UiControlType.Button, "Action");

        await Task.WhenAll(pageA.PointerClickAsync(target), pageB.PointerClickAsync(target));

        var snapshots = provider.PointerContexts.ToArray();
        await Assert.That(snapshots.Length).IsEqualTo(2);
        await Assert.That(string.Join("|", snapshots.Select(snapshot => snapshot!.Notifications.Single().RootDefinition.LocatorValue)
            .Order(StringComparer.Ordinal))).IsEqualTo("RootA|RootB");
        await Assert.That(snapshots[0]!.HasSameOwner(snapshots[1]!)).IsFalse();
        await Assert.That(UiInputPreparationContext.Capture()).IsNull();
    }

    [Test]
    public async Task FailedResolutionRestoresScopeAndDoesNotChangePublicDefinitionValueIdentity()
    {
        var provider = new CapturingResolver();
        var root = Root("Toast", "UnchangedRoot", "UnchangedScope");
        var json = JsonSerializer.Serialize(root);
        var hash = root.GetHashCode();
        var page = new ManualPage(provider.WithNotification(root, Parts("A")), root);
        _ = page.Action;
        var previous = provider.CapturedContext;
        provider.ThrowOnResolve = true;
        using (UiInputPreparationContext.Enter(previous))
        {
            await Assert.That(() => page.Action).Throws<InvalidOperationException>();
            await Assert.That(ReferenceEquals(UiInputPreparationContext.Capture(), previous)).IsTrue();
        }

        await Assert.That(root.Equals(root with { })).IsTrue();
        await Assert.That(root.GetHashCode()).IsEqualTo(hash);
        await Assert.That(JsonSerializer.Serialize(root)).IsEqualTo(json);
        await Assert.That(UiInputPreparationContext.Capture()).IsNull();
    }

    private static UiControlDefinition Root(string propertyName, string locator, string scope) =>
        new(propertyName, UiControlType.AutomationElement, locator, UiLocatorKind.AutomationId, FallbackToName: false)
        { Scope = new UiControlScope(scope, UiLocatorKind.Name, FallbackToName: false) };

    private static NotificationControlParts Parts(string prefix) => NotificationControlParts.ByAutomationIds(prefix + "Text", prefix + "Dismiss");

    [UiControl("Toast", UiControlType.Notification, "NeutralToastRoot", FallbackToName = false)]
    private sealed class AttributePage(IUiControlResolver resolver) : UiPage(resolver)
    {
        public IButtonControl Action => Resolve<IButtonControl>(new("Action", UiControlType.Button, "Action"));
        public ILabelControl Deferred => Resolve<ILabelControl>(new("Deferred", UiControlType.Label, "Deferred"));
        public INotificationControl Toast => throw new InvalidOperationException("Metadata discovery must not invoke this property.");
    }

    private sealed class ManualPage(IUiControlResolver resolver, UiControlDefinition root) : UiPage(resolver)
    {
        public IButtonControl Action => Resolve<IButtonControl>(new("Action", UiControlType.Button, "Action"));
        public INotificationControl Toast => Resolve<INotificationControl>(root);
        public static UiControlDefinition ComputedMetadata => throw new InvalidOperationException("Metadata discovery must not invoke static getters.");
    }

    // Deliberately forwards only the public capability; no internal context interface is available here.
    private sealed class OpaqueForwardingResolver(IUiControlResolver inner) : IUiControlResolver, IUiPointerRuntime
    {
        public UiRuntimeCapabilities Capabilities => inner.Capabilities;
        public TControl Resolve<TControl>(UiControlDefinition definition) where TControl : class =>
            inner.Resolve<TControl>(definition with { Scope = definition.Scope });

        public async Task PointerClickAsync(UiControlDefinition target, PointerClickOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            await ((IUiPointerRuntime)inner).PointerClickAsync(target with { Scope = target.Scope }, options, cancellationToken);
        }
        public Task HoverAsync(UiControlDefinition target, Func<CancellationToken, Task> verifyWhileHovered,
            PointerHoverOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DragAndDropAsync(UiControlDefinition source, UiControlDefinition target,
            PointerDragOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingResolver : IUiControlResolver, INotificationRuntimeResolver, IUiPointerRuntime
    {
        public UiRuntimeCapabilities Capabilities { get; } = new("input-preparation-contract-test") { SupportsPointerInput = true };
        public UiInputPreparationContext? CapturedContext { get; private set; }
        public int ResolveCount { get; private set; }
        public UiControlDefinition? ReadNotificationRoot { get; private set; }
        public bool ThrowOnResolve { get; set; }
        public ConcurrentQueue<UiInputPreparationContext?> PointerContexts { get; } = new();

        public TControl Resolve<TControl>(UiControlDefinition definition) where TControl : class
        {
            CapturedContext = UiInputPreparationContext.Capture();
            ResolveCount++;
            if (ThrowOnResolve) throw new InvalidOperationException("Expected resolver failure.");
            object control = typeof(TControl) == typeof(ILabelControl) ? new Label() : new Button(CapturedContext);
            return control as TControl ?? throw new NotSupportedException();
        }

        public IReadOnlyList<NotificationRuntimeSnapshot>? ReadNotifications(UiControlDefinition rootDefinition, NotificationControlParts parts)
        {
            ReadNotificationRoot = rootDefinition;
            CapturedContext = UiInputPreparationContext.Capture();
            return [new("Notification text", true, static () => { })];
        }
        public async Task PointerClickAsync(UiControlDefinition target, PointerClickOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var captured = UiInputPreparationContext.Capture();
            await Task.Yield();
            PointerContexts.Enqueue(captured);
        }
        public Task HoverAsync(UiControlDefinition target, Func<CancellationToken, Task> verifyWhileHovered,
            PointerHoverOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DragAndDropAsync(UiControlDefinition source, UiControlDefinition target,
            PointerDragOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Button(UiInputPreparationContext? context) : IButtonControl
    {
        public UiInputPreparationContext? Context { get; } = context;
        public string AutomationId => "Action";
        public string Name => AutomationId;
        public bool IsEnabled => true;
        public void Invoke() { }
    }

    private sealed class Label : ILabelControl
    {
        public string AutomationId => "Child";
        public string Name => AutomationId;
        public bool IsEnabled => true;
        public string Text => "Child text";
    }

    private sealed class NotificationShadow : IUiControlAdapter
    {
        public bool CanResolve(Type requestedType, UiControlDefinition definition) =>
            requestedType == typeof(INotificationControl) && definition.PropertyName == "Toast";
        public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver) =>
            throw new NotSupportedException("The never-resolved custom notification must not be invoked during metadata binding.");
    }

    private sealed class DeferredLabelAdapter : IUiControlAdapter
    {
        public bool CanResolve(Type requestedType, UiControlDefinition definition) =>
            requestedType == typeof(ILabelControl) && definition.PropertyName == "Deferred";
        public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver) =>
            new DeferredLabel(innerResolver);
    }

    private sealed class DeferredLabel(IUiControlResolver inner) : ILabelControl
    {
        public string AutomationId => "Deferred";
        public string Name => AutomationId;
        public bool IsEnabled => true;
        public string Text => inner.Resolve<ILabelControl>(new("Child", UiControlType.Label, "Child")).Text;
    }
}
