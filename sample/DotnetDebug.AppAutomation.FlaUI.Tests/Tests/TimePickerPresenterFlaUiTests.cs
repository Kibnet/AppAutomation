using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using TUnit.Assertions;
using TUnit.Core;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

public sealed class TimePickerPresenterFlaUiTests
{
    [Test]
    [Arguments("none")]
    [Arguments("name-fallback")]
    [Arguments("epoch")]
    [Arguments("owner")]
    [Arguments("panel")]
    [Arguments("pending")]
    [Arguments("capabilities")]
    [Arguments("missing-popup")]
    [NotInParallel("DesktopUi")]
    public async Task SameSurfaceLocatorUsesOneStepAndRejectsChangesBeforeConfirm(string mutation)
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var fixture = new PresenterFixture(mutation);
        var parts = TimePickerParts.ByAutomationIds("TimeOwner", "TimeSurface",
            openButtonAutomationId: "UnusedOpen", popupRootAutomationId: mutation == "missing-popup" ? "MissingPopup" : "TimeSurface",
            confirmButtonAutomationId: "Confirm", commitMode: TimePickerCommitMode.Confirm) with
        {
            SurfaceKind = TimePickerSurfaceKind.TimePickerPresenter,
            CommittedValueProperty = "Value",
            PresenterParts = new(new("Hours"), new("Minutes", "IncreaseMinute"))
        };
        var resolver = new FlaUiControlResolver(fixture.Window, fixture.Automation.ConditionFactory)
            .WithTimePicker("Time", parts);
        var page = new PresenterPage(resolver);
        var timer = Stopwatch.StartNew();
        Exception? failure = null;
        try { page.SetTime(static candidate => candidate.Time, TimeSpan.FromMinutes(61)); }
        catch (Exception error) { failure = error; }
        timer.Stop();
        var result = fixture.ReadResult();
        // Provider tree reads are observable traversal work, not an exact count of client FindAll calls.
        Console.WriteLine($"TimePickerPresenter mutation={mutation}; elapsedMs={timer.Elapsed.TotalMilliseconds:F0}; "
            + $"sceneTreeReads={result.SceneReads}; surfaceTreeReads={result.SurfaceReads}; "
            + $"stepInvokes={result.Steps}; confirmPatterns={result.Patterns}; confirmInvokes={result.Confirms}; "
            + $"mutations={result.Mutations}; failure={failure?.GetType().Name ?? "none"}");

