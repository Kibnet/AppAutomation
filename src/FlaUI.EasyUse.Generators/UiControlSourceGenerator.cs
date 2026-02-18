using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FlaUI.EasyUse.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class UiControlSourceGenerator : IIncrementalGenerator
{
    private const string UiControlAttributeMetadataName = "FlaUI.EasyUse.PageObjects.UiControlAttribute";
    private const string UiPageMetadataName = "FlaUI.EasyUse.PageObjects.UiPage";

    private static readonly DiagnosticDescriptor NonPartialClassRule = new(
        id: "FEU001",
        title: "UiControl requires partial class",
        messageFormat: "Class '{0}' must be partial to use UiControl attributes",
        category: "FlaUI.EasyUse.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NonUiPageRule = new(
        id: "FEU002",
        title: "UiControl requires UiPage inheritance",
        messageFormat: "Class '{0}' must inherit from UiPage to use UiControl attributes",
        category: "FlaUI.EasyUse.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidPropertyNameRule = new(
        id: "FEU003",
        title: "Invalid generated property name",
        messageFormat: "Property name '{0}' is not a valid C# identifier",
        category: "FlaUI.EasyUse.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NestedClassRule = new(
        id: "FEU004",
        title: "Nested classes are not supported",
        messageFormat: "UiControl source generation does not support nested class '{0}'",
        category: "FlaUI.EasyUse.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                UiControlAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => BuildCandidate(ctx))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!);

        context.RegisterSourceOutput(candidates, static (productionContext, candidate) =>
        {
            Emit(productionContext, candidate);
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
            controls.ToImmutable());
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

        if (!InheritsFromUiPage(candidate.ClassSymbol))
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

        var source = RenderSource(candidate.ClassSymbol, candidate.Controls);
        context.AddSource($"{candidate.ClassSymbol.Name}.UiControls.g.cs", source);
    }

    private static bool InheritsFromUiPage(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == UiPageMetadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static string RenderSource(INamedTypeSymbol classSymbol, ImmutableArray<UiControlDescriptor> controls)
    {
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
            var (typeName, finderMethod) = ResolveControlAccess(control.ControlTypeValue);
            source.Append("    public ")
                .Append(typeName)
                .Append(' ')
                .Append(control.PropertyName)
                .Append(" => ")
                .Append(finderMethod)
                .Append("(\"")
                .Append(EscapeStringLiteral(control.LocatorValue))
                .Append("\", global::FlaUI.EasyUse.PageObjects.UiLocatorKind.")
                .Append(ResolveLocatorKind(control.LocatorKindValue))
                .Append(", ")
                .Append(control.FallbackToName ? "true" : "false")
                .AppendLine(");");
        }

        source.AppendLine("}");
        return source.ToString();
    }

    private static (string TypeName, string FinderMethod) ResolveControlAccess(int controlType)
    {
        return controlType switch
        {
            1 => ("global::FlaUI.Core.AutomationElements.TextBox", "FindTextBox"),
            2 => ("global::FlaUI.Core.AutomationElements.Button", "FindButton"),
            3 => ("global::FlaUI.Core.AutomationElements.Label", "FindLabel"),
            4 => ("global::FlaUI.Core.AutomationElements.ListBox", "FindListBox"),
            5 => ("global::FlaUI.Core.AutomationElements.CheckBox", "FindCheckBox"),
            6 => ("global::FlaUI.Core.AutomationElements.ComboBox", "FindComboBox"),
            7 => ("global::FlaUI.Core.AutomationElements.RadioButton", "FindRadioButton"),
            8 => ("global::FlaUI.Core.AutomationElements.ToggleButton", "FindToggleButton"),
            9 => ("global::FlaUI.Core.AutomationElements.Slider", "FindSlider"),
            10 => ("global::FlaUI.Core.AutomationElements.ProgressBar", "FindProgressBar"),
            11 => ("global::FlaUI.Core.AutomationElements.Calendar", "FindCalendar"),
            12 => ("global::FlaUI.Core.AutomationElements.DateTimePicker", "FindDateTimePicker"),
            13 => ("global::FlaUI.Core.AutomationElements.Spinner", "FindSpinner"),
            14 => ("global::FlaUI.Core.AutomationElements.Tab", "FindTab"),
            15 => ("global::FlaUI.Core.AutomationElements.Tree", "FindTree"),
            16 => ("global::FlaUI.Core.AutomationElements.TreeItem", "FindTreeItem"),
            17 => ("global::FlaUI.Core.AutomationElements.DataGridView", "FindDataGridView"),
            18 => ("global::FlaUI.Core.AutomationElements.GridRow", "FindDataGridViewRow"),
            19 => ("global::FlaUI.Core.AutomationElements.GridCell", "FindDataGridViewCell"),
            20 => ("global::FlaUI.Core.AutomationElements.TabItem", "FindTabItem"),
            21 => ("global::FlaUI.Core.AutomationElements.Grid", "FindGrid"),
            22 => ("global::FlaUI.Core.AutomationElements.GridRow", "FindGridRow"),
            23 => ("global::FlaUI.Core.AutomationElements.GridCell", "FindGridCell"),
            _ => ("global::FlaUI.Core.AutomationElements.AutomationElement", "FindElement")
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
