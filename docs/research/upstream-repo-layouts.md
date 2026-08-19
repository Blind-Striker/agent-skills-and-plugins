# Upstream repo layouts

Date: 2026-07-31

What the five vendored repos actually look like on disk, where that deviates from the obvious, and
what it means for curation. This is pin-relative layout evidence: `docs/inventory.md` lists the
components visible at the generated catalog's pins, while this note records the traps and the
commands that re-derived them.

> **Dated evidence and advisory interpretation, not current item policy.** Re-run the catalog and
> the commands below after a pin move. Current take/skip/modify posture and reasons live in
> [`curation/*.yaml`](../../curation/) and resolved output posture lives in
> [`docs/ledger.json`](../ledger.json). Current scanner, emission, and reference mechanics live in
> [Transformation and emission](../architecture/transformation-and-emission.md) and
> [References and linking](../architecture/references-and-linking.md).

## aspire-skills

- The canonical tree is `skills/<name>`. `.github/plugins/aspire-skills/` is a **symlink mirror** of
  it (per-file mode-120000 entries inside real directories) — every product skill exists twice.
  At this snapshot the scanner skipped symlinks, and using the canonical
  `aspire-skills/skills/<name>` source form avoided treating the mirror as a second product tree.
  Current `source:` values are owned by the curation manifests.
- On a checkout without symlink support (`core.symlinks=false` — Windows without Developer
  Mode/admin), git materializes those symlinks as plain text files containing the target path: the
  mirror resurfaces as empty-description duplicate components, and a "plugin.json" that is really a
  path string is why the scanner guards against malformed manifests. Linux/macOS CI is unaffected.
- `.github/skills/pr-review` is that repo's own PR-review workflow, not a product skill.

## mattpocock-skills

- Skills nest one level deeper than the standard layout: `skills/<category>/<name>`. Upstream's own
  `CLAUDE.md` called `engineering` and `productivity` the **promoted** buckets and shipped exactly
  those in its plugin; `misc`, `personal`, `in-progress` and `deprecated` are excluded by policy.
  The snapshot's curation heuristic treated the promoted pair as the candidate set and the rest as
  drafts, personal setup, or retired work; current item decisions remain in the manifest and ledger.
- `ask-matt` is that repo's **router** over its own skills. Two frameworks routing at once fight
  over names and pull in conflicting workflows. This evidence motivated its exclusion at the
  snapshot; the current exclusion and reason are owned by `curation/deniz-process.yaml`.
- Several engineering skills are wired to upstream's own process rather than to a codebase:
  `setup-matt-pocock-skills`, `triage` and `setup-ts-deep-modules` assume his issue tracker, label
  vocabulary and repo conventions.
- `ubiquitous-language` sat in `deprecated` even though its subject (DDD glossary work) was live —
  the active discipline had moved into `domain-modeling`. Check what replaced a deprecated skill
  before concluding the capability is gone.

## dotnet-agent-skills (github.com/dotnet/skills)

- A marketplace monorepo of ~15 plugins whose per-plugin manifests are **bare**
  `plugins/<name>/plugin.json`, not `.claude-plugin/plugin.json` — the scanner probes both shapes
  at every level; without that, most of its components would collapse into one namespace.
- Some scanned components are repo infrastructure, not product skills: `.agents/skills/*`
  (authoring skills), `.github/skills` and `.github/agents`, and
  `eng/skill-validator/tests/fixtures/*` — the last are literally upstream's test fixtures. They
  appeared in `docs/inventory.md` looking like ordinary skills. That is a candidate-classification
  trap, not evidence of current item posture.

## superpowers

