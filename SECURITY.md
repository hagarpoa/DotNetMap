# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 1.0.x   | Yes |
| &lt; 1.0 | Best-effort |

## Product threat model

DotNetMap is a **local-first** CLI/MCP tool:

- Reads your solution source to build a SQLite index under a path you choose (default `.dotnetmap/index.db`).
- Does **not** phone home; no telemetry by default.
- MCP server speaks **stdio** to a local agent — treat the agent host as trusted.

## Path allowlist (snippets)

On-demand source snippets only read files under the **indexed solution root**. Path traversal (`..`) and absolute paths outside the root are rejected (DNM-030).

## Reporting a vulnerability

Please open a private security advisory on the GitHub repository, or contact the maintainers via the repository profile. Do not file public issues for exploitable path/RCE bugs until a fix is available.

## Hardening tips

- Run MCP only against databases you created.
- Prefer `detail=compact` and default caps when exposing tools to untrusted prompts.
- Re-index after pulling untrusted code before trusting the map.
