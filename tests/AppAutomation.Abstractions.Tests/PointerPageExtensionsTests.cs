namespace AppAutomation.Abstractions.Tests;

public sealed class PointerPageExtensionsTests
{
    private static readonly UiControlDefinition Source = new(
        "Source", UiControlType.AutomationElement, "drag-source", FallbackToName: false)
    {
        Scope = new UiControlScope("source-panel")
    };

    private static readonly UiControlDefinition Target = new(
        "Target", UiControlType.AutomationElement, "target-name", UiLocatorKind.Name, FallbackToName: false)
    {
        Scope = new UiControlScope("target-panel")
    };

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PointerClick_ForwardsDefinitionOptionsAndToken_AndReturnsSamePage(bool adapted)
    {
        var runtime = new PointerResolver();
        var page = new PointerPage(Adapt(runtime, adapted));
        var options = new PointerClickOptions
        {
            Button = PointerButton.Right,
            ClickCount = 2,
            Timeout = TimeSpan.FromSeconds(2)
        };
        using var cancellation = new CancellationTokenSource();

        var result = await page.PointerClickAsync(Target, options, cancellation.Token);

        using (Assert.Multiple())
        {
            await Assert.That(result).IsSameReferenceAs(page);
            await Assert.That(runtime.Target).IsSameReferenceAs(Target);
            await Assert.That(runtime.ClickOptions).IsSameReferenceAs(options);
            await Assert.That(runtime.Token).IsEqualTo(cancellation.Token);
            await Assert.That(runtime.CallCount).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Hover_ForwardsCallback_AndCompletesAfterItsVerification(bool adapted)
    {
        var runtime = new PointerResolver();
        var page = new PointerPage(Adapt(runtime, adapted));
        var options = new PointerHoverOptions { Timeout = TimeSpan.FromSeconds(3) };
        using var cancellation = new CancellationTokenSource();
        var verification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCount = 0;
        var callbackToken = CancellationToken.None;
        Func<CancellationToken, Task> callback = token =>
        {
            callbackCount++;
            callbackToken = token;
            return verification.Task;
        };

        var operation = page.HoverAsync(Target, callback, options, cancellation.Token);
        await Assert.That(operation.IsCompleted).IsFalse();
        verification.SetResult();
        var result = await operation;

        using (Assert.Multiple())
        {
            await Assert.That(result).IsSameReferenceAs(page);
            await Assert.That(runtime.Target).IsSameReferenceAs(Target);
            await Assert.That(runtime.HoverOptions).IsSameReferenceAs(options);
            await Assert.That(runtime.Verification).IsSameReferenceAs(callback);
            await Assert.That(callbackCount).IsEqualTo(1);
            await Assert.That(callbackToken).IsEqualTo(cancellation.Token);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DragAndDrop_ForwardsBothScopedDefinitionsOptionsAndToken(bool adapted)
    {
        var runtime = new PointerResolver();
        var page = new PointerPage(Adapt(runtime, adapted));
        var options = new PointerDragOptions
        {
            Button = PointerButton.Middle,
            Duration = TimeSpan.FromMilliseconds(400),
            Timeout = TimeSpan.FromSeconds(3)
        };
        using var cancellation = new CancellationTokenSource();

        var result = await page.DragAndDropAsync(Source, Target, options, cancellation.Token);

        using (Assert.Multiple())
        {
            await Assert.That(result).IsSameReferenceAs(page);
            await Assert.That(runtime.Source).IsSameReferenceAs(Source);
            await Assert.That(runtime.Target).IsSameReferenceAs(Target);
            await Assert.That(runtime.DragOptions).IsSameReferenceAs(options);
            await Assert.That(runtime.Token).IsEqualTo(cancellation.Token);
            await Assert.That(runtime.CallCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task NullOptions_AreForwardedAsRuntimeDefaults()
    {
        var runtime = new PointerResolver();
        var page = new PointerPage(Adapt(runtime, true));

        await page.PointerClickAsync(Target);
        await page.HoverAsync(Target, static _ => Task.CompletedTask);
        await page.DragAndDropAsync(Source, Target);

        using (Assert.Multiple())
        {
            await Assert.That(runtime.ClickOptions).IsNull();
            await Assert.That(runtime.HoverOptions).IsNull();
            await Assert.That(runtime.DragOptions).IsNull();
            await Assert.That(runtime.CallCount).IsEqualTo(3);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RuntimeWithoutCapability_IsRejectedBeforeAnyInputOrCallback(bool adapted)
    {
        var runtime = new PointerResolver(supportsPointer: false);
        var page = new PointerPage(Adapt(runtime, adapted));
        var callbackCalled = false;

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => page.PointerClickAsync(Target));
        _ = await Assert.ThrowsAsync<NotSupportedException>(() => page.HoverAsync(Target, _ =>
        {
            callbackCalled = true;
            return Task.CompletedTask;
        }));
        _ = await Assert.ThrowsAsync<NotSupportedException>(() => page.DragAndDropAsync(Source, Target));

        using (Assert.Multiple())
        {
            await Assert.That(exception!.Message).Contains("physical pointer input");
            await Assert.That(callbackCalled).IsFalse();
            await Assert.That(runtime.CallCount).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task MissingOptionalRuntime_IsRejected_EvenWhenCapabilityClaimsSupport(bool advertised, bool adapted)
    {
        var page = new PointerPage(Adapt(new NoPointerResolver(advertised), adapted));

        _ = await Assert.ThrowsAsync<NotSupportedException>(() => page.PointerClickAsync(Target));
        _ = await Assert.ThrowsAsync<NotSupportedException>(() =>
            page.HoverAsync(Target, static _ => throw new InvalidOperationException("Must not run.")));
        _ = await Assert.ThrowsAsync<NotSupportedException>(() => page.DragAndDropAsync(Source, Target));
    }

    [Test]
    public async Task AdapterRuntimeDirectCalls_StillRequireAdvertisedCapability()
    {
        var inner = new PointerResolver(supportsPointer: false);
        var wrapped = (IUiPointerRuntime)Adapt(inner, true);

        _ = await Assert.ThrowsAsync<NotSupportedException>(() => wrapped.PointerClickAsync(Target));
        _ = await Assert.ThrowsAsync<NotSupportedException>(() =>
            wrapped.HoverAsync(Target, static _ => Task.CompletedTask));
        _ = await Assert.ThrowsAsync<NotSupportedException>(() => wrapped.DragAndDropAsync(Source, Target));

        await Assert.That(inner.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task AlreadyCancelledOperations_DoNotReachRuntime()
    {
        var runtime = new PointerResolver();
        var page = new PointerPage(Adapt(runtime, true));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            page.PointerClickAsync(Target, cancellationToken: cancellation.Token));
        _ = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            page.HoverAsync(Target, static _ => throw new InvalidOperationException("Must not run."),
                cancellationToken: cancellation.Token));
        _ = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            page.DragAndDropAsync(Source, Target, cancellationToken: cancellation.Token));

        await Assert.That(runtime.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task HoverCallbackFailure_PropagatesOriginalException()
    {
        var failure = new InvalidOperationException("Tooltip did not contain the expected text.");
        var page = new PointerPage(Adapt(new PointerResolver(), true));

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            page.HoverAsync(Target, _ => Task.FromException(failure)));

        await Assert.That(observed).IsSameReferenceAs(failure);
    }

    [Test]
    public async Task RuntimeFailure_PropagatesOriginalException_ForEveryGesture()
    {
        var failure = new InvalidOperationException("Cursor restoration failed.");
        var page = new PointerPage(Adapt(new PointerResolver { Failure = failure }, true));

        var click = await Assert.ThrowsAsync<InvalidOperationException>(() => page.PointerClickAsync(Target));
        var hover = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            page.HoverAsync(Target, static _ => Task.CompletedTask));
        var drag = await Assert.ThrowsAsync<InvalidOperationException>(() => page.DragAndDropAsync(Source, Target));

        using (Assert.Multiple())
        {
            await Assert.That(click).IsSameReferenceAs(failure);
            await Assert.That(hover).IsSameReferenceAs(failure);
            await Assert.That(drag).IsSameReferenceAs(failure);
        }
    }

    [Test]
    public async Task RuntimeCancellation_PreservesOriginalToken()
    {
        using var cancellation = new CancellationTokenSource();
        var page = new PointerPage(Adapt(new PointerResolver(), true));

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => page.HoverAsync(Target, token =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(token);
        }, cancellationToken: cancellation.Token));

        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task DragDurationOutsideTimeout_IsRejectedBeforeRuntimeInput()
    {
        var runtime = new PointerResolver();
        var page = new PointerPage(runtime);

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            page.DragAndDropAsync(Source, Target, new PointerDragOptions
            {
                Duration = TimeSpan.FromMilliseconds(300),
                Timeout = TimeSpan.FromMilliseconds(100)
            }));

        await Assert.That(runtime.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task NullArguments_AreRejectedBeforeRuntimeInput()
    {
        var runtime = new PointerResolver();
        var page = new PointerPage(runtime);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            PointerPageExtensions.PointerClickAsync<PointerPage>(null!, Target));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => page.PointerClickAsync(null!));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => page.HoverAsync(Target, null!));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            page.HoverAsync(null!, static _ => Task.CompletedTask));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => page.DragAndDropAsync(null!, Target));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => page.DragAndDropAsync(Source, null!));

        await Assert.That(runtime.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Options_RejectNonpositiveDurationsInvalidButtonsAndClickCounts()
    {
        foreach (var duration in new[] { TimeSpan.Zero, Timeout.InfiniteTimeSpan, TimeSpan.MinValue })
        {
            await Assert.That(() => new PointerClickOptions { Timeout = duration }).Throws<ArgumentOutOfRangeException>();
            await Assert.That(() => new PointerHoverOptions { Timeout = duration }).Throws<ArgumentOutOfRangeException>();
            await Assert.That(() => new PointerDragOptions { Timeout = duration }).Throws<ArgumentOutOfRangeException>();
            await Assert.That(() => new PointerDragOptions { Duration = duration }).Throws<ArgumentOutOfRangeException>();
        }

        foreach (var button in new[] { (PointerButton)(-1), (PointerButton)3 })
        {
            await Assert.That(() => new PointerClickOptions { Button = button }).Throws<ArgumentOutOfRangeException>();
            await Assert.That(() => new PointerDragOptions { Button = button }).Throws<ArgumentOutOfRangeException>();
        }

        foreach (var count in new[] { -1, 0, 3 })
        {
            await Assert.That(() => new PointerClickOptions { ClickCount = count }).Throws<ArgumentOutOfRangeException>();
        }
    }

    private static IUiControlResolver Adapt(IUiControlResolver runtime, bool adapted) =>
        adapted ? runtime.WithAdapters(new NoOpAdapter()).WithAdapters(new NoOpAdapter()) : runtime;

    private sealed class PointerPage(IUiControlResolver resolver) : UiPage(resolver);

    private sealed class NoOpAdapter : IUiControlAdapter
    {
        public bool CanResolve(Type requestedType, UiControlDefinition definition) => false;

        public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver) =>
            throw new InvalidOperationException("Pointer definitions must be passed intact to the runtime.");
    }

    private sealed class NoPointerResolver(bool advertised) : IUiControlResolver
    {
        public UiRuntimeCapabilities Capabilities { get; } = new("without-pointer-runtime")
        {
            SupportsPointerInput = advertised
        };

        public TControl Resolve<TControl>(UiControlDefinition definition) where TControl : class =>
            throw new InvalidOperationException("Unsupported pointer input must not resolve controls.");
    }

    private sealed class PointerResolver(bool supportsPointer = true) : IUiControlResolver, IUiPointerRuntime
    {
        public UiRuntimeCapabilities Capabilities { get; } = new("pointer-test")
        {
            SupportsPointerInput = supportsPointer
        };

        public UiControlDefinition? Source { get; private set; }
        public UiControlDefinition? Target { get; private set; }
        public PointerClickOptions? ClickOptions { get; private set; }
        public PointerHoverOptions? HoverOptions { get; private set; }
        public PointerDragOptions? DragOptions { get; private set; }
        public Func<CancellationToken, Task>? Verification { get; private set; }
        public CancellationToken Token { get; private set; }
        public int CallCount { get; private set; }
        public Exception? Failure { get; init; }

        public TControl Resolve<TControl>(UiControlDefinition definition) where TControl : class =>
            throw new InvalidOperationException("Pointer target resolution belongs to the runtime.");

        public Task PointerClickAsync(UiControlDefinition target, PointerClickOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            RecordCall(target, cancellationToken);
            ClickOptions = options;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task HoverAsync(UiControlDefinition target, Func<CancellationToken, Task> verifyWhileHovered,
            PointerHoverOptions? options = null, CancellationToken cancellationToken = default)
        {
            RecordCall(target, cancellationToken);
            HoverOptions = options;
            Verification = verifyWhileHovered;
            return Failure is null ? verifyWhileHovered(cancellationToken) : Task.FromException(Failure);
        }

        public Task DragAndDropAsync(UiControlDefinition source, UiControlDefinition target,
            PointerDragOptions? options = null, CancellationToken cancellationToken = default)
        {
            RecordCall(target, cancellationToken);
            Source = source;
            DragOptions = options;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        private void RecordCall(UiControlDefinition target, CancellationToken cancellationToken)
        {
            CallCount++;
            Target = target;
            Token = cancellationToken;
        }
    }
}
