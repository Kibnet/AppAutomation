using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Avalonia.Automation;
using Avalonia.Controls;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
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
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.Control.LocatorKind).IsEqualTo(UiLocatorKind.AutomationId);
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo("CalculateButton");
            await Assert.That(result.Step.Control.ProposedPropertyName).IsEqualTo("CalculateButton");
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
            await Assert.That(enabledResult.Step).IsNotNull();
            await Assert.That(enabledResult.Step!.Control.LocatorKind).IsEqualTo(UiLocatorKind.Name);
            await Assert.That(enabledResult.Step.Control.LocatorValue).IsEqualTo("ResultText");
            await Assert.That(enabledResult.Step.Control.Warning).Contains("Using Name locator");
            await Assert.That(disabledResult.Success).IsEqualTo(false);
            await Assert.That(disabledResult.Message).Contains("AutomationId locator");
        }
    }

    [Test]
    public async Task SaveAsync_GeneratesOnlyMissingControls_AndRecordedScenario()
    {
        using var directory = new TemporaryDirectory();
        CreateAuthoringProject(
            directory.Path,
            pageIsPartial: true,
            scenarioIsPartial: true,
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
    public async Task SaveAsync_Fails_WhenScenarioClassIsNotPartial()
    {
        using var directory = new TemporaryDirectory();
        CreateAuthoringProject(
            directory.Path,
            pageIsPartial: true,
            scenarioIsPartial: false,
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
        var options = new AppAutomationRecorderOptions
        {
            ScenarioName = scenarioName,
            AuthoringProjectDirectory = authoringProjectDirectory,
            PageNamespace = "Sample.Authoring.Pages",
            PageClassName = "MainWindowPage",
            ScenarioNamespace = "Sample.Authoring.Tests",
            ScenarioClassName = "MainWindowScenariosBase",
            ShowOverlay = false
        };

        return options;
    }

    private static Window CreateWindowStub()
    {
#pragma warning disable SYSLIB0050
        return (Window)FormatterServices.GetUninitializedObject(typeof(TestRecorderWindow));
#pragma warning restore SYSLIB0050
    }

    private static void CreateAuthoringProject(
        string rootPath,
        bool pageIsPartial,
        bool scenarioIsPartial,
        string existingPageContent,
        string existingScenarioContent)
    {
        _ = pageIsPartial;
        _ = scenarioIsPartial;

        var pagesDirectory = Directory.CreateDirectory(Path.Combine(rootPath, "Pages"));
        var testsDirectory = Directory.CreateDirectory(Path.Combine(rootPath, "Tests"));

        File.WriteAllText(Path.Combine(pagesDirectory.FullName, "MainWindowPage.cs"), existingPageContent);
        File.WriteAllText(Path.Combine(testsDirectory.FullName, "MainWindowScenariosBase.cs"), existingScenarioContent);
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
