using System.Reflection;

namespace NetMap.Core.Store;

public static class SchemaLoader
{
    public static string LoadV0()
    {
        var assembly = typeof(SchemaLoader).Assembly;
        const string resourceName = "NetMap.Core.schema.v0.sql";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is not null)
        {
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        // Fallback: repo layout when running from source without embedded resource.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "schema", "v0.sql"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schema", "v0.sql")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "schema", "v0.sql"))
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        throw new InvalidOperationException(
            "Could not load schema/v0.sql. Ensure it is embedded as NetMap.Core.schema.v0.sql.");
    }
}
