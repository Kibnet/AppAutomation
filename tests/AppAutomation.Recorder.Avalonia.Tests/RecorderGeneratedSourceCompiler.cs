using AppAutomation.Authoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal static class RecorderGeneratedSourceCompiler
{
    public static Func<string, Task> CompileNumericAssertion(
        string generatedStatements, string checkpointText = "182,50 руб.")
    {
        var source = $$"""
            using System;
            using System.Threading.Tasks;
            using AppAutomation.Abstractions;
            using TUnit.Assertions;
            using TUnit.Assertions.Extensions;

            public static class GeneratedNumericAssertion
            {
                public static async Task RunAsync(string displayedTotal, string checkpointText)
                {
                    var Page = new ReplayPage();
                    Page.RequiredVolume.Value = 2;
                    Page.UnitPrice.Text = checkpointText;
                    Page.DisplayedTotal.Text = displayedTotal;
                    {{generatedStatements}}
                }
            }

            public sealed class ReplayPage
            {
                public ReplayTextBox UnitPrice { get; } = new("UnitPrice");
                public ReplayTextBox DisplayedTotal { get; } = new("DisplayedTotal");
                public ReplaySpinner RequiredVolume { get; } = new();
            }

            public sealed class ReplayTextBox(string automationId) : ITextBoxControl
            {
                public string AutomationId => automationId;
                public string Name => automationId;
                public bool IsEnabled => true;
                public string Text { get; set; } = string.Empty;
                public void Enter(string value) => Text = value;
            }

            public sealed class ReplaySpinner : ISpinnerControl
            {
                public string AutomationId => "RequiredVolume";
                public string Name => AutomationId;
                public bool IsEnabled => true;
                public double Value { get; set; }
            }
            """;
        var compilation = CSharpCompilation.Create(
            $"RecorderNumericReplay_{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(source)],
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        if (!emitted.Success)
        {
            throw new InvalidOperationException(
                "Generated numeric assertion did not compile: "
                + string.Join(Environment.NewLine, emitted.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        }

        var assembly = System.Reflection.Assembly.Load(image.ToArray());
        var method = assembly.GetType("GeneratedNumericAssertion")!
            .GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;
        return displayedTotal => (Task)method.Invoke(null, [displayedTotal, checkpointText])!;
    }

    public static IReadOnlyList<Diagnostic> Compile(string projectDirectory)
    {
        var syntaxTrees = Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(static path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "RecorderGeneratedOutput",
            syntaxTrees,
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new UiControlSourceGenerator().AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generatedCompilation, out var generatorDiagnostics);

        return generatorDiagnostics
            .Concat(generatedCompilation.GetDiagnostics())
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
    }

    private static MetadataReference[] CreateMetadataReferences()
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            references[path] = MetadataReference.CreateFromFile(path);
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            {
                references[assembly.Location] = MetadataReference.CreateFromFile(assembly.Location);
            }
        }

        return references.Values.ToArray();
    }
}
