---
name: code-search
description: >-
  Codebase discovery priority for all agents: (1) mcp-codebase-search MCP, (2) graphify if
  available, (3) default tools (Grep/Glob/Read). Use before claiming code exists, before
  adapt stack detection, and when grilling/domain-modeling cross-checks the repo.
disable-model-invocation: false
---

# Code Search — discovery priority

**Always follow this order** when exploring a repo, answering "does X exist?", stack detection,
or cross-checking user claims against code.

| Priority | Source | When to use |
|----------|--------|-------------|
| **1** | **[mcp-codebase-search](https://github.com/teknologika/mcp-codebase-search/)** MCP | MCP server `codebase-search` available in the host |
| **2** | **graphify** | MCP unavailable or insufficient; `graphify` CLI/skill on PATH |
| **3** | **Default tools** | Grep, Glob, Read, shell `find`/`rg` — last resort |

State **DEGRADED MODE** in chat when priority 1 and 2 are unavailable before making
structural claims about the codebase.

---

## Priority 1 — mcp-codebase-search (preferred)

Local semantic search via MCP ([`@teknologika/mcp-codebase-search`](https://github.com/teknologika/mcp-codebase-search/)).

### Setup (once per machine / repo)

```bash
npm install -g @teknologika/mcp-codebase-search
mcp-codebase-ingest --path . --name <project-name>
```

Cursor MCP config (`.cursor/mcp.json` or user settings):

```json
{
  "mcpServers": {
    "codebase-search": {
      "command": "mcp-codebase-search",
      "args": []
    }
  }
}
```

Set `pipeline.manifest.json` → `code_search.codebase_name` to the ingest name.

### MCP tools (use via CallMcpTool when host exposes them)

| Tool | Use |
|------|-----|
| `list_codebases` | Verify index exists; check `lastScanAge` |
| `update_codebase_scan` | Refresh after edits if `staleWarning` or scan age > 10 min |
| `search_codebases` | Semantic search — primary discovery |
| `get_chunk_content` / `get_file_content` | Read matched code (paths, not pastes in chat) |
| `get_adjacent_chunks` | Context around a partial chunk |
| `list_files` | Stack detection — extensions, layout, test vs src |
| `get_codebase_stats` | Language distribution for pack matching |

### Workflow

1. `list_codebases` — confirm project is indexed.
2. If stale → `update_codebase_scan` with manifest `code_search.codebase_name`.
3. `search_codebases` with descriptive queries (not bare identifiers).
4. Cite **file paths + line ranges** in chat; use `get_chunk_content` for detail.

### Dedupe Ticket — hard rule (no creation without one)

**codebase-search is the *only valid source* for claims about what already exists** during a
session. Before adding **any** new file, module, class, function, or helper, produce a
**Dedupe Ticket**:

- **Intent signature** — one sentence describing what you are about to add
- **Queries** — 2–4 `search_codebases` calls you will run
- **Top matches** — up to 5 file paths returned by the tool
- **Decision** — `reuse | extend | new`
- **Rationale** — why

Skipping the ticket to create new code is a protocol violation. If the MCP server is
unavailable, state **DEGRADED MODE** and stop before making changes.

---

## Priority 2 — graphify (fallback)

When MCP codebase search is unavailable or did not answer (e.g. no index yet):

```bash
graphify . --no-viz            # adapt: first run
graphify . --update --no-viz   # re-adapt
```

Read `graphify-out/GRAPH_REPORT.md` and `.graphify_labels.json` if present.

Graphify is strong for **stack communities** and dependency graphs; prefer MCP for
**"where is X implemented?"** during implementation and grilling.

---

## Priority 3 — default tools

Only when MCP and graphify are unavailable:

- **Glob** — find files by name pattern
- **Grep** — exact symbol/string matches
- **Read** — file contents after narrowing

Avoid repo-wide blind grep when MCP or graphify can answer semantically.

---

## Adapt / stack-selector

During `.agents adapt` (after `PROJECT.md` intake):

1. **mcp-codebase-search** — `list_files` + `get_codebase_stats` + targeted
   `search_codebases` (e.g. "entry point", "database migrations", "SwiftUI app").
2. **graphify** — if on PATH and repo has code; read `graphify-out/`.
3. **Filesystem globs** — `*.csproj`, `pubspec.yaml`, `Package.swift`, etc.
4. **`PROJECT.md` Stack** — primary on greenfield.

Record `adapt.code_search_source` in recommendation: `mcp-codebase-search` | `graphify` | `filesystem` | `project-brief`.

---

## Grilling & domain-modeling

When a grill question is answerable from code:

1. Run **code-search** priority chain (MCP first).
2. Then ask the human only if still ambiguous.

`domain-modeling` cross-reference: use MCP `search_codebases` before claiming code contradicts the glossary.

---

## Constraints

- Cite paths; do not dump large files into chat.
- Call `update_codebase_scan` after significant local edits before relying on search results.
- MCP runs locally — no code leaves the machine.
- Graphify and MCP complement each other; **MCP wins for search**, graphify for graph-level stack signals when MCP is missing.
