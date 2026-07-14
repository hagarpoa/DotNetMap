namespace NetMap.Core.Domain;

/// <summary>Stable symbol IDs for agents and cross-run identity.</summary>
public static class Ids
{
    public static string Solution(string pathOrName) =>
        $"solution:{Normalize(pathOrName)}";

    public static string Project(string name) =>
        $"project:{Normalize(name)}";

    public static string File(string projectId, string relativePath) =>
        $"file:{projectId}:{Normalize(relativePath).Replace('\\', '/')}";

    public static string Namespace(string projectId, string name) =>
        string.IsNullOrEmpty(name)
            ? $"ns:{projectId}:global"
            : $"ns:{projectId}:{name}";

    public static string Type(string fullyQualifiedMetadataName) =>
        $"type:{fullyQualifiedMetadataName}";

    public static string Method(string fullyQualifiedMetadataName) =>
        $"method:{fullyQualifiedMetadataName}";

    public static string Property(string fullyQualifiedMetadataName) =>
        $"property:{fullyQualifiedMetadataName}";

    public static string Field(string fullyQualifiedMetadataName) =>
        $"field:{fullyQualifiedMetadataName}";

    public static string Event(string fullyQualifiedMetadataName) =>
        $"event:{fullyQualifiedMetadataName}";

    private static string Normalize(string value) =>
        value.Trim().Replace('\\', '/');
}
