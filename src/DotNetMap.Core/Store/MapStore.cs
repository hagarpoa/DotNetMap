using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using DotNetMap.Core.Domain;

namespace DotNetMap.Core.Store;

public sealed class MapStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly SqliteConnection _connection;
    private readonly string _dbPath;

    private MapStore(SqliteConnection connection, string dbPath)
    {
        _connection = connection;
        _dbPath = dbPath;
    }

    public static MapStore Open(string dbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        var conn = new SqliteConnection(cs);
        conn.Open();

        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
            pragma.ExecuteNonQuery();
        }

        var store = new MapStore(conn, dbPath);
        store.EnsureSchema();
        return store;
    }

    /// <summary>Create a command on the open connection (for analysis queries).</summary>
    public SqliteCommand CreateCommand() => _connection.CreateCommand();

    /// <summary>Current on-write schema (DNM-014 edges table).</summary>
    public const int CurrentSchemaVersion = 1;

    public void EnsureSchema()
    {
        var sql = SchemaLoader.LoadV0();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();

        if (GetMeta("schema_version") is null)
            SetMeta("schema_version", "0");

        // Additive columns for older DBs (CREATE TABLE IF NOT EXISTS does not alter).
        EnsureColumn("types", "locations_json", "TEXT NOT NULL DEFAULT '[]'");

        // Old DBs: materialize edges from JSON once (no full reindex required).
        TryMigrateEdgesFromJson();
    }

    private void EnsureColumn(string table, string column, string columnDef)
    {
        try
        {
            using var check = _connection.CreateCommand();
            check.CommandText = $"PRAGMA table_info({table});";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }
        catch (SqliteException)
        {
            return;
        }

        try
        {
            using var alter = _connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDef};";
            alter.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // race / unsupported
        }
    }

    public void WriteMap(SolutionMap map)
    {
        TokenEstimator.EstimateOverview(map);

        using var tx = _connection.BeginTransaction();

        // Full rewrite for MVP simplicity (incremental reindex per project comes in PR-6).
        Execute("""
            DELETE FROM edges;
            DELETE FROM body_fts;
            DELETE FROM members_fts;
            DELETE FROM types_fts;
            DELETE FROM members;
            DELETE FROM types;
            DELETE FROM namespaces;
            DELETE FROM source_files;
            DELETE FROM projects;
            DELETE FROM solutions;
            """);

        InsertSolution(map);

        var bodyFiles = 0;
        var edgeCount = 0;
        foreach (var project in map.Projects)
        {
            InsertProject(map.Id, project);
            foreach (var file in project.Files)
            {
                InsertFile(project.Id, file);
                if (map.IndexBody && TryInsertBodyFts(file))
                    bodyFiles++;
            }
            foreach (var ns in project.Namespaces)
                InsertNamespace(project.Id, ns);
            foreach (var type in project.Types)
            {
                TokenEstimator.EstimateType(type);
                InsertType(project.Id, type);
                edgeCount += InsertRelationEdges(type.Id, type.Dependencies, type.Consumers);
                foreach (var member in type.Members)
                {
                    TokenEstimator.EstimateMember(member);
                    InsertMember(type.Id, type.FullName, member);
                    edgeCount += InsertRelationEdges(member.Id, member.Dependencies, member.Consumers);
                }
            }
        }

        SetMeta("schema_version", CurrentSchemaVersion.ToString());
        SetMeta("solution_path", map.Path);
        SetMeta("solution_name", map.Name);
        SetMeta("indexed_at_utc", map.IndexedAtUtc.UtcDateTime.ToString("o"));
        SetMeta("index_mode", ModeToString(map.Mode));
        SetMeta("include_private", map.IncludePrivate ? "1" : "0");
        SetMeta("include_test", map.IncludeTest ? "1" : "0");
        SetMeta("index_body", map.IndexBody ? "1" : "0");
        SetMeta("body_file_count", bodyFiles.ToString());
        SetMeta("edge_count", edgeCount.ToString());
        SetMeta("dotnetmap_version", map.DotNetMapVersion);
        SetMeta("token_estimate_overview", TokenEstimator.EstimateOverview(map).ToString());

        tx.Commit();
    }

    /// <summary>Max chars of source text stored per file in body_fts (DNM-013).</summary>
    public const int MaxBodyFileChars = 512_000;

    public IndexStatus GetStatus()
    {
        var schema = int.TryParse(GetMeta("schema_version"), out var v) ? v : -1;
        DateTimeOffset? indexedAt = null;
        if (DateTimeOffset.TryParse(GetMeta("indexed_at_utc"), out var dt))
            indexedAt = dt;

        int? tokenEst = null;
        if (int.TryParse(GetMeta("token_estimate_overview"), out var te))
            tokenEst = te;

        long dbBytes = 0;
        if (File.Exists(_dbPath))
            dbBytes = new FileInfo(_dbPath).Length;

        var bodyFiles = 0;
        _ = int.TryParse(GetMeta("body_file_count"), out bodyFiles);
        var edgeCount = 0;
        if (!int.TryParse(GetMeta("edge_count"), out edgeCount))
        {
            try { edgeCount = ScalarInt("SELECT COUNT(*) FROM edges"); }
            catch (SqliteException) { edgeCount = 0; }
        }

        return new IndexStatus
        {
            SchemaVersion = schema,
            SolutionPath = GetMeta("solution_path"),
            SolutionName = GetMeta("solution_name"),
            IndexedAtUtc = indexedAt,
            IndexMode = GetMeta("index_mode"),
            IncludePrivate = GetMeta("include_private") == "1",
            IncludeTest = GetMeta("include_test") == "1",
            IndexBody = GetMeta("index_body") == "1",
            BodyFileCount = bodyFiles,
            EdgeCount = edgeCount,
            DotNetMapVersion = GetMeta("dotnetmap_version"),
            ProjectCount = ScalarInt("SELECT COUNT(*) FROM projects"),
            TypeCount = ScalarInt("SELECT COUNT(*) FROM types"),
            MemberCount = ScalarInt("SELECT COUNT(*) FROM members"),
            FileCount = ScalarInt("SELECT COUNT(*) FROM source_files"),
            DatabaseBytes = dbBytes,
            TokenEstimateOverview = tokenEst
        };
    }

    /// <summary>Whether the normalized edges table has rows (DNM-014).</summary>
    public bool HasEdges()
    {
        try
        {
            return ScalarInt("SELECT COUNT(*) FROM edges") > 0;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>
    /// Outbound edges from <paramref name="fromId"/> (dependencies / calls).
    /// </summary>
    public IReadOnlyList<EdgeRow> GetOutboundEdges(string fromId, int max = 100)
    {
        max = Math.Clamp(max, 1, 500);
        return QueryEdges(
            "SELECT from_id, to_id, kind, file, line FROM edges WHERE from_id = $id LIMIT $max",
            fromId, max);
    }

    /// <summary>
    /// Inbound edges to <paramref name="toId"/> (consumers / callers as from→to).
    /// </summary>
    public IReadOnlyList<EdgeRow> GetInboundEdges(string toId, int max = 100)
    {
        max = Math.Clamp(max, 1, 500);
        return QueryEdges(
            "SELECT from_id, to_id, kind, file, line FROM edges WHERE to_id = $id LIMIT $max",
            toId, max);
    }

    /// <summary>
    /// SQL multi-hop walk (1..depth) over <c>edges</c>. Depth 2 = classic 2-hop graph query.
    /// Outbound follows from→to; inbound reverses (treats to as start).
    /// </summary>
    public IReadOnlyList<GraphHop> QueryGraphHops(
        string rootId,
        int depth = 2,
        bool outbound = true,
        int max = 80)
    {
        depth = Math.Clamp(depth, 1, 4);
        max = Math.Clamp(max, 1, 200);

        var hops = new List<GraphHop>();
        var frontier = new List<string> { rootId };
        var seen = new HashSet<string>(StringComparer.Ordinal) { rootId };

        for (var d = 1; d <= depth && hops.Count < max; d++)
        {
            var next = new List<string>();
            foreach (var node in frontier)
            {
                if (hops.Count >= max)
                    break;

                var edges = outbound
                    ? GetOutboundEdges(node, max: max)
                    : GetInboundEdges(node, max: max);

                foreach (var e in edges)
                {
                    var neighbor = outbound ? e.ToId : e.FromId;
                    if (!seen.Add(neighbor))
                        continue;

                    hops.Add(new GraphHop(neighbor, node, e.Kind, d, e.File, e.Line));
                    next.Add(neighbor);
                    if (hops.Count >= max)
                        break;
                }
            }

            frontier = next;
            if (frontier.Count == 0)
                break;
        }

        return hops;
    }

    public bool HasSolutionData() =>
        !string.IsNullOrEmpty(GetMeta("solution_path"));

    /// <summary>Resolve on-disk absolute path for an indexed relative source path.</summary>
    public string? ResolveFileAbsolutePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var normalized = relativePath.Replace('\\', '/');
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT absolute_path FROM source_files
                WHERE relative_path = $rel
                   OR replace(relative_path, '\', '/') = $rel
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$rel", normalized);
            var hit = cmd.ExecuteScalar() as string;
            if (!string.IsNullOrEmpty(hit) && File.Exists(hit))
                return hit;
        }

        var solutionPath = GetMeta("solution_path");
        var root = Source.SourceSnippetReader.ResolveSolutionRoot(solutionPath);
        return Source.SourceSnippetReader.ResolveAbsolutePath(normalized, null, root);
    }


    /// <summary>
    /// Loads solution/projects/types (no members) for scoped consumer updates.
    /// </summary>
    public SolutionMap? LoadMapSkeleton() => LoadMapCore(includeFiles: false, includeMembers: false);

    /// <summary>
    /// Loads the full map for incremental reindex (files + types + members + relations).
    /// </summary>
    public SolutionMap? LoadFullMap() => LoadMapCore(includeFiles: true, includeMembers: true);

    private SolutionMap? LoadMapCore(bool includeFiles, bool includeMembers)
    {
        var path = GetMeta("solution_path");
        if (string.IsNullOrEmpty(path))
            return null;

        var name = GetMeta("solution_name") ?? Path.GetFileNameWithoutExtension(path);
        var includePrivate = GetMeta("include_private") == "1";
        var includeTest = GetMeta("include_test") == "1";
        var mode = (GetMeta("index_mode") ?? "structure+light-deps") switch
        {
            "full-relations" => IndexMode.FullRelations,
            "structure" => IndexMode.Structure,
            _ => IndexMode.StructureLightDeps
        };

        DateTimeOffset indexedAt = DateTimeOffset.UtcNow;
        if (DateTimeOffset.TryParse(GetMeta("indexed_at_utc"), out var dt))
            indexedAt = dt;

        var solutionId = Ids.Solution(path);
        var map = new SolutionMap
        {
            Id = solutionId,
            Name = name,
            Path = path,
            Mode = mode,
            IncludePrivate = includePrivate,
            IncludeTest = includeTest,
            IndexedAtUtc = indexedAt,
            DotNetMapVersion = GetMeta("dotnetmap_version") ?? "0.3.0"
        };

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name, path, target_framework, is_test, file_hash FROM projects ORDER BY name;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                map.Projects.Add(new ProjectNode
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Path = reader.GetString(2),
                    TargetFramework = reader.IsDBNull(3) ? null : reader.GetString(3),
                    IsTest = reader.GetInt32(4) != 0,
                    FileHash = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }
        }

        foreach (var project in map.Projects)
        {
            if (includeFiles)
            {
                using var fcmd = _connection.CreateCommand();
                fcmd.CommandText = """
                    SELECT id, relative_path, absolute_path, content_hash, length_chars
                    FROM source_files WHERE project_id = $pid ORDER BY relative_path;
                    """;
                fcmd.Parameters.AddWithValue("$pid", project.Id);
                using var fr = fcmd.ExecuteReader();
                while (fr.Read())
                {
                    project.Files.Add(new SourceFileNode
                    {
                        Id = fr.GetString(0),
                        RelativePath = fr.GetString(1),
                        AbsolutePath = fr.GetString(2),
                        ContentHash = fr.GetString(3),
                        LengthChars = fr.GetInt32(4)
                    });
                }
            }

            using (var ncmd = _connection.CreateCommand())
            {
                ncmd.CommandText = "SELECT id, name FROM namespaces WHERE project_id = $pid ORDER BY name;";
                ncmd.Parameters.AddWithValue("$pid", project.Id);
                using var nr = ncmd.ExecuteReader();
                while (nr.Read())
                {
                    project.Namespaces.Add(new NamespaceNode
                    {
                        Id = nr.GetString(0),
                        Name = nr.GetString(1)
                    });
                }
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, namespace_id, name, full_name, kind, accessibility,
                       is_static, is_abstract, is_sealed, summary,
                       file_id, start_line, end_line, start_offset, end_offset, size_chars,
                       dependencies_json, consumers_json, token_estimate,
                       locations_json
                FROM types
                WHERE project_id = $pid
                ORDER BY full_name;
                """;
            cmd.Parameters.AddWithValue("$pid", project.Id);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var kindStr = reader.GetString(4);
                var kind = Enum.TryParse<TypeKind>(kindStr, ignoreCase: true, out var k) ? k : TypeKind.Class;
                var span = new SourceSpan(
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetInt32(11),
                    reader.IsDBNull(12) ? null : reader.GetInt32(12),
                    reader.IsDBNull(13) ? null : reader.GetInt32(13),
                    reader.IsDBNull(14) ? null : reader.GetInt32(14),
                    reader.GetInt32(15));
                var type = new TypeNode
                {
                    Id = reader.GetString(0),
                    NamespaceId = reader.GetString(1),
                    Name = reader.GetString(2),
                    FullName = reader.GetString(3),
                    Kind = kind,
                    Accessibility = reader.GetString(5),
                    IsStatic = reader.GetInt32(6) != 0,
                    IsAbstract = reader.GetInt32(7) != 0,
                    IsSealed = reader.GetInt32(8) != 0,
                    Summary = reader.IsDBNull(9) ? null : reader.GetString(9),
                    Span = span,
                    TokenEstimate = reader.GetInt32(18)
                };

                TryLoadRelations(reader.IsDBNull(16) ? "[]" : reader.GetString(16), type.Dependencies);
                TryLoadRelations(reader.IsDBNull(17) ? "[]" : reader.GetString(17), type.Consumers);
                type.Locations.AddRange(ParseLocations(
                    reader.FieldCount > 19 && !reader.IsDBNull(19) ? reader.GetString(19) : "[]",
                    span));

                if (includeMembers)
                    LoadMembers(type);

                project.Types.Add(type);
            }
        }

        return map;
    }

    private void LoadMembers(TypeNode type)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, kind, signature, accessibility, is_static, is_abstract, is_async,
                   return_type, summary, file_id, start_line, end_line, start_offset, end_offset,
                   size_chars, dependencies_json, consumers_json, token_estimate
            FROM members WHERE type_id = $tid ORDER BY kind, name;
            """;
        cmd.Parameters.AddWithValue("$tid", type.Id);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var kindStr = reader.GetString(2);
            var kind = Enum.TryParse<MemberKind>(kindStr, ignoreCase: true, out var k) ? k : MemberKind.Method;
            var member = new MemberNode
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Kind = kind,
                Signature = reader.GetString(3),
                Accessibility = reader.GetString(4),
                IsStatic = reader.GetInt32(5) != 0,
                IsAbstract = reader.GetInt32(6) != 0,
                IsAsync = reader.GetInt32(7) != 0,
                ReturnType = reader.IsDBNull(8) ? null : reader.GetString(8),
                Summary = reader.IsDBNull(9) ? null : reader.GetString(9),
                Span = new SourceSpan(
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetInt32(11),
                    reader.IsDBNull(12) ? null : reader.GetInt32(12),
                    reader.IsDBNull(13) ? null : reader.GetInt32(13),
                    reader.IsDBNull(14) ? null : reader.GetInt32(14),
                    reader.GetInt32(15)),
                TokenEstimate = reader.GetInt32(18)
            };
            TryLoadRelations(reader.IsDBNull(16) ? "[]" : reader.GetString(16), member.Dependencies);
            TryLoadRelations(reader.IsDBNull(17) ? "[]" : reader.GetString(17), member.Consumers);
            type.Members.Add(member);
        }
    }

    private void TryLoadRelations(string json, List<RelationRef> target)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<RelationRef>>(json, JsonOptions);
            if (list is not null)
                target.AddRange(list);
        }
        catch
        {
            // ignore corrupt json
        }
    }

    public void SaveTypeConsumers(IEnumerable<TypeNode> types, IndexMode? mode = null)
    {
        using var tx = _connection.BeginTransaction();
        foreach (var type in types)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE types
                SET consumers_json = $json
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", type.Id);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(type.Consumers, JsonOptions));
            cmd.ExecuteNonQuery();

            // Replace inbound consumer edges for this type (DNM-014)
            DeleteEdgesForOwner(type.Id, consumersOnly: true);
            foreach (var c in type.Consumers)
                InsertEdge(c.TargetId, type.Id, KindToEdgeString(c.Kind), c.File, c.Line);
        }

        if (mode is not null)
            SetMeta("index_mode", mode == IndexMode.FullRelations ? "full-relations"
                : mode == IndexMode.Structure ? "structure" : "structure+light-deps");

        SetMeta("indexed_at_utc", DateTimeOffset.UtcNow.UtcDateTime.ToString("o"));
        SetMeta("schema_version", CurrentSchemaVersion.ToString());
        RefreshEdgeCountMeta();
        tx.Commit();
    }


    public IReadOnlyList<TypeSummaryRow> ListTypes(int max = 500)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT t.full_name, t.kind, t.accessibility, t.summary, t.token_estimate,
                   (SELECT COUNT(*) FROM members m WHERE m.type_id = t.id) AS member_count
            FROM types t
            ORDER BY t.full_name
            LIMIT $max;
            """;
        cmd.Parameters.AddWithValue("$max", max);
        var list = new List<TypeSummaryRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TypeSummaryRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5)));
        }
        return list;
    }

    public IReadOnlyList<MemberSummaryRow> ListMembersForType(string fullName, int max = 100)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT m.name, m.kind, m.signature, m.summary, m.token_estimate
            FROM members m
            INNER JOIN types t ON t.id = m.type_id
            WHERE t.full_name = $fn
            ORDER BY m.kind, m.name
            LIMIT $max;
            """;
        cmd.Parameters.AddWithValue("$fn", fullName);
        cmd.Parameters.AddWithValue("$max", max);
        var list = new List<MemberSummaryRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new MemberSummaryRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4)));
        }
        return list;
    }

    /// <summary>
    /// Search types and/or members via FTS5, with LIKE fallback.
    /// When <paramref name="body"/> is true, searches source body_fts only (DNM-013).
    /// </summary>
    /// <param name="kind">type | member | all (ignored when body=true)</param>
    public IReadOnlyList<SearchHit> Search(string text, string kind = "all", int max = 20, bool body = false)
    {
        kind = kind.ToLowerInvariant();
        if (max < 1) max = 1;
        if (max > 200) max = 200;

        if (body)
            return SearchBodyFts(text, max);

        var hits = new List<SearchHit>();
        var match = FtsQuery.ToMatchExpression(text);
        var like = FtsQuery.ToLikePattern(text);

        if (kind is "all" or "type")
            hits.AddRange(SearchTypesFts(match, like, max));

        if (kind is "all" or "member")
        {
            var remaining = Math.Max(1, max - (kind == "all" ? hits.Count : 0));
            if (kind == "member")
                remaining = max;
            hits.AddRange(SearchMembersFts(match, like, remaining));
        }

        return hits
            .OrderBy(h => h.Rank ?? 0)
            .ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    /// <summary>True when last index ran with <c>--index-body</c> and body_fts has rows.</summary>
    public bool HasBodyIndex()
    {
        if (GetMeta("index_body") == "1")
            return true;
        try
        {
            return ScalarInt("SELECT COUNT(*) FROM body_fts") > 0;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    public TypeDetail? GetTypeDetail(string nameOrId, int maxMembers = 200)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.full_name, t.kind, t.accessibility, t.summary,
                   t.start_line, t.end_line, t.size_chars,
                   t.dependencies_json, t.consumers_json, t.token_estimate,
                   f.relative_path, t.locations_json, t.file_id
            FROM types t
            LEFT JOIN source_files f ON f.id = t.file_id
            WHERE t.full_name = $q
               OR t.id = $q
               OR t.id = $typePrefixed
               OR t.name = $q
            ORDER BY
              CASE
                WHEN t.full_name = $q THEN 0
                WHEN t.id = $q OR t.id = $typePrefixed THEN 1
                ELSE 2
              END,
              t.full_name
            LIMIT 1;
            """;
        var q = nameOrId.Trim();
        cmd.Parameters.AddWithValue("$q", q);
        cmd.Parameters.AddWithValue("$typePrefixed", q.StartsWith("type:", StringComparison.Ordinal) ? q : "type:" + q);

        string typeId;
        string fullName;
        string kind;
        string accessibility;
        string? summary;
        int? startLine;
        int? endLine;
        int sizeChars;
        string depsJson;
        string consJson;
        int tokenEstimate;
        string? relativePath;
        string locationsJson;
        string? fileId;

        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read())
                return null;

            typeId = reader.GetString(0);
            fullName = reader.GetString(1);
            kind = reader.GetString(2);
            accessibility = reader.GetString(3);
            summary = reader.IsDBNull(4) ? null : reader.GetString(4);
            startLine = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            endLine = reader.IsDBNull(6) ? null : reader.GetInt32(6);
            sizeChars = reader.GetInt32(7);
            depsJson = reader.GetString(8);
            consJson = reader.GetString(9);
            tokenEstimate = reader.GetInt32(10);
            relativePath = reader.IsDBNull(11) ? null : reader.GetString(11);
            locationsJson = reader.FieldCount > 12 && !reader.IsDBNull(12) ? reader.GetString(12) : "[]";
            fileId = reader.FieldCount > 13 && !reader.IsDBNull(13) ? reader.GetString(13) : null;
        }

        var span = new SourceSpan(fileId, startLine, endLine, null, null, sizeChars);
        var locations = ParseLocations(locationsJson, span, relativePath);

        return new TypeDetail(
            typeId,
            fullName,
            kind,
            accessibility,
            summary,
            startLine,
            endLine,
            sizeChars,
            relativePath,
            depsJson,
            consJson,
            tokenEstimate,
            ListMemberDetails(typeId, maxMembers),
            locations);
    }

    private IReadOnlyList<MemberDetail> ListMemberDetails(string typeId, int max)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT m.id, m.name, m.kind, m.signature, m.accessibility, m.return_type, m.summary,
                   m.start_line, m.end_line, m.size_chars, m.dependencies_json, m.consumers_json,
                   m.token_estimate, f.relative_path, t.full_name
            FROM members m
            INNER JOIN types t ON t.id = m.type_id
            LEFT JOIN source_files f ON f.id = m.file_id
            WHERE m.type_id = $tid
            ORDER BY m.kind, m.name
            LIMIT $max;
            """;
        cmd.Parameters.AddWithValue("$tid", typeId);
        cmd.Parameters.AddWithValue("$max", max);
        return ReadMemberDetails(cmd);
    }

    /// <summary>
    /// Resolve a method/property/field by id, signature, Name, Type.Name, or method:Id.
    /// </summary>
    public MemberDetail? GetMemberDetail(string nameOrId)
    {
        var q = nameOrId.Trim();
        if (q.StartsWith("method:", StringComparison.OrdinalIgnoreCase)
            || q.StartsWith("property:", StringComparison.OrdinalIgnoreCase)
            || q.StartsWith("field:", StringComparison.OrdinalIgnoreCase)
            || q.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
        {
            // exact id path
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT m.id, m.name, m.kind, m.signature, m.accessibility, m.return_type, m.summary,
                   m.start_line, m.end_line, m.size_chars, m.dependencies_json, m.consumers_json,
                   m.token_estimate, f.relative_path, t.full_name
            FROM members m
            INNER JOIN types t ON t.id = m.type_id
            LEFT JOIN source_files f ON f.id = m.file_id
            WHERE m.id = $q
               OR m.id = $methodPrefixed
               OR m.signature = $q
               OR m.name = $q
               OR (t.full_name || '.' || m.name) = $q
               OR (t.name || '.' || m.name) = $q
               OR m.signature LIKE $like
            ORDER BY
              CASE
                WHEN m.id = $q OR m.id = $methodPrefixed THEN 0
                WHEN m.signature = $q THEN 1
                WHEN (t.full_name || '.' || m.name) = $q THEN 2
                WHEN m.name = $q THEN 3
                ELSE 4
              END,
              t.full_name, m.name
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$q", q);
        // Prefer exact id for method:/property:/field:/event:
        var prefixed = q switch
        {
            _ when q.StartsWith("method:", StringComparison.OrdinalIgnoreCase) => q,
            _ when q.StartsWith("property:", StringComparison.OrdinalIgnoreCase) => q,
            _ when q.StartsWith("field:", StringComparison.OrdinalIgnoreCase) => q,
            _ when q.StartsWith("event:", StringComparison.OrdinalIgnoreCase) => q,
            _ => "method:" + q
        };
        cmd.Parameters.AddWithValue("$methodPrefixed", prefixed);
        cmd.Parameters.AddWithValue("$like", "%" + q + "%");

        var list = ReadMemberDetails(cmd);
        if (list.Count > 0)
            return list[0];

        // Retry with property:/field: prefixes when bare name
        if (!q.Contains(':', StringComparison.Ordinal))
        {
            foreach (var prefix in new[] { "property:", "field:", "event:" })
            {
                using var cmd2 = _connection.CreateCommand();
                cmd2.CommandText = """
                    SELECT m.id, m.name, m.kind, m.signature, m.accessibility, m.return_type, m.summary,
                           m.start_line, m.end_line, m.size_chars, m.dependencies_json, m.consumers_json,
                           m.token_estimate, f.relative_path, t.full_name
                    FROM members m
                    INNER JOIN types t ON t.id = m.type_id
                    LEFT JOIN source_files f ON f.id = m.file_id
                    WHERE m.id = $id OR (t.name || '.' || m.name) = $q OR (t.full_name || '.' || m.name) = $q
                    ORDER BY t.full_name LIMIT 1;
                    """;
                cmd2.Parameters.AddWithValue("$id", prefix + q);
                cmd2.Parameters.AddWithValue("$q", q);
                var list2 = ReadMemberDetails(cmd2);
                if (list2.Count > 0)
                    return list2[0];
            }
        }

        return null;
    }

    public void SaveMemberConsumers(string memberId, IReadOnlyList<RelationRef> consumers)
    {
        using var tx = _connection.BeginTransaction();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                UPDATE members
                SET consumers_json = $json
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", memberId);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(consumers, JsonOptions));
            cmd.ExecuteNonQuery();
        }

        DeleteEdgesForOwner(memberId, consumersOnly: true);
        foreach (var c in consumers)
            InsertEdge(c.TargetId, memberId, KindToEdgeString(c.Kind), c.File, c.Line);

        SetMeta("indexed_at_utc", DateTimeOffset.UtcNow.UtcDateTime.ToString("o"));
        SetMeta("schema_version", CurrentSchemaVersion.ToString());
        RefreshEdgeCountMeta();
        tx.Commit();
    }

    private static List<MemberDetail> ReadMemberDetails(SqliteCommand cmd)
    {
        var list = new List<MemberDetail>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new MemberDetail(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? "[]" : reader.GetString(11),
                reader.GetInt32(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14)));
        }
        return list;
    }

    private bool TryInsertBodyFts(SourceFileNode file)
    {
        if (string.IsNullOrEmpty(file.AbsolutePath) || !File.Exists(file.AbsolutePath))
            return false;

        try
        {
            var info = new FileInfo(file.AbsolutePath);
            if (info.Length > MaxBodyFileChars * 2) // rough byte gate before read
                return false;

            var text = File.ReadAllText(file.AbsolutePath);
            if (text.Length == 0)
                return false;
            if (text.Length > MaxBodyFileChars)
                text = text[..MaxBodyFileChars];

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO body_fts (file_id, relative_path, absolute_path, content)
                VALUES ($id, $rel, $abs, $content);
                """;
            cmd.Parameters.AddWithValue("$id", file.Id);
            cmd.Parameters.AddWithValue("$rel", file.RelativePath);
            cmd.Parameters.AddWithValue("$abs", file.AbsolutePath);
            cmd.Parameters.AddWithValue("$content", text);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private List<SearchHit> SearchBodyFts(string text, int max)
    {
        var list = new List<SearchHit>();
        var match = FtsQuery.ToMatchExpression(text);
        var tokens = FtsQuery.ExtractTokens(text);

        // Only body_fts — no disk scrape. Callers must index with --index-body first.
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT file_id, relative_path, content, bm25(body_fts) AS rank
                FROM body_fts
                WHERE body_fts MATCH $m
                ORDER BY rank
                LIMIT $max;
                """;
            cmd.Parameters.AddWithValue("$m", match);
            cmd.Parameters.AddWithValue("$max", max);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var fileId = reader.GetString(0);
                var rel = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var content = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var rank = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3);
                var (line, snippet) = FtsQuery.FindFirstMatchLine(content, tokens);
                list.Add(new SearchHit(
                    "body",
                    fileId,
                    Path.GetFileName(rel.Replace('\\', '/')),
                    rel,
                    snippet,
                    null,
                    rank,
                    RelativePath: rel,
                    Line: line,
                    Snippet: snippet));
            }
        }
        catch (SqliteException)
        {
            // body_fts missing
        }

        return list;
    }

    private List<SearchHit> SearchTypesFts(string match, string like, int max)
    {
        var list = new List<SearchHit>();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT f.type_id, f.full_name, f.name, f.summary, bm25(types_fts) AS rank
                FROM types_fts f
                WHERE types_fts MATCH $m
                ORDER BY rank
                LIMIT $max;
                """;
            cmd.Parameters.AddWithValue("$m", match);
            cmd.Parameters.AddWithValue("$max", max);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new SearchHit(
                    "type",
                    reader.GetString(0),
                    reader.GetString(2),
                    reader.GetString(1),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    null,
                    reader.IsDBNull(4) ? null : reader.GetDouble(4)));
            }
        }
        catch (SqliteException)
        {
            // fall through to LIKE
        }

        if (list.Count > 0)
            return list;

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, full_name, name, summary
                FROM types
                WHERE full_name LIKE $like OR name LIKE $like OR IFNULL(summary,'') LIKE $like
                ORDER BY full_name
                LIMIT $max;
                """;
            cmd.Parameters.AddWithValue("$like", like);
            cmd.Parameters.AddWithValue("$max", max);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new SearchHit(
                    "type",
                    reader.GetString(0),
                    reader.GetString(2),
                    reader.GetString(1),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    null,
                    null));
            }
        }

        return list;
    }

    private List<SearchHit> SearchMembersFts(string match, string like, int max)
    {
        var list = new List<SearchHit>();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT f.member_id, f.name, f.signature, f.summary, f.type_full_name, bm25(members_fts) AS rank
                FROM members_fts f
                WHERE members_fts MATCH $m
                ORDER BY rank
                LIMIT $max;
                """;
            cmd.Parameters.AddWithValue("$m", match);
            cmd.Parameters.AddWithValue("$max", max);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new SearchHit(
                    "member",
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5)));
            }
        }
        catch (SqliteException)
        {
            // fall through
        }

        if (list.Count > 0)
            return list;

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT m.id, m.name, m.signature, m.summary, t.full_name
                FROM members m
                INNER JOIN types t ON t.id = m.type_id
                WHERE m.name LIKE $like OR m.signature LIKE $like OR IFNULL(m.summary,'') LIKE $like
                ORDER BY t.full_name, m.name
                LIMIT $max;
                """;
            cmd.Parameters.AddWithValue("$like", like);
            cmd.Parameters.AddWithValue("$max", max);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new SearchHit(
                    "member",
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    null));
            }
        }

        return list;
    }

    private void InsertSolution(SolutionMap map)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO solutions (id, name, path, file_hash)
            VALUES ($id, $name, $path, $hash);
            """;
        cmd.Parameters.AddWithValue("$id", map.Id);
        cmd.Parameters.AddWithValue("$name", map.Name);
        cmd.Parameters.AddWithValue("$path", map.Path);
        cmd.Parameters.AddWithValue("$hash", (object?)map.FileHash ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private void InsertProject(string solutionId, ProjectNode project)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO projects (id, solution_id, name, path, target_framework, is_test, file_hash)
            VALUES ($id, $sid, $name, $path, $tfm, $test, $hash);
            """;
        cmd.Parameters.AddWithValue("$id", project.Id);
        cmd.Parameters.AddWithValue("$sid", solutionId);
        cmd.Parameters.AddWithValue("$name", project.Name);
        cmd.Parameters.AddWithValue("$path", project.Path);
        cmd.Parameters.AddWithValue("$tfm", (object?)project.TargetFramework ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$test", project.IsTest ? 1 : 0);
        cmd.Parameters.AddWithValue("$hash", (object?)project.FileHash ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private void InsertFile(string projectId, SourceFileNode file)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO source_files (id, project_id, relative_path, absolute_path, content_hash, length_chars)
            VALUES ($id, $pid, $rel, $abs, $hash, $len);
            """;
        cmd.Parameters.AddWithValue("$id", file.Id);
        cmd.Parameters.AddWithValue("$pid", projectId);
        cmd.Parameters.AddWithValue("$rel", file.RelativePath);
        cmd.Parameters.AddWithValue("$abs", file.AbsolutePath);
        cmd.Parameters.AddWithValue("$hash", file.ContentHash);
        cmd.Parameters.AddWithValue("$len", file.LengthChars);
        cmd.ExecuteNonQuery();
    }

    private void InsertNamespace(string projectId, NamespaceNode ns)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO namespaces (id, project_id, name)
            VALUES ($id, $pid, $name);
            """;
        cmd.Parameters.AddWithValue("$id", ns.Id);
        cmd.Parameters.AddWithValue("$pid", projectId);
        cmd.Parameters.AddWithValue("$name", ns.Name);
        cmd.ExecuteNonQuery();
    }

    private void InsertType(string projectId, TypeNode type)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO types (
                id, project_id, namespace_id, name, full_name, kind, accessibility,
                is_static, is_abstract, is_sealed, summary,
                file_id, start_line, end_line, start_offset, end_offset, size_chars,
                dependencies_json, consumers_json, token_estimate, locations_json)
            VALUES (
                $id, $pid, $nid, $name, $full, $kind, $acc,
                $st, $ab, $se, $sum,
                $fid, $sl, $el, $so, $eo, $sz,
                $deps, $cons, $tok, $locs);
            """;
        cmd.Parameters.AddWithValue("$id", type.Id);
        cmd.Parameters.AddWithValue("$pid", projectId);
        cmd.Parameters.AddWithValue("$nid", type.NamespaceId);
        cmd.Parameters.AddWithValue("$name", type.Name);
        cmd.Parameters.AddWithValue("$full", type.FullName);
        cmd.Parameters.AddWithValue("$kind", type.Kind.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$acc", type.Accessibility);
        cmd.Parameters.AddWithValue("$st", type.IsStatic ? 1 : 0);
        cmd.Parameters.AddWithValue("$ab", type.IsAbstract ? 1 : 0);
        cmd.Parameters.AddWithValue("$se", type.IsSealed ? 1 : 0);
        cmd.Parameters.AddWithValue("$sum", (object?)type.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fid", (object?)type.Span.FileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sl", (object?)type.Span.StartLine ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$el", (object?)type.Span.EndLine ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$so", (object?)type.Span.StartOffset ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$eo", (object?)type.Span.EndOffset ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sz", type.Span.SizeChars);
        cmd.Parameters.AddWithValue("$deps", SerializeRelations(type.Dependencies));
        cmd.Parameters.AddWithValue("$cons", SerializeRelations(type.Consumers));
        cmd.Parameters.AddWithValue("$tok", type.TokenEstimate);
        cmd.Parameters.AddWithValue("$locs", SerializeLocations(type));
        cmd.ExecuteNonQuery();

        using var fts = _connection.CreateCommand();
        fts.CommandText = """
            INSERT INTO types_fts (type_id, full_name, name, summary)
            VALUES ($id, $full, $name, $sum);
            """;
        fts.Parameters.AddWithValue("$id", type.Id);
        fts.Parameters.AddWithValue("$full", type.FullName);
        fts.Parameters.AddWithValue("$name", type.Name);
        fts.Parameters.AddWithValue("$sum", type.Summary ?? "");
        fts.ExecuteNonQuery();
    }

    private void InsertMember(string typeId, string typeFullName, MemberNode member)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO members (
                id, type_id, name, kind, signature, accessibility,
                is_static, is_abstract, is_async, return_type, summary,
                file_id, start_line, end_line, start_offset, end_offset, size_chars,
                dependencies_json, consumers_json, token_estimate)
            VALUES (
                $id, $tid, $name, $kind, $sig, $acc,
                $st, $ab, $as, $ret, $sum,
                $fid, $sl, $el, $so, $eo, $sz,
                $deps, $cons, $tok);
            """;
        cmd.Parameters.AddWithValue("$id", member.Id);
        cmd.Parameters.AddWithValue("$tid", typeId);
        cmd.Parameters.AddWithValue("$name", member.Name);
        cmd.Parameters.AddWithValue("$kind", member.Kind.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$sig", member.Signature);
        cmd.Parameters.AddWithValue("$acc", member.Accessibility);
        cmd.Parameters.AddWithValue("$st", member.IsStatic ? 1 : 0);
        cmd.Parameters.AddWithValue("$ab", member.IsAbstract ? 1 : 0);
        cmd.Parameters.AddWithValue("$as", member.IsAsync ? 1 : 0);
        cmd.Parameters.AddWithValue("$ret", (object?)member.ReturnType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sum", (object?)member.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fid", (object?)member.Span.FileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sl", (object?)member.Span.StartLine ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$el", (object?)member.Span.EndLine ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$so", (object?)member.Span.StartOffset ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$eo", (object?)member.Span.EndOffset ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sz", member.Span.SizeChars);
        cmd.Parameters.AddWithValue("$deps", SerializeRelations(member.Dependencies));
        cmd.Parameters.AddWithValue("$cons", SerializeRelations(member.Consumers));
        cmd.Parameters.AddWithValue("$tok", member.TokenEstimate);
        cmd.ExecuteNonQuery();

        using var fts = _connection.CreateCommand();
        fts.CommandText = """
            INSERT INTO members_fts (member_id, type_full_name, name, signature, summary)
            VALUES ($id, $tfn, $name, $sig, $sum);
            """;
        fts.Parameters.AddWithValue("$id", member.Id);
        fts.Parameters.AddWithValue("$tfn", typeFullName);
        fts.Parameters.AddWithValue("$name", member.Name);
        fts.Parameters.AddWithValue("$sig", member.Signature);
        fts.Parameters.AddWithValue("$sum", member.Summary ?? "");
        fts.ExecuteNonQuery();
    }

    private static string SerializeRelations(IReadOnlyList<RelationRef> relations) =>
        JsonSerializer.Serialize(relations, JsonOptions);

    private static string SerializeLocations(TypeNode type)
    {
        var locs = type.Locations;
        if (locs.Count == 0 && type.Span.FileId is not null)
        {
            locs =
            [
                new DeclarationLocation(
                    type.Span.FileId,
                    null,
                    type.Span.StartLine,
                    type.Span.EndLine,
                    type.Span.SizeChars,
                    IsPrimary: true)
            ];
        }

        return JsonSerializer.Serialize(locs, JsonOptions);
    }

    private static List<DeclarationLocation> ParseLocations(
        string? json,
        SourceSpan primarySpan,
        string? primaryRelativePath = null)
    {
        List<DeclarationLocation>? list = null;
        if (!string.IsNullOrWhiteSpace(json) && json is not "[]")
        {
            try
            {
                list = JsonSerializer.Deserialize<List<DeclarationLocation>>(json, JsonOptions);
            }
            catch
            {
                list = null;
            }
        }

        if (list is { Count: > 0 })
            return list;

        if (primarySpan.FileId is null && primarySpan.StartLine is null)
            return [];

        return
        [
            new DeclarationLocation(
                primarySpan.FileId,
                primaryRelativePath,
                primarySpan.StartLine,
                primarySpan.EndLine,
                primarySpan.SizeChars,
                IsPrimary: true)
        ];
    }

    /// <summary>
    /// Insert dependency edges (owner→target) and consumer edges (consumer→owner).
    /// Returns number of rows inserted.
    /// </summary>
    private int InsertRelationEdges(
        string ownerId,
        IReadOnlyList<RelationRef> dependencies,
        IReadOnlyList<RelationRef> consumers)
    {
        var n = 0;
        foreach (var d in dependencies)
        {
            InsertEdge(ownerId, d.TargetId, KindToEdgeString(d.Kind), d.File, d.Line);
            n++;
        }

        foreach (var c in consumers)
        {
            // Consumer JSON stores the consumer as Target*; edge direction is consumer → owner.
            InsertEdge(c.TargetId, ownerId, KindToEdgeString(c.Kind), c.File, c.Line);
            n++;
        }

        return n;
    }

    private void InsertEdge(string fromId, string toId, string kind, string? file, int? line)
    {
        if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId) || string.IsNullOrEmpty(kind))
            return;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO edges (from_id, to_id, kind, file, line)
            VALUES ($from, $to, $kind, $file, $line);
            """;
        cmd.Parameters.AddWithValue("$from", fromId);
        cmd.Parameters.AddWithValue("$to", toId);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$file", (object?)file ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$line", line is int l ? l : DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private void DeleteEdgesForOwner(string ownerId, bool consumersOnly)
    {
        using var cmd = _connection.CreateCommand();
        if (consumersOnly)
        {
            // Inbound consumer edges: to_id = owner, kind = referencedBy
            cmd.CommandText = """
                DELETE FROM edges
                WHERE to_id = $id AND kind = 'referencedBy';
                """;
        }
        else
        {
            cmd.CommandText = """
                DELETE FROM edges
                WHERE from_id = $id OR to_id = $id;
                """;
        }

        cmd.Parameters.AddWithValue("$id", ownerId);
        cmd.ExecuteNonQuery();
    }

    private void RefreshEdgeCountMeta()
    {
        try
        {
            SetMeta("edge_count", ScalarInt("SELECT COUNT(*) FROM edges").ToString());
        }
        catch (SqliteException)
        {
            SetMeta("edge_count", "0");
        }
    }

    private List<EdgeRow> QueryEdges(string sql, string id, int max)
    {
        var list = new List<EdgeRow>();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$max", max);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new EdgeRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4)));
            }
        }
        catch (SqliteException)
        {
            // table missing
        }

        return list;
    }

    /// <summary>
    /// When opening a v0 DB that has relation JSON but empty edges, backfill edges once.
    /// Full reindex is still preferred for brand-new indexes (always writes schema_version=1).
    /// </summary>
    private void TryMigrateEdgesFromJson()
    {
        try
        {
            if (!HasSolutionData())
                return;

            var edgeCount = ScalarInt("SELECT COUNT(*) FROM edges");
            if (edgeCount > 0)
            {
                // Already materialised
                if (GetMeta("schema_version") is "0" or null)
                    SetMeta("schema_version", CurrentSchemaVersion.ToString());
                return;
            }

            // Any relation JSON present?
            var withJson = ScalarInt("""
                SELECT COUNT(*) FROM (
                  SELECT 1 FROM types WHERE dependencies_json != '[]' OR consumers_json != '[]'
                  UNION ALL
                  SELECT 1 FROM members WHERE dependencies_json != '[]' OR consumers_json != '[]'
                ) x
                """);
            if (withJson == 0)
            {
                // Empty relations — still mark as v1-capable schema
                if (GetMeta("schema_version") is "0" or null)
                    SetMeta("schema_version", CurrentSchemaVersion.ToString());
                SetMeta("edge_count", "0");
                return;
            }

            // Materialize rows first (SQLite: no nested commands while reader is open).
            var pending = new List<(string Id, List<RelationRef> Deps, List<RelationRef> Cons)>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT id, dependencies_json, consumers_json FROM types;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    pending.Add((
                        reader.GetString(0),
                        RelationPresentationParse(reader.IsDBNull(1) ? "[]" : reader.GetString(1)),
                        RelationPresentationParse(reader.IsDBNull(2) ? "[]" : reader.GetString(2))));
                }
            }

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT id, dependencies_json, consumers_json FROM members;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    pending.Add((
                        reader.GetString(0),
                        RelationPresentationParse(reader.IsDBNull(1) ? "[]" : reader.GetString(1)),
                        RelationPresentationParse(reader.IsDBNull(2) ? "[]" : reader.GetString(2))));
                }
            }

            using var tx = _connection.BeginTransaction();
            var inserted = 0;
            foreach (var (id, deps, cons) in pending)
                inserted += InsertRelationEdges(id, deps, cons);

            SetMeta("schema_version", CurrentSchemaVersion.ToString());
            SetMeta("edge_count", inserted.ToString());
            SetMeta("edges_migrated_from_json", "1");
            tx.Commit();
        }
        catch (SqliteException)
        {
            // edges table not ready / empty db
        }
    }

    private static List<RelationRef> RelationPresentationParse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<RelationRef>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string KindToEdgeString(RelationKind kind) => kind switch
    {
        RelationKind.Inherits => "inherits",
        RelationKind.Implements => "implements",
        RelationKind.UsesInSignature => "usesInSignature",
        RelationKind.UsesInMember => "usesInMember",
        RelationKind.Calls => "calls",
        RelationKind.ReferencedBy => "referencedBy",
        _ => kind.ToString()
    };

    private string? GetMeta(string key)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private void SetMeta(string key, string value)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO meta (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private int ScalarInt(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        var result = cmd.ExecuteScalar();
        return result is long l ? (int)l : Convert.ToInt32(result);
    }

    /// <summary>Public scalar COUNT helper for analysis modules.</summary>
    public int ScalarCount(string sql) => ScalarInt(sql);

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string ModeToString(IndexMode mode) => mode switch
    {
        IndexMode.Structure => "structure",
        IndexMode.StructureLightDeps => "structure+light-deps",
        IndexMode.FullRelations => "full-relations",
        _ => mode.ToString()
    };

    public void Dispose() => _connection.Dispose();
}
