# Upstream repo layouts

Date: 2026-07-30

What the five vendored repos actually look like on disk, where that deviates from the obvious, and
what it means for curation. `docs/inventory.md` lists the components; this records the traps.

## aspire-skills

- The canonical tree is `skills/<name>`. `.github/plugins/aspire-skills/` is a **symlink mirror** of
  it (per-file mode-120000 entries inside real directories) — every product skill exists twice.
  The scanner skips symlinks; curation `source:` values must use the canonical
  `aspire-skills/skills/<name>` form.
- On a checkout without symlink support (`core.symlinks=false` — Windows without Developer
  Mode/admin), git materializes those symlinks as plain text files containing the target path: the
  mirror resurfaces as empty-description duplicate components, and a "plugin.json" that is really a
  path string is why the scanner guards against malformed manifests. Linux/macOS CI is unaffected.
- `.github/skills/pr-review` is that repo's own PR-review workflow, not a product skill.

## mattpocock-skills

- Skills nest one level deeper than the standard layout: `skills/<category>/<name>`. Upstream's own
  `CLAUDE.md` calls `engineering` and `productivity` the **promoted** buckets and ships exactly
  those in its plugin; `misc`, `personal`, `in-progress` and `deprecated` are excluded by policy.
  Treat the promoted pair as the candidate set — the rest is drafts, his own setup, and retired
  work.
- `ask-matt` is that repo's **router** over its own skills. Two frameworks routing at once fight
  over names and pull in conflicting workflows, and in this repo the router is the user; do not
  curate it.
- Several engineering skills are wired to upstream's own process rather than to a codebase:
  `setup-matt-pocock-skills`, `triage` and `setup-ts-deep-modules` assume his issue tracker, label
  vocabulary and repo conventions.
- `ubiquitous-language` sits in `deprecated` even though its subject (DDD glossary work) is live —
  the active discipline moved into `domain-modeling`. Check what replaced a deprecated skill before
  concluding the capability is gone.

## dotnet-agent-skills (github.com/dotnet/skills)

- A marketplace monorepo of ~15 plugins whose per-plugin manifests are **bare**
  `plugins/<name>/plugin.json`, not `.claude-plugin/plugin.json` — the scanner probes both shapes
  at every level; without that, most of its components would collapse into one namespace.
- Some scanned components are repo infrastructure, not product skills: `.agents/skills/*`
  (authoring skills), `.github/skills` and `.github/agents`, and
  `eng/skill-validator/tests/fixtures/*` — the last are literally upstream's test fixtures. They
  appear in `docs/inventory.md` looking like ordinary skills; do not curate them by accident.

## superpowers

- Standard layout, but the skills are **densely cross-referenced**, which couples curation
  decisions: skipping a skill leaves a dangling `superpowers:<name>` reference in the body of every
  skill that points at it, and that is a body edit, not a manifest one. Regenerate the graph with:

  ```
  for d in external/superpowers/skills/*/; do
    printf '%-32s %s\n' "$(basename $d)" \
      "$(grep -oh 'superpowers:[a-z-]*' -r "$d" | sed 's/superpowers://' | sort -u | tr '\n' ',')"
  done
  ```

  As pinned today it forms three groups. **Free to take** — reference nothing, referenced by
  nothing but `using-superpowers`: `brainstorming`, `dispatching-parallel-agents`,
  `receiving-code-review`. **Sinks** — referenced but reference nothing, so skipping one breaks its
  referrers while taking it drags nothing along: `using-git-worktrees` (3 referrers),
  `finishing-a-development-branch` (2), `requesting-code-review` (2),
  `verification-before-completion` (1). **Coupled** — `writing-plans` → `executing-plans` →
  `subagent-driven-development` pulls in six skills transitively, and
  `systematic-debugging` ↔ `test-driven-development` ↔ `writing-skills` is a cycle.

- Three skills bundle executable scripts, so each needs the `git update-index --chmod=+x` treatment
  on its built copies: `brainstorming` (`scripts/helper.js`, `start-server.sh`, `stop-server.sh` —
  the browser companion), `systematic-debugging` (`find-polluter.sh`), `writing-skills`
  (`render-graphs.js`).

- `systematic-debugging` ships author-facing material alongside the skill — `CREATION-LOG.md` and
  three `test-pressure-*.md` files — which travel into output unless excluded per item.

## dotnet-skills

- Standard layout; no surprises recorded.
