using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using AppAutomation.Recorder.Avalonia.UI;
using Microsoft.Extensions.Logging;
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
            await Assert.That(result.Step.Control.ControlType).IsEqualTo(UiControlType.TextBox);
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo("MixCountSpinner");
        }
    }

    [Test]
    public async Task Resolve_AppliesControlHint_ToCustomAutomationId()
    {
        var options = new AppAutomationRecorderOptions();
        options.ControlHints.Add(new RecorderControlHint(
            "ServerSearchComboBox",
            RecorderActionHint.None,
            UiControlType.ComboBox));
        var resolver = new RecorderSelectorResolver(options);
        var wrapper = new Border();
        AutomationProperties.SetAutomationId(wrapper, "ServerSearchComboBox");

        var result = resolver.Resolve(wrapper, UiControlType.AutomationElement);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Control).IsNotNull();
            await Assert.That(result.Control!.ControlType).IsEqualTo(UiControlType.ComboBox);
            await Assert.That(result.Control.LocatorKind).IsEqualTo(UiLocatorKind.AutomationId);
            await Assert.That(result.Control.LocatorValue).IsEqualTo("ServerSearchComboBox");
            await Assert.That(result.Control.FallbackToName).IsEqualTo(false);
            await Assert.That(result.Control.Warning).Contains("Applied recorder control hint");
        }
    }

    [Test]
    public async Task Resolve_AppliesControlHint_ToNameLocatorMetadata()
    {
        var options = new AppAutomationRecorderOptions { AllowNameLocators = true };
        options.ControlHints.Add(new RecorderControlHint(
            "PART_RealEditor",
            RecorderActionHint.None,
            UiControlType.TextBox,
            UiLocatorKind.Name,
            FallbackToName: true));
        var root = new StackPanel();
        var wrapper = new Border { Name = "PART_RealEditor" };
        root.Children.Add(wrapper);
        var resolver = new RecorderSelectorResolver(options, validationRoot: root);

        var result = resolver.Resolve(wrapper, UiControlType.AutomationElement);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Control).IsNotNull();
            await Assert.That(result.Control!.ControlType).IsEqualTo(UiControlType.TextBox);
            await Assert.That(result.Control.LocatorKind).IsEqualTo(UiLocatorKind.Name);
            await Assert.That(result.Control.LocatorValue).IsEqualTo("PART_RealEditor");
            await Assert.That(result.Control.FallbackToName).IsEqualTo(true);
            await Assert.That(result.Control.Warning).Contains("Applied recorder control hint");
            await Assert.That(result.ValidationStatus).IsEqualTo(RecorderValidationStatus.Warning);
            await Assert.That(result.CanPersist).IsEqualTo(true);
        }
    }

    [Test]
    public async Task SaveAsync_UsesHintedControlDescriptor_InUiControlAttribute()
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
        var recorderOptions = new AppAutomationRecorderOptions { AllowNameLocators = true };
        recorderOptions.ControlHints.Add(new RecorderControlHint(
            "PART_RealEditor",
            RecorderActionHint.None,
            UiControlType.TextBox,
            UiLocatorKind.Name,
            FallbackToName: true));
        var wrapper = new Border { Name = "PART_RealEditor" };
        var resolver = new RecorderSelectorResolver(recorderOptions);
        var resolved = resolver.Resolve(wrapper, UiControlType.AutomationElement);
        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var options = CreateOptions(directory.Path, scenarioName: "Custom Editor Flow");

        var result = await generator.SaveAsync(
            CreateWindowStub(),
            options,
            [
                new RecordedStep(
                    RecordedActionKind.WaitUntilIsEnabled,
                    resolved.Control!,
                    BoolValue: true)
            ],
            outputDirectoryOverride: null);

        using (Assert.Multiple())
        {
            await Assert.That(resolved.Success).IsEqualTo(true);
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.PageFilePath).IsNotNull();
            await Assert.That(result.ScenarioFilePath).IsNotNull();
        }

        var pageSource = await File.ReadAllTextAsync(result.PageFilePath!);
        var scenarioSource = await File.ReadAllTextAsync(result.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(pageSource.Contains(
                "[UiControl(\"PART_RealEditor\", UiControlType.TextBox, \"PART_RealEditor\", LocatorKind = UiLocatorKind.Name, FallbackToName = true)]",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.WaitUntilIsEnabled(static page => page.PART_RealEditor, true);",
                StringComparison.Ordinal)).IsEqualTo(true);
        }
    }

    [Test]
    public async Task TryCreateSearchPickerStep_WithConfiguredParts_CapturesCompositeAction()
    {
        var options = CreateSearchPickerOptions();
        var factory = new RecorderStepFactory(options);
        var searchInput = new TextBox { Text = "least" };
        var results = new ComboBox
        {
            ItemsSource = new[] { "Greatest Common Divisor", "Least Common Multiple" },
            SelectedItem = "Least Common Multiple"
        };
        AutomationProperties.SetAutomationId(searchInput, "HistoryFilterInput");
        AutomationProperties.SetAutomationId(results, "OperationCombo");

        var result = factory.TryCreateSearchPickerStep(searchInput, results);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.SearchAndSelect);
            await Assert.That(result.Step.Control.ControlType).IsEqualTo(UiControlType.SearchPicker);
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo("HistoryOperationPicker");
            await Assert.That(result.Step.StringValue).IsEqualTo("least");
            await Assert.That(result.Step.ItemValue).IsEqualTo("Least Common Multiple");
            await Assert.That(result.Step.CanPersist).IsEqualTo(true);
        }
    }

    [Test]
    public async Task TryCreateSearchPickerStep_WithoutHint_ReturnsUnsupported()
    {
        var factory = new RecorderStepFactory(new AppAutomationRecorderOptions());
        var searchInput = new TextBox { Text = "least" };
        var results = new ComboBox
        {
            ItemsSource = new[] { "Least Common Multiple" },
            SelectedItem = "Least Common Multiple"
        };
        AutomationProperties.SetAutomationId(searchInput, "HistoryFilterInput");
        AutomationProperties.SetAutomationId(results, "OperationCombo");

        var result = factory.TryCreateSearchPickerStep(searchInput, results);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(false);
            await Assert.That(result.Message).Contains("not configured");
        }
    }

    [Test]
    public async Task SaveAsync_UsesSearchPickerStep_InGeneratedScenario()
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
        var factory = new RecorderStepFactory(CreateSearchPickerOptions());
        var searchInput = new TextBox { Text = "least" };
        var results = new ComboBox
        {
            ItemsSource = new[] { "Greatest Common Divisor", "Least Common Multiple" },
            SelectedItem = "Least Common Multiple"
        };
        AutomationProperties.SetAutomationId(searchInput, "HistoryFilterInput");
        AutomationProperties.SetAutomationId(results, "OperationCombo");
        var stepResult = factory.TryCreateSearchPickerStep(searchInput, results);
        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var options = CreateOptions(directory.Path, scenarioName: "Search Picker Flow");

        var result = await generator.SaveAsync(
            CreateWindowStub(),
            options,
            [stepResult.Step!],
            outputDirectoryOverride: null);

        using (Assert.Multiple())
        {
            await Assert.That(stepResult.Success).IsEqualTo(true);
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.PageFilePath).IsNotNull();
            await Assert.That(result.ScenarioFilePath).IsNotNull();
        }

        var pageSource = await File.ReadAllTextAsync(result.PageFilePath!);
        var scenarioSource = await File.ReadAllTextAsync(result.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(pageSource.Contains(
                "[UiControl(\"HistoryOperationPicker\", UiControlType.SearchPicker, \"HistoryOperationPicker\", FallbackToName = false)]",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.SearchAndSelect(static page => page.HistoryOperationPicker, \"least\", \"Least Common Multiple\");",
                StringComparison.Ordinal)).IsEqualTo(true);
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
    public async Task Resolve_MapsLocatorAlias_ToStableAutomationAnchor()
    {
        var options = new AppAutomationRecorderOptions();
        options.LocatorAliases.Add(new RecorderLocatorAlias("EremexDemoDataGridControl", "EremexDemoDataGrid"));
        var root = new StackPanel();
        var eremexAnchor = new TextBlock { Text = "Eremex DataGrid" };
        var eremexVisualControl = new Border();
        AutomationProperties.SetAutomationId(eremexAnchor, "EremexDemoDataGrid");
        AutomationProperties.SetAutomationId(eremexVisualControl, "EremexDemoDataGridControl");
        root.Children.Add(eremexAnchor);
        root.Children.Add(eremexVisualControl);
        var resolver = new RecorderSelectorResolver(options, validationRoot: root);

        var result = resolver.Resolve(eremexVisualControl, UiControlType.AutomationElement);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Control).IsNotNull();
            await Assert.That(result.Control!.LocatorValue).IsEqualTo("EremexDemoDataGrid");
            await Assert.That(result.Control.ProposedPropertyName).IsEqualTo("EremexDemoDataGrid");
            await Assert.That(result.Control.ControlType).IsEqualTo(UiControlType.AutomationElement);
            await Assert.That(result.Control.Warning).Contains("Mapped recorder locator");
            await Assert.That(result.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(result.CanPersist).IsEqualTo(true);
        }
    }

    [Test]
    public async Task Resolve_MapsGridHint_ToTypedAutomationBridge()
    {
        var options = CreateEremexGridOptions();
        var root = new StackPanel();
        var eremexVisualControl = new RecorderGridHost();
        var bridge = new Border();
        AutomationProperties.SetAutomationId(eremexVisualControl, "EremexDemoDataGridControl");
        AutomationProperties.SetAutomationId(bridge, "EremexDemoDataGridAutomationBridge");
        root.Children.Add(eremexVisualControl);
        root.Children.Add(bridge);
        var resolver = new RecorderSelectorResolver(options, validationRoot: root);

        var result = resolver.Resolve(eremexVisualControl, UiControlType.AutomationElement);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Control).IsNotNull();
            await Assert.That(result.Control!.LocatorValue).IsEqualTo("EremexDemoDataGridAutomationBridge");
            await Assert.That(result.Control.ProposedPropertyName).IsEqualTo("EremexDemoDataGridAutomationBridge");
            await Assert.That(result.Control.ControlType).IsEqualTo(UiControlType.Grid);
            await Assert.That(result.Control.Warning).Contains("Mapped recorder locator");
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
            await Assert.That(
                result.ValidationMessage?.Contains("ambiguous", StringComparison.OrdinalIgnoreCase) == true
                || result.ValidationMessage?.Contains("different control", StringComparison.OrdinalIgnoreCase) == true)
                .IsEqualTo(true);
        }
    }

    [Test]
    public async Task Resolve_UsesLiveRootProvider_WhenValidationRootChanges()
    {
        var firstRoot = new StackPanel();
        var secondRoot = new StackPanel();
        var firstButton = new Button { Content = "First" };
        var secondButton = new Button { Content = "Second" };
        AutomationProperties.SetAutomationId(firstButton, "RunButton");
        AutomationProperties.SetAutomationId(secondButton, "RunButton");
        firstRoot.Children.Add(firstButton);
        secondRoot.Children.Add(secondButton);

        Control? currentRoot = firstRoot;
        var resolver = new RecorderSelectorResolver(new AppAutomationRecorderOptions(), () => currentRoot);

        var initialResult = resolver.Resolve(firstButton, UiControlType.Button);
        currentRoot = secondRoot;
        var swappedResult = resolver.Resolve(secondButton, UiControlType.Button);

        using (Assert.Multiple())
        {
            await Assert.That(initialResult.Success).IsEqualTo(true);
            await Assert.That(initialResult.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(swappedResult.Success).IsEqualTo(true);
            await Assert.That(swappedResult.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(swappedResult.CanPersist).IsEqualTo(true);
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
    public async Task TryCreateAssertionStep_WithGridHintRoot_CapturesRowsAtLeast()
    {
        var options = CreateEremexGridOptions();
        var root = new StackPanel();
        var rows = CreateEremexRows();
        var eremexVisualControl = new RecorderGridHost { ItemsSource = rows };
        var bridge = new Border();
        AutomationProperties.SetAutomationId(eremexVisualControl, "EremexDemoDataGridControl");
        AutomationProperties.SetAutomationId(bridge, "EremexDemoDataGridAutomationBridge");
        root.Children.Add(eremexVisualControl);
        root.Children.Add(bridge);
        var factory = new RecorderStepFactory(options, () => root);

        var result = factory.TryCreateAssertionStep(eremexVisualControl, RecorderAssertionMode.Auto);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilGridRowsAtLeast);
            await Assert.That(result.Step.IntValue).IsEqualTo(3);
            await Assert.That(result.Step.Control.ControlType).IsEqualTo(UiControlType.Grid);
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo("EremexDemoDataGridAutomationBridge");
            await Assert.That(result.Step.CanPersist).IsEqualTo(true);
        }
    }

    [Test]
    public async Task TryCreateAssertionStep_WithGridHintCell_CapturesCellValue()
    {
        var options = CreateEremexGridOptions();
        var root = new StackPanel();
        var rows = CreateEremexRows();
        var eremexVisualControl = new RecorderGridHost { ItemsSource = rows };
        var bridge = new Border();
        var cell = new TextBlock
        {
            Text = "EX-13",
            DataContext = rows[2]
        };
        AutomationProperties.SetAutomationId(eremexVisualControl, "EremexDemoDataGridControl");
        AutomationProperties.SetAutomationId(bridge, "EremexDemoDataGridAutomationBridge");
        AutomationProperties.SetAutomationId(cell, "EremexDemoDataGridAutomationBridge_Row2_Cell1");
        eremexVisualControl.Children.Add(cell);
        root.Children.Add(eremexVisualControl);
        root.Children.Add(bridge);
        var factory = new RecorderStepFactory(options, () => root);

        var result = factory.TryCreateAssertionStep(cell, RecorderAssertionMode.Text);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilGridCellEquals);
            await Assert.That(result.Step.RowIndex).IsEqualTo(2);
            await Assert.That(result.Step.ColumnIndex).IsEqualTo(1);
            await Assert.That(result.Step.StringValue).IsEqualTo("EX-13");
            await Assert.That(result.Step.Control.ControlType).IsEqualTo(UiControlType.Grid);
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo("EremexDemoDataGridAutomationBridge");
            await Assert.That(result.Step.CanPersist).IsEqualTo(true);
        }
    }

    [Test]
    public async Task TryCreateAssertionStep_WithGridHintCellAutomationId_UsesCellColumnIndexWhenValuesRepeat()
    {
        var options = CreateEremexGridOptions();
        var root = new StackPanel();
        var rows =
            new[]
            {
                new RecorderGridRow("EX-R1", "EX-Duplicate", "EX-Duplicate")
            };
        var eremexVisualControl = new RecorderGridHost { ItemsSource = rows };
        var bridge = new Border();
        var cell = new TextBlock
        {
            Text = "EX-Duplicate",
            DataContext = rows[0]
        };
        AutomationProperties.SetAutomationId(eremexVisualControl, "EremexDemoDataGridControl");
        AutomationProperties.SetAutomationId(bridge, "EremexDemoDataGridAutomationBridge");
        AutomationProperties.SetAutomationId(cell, "EremexDemoDataGridAutomationBridge_Row0_Cell2");
        eremexVisualControl.Children.Add(cell);
        root.Children.Add(eremexVisualControl);
        root.Children.Add(bridge);
        var factory = new RecorderStepFactory(options, () => root);

        var result = factory.TryCreateAssertionStep(cell, RecorderAssertionMode.Text);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilGridCellEquals);
            await Assert.That(result.Step.RowIndex).IsEqualTo(0);
            await Assert.That(result.Step.ColumnIndex).IsEqualTo(2);
            await Assert.That(result.Step.StringValue).IsEqualTo("EX-Duplicate");
        }
    }

    [Test]
    public async Task TryCreateAssertionStep_WithAmbiguousGridHintCellText_DoesNotGuessColumn()
    {
        var options = CreateEremexGridOptions();
        var root = new StackPanel();
        var rows =
            new[]
            {
                new RecorderGridRow("EX-R1", "EX-Duplicate", "EX-Duplicate")
            };
        var eremexVisualControl = new RecorderGridHost { ItemsSource = rows };
        var bridge = new Border();
        var cell = new TextBlock
        {
            Text = "EX-Duplicate",
            DataContext = rows[0]
        };
        AutomationProperties.SetAutomationId(eremexVisualControl, "EremexDemoDataGridControl");
        AutomationProperties.SetAutomationId(bridge, "EremexDemoDataGridAutomationBridge");
        eremexVisualControl.Children.Add(cell);
        root.Children.Add(eremexVisualControl);
        root.Children.Add(bridge);
        var factory = new RecorderStepFactory(options, () => root);

        var result = factory.TryCreateAssertionStep(cell, RecorderAssertionMode.Text);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilGridRowsAtLeast);
            await Assert.That(result.Step.IntValue).IsEqualTo(1);
        }
    }

    [Test]
    public async Task TryCreateGridActionStep_WithConfiguredHints_CapturesGridUserActions()
    {
        var options = CreateEremexGridActionOptions();
        var root = new StackPanel();
        var rows = CreateEremexRows();
        var eremexVisualControl = new RecorderGridHost { ItemsSource = rows };
        var bridge = new Border();
        var openCell = new TextBlock { Text = "EX-R3", DataContext = rows[2] };
        var copyCell = new TextBlock { Text = "EX-13", DataContext = rows[2] };
        var header = new TextBlock { Text = "Value" };
        var loadMoreButton = new Button { Content = "Load more" };
        var exportButton = new Button { Content = "Export" };
        AutomationProperties.SetAutomationId(eremexVisualControl, "EremexDemoDataGridControl");
        AutomationProperties.SetAutomationId(bridge, "EremexDemoDataGridAutomationBridge");
        AutomationProperties.SetAutomationId(openCell, "EremexDemoDataGridAutomationBridge_Row2_Cell0");
        AutomationProperties.SetAutomationId(copyCell, "EremexDemoDataGridAutomationBridge_Row2_Cell1");
        AutomationProperties.SetAutomationId(header, "EremexDemoDataGridAutomationBridge_HeaderValue");
        AutomationProperties.SetAutomationId(loadMoreButton, "EremexDemoDataGridLoadMoreButton");
        AutomationProperties.SetAutomationId(exportButton, "EremexDemoDataGridExportButton");
        eremexVisualControl.Children.Add(openCell);
        eremexVisualControl.Children.Add(copyCell);
        root.Children.Add(eremexVisualControl);
        root.Children.Add(header);
        root.Children.Add(loadMoreButton);
        root.Children.Add(exportButton);
        root.Children.Add(bridge);
        var factory = new RecorderStepFactory(options, () => root);

        var openResult = factory.TryCreateGridActionStep(openCell);
        var sortResult = factory.TryCreateGridActionStep(header);
        var scrollResult = factory.TryCreateGridActionStep(loadMoreButton);
        var copyResult = factory.TryCreateGridActionStep(copyCell);
        var exportResult = factory.TryCreateGridActionStep(exportButton);

        using (Assert.Multiple())
        {
            await Assert.That(openResult.Success).IsEqualTo(true);
            await Assert.That(openResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.OpenGridRow);
            await Assert.That(openResult.Step.RowIndex).IsEqualTo(2);
            await Assert.That(openResult.Step.Control.LocatorValue).IsEqualTo("EremexDemoDataGridAutomationBridge");

            await Assert.That(sortResult.Success).IsEqualTo(true);
            await Assert.That(sortResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.SortGridByColumn);
            await Assert.That(sortResult.Step.StringValue).IsEqualTo("Value");

            await Assert.That(scrollResult.Success).IsEqualTo(true);
            await Assert.That(scrollResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.ScrollGridToEnd);

            await Assert.That(copyResult.Success).IsEqualTo(true);
            await Assert.That(copyResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.CopyGridCell);
            await Assert.That(copyResult.Step.RowIndex).IsEqualTo(2);
            await Assert.That(copyResult.Step.ColumnIndex).IsEqualTo(1);

            await Assert.That(exportResult.Success).IsEqualTo(true);
            await Assert.That(exportResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.ExportGrid);
        }
    }

    [Test]
    public async Task TryCreateGridActionStep_OpenRowWithoutRowContext_ReturnsDiagnostic()
    {
        var options = CreateEremexGridOptions();
        options.GridActionHints.Add(new RecorderGridActionHint(
            "EremexDemoDataGridOpenButton",
            "EremexDemoDataGridAutomationBridge",
            RecorderGridUserActionKind.OpenRow));
        var openButton = new Button { Content = "Open" };
        AutomationProperties.SetAutomationId(openButton, "EremexDemoDataGridOpenButton");
        var factory = new RecorderStepFactory(options, validationRootProvider: null);

        var result = factory.TryCreateGridActionStep(openButton);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(false);
            await Assert.That(result.Message).Contains("requires a row index");
        }
    }

    [Test]
    public async Task RecorderSession_CapturesConfiguredGridExportButton_InsteadOfGenericClick()
    {
        var options = CreateEremexGridActionOptions();
        var root = new StackPanel();
        var bridge = new Border();
        var exportButton = new Button { Content = "Export" };
        AutomationProperties.SetAutomationId(bridge, "EremexDemoDataGridAutomationBridge");
        AutomationProperties.SetAutomationId(exportButton, "EremexDemoDataGridExportButton");
        root.Children.Add(exportButton);
        root.Children.Add(bridge);
        var session = new RecorderSession(CreateWindowStub(), options, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.CaptureButtonClickForTesting(exportButton);

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(1);
            await Assert.That(details.StepJournal[0].Preview).Contains("Page.ExportGrid(static page => page.EremexDemoDataGridAutomationBridge);");
            await Assert.That(details.StepJournal[0].Preview.Contains("Page.ClickButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(details.StepJournal[0].ValidationStatus).IsEqualTo(RecorderValidationStatus.Warning);
            await Assert.That(details.StepJournal[0].CanPersist).IsEqualTo(true);
            await Assert.That(details.StepJournal[0].Preview).Contains("grid-user-action-adapter-required");
        }
    }

    [Test]
    public async Task RuntimeValidator_ButtonCommand_PassesHeadlessAndFlaUIReadiness()
    {
        var validator = new RecorderCommandRuntimeValidator(new AppAutomationRecorderOptions());
        var step = new RecordedStep(
            RecordedActionKind.ClickButton,
            new RecordedControlDescriptor(
                "RunButton",
                UiControlType.Button,
                "RunButton",
                UiLocatorKind.AutomationId,
                FallbackToName: false,
                AvaloniaTypeName: typeof(Button).FullName ?? nameof(Button),
                Warning: null));

        var result = validator.Validate(step);

        using (Assert.Multiple())
        {
            await Assert.That(result.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(result.CanPersist).IsEqualTo(true);
            await Assert.That(result.RuntimeValidationFindings?.Count).IsEqualTo(2);
            await Assert.That(result.RuntimeValidationFindings!.All(static finding => finding.Severity == RecorderRuntimeValidationSeverity.Info)).IsEqualTo(true);
        }
    }

    [Test]
    public async Task RuntimeValidator_InvalidActionValidation_DoesNotReportTargetSupported()
    {
        var validator = new RecorderCommandRuntimeValidator(new AppAutomationRecorderOptions());
        var step = new RecordedStep(
            RecordedActionKind.ClickButton,
            new RecordedControlDescriptor(
                "RunButton",
                UiControlType.AutomationElement,
                "RunButton",
                UiLocatorKind.AutomationId,
                FallbackToName: false,
                AvaloniaTypeName: typeof(Border).FullName ?? nameof(Border),
                Warning: null),
            ValidationStatus: RecorderValidationStatus.Invalid,
            ValidationMessage: "Captured source is not compatible with action ClickButton.",
            CanPersist: false);

        var result = validator.Validate(step);

        using (Assert.Multiple())
        {
            await Assert.That(result.ValidationStatus).IsEqualTo(RecorderValidationStatus.Invalid);
            await Assert.That(result.CanPersist).IsEqualTo(false);
            await Assert.That(result.ValidationMessage).IsEqualTo("Captured source is not compatible with action ClickButton.");
            await Assert.That(result.RuntimeValidationFindings).IsNotNull();
            await Assert.That(result.RuntimeValidationFindings!.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task RuntimeValidation_MissingPayload_BlocksAllTargetsAndLogsDiagnostics()
    {
        var logger = new TestLogger();
        var options = new AppAutomationRecorderOptions { ShowOverlay = false, Logger = logger };
        var root = new StackPanel();
        var button = new Button { Content = "Run" };
        AutomationProperties.SetAutomationId(button, "RunButton");
        root.Children.Add(button);
        var session = new RecorderSession(CreateWindowStub(), options, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;
        var stepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(
            new RecordedStep(
                RecordedActionKind.WaitUntilIsEnabled,
                new RecordedControlDescriptor(
                    "RunButton",
                    UiControlType.Button,
                    "RunButton",
                    UiLocatorKind.AutomationId,
                    FallbackToName: false,
                    AvaloniaTypeName: typeof(Button).FullName ?? nameof(Button),
                    Warning: null),
                StepId: stepId));

        var retried = details.RetryStepValidation(stepId);

        using (Assert.Multiple())
        {
            await Assert.That(retried).IsEqualTo(true);
            await Assert.That(details.StepJournal[0].ValidationStatus).IsEqualTo(RecorderValidationStatus.Invalid);
            await Assert.That(details.StepJournal[0].CanPersist).IsEqualTo(false);
            await Assert.That(details.StepJournal[0].StatusMessage).Contains("Headless validation failed");
            await Assert.That(details.StepJournal[0].StatusMessage).Contains("FlaUI validation failed");
            await Assert.That(logger.Entries.Any(static entry =>
                entry.EventId.Id == RecorderDiagnosticsEventIds.RuntimeValidationFailed.Id
                && entry.Message.Contains("payload-missing-bool", StringComparison.Ordinal)
                && entry.Message.Contains("RecordedCommand", StringComparison.Ordinal))).IsEqualTo(true);
        }
    }

    [Test]
    public async Task RecorderSession_UnsupportedCapture_LogsControlSnapshotAndTreePaths()
    {
        var logger = new TestLogger();
        var root = new StackPanel();
        var unsupported = new Border();
        AutomationProperties.SetAutomationId(unsupported, "UnsupportedBorder");
        root.Children.Add(unsupported);
        var session = new RecorderSession(
            CreateWindowStub(),
            new AppAutomationRecorderOptions { ShowOverlay = false, Logger = logger },
            () => root,
            attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.CaptureButtonClickForTesting(unsupported);

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(0);
            await Assert.That(logger.Entries.Any(static entry =>
                entry.EventId.Id == RecorderDiagnosticsEventIds.CaptureFailed.Id
                && entry.Message.Contains("UnsupportedBorder", StringComparison.Ordinal)
                && entry.Message.Contains("ControlSnapshot", StringComparison.Ordinal)
                && entry.Message.Contains("VisualPath", StringComparison.Ordinal)
                && entry.Message.Contains("LogicalPath", StringComparison.Ordinal))).IsEqualTo(true);
        }
    }

    [Test]
    public async Task RecorderSession_DiagnosticLogFileToggle_WritesDiagnosticsToFile()
    {
        using var directory = new TemporaryDirectory();
        var logPath = Path.Combine(directory.Path, "recorder-diagnostics.log");
        var root = new StackPanel();
        var unsupported = new Border();
        AutomationProperties.SetAutomationId(unsupported, "UnsupportedBorder");
        root.Children.Add(unsupported);
        var session = new RecorderSession(
            CreateWindowStub(),
            new AppAutomationRecorderOptions
            {
                ShowOverlay = false,
                DiagnosticLog = new RecorderDiagnosticLogOptions
                {
                    FilePath = logPath
                }
            },
            () => root,
            attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.CaptureButtonClickForTesting(unsupported);
        var disabledLogExists = File.Exists(logPath);

        details.SetDiagnosticLogFileEnabled(true);
        session.CaptureButtonClickForTesting(unsupported);

        var logSource = await File.ReadAllTextAsync(logPath);

        using (Assert.Multiple())
        {
            await Assert.That(disabledLogExists).IsEqualTo(false);
            await Assert.That(details.IsDiagnosticLogFileEnabled).IsEqualTo(true);
            await Assert.That(details.DiagnosticLogFilePath).IsEqualTo(logPath);
            await Assert.That(details.DiagnosticLogEntryCount).IsEqualTo(1);
            await Assert.That(logSource).Contains("EventId=4101");
            await Assert.That(logSource).Contains("UnsupportedBorder");
            await Assert.That(logSource).Contains("ControlSnapshot");
            await Assert.That(logSource).Contains("VisualPath");
            await Assert.That(logSource).Contains("LogicalPath");
        }
    }

    [Test]
    public async Task RecorderSession_ActionValidationFailure_LogsDiagnosticsAndRemainsNonPersistable()
    {
        var logger = new TestLogger();
        var root = new StackPanel();
        var container = new Border();
        var button = new Button { Content = "Run" };
        AutomationProperties.SetAutomationId(container, "RunButton");
        container.Child = button;
        root.Children.Add(container);

        var session = new RecorderSession(
            CreateWindowStub(),
            new AppAutomationRecorderOptions { ShowOverlay = false, Logger = logger },
            () => root,
            attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.CaptureButtonClickForTesting(button);

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(1);
            await Assert.That(details.StepJournal[0].ValidationStatus).IsEqualTo(RecorderValidationStatus.Invalid);
            await Assert.That(details.StepJournal[0].CanPersist).IsEqualTo(false);
            await Assert.That(logger.Entries.Any(static entry =>
                entry.EventId.Id == RecorderDiagnosticsEventIds.ActionValidationFailed.Id
                && entry.Message.Contains("not compatible", StringComparison.OrdinalIgnoreCase)
                && entry.Message.Contains("RecordedCommand", StringComparison.Ordinal))).IsEqualTo(true);
        }
    }

    [Test]
    public async Task RecorderSession_RuntimeValidationCanBeDisabled_ForLegacyValidationOutcome()
    {
        var options = CreateEremexGridActionOptions(validateRuntimeTargets: false);
        var root = new StackPanel();
        var bridge = new Border();
        var exportButton = new Button { Content = "Export" };
        AutomationProperties.SetAutomationId(bridge, "EremexDemoDataGridAutomationBridge");
        AutomationProperties.SetAutomationId(exportButton, "EremexDemoDataGridExportButton");
        root.Children.Add(exportButton);
        root.Children.Add(bridge);
        var session = new RecorderSession(CreateWindowStub(), options, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.CaptureButtonClickForTesting(exportButton);

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(1);
            await Assert.That(details.StepJournal[0].ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(details.StepJournal[0].CanPersist).IsEqualTo(true);
            await Assert.That(details.StepJournal[0].Preview).DoesNotContain("grid-user-action-adapter-required");
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
    public async Task RecorderSession_CapturesTextFromLateAttachedObservedControls()
    {
        var root = new StackPanel();
        var options = new AppAutomationRecorderOptions { ShowOverlay = false };
        var session = new RecorderSession(CreateWindowStub(), options, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;
        var textBox = new TextBox();
        AutomationProperties.SetAutomationId(textBox, "SearchBox");

        session.Start();
        session.RefreshObservedControlsForTesting();
        root.Children.Add(textBox);
        session.RefreshObservedControlsForTesting();
        session.RegisterKeyboardInputForTesting(textBox);
        textBox.Text = "Alpha";
        session.FlushPendingStateForTesting();

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(1);
            await Assert.That(details.StepJournal[0].Preview).Contains("Page.EnterText(static page => page.SearchBox, \"Alpha\");");
            await Assert.That(details.StepJournal[0].ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
        }
    }

    [Test]
    public async Task RecorderSession_CapturesDeleteOnlyTextEdits_ViaTextPropertyChanges()
    {
        var root = new StackPanel();
        var options = new AppAutomationRecorderOptions { ShowOverlay = false };
        var session = new RecorderSession(CreateWindowStub(), options, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;
        var textBox = new TextBox { Text = "Seed" };
        AutomationProperties.SetAutomationId(textBox, "QueryBox");
        root.Children.Add(textBox);

        session.Start();
        session.RefreshObservedControlsForTesting();
        session.RegisterKeyboardInputForTesting(textBox);
        textBox.Text = string.Empty;
        session.FlushPendingStateForTesting();

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(1);
            await Assert.That(details.StepJournal[0].Preview).Contains("Page.EnterText(static page => page.QueryBox, \"\");");
            await Assert.That(details.StepJournal[0].CanPersist).IsEqualTo(true);
        }
    }

    [Test]
    public async Task RecorderSession_SuppressesConfiguredGridCellEditorTextEntry()
    {
        var options = CreateEremexGridOptions();
        var root = new StackPanel();
        var grid = new RecorderGridHost();
        var editor = new TextBox { Name = "PART_RealEditor" };
        AutomationProperties.SetAutomationId(grid, "EremexDemoDataGridControl");
        grid.Children.Add(editor);
        root.Children.Add(grid);
        var session = new RecorderSession(CreateWindowStub(), options, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.RefreshObservedControlsForTesting();
        session.RegisterKeyboardInputForTesting(editor);
        editor.Text = "Edited grid value";
        session.FlushPendingStateForTesting();

        await Assert.That(details.StepJournal.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RecorderSession_SuppressesComboBoxTemplateTextEntry()
    {
        var root = new StackPanel();
        var comboBox = new ComboBox();
        var templateTextBox = new TextBox { Name = "PART_EditableTextBox" };
        AutomationProperties.SetAutomationId(comboBox, "ArmSearchResults");
        SetTemplatedParentForTesting(templateTextBox, comboBox);
        root.Children.Add(comboBox);
        root.Children.Add(templateTextBox);
        var session = new RecorderSession(CreateWindowStub(), new AppAutomationRecorderOptions { ShowOverlay = false }, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.RefreshObservedControlsForTesting();
        session.RegisterKeyboardInputForTesting(templateTextBox);
        templateTextBox.Text = "Template text";
        session.FlushPendingStateForTesting();

        await Assert.That(details.StepJournal.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RecorderSession_SuppressesDatePickerTemplateButtonClick()
    {
        var root = new StackPanel();
        var datePicker = new DatePicker();
        var flyoutButton = new Button { Name = "PART_FlyoutButton" };
        AutomationProperties.SetAutomationId(datePicker, "ArmDateRangeTo");
        SetTemplatedParentForTesting(flyoutButton, datePicker);
        root.Children.Add(datePicker);
        root.Children.Add(flyoutButton);
        var session = new RecorderSession(CreateWindowStub(), new AppAutomationRecorderOptions { ShowOverlay = false }, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.CaptureButtonClickForTesting(flyoutButton);

        await Assert.That(details.StepJournal.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RecorderSession_CapturesButtonClick_FromNestedButtonContent()
    {
        var root = new StackPanel();
        var nestedText = new TextBlock { Text = "Run" };
        var button = new Button { Content = nestedText };
        AutomationProperties.SetAutomationId(button, "CalculateButton");
        root.Children.Add(button);

        var session = new RecorderSession(CreateWindowStub(), new AppAutomationRecorderOptions { ShowOverlay = false }, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.CaptureButtonClickForTesting(nestedText);

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(1);
            await Assert.That(details.StepJournal[0].Preview).Contains("Page.ClickButton(static page => page.CalculateButton);");
            await Assert.That(details.StepJournal[0].ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
        }
    }

    [Test]
    public async Task RecorderSession_RevalidatesButtonActionImmediately_WhenLocatorTargetsNonClickableAncestor()
    {
        var root = new StackPanel();
        var container = new Border();
        var button = new Button { Content = "Run" };
        AutomationProperties.SetAutomationId(container, "RunButton");
        container.Child = button;
        root.Children.Add(container);

        var session = new RecorderSession(CreateWindowStub(), new AppAutomationRecorderOptions { ShowOverlay = false }, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.CaptureButtonClickForTesting(button);

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(1);
            await Assert.That(details.StepJournal[0].ValidationStatus).IsEqualTo(RecorderValidationStatus.Invalid);
            await Assert.That(details.StepJournal[0].CanPersist).IsEqualTo(false);
            await Assert.That(details.StepJournal[0].StatusMessage.Contains("not compatible", StringComparison.OrdinalIgnoreCase)).IsEqualTo(true);
        }
    }

    [Test]
    public async Task RecorderSession_CapturesComboAndListSelection_WhenTriggeredByRecordedInput()
    {
        var root = new StackPanel();
        var comboBox = new ComboBox
        {
            ItemsSource = new[] { "GCD", "LCM" },
            SelectedItem = "LCM"
        };
        var listBox = new ListBox
        {
            ItemsSource = new[] { "Prime", "Fibonacci" },
            SelectedItem = "Fibonacci"
        };
        AutomationProperties.SetAutomationId(comboBox, "OperationCombo");
        AutomationProperties.SetAutomationId(listBox, "SeriesList");
        root.Children.Add(comboBox);
        root.Children.Add(listBox);

        var session = new RecorderSession(CreateWindowStub(), new AppAutomationRecorderOptions { ShowOverlay = false }, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.RegisterPointerInputFromSourceForTesting(comboBox);
        session.CaptureComboBoxSelectionForTesting(comboBox);
        session.RegisterPointerInputFromSourceForTesting(listBox);
        session.CaptureListBoxSelectionForTesting(listBox);

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(2);
            await Assert.That(details.StepJournal[0].Preview).Contains("Page.SelectComboItem(static page => page.OperationCombo, \"LCM\");");
            await Assert.That(details.StepJournal[1].Preview).Contains("Page.SelectListBoxItem(static page => page.SeriesList, \"Fibonacci\");");
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
            LatestValidationStatus = RecorderValidationStatus.Warning,
            CurrentScenarioFilePath = @"C:\Recorder\Recorded\MainWindowScenariosBase.RecordedSmoke.<timestamp>.g.cs"
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
        var scenarioPathText = overlay.FindControl<TextBlock>("ScenarioPathText");

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
            await Assert.That(scenarioPathText).IsNotNull();
            await Assert.That(scenarioPathText!.Text).IsEqualTo(@"C:\Recorder\Recorded\MainWindowScenariosBase.RecordedSmoke.<timestamp>.g.cs");
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
    public async Task Overlay_DiagnosticLogToggle_UpdatesSessionAndShowsPath()
    {
        var session = new FakeRecorderSession
        {
            DiagnosticLogFilePath = @"C:\Recorder\Recorded\recorder-diagnostics.log",
            DiagnosticLogEntryCount = 2
        };
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());

        var checkBox = overlay.FindControl<CheckBox>("DiagnosticLogCheckBox");
        var pathText = overlay.FindControl<TextBlock>("DiagnosticLogPathText");
        var copyPathButton = overlay.FindControl<Button>("CopyDiagnosticLogPathButton");

        checkBox!.IsChecked = true;
        checkBox.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        using (Assert.Multiple())
        {
            await Assert.That(checkBox).IsNotNull();
            await Assert.That(pathText).IsNotNull();
            await Assert.That(copyPathButton).IsNotNull();
            await Assert.That(session.IsDiagnosticLogFileEnabled).IsEqualTo(true);
            await Assert.That(pathText!.Text).Contains(@"C:\Recorder\Recorded\recorder-diagnostics.log");
            await Assert.That(copyPathButton!.IsEnabled).IsEqualTo(true);
        }
    }

    [Test]
    public async Task GetOverlayWindowConfiguration_UsesStandaloneOpaqueWindowConfiguration()
    {
        var configuration = AppAutomationRecorder.GetOverlayWindowConfiguration(
            new AppAutomationRecorderOptions
            {
                OverlayTheme = RecorderOverlayTheme.Dark
            });

        using (Assert.Multiple())
        {
            await Assert.That(configuration.ShowInTaskbar).IsEqualTo(true);
            await Assert.That(configuration.Topmost).IsEqualTo(false);
            await Assert.That(configuration.SystemDecorations).IsEqualTo(SystemDecorations.Full);
            await Assert.That(configuration.WindowStartupLocation).IsEqualTo(WindowStartupLocation.CenterScreen);
            await Assert.That(configuration.SizeToContent).IsEqualTo(SizeToContent.Manual);
            await Assert.That(configuration.CanResize).IsEqualTo(true);
            await Assert.That(configuration.Width).IsEqualTo(1080d);
            await Assert.That(configuration.Height).IsEqualTo(760d);
            await Assert.That(configuration.MinWidth).IsEqualTo(760d);
            await Assert.That(configuration.MinHeight).IsEqualTo(420d);
            await Assert.That(configuration.BackgroundColor.A).IsEqualTo((byte)255);
            await Assert.That(configuration.ThemeVariant).IsEqualTo(ThemeVariant.Dark);
            await Assert.That(configuration.BackgroundColor).IsEqualTo(Color.Parse("#18212B"));
        }
    }

    [Test]
    public async Task Overlay_Attach_AppliesDarkPaletteResources()
    {
        var overlay = new RecorderOverlay();
        overlay.Attach(
            new FakeRecorderSession(),
            new AppAutomationRecorderOptions
            {
                OverlayTheme = RecorderOverlayTheme.Dark
            });

        var foundBackground = overlay.TryFindResource("RecorderOverlayBackground", out var overlayBackground);
        var foundSurface = overlay.TryFindResource("RecorderSurfaceBackground", out var surfaceBackground);
        var foundText = overlay.TryFindResource("RecorderText", out var textBrush);

        using (Assert.Multiple())
        {
            await Assert.That(foundBackground).IsEqualTo(true);
            await Assert.That(foundSurface).IsEqualTo(true);
            await Assert.That(foundText).IsEqualTo(true);
            await Assert.That(overlayBackground is ISolidColorBrush).IsEqualTo(true);
            await Assert.That(surfaceBackground is ISolidColorBrush).IsEqualTo(true);
            await Assert.That(textBrush is ISolidColorBrush).IsEqualTo(true);
            await Assert.That(((ISolidColorBrush)overlayBackground!).Color).IsEqualTo(Color.Parse("#18212B"));
            await Assert.That(((ISolidColorBrush)surfaceBackground!).Color).IsEqualTo(Color.Parse("#0F172A"));
            await Assert.That(((ISolidColorBrush)textBrush!).Color).IsEqualTo(Color.Parse("#E2E8F0"));
        }
    }

    [Test]
    public async Task Overlay_RendersStepJournal_BusySummary_AndReviewActions()
    {
        var firstStepId = Guid.NewGuid();
        var secondStepId = Guid.NewGuid();
        var session = new FakeRecorderSession
        {
            StepCount = 3,
            PersistableStepCount = 1,
            LatestStatus = "Save in progress...",
            LatestPreview = "Page.EnterText(static page => page.SearchBox, \"Alpha\");",
            LatestValidationStatus = RecorderValidationStatus.Warning,
            IsBusy = true,
            BusyDescription = "Save...",
            SessionSummary = "1/3 steps | 1 warnings | 1 invalid | save..."
        };
        session.SetJournal(
        [
            new RecorderStepJournalEntry(
                firstStepId,
                "Page.EnterText(static page => page.SearchBox, \"Alpha\");",
                "Ready to persist.",
                RecorderValidationStatus.Valid,
                CanPersist: true,
                IsIgnored: false,
                RecorderStepReviewState.Active,
                FailureCode: null,
                LastValidationAt: DateTimeOffset.UtcNow),
            new RecorderStepJournalEntry(
                secondStepId,
                "Page.ClickButton(static page => page.RunButton);",
                "Selector is ambiguous.",
                RecorderValidationStatus.Invalid,
                CanPersist: false,
                IsIgnored: false,
                RecorderStepReviewState.NeedsReview,
                FailureCode: "validation-invalid",
                LastValidationAt: DateTimeOffset.UtcNow)
        ]);

        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());

        var summary = overlay.FindControl<TextBlock>("SessionSummaryText");
        var saveButton = overlay.FindControl<Button>("SaveButton");
        var exportButton = overlay.FindControl<Button>("ExportButton");
        var journalEmpty = overlay.FindControl<TextBlock>("JournalEmptyText");
        var journalPanel = overlay.FindControl<Panel>("StepJournalPanel");

        using (Assert.Multiple())
        {
            await Assert.That(summary).IsNotNull();
            await Assert.That(summary!.Text).IsEqualTo("1/3 steps | 1 warnings | 1 invalid | save...");
            await Assert.That(saveButton).IsNotNull();
            await Assert.That(saveButton!.IsEnabled).IsEqualTo(false);
            await Assert.That(exportButton).IsNotNull();
            await Assert.That(exportButton!.IsEnabled).IsEqualTo(false);
            await Assert.That(journalEmpty).IsNotNull();
            await Assert.That(journalEmpty!.IsVisible).IsEqualTo(false);
            await Assert.That(journalPanel).IsNotNull();
            await Assert.That(journalPanel!.Children.Count).IsEqualTo(2);
        }

        session.IsBusy = false;
        session.RaiseChanged();
        var refreshedFirstItem = (Border)journalPanel!.Children[0];
        var refreshedContainer = (StackPanel)refreshedFirstItem.Child!;
        var refreshedActions = (StackPanel)refreshedContainer.Children[2];
        var removeButton = (Button)refreshedActions.Children[0];
        var ignoreButton = (Button)refreshedActions.Children[1];
        var retryButton = (Button)refreshedActions.Children[2];
        removeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ignoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        retryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        using (Assert.Multiple())
        {
            await Assert.That(session.RemovedStepIds).Contains(secondStepId);
            await Assert.That(session.IgnoredStepIds).Contains(secondStepId);
            await Assert.That(session.RetriedStepIds).Contains(secondStepId);
        }
    }

    [Test]
    public async Task RecorderSession_SaveAsync_IsSingleFlight()
    {
        var firstSaveRelease = new TaskCompletionSource<RecorderSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCallCount = 0;
        var session = new RecorderSession(
            CreateWindowStub(),
            new AppAutomationRecorderOptions { ShowOverlay = false },
            validationRootProvider: null,
            attachWindowHandlers: false,
            saveOperation: async (_, _, _) =>
            {
                Interlocked.Increment(ref saveCallCount);
                return await firstSaveRelease.Task;
            });

        session.AddRecordedStepForTesting(
            new RecordedStep(
                RecordedActionKind.ClickButton,
                new RecordedControlDescriptor(
                    "RunButton",
                    UiControlType.Button,
                    "RunButton",
                    UiLocatorKind.AutomationId,
                    FallbackToName: false,
                    AvaloniaTypeName: typeof(Button).FullName ?? nameof(Button),
                    Warning: null),
                StepId: Guid.NewGuid()));

        var firstSave = session.SaveAsync();
        var secondSave = await session.SaveAsync();

        using (Assert.Multiple())
        {
            await Assert.That(session.IsBusy).IsEqualTo(true);
            await Assert.That(secondSave.Success).IsEqualTo(false);
            await Assert.That(secondSave.Message).Contains("already in progress");
            await Assert.That(saveCallCount).IsEqualTo(1);
        }

        firstSaveRelease.SetResult(
            RecorderSaveResult.Completed(
                "Saved.",
                pageFilePath: "MainWindowPage.Recorded.cs",
                scenarioFilePath: "Scenario.Recorded.cs",
                persistedStepCount: 1,
                skippedStepCount: 0));
        var completedResult = await firstSave;

        using (Assert.Multiple())
        {
            await Assert.That(completedResult.Success).IsEqualTo(true);
            await Assert.That(session.IsBusy).IsEqualTo(false);
            await Assert.That(saveCallCount).IsEqualTo(1);
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
    public async Task SaveAsync_ReusesAliasedEremexBridge_ForRecorderGeneratedGridAssertions()
    {
        using var directory = new TemporaryDirectory();
        CreateAuthoringProject(
            directory.Path,
            existingPageContent:
            """
            using AppAutomation.Abstractions;

            namespace Sample.Authoring.Pages;

            [UiControl("EremexDemoDataGridAutomationBridge", UiControlType.Grid, "EremexDemoDataGridAutomationBridge", FallbackToName = false)]
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
        var options = CreateOptions(directory.Path, scenarioName: "Eremex Grid Flow");
        IReadOnlyList<RecordedStep> steps =
        [
            new RecordedStep(
                RecordedActionKind.WaitUntilGridRowsAtLeast,
                new RecordedControlDescriptor(
                    "EremexDemoDataGridAutomationBridge",
                    UiControlType.Grid,
                    "EremexDemoDataGridAutomationBridge",
                    UiLocatorKind.AutomationId,
                    FallbackToName: false,
                    AvaloniaTypeName: typeof(Border).FullName ?? nameof(Border),
                    Warning: "Mapped recorder locator 'AutomationId:EremexDemoDataGridControl' to stable locator 'AutomationId:EremexDemoDataGridAutomationBridge'."),
                IntValue: 5),
            new RecordedStep(
                RecordedActionKind.WaitUntilGridCellEquals,
                new RecordedControlDescriptor(
                    "EremexDemoDataGridAutomationBridge",
                    UiControlType.Grid,
                    "EremexDemoDataGridAutomationBridge",
                    UiLocatorKind.AutomationId,
                    FallbackToName: false,
                    AvaloniaTypeName: typeof(TextBlock).FullName ?? nameof(TextBlock),
                    Warning: "Mapped recorder locator 'AutomationId:EremexDemoDataGridControl' to stable locator 'AutomationId:EremexDemoDataGridAutomationBridge'."),
                StringValue: "EX-13",
                RowIndex: 2,
                ColumnIndex: 1)
        ];

        var result = await generator.SaveAsync(CreateWindowStub(), options, steps, outputDirectoryOverride: null);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.PageFilePath).IsNull();
            await Assert.That(result.ScenarioFilePath).IsNotNull();
        }

        var scenarioSource = await File.ReadAllTextAsync(result.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(scenarioSource.Contains("Page.WaitUntilGridRowsAtLeast(static page => page.EremexDemoDataGridAutomationBridge, 5);", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.WaitUntilGridCellEquals(static page => page.EremexDemoDataGridAutomationBridge, 2, 1, \"EX-13\");", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.WaitUntilIsEnabled(static page => page.EremexDemoDataGrid", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("page.EremexDemoDataGridControl", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("UiControl(\"EremexDemoDataGridControl\"", StringComparison.Ordinal)).IsEqualTo(false);
        }
    }

    [Test]
    public async Task SaveAsync_UsesGridUserActions_InGeneratedScenario()
    {
        using var directory = new TemporaryDirectory();
        CreateAuthoringProject(
            directory.Path,
            existingPageContent:
            """
            using AppAutomation.Abstractions;

            namespace Sample.Authoring.Pages;

            [UiControl("EremexDemoDataGridAutomationBridge", UiControlType.Grid, "EremexDemoDataGridAutomationBridge", FallbackToName = false)]
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

        var gridDescriptor = new RecordedControlDescriptor(
            "EremexDemoDataGridAutomationBridge",
            UiControlType.Grid,
            "EremexDemoDataGridAutomationBridge",
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: typeof(Border).FullName ?? nameof(Border),
            Warning: "Recorded grid user action from configured hint.");
        IReadOnlyList<RecordedStep> steps =
        [
            new RecordedStep(RecordedActionKind.OpenGridRow, gridDescriptor, RowIndex: 2),
            new RecordedStep(RecordedActionKind.SortGridByColumn, gridDescriptor, StringValue: "Value"),
            new RecordedStep(RecordedActionKind.ScrollGridToEnd, gridDescriptor),
            new RecordedStep(RecordedActionKind.CopyGridCell, gridDescriptor, RowIndex: 2, ColumnIndex: 1),
            new RecordedStep(RecordedActionKind.ExportGrid, gridDescriptor)
        ];
        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var options = CreateOptions(directory.Path, scenarioName: "Eremex Grid Actions");

        var result = await generator.SaveAsync(CreateWindowStub(), options, steps, outputDirectoryOverride: null);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.PageFilePath).IsNull();
            await Assert.That(result.ScenarioFilePath).IsNotNull();
        }

        var scenarioSource = await File.ReadAllTextAsync(result.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(scenarioSource.Contains("Page.OpenGridRow(static page => page.EremexDemoDataGridAutomationBridge, 2);", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.SortGridByColumn(static page => page.EremexDemoDataGridAutomationBridge, \"Value\");", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.ScrollGridToEnd(static page => page.EremexDemoDataGridAutomationBridge);", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.CopyGridCell(static page => page.EremexDemoDataGridAutomationBridge, 2, 1);", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.ExportGrid(static page => page.EremexDemoDataGridAutomationBridge);", StringComparison.Ordinal)).IsEqualTo(true);
        }
    }

    [Test]
    public async Task SaveAsync_EmitsRuntimeWarningComment_ForPersistableTargetGap()
    {
        using var directory = new TemporaryDirectory();
        CreateAuthoringProject(
            directory.Path,
            existingPageContent:
            """
            using AppAutomation.Abstractions;

            namespace Sample.Authoring.Pages;

            [UiControl("EremexDemoDataGridAutomationBridge", UiControlType.Grid, "EremexDemoDataGridAutomationBridge", FallbackToName = false)]
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

        var gridDescriptor = new RecordedControlDescriptor(
            "EremexDemoDataGridAutomationBridge",
            UiControlType.Grid,
            "EremexDemoDataGridAutomationBridge",
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: typeof(Border).FullName ?? nameof(Border),
            Warning: null);
        IReadOnlyList<RecordedStep> steps =
        [
            new RecordedStep(
                RecordedActionKind.ExportGrid,
                gridDescriptor,
                ValidationStatus: RecorderValidationStatus.Warning,
                ValidationMessage: "Headless validation warning: headless-grid-user-action-adapter-required.",
                RuntimeValidationFindings:
                [
                    new RecorderRuntimeValidationFinding(
                        RecorderRuntimeValidationTarget.Headless,
                        RecorderRuntimeValidationSeverity.Warning,
                        "headless-grid-user-action-adapter-required",
                        "Grid user action requires a runtime grid action adapter.",
                        BlocksTarget: false)
                ])
        ];
        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var options = CreateOptions(directory.Path, scenarioName: "Runtime Warning Comment");

        var result = await generator.SaveAsync(CreateWindowStub(), options, steps, outputDirectoryOverride: null);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.ScenarioFilePath).IsNotNull();
        }

        var scenarioSource = await File.ReadAllTextAsync(result.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(scenarioSource.Contains("// AppAutomation recorder warning: Headless target warning (headless-grid-user-action-adapter-required): Grid user action requires a runtime grid action adapter.", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.ExportGrid(static page => page.EremexDemoDataGridAutomationBridge);", StringComparison.Ordinal)).IsEqualTo(true);
        }
    }

    [Test]
    public async Task SaveAsync_EmitsUnsupportedRuntimeComment_AndPersistsWhenAnotherTargetWorks()
    {
        using var directory = new TemporaryDirectory();
        CreateAuthoringProject(
            directory.Path,
            existingPageContent:
            """
            using AppAutomation.Abstractions;

            namespace Sample.Authoring.Pages;

            [UiControl("RunButton", UiControlType.Button, "RunButton", FallbackToName = false)]
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

        var buttonDescriptor = new RecordedControlDescriptor(
            "RunButton",
            UiControlType.Button,
            "RunButton",
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: typeof(Button).FullName ?? nameof(Button),
            Warning: null);
        IReadOnlyList<RecordedStep> steps =
        [
            new RecordedStep(
                RecordedActionKind.ClickButton,
                buttonDescriptor,
                ValidationStatus: RecorderValidationStatus.Warning,
                ValidationMessage: "Headless validation failed: headless-action-unsupported.",
                RuntimeValidationFindings:
                [
                    new RecorderRuntimeValidationFinding(
                        RecorderRuntimeValidationTarget.Headless,
                        RecorderRuntimeValidationSeverity.Invalid,
                        "headless-action-unsupported",
                        "Recorded action is not supported by Headless.",
                        BlocksTarget: true),
                    new RecorderRuntimeValidationFinding(
                        RecorderRuntimeValidationTarget.FlaUI,
                        RecorderRuntimeValidationSeverity.Info,
                        "flaui-target-supported",
                        "Recorded action is supported by FlaUI readiness validation.",
                        BlocksTarget: false)
                ])
        ];
        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var options = CreateOptions(directory.Path, scenarioName: "Runtime Unsupported Comment");

        var result = await generator.SaveAsync(CreateWindowStub(), options, steps, outputDirectoryOverride: null);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.ScenarioFilePath).IsNotNull();
        }

        var scenarioSource = await File.ReadAllTextAsync(result.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(scenarioSource.Contains("// AppAutomation recorder warning: Headless target unsupported (headless-action-unsupported): Recorded action is not supported by Headless.", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.ClickButton(static page => page.RunButton);", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("flaui-target-supported", StringComparison.Ordinal)).IsEqualTo(false);
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

    private static void SetTemplatedParentForTesting(Control child, Control parent)
    {
        typeof(StyledElement)
            .GetProperty(nameof(StyledElement.TemplatedParent))!
            .SetValue(child, parent);
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

    private static AppAutomationRecorderOptions CreateEremexGridOptions()
    {
        var options = new AppAutomationRecorderOptions();
        options.GridHints.Add(new RecorderGridHint(
            "EremexDemoDataGridControl",
            "EremexDemoDataGridAutomationBridge",
            ["EremexRow", "EremexValue", "EremexParity"]));
        return options;
    }

    private static AppAutomationRecorderOptions CreateEremexGridActionOptions(bool validateRuntimeTargets = true)
    {
        var options = validateRuntimeTargets
            ? CreateEremexGridOptions()
            : new AppAutomationRecorderOptions
            {
                Validation = new RecorderValidationOptions
                {
                    ValidateRuntimeTargets = false
                }
            };
        if (!validateRuntimeTargets)
        {
            options.GridHints.Add(new RecorderGridHint(
                "EremexDemoDataGridControl",
                "EremexDemoDataGridAutomationBridge",
                ["EremexRow", "EremexValue", "EremexParity"]));
        }

        options.GridActionHints.Add(new RecorderGridActionHint(
            "EremexDemoDataGridAutomationBridge_Row2_Cell0",
            "EremexDemoDataGridAutomationBridge",
            RecorderGridUserActionKind.OpenRow));
        options.GridActionHints.Add(new RecorderGridActionHint(
            "EremexDemoDataGridAutomationBridge_HeaderValue",
            "EremexDemoDataGridAutomationBridge",
            RecorderGridUserActionKind.SortByColumn));
        options.GridActionHints.Add(new RecorderGridActionHint(
            "EremexDemoDataGridLoadMoreButton",
            "EremexDemoDataGridAutomationBridge",
            RecorderGridUserActionKind.ScrollToEnd));
        options.GridActionHints.Add(new RecorderGridActionHint(
            "EremexDemoDataGridAutomationBridge_Row2_Cell1",
            "EremexDemoDataGridAutomationBridge",
            RecorderGridUserActionKind.CopyCell));
        options.GridActionHints.Add(new RecorderGridActionHint(
            "EremexDemoDataGridExportButton",
            "EremexDemoDataGridAutomationBridge",
            RecorderGridUserActionKind.Export));
        return options;
    }

    private static AppAutomationRecorderOptions CreateSearchPickerOptions()
    {
        var options = new AppAutomationRecorderOptions();
        options.SearchPickerHints.Add(new RecorderSearchPickerHint(
            "HistoryOperationPicker",
            SearchPickerParts.ByAutomationIds(
                "HistoryFilterInput",
                "OperationCombo",
                applyButtonAutomationId: "ApplyFilterButton")));
        return options;
    }

    private static RecorderGridRow[] CreateEremexRows()
    {
        return
        [
            new("EX-R1", "EX-11", "EX-Odd"),
            new("EX-R2", "EX-12", "EX-Even"),
            new("EX-R3", "EX-13", "EX-Odd")
        ];
    }

    private sealed record RecorderGridRow(string EremexRow, string EremexValue, string EremexParity);

    private sealed class RecorderGridHost : StackPanel
    {
        public IEnumerable<RecorderGridRow>? ItemsSource { get; init; }
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

    private sealed class FakeRecorderSession : IAppAutomationRecorderSession, IAppAutomationRecorderSessionDetails, IRecorderScenarioPathDetails
    {
        private List<RecorderStepJournalEntry> _stepJournal = new();

        public event EventHandler? SessionChanged;

        public RecorderSessionState State { get; set; }

        public int StepCount { get; set; }

        public int PersistableStepCount { get; set; }

        public string LatestPreview { get; set; } = string.Empty;

        public string LatestStatus { get; set; } = string.Empty;

        public RecorderValidationStatus LatestValidationStatus { get; set; } = RecorderValidationStatus.Valid;

        public bool IsBusy { get; set; }

        public string BusyDescription { get; set; } = string.Empty;

        public string SessionSummary { get; set; } = string.Empty;

        public string CurrentScenarioFilePath { get; set; } = string.Empty;

        public bool IsDiagnosticLogFileEnabled { get; set; }

        public string DiagnosticLogFilePath { get; set; } = @"C:\Recorder\Recorded\recorder-diagnostics.log";

        public int DiagnosticLogEntryCount { get; set; }

        public int WarningStepCount => _stepJournal.Count(entry => !entry.IsIgnored && entry.ValidationStatus == RecorderValidationStatus.Warning);

        public int InvalidStepCount => _stepJournal.Count(entry => !entry.IsIgnored && !entry.CanPersist);

        public int IgnoredStepCount => _stepJournal.Count(entry => entry.IsIgnored);

        public IReadOnlyList<RecorderStepJournalEntry> StepJournal => _stepJournal;

        public List<Guid> RemovedStepIds { get; } = new();

        public List<Guid> IgnoredStepIds { get; } = new();

        public List<Guid> RetriedStepIds { get; } = new();

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
            _stepJournal.Clear();
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

        public void RemoveStep(Guid stepId)
        {
            RemovedStepIds.Add(stepId);
            _stepJournal = _stepJournal.Where(entry => entry.StepId != stepId).ToList();
            RaiseChanged();
        }

        public void SetStepIgnored(Guid stepId, bool isIgnored)
        {
            IgnoredStepIds.Add(stepId);
            _stepJournal = _stepJournal
                .Select(entry => entry.StepId == stepId
                    ? entry with
                    {
                        IsIgnored = isIgnored,
                        ReviewState = isIgnored ? RecorderStepReviewState.Ignored : RecorderStepReviewState.Active
                    }
                    : entry)
                .ToList();
            RaiseChanged();
        }

        public bool RetryStepValidation(Guid stepId)
        {
            RetriedStepIds.Add(stepId);
            RaiseChanged();
            return true;
        }

        public void SetDiagnosticLogFileEnabled(bool isEnabled)
        {
            IsDiagnosticLogFileEnabled = isEnabled;
            RaiseChanged();
        }

        public void SetJournal(IReadOnlyList<RecorderStepJournalEntry> entries)
        {
            _stepJournal = entries.ToList();
        }

        public void RaiseChanged()
        {
            SessionChanged?.Invoke(this, EventArgs.Empty);
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

    private sealed class TestLogger : ILogger
    {
        public List<TestLogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new TestLogEntry(logLevel, eventId, formatter(state, exception), exception));
        }
    }

    private sealed record TestLogEntry(LogLevel LogLevel, EventId EventId, string Message, Exception? Exception);

    private sealed class TestRecorderWindow : Window
    {
    }
}
