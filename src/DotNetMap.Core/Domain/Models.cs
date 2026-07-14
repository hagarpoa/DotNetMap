namespace DotNetMap.Core.Domain;

public enum IndexMode
{
    Structure,
    StructureLightDeps,
    FullRelations
}

public enum TypeKind
{
    Class,
    Record,
    Struct,
    Interface,
    Enum,
    Delegate
}

public enum MemberKind
{
    Method,
    Property,
    Field,
    Event,
    Constructor
}

public enum RelationKind
{
    Inherits,
    Implements,
    UsesInSignature,
    UsesInMember,
    /// <summary>Method invocation / constructor call from a member body.</summary>
    Calls,
    /// <summary>Another type or method references this symbol (consumer side).</summary>
    ReferencedBy
}

public sealed record SourceSpan(
    string? FileId,
    int? StartLine,
    int? EndLine,
    int? StartOffset,
    int? EndOffset,
    int SizeChars)
{
    /// <summary>Number of source lines spanned (inclusive), or 0 if unknown.</summary>
    public int LineCount =>
        StartLine is int s && EndLine is int e && e >= s ? e - s + 1 : 0;
}

public sealed record RelationRef(
    RelationKind Kind,
    string TargetId,
    string TargetName);

public sealed class SolutionMap
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? FileHash { get; init; }
    public IndexMode Mode { get; init; } = IndexMode.StructureLightDeps;
    public bool IncludePrivate { get; init; }
    public bool IncludeTest { get; init; }
    public DateTimeOffset IndexedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string DotNetMapVersion { get; init; } = "0.2.0";
    public List<ProjectNode> Projects { get; init; } = [];
}

public sealed class ProjectNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? TargetFramework { get; init; }
    public bool IsTest { get; init; }
    public string? FileHash { get; init; }
    public List<SourceFileNode> Files { get; init; } = [];
    public List<NamespaceNode> Namespaces { get; init; } = [];
    public List<TypeNode> Types { get; init; } = [];
}

public sealed class SourceFileNode
{
    public required string Id { get; init; }
    public required string RelativePath { get; init; }
    public required string AbsolutePath { get; init; }
    public required string ContentHash { get; init; }
    public int LengthChars { get; init; }
}

public sealed class NamespaceNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed class TypeNode
{
    public required string Id { get; init; }
    public required string NamespaceId { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public required TypeKind Kind { get; init; }
    public required string Accessibility { get; init; }
    public bool IsStatic { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsSealed { get; init; }
    public string? Summary { get; init; }
    public SourceSpan Span { get; init; } = new(null, null, null, null, null, 0);
    public List<RelationRef> Dependencies { get; init; } = [];
    public List<RelationRef> Consumers { get; init; } = [];
    public int TokenEstimate { get; set; }
    public List<MemberNode> Members { get; init; } = [];
}

public sealed class MemberNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required MemberKind Kind { get; init; }
    public required string Signature { get; init; }
    public required string Accessibility { get; init; }
    public bool IsStatic { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsAsync { get; init; }
    public string? ReturnType { get; init; }
    public string? Summary { get; init; }
    public SourceSpan Span { get; init; } = new(null, null, null, null, null, 0);
    public List<RelationRef> Dependencies { get; init; } = [];
    public List<RelationRef> Consumers { get; init; } = [];
    public int TokenEstimate { get; set; }
}

public sealed class IndexStatus
{
    public required int SchemaVersion { get; init; }
    public string? SolutionPath { get; init; }
    public string? SolutionName { get; init; }
    public DateTimeOffset? IndexedAtUtc { get; init; }
    public string? IndexMode { get; init; }
    public bool IncludePrivate { get; init; }
    public bool IncludeTest { get; init; }
    public string? DotNetMapVersion { get; init; }
    public int ProjectCount { get; init; }
    public int TypeCount { get; init; }
    public int MemberCount { get; init; }
    public int FileCount { get; init; }
    public long DatabaseBytes { get; init; }
    public int? TokenEstimateOverview { get; init; }
}
