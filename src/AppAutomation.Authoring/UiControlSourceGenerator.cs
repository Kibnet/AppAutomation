using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AppAutomation.Authoring;

[Generator(LanguageNames.CSharp)]
public sealed class UiControlSourceGenerator : IIncrementalGenerator
{
    private const string UiControlAttributeMetadataName = "AppAutomation.Abstractions.UiControlAttribute";
    private const string UiPageMetadataName = "AppAutomation.Abstractions.UiPage";

    private static readonly DiagnosticDescriptor NonPartialClassRule = new(
        id: "EUA001",
        title: "UiControl requires partial class",
        messageFormat: "Class '{0}' must be partial to use UiControl attributes",
        category: "AppAutomation.Authoring",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NonUiPageRule = new(
        id: "EUA002",
        title: "UiControl requires UiPage inheritance",
        messageFormat: "Class '{0}' must inherit from UiPage to use UiControl attributes",
        category: "AppAutomation.Authoring",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidPropertyNameRule = new(
        id: "EUA003",
        title: "Invalid generated property name",
        messageFormat: "Property name '{0}' is not a valid C# identifier",
        category: "AppAutomation.Authoring",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NestedClassRule = new(
        id: "EUA004",
        title: "Nested classes are not supported",
        messageFormat: "UiControl source generation does not support nested class '{0}'",
        category: "AppAutomation.Authoring",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                UiControlAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) => BuildCandidate(attributeContext))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!);

        context.RegisterSourceOutput(candidates, static (productionContext, candidate) =>
        {
            EmitPageSource(productionContext, candidate);
        });

        var manifestInputs = context.CompilationProvider.Combine(candidates.Collect());
        context.RegisterSourceOutput(manifestInputs, static (productionContext, source) =>
        {
            EmitManifestSource(productionContext, source.Left, source.Right);
        });
    }

    private static PageCandidate? BuildCandidate(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        var classSyntax = classSymbol.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault();

        if (classSyntax is null)
        {
            return null;
        }

        var controls = ImmutableArray.CreateBuilder<UiControlDescriptor>();
        foreach (var attribute in context.Attributes)
        {
            if (attribute.ConstructorArguments.Length < 3)
            {
                continue;
            }

            var propertyName = attribute.ConstructorArguments[0].Value as string;
            var controlTypeValue = attribute.ConstructorArguments[1].Value as int?;
            var locatorValue = attribute.ConstructorArguments[2].Value as string;
            if (string.IsNullOrWhiteSpace(propertyName)
                || string.IsNullOrWhiteSpace(locatorValue)
                || controlTypeValue is null)
            {
                continue;
            }

            var locatorKind = 0;
            var fallbackToName = true;
            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == "LocatorKind" && namedArgument.Value.Value is int locatorKindValue)
                {
                    locatorKind = locatorKindValue;
                }
                else if (namedArgument.Key == "FallbackToName" && namedArgument.Value.Value is bool fallbackToNameValue)
                {
                    fallbackToName = fallbackToNameValue;
                }
            }