- Standard layout, but the skills are **densely cross-referenced**, which couples curation
  decisions: skipping a skill leaves a dangling reference in the body of every skill that points at
  it, and that is a body edit, not a manifest one.

  At this snapshot the local linker classified the three spellings as follows. Current mechanics and
  proof limits are owned by [References and linking](../architecture/references-and-linking.md):

  | Spelling | Example | Seen by `validate`? |
  |---|---|---|
  | Namespaced | `superpowers:writing-plans` | yes — a fact: resolved, kind-checked, declared |
  | Relative path | `../using-superpowers/references/` | yes, where the snapshot build could have broken it |
  | Bare name | `invoke writing-plans skill` | **no** — a candidate, never build state |

  At the snapshot, the middle row was deliberately narrower than it looked. Resolving a path was
  deterministic, but whether a broken one was *our* fault was not: upstream bodies carried
  illustrative paths that never resolved anywhere. The linker therefore asked two questions instead
  of one — whether a `../<item>/` climb into a sibling item still landed, and whether a missing
  same-directory file was one upstream still shipped. Other upstream prose stayed silent (ADR-0008
  rationale: a warning in a green build is one nobody reads).

  A graph built from the namespaced spelling alone was therefore not the coupling graph — it was a
  lower bound. Regenerate the full one with:

  ```
  SKILLS=$(ls -d external/superpowers/skills/*/ | xargs -n1 basename)
  for d in external/superpowers/skills/*/; do
    b=$(basename "$d"); hits=""
    for s in $SKILLS; do
      [ "$s" = "$b" ] && continue
      grep -rqE "(^|[^a-z-])$s([^a-z-]|$)" "$d" && hits="$hits$s,"
    done
    printf '%-32s %s\n' "$b" "$hits"
  done
  ```

  It over-reports slightly — an edge can come from an example argument inside a bundled script
  rather than from content — so read the hit before letting it decide anything.

  At the snapshot pin the output formed three groups, treating `using-superpowers` as neither a
  candidate nor a referrer for the purpose of reading the *upstream* graph — upstream it is the
  bootstrap payload, while the local output at the snapshot packaged no hooks. The then-current
  curation made it a `manual` opt-in switch whose patched body kept two of upstream's seven outgoing
  references. Current invocation, body, and dependency posture must be read from
  `curation/deniz-process.yaml` and `docs/ledger.json`, not inferred from this upstream graph — the
  graph is the finder, never the record.
  **Free to take** — reference nothing, referenced by nothing: `dispatching-parallel-agents`,
  `receiving-code-review`. **Sinks** — referenced but reference nothing, so skipping one breaks its
  referrers while taking it drags nothing along: `using-git-worktrees`,
  `finishing-a-development-branch`, `verification-before-completion`, `requesting-code-review`.
  **Coupled** — everything else, in two clusters joined by `writing-skills`:
  `brainstorming` ↔ `writing-plans` ↔ `executing-plans` ↔ `subagent-driven-development` on the
  planning side, and `systematic-debugging` ↔ `test-driven-development` ↔ `writing-skills` on the
  authoring side.

  At that pin, `brainstorming` was the trap. It carried no namespaced reference at all, so it looked
  free, but it named `writing-plans` seven times in bare form — including a graphviz terminal node
  and "**The terminal state is invoking writing-plans.** … The ONLY skill you invoke after
  brainstorming is writing-plans." Taking it without `writing-plans` would ship a skill whose
  documented exit was a skill that was not there, and nothing in the toolchain would say so.

- At the snapshot pin, four skills bundled executable scripts, and each then needed the
  `git update-index --chmod=+x` treatment on its built copies to retain executable mode:
  `brainstorming` (`scripts/start-server.sh`, `scripts/stop-server.sh` — the browser companion),
  `subagent-driven-development` (`scripts/review-package`,
  `scripts/sdd-workspace`, `scripts/task-brief`), `systematic-debugging` (`find-polluter.sh`),
  `writing-skills` (`render-graphs.js`). Re-derive rather than trust the list:
  `git -C external/superpowers ls-files -s skills | grep 100755`.

- At the snapshot pin, `systematic-debugging` shipped author-facing material alongside the skill —
  `CREATION-LOG.md` and three `test-pressure-*.md` files — which traveled into output unless excluded
  per item.

## dotnet-skills

- Standard layout; no surprises recorded.
