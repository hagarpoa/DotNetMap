-- NetMap SQLite schema v0
-- Local-first index for AI context. Relations stored as JSON in MVP.
-- Apply on empty database. schema_version in meta must be 0.

PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;

-- ---------------------------------------------------------------------------
-- Meta / index status
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY NOT NULL,
    value TEXT NOT NULL
);

-- Expected keys (written by store, not enforced by SQL):
--   schema_version          = '0'
--   solution_path           = absolute path indexed
--   solution_name           = display name
--   indexed_at_utc          = ISO-8601
--   index_mode              = 'structure' | 'structure+light-deps' | 'full-relations'
--   include_private         = '0' | '1'
--   include_test            = '0' | '1'
--   netmap_version          = tool semver
--   token_estimate_overview = integer as text

-- ---------------------------------------------------------------------------
-- Hierarchy
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS solutions (
    id           TEXT PRIMARY KEY NOT NULL, -- solution:{normalized path or name}
    name         TEXT NOT NULL,
    path         TEXT NOT NULL UNIQUE,
    file_hash    TEXT NULL              -- hash of .sln/.slnx content
);

CREATE TABLE IF NOT EXISTS projects (
    id             TEXT PRIMARY KEY NOT NULL, -- project:{assembly or unique name}
    solution_id    TEXT NOT NULL REFERENCES solutions(id) ON DELETE CASCADE,
    name           TEXT NOT NULL,
    path           TEXT NOT NULL,
    target_framework TEXT NULL,
    is_test        INTEGER NOT NULL DEFAULT 0,
    file_hash      TEXT NULL,                 -- aggregate or csproj hash
    UNIQUE (solution_id, path)
);

CREATE TABLE IF NOT EXISTS source_files (
    id           TEXT PRIMARY KEY NOT NULL, -- file:{projectId}:{relative path}
    project_id   TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    relative_path TEXT NOT NULL,
    absolute_path TEXT NOT NULL,
    content_hash TEXT NOT NULL,             -- SHA-256 hex
    length_chars INTEGER NOT NULL DEFAULT 0,
    UNIQUE (project_id, relative_path)
);

CREATE TABLE IF NOT EXISTS namespaces (
    id           TEXT PRIMARY KEY NOT NULL, -- ns:{projectId}:{name} or ns:global
    project_id   TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    name         TEXT NOT NULL,             -- empty string = global
    UNIQUE (project_id, name)
);

-- Types: class, record, struct, interface, enum, delegate
CREATE TABLE IF NOT EXISTS types (
    id              TEXT PRIMARY KEY NOT NULL, -- type:{fq metadata name}
    project_id      TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    namespace_id    TEXT NOT NULL REFERENCES namespaces(id) ON DELETE CASCADE,
    name            TEXT NOT NULL,
    full_name       TEXT NOT NULL,             -- Namespace.Name
    kind            TEXT NOT NULL,             -- class|record|struct|interface|enum|delegate
    accessibility   TEXT NOT NULL,             -- public|internal|private|protected|...
    is_static       INTEGER NOT NULL DEFAULT 0,
    is_abstract     INTEGER NOT NULL DEFAULT 0,
    is_sealed       INTEGER NOT NULL DEFAULT 0,
    summary         TEXT NULL,
    -- Source location (primary partial declaration in MVP)
    file_id         TEXT NULL REFERENCES source_files(id) ON DELETE SET NULL,
    start_line      INTEGER NULL,
    end_line        INTEGER NULL,
    start_offset    INTEGER NULL,
    end_offset      INTEGER NULL,
    size_chars      INTEGER NOT NULL DEFAULT 0,
    -- Relations (JSON arrays; see docs/DECISIONS.md)
    dependencies_json TEXT NOT NULL DEFAULT '[]',
    consumers_json    TEXT NOT NULL DEFAULT '[]', -- only when Phase B ran for this type
    token_estimate    INTEGER NOT NULL DEFAULT 0,
    UNIQUE (project_id, full_name)
);

CREATE TABLE IF NOT EXISTS members (
    id              TEXT PRIMARY KEY NOT NULL, -- method:|property:|field:|event:{stable id}
    type_id         TEXT NOT NULL REFERENCES types(id) ON DELETE CASCADE,
    name            TEXT NOT NULL,
    kind            TEXT NOT NULL,             -- method|property|field|event|constructor
    signature       TEXT NOT NULL,             -- display signature
    accessibility   TEXT NOT NULL,
    is_static       INTEGER NOT NULL DEFAULT 0,
    is_abstract     INTEGER NOT NULL DEFAULT 0,
    is_async        INTEGER NOT NULL DEFAULT 0,
    return_type     TEXT NULL,
    summary         TEXT NULL,
    file_id         TEXT NULL REFERENCES source_files(id) ON DELETE SET NULL,
    start_line      INTEGER NULL,
    end_line        INTEGER NULL,
    start_offset    INTEGER NULL,
    end_offset      INTEGER NULL,
    size_chars      INTEGER NOT NULL DEFAULT 0,
    dependencies_json TEXT NOT NULL DEFAULT '[]',
    consumers_json    TEXT NOT NULL DEFAULT '[]',
    token_estimate    INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS ix_projects_solution ON projects(solution_id);
CREATE INDEX IF NOT EXISTS ix_files_project ON source_files(project_id);
CREATE INDEX IF NOT EXISTS ix_files_hash ON source_files(content_hash);
CREATE INDEX IF NOT EXISTS ix_namespaces_project ON namespaces(project_id);
CREATE INDEX IF NOT EXISTS ix_types_project ON types(project_id);
CREATE INDEX IF NOT EXISTS ix_types_full_name ON types(full_name);
CREATE INDEX IF NOT EXISTS ix_types_namespace ON types(namespace_id);
CREATE INDEX IF NOT EXISTS ix_members_type ON members(type_id);
CREATE INDEX IF NOT EXISTS ix_members_name ON members(name);

-- ---------------------------------------------------------------------------
-- Full-text search (name + summary). Content tables kept in sync by store.
-- ---------------------------------------------------------------------------
CREATE VIRTUAL TABLE IF NOT EXISTS types_fts USING fts5(
    type_id UNINDEXED,
    full_name,
    name,
    summary,
    tokenize = 'porter unicode61'
);

CREATE VIRTUAL TABLE IF NOT EXISTS members_fts USING fts5(
    member_id UNINDEXED,
    type_full_name,
    name,
    signature,
    summary,
    tokenize = 'porter unicode61'
);
