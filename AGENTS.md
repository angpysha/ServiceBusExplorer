# Agent Instructions

This project uses **bd** (beads) for issue tracking. Run `bd onboard` to get started.

## Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work atomically
bd close <id>         # Complete work
bd dolt push          # Push beads data to remote
```

## Non-Interactive Shell Commands

**ALWAYS use non-interactive flags** with file operations to avoid hanging on confirmation prompts.

Shell commands like `cp`, `mv`, and `rm` may be aliased to include `-i` (interactive) mode on some systems, causing the agent to hang indefinitely waiting for y/n input.

**Use these forms instead:**
```bash
# Force overwrite without prompting
cp -f source dest           # NOT: cp source dest
mv -f source dest           # NOT: mv source dest
rm -f file                  # NOT: rm file

# For recursive operations
rm -rf directory            # NOT: rm -r directory
cp -rf source dest          # NOT: cp -r source dest
```

**Other commands that may prompt:**
- `scp` - use `-o BatchMode=yes` for non-interactive
- `ssh` - use `-o BatchMode=yes` to fail instead of prompting
- `apt-get` - use `-y` flag
- `brew` - use `HOMEBREW_NO_AUTO_UPDATE=1` env var

<!-- BEGIN BEADS INTEGRATION v:1 profile:minimal hash:ca08a54f -->
## Beads Issue Tracker

This project uses **bd (beads)** for issue tracking. Run `bd prime` to see full workflow context and commands.

### Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work
bd close <id>         # Complete work
```

### Rules

- Use `bd` for ALL task tracking — do NOT use TodoWrite, TaskCreate, or markdown TODO lists
- Run `bd prime` for detailed command reference and session close protocol
- Use `bd remember` for persistent knowledge — do NOT use MEMORY.md files

## Session Completion

**When ending a work session**, you MUST complete ALL steps below. Work is NOT complete until `git push` succeeds.

**MANDATORY WORKFLOW:**

1. **File issues for remaining work** - Create issues for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **PUSH TO REMOTE** - This is MANDATORY:
   ```bash
   git pull --rebase
   bd dolt push
   git push
   git status  # MUST show "up to date with origin"
   ```
5. **Clean up** - Clear stashes, prune remote branches
6. **Verify** - All changes committed AND pushed
7. **Hand off** - Provide context for next session

**CRITICAL RULES:**
- Work is NOT complete until `git push` succeeds
- NEVER stop before pushing - that leaves work stranded locally
- NEVER say "ready to push when you are" - YOU must push
- If push fails, resolve and retry until it succeeds
<!-- END BEADS INTEGRATION -->

<!-- agentic-pipeline:begin -->
# Agent index

Universal agent-orchestrated SDLC pipeline for this repository.

## Bootstrap

If `pipeline.manifest.json` shows `"status": "needs_adapt"`:

```text
Run .agents adapt
```

Adapt skill: [.agents/SKILL.md](.agents/SKILL.md)

**Greenfield / empty repo:** adapt runs **project-intake** first — ensures `PROJECT.md`
(Vision + Stack) exists before recommending packs and agents.

## Spec-Driven Development (Spec Kit)

This pipeline runs on **[GitHub Spec Kit](https://github.github.com/spec-kit/)**. Every phase
drives the matching `/speckit.*` command; `spec.md`/`plan.md`/`tasks.md` are canonical.
Install once and initialize per repo:

```text
uv tool install specify-cli --from git+https://github.com/github/spec-kit.git
specify init . --integration cursor        # then /speckit.constitution from PROJECT.md
```

All agents follow `rules/spec-kit.mdc`. Playbook: `skills/spec-kit/SKILL.md`. If Spec Kit is
unavailable, the pipeline runs in **SPEC-KIT DEGRADED** mode (hand-written SDLC docs, same gates).

## After adapt (`status: ready`)

```text
Run SDLC for: {your feature}
```

Coordinator playbook: `.cursor/skills/sdlc-orchestrator/SKILL.md` (or `.agents/skills/sdlc-orchestrator/SKILL.md`)

## Specialist agents

| Agent | Role |
|-------|------|
| coordinator | Hub — routing, gates, checkpoints, closeout |
| ba-analyst | Business grilling + requirements |
| architect | SDD, TDD, ADR |
| team-lead | Planning + TL code review |
| developer | Implementation (one task at a time) |
| tester | Adversarial testing + report |
| security-reviewer | Auth/token/storage review (conditional) |
| devops | Infra planning + IaC (conditional) |
| tech-writer | Docs sync (Phase 9) |
| stack-selector | Stack detection during adapt only |

Contract: [pipeline.manifest.json](pipeline.manifest.json)

## Catalog refresh (keep skills/agents/rules current)

After any `agentic-tool` upgrade, refresh from the package catalog:

```bash
agentic-tool apply --target .
agentic-tool sync --target . --host auto
agentic-tool verify --target .
```

`verify` warns when `catalog_version` in the manifest is behind the tool. Rule:
`rules/catalog-refresh.mdc`. Coordinator stops SDLC if the catalog is stale.

## Codebase Dedupe Protocol

### Goal

Prevent duplicate implementations and "wrong file" edits by making **codebase-search** the
*only valid source* for claims about what already exists in this repo during this session.

### Tools you MUST use for codebase discovery

- `list_codebases`
- `search_codebases`
- `get_codebase_stats`
- `get_chunk_content`
- `get_file_content`
- `get_adjacent_chunks`
- `list_files`
- `update_codebase_scan`

After edits run `update_codebase_scan` to refresh the index.

### Hard rule: No creation without a Dedupe Ticket

Before adding a new file, module, class, function, or helper, produce a **Dedupe Ticket**:

- **Intent signature** — one sentence describing what you are about to add
- **Queries** — 2–4 searches you will run
- **Top matches** — up to 5 file paths returned by the tool
- **Decision** — `reuse | extend | new`
- **Rationale** — why

### Graceful degradation

If the MCP server is unavailable: state **DEGRADED MODE** and stop before making changes.
Full priority chain (MCP → graphify → default tools): `rules/code-search.mdc` +
`skills/code-search/SKILL.md`.
<!-- agentic-pipeline:end -->
