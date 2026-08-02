# S9 — Docs system + M1–M5 closed; product curation is next

Date: 2026-08-02

> Temp pickup. Consume against live git/gates; delete when the next wave’s follow-up ships.
> Not a second ROADMAP. Policy lives in `AGENTS.md` + ADRs.

## First principle

Verify every claim below before acting: `git log --oneline -8`, `git status`, six gates, and
`pwsh -File experiments/harness-invocation/selftest.ps1 -SkipLab`.

## What just shipped (this multi-session wave)

### A. Documentation claim lifecycle (commits through ~`212fd76` and earlier doc commits)

- **Normative split:** `AGENTS.md` always-on; ADR **Decision** = intent; code = current behavior;
  Consequences are **not** law. Mismatch → narrow Decision or ROADMAP gap same change.
- **ADR bar (Matt):** hard to reverse × surprising × trade-off; thin Consequences.
- **Homes:** `curation/SCHEMA.md` (authoring); `docs/research/harness-adapters.md` (adapter guide);
  `experiments/harness-invocation/` (protocol, runbook, selftest, records schema); ROADMAP = ops only.
- **Handovers:** track → consume → delete; template forbids HEAD/current-state homes.
- **s1–s8 handovers deleted** after promote-or-discard log (no unique durable loss — criterion 15).
- Planning scratch `docs/superpowers/` is **gone** (untracked plans deleted; do not recreate unless
  a new plan wave needs it).

### B. M1–M5 linker/build honesty (`41c6733`…`dc8bb7e`, CI green)

| Gap | Fix |
|---|---|
| M5 | `loadManifest` throws on bad `invocation` / `as` / `body` (incl. null/non-string) |
| M3 | `collectIdentityProblems`: dup `plugin.name`, same-plugin `${outType}:${outName}`; ledger id `plugin/outType/outName` |
| M2 | `overlayDrift` live keys = eject `stampFiles` set (bidirectional) |
| M1 | refs skip after `[A-Z]` or `_`; unknown-ns **warn** once/ns (exact-address allowlist for prose noise) |
| M4 | ledger `claude.flags` booleans from emitted skill frontmatter |

**Skill trees:** `plugins/` and `opencode/` **byte-stable** through M-series (only `docs/ledger.json`
changed among build outputs). Freshness: `npm run build` then staged diff of plugins/opencode must
be empty.

### C. CI

- `actions/checkout@v7`, `actions/setup-node@v7`, Node 24 + `check-latest`
- Biome excludes `experiments/**/fixtures` (lab byte-identical blobs)

## Live state to assume until verified

- **Branch:** `master`, clean vs `origin/master` after push of this handover (if any).
- **HEAD tip family:** M-series ends `dc8bb7e` (style) / product tip may move with this commit.
- **Gates:** `npm test` (~134), typecheck, lint, format, build, validate **0 errors**.
- **Validate warnings (4):**
  1. **NEW (M1):** `elements-of-style` unknown namespace — `plugins/deniz-process/skills/brainstorming/SKILL.md`
  2–4. Standing converted-command / parked SKILL.md links (teach, writing-great-skills, subagent-driven-development)
- **Curation product:** `deniz-process` **closed**. Three dotnet plugins = one starter each.
- **Do not install** `deniz-dotnet-aspire` until router repair (ROADMAP #2–3).

## Pitfalls earned this wave

1. **“Verified” without a run is a lie** — design/plan claimed verified; only build/selftest/mutation caught real defects.
2. **Consequences is the rot sink** — post-decision findings must go research/SCHEMA/code/ROADMAP, not ADR essays.
3. **M1 error-on-unknown-ns is wrong on day one** — CSS/`file:line`/CamelCase noise; warn + allowlist + boundary first.
4. **`invocation: both` ≠ two ledger rows** — one skill outType; cross-kind collision needs two items.
5. **Eject stamp discriminator is file existence of `overlay.patch`**, not `item.body`.
6. **Experiment fixtures must stay out of Biome** or baseline hashes break.
7. **Manual Kimi agents** need user `@kimi-max-go` / `@kimi-max-moonshotai`; task deny otherwise.
8. **Handovers are not SoT** — this file dies when follow-up ships.

## Recommended next (curator chooses; do not silent-pick)

**Option A — Product:** ROADMAP Next Up **#1** — one dotnet module curation session (`inventory` first).

**Option B — Aspire safety:** ROADMAP **#2** router repair before any aspire install/public talk.

**Option C — Hygiene:** clear or allowlist `elements-of-style` warn; optional repo-wide path CI (Known Gap).

**Option D — Measurement:** ROADMAP **#9** intent-fire probe on existing lab (cheap).

## Commands to run (next session)

```powershell
git status --short
git log --oneline -8
npm test
npm run typecheck
npm run lint
npm run format:check
npm run build
npm run validate
pwsh -File experiments/harness-invocation/selftest.ps1 -SkipLab
```

Optional freshness proof:

```powershell
npm run build
git add -A -- plugins opencode .claude-plugin docs/inventory.md docs/ledger.json
git diff --cached --exit-code -- plugins opencode .claude-plugin docs/inventory.md docs/ledger.json
git restore --staged plugins opencode .claude-plugin docs/inventory.md docs/ledger.json
```

## Deltas vs `docs/ROADMAP.md`

- **Current State:** note validate is **0 errors / 4 warnings** (one unknown-ns); M1–M5 tooling gaps **closed** (removed from Known Gaps already).
- **Next Up #1–6, #8–9:** still the product backlog — no silent reorder.
- **Known Gaps:** remaining items start at repo-wide path CI, long-body manual paste, etc. — not M1–M5.
- After the chosen option ships: **delete this handover file** in the same change.

## Locked policy recap (pointers only)

- Hard Rules + purpose: `AGENTS.md`
- Overlay/merge: ADR-0001 + `curation/SCHEMA.md`
- Transformation axes: ADR-0006; taste: ADR-0007; linker: ADR-0008
- Experiments: `experiments/harness-invocation/README.md` + `protocol.md`
- Adapters: `docs/research/harness-adapters.md`

## Priming one-liner for the human

Infra and docs contract are done; skill **content** curation for dotnet modules is the product path.
Ask before changing curation manifests. Never hand-edit `plugins/` or `opencode/`.