        await Assert.That(result.Steps).IsEqualTo(mutation == "missing-popup" ? 0 : 1);
        await Assert.That(result.OtherValue).IsEqualTo("09:00:00");
        if (mutation is "none" or "name-fallback")
        {
            await Assert.That(failure).IsNull();
            await Assert.That(result.Confirms).IsEqualTo(1);
            await Assert.That(result.Value).IsEqualTo("01:01:00");
            await Assert.That(result.IsOpen).IsFalse();
        }
        else
        {
            await Assert.That(result.Mutations).IsEqualTo(mutation == "missing-popup" ? 0 : 1);
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure is TimeoutException).IsFalse();
            await Assert.That(result.Confirms).IsEqualTo(0);
            await Assert.That(result.Value).IsEqualTo("01:00:00");
            await Assert.That(result.IsOpen).IsTrue();
        }
    }

    private sealed class PresenterPage(IUiControlResolver resolver) : UiPage(resolver)
    {
        public ITimePickerControl Time => Resolve<ITimePickerControl>(
            new UiControlDefinition("Time", UiControlType.TimePicker, "TimeOwner")
            { Scope = new UiControlScope("ActiveCard") });
    }

    private sealed record Result(string Value, string OtherValue, bool IsOpen, int Steps, int Confirms, int Patterns,
        int Mutations, int SceneReads, int SurfaceReads);

    private sealed class PresenterFixture : IDisposable
    {
        private readonly Thread _thread;
        private readonly Host _host;
        private Dispatcher? _dispatcher;
        private int _stopping;
        private Exception? _closeError;

        internal PresenterFixture(string mutation)
        {
            var ready = new TaskCompletionSource<(IntPtr Handle, Host Host)>(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread = new Thread(() =>
            {
                System.Windows.Window? window = null;
                try
                {
                    Volatile.Write(ref _dispatcher, Dispatcher.CurrentDispatcher);
                    if (Volatile.Read(ref _stopping) != 0) return;
                    var host = new Host(mutation);
                    window = new System.Windows.Window
                    {
                        Title = "Time presenter protocol fixture", Width = 420, Height = 260,
                        ShowActivated = false, Content = host.Content
                    };
                    window.Show();
                    window.UpdateLayout();
                    if (Volatile.Read(ref _stopping) != 0) return;
                    ready.TrySetResult((new WindowInteropHelper(window).Handle, host));
                    if (Volatile.Read(ref _stopping) == 0) Dispatcher.Run();
                }
                catch (Exception error) { ready.TrySetException(error); }
                finally
                {
                    try { if (window?.IsVisible == true) window.Close(); }
                    catch (Exception error) { _closeError = error; }
                }
            }) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            UIA3Automation? automation = null;
            try
            {
                var (handle, host) = ready.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                _host = host;
                automation = new UIA3Automation();
                Window = automation.FromHandle(handle).AsWindow();
                Automation = automation;
            }
            catch (Exception error)
            {
                try { Stop(automation); }
                catch (Exception cleanup) { error.Data["FixtureCleanup"] = cleanup; }
                throw;
            }
        }

        internal UIA3Automation Automation { get; }
        internal global::FlaUI.Core.AutomationElements.Window Window { get; }
        internal Result ReadResult() => _host.Scene.Dispatcher.Invoke(() => _host.ReadResult());
        public void Dispose() => Stop(Automation);

        private void Stop(UIA3Automation? automation)
        {
            if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
            try { automation?.Dispose(); }
            finally
            {
                if (Volatile.Read(ref _dispatcher) is { HasShutdownStarted: false } dispatcher)
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                if (!_thread.Join(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The time-presenter fixture dispatcher did not stop.");
                if (_closeError is { } error)
                    throw new InvalidOperationException("The time-presenter fixture window did not close.", error);
            }
        }
    }

    private sealed class Host
    {
        private readonly string _mutation;
        private readonly TimeRoot _root = new() { Value = TimeSpan.FromHours(1), Margin = new Thickness(4) };
        private readonly TimeRoot _otherRoot = new() { Value = TimeSpan.FromHours(9), Margin = new Thickness(4) };
        private readonly TextBox _hours = Selector("Hours", "1");
        private readonly StackPanel _components = new();
        private readonly CountingSurface _surface = new();
        private TextBox _minutes = Selector("Minutes", "0");
        private string _owner = "time-fixture-owner";
        private long _epoch = 1;
        private bool _isOpen = true;
        private int _minuteMaximum = 59;
        private int _steps, _confirms, _patterns, _mutations;

        internal Host(string mutation)
        {
            _mutation = mutation == "name-fallback" ? "none" : mutation;
            AutomationProperties.SetAutomationId(Scene, "ActiveCard");
            AutomationProperties.SetAutomationId(_root, mutation == "name-fallback" ? "NameOnlyTimeOwner" : "TimeOwner");
            if (mutation == "name-fallback") AutomationProperties.SetName(_root, "TimeOwner");
            AutomationProperties.SetAutomationId(_surface, "TimeSurface");
            _components.Children.Add(_hours);
            _components.Children.Add(_minutes);
            var increase = new Button { Content = "Increase minute", Margin = new Thickness(4) };
            AutomationProperties.SetAutomationId(increase, "IncreaseMinute");
            increase.Click += (_, _) =>
            {
                _steps++;
                _minutes.Text = (int.Parse(_minutes.Text, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
                PublishPanels();
            };
            _components.Children.Add(increase);
            var confirm = new ConfirmButton(OnConfirmPattern) { Content = "Confirm", Margin = new Thickness(4) };
            AutomationProperties.SetAutomationId(confirm, "Confirm");
            confirm.Click += (_, _) =>
            {
                _confirms++;
                _root.Value = new TimeSpan(int.Parse(_hours.Text, CultureInfo.InvariantCulture),
                    int.Parse(_minutes.Text, CultureInfo.InvariantCulture), 0);
                _isOpen = false;
                PublishOwner();
                _surface.Visibility = Visibility.Collapsed;
            };
            _components.Children.Add(confirm);
            _surface.Child = _components;
            var content = new StackPanel();
            content.Children.Add(_root);
            content.Children.Add(_surface);
            Scene.Child = content;
            var otherCard = new CountingSurface { Child = _otherRoot };
            AutomationProperties.SetAutomationId(otherCard, "OtherCard");
            AutomationProperties.SetAutomationId(_otherRoot, "TimeOwner");
            AutomationProperties.SetItemStatus(_otherRoot, TimePickerRuntimeMetadata.ToAutomationValue(
                new TimePickerOwnerState("other-time-owner", 1, false, false, 24, false)));
            var cards = new StackPanel();
            cards.Children.Add(Scene);
            cards.Children.Add(otherCard);
            Content = cards;
            PublishOwner();
            AutomationProperties.SetItemStatus(_surface,
                TimePickerRuntimeMetadata.ToAutomationValue(new TimePickerPresenterState(_owner, _epoch)));
            PublishPanels();
        }

        internal CountingSurface Scene { get; } = new();
        internal FrameworkElement Content { get; }
        internal Result ReadResult() => new(_root.Text, _otherRoot.Text, _isOpen, _steps, _confirms, _patterns,
            _mutations, Scene.TreeReads, _surface.TreeReads);

        private void OnConfirmPattern()
        {
            _patterns++;
            if (_steps != 1 || _mutation == "none" || _mutations != 0) return;
            _mutations++;
            switch (_mutation)
            {
                case "epoch": _epoch++; PublishOwner(); break;
                case "owner": _owner += "-replaced"; PublishOwner(); break;
                case "pending": _minutes.Text = "2"; PublishPanels(); break;
                case "capabilities": _minuteMaximum = 58; PublishPanels(); break;
                case "panel":
                    var replacement = Selector("Minutes", _minutes.Text);
                    _components.Children.Remove(_minutes);
                    _components.Children.Insert(1, replacement);
                    _minutes = replacement;
                    PublishPanels();
                    Scene.UpdateLayout();
                    break;
                default: throw new InvalidOperationException("Unknown time-presenter test mutation.");
            }
        }

        private void PublishOwner() => AutomationProperties.SetItemStatus(_root,
            TimePickerRuntimeMetadata.ToAutomationValue(new TimePickerOwnerState(_owner, _epoch, _isOpen, false, 24, false)));

        private void PublishPanels()
        {
            AutomationProperties.SetItemStatus(_hours, TimePickerRuntimeMetadata.ToAutomationValue(
                new TimePickerComponentState(_owner, _epoch, TimePickerComponentKind.Hour, 0, 23, 1,
                    int.Parse(_hours.Text, CultureInfo.InvariantCulture))));
            AutomationProperties.SetItemStatus(_minutes, TimePickerRuntimeMetadata.ToAutomationValue(
                new TimePickerComponentState(_owner, _epoch, TimePickerComponentKind.Minute, 0, _minuteMaximum, 1,
                    int.Parse(_minutes.Text, CultureInfo.InvariantCulture))));
        }

        private static TextBox Selector(string id, string value)
        {
            var result = new TextBox { Text = value, IsReadOnly = true, Margin = new Thickness(4) };
            AutomationProperties.SetAutomationId(result, id);
            return result;
        }
    }

    private sealed class TimeRoot : TextBox
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value),
            typeof(TimeSpan?), typeof(TimeRoot), new FrameworkPropertyMetadata(null, static (owner, args) =>
            {
                ((TimeRoot)owner).Text = args.NewValue is TimeSpan value
                    ? value.ToString("c", CultureInfo.InvariantCulture) : string.Empty;
            }));

        public TimeSpan? Value
        {
            get => (TimeSpan?)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }

    private sealed class CountingSurface : Border
    {
        internal int TreeReads;
        protected override AutomationPeer OnCreateAutomationPeer() => new SurfacePeer(this);

        private sealed class SurfacePeer(CountingSurface owner) : FrameworkElementAutomationPeer(owner)
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
            protected override string GetClassNameCore() => nameof(CountingSurface);
            protected override List<AutomationPeer> GetChildrenCore()
            {
                owner.TreeReads++;
                return base.GetChildrenCore();
            }
        }
    }

    private sealed class ConfirmButton(Action patternRequested) : Button
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new ConfirmPeer(this, patternRequested);

        private sealed class ConfirmPeer(ConfirmButton owner, Action patternRequested) : ButtonAutomationPeer(owner)
        {
            public override object GetPattern(PatternInterface patternInterface)
            {
                if (patternInterface == PatternInterface.Invoke) patternRequested();
                return base.GetPattern(patternInterface);
            }
        }
    }
}
