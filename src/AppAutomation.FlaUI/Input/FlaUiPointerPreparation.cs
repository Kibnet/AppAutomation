using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using FlaUI.Core.AutomationElements;

namespace AppAutomation.FlaUI.Input;

// Scoped transport into native control construction and pointer calls, never a window-wide allowlist.
internal sealed class FlaUiPointerPreparation(FlaUiControlResolver resolver, UiInputPreparationContext? context)
{
    private static readonly AsyncLocal<FlaUiPointerPreparation?> Ambient = new();

    internal static FlaUiPointerPreparation? Capture() => Ambient.Value;

    internal static IDisposable EnterCaptured(FlaUiPointerPreparation? captured) => captured is null
        ? Enter(null) : captured.EnterForOperation();

    internal static IDisposable Enter(FlaUiControlResolver resolver, UiInputPreparationContext? context) =>
        Enter(new FlaUiPointerPreparation(resolver, context));

    internal IDisposable EnterForOperation() =>
        Enter(new FlaUiPointerPreparation(resolver, UiInputPreparationContext.ForOperation(context)));

    internal bool IsRegisteredBlocker(AutomationElement target, System.Drawing.Point point) =>
        context is { Notifications.Count: > 0 } && resolver.IsRegisteredPointerBlocker(context, target, point);

    internal bool Recover(AutomationElement target, PointerOcclusionException obstruction,
        DesktopPointerController.PointerOperation operation) =>
        context is { Notifications.Count: > 0 }
        && resolver.DismissPointerBlocker(context, target, obstruction, operation);

    private static IDisposable Enter(FlaUiPointerPreparation? value)
    {
        var previous = Ambient.Value;
        Ambient.Value = value;
        return new Scope(previous);
    }

    private sealed class Scope(FlaUiPointerPreparation? previous) : IDisposable
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

internal sealed class PointerOcclusionException(System.Drawing.Point point, int[] runtimeId, bool late = false)
    : InvalidOperationException("The physical pointer target is covered before its first press.")
{
    internal System.Drawing.Point Point { get; } = point;
    internal int[] RuntimeId { get; } = runtimeId;
    internal bool Late { get; } = late;
}
