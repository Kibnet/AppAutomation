using System.Reflection;
using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AppAutomation.Recorder.Avalonia;

// One native binding is shared by capture, suppression, semantic reads and revalidation.
internal sealed record RecorderTimePickerBinding(
    RecorderTimePickerHint Hint,
    Control Root,
    AvaloniaProperty ValueProperty,
    TimePickerOwnerState Owner,
    TimePickerPresenter? Presenter,
    Popup? Popup,
    bool HasPublishedIdentity)
{
    internal TimeSpan? ReadValue() => Root.GetValue(ValueProperty) switch
    {
        null when ValueProperty.PropertyType == typeof(TimeSpan?) => null,
        TimeSpan value => value,
        _ => throw new InvalidOperationException("The committed UI property did not expose its declared TimeSpan value.")
    };

    internal bool Contains(Control source) => IsWithin(source, Root)
        || Presenter is not null && IsWithin(source, Presenter)
        || Popup?.Child is { } child && IsWithin(source, child);

    internal bool IsButton(Control source, string? locator) => Matches(source, Hint.Parts.LocatorKind, locator);

    internal static bool IsPresenterHint(RecorderTimePickerHint hint) =>
        hint.Parts.SurfaceKind == TimePickerSurfaceKind.TimePickerPresenter;

    internal static bool IsCandidate(Control source, RecorderTimePickerHint hint) =>
        IsPresenterHint(hint) && RecorderControlTree.EnumerateRelatedControls(source, true)
            .Any(control => IsRoot(control, hint));

    internal static bool IsRoot(Control control, RecorderTimePickerHint hint) =>
        IsPresenterHint(hint) && MatchesSelf(control, hint.Parts.LocatorKind, hint.Parts.RootLocator);

    internal static bool TryResolve(AppAutomationRecorderOptions options, Control? validationRoot, Control? source,
        out RecorderTimePickerBinding binding, out string error, RecorderTimePickerHint? requiredHint = null)
    {
        binding = null!;
        error = string.Empty;
        var hints = options.TimePickerHints.Where(IsPresenterHint)
            .Where(hint => requiredHint is not null ? Equals(hint, requiredHint) : source is not null && IsCandidate(source, hint))
            .ToArray();
        if (hints.Length == 0) return false;
        try
        {
            if (hints.Length != 1) throw new InvalidOperationException("The source matches multiple presenter time-picker hints.");
            if (validationRoot is null) throw new InvalidOperationException("The time-picker observation root is unavailable.");
            var hint = hints[0];
            var parts = hint.Parts;
            parts.ValidateConfiguration();
            var controls = RecorderControlTree.EnumerateReachableControls(validationRoot).ToArray();
            var roots = controls.Where(control => IsRoot(control, hint)).ToArray();
            // The originating event/check proves the concrete card even when template IDs repeat.
            var ownedRoots = source is null ? roots : roots.Where(root => IsWithin(source, root)
                || controls.OfType<Popup>().Any(popup => popup.Child is { } child && IsWithin(source, child)
                    && popup.PlacementTarget is { } placement && IsWithin(placement, root))).ToArray();
            var root = ownedRoots.Length == 1 ? ownedRoots[0]
                : ownedRoots.Length == 0 && roots.Length == 1 ? roots[0]
                : throw new InvalidOperationException("The time-picker source does not identify one live owner instance.");
            var status = AutomationProperties.GetItemStatus(root);
            var published = !string.IsNullOrEmpty(status);
            var ownedPopups = controls.OfType<Popup>()
                .Where(candidate => candidate.PlacementTarget is { } placement && IsWithin(placement, root)).ToArray();
            var nativePresenters = controls.OfType<TimePickerPresenter>()
                .Concat(ownedPopups.Where(candidate => candidate.Child is not null)
                    .SelectMany(candidate => RecorderControlTree.EnumerateReachableControls(candidate.Child!)).OfType<TimePickerPresenter>())
                .Concat(source is TimePickerPresenter native ? [native] : [])
                .Distinct<TimePickerPresenter>(ReferenceEqualityComparer.Instance)
                .Where(candidate => MatchesSelf(candidate, parts.LocatorKind, parts.TimePickerLocator))
                .Where(candidate => IsWithin(candidate, root) || ownedPopups.Any(popup => popup.Child is { } child && IsWithin(candidate, child)))
                .ToArray();
            var nativePresenter = nativePresenters.Length == 1 ? nativePresenters[0] : null;
            if (nativePresenter is not null && nativePresenter.ClockIdentifier is not ("12HourClock" or "24HourClock"))
                throw new NotSupportedException("The native time presenter has an unsupported clock identifier.");
            var nativePopup = ownedPopups.SingleOrDefault(candidate => candidate.Child is { } child
                && nativePresenter is not null && IsWithin(nativePresenter, child));
            var state = published ? TimePickerRuntimeMetadata.ReadOwner(status)
                : new TimePickerOwnerState("native-owner", 0, nativePopup?.IsOpen == true, ReadNativeReadOnly(root),
                    nativePresenter?.ClockIdentifier == "12HourClock" ? 12 : 24, nativePresenter?.UseSeconds == true,
                    nativePresenter?.MinuteIncrement ?? 1, nativePresenter?.SecondIncrement ?? 1);
            if (published && state.IsReadOnly != ReadNativeReadOnly(root))
                throw new InvalidOperationException("The owner read-only publication disagrees with the real UI state.");
            foreach (var other in roots.Where(candidate => !ReferenceEquals(candidate, root)))
            {
                var otherStatus = AutomationProperties.GetItemStatus(other);
                if (!published || string.IsNullOrEmpty(otherStatus)) continue;
                var otherState = TimePickerRuntimeMetadata.ReadOwner(otherStatus);
                if (otherState.OwnerId == state.OwnerId)
                    throw new InvalidOperationException("Two live time-picker roots publish the same owner identity.");
            }
            var property = FindValueProperty(root, parts.CommittedValueProperty!);
            TimePickerPresenter? presenter = null;
            Popup? popup = null;
            if (state.IsOpen || source is TimePickerPresenter)
            {
                if (nativePresenters.Length != 1)
                    throw new InvalidOperationException("The owner has no unique native TimePickerPresenter with proven popup ownership.");
                presenter = nativePresenter!;
                var presenterStatus = AutomationProperties.GetItemStatus(presenter);
                if (published)
                {
                    var presenterState = TimePickerRuntimeMetadata.ReadPresenter(presenterStatus);
                    TimePickerRuntimeMetadata.RequireIdentity(state.OwnerId, state.Epoch, presenterState.OwnerId, presenterState.Epoch);
                }
                else if (!string.IsNullOrEmpty(presenterStatus))
                    throw new InvalidOperationException("The presenter publishes an identity without a matching owner snapshot.");
                popup = nativePopup;
                if (popup is null || popup.IsOpen != state.IsOpen)
                    throw new InvalidOperationException("The native popup opening state disagrees with its time-picker owner.");
                if (parts.PopupScope is { } scope && !RecorderControlTree.EnumerateRelatedControls(presenter, true)
                        .Any(candidate => MatchesSelf(candidate, scope.LocatorKind, scope.LocatorValue)))
                    throw new InvalidOperationException("The native presenter is outside its declared popup scope.");
                if (!string.IsNullOrWhiteSpace(parts.PopupRootLocator)
                    && !Matches(presenter, parts.LocatorKind, parts.PopupRootLocator)
                    && (popup is null || !Matches(popup, parts.LocatorKind, parts.PopupRootLocator)))
                    throw new InvalidOperationException("The native presenter does not belong to its declared popup root.");
                if (state.Clock != (presenter.ClockIdentifier == "12HourClock" ? 12 : 24)
                    || state.UseSeconds != presenter.UseSeconds || state.MinuteStep != presenter.MinuteIncrement
                    || state.SecondStep != presenter.SecondIncrement)
                    throw new InvalidOperationException("The owner capabilities disagree with the native time presenter.");
                if (state.Clock == 12 && parts.PresenterParts!.Period is null
                    || state.UseSeconds && parts.PresenterParts!.Seconds is null)
                    throw new InvalidOperationException("The presenter is missing a required declared numeric component.");
                ValidateNativeParts(presenter, popup, parts, state, published);
            }
            binding = new(hint, root, property, state, presenter ?? nativePresenter, popup ?? nativePopup, published);
            _ = binding.ReadValue();
            if (source is not null && !binding.Contains(source))
                throw new InvalidOperationException("The source is not part of the resolved native time-picker owner.");
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or NotSupportedException or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException or FormatException)
        {
            error = "Configured time-picker presenter: " + exception.Message;
            return false;
        }
    }

    internal static AvaloniaProperty FindValueProperty(Control root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('.'))
            throw new NotSupportedException("A direct public committed UI property is required; model paths are not supported.");
        var clr = root.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        var property = AvaloniaPropertyRegistry.Instance.FindRegistered(root, name);
        if (clr?.GetMethod?.IsPublic != true || clr.GetIndexParameters().Length != 0 || property is null
            || clr.PropertyType != property.PropertyType
            || property.PropertyType != typeof(TimeSpan) && property.PropertyType != typeof(TimeSpan?))
            throw new NotSupportedException("The committed property must be a registered public TimeSpan/TimeSpan? Avalonia UI property.");
        return property;
    }

    internal static bool IsWithin(Control source, Control owner) =>
        RecorderControlTree.EnumerateRelatedControls(source, includePopupPlacementTarget: true)
            .Any(candidate => ReferenceEquals(candidate, owner));

    internal static bool Matches(Control source, UiLocatorKind kind, string? locator) =>
        !string.IsNullOrWhiteSpace(locator) && RecorderControlTree.EnumerateRelatedControls(source, true)
            .Any(candidate => MatchesSelf(candidate, kind, locator));

    private static bool MatchesSelf(Control source, UiLocatorKind kind, string? locator) =>
        !string.IsNullOrWhiteSpace(locator) && string.Equals(locator.Trim(), kind == UiLocatorKind.AutomationId
            ? AutomationProperties.GetAutomationId(source)?.Trim()
            : (AutomationProperties.GetName(source) ?? source.Name)?.Trim(), StringComparison.Ordinal);

    private static bool ReadNativeReadOnly(Control root)
    {
        var property = AvaloniaPropertyRegistry.Instance.FindRegistered(root, "IsReadOnly");
        if (property?.PropertyType == typeof(bool)) return root.GetValue(property) is true;
        return ControlAutomationPeer.CreatePeerForElement(root).GetProvider<IValueProvider>()?.IsReadOnly
            ?? throw new NotSupportedException("The time-picker root does not expose its actual read-only UI state.");
    }

    private static void ValidateNativeParts(TimePickerPresenter presenter, Popup popup, TimePickerParts parts,
        TimePickerOwnerState owner, bool published)
    {
        var controls = RecorderControlTree.EnumerateReachableControls(popup.Child!).ToArray();
        var confirm = controls.Where(candidate => MatchesSelf(candidate, parts.LocatorKind, parts.ConfirmButtonLocator)).ToArray();
        if (confirm.Length != 1 || confirm[0] is not Button)
            throw new NotSupportedException("The presenter requires exactly one real configured confirm Button.");
        foreach (var (kind, component) in new[] { (TimePickerComponentKind.Hour, parts.PresenterParts!.Hours),
                     (TimePickerComponentKind.Minute, parts.PresenterParts.Minutes),
                     (TimePickerComponentKind.Second, parts.PresenterParts.Seconds),
                     (TimePickerComponentKind.Period, parts.PresenterParts.Period) })
        {
            if (component is null) continue;
            if (kind == TimePickerComponentKind.Second && !owner.UseSeconds
                || kind == TimePickerComponentKind.Period && owner.Clock != 12) continue;
            var matches = controls.Where(candidate => MatchesSelf(candidate, parts.LocatorKind, component.SelectorLocator)).ToArray();
            if (matches.Length != 1 || matches[0] is not DateTimePickerPanel panel || !IsWithin(panel, presenter))
                throw new NotSupportedException("A declared time component does not identify one native presenter panel.");
            var expected = owner.GetComponentRange(kind);
            if (panel.MinimumValue != expected.Minimum || panel.MaximumValue != expected.Maximum || panel.Increment != expected.Step)
                throw new NotSupportedException("The native time component range or increment disagrees with its declared presenter capabilities.");
            if (!published) continue;
            var state = TimePickerRuntimeMetadata.ReadComponent(AutomationProperties.GetItemStatus(panel), kind);
            TimePickerRuntimeMetadata.RequireIdentity(owner.OwnerId, owner.Epoch, state.OwnerId, state.Epoch);
            if (state.Minimum != panel.MinimumValue || state.Maximum != panel.MaximumValue
                || state.Step != panel.Increment || state.SelectedValue != panel.SelectedValue)
                throw new InvalidOperationException("Published time component state disagrees with the real native UI panel.");
        }
    }
}
