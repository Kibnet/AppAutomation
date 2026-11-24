using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppAutomation.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Authoring.Tests;

public sealed class UiControlSourceGeneratorTests
{
    [Test]
    public async Task Generator_EmitsAccessorsAndManifestProvider_ForValidPage()
    {
        const string source = """
using AppAutomation.Abstractions;

namespace Sample.Pages;

[UiControl("NumbersInput", UiControlType.TextBox, "NumbersInput")]
[UiControl("ResultText", UiControlType.Label, "ResultText", LocatorKind = UiLocatorKind.Name, FallbackToName = false)]
[UiControl("HistoryOperationPicker", UiControlType.SearchPicker, "HistoryOperationPicker")]
[UiControl("CreatedAtFilter", UiControlType.DateRangeFilter, "CreatedAtFilter")]
[UiControl("AmountFilter", UiControlType.NumericRangeFilter, "AmountFilter")]
[UiControl("DeleteDialog", UiControlType.Dialog, "DeleteDialog")]
[UiControl("ExportToast", UiControlType.Notification, "ExportToast")]
[UiControl("ReportExport", UiControlType.FolderExport, "ReportExport")]
[UiControl("Shell", UiControlType.ShellNavigation, "Shell")]
[UiControl("Categories", UiControlType.MultiSelect, "Categories")]
[UiControl("StatusFilter", UiControlType.ComboBoxFilter, "StatusFilter")]
[UiControl("TableSearch", UiControlType.Search, "TableSearch")]
[UiControl("StartTimePicker", UiControlType.TimePicker, "StartTimePicker")]
[UiControl("DetailsExpander", UiControlType.Expander, "DetailsExpander")]
[UiControl("AccentColor", UiControlType.ColorPicker, "AccentColor")]
[UiControl("MainMenu", UiControlType.Menu, "MainMenu")]
[UiControl("RefreshMenuItem", UiControlType.MenuItem, "RefreshMenuItem")]
[UiControl("UnitQuantityEditors", UiControlType.MultiItemControlCollection, "UnitQuantityCollection")]
public sealed partial class MainWindowPage : UiPage
{
    public MainWindowPage(IUiControlResolver resolver) : base(resolver)
    {
    }
}
""";

        var result = RunGenerator(source, "Sample.Authoring");
        await AssertSuccessfulGeneration(result);
        var generatedSources = string.Join(
            Environment.NewLine + Environment.NewLine,
            result.GeneratedSources.Values.OrderBy(static value => value, StringComparer.Ordinal));

        using (Assert.Multiple())
        {
            await Assert.That(result.GeneratedSources.Count).IsEqualTo(2);
            await Assert.That(generatedSources).Contains("public static class MainWindowPageDefinitions");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.ITextBoxControl NumbersInput => Resolve<global::AppAutomation.Abstractions.ITextBoxControl>(MainWindowPageDefinitions.NumbersInput);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.ILabelControl ResultText => Resolve<global::AppAutomation.Abstractions.ILabelControl>(MainWindowPageDefinitions.ResultText);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.ISearchPickerControl HistoryOperationPicker => Resolve<global::AppAutomation.Abstractions.ISearchPickerControl>(MainWindowPageDefinitions.HistoryOperationPicker);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.IDateRangeFilterControl CreatedAtFilter => Resolve<global::AppAutomation.Abstractions.IDateRangeFilterControl>(MainWindowPageDefinitions.CreatedAtFilter);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.INumericRangeFilterControl AmountFilter => Resolve<global::AppAutomation.Abstractions.INumericRangeFilterControl>(MainWindowPageDefinitions.AmountFilter);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.IDialogControl DeleteDialog => Resolve<global::AppAutomation.Abstractions.IDialogControl>(MainWindowPageDefinitions.DeleteDialog);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.INotificationControl ExportToast => Resolve<global::AppAutomation.Abstractions.INotificationControl>(MainWindowPageDefinitions.ExportToast);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.IFolderExportControl ReportExport => Resolve<global::AppAutomation.Abstractions.IFolderExportControl>(MainWindowPageDefinitions.ReportExport);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.IShellNavigationControl Shell => Resolve<global::AppAutomation.Abstractions.IShellNavigationControl>(MainWindowPageDefinitions.Shell);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.IMultiSelectControl Categories => Resolve<global::AppAutomation.Abstractions.IMultiSelectControl>(MainWindowPageDefinitions.Categories);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.IComboBoxFilterControl StatusFilter => Resolve<global::AppAutomation.Abstractions.IComboBoxFilterControl>(MainWindowPageDefinitions.StatusFilter);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.ISearchControl TableSearch => Resolve<global::AppAutomation.Abstractions.ISearchControl>(MainWindowPageDefinitions.TableSearch);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.ITimePickerControl StartTimePicker => Resolve<global::AppAutomation.Abstractions.ITimePickerControl>(MainWindowPageDefinitions.StartTimePicker);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.IExpanderControl DetailsExpander => Resolve<global::AppAutomation.Abstractions.IExpanderControl>(MainWindowPageDefinitions.DetailsExpander);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.IColorPickerControl AccentColor => Resolve<global::AppAutomation.Abstractions.IColorPickerControl>(MainWindowPageDefinitions.AccentColor);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.IMenuControl MainMenu => Resolve<global::AppAutomation.Abstractions.IMenuControl>(MainWindowPageDefinitions.MainMenu);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.IMenuItemControl RefreshMenuItem => Resolve<global::AppAutomation.Abstractions.IMenuItemControl>(MainWindowPageDefinitions.RefreshMenuItem);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.IMultiItemControlCollection UnitQuantityEditors => Resolve<global::AppAutomation.Abstractions.IMultiItemControlCollection>(MainWindowPageDefinitions.UnitQuantityEditors);");
            await Assert.That(generatedSources).Contains("namespace Sample.Authoring.Generated;");
            await Assert.That(generatedSources).Contains("public sealed class SampleAuthoringManifestProvider");
            await Assert.That(generatedSources).Contains("global::Sample.Pages.MainWindowPageDefinitions.Page");
        }
    }

