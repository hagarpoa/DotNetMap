using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetMap.Core.Domain;

namespace NetMap.Core.Extraction;

/// <summary>
/// Extracts Solution → Project → Namespace → Type → Members (structure + optional light deps).
/// Does not run SymbolFinder.FindReferences.
/// </summary>
public sealed class StructureExtractor
{
    private readonly IndexOptions _options;

    public StructureExtractor(IndexOptions options) => _options = options;

    public async Task<(SolutionMap Map, int Reused, int Reindexed, int SkippedTest)> ExtractAsync(
        Solution solution,
        string entryPath,
        CancellationToken cancellationToken = default)
    {
        var absoluteEntry = Path.GetFullPath(entryPath);
        var solutionName = Path.GetFileNameWithoutExtension(absoluteEntry);
        var solutionDir = Path.GetDirectoryName(absoluteEntry) ?? absoluteEntry;

        string? solutionHash = null;
        if (File.Exists(absoluteEntry))
            solutionHash = ContentHasher.Sha256File(absoluteEntry);

        var map = new SolutionMap
        {
            Id = Ids.Solution(absoluteEntry),
            Name = solutionName,
            Path = absoluteEntry,
            FileHash = solutionHash,
            Mode = _options.Mode,
            IncludePrivate = _options.IncludePrivate,
            IncludeTest = _options.IncludeTest,
            IndexedAtUtc = DateTimeOffset.UtcNow,
            NetMapVersion = _options.NetMapVersion
        };

        var previousById = _options.PreviousMap?.Projects
            .ToDictionary(p => p.Id, p => p, StringComparer.Ordinal)
            ?? new Dictionary<string, ProjectNode>(StringComparer.Ordinal);

        var previousByName = _options.PreviousMap?.Projects
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ProjectNode>(StringComparer.OrdinalIgnoreCase);

        var useIncremental = _options.ChangedOnly && _options.PreviousMap is not null;
        var reused = 0;
        var reindexed = 0;
        var skippedTest = 0;

        var projects = solution.Projects
            .Where(p => p.Language == LanguageNames.CSharp)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isTest = Visibility.LooksLikeTestProject(project.Name, project.FilePath);
            if (isTest && !_options.IncludeTest)
            {
                skippedTest++;
                _options.Progress?.Report($"skip test project: {project.Name}");
                continue;
            }

            var projectId = Ids.Project(project.Name);

            ProjectNode? previous = null;
            if (useIncremental)
            {
                if (!previousById.TryGetValue(projectId, out previous))
                    previousByName.TryGetValue(project.Name, out previous);
            }

            if (useIncremental && previous is not null)
            {
                var currentFp = await ProjectFingerprint.ComputeAsync(project, solutionDir, cancellationToken).ConfigureAwait(false);
                var previousFp = ProjectFingerprint.ComputeFromNode(previous);
                if (string.Equals(currentFp, previousFp, StringComparison.Ordinal))
                {
                    _options.Progress?.Report($"reuse project: {project.Name}");
                    map.Projects.Add(previous);
                    reused++;
                    continue;
                }

                _options.Progress?.Report($"reindex project: {project.Name} (changed)");
            }
            else
            {
                _options.Progress?.Report($"project: {project.Name}");
            }

            var node = await ExtractProjectAsync(project, solutionDir, isTest, cancellationToken).ConfigureAwait(false);
            if (node is not null)
            {
                map.Projects.Add(node);
                reindexed++;
            }
        }

