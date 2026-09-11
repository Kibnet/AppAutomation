using AppAutomation.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AppAutomation.Recorder.Avalonia.SourceScanning;

internal sealed class AuthoringProjectScanner
{
    public ScenarioDestinationDiscoveryResult DiscoverScenarioDestinations(
        string? projectDirectory,
        string? scenarioNamespaceRoot,
        string? outputSubdirectoryRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return ScenarioDestinationDiscoveryResult.Failed("Authoring project directory is not configured.");
        }

        var normalizedProjectDirectory = Path.GetFullPath(projectDirectory);
        if (!Directory.Exists(normalizedProjectDirectory))
        {
            return ScenarioDestinationDiscoveryResult.Failed(
                $"Authoring project directory '{normalizedProjectDirectory}' does not exist.");
        }

        var normalizedNamespaceRoot = scenarioNamespaceRoot?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedNamespaceRoot))
        {
            return ScenarioDestinationDiscoveryResult.Failed("Scenario namespace root is not configured.");
        }

        if (string.IsNullOrWhiteSpace(outputSubdirectoryRoot))
        {
            return ScenarioDestinationDiscoveryResult.Failed("Output subdirectory root is not configured.");
        }

        var declarations = new Dictionary<(string Namespace, string Name, int Arity), ClassDeclarationSyntax>();
        foreach (var filePath in EnumerateSourceFiles(normalizedProjectDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath), cancellationToken: cancellationToken);
            var root = syntaxTree.GetCompilationUnitRoot(cancellationToken);
            foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (declaration.Ancestors().OfType<TypeDeclarationSyntax>().Any()
                    || !declaration.Modifiers.Any(static token => token.IsKind(SyntaxKind.PartialKeyword)))
                {
                    continue;
                }

                var namespaceName = GetNamespaceName(declaration);
                if (!IsWithinNamespaceRoot(namespaceName, normalizedNamespaceRoot))
                {
                    continue;
                }

                var key = (namespaceName, declaration.Identifier.ValueText, declaration.TypeParameterList?.Parameters.Count ?? 0);
                declarations.TryAdd(key, declaration);
            }
        }

        var ambiguousClass = declarations.Keys
            .GroupBy(static key => (key.Namespace, key.Name))
            .FirstOrDefault(static group => group.Select(static key => key.Arity).Distinct().Skip(1).Any());
        if (ambiguousClass is not null)
        {
            return ScenarioDestinationDiscoveryResult.Failed(
                $"Scenario class '{ambiguousClass.Key.Namespace}.{ambiguousClass.Key.Name}' is ambiguous because multiple generic arities were found.");
        }

        var destinations = declarations
            .Select(pair => CreateDestination(
                pair.Key.Namespace,
                pair.Key.Name,
                pair.Key.Arity,
                pair.Value.TypeParameterList?.ToString() ?? string.Empty,
                CreateTypeParameterSignature(pair.Value),
                normalizedNamespaceRoot,
                outputSubdirectoryRoot.Trim()))
            .OrderBy(static destination => destination.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static destination => destination.DisplayName, StringComparer.Ordinal)
            .ToArray();

        return destinations.Length == 0
            ? ScenarioDestinationDiscoveryResult.Failed(
                $"No partial scenario classes were found under namespace '{normalizedNamespaceRoot}'.")
            : new ScenarioDestinationDiscoveryResult(destinations, Error: null);
    }

    public AuthoringProjectSnapshot Scan(AuthoringTargetConfiguration target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var existingControlsByKey = new Dictionary<string, ExistingControlInfo>(StringComparer.Ordinal);
        var existingControlsByTypedKey = new Dictionary<string, ExistingControlInfo>(StringComparer.Ordinal);
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        var methodNames = new HashSet<string>(StringComparer.Ordinal);
        var pageDeclarations = new List<(string FilePath, ClassDeclarationSyntax Declaration)>();
        var scenarioDeclarations = new List<(string FilePath, ClassDeclarationSyntax Declaration)>();

        var syntaxTrees = Directory
            .EnumerateFiles(target.ProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static filePath => !IsIgnoredPath(filePath))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(filePath => CSharpSyntaxTree.ParseText(
                File.ReadAllText(filePath),
                path: filePath,
                cancellationToken: cancellationToken))
            .ToArray();
        var compilation = CreateScanCompilation(syntaxTrees);

        foreach (var syntaxTree in syntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = syntaxTree.FilePath;
            var root = syntaxTree.GetCompilationUnitRoot(cancellationToken);
            var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);

            foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var namespaceName = GetNamespaceName(declaration);

                if (string.Equals(namespaceName, target.PageNamespace, StringComparison.Ordinal)
                    && string.Equals(declaration.Identifier.ValueText, target.PageClassName, StringComparison.Ordinal))
                {
                    if (!IsGeneratedFile(filePath))
                    {
                        pageDeclarations.Add((filePath, declaration));
                    }

                    foreach (var controlInfo in ParseControls(declaration, semanticModel, cancellationToken))
                    {
                        propertyNames.Add(controlInfo.PropertyName);
                        existingControlsByKey.TryAdd(CreateControlKey(controlInfo.LocatorKind, controlInfo.LocatorValue), controlInfo);
                        existingControlsByTypedKey.TryAdd(
                            CreateTypedControlKey(controlInfo.LocatorKind, controlInfo.LocatorValue, controlInfo.ControlType),
                            controlInfo);
                    }
                }

                if (string.Equals(namespaceName, target.ScenarioNamespace, StringComparison.Ordinal)
                    && string.Equals(declaration.Identifier.ValueText, target.ScenarioClassName, StringComparison.Ordinal)
                    && (target.ScenarioGenericArity is null
                        || target.ScenarioGenericArity == (declaration.TypeParameterList?.Parameters.Count ?? 0)))
                {
                    if (!IsGeneratedFile(filePath))
                    {
                        scenarioDeclarations.Add((filePath, declaration));
                    }

                    foreach (var method in declaration.Members.OfType<MethodDeclarationSyntax>())
                    {
                        methodNames.Add(method.Identifier.ValueText);
                    }
                }
            }
        }

        var pageClass = SelectPreferredClass(pageDeclarations, target.PageClassName);
        var scenarioClass = SelectPreferredClass(scenarioDeclarations, target.ScenarioClassName);

        return new AuthoringProjectSnapshot(
            pageClass,
            scenarioClass,
            existingControlsByKey,
            existingControlsByTypedKey,
            propertyNames,
            methodNames);
    }

    internal IReadOnlyList<ExistingControlInfo> ScanControlsFile(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            return Array.Empty<ExistingControlInfo>();
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(
            File.ReadAllText(filePath),
            path: filePath,
            cancellationToken: cancellationToken);
        var compilation = CreateScanCompilation([syntaxTree]);
        var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
        var root = syntaxTree.GetCompilationUnitRoot(cancellationToken);
        return root
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .SelectMany(declaration => ParseControls(declaration, semanticModel, cancellationToken))
            .ToArray();
    }

    internal static string CreateControlKey(UiLocatorKind locatorKind, string locatorValue)
    {
        return $"{locatorKind}:{locatorValue}";
    }

    internal static string CreateTypedControlKey(
        UiLocatorKind locatorKind,
        string locatorValue,
        UiControlType controlType)
    {
        return $"{CreateControlKey(locatorKind, locatorValue)}:{controlType}";
    }

    private static bool IsIgnoredPath(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.Contains(".autosave.", StringComparison.OrdinalIgnoreCase)
            || filePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || filePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string projectDirectory)
    {
        return Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static filePath => !IsIgnoredPath(filePath) && !IsGeneratedFile(filePath));
    }

    private static bool IsGeneratedFile(string filePath)
    {
        return Path.GetFileName(filePath).EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithinNamespaceRoot(string namespaceName, string namespaceRoot)
    {
        return string.Equals(namespaceName, namespaceRoot, StringComparison.Ordinal)
            || namespaceName.StartsWith(namespaceRoot + ".", StringComparison.Ordinal);
    }

    private static RecordedScenarioDestination CreateDestination(
        string namespaceName,
        string className,
        int genericArity,
        string typeParameterListText,
        string typeParameterSignature,
        string namespaceRoot,
        string outputSubdirectoryRoot)
    {
        var relativeNamespace = string.Equals(namespaceName, namespaceRoot, StringComparison.Ordinal)
            ? string.Empty
            : namespaceName[(namespaceRoot.Length + 1)..];
        var displayName = string.IsNullOrEmpty(relativeNamespace)
            ? className
            : $"{relativeNamespace}.{className}";
        var outputSubdirectory = string.IsNullOrEmpty(relativeNamespace)
            ? outputSubdirectoryRoot
            : relativeNamespace
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Aggregate(outputSubdirectoryRoot, Path.Combine);

        return new RecordedScenarioDestination(displayName, namespaceName, className, outputSubdirectory)
        {
            GenericArity = genericArity,
            TypeParameterListText = typeParameterListText,
            TypeParameterSignature = typeParameterSignature
        };
    }

    private static ScannedClassInfo? SelectPreferredClass(
        IReadOnlyList<(string FilePath, ClassDeclarationSyntax Declaration)> declarations,
        string className)
    {
        var selected = declarations
            .OrderByDescending(candidate => string.Equals(
                Path.GetFileName(candidate.FilePath),
                $"{className}.cs",
                StringComparison.OrdinalIgnoreCase))
            .ThenBy(static candidate => candidate.FilePath, StringComparer.Ordinal)
            .FirstOrDefault();
        return selected.Declaration is null
            ? null
            : CreateClassInfo(GetNamespaceName(selected.Declaration), selected.Declaration, selected.FilePath);
    }

    private static ScannedClassInfo CreateClassInfo(
        string namespaceName,
        ClassDeclarationSyntax declaration,
        string filePath)
    {
        var modifiers = declaration.Modifiers
            .Where(static token => !token.IsKind(SyntaxKind.PartialKeyword))
            .Select(static token => token.Text)
            .ToArray();

        return new ScannedClassInfo(
            namespaceName,
            declaration.Identifier.ValueText,
            filePath,
            modifiers.Length == 0 ? "internal" : string.Join(" ", modifiers),
            declaration.TypeParameterList?.ToString() ?? string.Empty,
            CreateTypeParameterSignature(declaration),
            declaration.TypeParameterList?.Parameters.Count ?? 0,
            declaration.Modifiers.Any(static token => token.IsKind(SyntaxKind.PartialKeyword)));
    }

    private static IEnumerable<ExistingControlInfo> ParseControls(
        ClassDeclarationSyntax declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var attributeList in declaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (!IsUiControlAttribute(attribute) || attribute.ArgumentList is null || attribute.ArgumentList.Arguments.Count < 3)
                {
                    continue;
                }

                if (!TryReadStringConstant(
                        attribute.ArgumentList.Arguments[0].Expression,
                        semanticModel,
                        cancellationToken,
                        out var propertyName)
                    || !TryReadStringConstant(
                        attribute.ArgumentList.Arguments[2].Expression,
                        semanticModel,
                        cancellationToken,
                        out var locatorValue))
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

    private static bool TryReadStringConstant(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out string value)
    {
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            value = literal.Token.ValueText;
            return true;
        }

        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constant.HasValue && constant.Value is string constantValue)
        {
            value = constantValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static CSharpCompilation CreateScanCompilation(IEnumerable<SyntaxTree> syntaxTrees)
    {
        var references = new[]
            {
                typeof(object).Assembly.Location,
                typeof(UiControlAttribute).Assembly.Location
            }
            .Where(static location => !string.IsNullOrWhiteSpace(location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static location => MetadataReference.CreateFromFile(location));
        return CSharpCompilation.Create(
            "AppAutomation.Recorder.SourceScan",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
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
        return string.Join(
            ".",
            node.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(static declaration => declaration.Name.ToString()));
    }

    private static string CreateTypeParameterSignature(ClassDeclarationSyntax declaration)
    {
        return string.Join(
            ",",
            declaration.TypeParameterList?.Parameters
                .Select(static parameter => parameter.Identifier.ValueText)
                ?? Array.Empty<string>());
    }
}
