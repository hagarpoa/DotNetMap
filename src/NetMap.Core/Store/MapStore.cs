using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using NetMap.Core.Domain;

namespace NetMap.Core.Store;

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

    public void EnsureSchema()
    {
        var sql = SchemaLoader.LoadV0();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();

        if (GetMeta("schema_version") is null)
            SetMeta("schema_version", "0");
    }

    public void WriteMap(SolutionMap map)
    {
        TokenEstimator.EstimateOverview(map);

        using var tx = _connection.BeginTransaction();

        // Full rewrite for MVP simplicity (incremental reindex per project comes in PR-6).
        Execute("""
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

        foreach (var project in map.Projects)
        {
            InsertProject(map.Id, project);
            foreach (var file in project.Files)
                InsertFile(project.Id, file);
            foreach (var ns in project.Namespaces)
                InsertNamespace(project.Id, ns);
            foreach (var type in project.Types)
            {
                TokenEstimator.EstimateType(type);
                InsertType(project.Id, type);
                foreach (var member in type.Members)
                {
                    TokenEstimator.EstimateMember(member);
                    InsertMember(type.Id, type.FullName, member);
                }
            }
        }

        SetMeta("schema_version", "0");
        SetMeta("solution_path", map.Path);
        SetMeta("solution_name", map.Name);
        SetMeta("indexed_at_utc", map.IndexedAtUtc.UtcDateTime.ToString("o"));
        SetMeta("index_mode", ModeToString(map.Mode));
        SetMeta("include_private", map.IncludePrivate ? "1" : "0");
        SetMeta("include_test", map.IncludeTest ? "1" : "0");
        SetMeta("netmap_version", map.NetMapVersion);
        SetMeta("token_estimate_overview", TokenEstimator.EstimateOverview(map).ToString());

        tx.Commit();
    }

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

        return new IndexStatus
        {
            SchemaVersion = schema,
            SolutionPath = GetMeta("solution_path"),
            SolutionName = GetMeta("solution_name"),
            IndexedAtUtc = indexedAt,
            IndexMode = GetMeta("index_mode"),
            IncludePrivate = GetMeta("include_private") == "1",
            IncludeTest = GetMeta("include_test") == "1",
            NetMapVersion = GetMeta("netmap_version"),
            ProjectCount = ScalarInt("SELECT COUNT(*) FROM projects"),
            TypeCount = ScalarInt("SELECT COUNT(*) FROM types"),
            MemberCount = ScalarInt("SELECT COUNT(*) FROM members"),
            FileCount = ScalarInt("SELECT COUNT(*) FROM source_files"),
            DatabaseBytes = dbBytes,
            TokenEstimateOverview = tokenEst
        };
    }

    public bool HasSolutionData() =>
        !string.IsNullOrEmpty(GetMeta("solution_path"));

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
            NetMapVersion = GetMeta("netmap_version") ?? "0.1.0"
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
                       dependencies_json, consumers_json, token_estimate
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
                    Span = new SourceSpan(
                        reader.IsDBNull(10) ? null : reader.GetString(10),
                        reader.IsDBNull(11) ? null : reader.GetInt32(11),
                        reader.IsDBNull(12) ? null : reader.GetInt32(12),
                        reader.IsDBNull(13) ? null : reader.GetInt32(13),
                        reader.IsDBNull(14) ? null : reader.GetInt32(14),
                        reader.GetInt32(15)),
                    TokenEstimate = reader.GetInt32(18)
                };

                TryLoadRelations(reader.IsDBNull(16) ? "[]" : reader.GetString(16), type.Dependencies);
                TryLoadRelations(reader.IsDBNull(17) ? "[]" : reader.GetString(17), type.Consumers);

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
        }

        if (mode is not null)
            SetMeta("index_mode", mode == IndexMode.FullRelations ? "full-relations"
                : mode == IndexMode.Structure ? "structure" : "structure+light-deps");

        SetMeta("indexed_at_utc", DateTimeOffset.UtcNow.UtcDateTime.ToString("o"));
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
    /// </summary>
    /// <param name="kind">type | member | all</param>
    public IReadOnlyList<SearchHit> Search(string text, string kind = "all", int max = 20)
    {
        kind = kind.ToLowerInvariant();
        if (max < 1) max = 1;
        if (max > 200) max = 200;

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

    public TypeDetail? GetTypeDetail(string nameOrId, int maxMembers = 200)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.full_name, t.kind, t.accessibility, t.summary,
                   t.start_line, t.end_line, t.size_chars,
                   t.dependencies_json, t.consumers_json, t.token_estimate,
                   f.relative_path
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
        }

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
            ListMemberDetails(typeId, maxMembers));
    }

    private IReadOnlyList<MemberDetail> ListMemberDetails(string typeId, int max)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, kind, signature, accessibility, return_type, summary,
                   start_line, end_line, size_chars, dependencies_json, token_estimate
            FROM members
            WHERE type_id = $tid
            ORDER BY kind, name
            LIMIT $max;
            """;
        cmd.Parameters.AddWithValue("$tid", typeId);
        cmd.Parameters.AddWithValue("$max", max);
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
                reader.GetInt32(11)));
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
                dependencies_json, consumers_json, token_estimate)
            VALUES (
                $id, $pid, $nid, $name, $full, $kind, $acc,
                $st, $ab, $se, $sum,
                $fid, $sl, $el, $so, $eo, $sz,
                $deps, $cons, $tok);
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