            controls.Add(new UiControlDescriptor(
                propertyName!,
                controlTypeValue.Value,
                locatorValue!,
                locatorKind,
                fallbackToName));
        }

        if (controls.Count == 0)
        {
            return null;
        }

        return new PageCandidate(
            classSymbol,
            classSyntax.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)),
            classSyntax.Identifier.GetLocation(),
            controls.ToImmutable());
    }

    private static void EmitPageSource(SourceProductionContext context, PageCandidate candidate)
    {
        if (!ValidateCandidate(context, candidate, reportDiagnostics: true))
        {
            return;
        }

        var source = RenderPageSource(candidate);
        context.AddSource($"{candidate.ClassSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}.UiControls.g.cs", source);
    }

    private static void EmitManifestSource(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<PageCandidate> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        var validCandidates = new List<PageCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!ValidateCandidate(context, candidate, reportDiagnostics: false))
            {
                continue;
            }

            var pageKey = candidate.ClassSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (seen.Add(pageKey))
            {
                validCandidates.Add(candidate);
            }
        }

        if (validCandidates.Count == 0)
        {
            return;
        }

        var source = RenderManifestSource(compilation, validCandidates);
        context.AddSource("UiLocatorManifestProvider.g.cs", source);
    }

    private static bool ValidateCandidate(SourceProductionContext context, PageCandidate candidate, bool reportDiagnostics)
    {
        if (!candidate.IsPartial)
        {
            if (reportDiagnostics)
            {
                context.ReportDiagnostic(Diagnostic.Create(NonPartialClassRule, candidate.Location, candidate.ClassSymbol.Name));
            }

            return false;
        }

        if (candidate.ClassSymbol.ContainingType is not null)
        {
            if (reportDiagnostics)
            {
                context.ReportDiagnostic(Diagnostic.Create(NestedClassRule, candidate.Location, candidate.ClassSymbol.Name));
            }

            return false;
        }

        if (!InheritsFromUiPage(candidate.ClassSymbol))
        {
            if (reportDiagnostics)
            {
                context.ReportDiagnostic(Diagnostic.Create(NonUiPageRule, candidate.Location, candidate.ClassSymbol.Name));
            }

            return false;
        }

        foreach (var control in candidate.Controls)
        {
            if (!SyntaxFacts.IsValidIdentifier(control.PropertyName))
            {
                if (reportDiagnostics)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidPropertyNameRule, candidate.Location, control.PropertyName));
                }

                return false;
            }
        }

        return true;
    }

    private static bool InheritsFromUiPage(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), UiPageMetadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string RenderPageSource(PageCandidate candidate)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        source.AppendLine();

        if (!candidate.ClassSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            source.Append("namespace ")
                .Append(candidate.ClassSymbol.ContainingNamespace.ToDisplayString())
                .AppendLine(";");
            source.AppendLine();
        }

        source.Append("public static class ")
            .Append(candidate.ClassSymbol.Name)
            .AppendLine("Definitions");
        source.AppendLine("{");

        foreach (var control in candidate.Controls)
        {
            source.Append("    public static global::AppAutomation.Abstractions.UiControlDefinition ")
                .Append(control.PropertyName)
                .Append(" { get; } = new(")
                .Append('"')
                .Append(EscapeStringLiteral(control.PropertyName))
                .Append("\", global::AppAutomation.Abstractions.UiControlType.")
                .Append(ResolveControlType(control.ControlTypeValue))
                .Append(", \"")
                .Append(EscapeStringLiteral(control.LocatorValue))
                .Append("\", global::AppAutomation.Abstractions.UiLocatorKind.")
                .Append(ResolveLocatorKind(control.LocatorKindValue))
                .Append(", ")
                .Append(control.FallbackToName ? "true" : "false")
                .AppendLine(");");
        }

        source.Append("    public static global::AppAutomation.Abstractions.UiPageDefinition Page { get; } = new(\"")
            .Append(EscapeStringLiteral(candidate.ClassSymbol.ToDisplayString()))
            .Append("\", \"")
            .Append(EscapeStringLiteral(candidate.ClassSymbol.Name))
            .Append("\", new global::AppAutomation.Abstractions.UiControlDefinition[]");
        source.AppendLine();
        source.AppendLine("    {");
        foreach (var control in candidate.Controls)
        {
            source.Append("        ")
                .Append(control.PropertyName)
                .AppendLine(",");
        }

        source.AppendLine("    });");
        source.AppendLine("}");
        source.AppendLine();

        source.Append("public sealed partial class ")
            .Append(candidate.ClassSymbol.Name)
            .AppendLine();
        source.AppendLine("{");

        foreach (var control in candidate.Controls)
        {
            source.Append("    public ")
                .Append(ResolveAccessorTypeName(control.ControlTypeValue))
                .Append(' ')
                .Append(control.PropertyName)
                .Append(" => Resolve<")
                .Append(ResolveAccessorTypeName(control.ControlTypeValue))
                .Append(">(")
                .Append(candidate.ClassSymbol.Name)
                .Append("Definitions.")
                .Append(control.PropertyName)
                .AppendLine(");");
        }

        source.AppendLine("}");
        return source.ToString();
    }

    private static string RenderManifestSource(Compilation compilation, IReadOnlyList<PageCandidate> candidates)
    {
        var assemblyName = compilation.AssemblyName ?? "AppAutomationAuthoring";
        var providerNamespace = $"{assemblyName}.Generated";
        var providerName = $"{SanitizeIdentifier(assemblyName)}ManifestProvider";

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.Append("namespace ")
            .Append(providerNamespace)
            .AppendLine(";");
        source.AppendLine();
        source.Append("public sealed class ")
            .Append(providerName)
            .Append(" : global::AppAutomation.Abstractions.IUiLocatorManifestProvider")
            .AppendLine();
        source.AppendLine("{");
        source.AppendLine("    public global::AppAutomation.Abstractions.UiLocatorManifest GetManifest() => Manifest;");
        source.AppendLine();
        source.Append("    public static global::AppAutomation.Abstractions.UiLocatorManifest Manifest { get; } = new(\"1\", \"")
            .Append(EscapeStringLiteral(assemblyName))
            .Append("\", new global::AppAutomation.Abstractions.UiPageDefinition[]");
        source.AppendLine();
        source.AppendLine("    {");
        foreach (var candidate in candidates)
        {
            var fullyQualifiedPageNamespace = candidate.ClassSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : candidate.ClassSymbol.ContainingNamespace.ToDisplayString();
            var definitionsReference = string.IsNullOrEmpty(fullyQualifiedPageNamespace)
                ? $"{candidate.ClassSymbol.Name}Definitions.Page"
                : $"global::{fullyQualifiedPageNamespace}.{candidate.ClassSymbol.Name}Definitions.Page";

            source.Append("        ")
                .Append(definitionsReference)
                .AppendLine(",");
        }

        source.AppendLine("    });");
        source.AppendLine("}");
        return source.ToString();
    }

    private static string ResolveAccessorTypeName(int controlType)
    {
        return controlType switch
        {
            1 => "global::AppAutomation.Abstractions.ITextBoxControl",
            2 => "global::AppAutomation.Abstractions.IButtonControl",
            3 => "global::AppAutomation.Abstractions.ILabelControl",
            4 => "global::AppAutomation.Abstractions.IListBoxControl",
            5 => "global::AppAutomation.Abstractions.ICheckBoxControl",
            6 => "global::AppAutomation.Abstractions.IComboBoxControl",
            7 => "global::AppAutomation.Abstractions.IRadioButtonControl",
            8 => "global::AppAutomation.Abstractions.IToggleButtonControl",
            9 => "global::AppAutomation.Abstractions.ISliderControl",
            10 => "global::AppAutomation.Abstractions.IProgressBarControl",
            11 => "global::AppAutomation.Abstractions.ICalendarControl",
            12 => "global::AppAutomation.Abstractions.IDateTimePickerControl",
            13 => "global::AppAutomation.Abstractions.ISpinnerControl",
            14 => "global::AppAutomation.Abstractions.ITabControl",
            15 => "global::AppAutomation.Abstractions.ITreeControl",
            16 => "global::AppAutomation.Abstractions.ITreeItemControl",
            17 => "global::AppAutomation.Abstractions.IGridControl",
            18 => "global::AppAutomation.Abstractions.IGridRowControl",
            19 => "global::AppAutomation.Abstractions.IGridCellControl",
            20 => "global::AppAutomation.Abstractions.ITabItemControl",
            21 => "global::AppAutomation.Abstractions.IGridControl",
            22 => "global::AppAutomation.Abstractions.IGridRowControl",
            23 => "global::AppAutomation.Abstractions.IGridCellControl",
            24 => "global::AppAutomation.Abstractions.ISearchPickerControl",
            25 => "global::AppAutomation.Abstractions.IDateRangeFilterControl",
            26 => "global::AppAutomation.Abstractions.INumericRangeFilterControl",
            _ => "global::AppAutomation.Abstractions.IUiControl"
        };
    }

    private static string ResolveControlType(int controlType)
    {
        return controlType switch
        {
            1 => "TextBox",
            2 => "Button",
            3 => "Label",
            4 => "ListBox",
            5 => "CheckBox",
            6 => "ComboBox",
            7 => "RadioButton",
            8 => "ToggleButton",
            9 => "Slider",
            10 => "ProgressBar",
            11 => "Calendar",
            12 => "DateTimePicker",
            13 => "Spinner",
            14 => "Tab",
            15 => "Tree",
            16 => "TreeItem",
            17 => "DataGridView",
            18 => "DataGridViewRow",
            19 => "DataGridViewCell",
            20 => "TabItem",
            21 => "Grid",
            22 => "GridRow",
            23 => "GridCell",
            24 => "SearchPicker",
            25 => "DateRangeFilter",
            26 => "NumericRangeFilter",
            _ => "AutomationElement"
        };
    }

    private static string ResolveLocatorKind(int locatorKind)
    {
        return locatorKind switch
        {
            1 => "Name",
            _ => "AutomationId"
        };
    }

    private static string EscapeStringLiteral(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        if (builder.Length == 0 || !SyntaxFacts.IsIdentifierStartCharacter(builder[0]))
        {
            builder.Insert(0, 'G');
        }

        return builder.ToString();
    }

    private sealed class PageCandidate
    {
        public PageCandidate(
            INamedTypeSymbol classSymbol,
            bool isPartial,
            Location location,
            ImmutableArray<UiControlDescriptor> controls)
        {
            ClassSymbol = classSymbol;
            IsPartial = isPartial;
            Location = location;
            Controls = controls;
        }

        public INamedTypeSymbol ClassSymbol { get; }

        public bool IsPartial { get; }

        public Location Location { get; }

        public ImmutableArray<UiControlDescriptor> Controls { get; }
    }

    private sealed class UiControlDescriptor
    {
        public UiControlDescriptor(
            string propertyName,
            int controlTypeValue,
            string locatorValue,
            int locatorKindValue,
            bool fallbackToName)
        {
            PropertyName = propertyName;
            ControlTypeValue = controlTypeValue;
            LocatorValue = locatorValue;
            LocatorKindValue = locatorKindValue;
            FallbackToName = fallbackToName;
        }

        public string PropertyName { get; }

        public int ControlTypeValue { get; }

        public string LocatorValue { get; }

        public int LocatorKindValue { get; }

        public bool FallbackToName { get; }
    }
}