    [Test]
    [Arguments("public sealed class MainWindowPage", false, "EUA001")]
    [Arguments("public partial class MainWindowPage<T>", false, "EUA007")]
    [Arguments("file partial class MainWindowPage", false, "EUA007")]
    [Arguments("public partial class MainWindowPage", true, "EUA004")]
    public async Task Generator_ReportsDiagnostic_ForUnsupportedPage(string declaration, bool nested, string diagnosticId)
    {
        var source = $$"""
using AppAutomation.Abstractions;

namespace Sample.Pages;

{{(nested ? "public class Container {" : string.Empty)}}
[UiControl("ResultText", UiControlType.Label, "ResultText")]
{{declaration}} : UiPage
{
    public MainWindowPage(IUiControlResolver resolver) : base(resolver)
    {
    }
}
{{(nested ? "}" : string.Empty)}}
""";

        var result = RunGenerator(source, "Sample.Authoring");

        using (Assert.Multiple())
        {
            await Assert.That(result.GeneratedSources.Count).IsEqualTo(0);
            await Assert.That(result.Diagnostics.Count).IsEqualTo(1);
            await Assert.That(result.Diagnostics.Single().Id).IsEqualTo(diagnosticId);
            await Assert.That(result.GeneratorExceptions.Count).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments("public", "MainWindowPage")]
    [Arguments("internal", "MainWindowPage")]
    [Arguments("public abstract", "MainWindowPage")]
    [Arguments("internal abstract", "@class")]
    public async Task Generator_PreservesSupportedPageShapeAndLiteralValues(string modifiers, string typeName)
    {
        var source = $$"""
using AppAutomation.Abstractions;

namespace Sample.Pages;

[UiControl("ResultText", UiControlType.Label, "line1\r\n\"quoted\"\\path\t\0\u0001\u2028\u2029", LocatorKind = UiLocatorKind.Name)]
{{modifiers}} partial class {{typeName}} : UiPage
{
    protected {{typeName}}(IUiControlResolver resolver) : base(resolver) { }
}

internal sealed class DerivedPage : {{typeName}}
{
    public DerivedPage(IUiControlResolver resolver) : base(resolver) { }
}
""";

        var result = RunGenerator(source, "Sample.Authoring");
        await AssertSuccessfulGeneration(result);
        var page = result.Compilation.GetTypeByMetadataName("Sample.Pages." + typeName.TrimStart('@'))!;
        var originalLocator = (string)page.GetAttributes().Single().ConstructorArguments[2].Value!;
        var generatedTree = result.Compilation.SyntaxTrees.Single(static tree => tree.FilePath.EndsWith(".UiControls.g.cs", StringComparison.Ordinal));
        var generatedLiterals = generatedTree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>()
            .Select(static literal => literal.Token.ValueText).ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(page.DeclaredAccessibility).IsEqualTo(modifiers.StartsWith("public", StringComparison.Ordinal)
                ? Accessibility.Public : Accessibility.Internal);
            await Assert.That(page.IsAbstract).IsEqualTo(modifiers.Contains("abstract", StringComparison.Ordinal));
            await Assert.That(page.IsSealed).IsFalse();
            await Assert.That(generatedLiterals.Count(value => value == originalLocator)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Generator_UsesDistinctStableHints_ForEqualNamesInDifferentNamespaces()
    {
        const string source = """
using AppAutomation.Abstractions;

namespace A.B
{
    [UiControl("ResultText", UiControlType.Label, "ResultText")]
    public partial class MainWindowPage(IUiControlResolver resolver) : UiPage(resolver);
}

namespace A_B
{
    [UiControl("ResultText", UiControlType.Label, "ResultText")]
    public partial class MainWindowPage(IUiControlResolver resolver) : UiPage(resolver);
}

namespace a.b
{
    [UiControl("ResultText", UiControlType.Label, "ResultText")]
    public partial class MainWindowPage(IUiControlResolver resolver) : UiPage(resolver);
}
""";

        var first = RunGenerator(source, "Sample.Authoring");
        var second = RunGenerator(source, "Sample.Authoring");
        await AssertSuccessfulGeneration(first);
        await AssertSuccessfulGeneration(second);

        using (Assert.Multiple())
        {
            await Assert.That(first.GeneratedSources.Count).IsEqualTo(4);
            await Assert.That(first.GeneratedSources.Keys.OrderBy(static hint => hint, StringComparer.Ordinal)
                .SequenceEqual(second.GeneratedSources.Keys.OrderBy(static hint => hint, StringComparer.Ordinal))).IsTrue();
        }
    }

    private static async Task AssertSuccessfulGeneration(GeneratorExecutionResult result)
    {
        using (Assert.Multiple())
        {
            await Assert.That(result.Diagnostics.Count).IsEqualTo(0);
            await Assert.That(result.GeneratorExceptions.Count).IsEqualTo(0);
            await Assert.That(string.Join(Environment.NewLine,
                result.Compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)))
                .IsEqualTo(string.Empty);
        }
    }

    private static GeneratorExecutionResult RunGenerator(string source, string assemblyName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new UiControlSourceGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        var generatedSources = runResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .ToDictionary(
                static result => result.HintName,
                static result => result.SourceText.ToString(),
                StringComparer.Ordinal);
        var generatorExceptions = runResult.Results
            .Where(static result => result.Exception is not null)
            .Select(static result => result.Exception!)
            .ToArray();

        return new GeneratorExecutionResult(diagnostics, generatedSources, outputCompilation, generatorExceptions);
    }

    private static MetadataReference[] CreateMetadataReferences()
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            references[path] = MetadataReference.CreateFromFile(path);
        }

        foreach (var assembly in new[]
                 {
                     typeof(object).Assembly,
                     typeof(Enumerable).Assembly,
                     typeof(UiPage).Assembly,
                     typeof(Compilation).Assembly,
                     typeof(CSharpCompilation).Assembly
                 })
        {
            if (!string.IsNullOrWhiteSpace(assembly.Location))
            {
                references[assembly.Location] = MetadataReference.CreateFromFile(assembly.Location);
            }
        }

        return references.Values.ToArray();
    }

    private sealed record GeneratorExecutionResult(
        IReadOnlyList<Diagnostic> Diagnostics,
        IReadOnlyDictionary<string, string> GeneratedSources,
        Compilation Compilation,
        IReadOnlyList<Exception> GeneratorExceptions);
}