        return (map, reused, reindexed, skippedTest);
    }

    public async Task<ProjectNode?> ExtractProjectAsync(
        Project project,
        string solutionDir,
        bool isTest,
        CancellationToken cancellationToken)
    {
        var projectPath = project.FilePath ?? project.Name;
        var projectId = Ids.Project(project.Name);

        string? projectHash = null;
        if (project.FilePath is not null && File.Exists(project.FilePath))
            projectHash = ContentHasher.Sha256File(project.FilePath);

        var projectNode = new ProjectNode
        {
            Id = projectId,
            Name = project.Name,
            Path = projectPath,
            TargetFramework = TryGetTfm(project),
            IsTest = isTest,
            FileHash = projectHash
        };

        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is null)
        {
            _options.Progress?.Report($"  warning: no compilation for {project.Name}");
            return projectNode;
        }

        var fileNodes = new Dictionary<string, SourceFileNode>(StringComparer.OrdinalIgnoreCase);
        var namespaceNodes = new Dictionary<string, NamespaceNode>(StringComparer.Ordinal);

        foreach (var document in project.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Visibility.ShouldSkipSourcePath(document.FilePath))
                continue;

            if (document.FilePath is null)
                continue;

            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var content = text.ToString();
            var relative = GetRelativePath(solutionDir, document.FilePath);
            var fileId = Ids.File(projectId, relative);

            if (!fileNodes.ContainsKey(fileId))
            {
                fileNodes[fileId] = new SourceFileNode
                {
                    Id = fileId,
                    RelativePath = relative.Replace('\\', '/'),
                    AbsolutePath = document.FilePath,
                    ContentHash = ContentHasher.Sha256Hex(content),
                    LengthChars = content.Length
                };
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (root is null || semanticModel is null)
                continue;

            foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken) as INamedTypeSymbol;
                if (symbol is null || symbol.IsImplicitlyDeclared)
                    continue;

                // Only primary declaration location for this document (partials: one entry per partial file's type would duplicate ids)
                // Prefer single type node: merge members across partials by id.
                if (!Visibility.IsIncluded(symbol.DeclaredAccessibility, _options.IncludePrivate))
                    continue;

                // Skip nested types that are compiler-generated
                if (symbol.ContainingType is not null && symbol.IsImplicitlyDeclared)
                    continue;

                var typeNode = GetOrCreateType(projectNode, namespaceNodes, projectId, symbol, fileId, typeDecl);
                AddMembers(typeNode, symbol, fileId, typeDecl.SyntaxTree, semanticModel, cancellationToken);

                if (_options.LightDeps)
                    AddTypeLightDeps(typeNode, symbol);
            }

            // Delegates are TypeDeclaration-like but BaseTypeDeclaration might miss some; include DelegateDeclaration
            foreach (var del in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
            {
                var symbol = semanticModel.GetDeclaredSymbol(del, cancellationToken) as INamedTypeSymbol;
                if (symbol is null || !Visibility.IsIncluded(symbol.DeclaredAccessibility, _options.IncludePrivate))
                    continue;

                var typeNode = GetOrCreateType(projectNode, namespaceNodes, projectId, symbol, fileId, del);
                if (_options.LightDeps)
                    AddTypeLightDeps(typeNode, symbol);
            }
        }

        projectNode.Files.AddRange(fileNodes.Values.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase));
        projectNode.Namespaces.AddRange(namespaceNodes.Values.OrderBy(n => n.Name, StringComparer.Ordinal));
        // Types already on projectNode.Types
        projectNode.Types.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));

        return projectNode;
    }

    private TypeNode GetOrCreateType(
        ProjectNode projectNode,
        Dictionary<string, NamespaceNode> namespaceNodes,
        string projectId,
        INamedTypeSymbol symbol,
        string fileId,
        SyntaxNode declaration)
    {
        var fullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "", StringComparison.Ordinal);
        var typeId = Ids.Type(GetMetadataName(symbol));
        var existing = projectNode.Types.FirstOrDefault(t => t.Id == typeId);
        if (existing is not null)
        {
            // Partial: keep first span, still allow members from other files
            return existing;
        }

        var nsName = symbol.ContainingNamespace?.IsGlobalNamespace == true
            ? ""
            : symbol.ContainingNamespace?.ToDisplayString() ?? "";
        var nsId = Ids.Namespace(projectId, nsName);
        if (!namespaceNodes.ContainsKey(nsId))
        {
            namespaceNodes[nsId] = new NamespaceNode { Id = nsId, Name = nsName };
        }

        var span = GetSpan(declaration, fileId);
        var typeNode = new TypeNode
        {
            Id = typeId,
            NamespaceId = nsId,
            Name = symbol.Name,
            FullName = fullName,
            Kind = MapTypeKind(symbol),
            Accessibility = Visibility.ToString(symbol.DeclaredAccessibility),
            IsStatic = symbol.IsStatic,
            IsAbstract = symbol.IsAbstract,
            IsSealed = symbol.IsSealed,
            Summary = XmlSummary.FromSymbol(symbol),
            Span = span
        };

        projectNode.Types.Add(typeNode);
        return typeNode;
    }

    private void AddMembers(
        TypeNode typeNode,
        INamedTypeSymbol symbol,
        string fileId,
        SyntaxTree tree,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        foreach (var member in symbol.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
                continue;

            // Skip nested types here (handled as types)
            if (member is INamedTypeSymbol)
                continue;

            // Skip property/event accessors — exposed via property/event
            if (member is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet
                or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise })
                continue;

            if (!Visibility.IsIncluded(member.DeclaredAccessibility, _options.IncludePrivate))
                continue;

            // Only members declared in this syntax tree (partial types)
            var declRef = member.DeclaringSyntaxReferences
                .FirstOrDefault(r => r.SyntaxTree == tree);
            if (declRef is null)
                continue;

            var syntax = declRef.GetSyntax(cancellationToken);
            var span = GetSpan(syntax, fileId);

            MemberNode? node = member switch
            {
                IMethodSymbol method => CreateMethod(method, span),
                IPropertySymbol prop => CreateProperty(prop, span),
                IFieldSymbol field => CreateField(field, span),
                IEventSymbol ev => CreateEvent(ev, span),
                _ => null
            };

            if (node is null)
                continue;

            // Avoid duplicates across partial re-walks
            if (typeNode.Members.Any(m => m.Id == node.Id))
                continue;

            if (_options.LightDeps)
                AddMemberLightDeps(node, member);

            typeNode.Members.Add(node);
        }

        typeNode.Members.Sort((a, b) =>
        {
            var k = string.Compare(a.Kind.ToString(), b.Kind.ToString(), StringComparison.Ordinal);
            return k != 0 ? k : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });
    }

    private static MemberNode CreateMethod(IMethodSymbol method, SourceSpan span)
    {
        var kind = method.MethodKind == MethodKind.Constructor
            ? MemberKind.Constructor
            : MemberKind.Method;

        var display = method.ToDisplayString(SignatureFormat);
        var idName = method.ToDisplayString(MetadataFormat);

        return new MemberNode
        {
            Id = Ids.Method(idName),
            Name = method.MethodKind == MethodKind.Constructor ? ".ctor" : method.Name,
            Kind = kind,
            Signature = display,
            Accessibility = Visibility.ToString(method.DeclaredAccessibility),
            IsStatic = method.IsStatic,
            IsAbstract = method.IsAbstract,
            IsAsync = method.IsAsync,
            ReturnType = method.MethodKind == MethodKind.Constructor
                ? null
                : method.ReturnType.ToDisplayString(ShortFormat),
            Summary = XmlSummary.FromSymbol(method),
            Span = span
        };
    }

    private static MemberNode CreateProperty(IPropertySymbol prop, SourceSpan span)
    {
        var display = prop.ToDisplayString(SignatureFormat);
        return new MemberNode
        {
            Id = Ids.Property(prop.ToDisplayString(MetadataFormat)),
            Name = prop.Name,
            Kind = MemberKind.Property,
            Signature = display,
            Accessibility = Visibility.ToString(prop.DeclaredAccessibility),
            IsStatic = prop.IsStatic,
            IsAbstract = prop.IsAbstract,
            ReturnType = prop.Type.ToDisplayString(ShortFormat),
            Summary = XmlSummary.FromSymbol(prop),
            Span = span
        };
    }

    private static MemberNode? CreateField(IFieldSymbol field, SourceSpan span)
    {
        // Skip backing fields and implicit
        if (field.AssociatedSymbol is not null || field.IsImplicitlyDeclared)
            return null;

        return new MemberNode
        {
            Id = Ids.Field(field.ToDisplayString(MetadataFormat)),
            Name = field.Name,
            Kind = MemberKind.Field,
            Signature = field.ToDisplayString(SignatureFormat),
            Accessibility = Visibility.ToString(field.DeclaredAccessibility),
            IsStatic = field.IsStatic,
            ReturnType = field.Type.ToDisplayString(ShortFormat),
            Summary = XmlSummary.FromSymbol(field),
            Span = span
        };
    }

    private static MemberNode CreateEvent(IEventSymbol ev, SourceSpan span) => new()
    {
        Id = Ids.Event(ev.ToDisplayString(MetadataFormat)),
        Name = ev.Name,
        Kind = MemberKind.Event,
        Signature = ev.ToDisplayString(SignatureFormat),
        Accessibility = Visibility.ToString(ev.DeclaredAccessibility),
        IsStatic = ev.IsStatic,
        ReturnType = ev.Type.ToDisplayString(ShortFormat),
        Summary = XmlSummary.FromSymbol(ev),
        Span = span
    };

    private static void AddTypeLightDeps(TypeNode typeNode, INamedTypeSymbol symbol)
    {
        if (symbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
            AddDep(typeNode.Dependencies, RelationKind.Inherits, baseType);

        foreach (var iface in symbol.Interfaces)
            AddDep(typeNode.Dependencies, RelationKind.Implements, iface);

        // Attribute types
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass is not null)
                AddDep(typeNode.Dependencies, RelationKind.UsesInSignature, attr.AttributeClass);
        }
    }

    private static void AddMemberLightDeps(MemberNode node, ISymbol member)
    {
        switch (member)
        {
            case IMethodSymbol method:
                if (method.MethodKind != MethodKind.Constructor)
                    AddDep(node.Dependencies, RelationKind.UsesInSignature, method.ReturnType);
                foreach (var p in method.Parameters)
                    AddDep(node.Dependencies, RelationKind.UsesInSignature, p.Type);
                foreach (var t in method.TypeArguments)
                    AddDep(node.Dependencies, RelationKind.UsesInSignature, t);
                break;
            case IPropertySymbol prop:
                AddDep(node.Dependencies, RelationKind.UsesInSignature, prop.Type);
                foreach (var p in prop.Parameters)
                    AddDep(node.Dependencies, RelationKind.UsesInSignature, p.Type);
                break;
            case IFieldSymbol field:
                AddDep(node.Dependencies, RelationKind.UsesInMember, field.Type);
                break;
            case IEventSymbol ev:
                AddDep(node.Dependencies, RelationKind.UsesInSignature, ev.Type);
                break;
        }
    }

    private static void AddDep(List<RelationRef> list, RelationKind kind, ITypeSymbol type)
    {
        type = type is INamedTypeSymbol { IsGenericType: true, OriginalDefinition: var od }
            ? type
            : type;

        // Unwrap arrays / nullable
        while (type is IArrayTypeSymbol arr)
            type = arr.ElementType;

        if (type is INamedTypeSymbol { ConstructedFrom: not null } nt && nt.IsGenericType)
        {
            // still record the constructed type
        }

        if (type.SpecialType is SpecialType.System_Void
            or SpecialType.System_Object
            or SpecialType.System_String
            or SpecialType.System_Boolean
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_Int32
            or SpecialType.System_Int64
            or SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_Decimal
            or SpecialType.System_Char
            or SpecialType.System_DateTime)
            return;

        if (type.TypeKind == Microsoft.CodeAnalysis.TypeKind.TypeParameter)
            return;

        var name = type.ToDisplayString(ShortFormat);
        var id = type is INamedTypeSymbol named
            ? Ids.Type(GetMetadataName(named))
            : Ids.Type(name);

        if (list.Any(r => r.Kind == kind && r.TargetId == id))
            return;

        list.Add(new RelationRef(kind, id, name));
    }

    private static SourceSpan GetSpan(SyntaxNode node, string fileId)
    {
        var span = node.Span;
        var lineSpan = node.SyntaxTree.GetLineSpan(span);
        return new SourceSpan(
            fileId,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.EndLinePosition.Line + 1,
            span.Start,
            span.End,
            span.Length);
    }

    private static Domain.TypeKind MapTypeKind(INamedTypeSymbol symbol)
    {
        if (symbol.TypeKind == Microsoft.CodeAnalysis.TypeKind.Delegate)
            return Domain.TypeKind.Delegate;
        if (symbol.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum)
            return Domain.TypeKind.Enum;
        if (symbol.TypeKind == Microsoft.CodeAnalysis.TypeKind.Interface)
            return Domain.TypeKind.Interface;
        if (symbol.TypeKind == Microsoft.CodeAnalysis.TypeKind.Struct)
            return Domain.TypeKind.Struct;
        if (symbol.IsRecord)
            return Domain.TypeKind.Record;
        return Domain.TypeKind.Class;
    }

    private static string GetMetadataName(INamedTypeSymbol symbol)
    {
        // Namespace.Type+Nested`1
        return symbol.ToDisplayString(MetadataFormat);
    }

    private static string? TryGetTfm(Project project)
    {
        // Roslyn may expose via CompilationOptions / AnalyzerConfig; best-effort from name or null
        try
        {
            if (project.ParseOptions is CSharpParseOptions)
            {
                // Not TFM. Leave null unless we read MSBuild props later.
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string GetRelativePath(string root, string fullPath)
    {
        try
        {
            var rel = Path.GetRelativePath(root, fullPath);
            return rel;
        }
        catch
        {
            return fullPath;
        }
    }

    private static readonly SymbolDisplayFormat ShortFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly SymbolDisplayFormat SignatureFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions:
            SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeType
            | SymbolDisplayMemberOptions.IncludeModifiers
            | SymbolDisplayMemberOptions.IncludeAccessibility,
        parameterOptions:
            SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeDefaultValue
            | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly SymbolDisplayFormat MetadataFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions:
            SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeContainingType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None);
}
