using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppAutomation.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
public sealed partial class MainWindowPage : UiPage
{
    public MainWindowPage(IUiControlResolver resolver) : base(resolver)
    {
    }
}
""";

        var result = RunGenerator(source, "Sample.Authoring");
        var generatedSources = string.Join(
            Environment.NewLine + Environment.NewLine,
            result.GeneratedSources.Values.OrderBy(static value => value, StringComparer.Ordinal));

        using (Assert.Multiple())
        {
            await Assert.That(result.Diagnostics.Count).IsEqualTo(0);
            await Assert.That(result.GeneratedSources.Count).IsEqualTo(2);
            await Assert.That(generatedSources).Contains("public static class MainWindowPageDefinitions");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.ITextBoxControl NumbersInput => Resolve<global::AppAutomation.Abstractions.ITextBoxControl>(MainWindowPageDefinitions.NumbersInput);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.ILabelControl ResultText => Resolve<global::AppAutomation.Abstractions.ILabelControl>(MainWindowPageDefinitions.ResultText);");
            await Assert.That(generatedSources).Contains("public global::AppAutomation.Abstractions.ISearchPickerControl HistoryOperationPicker => Resolve<global::AppAutomation.Abstractions.ISearchPickerControl>(MainWindowPageDefinitions.HistoryOperationPicker);");
            await Assert.That(generatedSources).Contains("namespace Sample.Authoring.Generated;");
            await Assert.That(generatedSources).Contains("public sealed class SampleAuthoringManifestProvider");
            await Assert.That(generatedSources).Contains("global::Sample.Pages.MainWindowPageDefinitions.Page");
        }
    }

    [Test]
    public async Task Generator_ReportsDiagnostic_ForNonPartialPage()
    {
        const string source = """
using AppAutomation.Abstractions;

namespace Sample.Pages;

[UiControl("ResultText", UiControlType.Label, "ResultText")]
public sealed class MainWindowPage : UiPage
{
    public MainWindowPage(IUiControlResolver resolver) : base(resolver)
    {
    }
}
""";

        var result = RunGenerator(source, "Sample.Authoring");
        var diagnosticIds = string.Join(",", result.Diagnostics.Select(static diagnostic => diagnostic.Id));

        using (Assert.Multiple())
        {
            await Assert.That(result.GeneratedSources.Count).IsEqualTo(0);
            await Assert.That(result.Diagnostics.Count).IsEqualTo(1);
            await Assert.That(diagnosticIds).Contains("EUA001");
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
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var runResult = driver.GetRunResult();
        var generatedSources = runResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .ToDictionary(
                static result => result.HintName,
                static result => result.SourceText.ToString(),
                StringComparer.Ordinal);
        var diagnostics = runResult.Results
            .SelectMany(static result => result.Diagnostics)
            .ToArray();

        return new GeneratorExecutionResult(diagnostics, generatedSources);
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
        IReadOnlyDictionary<string, string> GeneratedSources);
}
