using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace DotnetDebug.Avalonia;

/// <summary>A deterministic native input surface, enabled only by the pointer fixture launch option.</summary>
public sealed class PointerFixtureWindow : Window
{
    private readonly TextBlock _leftPresses = Counter("PointerLeftPresses");
    private readonly TextBlock _rightPresses = Counter("PointerRightPresses");
    private readonly TextBlock _leftReleases = Counter("PointerLeftReleases");
    private readonly TextBlock _rightReleases = Counter("PointerRightReleases");
    private readonly TextBlock _doubleClicks = Counter("PointerDoubleClicks");
    private readonly TextBlock _invocations = Counter("PointerInvocations");
    private readonly TextBlock _replacementPresses = Counter("PointerReplacementPresses");
    private readonly TextBlock _dragOvers = Counter("PointerDragOvers");
    private readonly TextBlock _drops = Counter("PointerDrops");
    private readonly TextBlock _order = Label("PointerOrder", "A,B");
    private readonly TextBlock _dragResult = Label("PointerDragResult", "Idle");
    private int _windowDragEnters;
    private int _windowDragOvers;
    private int _windowDrops;
    private Stopwatch? _dragTimer;

    public PointerFixtureWindow()
    {
        Title = "AppAutomation pointer fixture";
        Width = 820;
        Height = 650;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AutomationProperties.SetAutomationId(this, "PointerFixtureWindow");
        AddHandler(DragDrop.DragEnterEvent, (_, args) =>
        {
            _windowDragEnters++;
            RecordWindowDragEvent("window-drag-enter", args);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, (_, args) =>
        {
            _windowDragOvers++;
            RecordWindowDragEvent("window-drag-over", args);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, (_, args) =>
        {
            _windowDrops++;
            RecordWindowDragEvent("window-drop", args);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, (_, args) =>
        {
            if (_dragTimer is { IsRunning: true })
            {
                var point = args.GetPosition(this);
                RecordDragDiagnostic("window-pointer-move", new
                {
                    point.X, point.Y,
                    args.Properties.IsLeftButtonPressed,
                    Update = args.Properties.PointerUpdateKind.ToString()
                });
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        var clickTarget = Button("PointerClickTarget", "Click / right-click / double-click");
        var clickHost = new ContentControl { Content = clickTarget };
        clickTarget.AddHandler(PointerPressedEvent, (_, args) =>
        {
            var properties = args.Properties;
            if (properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            {
                Increment(_leftPresses);
                if (args.ClickCount == 2)
                    Increment(_doubleClicks);
            }
            else if (properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
            {
                Increment(_rightPresses);
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        clickTarget.AddHandler(PointerReleasedEvent, (_, args) =>
        {
            switch (args.Properties.PointerUpdateKind)
            {
                case PointerUpdateKind.LeftButtonReleased:
                    Increment(_leftReleases);
                    break;
                case PointerUpdateKind.RightButtonReleased:
                    Increment(_rightReleases);
                    break;
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        clickTarget.Click += (_, _) =>
        {
            Increment(_invocations);
            if (Environment.GetEnvironmentVariable("APPAUTOMATION_POINTER_REPLACEMENT_FIXTURE") == "1")
            {
                var replacement = Button("PointerReplacement", "Replacement must not receive the second click");
                replacement.AddHandler(PointerPressedEvent, (_, _) => Increment(_replacementPresses),
                    RoutingStrategies.Tunnel, handledEventsToo: true);
                clickHost.Content = replacement;
            }
        };

        var tooltipTarget = Button("PointerDisabledTooltipTarget", "Disabled target with tooltip");
        tooltipTarget.IsEnabled = false;
        ToolTip.SetTip(tooltipTarget, Label("PointerTooltipText", "Tooltip belongs to disabled target"));
        ToolTip.SetShowOnDisabled(tooltipTarget, true);
        ToolTip.SetShowDelay(tooltipTarget, 25);
        ToolTip.SetPlacement(tooltipTarget, PlacementMode.Bottom);
        var openTooltip = Button("PointerOpenTooltip", "Open tooltip before hover");
        openTooltip.Click += (_, _) => ToolTip.SetIsOpen(tooltipTarget, true);

        // A Name collision must not shadow a unique primary AutomationId; duplicate IDs must
        // fail resolution before input. Keep all three controls visible for native regression tests.
        var nameCollision = Button("PointerNameCollision", "PointerClickTarget");
        var duplicateFirst = Button("PointerDuplicateId", "Duplicate A");
        var duplicateSecond = Button("PointerDuplicateId", "Duplicate B");
        foreach (var regressionControl in new[] { nameCollision, duplicateFirst, duplicateSecond })
        {
            regressionControl.Height = 29;
            regressionControl.AddHandler(PointerPressedEvent, (_, _) => Increment(_replacementPresses),
                RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        var dragSource = Button("PointerDragSource", "A — drag onto B");
        var dropTarget = Button("PointerDropTarget", "B — drop A here");
        dragSource.Width = dropTarget.Width = 300;
        dragSource.Height = dropTarget.Height = 90;
        dragSource.AddHandler(PointerPressedEvent, async (_, args) =>
        {
            if (!args.Properties.IsLeftButtonPressed)
                return;

            args.Handled = true;
            _dragResult.Text = "Dragging";
            _dragTimer = Stopwatch.StartNew();
            RecordDragDiagnostic("source-pressed", new
            {
                Position = args.GetPosition(this).ToString(),
                SourcePosition = args.GetPosition(dragSource).ToString(),
                args.Properties.IsLeftButtonPressed,
                KeyModifiers = args.KeyModifiers.ToString(),
                PlatformDragSource = GetPlatformDragSourceType()
            });
            try
            {
                using var data = new DataTransfer();
                data.Add(DataTransferItem.CreateText("pointer-item-A"));
                RecordDragDiagnostic("before-do-drag-drop", null);
                var result = await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Move);
                _dragResult.Text = result.ToString();
                RecordDragDiagnostic("after-do-drag-drop", new { Result = result.ToString() });
            }
            catch (Exception exception)
            {
                // Surface an actual native drag failure in the test's UI assertion.
                _dragResult.Text = "Failed: " + exception.GetType().Name;
                RecordDragDiagnostic("do-drag-drop-error", new { Type = exception.GetType().FullName, exception.Message });
            }
            finally
            {
                RecordDragDiagnostic("drag-summary", new
                {
                    WindowEnters = _windowDragEnters,
                    WindowOvers = _windowDragOvers,
                    WindowDrops = _windowDrops,
                    TargetOvers = _dragOvers.Text,
                    TargetDrops = _drops.Text,
                    Result = _dragResult.Text
                });
                _dragTimer.Stop();
            }
        }, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(dropTarget, true);
        DragDrop.AddDragOverHandler(dropTarget, (_, args) =>
        {
            args.DragEffects = args.DataTransfer.TryGetText() == "pointer-item-A"
                ? DragDropEffects.Move
                : DragDropEffects.None;
            Increment(_dragOvers);
            RecordWindowDragEvent("target-drag-over", args);
            args.Handled = true;
        });
        DragDrop.AddDropHandler(dropTarget, (_, args) =>
        {
            if (args.DataTransfer.TryGetText() == "pointer-item-A")
            {
                _order.Text = "B,A";
                Increment(_drops);
                args.DragEffects = DragDropEffects.Move;
            }
            else
            {
                args.DragEffects = DragDropEffects.None;
            }

            args.Handled = true;
            RecordWindowDragEvent("target-drop", args);
        });

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Physical pointer lifecycle", FontSize = 24 },
                        nameCollision,
                        duplicateFirst,
                        duplicateSecond
                    }
                },
                clickHost,
                new WrapPanel
                {
                    Children =
                    {
                        Stat("Left down/up", _leftPresses, _leftReleases),
                        Stat("Right down/up", _rightPresses, _rightReleases),
                        Stat("Double-clicks", _doubleClicks),
                        Stat("Invocations", _invocations),
                        Stat("Replacement presses", _replacementPresses)
                    }
                },
                tooltipTarget,
                openTooltip,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 40,
                    Children = { dragSource, dropTarget }
                },
                new WrapPanel
                {
                    Children =
                    {
                        Stat("Drag over", _dragOvers),
                        Stat("Drops", _drops),
                        Stat("Order", _order),
                        Stat("Drag result", _dragResult)
                    }
                }
            }
        };
    }

    private static Button Button(string id, string text)
    {
        var button = new Button { Content = text, Height = 42, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    private static TextBlock Label(string id, string text)
    {
        var label = new TextBlock { Text = text, Margin = new Thickness(4, 0) };
        AutomationProperties.SetAutomationId(label, id);
        return label;
    }

    private static TextBlock Counter(string id) => Label(id, "0");

    private void RecordWindowDragEvent(string stage, DragEventArgs args)
    {
        var point = args.GetPosition(this);
        RecordDragDiagnostic(stage, new
        {
            point.X, point.Y,
            Source = (args.Source as Control)?.GetValue(AutomationProperties.AutomationIdProperty),
            Effects = args.DragEffects.ToString(),
            args.Handled,
            KeyModifiers = args.KeyModifiers.ToString()
        });
    }

    private void RecordDragDiagnostic(string stage, object? detail)
    {
        var path = Environment.GetEnvironmentVariable("APPAUTOMATION_POINTER_FIXTURE_TRACE_PATH");
        if (string.IsNullOrWhiteSpace(path))
            return;
        File.AppendAllText(path, JsonSerializer.Serialize(new
        {
            Stage = stage,
            Timestamp = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            ThreadId = Environment.CurrentManagedThreadId,
            Apartment = Thread.CurrentThread.GetApartmentState().ToString(),
            ElapsedMilliseconds = _dragTimer?.Elapsed.TotalMilliseconds,
            EscapePressed = OperatingSystem.IsWindows() ? (GetAsyncKeyState(0x1B) & 0x8000) != 0 : (bool?)null,
            LeftPressed = OperatingSystem.IsWindows() ? (GetAsyncKeyState(0x01) & 0x8000) != 0 : (bool?)null,
            Detail = detail
        }) + Environment.NewLine);
    }

    private static string? GetPlatformDragSourceType()
    {
        // Avalonia's reference assembly intentionally hides its service locator. Reflection is
        // restricted to this opt-in fixture diagnostic and does not participate in drag behavior.
        try
        {
            var current = typeof(AvaloniaLocator).GetProperty("Current",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var resolverContract = typeof(AvaloniaLocator).Assembly.GetType("Avalonia.IAvaloniaDependencyResolver");
            var getService = resolverContract?.GetMethod("GetService");
            if (current is null || getService is null)
                return "diagnostic-api-unavailable";
            var resolver = current.GetValue(null);
            return getService.Invoke(resolver, [typeof(IPlatformDragSource)])?.GetType().FullName;
        }
        catch (Exception exception)
        {
            return "diagnostic-failed:" + exception.GetType().Name;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private static void Increment(TextBlock counter) =>
        counter.Text = (int.Parse(counter.Text!, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);

    private static StackPanel Stat(string title, params TextBlock[] values)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 12 });
        foreach (var value in values)
            panel.Children.Add(value);
        return panel;
    }
}
