using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using AppAutomation.Recorder.Avalonia.UI;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderTests
{
    [Test]
    public async Task TryCreateTextEntryStep_UsesSpinnerHint_ForNumericTextBox()
    {
        var options = new AppAutomationRecorderOptions();
        options.ControlHints.Add(new RecorderControlHint("MixCountSpinner", RecorderActionHint.SpinnerTextBox));
        var factory = new RecorderStepFactory(options);
        var textBox = new TextBox { Text = "10.5" };
        AutomationProperties.SetAutomationId(textBox, "MixCountSpinner");

        var result = factory.TryCreateTextEntryStep(textBox);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.SetSpinnerValue);
            await Assert.That(result.Step.DoubleValue).IsEqualTo(10.5);
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo("MixCountSpinner");
        }
    }

    [Test]
    public async Task Resolve_UsesAutomationIdFromVisualAncestors()
    {
        var resolver = new RecorderSelectorResolver(new AppAutomationRecorderOptions());
        var root = new StackPanel();
        AutomationProperties.SetAutomationId(root, "CalculateButton");
        var child = new Border();
        root.Children.Add(child);

        var result = resolver.Resolve(child, UiControlType.Button);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Control).IsNotNull();
            await Assert.That(result.Control!.LocatorKind).IsEqualTo(UiLocatorKind.AutomationId);
            await Assert.That(result.Control.LocatorValue).IsEqualTo("CalculateButton");
            await Assert.That(result.Control.ProposedPropertyName).IsEqualTo("CalculateButton");
            await Assert.That(result.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(result.CanPersist).IsEqualTo(true);
        }
    }

    [Test]
    public async Task Resolve_UsesNameFallback_OnlyWhenEnabled()
    {
        var namedControl = new TextBox { Name = "ResultText" };
        var enabledResolver = new RecorderSelectorResolver(new AppAutomationRecorderOptions { AllowNameLocators = true });
        var disabledResolver = new RecorderSelectorResolver(new AppAutomationRecorderOptions { AllowNameLocators = false });

        var enabledResult = enabledResolver.Resolve(namedControl, UiControlType.TextBox);
        var disabledResult = disabledResolver.Resolve(namedControl, UiControlType.TextBox);

        using (Assert.Multiple())
        {
            await Assert.That(enabledResult.Success).IsEqualTo(true);
            await Assert.That(enabledResult.Control).IsNotNull();
            await Assert.That(enabledResult.Control!.LocatorKind).IsEqualTo(UiLocatorKind.Name);
            await Assert.That(enabledResult.Control.LocatorValue).IsEqualTo("ResultText");
            await Assert.That(enabledResult.Control.Warning).Contains("Using Name locator");
            await Assert.That(enabledResult.ValidationStatus).IsEqualTo(RecorderValidationStatus.Warning);
            await Assert.That(enabledResult.CanPersist).IsEqualTo(true);
            await Assert.That(disabledResult.Success).IsEqualTo(false);
            await Assert.That(disabledResult.Message).Contains("AutomationId locator");
        }
    }

    [Test]
    public async Task Resolve_ReturnsInvalid_WhenSelectorIsAmbiguous()
    {
        var root = new StackPanel();
        var recordedButton = new Button { Content = "Recorded" };
        var duplicateButton = new Button { Content = "Duplicate" };
        AutomationProperties.SetAutomationId(recordedButton, "RunButton");
        AutomationProperties.SetAutomationId(duplicateButton, "RunButton");
        root.Children.Add(recordedButton);
        root.Children.Add(duplicateButton);

        var resolver = new RecorderSelectorResolver(new AppAutomationRecorderOptions(), validationRoot: root);

        var result = resolver.Resolve(recordedButton, UiControlType.Button);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.ValidationStatus).IsEqualTo(RecorderValidationStatus.Invalid);
            await Assert.That(result.CanPersist).IsEqualTo(false);
            await Assert.That(result.ValidationMessage).Contains("ambiguous");
        }
    }

    [Test]
    public async Task TryCreateListBoxStep_CapturesSelectedItem()
    {
        var factory = new RecorderStepFactory(new AppAutomationRecorderOptions());
        var listBox = new ListBox
        {
            ItemsSource = new[] { "Prime", "Fibonacci" },
            SelectedItem = "Fibonacci"
        };
        AutomationProperties.SetAutomationId(listBox, "HierarchySelectionList");

        var result = factory.TryCreateListBoxStep(listBox);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.SelectListBoxItem);
            await Assert.That(result.Step.StringValue).IsEqualTo("Fibonacci");
            await Assert.That(result.Step.Control.ControlType).IsEqualTo(UiControlType.ListBox);
        }
    }

    [Test]
    public async Task TryCreateAssertionStep_BuiltInsTakePrecedenceOverCustomExtractors()
    {
        var options = new AppAutomationRecorderOptions();
        options.AssertionExtractors.Add(new AggressiveTextOverrideExtractor());
        var factory = new RecorderStepFactory(options);
        var textBox = new TextBox { Text = "Alpha Beta" };
        AutomationProperties.SetAutomationId(textBox, "SearchBox");

        var result = factory.TryCreateAssertionStep(textBox, RecorderAssertionMode.Text);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilTextEquals);
            await Assert.That(result.Step.StringValue).IsEqualTo("Alpha Beta");
            await Assert.That(result.Step.Warning?.Contains("custom extractor", StringComparison.Ordinal) ?? false).IsEqualTo(false);
        }
    }

    [Test]
    public async Task HotkeyMap_UsesConfiguredGestures_AndBuildsLegend()
    {
        var hotkeys = new RecorderHotkeys
        {
            StartStop = "Alt+R",
            Export = "Ctrl+Alt+E",
            ToggleOverlayMinimize = "Shift+M"
        };

        var map = RecorderHotkeyMap.Create(hotkeys);
        var startStopResolved = map.TryGetCommand(Key.R, KeyModifiers.Alt, out var startStopCommand);
        var exportResolved = map.TryGetCommand(Key.E, KeyModifiers.Control | KeyModifiers.Alt, out var exportCommand);
        var overlayResolved = map.TryGetCommand(Key.M, KeyModifiers.Shift, out var overlayCommand);
        var legend = map.BuildLegend();

        using (Assert.Multiple())
        {
            await Assert.That(startStopResolved).IsEqualTo(true);
            await Assert.That(startStopCommand).IsEqualTo(RecorderCommandKind.StartStop);
            await Assert.That(exportResolved).IsEqualTo(true);
            await Assert.That(exportCommand).IsEqualTo(RecorderCommandKind.Export);
            await Assert.That(overlayResolved).IsEqualTo(true);
            await Assert.That(overlayCommand).IsEqualTo(RecorderCommandKind.ToggleOverlayMinimize);
            await Assert.That(legend.Contains("Alt+R: Start/Stop", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(legend.Contains("Ctrl+Alt+E: Export", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(legend.Contains("Shift+M: Overlay", StringComparison.Ordinal)).IsEqualTo(true);
        }
    }

    [Test]
    public async Task Overlay_MinimizeRestore_UpdatesPresentationAndCounters()
    {
        var session = new FakeRecorderSession
        {
            StepCount = 3,
            PersistableStepCount = 2,
            LatestStatus = "Selector warning",
            LatestPreview = "Page.SelectListBoxItem(static page => page.HierarchySelectionList, \"Fibonacci\");",
            LatestValidationStatus = RecorderValidationStatus.Warning
        };
        var overlay = new RecorderOverlay();
        var minimizedRaised = 0;
        var restoredRaised = 0;
        overlay.MinimizeRequested += (_, _) => minimizedRaised++;
        overlay.RestoreRequested += (_, _) => restoredRaised++;

        overlay.Attach(
            session,
            new AppAutomationRecorderOptions
            {
                Overlay = new RecorderOverlayOptions
                {
                    EnableExportButton = true,
                    ShowShortcutLegend = true
                }
            });

        var stepCounter = overlay.FindControl<TextBlock>("StepCounter");
        var validationBadge = overlay.FindControl<TextBlock>("ValidationBadgeText");
        var exportButton = overlay.FindControl<Button>("ExportButton");
        var expandedPanel = overlay.FindControl<Control>("ExpandedPanel");
        var minimizedPanel = overlay.FindControl<Control>("MinimizedPanel");

        overlay.Minimize();
        overlay.Restore();

        using (Assert.Multiple())
        {
            await Assert.That(stepCounter).IsNotNull();
            await Assert.That(stepCounter!.Text).IsEqualTo("2/3 steps");
            await Assert.That(validationBadge).IsNotNull();
            await Assert.That(validationBadge!.Text).IsEqualTo("WARN");
            await Assert.That(exportButton).IsNotNull();
            await Assert.That(exportButton!.IsVisible).IsEqualTo(true);
            await Assert.That(expandedPanel).IsNotNull();
            await Assert.That(expandedPanel!.IsVisible).IsEqualTo(true);
            await Assert.That(minimizedPanel).IsNotNull();
            await Assert.That(minimizedPanel!.IsVisible).IsEqualTo(false);
            await Assert.That(minimizedRaised).IsEqualTo(1);
            await Assert.That(restoredRaised).IsEqualTo(1);
            await Assert.That(overlay.IsMinimized).IsEqualTo(false);
        }
    }

    [Test]
    public async Task SaveAsync_GeneratesOnlyMissingControls_AndRecordedScenario()
    {
        using var directory = new TemporaryDirectory();
        CreateAuthoringProject(
            directory.Path,
            existingPageContent:
            """
            using AppAutomation.Abstractions;

            namespace Sample.Authoring.Pages;

            [UiControl("ExistingResult", UiControlType.Label, "ResultText", FallbackToName = false)]
            public sealed partial class MainWindowPage
            {
            }
            """,
            existingScenarioContent:
            """
            namespace Sample.Authoring.Tests;

            public abstract partial class MainWindowScenariosBase<TSession>
            {
                public void ExistingScenario()
                {
                }
            }
            """);

        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var options = CreateOptions(directory.Path, scenarioName: "Smoke Flow");
        IReadOnlyList<RecordedStep> steps =
        [
            new RecordedStep(
                RecordedActionKind.WaitUntilTextEquals,
                new RecordedControlDescriptor(
                    "ExistingResult",
                    UiControlType.Label,
                    "ResultText",
                    UiLocatorKind.AutomationId,
                    FallbackToName: false,
                    AvaloniaTypeName: typeof(TextBlock).FullName ?? nameof(TextBlock),
                    Warning: null),
                StringValue: "Ready"),
            new RecordedStep(
                RecordedActionKind.ClickButton,
                new RecordedControlDescriptor(
                    "ExistingResult",
                    UiControlType.Button,
                    "RunButton",
                    UiLocatorKind.AutomationId,
                    FallbackToName: false,
                    AvaloniaTypeName: typeof(Button).FullName ?? nameof(Button),
                    Warning: null))
        ];

        var result = await generator.SaveAsync(CreateWindowStub(), options, steps, outputDirectoryOverride: null);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.PageFilePath).IsNotNull();
            await Assert.That(result.ScenarioFilePath).IsNotNull();
            await Assert.That(result.Diagnostics.Any(static message => message.Contains("renamed", StringComparison.Ordinal))).IsEqualTo(true);
        }

        var pageSource = await File.ReadAllTextAsync(result.PageFilePath!);
        var scenarioSource = await File.ReadAllTextAsync(result.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(pageSource.Contains("[UiControl(\"ExistingResult2\", UiControlType.Button, \"RunButton\", FallbackToName = false)]", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(pageSource.Contains("ResultText", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("Page.WaitUntilTextEquals(static page => page.ExistingResult, \"Ready\");", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.ClickButton(static page => page.ExistingResult2);", StringComparison.Ordinal)).IsEqualTo(true);
        }
    }

    [Test]
    public async Task SaveAsync_SkipsInvalidSteps_AndReportsCounts()
    {
        using var directory = new TemporaryDirectory();
        CreateAuthoringProject(
            directory.Path,
            existingPageContent:
            """
            namespace Sample.Authoring.Pages;

            public sealed partial class MainWindowPage
            {
            }
            """,
            existingScenarioContent:
            """
            namespace Sample.Authoring.Tests;

            public abstract partial class MainWindowScenariosBase<TSession>
            {
            }
            """);

        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var options = CreateOptions(directory.Path, scenarioName: "Parity Flow");
        IReadOnlyList<RecordedStep> steps =
        [
            new RecordedStep(
                RecordedActionKind.ClickButton,
                new RecordedControlDescriptor(
                    "RunButton",
                    UiControlType.Button,
                    "RunButton",
                    UiLocatorKind.AutomationId,
                    FallbackToName: false,
                    AvaloniaTypeName: typeof(Button).FullName ?? nameof(Button),
                    Warning: null)),
            new RecordedStep(
                RecordedActionKind.WaitUntilTextEquals,
                new RecordedControlDescriptor(
                    "HierarchySelectionList",
                    UiControlType.ListBox,
                    "HierarchySelectionList",
                    UiLocatorKind.AutomationId,
                    FallbackToName: false,
                    AvaloniaTypeName: typeof(ListBox).FullName ?? nameof(ListBox),
                    Warning: null),
                StringValue: "Fibonacci",
                ValidationStatus: RecorderValidationStatus.Invalid,
                ValidationMessage: "Selector 'AutomationId:HierarchySelectionList' is ambiguous and was skipped.",
                CanPersist: false)
        ];

        var result = await generator.SaveAsync(CreateWindowStub(), options, steps, outputDirectoryOverride: null);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.PersistedStepCount).IsEqualTo(1);
            await Assert.That(result.SkippedStepCount).IsEqualTo(1);
            await Assert.That(result.Diagnostics.Any(static diagnostic => diagnostic.Contains("ambiguous", StringComparison.OrdinalIgnoreCase))).IsEqualTo(true);
            await Assert.That(result.Message.Contains("skipped", StringComparison.OrdinalIgnoreCase)).IsEqualTo(true);
        }

        var scenarioSource = await File.ReadAllTextAsync(result.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(scenarioSource.Contains("Page.ClickButton(static page => page.RunButton);", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("HierarchySelectionList", StringComparison.Ordinal)).IsEqualTo(false);
        }
    }

    [Test]
    public async Task SaveAsync_Fails_WhenScenarioClassIsNotPartial()
    {
        using var directory = new TemporaryDirectory();
        CreateAuthoringProject(
            directory.Path,
            existingPageContent:
            """
            using AppAutomation.Abstractions;

            namespace Sample.Authoring.Pages;

            public sealed partial class MainWindowPage
            {
            }
            """,
            existingScenarioContent:
            """
            namespace Sample.Authoring.Tests;

            public abstract class MainWindowScenariosBase<TSession>
            {
            }
            """);

        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var options = CreateOptions(directory.Path, scenarioName: "Invalid Flow");
        IReadOnlyList<RecordedStep> steps =
        [
            new RecordedStep(
                RecordedActionKind.ClickButton,
                new RecordedControlDescriptor(
                    "RunButton",
                    UiControlType.Button,
                    "RunButton",
                    UiLocatorKind.AutomationId,
                    FallbackToName: false,
                    AvaloniaTypeName: typeof(Button).FullName ?? nameof(Button),
                    Warning: null))
        ];

        var result = await generator.SaveAsync(CreateWindowStub(), options, steps, outputDirectoryOverride: null);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(false);
            await Assert.That(result.Message).Contains("must be partial");
            await Assert.That(result.PageFilePath).IsNull();
            await Assert.That(result.ScenarioFilePath).IsNull();
        }
    }

    private static AppAutomationRecorderOptions CreateOptions(string authoringProjectDirectory, string scenarioName)
    {
        return new AppAutomationRecorderOptions
        {
            ScenarioName = scenarioName,
            AuthoringProjectDirectory = authoringProjectDirectory,
            PageNamespace = "Sample.Authoring.Pages",
            PageClassName = "MainWindowPage",
            ScenarioNamespace = "Sample.Authoring.Tests",
            ScenarioClassName = "MainWindowScenariosBase",
            ShowOverlay = false
        };
    }

    private static Window CreateWindowStub()
    {
#pragma warning disable SYSLIB0050
        return (Window)FormatterServices.GetUninitializedObject(typeof(TestRecorderWindow));
#pragma warning restore SYSLIB0050
    }

    private static void CreateAuthoringProject(
        string rootPath,
        string existingPageContent,
        string existingScenarioContent)
    {
        var pagesDirectory = Directory.CreateDirectory(Path.Combine(rootPath, "Pages"));
        var testsDirectory = Directory.CreateDirectory(Path.Combine(rootPath, "Tests"));

        File.WriteAllText(Path.Combine(pagesDirectory.FullName, "MainWindowPage.cs"), existingPageContent);
        File.WriteAllText(Path.Combine(testsDirectory.FullName, "MainWindowScenariosBase.cs"), existingScenarioContent);
    }

    private sealed class AggressiveTextOverrideExtractor : IRecorderAssertionExtractor
    {
        public bool TryCreate(Control control, RecorderAssertionMode mode, out RecorderAssertionCandidate? candidate)
        {
            candidate = null;
            if (control is not TextBox || mode is not (RecorderAssertionMode.Auto or RecorderAssertionMode.Text))
            {
                return false;
            }

            candidate = new RecorderAssertionCandidate(
                UiControlType.TextBox,
                RecordedActionKind.WaitUntilIsEnabled,
                BoolValue: false,
                Warning: "custom extractor");
            return true;
        }
    }

    private sealed class FakeRecorderSession : IAppAutomationRecorderSession
    {
        public RecorderSessionState State { get; set; }

        public int StepCount { get; set; }

        public int PersistableStepCount { get; set; }

        public string LatestPreview { get; set; } = string.Empty;

        public string LatestStatus { get; set; } = string.Empty;

        public RecorderValidationStatus LatestValidationStatus { get; set; } = RecorderValidationStatus.Valid;

        public void Start()
        {
            State = RecorderSessionState.Recording;
        }

        public void Stop()
        {
            State = RecorderSessionState.Off;
        }

        public void Clear()
        {
            StepCount = 0;
            PersistableStepCount = 0;
            LatestPreview = string.Empty;
            LatestStatus = "Recorded steps cleared.";
            LatestValidationStatus = RecorderValidationStatus.Valid;
        }

        public string ExportPreview()
        {
            return LatestPreview;
        }

        public Task<RecorderSaveResult> SaveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                RecorderSaveResult.Completed(
                    "Saved.",
                    pageFilePath: "MainWindowPage.Recorded.cs",
                    scenarioFilePath: "MainWindowScenariosBase.Recorded.cs",
                    persistedStepCount: PersistableStepCount,
                    skippedStepCount: Math.Max(0, StepCount - PersistableStepCount)));
        }

        public Task<RecorderSaveResult> SaveToDirectoryAsync(string outputDirectory, CancellationToken cancellationToken = default)
        {
            return SaveAsync(cancellationToken);
        }

        public void Dispose()
        {
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "AppAutomation.Recorder.Avalonia.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class TestRecorderWindow : Window
    {
    }
}
