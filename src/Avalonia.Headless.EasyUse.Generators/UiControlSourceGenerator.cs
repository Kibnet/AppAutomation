using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Avalonia.Headless.EasyUse.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class UiControlSourceGenerator : IIncrementalGenerator
{
    private const string LegacyUiControlAttributeMetadataName = "FlaUI.EasyUse.PageObjects.UiControlAttribute";
    private const string LegacyUiPageMetadataName = "FlaUI.EasyUse.PageObjects.UiPage";
    private const string NewUiControlAttributeMetadataName = "Avalonia.Headless.EasyUse.PageObjects.UiControlAttribute";
    private const string NewUiPageMetadataName = "Avalonia.Headless.EasyUse.PageObjects.UiPage";

    private static readonly DiagnosticDescriptor NonPartialClassRule = new(
        id: "AHEU001",
        title: "UiControl requires partial class",
        messageFormat: "Class '{0}' must be partial to use UiControl attributes",
        category: "Avalonia.Headless.EasyUse.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NonUiPageRule = new(
        id: "AHEU002",
        title: "UiControl requires UiPage inheritance",
        messageFormat: "Class '{0}' must inherit from UiPage to use UiControl attributes",
        category: "Avalonia.Headless.EasyUse.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidPropertyNameRule = new(
        id: "AHEU003",
        title: "Invalid generated property name",
        messageFormat: "Property name '{0}' is not a valid C# identifier",
        category: "Avalonia.Headless.EasyUse.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NestedClassRule = new(
        id: "AHEU004",
        title: "Nested classes are not supported",
        messageFormat: "UiControl source generation does not support nested class '{0}'",
        category: "Avalonia.Headless.EasyUse.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var legacyCandidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                LegacyUiControlAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => BuildCandidate(ctx, ApiFlavor.Legacy))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!);

        var newCandidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                NewUiControlAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => BuildCandidate(ctx, ApiFlavor.New))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!);

        context.RegisterSourceOutput(legacyCandidates, static (productionContext, candidate) => Emit(productionContext, candidate));
        context.RegisterSourceOutput(newCandidates, static (productionContext, candidate) => Emit(productionContext, candidate));
    }

    private static PageCandidate? BuildCandidate(GeneratorAttributeSyntaxContext context, ApiFlavor flavor)
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
                if (namedArgument.Key == "LocatorKind" && namedArgument.Value.Value is int value)
                {
                    locatorKind = value;
                }
                else if (namedArgument.Key == "FallbackToName" && namedArgument.Value.Value is bool flag)
                {
                    fallbackToName = flag;
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
            controls.ToImmutable(),
            flavor);
    }

    private static void Emit(SourceProductionContext context, PageCandidate candidate)
    {
        if (!candidate.IsPartial)
        {
            context.ReportDiagnostic(Diagnostic.Create(NonPartialClassRule, candidate.Location, candidate.ClassSymbol.Name));
            return;
        }

        if (candidate.ClassSymbol.ContainingType is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(NestedClassRule, candidate.Location, candidate.ClassSymbol.Name));
            return;
        }

        if (!InheritsFromUiPage(candidate.ClassSymbol, candidate.Flavor))
        {
            context.ReportDiagnostic(Diagnostic.Create(NonUiPageRule, candidate.Location, candidate.ClassSymbol.Name));
            return;
        }

        foreach (var control in candidate.Controls)
        {
            if (!SyntaxFacts.IsValidIdentifier(control.PropertyName))
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidPropertyNameRule, candidate.Location, control.PropertyName));
                return;
            }
        }

        var source = RenderSource(candidate.ClassSymbol, candidate.Controls, candidate.Flavor);
        context.AddSource($"{candidate.ClassSymbol.Name}.{candidate.Flavor}.UiControls.g.cs", source);
    }

    private static bool InheritsFromUiPage(INamedTypeSymbol symbol, ApiFlavor flavor)
    {
        var expected = flavor == ApiFlavor.Legacy ? LegacyUiPageMetadataName : NewUiPageMetadataName;

        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == expected)
            {
                return true;
            }
        }

        return false;
    }

    private static string RenderSource(INamedTypeSymbol classSymbol, ImmutableArray<UiControlDescriptor> controls, ApiFlavor flavor)
    {
        var (automationNamespace, locatorNamespace) = ResolveNamespaces(flavor);

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");

        if (!classSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            source.Append("namespace ")
                .Append(classSymbol.ContainingNamespace.ToDisplayString())
                .AppendLine(";");
            source.AppendLine();
        }

        source.Append("partial class ")
            .Append(classSymbol.Name)
            .AppendLine();
        source.AppendLine("{");

        foreach (var control in controls)
        {
            var (typeName, finderMethod) = ResolveControlAccess(control.ControlTypeValue, automationNamespace);
            source.Append("    public ")
                .Append(typeName)
                .Append(' ')
                .Append(control.PropertyName)
                .Append(" => ")
                .Append(finderMethod)
                .Append("(\"")
                .Append(EscapeStringLiteral(control.LocatorValue))
                .Append("\", ")
                .Append(locatorNamespace)
                .Append('.')
                .Append(ResolveLocatorKind(control.LocatorKindValue))
                .Append(", ")
                .Append(control.FallbackToName ? "true" : "false")
                .AppendLine(");");
        }

        source.AppendLine("}");
        return source.ToString();
    }

    private static (string AutomationNamespace, string LocatorNamespace) ResolveNamespaces(ApiFlavor flavor)
    {
        return flavor == ApiFlavor.Legacy
            ? ("global::FlaUI.Core.AutomationElements", "global::FlaUI.EasyUse.PageObjects.UiLocatorKind")
            : ("global::Avalonia.Headless.EasyUse.AutomationElements", "global::Avalonia.Headless.EasyUse.PageObjects.UiLocatorKind");
    }

    private static (string TypeName, string FinderMethod) ResolveControlAccess(int controlType, string automationNamespace)
    {
        return controlType switch
        {
            1 => ($"{automationNamespace}.TextBox", "FindTextBox"),
            2 => ($"{automationNamespace}.Button", "FindButton"),
            3 => ($"{automationNamespace}.Label", "FindLabel"),
            4 => ($"{automationNamespace}.ListBox", "FindListBox"),
            5 => ($"{automationNamespace}.CheckBox", "FindCheckBox"),
            6 => ($"{automationNamespace}.ComboBox", "FindComboBox"),
            7 => ($"{automationNamespace}.RadioButton", "FindRadioButton"),
            8 => ($"{automationNamespace}.ToggleButton", "FindToggleButton"),
            9 => ($"{automationNamespace}.Slider", "FindSlider"),
            10 => ($"{automationNamespace}.ProgressBar", "FindProgressBar"),
            11 => ($"{automationNamespace}.Calendar", "FindCalendar"),
            12 => ($"{automationNamespace}.DateTimePicker", "FindDateTimePicker"),
            13 => ($"{automationNamespace}.Spinner", "FindSpinner"),
            14 => ($"{automationNamespace}.Tab", "FindTab"),
            15 => ($"{automationNamespace}.Tree", "FindTree"),
            16 => ($"{automationNamespace}.TreeItem", "FindTreeItem"),
            17 => ($"{automationNamespace}.DataGridView", "FindDataGridView"),
            18 => ($"{automationNamespace}.GridRow", "FindDataGridViewRow"),
            19 => ($"{automationNamespace}.GridCell", "FindDataGridViewCell"),
            20 => ($"{automationNamespace}.TabItem", "FindTabItem"),
            21 => ($"{automationNamespace}.Grid", "FindGrid"),
            22 => ($"{automationNamespace}.GridRow", "FindGridRow"),
            23 => ($"{automationNamespace}.GridCell", "FindGridCell"),
            _ => ($"{automationNamespace}.AutomationElement", "FindElement")
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

    private enum ApiFlavor
    {
        Legacy,
        New
    }

    private sealed class PageCandidate
    {
        public PageCandidate(
            INamedTypeSymbol classSymbol,
            bool isPartial,
            Location location,
            ImmutableArray<UiControlDescriptor> controls,
            ApiFlavor flavor)
        {
            ClassSymbol = classSymbol;
            IsPartial = isPartial;
            Location = location;
            Controls = controls;
            Flavor = flavor;
        }

        public INamedTypeSymbol ClassSymbol { get; }

        public bool IsPartial { get; }

        public Location Location { get; }

        public ImmutableArray<UiControlDescriptor> Controls { get; }

        public ApiFlavor Flavor { get; }
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