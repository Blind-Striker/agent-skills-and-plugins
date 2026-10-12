# Session Pickup — Description Shortening and verified-waves Polish

Date: 2026-10-12

> Consume this against live Git and current canon, then delete it when the Process polish and the
> description shortening ship. Machine and harness configuration (listing-budget values, MCP
> servers, environment variables, tool versions on the curator's machines) is out of scope here: the
> curator now manages it in a separate machine-configuration repository.

---

## Commands to run

1. `git status --short`
2. `git log --oneline -5`
3. `gh run list --limit 3`
4. `npm run build && npm run inventory && git status --short`
5. List the model-reachable descriptions over the Next Up 1 target:

   ```sh
   node --input-type=module -e '
   import { readFileSync, globSync } from "node:fs";
   import { parse } from "yaml";
   const rows = [];
   for (const f of globSync("plugins/*/{skills/*/SKILL.md,agents/*.md}")) {
     const m = /^---\r?\n([\s\S]*?)\r?\n---/.exec(readFileSync(f, "utf8")); if (!m) continue;
     const fm = parse(m[1]); if (fm["disable-model-invocation"] === true) continue;
     const d = String(fm.description ?? ""); if (d.length > 190) rows.push([d.length, f]);
   }
   rows.sort((a, b) => b[0] - a[0]);
   for (const [n, f] of rows) console.log(n, f);'
   ```

6. Read `experiments/harness-invocation/records/2026-10-11-skill-catalog-budgets.md`, then each
   listed item's `curation/*.yaml` entry and why-comment before drafting its description.
7. Read `skills/deniz-process/verified-waves/SKILL.md`, its three `references/*-tools.md` files, and
   `experiments/harness-invocation/runbook.md`.
8. `gh issue view 98 --repo Aaronontheweb/dotnet-skills --json state,comments`

## Deltas vs `docs/ROADMAP.md`

- `Next Up`: add a small item before item 1, the `verified-waves` text polish found by the
  `installer-v0.7.1` review, then remove it when it ships with its Process version move:
  - Codex notes: V1's `resume_agent` can reopen a thread from its rollout, so "cannot be reached"
    after a restart is too strong for V1; dispatching a new child stays the rule.
  - Claude Code notes: a sandbox-blocked network call shows as a DNS or "network unreachable"
    error, not only as the write-denial text the notes give.
  - OpenCode notes: the `question` tool also returns when the user dismisses it; say what the
    parent does then.
  - Codex notes: cover the approval policies beyond `on-request` and `never`.
  - Brief template in `SKILL.md`: "DO NOT ... ask the user" contradicts the instruction to request
    the harness's escalation; make the boundary explicit.
  - Runbook: the discovery method moves data, state, cache, and database roots but not OpenCode's
    temp-dir root.
- `Known Gaps`, `verified-waves` panel item: rewrap the line that runs past 100 characters
  ("and one of the two reused an earlier attempt's run-id ...").
- `Current State`, `dotnet-skills` bullet: add that `opentelemetry-net-instrumentation`'s exporter
  guard was reported upstream as Aaronontheweb/dotnet-skills#98, so the next sync of that source
  checks whether the patch can shrink; retiring it stays the curator's call.
