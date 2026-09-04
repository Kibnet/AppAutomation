using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using AppAutomation.Recorder.Avalonia.UI;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using TUnit.Assertions;
using TUnit.Core;

[assembly: NotInParallel]

namespace AppAutomation.Recorder.Avalonia.Tests;

[NotInParallel("RecorderOverlay")]
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
    public async Task TryCreateSearchPickerStep_WithConfiguredListBackedParts_CapturesCompositeAction()
    {
        var options = CreateListSearchPickerOptions();
        var factory = new RecorderStepFactory(options);
        var searchInput = new TextBox { Text = "least" };
        var results = new ListBox
        {
            ItemsSource = new[] { "Greatest Common Divisor", "Least Common Multiple" },
            SelectedItem = "Least Common Multiple"
        };
        AutomationProperties.SetAutomationId(searchInput, "HistoryFilterInput");
        AutomationProperties.SetAutomationId(results, "OperationResults");

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
    public async Task TryCreateSearchPickerStep_WithConfiguredGridSearchPicker_CapturesGridAction()
    {
        var options = CreateGridSearchPickerOptions(validateRuntimeTargets: false);
        var root = new StackPanel();
        var rows = CreateEremexRows();
        var eremexVisualControl = new RecorderGridHost { ItemsSource = rows };
        var bridge = new Border();
        var editor = new StackPanel { DataContext = rows[1] };
        var searchInput = new TextBox { Text = "prod", DataContext = rows[1] };
        var results = new ListBox
        {
            ItemsSource = new[] { "EX-11", "EX-12" },
            SelectedItem = "EX-12",
            DataContext = rows[1]
        };
        AutomationProperties.SetAutomationId(eremexVisualControl, "EremexDemoDataGridControl");
        AutomationProperties.SetAutomationId(bridge, "EremexDemoDataGridAutomationBridge");
        AutomationProperties.SetAutomationId(editor, "OrderPositionProductEditor");
        AutomationProperties.SetAutomationId(searchInput, "OrderPositionProductEditor_Input");
        AutomationProperties.SetAutomationId(results, "OrderPositionProductEditor_Results");
        editor.Children.Add(searchInput);
        editor.Children.Add(results);
        eremexVisualControl.Children.Add(editor);
        root.Children.Add(eremexVisualControl);
        root.Children.Add(bridge);
        var factory = new RecorderStepFactory(options, () => root);

        var result = factory.TryCreateSearchPickerStep(searchInput, results);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.SearchAndSelectGridCell);
            await Assert.That(result.Step.Control.ControlType).IsEqualTo(UiControlType.Grid);
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo("EremexDemoDataGridAutomationBridge");
            await Assert.That(result.Step.RowIndex).IsEqualTo(1);
            await Assert.That(result.Step.ColumnIndex).IsEqualTo(1);
            await Assert.That(result.Step.StringValue).IsEqualTo("prod");
            await Assert.That(result.Step.ItemValue).IsEqualTo("EX-12");
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
    public async Task TryCreateSearchPickerStep_InsideConfiguredGridWithoutGridHint_ReturnsExplicitDiagnostic()
    {
        var options = CreateEremexGridOptions();
        var root = new StackPanel();
        var rows = CreateEremexRows();
        var eremexVisualControl = new RecorderGridHost { ItemsSource = rows };
        var editor = new StackPanel { DataContext = rows[0] };
        var searchInput = new TextBox { Text = "prod", DataContext = rows[0] };
        var results = new ListBox
        {
            ItemsSource = new[] { "EX-11" },
            SelectedItem = "EX-11",
            DataContext = rows[0]
        };
        AutomationProperties.SetAutomationId(eremexVisualControl, "EremexDemoDataGridControl");
        AutomationProperties.SetAutomationId(editor, "OrderPositionProductEditor");
        AutomationProperties.SetAutomationId(searchInput, "OrderPositionProductEditor_Input");
        AutomationProperties.SetAutomationId(results, "OrderPositionProductEditor_Results");
        editor.Children.Add(searchInput);
        editor.Children.Add(results);
        eremexVisualControl.Children.Add(editor);
        root.Children.Add(eremexVisualControl);
        var factory = new RecorderStepFactory(options, () => root);

        var result = factory.TryCreateSearchPickerStep(searchInput, results);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(false);
            await Assert.That(result.Message).Contains("grid search picker hint");
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
    public async Task SaveAsync_UsesCompositeWorkflowActions_InGeneratedScenario()
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
        var options = CreateOptions(directory.Path, scenarioName: "Composite Flow");
        var dialogDescriptor = new RecordedControlDescriptor(
            "DeleteDialog",
            UiControlType.Dialog,
            "DeleteDialog",
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: typeof(Border).FullName ?? nameof(Border),
            Warning: "Recorded dialog action from configured parts.");
        var notificationDescriptor = new RecordedControlDescriptor(
            "ExportToast",
            UiControlType.Notification,
            "ExportToast",
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: typeof(Border).FullName ?? nameof(Border),
            Warning: "Recorded notification action from configured parts.");
        var shellDescriptor = new RecordedControlDescriptor(
            "Shell",
            UiControlType.ShellNavigation,
            "Shell",
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: typeof(Border).FullName ?? nameof(Border),
            Warning: "Recorded shell navigation action from configured parts.");
        IReadOnlyList<RecordedStep> steps =
        [
            new RecordedStep(RecordedActionKind.ConfirmDialog, dialogDescriptor),
            new RecordedStep(RecordedActionKind.CancelDialog, dialogDescriptor),
            new RecordedStep(RecordedActionKind.DismissDialog, dialogDescriptor),
            new RecordedStep(RecordedActionKind.DismissNotification, notificationDescriptor),
            new RecordedStep(RecordedActionKind.OpenOrActivateShellPane, shellDescriptor, StringValue: "Customers"),
            new RecordedStep(RecordedActionKind.ActivateShellPane, shellDescriptor, StringValue: "Orders")
        ];

        var result = await generator.SaveAsync(CreateWindowStub(), options, steps, outputDirectoryOverride: null);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.PageFilePath).IsNotNull();
            await Assert.That(result.ScenarioFilePath).IsNotNull();
        }

        var pageSource = await File.ReadAllTextAsync(result.PageFilePath!);
        var scenarioSource = await File.ReadAllTextAsync(result.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(pageSource.Contains(
                "[UiControl(\"DeleteDialog\", UiControlType.Dialog, \"DeleteDialog\", FallbackToName = false)]",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(pageSource.Contains(
                "[UiControl(\"ExportToast\", UiControlType.Notification, \"ExportToast\", FallbackToName = false)]",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(pageSource.Contains(
                "[UiControl(\"Shell\", UiControlType.ShellNavigation, \"Shell\", FallbackToName = false)]",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.ConfirmDialog(static page => page.DeleteDialog);", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.CancelDialog(static page => page.DeleteDialog);", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.DismissDialog(static page => page.DeleteDialog);", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.DismissNotification(static page => page.ExportToast);", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.OpenOrActivateShellPane(static page => page.Shell, \"Customers\");", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.ActivateShellPane(static page => page.Shell, \"Orders\");", StringComparison.Ordinal)).IsEqualTo(true);
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
    public async Task ConfigureTextBoxProxy_MapsInnerPartWithoutNormalPathWarning()
    {
        var options = new AppAutomationRecorderOptions();
        options.ConfigureProxy(
            "ServerFilterEditor",
            "ServerFilterEditorInput",
            UiControlType.TextBox);
        var root = new StackPanel();
        var wrapper = new Border();
        var innerTextBox = new TextBox();
        AutomationProperties.SetAutomationId(wrapper, "ServerFilterEditor");
        AutomationProperties.SetAutomationId(innerTextBox, "ServerFilterEditorInput");
        wrapper.Child = innerTextBox;
        root.Children.Add(wrapper);
        var resolver = new RecorderSelectorResolver(options, validationRoot: root);

        var result = resolver.Resolve(innerTextBox, UiControlType.AutomationElement);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Control).IsNotNull();
            await Assert.That(result.Control!.LocatorValue).IsEqualTo("ServerFilterEditor");
            await Assert.That(result.Control.ControlType).IsEqualTo(UiControlType.TextBox);
            await Assert.That(result.Control.Warning).IsNull();
            await Assert.That(result.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(result.CanPersist).IsEqualTo(true);
        }
    }

    [Test]
    public async Task ConfigureSpinnerProxy_CapturesNumericInnerTextBox_AsLogicalSpinnerStep()
    {
        var options = new AppAutomationRecorderOptions();
        options.ConfigureSpinnerProxy("MixCountEditor", "MixCountEditorInput");
        var root = new StackPanel();
        var wrapper = new Border();
        var innerTextBox = new TextBox { Text = "10.5" };
        AutomationProperties.SetAutomationId(wrapper, "MixCountEditor");
        AutomationProperties.SetAutomationId(innerTextBox, "MixCountEditorInput");
        wrapper.Child = innerTextBox;
        root.Children.Add(wrapper);
        var factory = new RecorderStepFactory(options, () => root);

        var result = factory.TryCreateTextEntryStep(innerTextBox);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.SetSpinnerValue);
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo("MixCountEditor");
            await Assert.That(result.Step.Control.ControlType).IsEqualTo(UiControlType.Spinner);
            await Assert.That(result.Step.DoubleValue).IsEqualTo(10.5);
        }
    }

    [Test]
    public async Task ConfigureTextBoxProxy_RecordsValidatedLogicalTextEntry()
    {
        var options = new AppAutomationRecorderOptions
        {
            ShowOverlay = false,
            Validation = new RecorderValidationOptions { ValidateRuntimeTargets = false }
        };
        options.ConfigureTextBoxProxy("CustomerEditor", "CustomerEditor_Input");
        var root = new StackPanel();
        var logicalEditor = new Border();
        var input = new TextBox();
        AutomationProperties.SetAutomationId(logicalEditor, "CustomerEditor");
        AutomationProperties.SetAutomationId(input, "CustomerEditor_Input");
        logicalEditor.Child = input;
        root.Children.Add(logicalEditor);
        using var session = new RecorderSession(
            CreateWindowStub(),
            options,
            () => root,
            attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.RefreshObservedControlsForTesting();
        session.RegisterKeyboardInputForTesting(input);
        input.Text = "Customer 42";
        session.FlushPendingStateForTesting();

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(1);
            await Assert.That(details.StepJournal[0].Preview)
                .Contains("Page.EnterText(static page => page.CustomerEditor, \"Customer 42\");");
            await Assert.That(details.StepJournal[0].ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(details.StepJournal[0].CanPersist).IsTrue();
        }
    }

    [Test]
    public async Task NumericUpDown_CapturesLogicalSpinnerValue()
    {
        var spinner = new NumericUpDown { Value = 10.5m };
        AutomationProperties.SetAutomationId(spinner, "QuantitySpinner");
        var factory = new RecorderStepFactory(new AppAutomationRecorderOptions());

        var result = factory.TryCreateSpinnerStep(spinner);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.SetSpinnerValue);
            await Assert.That(result.Step.Control.ControlType).IsEqualTo(UiControlType.Spinner);
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo("QuantitySpinner");
            await Assert.That(result.Step.DoubleValue).IsEqualTo(10.5);
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
    public async Task TryCreateAssertionStep_WithExistsMode_CapturesAnyControl()
    {
        var factory = new RecorderStepFactory(new AppAutomationRecorderOptions());
        var border = new Border();
        AutomationProperties.SetAutomationId(border, "LatePanel");

        var result = factory.TryCreateAssertionStep(border, RecorderAssertionMode.Exists);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilExists);
            await Assert.That(result.Step.Control.ControlType).IsEqualTo(UiControlType.AutomationElement);
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo("LatePanel");
        }
    }

    [Test]
    public async Task TryCreateAssertionStep_TextAssertion_PreservesActualButtonControlTypes()
    {
        var factory = new RecorderStepFactory(new AppAutomationRecorderOptions());
        var button = new Button { Content = "Run" };
        var checkBox = new CheckBox { Content = "Agree" };
        var radioButton = new RadioButton { Content = "Primary" };
        var toggleButton = new ToggleButton { Content = "Pinned" };
        AutomationProperties.SetAutomationId(button, "RunButton");
        AutomationProperties.SetAutomationId(checkBox, "AgreeCheckBox");
        AutomationProperties.SetAutomationId(radioButton, "PrimaryRadioButton");
        AutomationProperties.SetAutomationId(toggleButton, "PinnedToggleButton");

        var buttonResult = factory.TryCreateAssertionStep(button, RecorderAssertionMode.Text);
        var checkBoxResult = factory.TryCreateAssertionStep(checkBox, RecorderAssertionMode.Text);
        var radioButtonResult = factory.TryCreateAssertionStep(radioButton, RecorderAssertionMode.Text);
        var toggleButtonResult = factory.TryCreateAssertionStep(toggleButton, RecorderAssertionMode.Text);

        using (Assert.Multiple())
        {
            await Assert.That(buttonResult.Success).IsEqualTo(true);
            await Assert.That(buttonResult.Step!.Control.ControlType).IsEqualTo(UiControlType.Button);
            await Assert.That(buttonResult.Step.StringValue).IsEqualTo("Run");

            await Assert.That(checkBoxResult.Success).IsEqualTo(true);
            await Assert.That(checkBoxResult.Step!.Control.ControlType).IsEqualTo(UiControlType.CheckBox);
            await Assert.That(checkBoxResult.Step.StringValue).IsEqualTo("Agree");

            await Assert.That(radioButtonResult.Success).IsEqualTo(true);
            await Assert.That(radioButtonResult.Step!.Control.ControlType).IsEqualTo(UiControlType.RadioButton);
            await Assert.That(radioButtonResult.Step.StringValue).IsEqualTo("Primary");

            await Assert.That(toggleButtonResult.Success).IsEqualTo(true);
            await Assert.That(toggleButtonResult.Step!.Control.ControlType).IsEqualTo(UiControlType.ToggleButton);
            await Assert.That(toggleButtonResult.Step.StringValue).IsEqualTo("Pinned");
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
            await Assert.That(details.StepJournal[0].ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(details.StepJournal[0].CanPersist).IsEqualTo(true);
            await Assert.That(details.StepJournal[0].Preview).DoesNotContain("grid-user-action-adapter-required");
            await Assert.That(details.StepJournal[0].StatusMessage).DoesNotContain("grid-user-action-adapter-required");
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
    public async Task RuntimeValidator_MultiSelectRejectsDuplicatePayload()
    {
        var validator = new RecorderCommandRuntimeValidator(new AppAutomationRecorderOptions());
        var step = new RecordedStep(
            RecordedActionKind.SelectMultiItems,
            new RecordedControlDescriptor(
                "Categories",
                UiControlType.MultiSelect,
                "Categories",
                UiLocatorKind.AutomationId,
                FallbackToName: false,
                AvaloniaTypeName: typeof(Control).FullName ?? nameof(Control),
                Warning: null),
            StringValues: ["Alpha", "alpha"]);

        var result = validator.Validate(step);

        using (Assert.Multiple())
        {
            await Assert.That(result.CanPersist).IsFalse();
            await Assert.That(result.ValidationStatus).IsEqualTo(RecorderValidationStatus.Invalid);
            await Assert.That(result.RuntimeValidationFindings!.Any(
                static finding => finding.Code.Contains("payload-invalid-multi-select-values", StringComparison.Ordinal))).IsTrue();
        }
    }

    [Test]
    public async Task RuntimeValidator_ComboBoxFilter_AllowsEmptySetAndRejectsDuplicates()
    {
        static RecordedStep CreateFilterStep(IReadOnlyList<string> values)
        {
            return new RecordedStep(
                RecordedActionKind.ApplyFilterSelection,
                new RecordedControlDescriptor(
                    "StatusFilter",
                    UiControlType.ComboBoxFilter,
                    "StatusFilter",
                    UiLocatorKind.AutomationId,
                    FallbackToName: false,
                    AvaloniaTypeName: typeof(Control).FullName ?? nameof(Control),
                    Warning: null),
                StringValues: values);
        }

        var validator = new RecorderCommandRuntimeValidator(new AppAutomationRecorderOptions());
        var emptySet = CreateFilterStep([]);
        var duplicateSet = CreateFilterStep(["Closed", "closed"]);

        var emptyResult = validator.Validate(emptySet);
        var duplicateResult = validator.Validate(duplicateSet);

        using (Assert.Multiple())
        {
            await Assert.That(emptyResult.CanPersist).IsTrue();
            await Assert.That(emptyResult.ValidationStatus).IsEqualTo(RecorderValidationStatus.Warning);
            await Assert.That(duplicateResult.CanPersist).IsFalse();
            await Assert.That(duplicateResult.RuntimeValidationFindings!.Any(
                static finding => finding.Code.Contains("payload-invalid-combo-box-filter-values", StringComparison.Ordinal))).IsTrue();
        }
    }

    [Test]
    public async Task RuntimeValidator_TextAssertion_OnValueOnlyControl_IsUnsupported()
    {
        var validator = new RecorderCommandRuntimeValidator(new AppAutomationRecorderOptions());
        var step = new RecordedStep(
            RecordedActionKind.WaitUntilTextEquals,
            new RecordedControlDescriptor(
                "CreatedAtPicker",
                UiControlType.DateTimePicker,
                "CreatedAtPicker",
                UiLocatorKind.AutomationId,
                FallbackToName: false,
                AvaloniaTypeName: typeof(DatePicker).FullName ?? nameof(DatePicker),
                Warning: null),
            StringValue: "2026-01-01");

        var result = validator.Validate(step);

        using (Assert.Multiple())
        {
            await Assert.That(result.ValidationStatus).IsEqualTo(RecorderValidationStatus.Invalid);
            await Assert.That(result.CanPersist).IsEqualTo(false);
            await Assert.That(result.RuntimeValidationFindings?.Count).IsEqualTo(2);
            await Assert.That(result.RuntimeValidationFindings!.All(static finding => finding.Severity == RecorderRuntimeValidationSeverity.Invalid)).IsEqualTo(true);
            await Assert.That(result.RuntimeValidationFindings!.All(static finding => finding.Code.EndsWith("-control-type-mismatch", StringComparison.Ordinal))).IsEqualTo(true);
            await Assert.That(result.RuntimeValidationFindings!.All(static finding => finding.Message.Contains("UiControlType.DateTimePicker", StringComparison.Ordinal))).IsEqualTo(true);
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
    public async Task RecorderSession_DiagnosticLogFile_IsEnabledByDefault()
    {
        using var directory = new TemporaryDirectory();
        var logPath = Path.Combine(directory.Path, "recorder-diagnostics.log");
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
            validationRootProvider: null,
            attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        var logSource = await File.ReadAllTextAsync(logPath);

        using (Assert.Multiple())
        {
            await Assert.That(details.IsDiagnosticLogFileEnabled).IsEqualTo(true);
            await Assert.That(details.DiagnosticLogFilePath).IsEqualTo(logPath);
            await Assert.That(logSource).Contains("AppAutomation recorder diagnostic log");
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
                    WriteToFile = false,
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
            CaptureAssertExists = "Ctrl+Alt+F",
            ToggleOverlayMinimize = "Shift+M"
        };

        var map = RecorderHotkeyMap.Create(hotkeys);
        var startStopResolved = map.TryGetCommand(Key.R, KeyModifiers.Alt, out var startStopCommand);
        var exportResolved = map.TryGetCommand(Key.E, KeyModifiers.Control | KeyModifiers.Alt, out var exportCommand);
        var existsResolved = map.TryGetCommand(Key.F, KeyModifiers.Control | KeyModifiers.Alt, out var existsCommand);
        var overlayResolved = map.TryGetCommand(Key.M, KeyModifiers.Shift, out _);
        var legend = map.BuildLegend();

        using (Assert.Multiple())
        {
            await Assert.That(startStopResolved).IsEqualTo(true);
            await Assert.That(startStopCommand).IsEqualTo(RecorderCommandKind.StartStop);
            await Assert.That(exportResolved).IsEqualTo(true);
            await Assert.That(exportCommand).IsEqualTo(RecorderCommandKind.Export);
            await Assert.That(existsResolved).IsEqualTo(true);
            await Assert.That(existsCommand).IsEqualTo(RecorderCommandKind.CaptureAssertExists);
            await Assert.That(overlayResolved).IsEqualTo(false);
            await Assert.That(legend.Contains("Alt+R: Start/Stop", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(legend.Contains("Ctrl+Alt+E: Export", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(legend.Contains("Ctrl+Alt+F: Assert Exists", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(legend.Contains("Shift+M", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(legend.Contains("Overlay", StringComparison.Ordinal)).IsEqualTo(false);
        }
    }

    [Test]
    public async Task HotkeySettings_RejectsDuplicateNormalizedGestures()
    {
        var settings = RecorderHotkeySettings.FromGestures(new Dictionary<RecorderCommandKind, string?>
        {
            [RecorderCommandKind.StartStop] = "Ctrl+Alt+R",
            [RecorderCommandKind.Save] = "Alt+Ctrl+R"
        });

        var result = settings.Validate();

        using (Assert.Multiple())
        {
            await Assert.That(result.IsValid).IsEqualTo(false);
            await Assert.That(result.ErrorMessage).Contains("multiple commands");
            await Assert.That(result.ErrorMessage).Contains("Start/Stop");
            await Assert.That(result.ErrorMessage).Contains("Save");
        }
    }

    [Test]
    public async Task HotkeySettings_CreateEffective_IgnoresNullPersistedGestures()
    {
        var defaults = new AppAutomationRecorderOptions { ShowOverlay = false };
        var overrides = new RecorderHotkeySettings { Gestures = null! };

        var settings = RecorderHotkeySettings.CreateEffective(defaults.Hotkeys, overrides);
        var resolved = settings.ToMap().TryGetCommand(
            Key.R,
            KeyModifiers.Control | KeyModifiers.Shift,
            out var command);

        using (Assert.Multiple())
        {
            await Assert.That(resolved).IsEqualTo(true);
            await Assert.That(command).IsEqualTo(RecorderCommandKind.StartStop);
        }
    }

    [Test]
    public async Task HotkeySettingsStore_PersistsOverridesInConfiguredFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "hotkeys.json");
        var store = new RecorderHotkeySettingsStore(path);
        var settings = RecorderHotkeySettings.FromGestures(new Dictionary<RecorderCommandKind, string?>
        {
            [RecorderCommandKind.StartStop] = "Alt+R",
            [RecorderCommandKind.Save] = null
        });

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        using (Assert.Multiple())
        {
            await Assert.That(File.Exists(path)).IsEqualTo(true);
            await Assert.That(loaded).IsNotNull();
            await Assert.That(loaded!.Gestures[RecorderCommandKind.StartStop]).IsEqualTo("Alt+R");
            await Assert.That(loaded.Gestures[RecorderCommandKind.Save]).IsNull();
        }
    }

    [Test]
    public async Task HotkeySettingsStore_LoadsDeprecatedOverlayMinimizeGestureButDoesNotActivateIt()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "hotkeys.json");
        File.WriteAllText(
            path,
            """
            {
              "Gestures": {
                "StartStop": "Alt+R",
                "ToggleOverlayMinimize": "Shift+M"
              }
            }
            """);
        var store = new RecorderHotkeySettingsStore(path);

        var loaded = store.TryLoad(out var settings, out var error);
        var effective = RecorderHotkeySettings.CreateEffective(new RecorderHotkeys(), settings);
        var startStopResolved = effective.ToMap().TryGetCommand(Key.R, KeyModifiers.Alt, out var startStopCommand);
        var overlayResolved = effective.ToMap().TryGetCommand(Key.M, KeyModifiers.Shift, out _);

        using (Assert.Multiple())
        {
            await Assert.That(loaded).IsEqualTo(true);
            await Assert.That(error).IsNull();
            await Assert.That(settings).IsNotNull();
            await Assert.That(startStopResolved).IsEqualTo(true);
            await Assert.That(startStopCommand).IsEqualTo(RecorderCommandKind.StartStop);
            await Assert.That(overlayResolved).IsEqualTo(false);
        }
    }

    [Test]
    public async Task HotkeySettingsStore_TryLoad_ReturnsErrorForCorruptJson()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "hotkeys.json");
        File.WriteAllText(path, "{ invalid json");
        var store = new RecorderHotkeySettingsStore(path);

        var loaded = store.TryLoad(out var settings, out var error);

        using (Assert.Multiple())
        {
            await Assert.That(loaded).IsEqualTo(false);
            await Assert.That(settings).IsNull();
            await Assert.That(error).IsNotNull();
        }
    }

    [Test]
    public async Task RecorderSession_IgnoresInvalidPersistedHotkeys_AndReportsLoadError()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "hotkeys.json");
        File.WriteAllText(
            path,
            """
            {
              "Gestures": {
                "StartStop": "Alt+R",
                "Save": "Alt+R"
              }
            }
            """);
        var options = new AppAutomationRecorderOptions { ShowOverlay = false };
        var session = new RecorderSession(
            CreateWindowStub(),
            options,
            validationRootProvider: null,
            attachWindowHandlers: false,
            hotkeySettingsStore: new RecorderHotkeySettingsStore(path));

        var invalidOverrideResolved = session.HotkeyMap.TryGetCommand(Key.R, KeyModifiers.Alt, out _);
        var defaultResolved = session.HotkeyMap.TryGetCommand(
            Key.R,
            KeyModifiers.Control | KeyModifiers.Shift,
            out var defaultCommand);

        using (Assert.Multiple())
        {
            await Assert.That(invalidOverrideResolved).IsEqualTo(false);
            await Assert.That(defaultResolved).IsEqualTo(true);
            await Assert.That(defaultCommand).IsEqualTo(RecorderCommandKind.StartStop);
            await Assert.That(session.LatestStatus).Contains("User hotkey settings were ignored");
            await Assert.That(session.LatestStatus).Contains("multiple commands");
        }
    }

    [Test]
    public async Task RecorderSession_ApplyHotkeySettings_UpdatesCommandMapImmediately()
    {
        var defaults = new AppAutomationRecorderOptions { ShowOverlay = false };
        var initialSettings = RecorderHotkeySettings.CreateEffective(defaults.Hotkeys, overrides: null);
        var session = new RecorderSession(
            CreateWindowStub(),
            defaults,
            validationRootProvider: null,
            attachWindowHandlers: false,
            initialHotkeySettings: initialSettings);
        var updatedSettings = RecorderHotkeySettings.FromGestures(new Dictionary<RecorderCommandKind, string?>
        {
            [RecorderCommandKind.StartStop] = "Alt+R",
            [RecorderCommandKind.Save] = "Ctrl+Shift+S"
        });

        var applied = session.TryApplyHotkeySettings(updatedSettings, out var error);
        var oldResolved = session.HotkeyMap.TryGetCommand(Key.R, KeyModifiers.Control | KeyModifiers.Shift, out _);
        var newResolved = session.HotkeyMap.TryGetCommand(Key.R, KeyModifiers.Alt, out var command);

        using (Assert.Multiple())
        {
            await Assert.That(applied).IsEqualTo(true);
            await Assert.That(error).IsNull();
            await Assert.That(oldResolved).IsEqualTo(false);
            await Assert.That(newResolved).IsEqualTo(true);
            await Assert.That(command).IsEqualTo(RecorderCommandKind.StartStop);
            await Assert.That(session.HotkeyMap.BuildLegend()).Contains("Alt+R: Start/Stop");
        }
    }

    [Test]
    public async Task Overlay_Attach_UsesRecorderSessionHotkeyMap_ForShortcutLegend()
    {
        var options = new AppAutomationRecorderOptions { ShowOverlay = false };
        var settings = RecorderHotkeySettings.FromGestures(new Dictionary<RecorderCommandKind, string?>
        {
            [RecorderCommandKind.StartStop] = "Alt+R",
            [RecorderCommandKind.Save] = "Ctrl+Shift+S"
        });
        var session = new RecorderSession(
            CreateWindowStub(),
            options,
            validationRootProvider: null,
            attachWindowHandlers: false,
            initialHotkeySettings: settings);
        var overlay = new RecorderOverlay();

        overlay.Attach(session, options);

        var shortcutText = overlay.FindControl<TextBlock>("ShortcutText");
        var settingsButton = overlay.FindControl<Button>("SettingsButton");

        using (Assert.Multiple())
        {
            await Assert.That(shortcutText).IsNotNull();
            await Assert.That(shortcutText!.Text).Contains("Alt+R: Start/Stop");
            await Assert.That(settingsButton).IsNotNull();
            await Assert.That(settingsButton!.IsEnabled).IsEqualTo(true);
        }
    }

    [Test]
    public async Task HotkeySettingsWindow_CapturesShortcutGestures_AndClearsWithDelete()
    {
        var captured = RecorderHotkeySettingsWindow.TryCaptureShortcut(
            Key.R,
            KeyModifiers.Control | KeyModifiers.Shift,
            out var gesture);
        var tabCaptured = RecorderHotkeySettingsWindow.TryCaptureShortcut(
            Key.Tab,
            KeyModifiers.Control,
            out var tabGesture);
        var deleteWithModifierCaptured = RecorderHotkeySettingsWindow.TryCaptureShortcut(
            Key.Delete,
            KeyModifiers.Control,
            out var deleteWithModifierGesture);
        var altLetterCaptured = RecorderHotkeySettingsWindow.TryCaptureShortcut(
            Key.None,
            PhysicalKey.Z,
            KeyModifiers.Alt,
            out var altLetterGesture);
        var systemAltLetterCaptured = RecorderHotkeySettingsWindow.TryCaptureShortcut(
            Key.System,
            PhysicalKey.Z,
            KeyModifiers.None,
            out var systemAltLetterGesture);
        var plainLetterCaptured = RecorderHotkeySettingsWindow.TryCaptureShortcut(
            Key.A,
            PhysicalKey.A,
            KeyModifiers.None,
            out var plainLetterGesture);
        var plainDigitCaptured = RecorderHotkeySettingsWindow.TryCaptureShortcut(
            Key.D1,
            PhysicalKey.Digit1,
            KeyModifiers.None,
            out var plainDigitGesture);
        var cyrillicLayoutCaptured = RecorderHotkeySettingsWindow.TryCaptureShortcut(
            Key.None,
            PhysicalKey.F,
            KeyModifiers.Control,
            out var cyrillicLayoutGesture);
        var modifierOnlyCaptured = RecorderHotkeySettingsWindow.TryCaptureShortcut(
            Key.LeftCtrl,
            KeyModifiers.Control,
            out var modifierOnlyGesture);
        var noneCaptured = RecorderHotkeySettingsWindow.TryCaptureShortcut(
            Key.None,
            KeyModifiers.Control,
            out var noneGesture);
        var cleared = RecorderHotkeySettingsWindow.TryCaptureShortcut(
            Key.Delete,
            KeyModifiers.None,
            out var clearedGesture);

        using (Assert.Multiple())
        {
            await Assert.That(captured).IsEqualTo(true);
            await Assert.That(gesture).IsEqualTo("Ctrl+Shift+R");
            await Assert.That(tabCaptured).IsEqualTo(true);
            await Assert.That(tabGesture).IsEqualTo("Ctrl+Tab");
            await Assert.That(deleteWithModifierCaptured).IsEqualTo(true);
            await Assert.That(deleteWithModifierGesture).IsEqualTo("Ctrl+Delete");
            await Assert.That(altLetterCaptured).IsEqualTo(true);
            await Assert.That(altLetterGesture).IsEqualTo("Alt+Z");
            await Assert.That(systemAltLetterCaptured).IsEqualTo(true);
            await Assert.That(systemAltLetterGesture).IsEqualTo("Alt+Z");
            await Assert.That(plainLetterCaptured).IsEqualTo(true);
            await Assert.That(plainLetterGesture).IsEqualTo("A");
            await Assert.That(plainDigitCaptured).IsEqualTo(true);
            await Assert.That(plainDigitGesture).IsEqualTo("1");
            await Assert.That(cyrillicLayoutCaptured).IsEqualTo(true);
            await Assert.That(cyrillicLayoutGesture).IsEqualTo("Ctrl+F");
            await Assert.That(modifierOnlyCaptured).IsEqualTo(false);
            await Assert.That(modifierOnlyGesture).IsNull();
            await Assert.That(noneCaptured).IsEqualTo(false);
            await Assert.That(noneGesture).IsNull();
            await Assert.That(cleared).IsEqualTo(true);
            await Assert.That(clearedGesture).IsNull();
        }
    }

    [Test]
    public async Task HotkeyMap_MatchesPhysicalQwertyKeys_ForLayoutIndependentShortcuts()
    {
        var map = RecorderHotkeyMap.Create(new Dictionary<RecorderCommandKind, string?>
        {
            [RecorderCommandKind.StartStop] = "Alt+Z",
            [RecorderCommandKind.Save] = "1"
        });

        var altResolved = map.TryGetCommand(
            Key.None,
            PhysicalKey.Z,
            KeyModifiers.Alt,
            out var altCommand);
        var digitResolved = map.TryGetCommand(
            Key.None,
            PhysicalKey.Digit1,
            KeyModifiers.None,
            out var digitCommand);

        using (Assert.Multiple())
        {
            await Assert.That(altResolved).IsEqualTo(true);
            await Assert.That(altCommand).IsEqualTo(RecorderCommandKind.StartStop);
            await Assert.That(digitResolved).IsEqualTo(true);
            await Assert.That(digitCommand).IsEqualTo(RecorderCommandKind.Save);
            await Assert.That(map.BuildLegend()).Contains("Alt+Z: Start/Stop");
            await Assert.That(map.BuildLegend()).Contains("1: Save");
        }
    }

    [Test]
    public async Task HotkeySettingsWindow_CapturesTextInput_ForPlainLettersDigitsAndSymbols()
    {
        var letterCaptured = RecorderShortcut.TryCreateFromText("a", out var letter);
        var digitCaptured = RecorderShortcut.TryCreateFromText("1", out var digit);
        var dashCaptured = RecorderShortcut.TryCreateFromText("-", out var dash);
        var slashCaptured = RecorderShortcut.TryCreateFromText("/", out var slash);
        var bangCaptured = RecorderShortcut.TryCreateFromText("!", out var bang);
        var cyrillicCaptured = RecorderShortcut.TryCreateFromText("ф", out var cyrillic);

        using (Assert.Multiple())
        {
            await Assert.That(letterCaptured).IsEqualTo(true);
            await Assert.That(letter.NormalizedText).IsEqualTo("A");
            await Assert.That(digitCaptured).IsEqualTo(true);
            await Assert.That(digit.NormalizedText).IsEqualTo("1");
            await Assert.That(dashCaptured).IsEqualTo(true);
            await Assert.That(dash.NormalizedText).IsEqualTo("-");
            await Assert.That(slashCaptured).IsEqualTo(true);
            await Assert.That(slash.NormalizedText).IsEqualTo("/");
            await Assert.That(bangCaptured).IsEqualTo(true);
            await Assert.That(bang.NormalizedText).IsEqualTo("Shift+1");
            await Assert.That(cyrillicCaptured).IsEqualTo(true);
            await Assert.That(cyrillic.NormalizedText).IsEqualTo("A");
        }
    }

    [Test]
    public async Task HotkeyMap_ParsesSymbolGestures_ForRuntimeMatching()
    {
        var map = RecorderHotkeyMap.Create(new Dictionary<RecorderCommandKind, string?>
        {
            [RecorderCommandKind.StartStop] = "/",
            [RecorderCommandKind.Save] = "Shift+1"
        });

        var slashResolved = map.TryGetCommand(Key.OemQuestion, KeyModifiers.None, out var slashCommand);
        var bangResolved = map.TryGetCommand(Key.D1, KeyModifiers.Shift, out var bangCommand);

        using (Assert.Multiple())
        {
            await Assert.That(slashResolved).IsEqualTo(true);
            await Assert.That(slashCommand).IsEqualTo(RecorderCommandKind.StartStop);
            await Assert.That(bangResolved).IsEqualTo(true);
            await Assert.That(bangCommand).IsEqualTo(RecorderCommandKind.Save);
            await Assert.That(map.BuildLegend()).Contains("/: Start/Stop");
            await Assert.That(map.BuildLegend()).Contains("Shift+1: Save");
        }
    }

    [Test]
    public async Task RecorderSession_CommandPath_CapturesExistsAssertion()
    {
        var root = new StackPanel();
        var statusLabel = new TextBlock { Text = "Ready" };
        AutomationProperties.SetAutomationId(statusLabel, "StatusLabel");
        root.Children.Add(statusLabel);
        var options = new AppAutomationRecorderOptions { ShowOverlay = false };
        var session = new RecorderSession(CreateWindowStub(), options, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.SetLastHoveredControlForTesting(statusLabel);
        session.HandleRecorderCommandForTesting(RecorderCommandKind.CaptureAssertExists);

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(1);
            await Assert.That(details.StepJournal[0].Preview).Contains("Page.WaitUntilExists(static page => page.StatusLabel);");
            await Assert.That(details.StepJournal[0].ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
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
            await Assert.That(details.StepJournal[0].StatusMessage.Contains("wrapper", StringComparison.OrdinalIgnoreCase)).IsEqualTo(true);
            await Assert.That(details.StepJournal[0].StatusMessage.Contains("stable part", StringComparison.OrdinalIgnoreCase)).IsEqualTo(true);
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
    public async Task RecorderSession_SuppressesConfiguredGridSearchPickerButtons_AndCapturesGridSelectionAsComposite()
    {
        var options = CreateGridSearchPickerOptions(validateRuntimeTargets: false);
        var root = new StackPanel();
        var rows = CreateEremexRows();
        var eremexVisualControl = new RecorderGridHost { ItemsSource = rows };
        var bridge = new Border();
        var editor = new StackPanel { DataContext = rows[1] };
        var searchInput = new TextBox { DataContext = rows[1] };
        var applyButton = new Button { Content = "Apply", DataContext = rows[1] };
        var expandButton = new Button { Content = "Open", DataContext = rows[1] };
        var results = new ListBox
        {
            ItemsSource = new[] { "EX-11", "EX-12" },
            DataContext = rows[1]
        };
        AutomationProperties.SetAutomationId(eremexVisualControl, "EremexDemoDataGridControl");
        AutomationProperties.SetAutomationId(bridge, "EremexDemoDataGridAutomationBridge");
        AutomationProperties.SetAutomationId(editor, "OrderPositionProductEditor");
        AutomationProperties.SetAutomationId(searchInput, "OrderPositionProductEditor_Input");
        AutomationProperties.SetAutomationId(applyButton, "OrderPositionProductEditor_ApplyButton");
        AutomationProperties.SetAutomationId(expandButton, "OrderPositionProductEditor_ExpandButton");
        AutomationProperties.SetAutomationId(results, "OrderPositionProductEditor_Results");
        editor.Children.Add(searchInput);
        editor.Children.Add(applyButton);
        editor.Children.Add(expandButton);
        editor.Children.Add(results);
        eremexVisualControl.Children.Add(editor);
        root.Children.Add(eremexVisualControl);
        root.Children.Add(bridge);

        var session = new RecorderSession(CreateWindowStub(), options, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.RefreshObservedControlsForTesting();
        session.RegisterKeyboardInputForTesting(searchInput);
        searchInput.Text = "prod";
        session.CaptureButtonClickForTesting(applyButton);
        session.CaptureButtonClickForTesting(expandButton);
        session.RegisterPointerInputFromSourceForTesting(results);
        results.SelectedItem = "EX-12";

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(1);
            await Assert.That(details.StepJournal[0].Preview).Contains("Page.SearchAndSelectGridCell(static page => page.EremexDemoDataGridAutomationBridge, 1, 1, \"prod\", \"EX-12\");");
            await Assert.That(details.StepJournal[0].Preview.Contains("OrderPositionProductEditor_ApplyButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(details.StepJournal[0].Preview.Contains("OrderPositionProductEditor_ExpandButton", StringComparison.Ordinal)).IsEqualTo(false);
        }
    }

    [Test]
    public async Task RecorderSession_CapturesConfiguredDialogAndNotificationButtons_AsCompositeSteps()
    {
        var options = CreateCompositeRecorderOptions();
        var root = new StackPanel();
        var confirmButton = new Button { Content = "Yes" };
        var dismissNotificationButton = new Button { Content = "Close" };
        AutomationProperties.SetAutomationId(confirmButton, "DeleteDialogConfirmButton");
        AutomationProperties.SetAutomationId(dismissNotificationButton, "ExportToastDismissButton");
        root.Children.Add(confirmButton);
        root.Children.Add(dismissNotificationButton);

        var session = new RecorderSession(CreateWindowStub(), options, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.CaptureButtonClickForTesting(confirmButton);
        session.CaptureButtonClickForTesting(dismissNotificationButton);

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(2);
            await Assert.That(details.StepJournal[0].Preview).Contains("Page.ConfirmDialog(static page => page.DeleteDialog);");
            await Assert.That(details.StepJournal[1].Preview).Contains("Page.DismissNotification(static page => page.ExportToast);");
        }
    }

    [Test]
    public async Task RecorderSession_CapturesConfiguredShellNavigation_FromListAndPaneTabs()
    {
        var options = CreateCompositeRecorderOptions();
        var root = new StackPanel();
        var navigationList = new ListBox
        {
            ItemsSource = new[] { "Customers", "Orders" }
        };
        var customersTab = new TabItem { Header = "Customers" };
        var ordersTab = new TabItem { Header = "Orders" };
        var paneTabs = new TabControl
        {
            ItemsSource = new[] { customersTab, ordersTab }
        };
        AutomationProperties.SetAutomationId(navigationList, "ShellNavigationList");
        AutomationProperties.SetAutomationId(paneTabs, "ShellPaneTabs");
        root.Children.Add(navigationList);
        root.Children.Add(paneTabs);

        var session = new RecorderSession(CreateWindowStub(), options, () => root, attachWindowHandlers: false);
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.RefreshObservedControlsForTesting();
        session.RegisterPointerInputFromSourceForTesting(navigationList);
        navigationList.SelectedItem = "Customers";
        session.RegisterPointerInputFromSourceForTesting(paneTabs);
        paneTabs.SelectedItem = ordersTab;

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(2);
            await Assert.That(details.StepJournal[0].Preview).Contains("Page.OpenOrActivateShellPane(static page => page.Shell, \"Customers\");");
            await Assert.That(details.StepJournal[1].Preview).Contains("Page.ActivateShellPane(static page => page.Shell, \"Orders\");");
        }
    }

    [Test]
    public async Task TryCreateShellNavigationStep_WithConfiguredCaptureHost_UsesActivePaneLabelFallback()
    {
        var options = CreateCompositeRecorderOptions(useCustomShellCaptureHost: true);
        var root = new StackPanel();
        var captureHost = new TabControl();
        var bridgeTabs = new TabControl();
        var activePaneLabel = new TextBlock { Text = "Orders" };
        AutomationProperties.SetAutomationId(captureHost, "DockPaneTabsCaptureHost");
        AutomationProperties.SetAutomationId(bridgeTabs, "ShellPaneTabs");
        AutomationProperties.SetAutomationId(activePaneLabel, "ShellActivePaneLabel");
        root.Children.Add(captureHost);
        root.Children.Add(bridgeTabs);
        root.Children.Add(activePaneLabel);
        var factory = new RecorderStepFactory(options, () => root);

        var result = factory.TryCreateShellNavigationStep(captureHost);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(true);
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.ActivateShellPane);
            await Assert.That(result.Step.StringValue).IsEqualTo("Orders");
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo("Shell");
        }
    }

    [Test]
    public async Task Overlay_Attach_RemovesMinimizeSurfaceAndUpdatesPresentationAndCounters()
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

        overlay.Attach(
            session,
            new AppAutomationRecorderOptions
            {
                Overlay = new RecorderOverlayOptions
                {
                    EnableExportButton = true,
                    ShowShortcutLegend = true,
                    StartMinimized = true
                }
            });

        var stepCounter = overlay.FindControl<TextBlock>("StepCounter");
        var validationBadge = overlay.FindControl<TextBlock>("ValidationBadgeText");
        var exportButton = overlay.FindControl<Button>("ExportButton");
        var expandedPanel = overlay.FindControl<Control>("ExpandedPanel");
        var minimizeButton = overlay.FindControl<Button>("MinimizeButton");
        var restoreButton = overlay.FindControl<Button>("RestoreButton");
        var minimizedPanel = overlay.FindControl<Control>("MinimizedPanel");
        var scenarioPathText = overlay.FindControl<TextBlock>("ScenarioPathText");

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
            await Assert.That(minimizeButton).IsNull();
            await Assert.That(restoreButton).IsNull();
            await Assert.That(minimizedPanel).IsNull();
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
            await Assert.That(configuration.WindowDecorations).IsEqualTo(WindowDecorations.Full);
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
            await Assert.That(CollectText(journalPanel.Children[0])).Contains("#1");
            await Assert.That(CollectText(journalPanel.Children[0])).Contains("Page.EnterText(static page => page.SearchBox, \"Alpha\");");
            await Assert.That(CollectText(journalPanel.Children[1])).Contains("#2");
            await Assert.That(CollectText(journalPanel.Children[1])).Contains("Page.ClickButton(static page => page.RunButton);");
        }

        var actionSession = new FakeRecorderSession
        {
            StepCount = 3,
            PersistableStepCount = 1,
            LatestStatus = "Ready.",
            LatestPreview = "Page.EnterText(static page => page.SearchBox, \"Alpha\");",
            LatestValidationStatus = RecorderValidationStatus.Warning,
            IsBusy = false,
            SessionSummary = "1/3 steps | 1 warnings | 1 invalid"
        };
        actionSession.SetJournal(session.StepJournal);
        var actionOverlay = new RecorderOverlay();
        actionOverlay.Attach(actionSession, new AppAutomationRecorderOptions());
        var actionJournalPanel = actionOverlay.FindControl<Panel>("StepJournalPanel");
        var refreshedFirstItem = (Border)actionJournalPanel!.Children[0];
        var refreshedContainer = (StackPanel)refreshedFirstItem.Child!;
        var refreshedActions = (StackPanel)refreshedContainer.Children[2];
        var moveEarlierButton = (Button)refreshedActions.Children[0];
        var moveLaterButton = (Button)refreshedActions.Children[1];
        var removeButton = (Button)refreshedActions.Children[2];
        var ignoreButton = (Button)refreshedActions.Children[3];
        var retryButton = (Button)refreshedActions.Children[4];
        moveLaterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        removeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ignoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        retryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        using (Assert.Multiple())
        {
            await Assert.That(moveEarlierButton.IsEnabled).IsEqualTo(false);
            await Assert.That(moveLaterButton.IsEnabled).IsEqualTo(true);
            await Assert.That(actionSession.MovedSteps.Any(move =>
                move.StepId == firstStepId && move.Direction == RecorderStepMoveDirection.Later)).IsEqualTo(true);
            await Assert.That(actionSession.RemovedStepIds).Contains(firstStepId);
            await Assert.That(actionSession.IgnoredStepIds).Contains(firstStepId);
            await Assert.That(actionSession.RetriedStepIds).Contains(firstStepId);
        }
    }

    [Test]
    public async Task Overlay_RendersStepNumbers_AndResetsAutoscrollStateAfterEmptyJournal()
    {
        var firstStepId = Guid.NewGuid();
        var secondStepId = Guid.NewGuid();
        var session = new FakeRecorderSession
        {
            StepCount = 1,
            PersistableStepCount = 1,
            LatestStatus = "Ready.",
            LatestPreview = "Page.EnterText(static page => page.SearchBox, \"Alpha\");",
            LatestValidationStatus = RecorderValidationStatus.Valid,
            SessionSummary = "1 steps"
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
                LastValidationAt: DateTimeOffset.UtcNow)
        ]);

        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());

        var scrollViewer = overlay.FindControl<ScrollViewer>("StepJournalScrollViewer");
        var journalPanel = overlay.FindControl<Panel>("StepJournalPanel");

        using (Assert.Multiple())
        {
            await Assert.That(scrollViewer).IsNotNull();
            await Assert.That(journalPanel).IsNotNull();
            await Assert.That(journalPanel!.Children.Count).IsEqualTo(1);
            await Assert.That(CollectText(journalPanel.Children[0])).Contains("#1");
        }

        session.SetJournal(
        [
            session.StepJournal[0],
            new RecorderStepJournalEntry(
                secondStepId,
                "Page.ClickButton(static page => page.RunButton);",
                "Ready to persist.",
                RecorderValidationStatus.Valid,
                CanPersist: true,
                IsIgnored: false,
                RecorderStepReviewState.Active,
                FailureCode: null,
                LastValidationAt: DateTimeOffset.UtcNow)
        ]);
        session.RaiseChanged();
        await DrainUiAsync();

        using (Assert.Multiple())
        {
            await Assert.That(journalPanel!.Children.Count).IsEqualTo(2);
            await Assert.That(CollectText(journalPanel.Children[1])).Contains("#2");
            await Assert.That(CollectText(journalPanel.Children[1])).Contains("Page.ClickButton(static page => page.RunButton);");
        }

        session.SetJournal([]);
        session.RaiseChanged();
        await DrainUiAsync();

        await Assert.That(journalPanel!.Children.Count).IsEqualTo(0);

        session.SetJournal(
        [
            new RecorderStepJournalEntry(
                Guid.NewGuid(),
                "Page.ClickButton(static page => page.ResetButton);",
                "Ready to persist.",
                RecorderValidationStatus.Valid,
                CanPersist: true,
                IsIgnored: false,
                RecorderStepReviewState.Active,
                FailureCode: null,
                LastValidationAt: DateTimeOffset.UtcNow)
        ]);
        session.RaiseChanged();
        await DrainUiAsync();

        using (Assert.Multiple())
        {
            await Assert.That(journalPanel.Children.Count).IsEqualTo(1);
            await Assert.That(CollectText(journalPanel.Children[0])).Contains("#1");
            await Assert.That(CollectText(journalPanel.Children[0])).Contains("Page.ClickButton(static page => page.ResetButton);");
        }
    }

    [Test]
    public async Task Overlay_AutoscrollsStepJournal_WhenEntryCountIncreases()
    {
        var session = new FakeRecorderSession
        {
            StepCount = 2,
            PersistableStepCount = 2,
            LatestStatus = "Ready.",
            LatestPreview = "Page.ClickButton(static page => page.Step2Button);",
            LatestValidationStatus = RecorderValidationStatus.Valid,
            SessionSummary = "2 steps"
        };
        session.SetJournal(
            Enumerable.Range(1, 2)
                .Select(static index => CreateJournalEntry(
                    Guid.NewGuid(),
                    $"Page.ClickButton(static page => page.Step{index}Button);"))
                .ToArray());

        var overlay = new RecorderOverlay();
        var scrolledViewers = new List<ScrollViewer>();
        overlay.ScrollToEndForTesting = scrollViewer => scrolledViewers.Add(scrollViewer);

        overlay.Attach(session, new AppAutomationRecorderOptions());
        var scrollViewer = overlay.FindControl<ScrollViewer>("StepJournalScrollViewer");
        var journalPanel = overlay.FindControl<Panel>("StepJournalPanel");
        await Assert.That(scrollViewer).IsNotNull();
        await Assert.That(journalPanel).IsNotNull();

        scrolledViewers.Clear();

        session.SetJournal(
            session.StepJournal
                .Select(static entry => entry with { StatusMessage = "Still ready." })
                .ToArray());
        overlay.RefreshForTesting();

        await Assert.That(scrolledViewers.Count).IsEqualTo(0);

        var newEntry = CreateJournalEntry(
            Guid.NewGuid(),
            "Page.ClickButton(static page => page.Step3Button);");
        session.SetJournal(session.StepJournal.Concat([newEntry]).ToArray());
        overlay.RefreshForTesting();

        using (Assert.Multiple())
        {
            await Assert.That(scrolledViewers.Count).IsEqualTo(1);
            await Assert.That(scrolledViewers[0]).IsSameReferenceAs(scrollViewer);
            await Assert.That(journalPanel!.Children.Count).IsEqualTo(3);
            await Assert.That(CollectText(journalPanel.Children[^1])).Contains("#3");
            await Assert.That(CollectText(journalPanel.Children[^1])).Contains("Page.ClickButton(static page => page.Step3Button);");
        }
    }

    [Test]
    public async Task RecorderSession_AutosavesRecordedStep_WhileRecording()
    {
        var root = new StackPanel();
        var button = new Button { Content = "Run" };
        AutomationProperties.SetAutomationId(button, "RunButton");
        root.Children.Add(button);
        var saveCallCount = 0;
        var savedStepCount = 0;
        var session = new RecorderSession(
            CreateWindowStub(),
            new AppAutomationRecorderOptions
            {
                ShowOverlay = false,
                DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false },
                Validation = new RecorderValidationOptions
                {
                    ValidateRuntimeTargets = false
                }
            },
            () => root,
            attachWindowHandlers: false,
            saveOperation: (steps, _, _) =>
            {
                saveCallCount++;
                savedStepCount = steps.Count;
                return Task.FromResult(
                    RecorderSaveResult.Completed(
                        "Autosaved.",
                        pageFilePath: "MainWindowPage.Recorded.cs",
                        scenarioFilePath: "MainWindowScenariosBase.Recorded.cs",
                        persistedStepCount: steps.Count,
                        skippedStepCount: 0));
            });
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.CaptureButtonClickForTesting(button);

        await WaitForConditionAsync(() => saveCallCount == 1);

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(1);
            await Assert.That(details.StepJournal[0].Preview).Contains("Page.ClickButton(static page => page.RunButton);");
            await Assert.That(savedStepCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task RecorderSession_AutosaveUsesAutosaveOperation()
    {
        var root = new StackPanel();
        var button = new Button { Content = "Run" };
        AutomationProperties.SetAutomationId(button, "RunButton");
        root.Children.Add(button);
        var manualSaveCallCount = 0;
        var autosaveCallCount = 0;
        var session = new RecorderSession(
            CreateWindowStub(),
            new AppAutomationRecorderOptions
            {
                ShowOverlay = false,
                DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false },
                Validation = new RecorderValidationOptions
                {
                    ValidateRuntimeTargets = false
                }
            },
            () => root,
            attachWindowHandlers: false,
            saveOperation: (steps, _, _) =>
            {
                manualSaveCallCount++;
                return Task.FromResult(
                    RecorderSaveResult.Completed(
                        "Saved.",
                        pageFilePath: "MainWindowPage.Recorded.cs",
                        scenarioFilePath: "MainWindowScenariosBase.Recorded.cs",
                        persistedStepCount: steps.Count,
                        skippedStepCount: 0));
            },
            autosaveOperation: (steps, _, _) =>
            {
                autosaveCallCount++;
                return Task.FromResult(
                    RecorderSaveResult.Completed(
                        "Autosaved.",
                        pageFilePath: "MainWindowPage.Recorded.autosave.cs",
                        scenarioFilePath: "MainWindowScenariosBase.Recorded.autosave.cs",
                        persistedStepCount: steps.Count,
                        skippedStepCount: 0));
            });

        session.Start();
        session.CaptureButtonClickForTesting(button);
        await WaitForConditionAsync(() => autosaveCallCount == 1);
        await session.SaveAsync();

        using (Assert.Multiple())
        {
            await Assert.That(autosaveCallCount).IsEqualTo(1);
            await Assert.That(manualSaveCallCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task RecorderSession_FinalSaveOnlyKeepsAutosaveWhenSaveFails()
    {
        var root = new StackPanel();
        var textBox = new TextBox();
        AutomationProperties.SetAutomationId(textBox, "SearchBox");
        root.Children.Add(textBox);
        var saveTracker = new RecorderSaveOperationTracker();
        var session = new RecorderSession(
            CreateWindowStub(),
            new AppAutomationRecorderOptions
            {
                ShowOverlay = false,
                DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false },
                Validation = new RecorderValidationOptions
                {
                    ValidateRuntimeTargets = false
                }
            },
            () => root,
            attachWindowHandlers: false,
            saveOperation: saveTracker.SaveAsync,
            autosaveOperation: saveTracker.AutosaveAsync);

        session.Start();
        session.RefreshObservedControlsForTesting();
        session.RegisterKeyboardInputForTesting(textBox);
        textBox.Text = "Search result";

        await session.SaveAsync();
        await WaitForConditionAsync(() => !session.IsBusy);

        using (Assert.Multiple())
        {
            await Assert.That(saveTracker.ManualSaveCallCount).IsEqualTo(1);
            await Assert.That(saveTracker.SavedStepCount).IsEqualTo(1);
            await Assert.That(saveTracker.AutosaveCallCount).IsEqualTo(0);
        }

        saveTracker.SaveShouldSucceed = false;
        session.RegisterKeyboardInputForTesting(textBox);
        textBox.Text = "Another result";

        await session.SaveAsync();
        await WaitForConditionAsync(() => !session.IsBusy);

        using (Assert.Multiple())
        {
            await Assert.That(saveTracker.ManualSaveCallCount).IsEqualTo(2);
            await Assert.That(saveTracker.SavedStepCount).IsEqualTo(2);
            await Assert.That(saveTracker.AutosaveCallCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task RecorderSession_SuccessfulFinalSave_DiscardsQueuedAutosave()
    {
        var manualSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manualSaveRelease = new TaskCompletionSource<RecorderSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var autosaveCallCount = 0;
        var stepId = Guid.NewGuid();
        var session = new RecorderSession(
            CreateWindowStub(),
            new AppAutomationRecorderOptions { ShowOverlay = false },
            validationRootProvider: null,
            attachWindowHandlers: false,
            saveOperation: (_, _, _) =>
            {
                manualSaveStarted.TrySetResult();
                return manualSaveRelease.Task;
            },
            autosaveOperation: (_, _, _) =>
            {
                Interlocked.Increment(ref autosaveCallCount);
                return Task.FromResult(
                    RecorderSaveResult.Completed(
                        "Autosaved.",
                        pageFilePath: "MainWindowPage.controls.g.cs.autosave",
                        scenarioFilePath: "MainWindowScenariosBase.g.cs.autosave",
                        persistedStepCount: 1,
                        skippedStepCount: 0));
            });
        var details = (IAppAutomationRecorderSessionDetails)session;
        session.AddRecordedStepForTesting(CreateRecordedButtonStep(stepId, "RunButton"));
        session.Start();

        var saveTask = session.SaveAsync();
        await manualSaveStarted.Task;
        details.SetStepIgnored(stepId, isIgnored: true);
        manualSaveRelease.SetResult(
            RecorderSaveResult.Completed(
                "Saved.",
                pageFilePath: "MainWindowPage.RecorderControls.g.cs",
                scenarioFilePath: "MainWindowScenariosBase.RecorderScenarios.g.cs",
                persistedStepCount: 1,
                skippedStepCount: 0));

        var result = await saveTask;
        await WaitForConditionAsync(() => !session.IsBusy);
        await Task.Delay(50);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(autosaveCallCount).IsEqualTo(0);
            await Assert.That(session.IsBusy).IsFalse();
        }
    }

    [Test]
    public async Task RecorderSession_SuppressesLateGridSearchPickerPopupEvents_AfterCompositeSelection()
    {
        using var fixture = new GridSearchPickerSessionFixture();

        fixture.RecordCompositeSelection();
        fixture.ClosePopupAndRaiseLatePrimitiveEvents();

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Details.StepJournal.Count).IsEqualTo(1);
            await Assert.That(fixture.Details.StepJournal[0].Preview)
                .Contains("Page.SearchAndSelectGridCell(");
            await Assert.That(fixture.Details.StepJournal[0].Preview).DoesNotContain("EnterText");
            await Assert.That(fixture.Details.StepJournal[0].Preview).DoesNotContain("SelectListBoxItem");
            await Assert.That(fixture.Details.StepJournal[0].ValidationStatus)
                .IsEqualTo(RecorderValidationStatus.Valid);
        }
    }

    [Test]
    public async Task RecorderSession_CapturesNumericUpDown_AsOneSpinnerStep()
    {
        var root = new StackPanel();
        var spinner = new NumericUpDown { Value = 8 };
        AutomationProperties.SetAutomationId(spinner, "QuantitySpinner");
        root.Children.Add(spinner);
        using var session = new RecorderSession(
            CreateWindowStub(),
            new AppAutomationRecorderOptions { ShowOverlay = false },
            () => root,
            attachWindowHandlers: false);

        session.Start();
        session.RefreshObservedControlsForTesting();
        session.RegisterKeyboardInputForTesting(spinner);
        spinner.Value = 12;
        session.FlushPendingStateForTesting();

        using (Assert.Multiple())
        {
            await Assert.That(session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(session.StepJournal[0].Preview).Contains(
                "Page.SetSpinnerValue(static page => page.QuantitySpinner, 12);");
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("Page.EnterText");
            await Assert.That(session.StepJournal[0].CanPersist).IsTrue();
        }
    }

    [Test]
    public async Task RecorderSession_CapturesNumericUpDownValueAssertion()
    {
        var root = new StackPanel();
        var spinner = new NumericUpDown { Value = 12 };
        AutomationProperties.SetAutomationId(spinner, "QuantitySpinner");
        root.Children.Add(spinner);
        using var session = new RecorderSession(
            CreateWindowStub(),
            new AppAutomationRecorderOptions { ShowOverlay = false },
            () => root,
            attachWindowHandlers: false);

        session.Start();
        session.CaptureAssertionForTesting(spinner, RecorderAssertionMode.Text);

        using (Assert.Multiple())
        {
            await Assert.That(session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(session.StepJournal[0].Preview).Contains(
                "Page.WaitUntilValueEquals(static page => page.QuantitySpinner, 12);");
            await Assert.That(session.StepJournal[0].CanPersist).IsTrue();
        }
    }

    [Test]
    public async Task RecorderSession_FinalSaveWaitsForActiveAutosave()
    {
        var root = new StackPanel();
        var button = new Button { Content = "Run" };
        AutomationProperties.SetAutomationId(button, "RunButton");
        root.Children.Add(button);
        var autosaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var autosaveRelease = new TaskCompletionSource<RecorderSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manualSaveCallCount = 0;
        var autosaveCallCount = 0;
        var session = new RecorderSession(
            CreateWindowStub(),
            new AppAutomationRecorderOptions
            {
                ShowOverlay = false,
                DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false },
                Validation = new RecorderValidationOptions
                {
                    ValidateRuntimeTargets = false
                }
            },
            () => root,
            attachWindowHandlers: false,
            saveOperation: (steps, _, _) =>
            {
                Interlocked.Increment(ref manualSaveCallCount);
                return Task.FromResult(
                    RecorderSaveResult.Completed(
                        "Saved.",
                        pageFilePath: "MainWindowPage.RecorderControls.g.cs",
                        scenarioFilePath: "MainWindowScenariosBase.RecorderScenarios.g.cs",
                        persistedStepCount: steps.Count,
                        skippedStepCount: 0));
            },
            autosaveOperation: (steps, _, _) =>
            {
                Interlocked.Increment(ref autosaveCallCount);
                autosaveStarted.TrySetResult();
                return autosaveRelease.Task;
            });

        session.Start();
        session.CaptureButtonClickForTesting(button);
        await autosaveStarted.Task;

        var saveTask = session.SaveAsync();
        await Task.Delay(50);

        using (Assert.Multiple())
        {
            await Assert.That(saveTask.IsCompleted).IsFalse();
            await Assert.That(manualSaveCallCount).IsEqualTo(0);
        }

        autosaveRelease.SetResult(
            RecorderSaveResult.Completed(
                "Autosaved.",
                pageFilePath: "MainWindowPage.controls.g.cs.autosave",
                scenarioFilePath: "MainWindowScenariosBase.g.cs.autosave",
                persistedStepCount: 1,
                skippedStepCount: 0));
        var result = await saveTask;
        await WaitForConditionAsync(() => !session.IsBusy);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(autosaveCallCount).IsEqualTo(1);
            await Assert.That(manualSaveCallCount).IsEqualTo(1);
            await Assert.That(session.IsBusy).IsFalse();
        }
    }

    [Test]
    public async Task RecorderSession_AutosaveQueuesLatestChange_WhileBusy()
    {
        var root = new StackPanel();
        var button = new Button { Content = "Run" };
        AutomationProperties.SetAutomationId(button, "RunButton");
        root.Children.Add(button);
        var firstSaveRelease = new TaskCompletionSource<RecorderSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCallCount = 0;
        var secondSavedStepCount = -1;
        var session = new RecorderSession(
            CreateWindowStub(),
            new AppAutomationRecorderOptions
            {
                ShowOverlay = false,
                DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false },
                Validation = new RecorderValidationOptions
                {
                    ValidateRuntimeTargets = false
                }
            },
            () => root,
            attachWindowHandlers: false,
            saveOperation: (steps, _, _) =>
            {
                saveCallCount++;
                if (saveCallCount == 1)
                {
                    return firstSaveRelease.Task;
                }

                secondSavedStepCount = steps.Count;
                return Task.FromResult(
                    RecorderSaveResult.Completed(
                        "Autosaved.",
                        pageFilePath: "MainWindowPage.Recorded.cs",
                        scenarioFilePath: "MainWindowScenariosBase.Recorded.cs",
                        persistedStepCount: steps.Count,
                        skippedStepCount: 0));
            });
        var details = (IAppAutomationRecorderSessionDetails)session;

        session.Start();
        session.CaptureButtonClickForTesting(button);
        await WaitForConditionAsync(() => saveCallCount == 1 && session.IsBusy);

        var stepId = details.StepJournal[0].StepId;
        details.SetStepIgnored(stepId, isIgnored: true);

        await Assert.That(saveCallCount).IsEqualTo(1);

        firstSaveRelease.SetResult(
            RecorderSaveResult.Completed(
                "Autosaved.",
                pageFilePath: "MainWindowPage.Recorded.cs",
                scenarioFilePath: "MainWindowScenariosBase.Recorded.cs",
                persistedStepCount: 1,
                skippedStepCount: 0));

        await WaitForConditionAsync(() => saveCallCount == 2 && !session.IsBusy);

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal[0].IsIgnored).IsEqualTo(true);
            await Assert.That(secondSavedStepCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task RecorderSession_MoveStep_ReordersJournalAndSavedSteps()
    {
        var firstStepId = Guid.NewGuid();
        var secondStepId = Guid.NewGuid();
        var thirdStepId = Guid.NewGuid();
        var savedOrder = Array.Empty<Guid>();
        var session = new RecorderSession(
            CreateWindowStub(),
            new AppAutomationRecorderOptions
            {
                ShowOverlay = false,
                DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false }
            },
            validationRootProvider: null,
            attachWindowHandlers: false,
            saveOperation: (steps, _, _) =>
            {
                savedOrder = steps.Select(static step => step.StepId).ToArray();
                return Task.FromResult(
                    RecorderSaveResult.Completed(
                        "Saved.",
                        pageFilePath: "MainWindowPage.Recorded.cs",
                        scenarioFilePath: "MainWindowScenariosBase.Recorded.cs",
                        persistedStepCount: steps.Count,
                        skippedStepCount: 0));
            });
        var details = (IAppAutomationRecorderSessionDetails)session;
        var reorder = (IRecorderStepReorderSessionDetails)session;
        session.AddRecordedStepForTesting(CreateRecordedButtonStep(firstStepId, "FirstButton"));
        session.AddRecordedStepForTesting(CreateRecordedButtonStep(secondStepId, "SecondButton"));
        session.AddRecordedStepForTesting(CreateRecordedButtonStep(thirdStepId, "ThirdButton"));

        var moved = reorder.MoveStep(secondStepId, RecorderStepMoveDirection.Earlier);
        var blocked = reorder.MoveStep(secondStepId, RecorderStepMoveDirection.Earlier);
        await session.SaveAsync();
        var journalOrder = details.StepJournal.Select(static entry => entry.StepId).ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(moved).IsEqualTo(true);
            await Assert.That(blocked).IsEqualTo(false);
            await Assert.That(journalOrder[0]).IsEqualTo(secondStepId);
            await Assert.That(journalOrder[1]).IsEqualTo(firstStepId);
            await Assert.That(journalOrder[2]).IsEqualTo(thirdStepId);
            await Assert.That(savedOrder[0]).IsEqualTo(secondStepId);
            await Assert.That(savedOrder[1]).IsEqualTo(firstStepId);
            await Assert.That(savedOrder[2]).IsEqualTo(thirdStepId);
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
        await WaitForConditionAsync(() => saveCallCount == 1);
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
            await Assert.That(Path.GetDirectoryName(result.PageFilePath!))
                .IsEqualTo(Path.Combine(directory.Path, "Pages"));
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
    public async Task AutosaveAsync_ReusesRecoveryFiles_WithinGeneratorSession()
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

            public abstract partial class MainWindowScenariosBase<TSession>
            {
            }
            """);

        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var options = CreateOptions(directory.Path, scenarioName: "Recovery Flow");
        var firstStep = CreateRecordedButtonStep(Guid.NewGuid(), "FirstButton");
        var secondStep = CreateRecordedButtonStep(Guid.NewGuid(), "SecondButton");

        var firstResult = await generator.AutosaveAsync(CreateWindowStub(), options, [firstStep], outputDirectoryOverride: null);
        var secondResult = await generator.AutosaveAsync(CreateWindowStub(), options, [firstStep, secondStep], outputDirectoryOverride: null);

        var outputDirectory = Path.Combine(directory.Path, "Recorded");
        var autosaveScenarioFiles = Directory
            .EnumerateFiles(outputDirectory, "MainWindowScenariosBase.Recovery-Flow.autosave.*.g.cs.autosave", SearchOption.TopDirectoryOnly)
            .ToArray();
        var scenarioSource = await File.ReadAllTextAsync(secondResult.ScenarioFilePath!);
        var pageSource = await File.ReadAllTextAsync(secondResult.PageFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(firstResult.Success).IsEqualTo(true);
            await Assert.That(secondResult.Success).IsEqualTo(true);
            await Assert.That(firstResult.ScenarioFilePath).IsEqualTo(secondResult.ScenarioFilePath);
            await Assert.That(firstResult.PageFilePath).IsEqualTo(secondResult.PageFilePath);
            await Assert.That(Path.GetFileName(secondResult.ScenarioFilePath!)).Contains(".autosave.");
            await Assert.That(Path.GetFileName(secondResult.PageFilePath!)).Contains(".autosave.");
            await Assert.That(autosaveScenarioFiles.Length).IsEqualTo(1);
            await Assert.That(scenarioSource).Contains("AppAutomation recorder autosave recovery file.");
            await Assert.That(scenarioSource).Contains("public void Autosave_RecoveryFlow_");
            await Assert.That(scenarioSource).Contains("Page.ClickButton(static page => page.FirstButton);");
            await Assert.That(scenarioSource).Contains("Page.ClickButton(static page => page.SecondButton);");
            await Assert.That(pageSource).Contains("[UiControl(\"FirstButton\", UiControlType.Button, \"FirstButton\", FallbackToName = false)]");
            await Assert.That(pageSource).Contains("[UiControl(\"SecondButton\", UiControlType.Button, \"SecondButton\", FallbackToName = false)]");
        }
    }

    [Test]
    public async Task AutosaveAsync_ReplacesTypeAgnosticControl_WhenTypedActionIsRecorded()
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

            public abstract partial class MainWindowScenariosBase<TSession>
            {
            }
            """);

        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var options = CreateOptions(directory.Path, scenarioName: "Autosave Control Upgrade");
        var genericControl = new RecordedControlDescriptor(
            "RunButton",
            UiControlType.AutomationElement,
            "RunButton",
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: typeof(Button).FullName ?? nameof(Button),
            Warning: null);
        var buttonControl = genericControl with { ControlType = UiControlType.Button };
        var existsStep = new RecordedStep(RecordedActionKind.WaitUntilExists, genericControl);
        var clickStep = new RecordedStep(RecordedActionKind.ClickButton, buttonControl);

        var firstResult = await generator.AutosaveAsync(
            CreateWindowStub(),
            options,
            [existsStep],
            outputDirectoryOverride: null);
        var secondResult = await generator.AutosaveAsync(
            CreateWindowStub(),
            options,
            [existsStep, clickStep],
            outputDirectoryOverride: null);

        var pageSource = await File.ReadAllTextAsync(secondResult.PageFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(firstResult.Success).IsTrue();
            await Assert.That(secondResult.Success).IsTrue();
            await Assert.That(firstResult.PageFilePath).IsEqualTo(secondResult.PageFilePath);
            await Assert.That(pageSource.Split("[UiControl(", StringSplitOptions.None).Length - 1).IsEqualTo(1);
            await Assert.That(pageSource).Contains(
                "[UiControl(\"RunButton\", UiControlType.Button, \"RunButton\", FallbackToName = false)]");
            await Assert.That(pageSource).DoesNotContain("UiControlType.AutomationElement");
        }
    }

    [Test]
    public async Task SaveAsync_PromotesFinalOutputAndRemovesAutosaveRecovery()
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

            public abstract partial class MainWindowScenariosBase<TSession>
            {
            }
            """);

        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var options = CreateOptions(directory.Path, scenarioName: "Recovery Flow");
        var step = CreateRecordedButtonStep(Guid.NewGuid(), "RunButton");

        var autosaveResult = await generator.AutosaveAsync(CreateWindowStub(), options, [step], outputDirectoryOverride: null);
        var saveResult = await generator.SaveAsync(CreateWindowStub(), options, [step], outputDirectoryOverride: null);

        var pageSource = await File.ReadAllTextAsync(saveResult.PageFilePath!);
        var scenarioSource = await File.ReadAllTextAsync(saveResult.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(autosaveResult.Success).IsEqualTo(true);
            await Assert.That(saveResult.Success).IsEqualTo(true);
            await Assert.That(Path.GetFileName(saveResult.ScenarioFilePath!)).DoesNotContain(".autosave.");
            await Assert.That(Path.GetFileName(saveResult.PageFilePath!)).DoesNotContain(".autosave.");
            await Assert.That(File.Exists(autosaveResult.PageFilePath!)).IsFalse();
            await Assert.That(File.Exists(autosaveResult.ScenarioFilePath!)).IsFalse();
            await Assert.That(pageSource).Contains("[UiControl(\"RunButton\", UiControlType.Button, \"RunButton\", FallbackToName = false)]");
            await Assert.That(scenarioSource).DoesNotContain("autosave recovery file");
            await Assert.That(scenarioSource).Contains("public void Recorded_RecoveryFlow_");
            await Assert.That(scenarioSource).Contains("Page.ClickButton(static page => page.RunButton);");
        }
    }

    [Test]
    public async Task SaveAsync_ReusesNotificationControl_ForExistsAssertion()
    {
        using var directory = new TemporaryDirectory();
        CreateAuthoringProject(
            directory.Path,
            existingPageContent:
            """
            using AppAutomation.Abstractions;

            namespace Sample.Authoring.Pages;

            [UiControl("ToastNotification", UiControlType.Notification, "ToastNotification", FallbackToName = false)]
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
        var options = CreateOptions(directory.Path, scenarioName: "Notification Exists Flow");
        var notification = new RecordedControlDescriptor(
            "ToastNotification",
            UiControlType.AutomationElement,
            "ToastNotification",
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: typeof(Border).FullName ?? nameof(Border),
            Warning: null);
        IReadOnlyList<RecordedStep> steps =
        [
            new RecordedStep(RecordedActionKind.WaitUntilExists, notification)
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
            await Assert.That(scenarioSource.Contains(
                "Page.WaitUntilExists(static page => page.ToastNotification);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("ToastNotification2", StringComparison.Ordinal)).IsFalse();
        }
    }

    [Test]
    public async Task SaveAsync_Fails_WhenTypedActionConflictsWithExistingControlType()
    {
        using var directory = new TemporaryDirectory();
        CreateAuthoringProject(
            directory.Path,
            existingPageContent:
            """
            using AppAutomation.Abstractions;

            namespace Sample.Authoring.Pages;

            [UiControl("RunLabel", UiControlType.Label, "RunButton", FallbackToName = false)]
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
        var options = CreateOptions(directory.Path, scenarioName: "Button Type Conflict");
        var runButton = new RecordedControlDescriptor(
            "RunButton",
            UiControlType.Button,
            "RunButton",
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: typeof(Button).FullName ?? nameof(Button),
            Warning: null);

        var result = await generator.SaveAsync(
            CreateWindowStub(),
            options,
            [new RecordedStep(RecordedActionKind.ClickButton, runButton)],
            outputDirectoryOverride: null);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Message).Contains("incompatible with ClickButton");
            await Assert.That(result.Message).Contains("will not generate a duplicate control property");
            await Assert.That(result.PageFilePath).IsNull();
            await Assert.That(result.ScenarioFilePath).IsNull();
            await Assert.That(Directory.EnumerateFiles(directory.Path, "*.RecorderControls.g.cs", SearchOption.AllDirectories))
                .IsEmpty();
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
    public async Task SaveAsync_UsesGridSearchPickerAction_InGeneratedScenario()
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
            Warning: "Recorded grid search picker from configured hint.");
        IReadOnlyList<RecordedStep> steps =
        [
            new RecordedStep(
                RecordedActionKind.SearchAndSelectGridCell,
                gridDescriptor,
                StringValue: "prod",
                RowIndex: 1,
                ColumnIndex: 1,
                ItemValue: "EX-12")
        ];
        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var options = CreateOptions(directory.Path, scenarioName: "Eremex Grid Search Picker");

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
            await Assert.That(scenarioSource.Contains("Page.SearchAndSelectGridCell(static page => page.EremexDemoDataGridAutomationBridge, 1, 1, \"prod\", \"EX-12\");", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("Page.SearchAndSelect(static page => page.EremexDemoDataGridAutomationBridge", StringComparison.Ordinal)).IsEqualTo(false);
        }
    }

    [Test]
    public async Task SaveAsync_DoesNotEmitRuntimeWarningComments_ForPersistableStep()
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
            await Assert.That(scenarioSource.Contains("AppAutomation recorder warning", StringComparison.Ordinal)).IsFalse();
            await Assert.That(scenarioSource.Contains("headless-grid-user-action-adapter-required", StringComparison.Ordinal)).IsFalse();
            await Assert.That(scenarioSource.Contains("Headless validation warning", StringComparison.Ordinal)).IsFalse();
            await Assert.That(scenarioSource.Contains("Page.ExportGrid(static page => page.EremexDemoDataGridAutomationBridge);", StringComparison.Ordinal)).IsTrue();
        }
    }

    [Test]
    public async Task SaveAsync_DoesNotEmitRuntimeComments_WhenAnotherTargetWorks()
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
            await Assert.That(scenarioSource.Contains("AppAutomation recorder warning", StringComparison.Ordinal)).IsFalse();
            await Assert.That(scenarioSource.Contains("headless-action-unsupported", StringComparison.Ordinal)).IsFalse();
            await Assert.That(scenarioSource.Contains("Headless validation failed", StringComparison.Ordinal)).IsFalse();
            await Assert.That(scenarioSource.Contains("Page.ClickButton(static page => page.RunButton);", StringComparison.Ordinal)).IsTrue();
            await Assert.That(scenarioSource.Contains("flaui-target-supported", StringComparison.Ordinal)).IsFalse();
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

    private static RecordedStep CreateRecordedButtonStep(Guid stepId, string automationId)
    {
        return new RecordedStep(
            RecordedActionKind.ClickButton,
            new RecordedControlDescriptor(
                automationId,
                UiControlType.Button,
                automationId,
                UiLocatorKind.AutomationId,
                FallbackToName: false,
                AvaloniaTypeName: typeof(Button).FullName ?? nameof(Button),
                Warning: null),
            StepId: stepId);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("Timed out waiting for recorder test condition.");
            }

            await Task.Delay(10);
        }
    }

    private static Task DrainUiAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.RunJobs();
        }

        return Task.CompletedTask;
    }

    private static RecorderStepJournalEntry CreateJournalEntry(Guid stepId, string preview)
    {
        return new RecorderStepJournalEntry(
            stepId,
            preview,
            "Ready to persist.",
            RecorderValidationStatus.Valid,
            CanPersist: true,
            IsIgnored: false,
            RecorderStepReviewState.Active,
            FailureCode: null,
            LastValidationAt: DateTimeOffset.UtcNow);
    }

    private static string CollectText(Control control)
    {
        var parts = new List<string>();
        CollectText(control, parts);
        return string.Join(Environment.NewLine, parts);
    }

    private static void CollectText(Control control, List<string> parts)
    {
        switch (control)
        {
            case TextBlock textBlock when !string.IsNullOrWhiteSpace(textBlock.Text):
                parts.Add(textBlock.Text!);
                break;
            case Button { Content: string buttonText } when !string.IsNullOrWhiteSpace(buttonText):
                parts.Add(buttonText);
                break;
            case ContentControl { Content: string contentText } when !string.IsNullOrWhiteSpace(contentText):
                parts.Add(contentText);
                break;
            case ContentControl { Content: Control contentControl }:
                CollectText(contentControl, parts);
                break;
            case Border { Child: Control child }:
                CollectText(child, parts);
                break;
            case Panel panel:
                foreach (var childControl in panel.Children.OfType<Control>())
                {
                    CollectText(childControl, parts);
                }

                break;
        }
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

    private static AppAutomationRecorderOptions CreateGridSearchPickerOptions(bool validateRuntimeTargets = true)
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

        options.GridSearchPickerHints.Add(new RecorderGridSearchPickerHint(
            "OrderPositionProductEditor",
            "EremexDemoDataGridAutomationBridge",
            SearchPickerParts.ByAutomationIds(
                "OrderPositionProductEditor_Input",
                "OrderPositionProductEditor_Results",
                applyButtonAutomationId: "OrderPositionProductEditor_ApplyButton",
                expandButtonAutomationId: "OrderPositionProductEditor_ExpandButton",
                resultsKind: SearchPickerResultsKind.ListBox),
            ColumnName: "EremexValue"));
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

    private static AppAutomationRecorderOptions CreateListSearchPickerOptions()
    {
        var options = new AppAutomationRecorderOptions();
        options.SearchPickerHints.Add(new RecorderSearchPickerHint(
            "HistoryOperationPicker",
            SearchPickerParts.ByAutomationIds(
                "HistoryFilterInput",
                "OperationResults",
                applyButtonAutomationId: "ApplyFilterButton",
                expandButtonAutomationId: "ExpandFilterButton",
                resultsKind: SearchPickerResultsKind.ListBox)));
        return options;
    }

    private static AppAutomationRecorderOptions CreateCompositeRecorderOptions(bool useCustomShellCaptureHost = false)
    {
        var options = new AppAutomationRecorderOptions();
        options.DialogHints.Add(new RecorderDialogHint(
            "DeleteDialog",
            DialogControlParts.ByAutomationIds(
                "DeleteDialogMessage",
                "DeleteDialogConfirmButton",
                cancelButtonAutomationId: "DeleteDialogCancelButton",
                dismissButtonAutomationId: "DeleteDialogDismissButton")));
        options.NotificationHints.Add(new RecorderNotificationHint(
            "ExportToast",
            NotificationControlParts.ByAutomationIds(
                "ExportToastText",
                dismissButtonAutomationId: "ExportToastDismissButton")));
        options.ShellNavigationHints.Add(new RecorderShellNavigationHint(
            "Shell",
            ShellNavigationParts.ByAutomationIds(
                "ShellNavigationList",
                paneTabsAutomationId: "ShellPaneTabs",
                activePaneLabelAutomationId: "ShellActivePaneLabel",
                navigationKind: ShellNavigationSourceKind.ListBox)));
        if (useCustomShellCaptureHost)
        {
            options.ShellNavigationHints[0] = options.ShellNavigationHints[0] with
            {
                PaneTabsCaptureLocator = "DockPaneTabsCaptureHost"
            };
        }

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

    private sealed class FakeRecorderSession :
        IAppAutomationRecorderSession,
        IAppAutomationRecorderSessionDetails,
        IRecorderScenarioPathDetails,
        IRecorderStepReorderSessionDetails
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

        public List<(Guid StepId, RecorderStepMoveDirection Direction)> MovedSteps { get; } = new();

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

        public bool CanMoveStep(Guid stepId, RecorderStepMoveDirection direction)
        {
            var index = _stepJournal.FindIndex(entry => entry.StepId == stepId);
            if (index < 0)
            {
                return false;
            }

            return direction switch
            {
                RecorderStepMoveDirection.Earlier => index > 0,
                RecorderStepMoveDirection.Later => index < _stepJournal.Count - 1,
                _ => false
            };
        }

        public bool MoveStep(Guid stepId, RecorderStepMoveDirection direction)
        {
            if (!CanMoveStep(stepId, direction))
            {
                return false;
            }

            var index = _stepJournal.FindIndex(entry => entry.StepId == stepId);
            var targetIndex = direction == RecorderStepMoveDirection.Earlier
                ? index - 1
                : index + 1;
            (_stepJournal[index], _stepJournal[targetIndex]) = (_stepJournal[targetIndex], _stepJournal[index]);
            MovedSteps.Add((stepId, direction));
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

    private sealed class GridSearchPickerSessionFixture : IDisposable
    {
        private readonly NeutralGridHost _sourceGrid;
        private readonly StackPanel _editor;
        private readonly TextBox _searchInput;
        private readonly ListBox _results;
        private readonly RecorderSession _session;

        public GridSearchPickerSessionFixture()
        {
            var options = new AppAutomationRecorderOptions
            {
                Validation = new RecorderValidationOptions { ValidateRuntimeTargets = false }
            };
            options.GridHints.Add(new RecorderGridHint(
                "ItemsGridVisual",
                "ItemsGrid",
                ["ItemCode", "Selection", "State"]));
            options.GridSearchPickerHints.Add(new RecorderGridSearchPickerHint(
                "ItemPicker",
                "ItemsGrid",
                SearchPickerParts.ByAutomationIds(
                    "ItemPicker_Input",
                    "ItemPicker_Results",
                    resultsKind: SearchPickerResultsKind.ListBox),
                ColumnName: "Selection"));
            var root = new StackPanel();
            var rows = new[]
            {
                new NeutralGridRow("Row 1", "Item 41", "Ready"),
                new NeutralGridRow("Row 2", "Item 42", "Ready")
            };
            _sourceGrid = new NeutralGridHost { ItemsSource = rows };
            _editor = new StackPanel { DataContext = rows[1] };
            _searchInput = new TextBox { DataContext = rows[1] };
            _results = new ListBox
            {
                ItemsSource = new[] { "Item 41", "Item 42" },
                DataContext = rows[1]
            };
            var bridge = new Border();

            AutomationProperties.SetAutomationId(_sourceGrid, "ItemsGridVisual");
            AutomationProperties.SetAutomationId(_editor, "ItemPicker");
            AutomationProperties.SetAutomationId(_searchInput, "ItemPicker_Input");
            AutomationProperties.SetAutomationId(_results, "ItemPicker_Results");
            AutomationProperties.SetAutomationId(bridge, "ItemsGrid");

            _editor.Children.Add(_searchInput);
            _editor.Children.Add(_results);
            _sourceGrid.Children.Add(_editor);
            root.Children.Add(_sourceGrid);
            root.Children.Add(bridge);

            _session = new RecorderSession(
                CreateWindowStub(),
                options,
                () => root,
                attachWindowHandlers: false);
            Details = (IAppAutomationRecorderSessionDetails)_session;
            _session.Start();
            _session.RefreshObservedControlsForTesting();
        }

        public IAppAutomationRecorderSessionDetails Details { get; }

        public void RecordCompositeSelection()
        {
            _session.RegisterKeyboardInputForTesting(_searchInput);
            _searchInput.Text = "prod";
            _session.RegisterPointerInputFromSourceForTesting(_results);
            _results.SelectedItem = "Item 42";
        }

        public void ClosePopupAndRaiseLatePrimitiveEvents()
        {
            _sourceGrid.Children.Remove(_editor);
            _session.CaptureListBoxSelectionForTesting(_results);
            _searchInput.Text = "Item 42";
            _session.FlushPendingStateForTesting();
        }

        public void Dispose()
        {
            _session.Dispose();
        }
    }

    private sealed record NeutralGridRow(string ItemCode, string Selection, string State);

    private sealed class NeutralGridHost : StackPanel
    {
        public IEnumerable<NeutralGridRow>? ItemsSource { get; init; }
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

    private sealed class RecorderSaveOperationTracker
    {
        public bool SaveShouldSucceed { get; set; } = true;

        public int ManualSaveCallCount { get; private set; }

        public int AutosaveCallCount { get; private set; }

        public int SavedStepCount { get; private set; }

        public Task<RecorderSaveResult> SaveAsync(
            IReadOnlyList<RecordedStep> steps,
            string? outputDirectory,
            CancellationToken cancellationToken)
        {
            ManualSaveCallCount++;
            SavedStepCount = steps.Count;
            return Task.FromResult(
                SaveShouldSucceed
                    ? RecorderSaveResult.Completed(
                        "Saved.",
                        pageFilePath: "MainWindowPage.RecorderControls.g.cs",
                        scenarioFilePath: "MainWindowScenariosBase.RecorderScenarios.g.cs",
                        persistedStepCount: steps.Count,
                        skippedStepCount: 0)
                    : RecorderSaveResult.Failed("Save failed."));
        }

        public Task<RecorderSaveResult> AutosaveAsync(
            IReadOnlyList<RecordedStep> steps,
            string? outputDirectory,
            CancellationToken cancellationToken)
        {
            AutosaveCallCount++;
            return Task.FromResult(
                RecorderSaveResult.Completed(
                    "Autosaved.",
                    pageFilePath: "MainWindowPage.controls.g.cs.autosave",
                    scenarioFilePath: "MainWindowScenariosBase.g.cs.autosave",
                    persistedStepCount: steps.Count,
                    skippedStepCount: 0));
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
