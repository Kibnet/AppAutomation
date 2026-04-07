using AppAutomation.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AppAutomation.Recorder.Avalonia.SourceScanning;

internal sealed class AuthoringProjectScanner
{
    public AuthoringProjectSnapshot Scan(AuthoringTargetConfiguration target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var existingControlsByKey = new Dictionary<string, ExistingControlInfo>(StringComparer.Ordinal);
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        var methodNames = new HashSet<string>(StringComparer.Ordinal);
        ScannedClassInfo? pageClass = null;
        ScannedClassInfo? scenarioClass = null;

        foreach (var filePath in Directory.EnumerateFiles(target.ProjectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (IsIgnoredPath(filePath))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath), cancellationToken: cancellationToken);
            var root = syntaxTree.GetCompilationUnitRoot(cancellationToken);

            foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var namespaceName = GetNamespaceName(declaration);

                if (string.Equals(namespaceName, target.PageNamespace, StringComparison.Ordinal)
                    && string.Equals(declaration.Identifier.ValueText, target.PageClassName, StringComparison.Ordinal))
                {
                    pageClass ??= CreateClassInfo(namespaceName, declaration);
                    foreach (var controlInfo in ParseControls(declaration))
                    {
                        propertyNames.Add(controlInfo.PropertyName);
                        existingControlsByKey.TryAdd(CreateControlKey(controlInfo.LocatorKind, controlInfo.LocatorValue), controlInfo);
                    }
                }

                if (string.Equals(namespaceName, target.ScenarioNamespace, StringComparison.Ordinal)
                    && string.Equals(declaration.Identifier.ValueText, target.ScenarioClassName, StringComparison.Ordinal))
                {
                    scenarioClass ??= CreateClassInfo(namespaceName, declaration);
                    foreach (var method in declaration.Members.OfType<MethodDeclarationSyntax>())
                    {
                        methodNames.Add(method.Identifier.ValueText);
                    }
                }
            }
        }

        return new AuthoringProjectSnapshot(
            pageClass,
            scenarioClass,
            existingControlsByKey,
            propertyNames,
            methodNames);
    }

    internal static string CreateControlKey(UiLocatorKind locatorKind, string locatorValue)
    {
        return $"{locatorKind}:{locatorValue}";
    }

    private static bool IsIgnoredPath(string filePath)
    {
        return filePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || filePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static ScannedClassInfo CreateClassInfo(string namespaceName, ClassDeclarationSyntax declaration)
    {
        var modifiers = declaration.Modifiers
            .Where(static token => !token.IsKind(SyntaxKind.PartialKeyword))
            .Select(static token => token.Text)
            .ToArray();

        return new ScannedClassInfo(
            namespaceName,
            declaration.Identifier.ValueText,
            modifiers.Length == 0 ? "internal" : string.Join(" ", modifiers),
            declaration.TypeParameterList?.ToString() ?? string.Empty,
            declaration.Modifiers.Any(static token => token.IsKind(SyntaxKind.PartialKeyword)));
    }

    private static IEnumerable<ExistingControlInfo> ParseControls(ClassDeclarationSyntax declaration)
    {
        foreach (var attributeList in declaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (!IsUiControlAttribute(attribute) || attribute.ArgumentList is null || attribute.ArgumentList.Arguments.Count < 3)
                {
                    continue;
                }

                if (!TryReadStringLiteral(attribute.ArgumentList.Arguments[0].Expression, out var propertyName)
                    || !TryReadStringLiteral(attribute.ArgumentList.Arguments[2].Expression, out var locatorValue))
                {
                    continue;
                }

                var controlType = TryReadControlType(attribute.ArgumentList.Arguments[1].Expression) ?? UiControlType.AutomationElement;
                var locatorKind = UiLocatorKind.AutomationId;
                var fallbackToName = true;

                foreach (var argument in attribute.ArgumentList.Arguments.Where(static arg => arg.NameEquals is not null))
                {
                    if (argument.NameEquals is null)
                    {
                        continue;
                    }

                    var name = argument.NameEquals.Name.Identifier.ValueText;
                    if (string.Equals(name, nameof(UiControlAttribute.LocatorKind), StringComparison.Ordinal))
                    {
                        locatorKind = TryReadLocatorKind(argument.Expression) ?? UiLocatorKind.AutomationId;
                    }
                    else if (string.Equals(name, nameof(UiControlAttribute.FallbackToName), StringComparison.Ordinal))
                    {
                        fallbackToName = TryReadBoolean(argument.Expression) ?? true;
                    }
                }

                yield return new ExistingControlInfo(propertyName, controlType, locatorValue, locatorKind, fallbackToName);
            }
        }
    }

    private static bool IsUiControlAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString();
        return name.EndsWith("UiControl", StringComparison.Ordinal)
            || name.EndsWith("UiControlAttribute", StringComparison.Ordinal);
    }

    private static bool TryReadStringLiteral(ExpressionSyntax expression, out string value)
    {
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            value = literal.Token.ValueText;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool? TryReadBoolean(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression) => true,
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression) => false,
            _ => null
        };
    }

    private static UiControlType? TryReadControlType(ExpressionSyntax expression)
    {
        var valueText = expression.ToString().Split('.').LastOrDefault();
        return Enum.TryParse<UiControlType>(valueText, ignoreCase: false, out var value) ? value : null;
    }

    private static UiLocatorKind? TryReadLocatorKind(ExpressionSyntax expression)
    {
        var valueText = expression.ToString().Split('.').LastOrDefault();
        return Enum.TryParse<UiLocatorKind>(valueText, ignoreCase: false, out var value) ? value : null;
    }

    private static string GetNamespaceName(SyntaxNode node)
    {
        for (SyntaxNode? current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                return namespaceDeclaration.Name.ToString();
            }
        }

        return string.Empty;
    }
}
