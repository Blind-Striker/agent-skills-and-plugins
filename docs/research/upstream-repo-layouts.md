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

- Skills nest one level deeper than the standard layout: `skills/<category>/<name>`, with lifecycle
  categories `engineering`, `productivity`, `misc`, `personal` — plus `in-progress` and
  `deprecated`, which upstream flags as not ready and which make up roughly a third of the repo's
  components. Curate from the ready categories.

## dotnet-agent-skills (github.com/dotnet/skills)

- A marketplace monorepo of ~15 plugins whose per-plugin manifests are **bare**
  `plugins/<name>/plugin.json`, not `.claude-plugin/plugin.json` — the scanner probes both shapes
  at every level; without that, most of its components would collapse into one namespace.
- Some scanned components are repo infrastructure, not product skills: `.agents/skills/*`
  (authoring skills), `.github/skills` and `.github/agents`, and
  `eng/skill-validator/tests/fixtures/*` — the last are literally upstream's test fixtures. They
  appear in `docs/inventory.md` looking like ordinary skills; do not curate them by accident.

## superpowers, dotnet-skills

- Standard layouts; no surprises recorded.
