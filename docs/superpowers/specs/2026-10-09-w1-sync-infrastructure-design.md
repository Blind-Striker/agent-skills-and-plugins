# W1 Sync Infrastructure Design

Date: 2026-10-09

Transient specification for [`docs/ROADMAP.md`](../../ROADMAP.md#next-up) Next Up item 1 (W1). It
records the chosen design, the alternatives it rejected, and the evidence behind both. The durable
rules are already written into canon in the same change; this file is deleted when W1 closes
([documentation.md](../../engineering/documentation.md#handovers-and-plans)). The execution plan is
[`docs/superpowers/plans/2026-10-09-w1-sync-infrastructure.md`](../plans/2026-10-09-w1-sync-infrastructure.md).

## 1. Scope

W1 is tooling and canon only. It changes `tools/` and their tests and the canon named in §9. It does
not move a submodule pin, and it must leave every generated surface byte-identical: `plugins/`,
`opencode/`, `codex/`, `dist/`, `.claude-plugin/marketplace.json`,
`.agents/plugins/marketplace.json`, `docs/inventory.md`, and `docs/ledger.json`.

| ID | Item | Owner of the rule |
|---|---|---|
| X5 | `eject --patch --rebase`, and `--bless` checks that a patch still applies | `docs/engineering/workflow.md` |
| X7 | Sources deleted upstream leave the manifest; the unknown-source message says so | `docs/engineering/workflow.md`, `curation/SCHEMA.md` |
| X10 | The drift scan honours the recorded-absent stamp; one bless ceremony in every message | `docs/engineering/workflow.md`, `curation/SCHEMA.md` |
| W0-R4 | A name deleted upstream stays in the handoff and candidate universe | `docs/architecture/references-and-linking.md` |
| W0-R2 | A path claim followed by `,`, `;`, or `:` resolves | `docs/architecture/references-and-linking.md` |

Out of W1: X6 description validation (moves to W3 with the Aspire shortening decision), X4
interpreter gate (W4), X8 attribution (before `pr`), X9 sanity panel.

## 2. Evidence

All measurements were read-only. Sources: the session scratchpad (`upstream-review/00-overview.md`
§5, the per-upstream packets, `w1probe/`, `w1-rebase-proto/proto*.mjs`) and the Memorizer record
"measured 3-way replay of every overlay.patch onto upstream heads".

- **Base blobs exist.** Every `index <abbrev>..` line in the 81 patched files of the current
  `overlay.patch` set names a blob the submodule object database holds, and the old patch applies
  cleanly to it.
- **`git apply --3way` needs a repository.** It implies `--index` and reads the preimage blob. It
  works in a throwaway `git init` + `git add -A` copy of the new upstream item when
  `GIT_ALTERNATE_OBJECT_DIRECTORIES` holds the absolute result of
  `git -C <upstream item> rev-parse --path-format=absolute --git-path objects`. A relative
  alternates path silently disables the merge: git prints "repository lacks the necessary blob"
  and falls back to a direct apply. `--3way` and `--reject` cannot be combined.
- **3-way quality.** On all 81 files the `--3way` result was byte-identical to an explicit
  `git merge-file` with base = index blob, ours = upstream head, theirs = base + old patch.
- **Wave outcomes at the reviewed heads** (aspire `da1230c`, dotnet/skills `0608d892`, mattpocock
  `b0618bc`, superpowers `8ca22db`):
  - Aspire: `aspire-monitoring` and `aspire-orchestration` replay clean; conflicts only in
    `SKILL.md`: `aspire` 2, `aspire-deployment` 1 (a real 13.5.3 versus 13.5 conflict),
    `aspire-init` 2, `aspireify` 1. Every references file merges clean.
  - dotnet/skills: 15 of 20 patch items fail a direct apply; 7 replay clean; conflicts in
    `coverage-analysis` 1, `detect-static-dependencies` 1, `generate-testability-wrappers` 1,
    `incremental-build` 1, `migrate-static-to-wrapper` 1, `test-anti-patterns` 5,
    `test-gap-analysis` 3, `code-testing-agent` 2; `code-testing-agent` also patches
    `unit-test-generation.prompt.md`, which upstream deleted.
  - mattpocock: conflicts in `implement` 1, `improve-codebase-architecture` 2 (`SKILL.md` only),
    `triage` 1, `wait-what` 1, `wayfinder` 2. `setup-matt-pocock-skills` and `to-tickets` replay
    clean although the packet says their intent must change: a clean replay is textual only.
  - superpowers: `executing-plans` 1 and `using-superpowers` 1; `brainstorming` and
    `writing-skills` still apply directly.
- **Recut stability.** For every clean replay, a recut with today's algorithm (`-U8`, `--no-index`)
  kept the old `+`/`-` lines exactly; only context moved.
- **Markers ship without a guard.** A cut taken while conflict markers remain in the working copy is
  a valid patch that applies, so build and validate would accept it.
- **A deleted target defeats every apply mode** (probe, git 2.55): `--3way` says "does not exist in
  index", and `--reject` and `--check` say "No such file or directory"; none writes a `.rej`.
  Because a multi-file `git apply` is atomic, one such file aborts every other file in the patch.
- **Modes survive.** On Linux (WSL), a patch whose index line says `100644` applied to a `100755`
  file keeps `100755` both on a clean 3-way merge and on a conflicted one; git only warns "has type
  100755, expected 100644".
- **Root-item gitfile.** Copying a root item (the `asd-ste100` source is the submodule root) copies
  the submodule's `.git` gitfile; a git command run in such a copy acts on the real submodule.
- **Drift-scan premise.** The ROADMAP sentence "the drift scan reports a null-sentinel merge file as
  changed" came from the review's ad-hoc scan (scratchpad `drift.txt`), not from
  `driftedMergeSources`, which already honours a null stamp for an absent file. The in-repo defect is
  the Known Gap "Recorded-absent merge directories": a directory at a null-stamped name is reported
  as "appeared upstream", and a directory at a stamped name throws a raw `git hash-object` error.
- **Ledger history.** `git log --format= -p -U0 -- docs/ledger.json` takes about 0.13 s at
  `6a32ef0`. Its added top-level keys hold 126 distinct output names, 10 of which the current ledger
  lacks: `exp-mock-usage-analysis`, `exp-test-maintainability`, `find-untested-sources`,
  `grade-tests`, `migrate-dotnet8-to-dotnet9`, `migrate-dotnet9-to-dotnet10`,
  `migrate-vstest-to-mtp`, `migrate-xunit-to-xunit-v3`, `mjml-email-templates`,
  `writing-great-skills`. The CI `validate` job already checks out with `fetch-depth: 0`.

## 3. Two candidate designs and the graft

Two designs were reviewed: a minimal one (whole-patch `--3way` with a whole-patch `--reject`
fallback, an explicit `--rebase` flag, a mode-restore step, lock-derived bless names in `sync`) and a
robustness one (per-file replay, an isolated git environment, the `.git` filter, sync deferring to
build, an implicit replay on `--patch`). The chosen design takes:

| Decision | Chosen | Rejected and why |
|---|---|---|
| D1 Flag | Explicit `--patch --rebase` | Implicit replay when `--patch` meets an existing patch: it changes the meaning of a command the curator already uses, and the curator named `--rebase` |
| D2 Replay unit | One file at a time: `--check`, then `--3way`, then `--reject`, then the whole chunk to `<file>.rej` | Whole-patch `--3way`: one deleted target (`code-testing-agent`) aborts every file |
| D3 Old patch | Stays in place until the cut; the cut skips it, and writes the new patch only after it checks | Deleting it at replay time, or overwriting it before the check (today's cut deletes the file it just wrote when the check fails, which during a rebase would be the old patch) |
| D4 Git isolation | One environment for every git call in the overlay plumbing: `GIT_CONFIG_NOSYSTEM=1`, inherited `GIT_DIR`-family variables removed, `-c core.autocrlf=false -c core.eol=lf -c core.longpaths=true -c apply.whitespace=nowarn`, seed attributes `* -text`, a forced seed `add`, absolute alternates | Inheriting the caller's environment: a hook-launched eject would init or add into the real repository |
| D5 Modes | No mode code; a POSIX test pins that replay and cut keep `100755` | A chmod-restore step: §2 shows git keeps the mode |
| D6 Residue guard | Every cut refuses an added `*.rej` path and any added line that is a diff3 marker: seven `<`, `\|`, or `>`, then a space or the line end (`^\+(<{7}\|\|{7}\|>{7})( \|$)`) | Checking `=======` too: it is a Markdown setext underline |
| D7 Bless | `--bless` runs `checkPatch` first and refuses a non-applying patch even with `--yes` | Stamping a patch the build cannot apply (today) |
| D8 Merge sources | The rebase prints the same drift and diffs as `--bless`; the cut stamps the primary and every declared merge source | A replay mechanism for merge sources: a patch lands only on the primary |
| D9 Drift predicate | `fileStamp(path)`: blob SHA for a regular file, `null` for anything else; stamping and both drift scans use it | Separate `existsSync` checks per site (today) |
| D10 Hints | `ejectCommand(plugin, name, step)`; build names `--bless` or, for a patch that no longer applies, `--patch --rebase`, never `--yes`; `sync` names no eject step and defers to build | A lock lookup in `sync` to name the command: only build knows whether a patch applies |
| D11 Deleted source | One `UNKNOWN_SOURCE_RULE` sentence in validate, build, the assemble mirror, and sync | A manifest tombstone (`retired:` or an exclude): rejected by the approved X7 direction |
| D12 Name universe | Every output name any committed `docs/ledger.json` listed, read from Git history | HEAD's ledger or the manifest diff: transient, so the same tree gets a different verdict one commit later; a manifest list: an authored tombstone |
| D13 Path punctuation | Drop one trailing `.`, `,`, `;`, or `:` from a claim's path | Ending the path at `,`, `;`, `:`: also cuts `path:42` but truncates exotic names |

Full-file overlays get no replay: their review stays the bless diff and a hand port (a 3-way replay
against the lock stamp is possible, measured at `ask-deniz` 6 conflicts, but would need its own
marker guard because no cut follows). ADR-0001 rejected direct vendoring with three-way merges as the
ownership model; W1 uses a 3-way merge only to seed a working copy, and `overlay.patch` stays the
owned artifact, so ADR-0001 is unchanged.

## 4. X5: `eject --patch --rebase`

### Command contract

```text
npm run eject -- <plugin> <item> --patch --rebase   # replay, then resolve and review
npm run eject -- <plugin> <item> --patch            # cut
```

Refusals, each exit 1, nothing written:

| Condition | Message |
|---|---|
| `--rebase` without `--patch` | `--rebase reopens an overlay.patch: npm run eject -- <p> <n> --patch --rebase` |
| `--rebase` with `--force` | `--rebase keeps the old edits and --force discards them — pass one` |
| Item output is not a skill | the existing "applies to skill output only" refusal |
| No `overlay.patch`, and the item says `body: overlay` | `overlays/<p>/<n>/ is a full-file overlay — review upstream with: npm run eject -- <p> <n> --bless` |
| No `overlay.patch`, any other item | `Nothing to rebase: overlays/<p>/<n>/overlay.patch does not exist — start a patch with: npm run eject -- <p> <n> --patch` |
| A working copy already exists | `overlays/<p>/<n>/ already holds a working copy — cut it with: npm run eject -- <p> <n> --patch, or delete its files (overlay.patch stays) to replay again` |

`--patch` without `--rebase` on an existing patch keeps refusing, with the message
`overlays/<p>/<n>/overlay.patch already exists — reopen it on current upstream: npm run eject -- <p> <n> --patch --rebase, or pass --force to discard it`.
`--force` keeps its meaning: discard the overlay and lay down pristine upstream.

### Replay

1. If the lock holds an entry, print exactly what `--bless` prints: one `DRIFTED <address>: <file>`
   line per drifted primary and merge-source file, each followed by its recorded-versus-current
   diff (`printDrift`, extracted from the bless branch).
2. Copy the current upstream item into a temporary directory with `copyUpstream` (no symlinks, no
   root `.git`).
3. `replayPatch(work, base, oldPatchText)` in `tools/lib/overlay.ts`:
   - `git init -q`; write `.git/info/attributes` = `* -text`; `git config merge.conflictStyle diff3`;
     `git add -A --force` (no ignore rule may keep a file out of the index that `--3way` reads), all
     under the isolated environment.
   - Split the old patch at column-0 `diff --git ` lines (`splitPatch`). For each chunk, with
     target = its `+++ b/` path, else its `--- a/` path, else the `diff --git a/` path:
     1. `git apply --check` passes: apply it; status `applied`.
     2. Else `git apply --3way` with the alternates. If `git ls-files -u -- <target>` is non-empty:
        status `conflict`, `regions` = the count of `^<<<<<<< ` lines. Else if it exited 0: status
        `merged`.
     3. Else `git apply --reject`. If `<target>.rej` exists: status `rejected`, with the hunk counts
        and git's first error line.
     4. Else write the chunk itself to `<target>.rej`; status `gone` when the target does not exist,
        else `rejected`.
   - Status comes from the index and the files, never from stderr wording. Remove `.git`; return
     the results.
4. Copy the result onto `overlays/<p>/<n>/`; `overlay.patch` stays untouched.
5. Report, one line per file, then the legend and the next step:

```text
Replayed overlays/<p>/<n>/overlay.patch onto <source>:
  applied   SKILL.md
  merged    references/x.md (upstream moved inside the patched region — read the merge)
  CONFLICT  SKILL.md (2 regions)
  REJECTED  scripts/run.ps1 -> scripts/run.ps1.rej (1 of 3 hunks: <git's first line>)
  GONE      notes.md -> notes.md.rej (upstream deleted the file)
Markers: <<<<<<< ours = current upstream, ||||||| base = what the patch was cut against, >>>>>>> theirs = the old patch's result.
Next: resolve every conflict and .rej, delete each .rej, review, then: npm run eject -- <p> <n> --patch
```

Exit 0 when every file is `applied` or `merged` (the next line then reads
`Next: review the working copy, then: npm run eject -- <p> <n> --patch`); exit 1 otherwise. A clean
replay is textual; the curator still judges intent.

### Cut changes (every cut, first or rebased)

- `cutPatch(upstreamDir, workDir)` (extracted from eject phase 2, unchanged format: `-c core.autocrlf=false -c core.eol=lf diff --no-ext-diff --no-textconv --no-prefix -U8 --no-index -- a b`)
  copies the upstream side with `copyUpstream` and the working side without symlinks and without the
  root `overlay.patch`.
- `cutResidue(patch)` returns the targets of chunks that add a `*.rej` path or an added conflict
  marker line. Non-empty: refuse before writing, naming each file; working copy and old patch stay.
- Write the new patch to a temporary file, `checkPatch(base, tmp)`, and only then write it to
  `overlay.patch`, delete the working copy, and `stamp()`.
- An empty cut while `overlay.patch` exists means upstream already carries every edit (a rebase
  merged them all away). The build would keep naming the rebase, so the cut refuses with
  `Upstream already carries every edit in overlays/<p>/<n>/overlay.patch — nothing is left to patch. Delete overlays/<p>/<n>/, its <p>/<n> entry in overlays/overlays.lock.json, and body: patch on the item.`
  An empty cut without `overlay.patch` keeps today's "No edits found" refusal.

### `--bless`

After the "Nothing to bless" guard and before any drift display: if `overlay.patch` exists and
`checkPatch(base, patchFile)` fails, print
`overlays/<p>/<n>/overlay.patch no longer applies to <source> — blessing would record a patch the build cannot apply. Rebase it: npm run eject -- <p> <n> --patch --rebase`
and git's text, then exit 1 with the lock untouched, even under `--yes`.

## 5. X10: drift predicate and one ceremony

```ts
export function fileStamp(p: string): string | null; // blobSha for a regular file, else null
```

- `stampFiles` keeps a file only when `fileStamp` is non-null; `stampMergeFiles` records
  `fileStamp`; `driftedFiles` flags `fileStamp(up) !== sha`; `driftedMergeSources` skips
  `fileStamp(p) === sha`, else reports `(appeared upstream)` when `sha === null`, else plain.
- `eject`'s `showDrift` prints `(gone upstream)` when `fileStamp(now) === null`.
- Effects: a directory or symlink at a null-stamped name is no drift; a stamped file replaced by a
  directory is drift and never throws; drift(stamp(x)) is empty for a file, an absent name, and a
  directory.

```ts
export type EjectStep = "overlay" | "patch" | "bless" | "rebase";
export function ejectCommand(plugin: string, name: string, step: EjectStep): string;
// overlay: npm run eject -- p n
// patch:   npm run eject -- p n --patch
// bless:   npm run eject -- p n --bless
// rebase:  npm run eject -- p n --patch --rebase
```

- `build.ts` `overlayDrift` takes `next: string` instead of building its own command. The patch
  branch of `collectProblems` runs `checkPatch` before `overlayDrift` and passes
  `ejectCommand(..., err ? "rebase" : "bless")`; the overlay branch passes `"bless"`. The three
  `--yes` suffixes go, and "Fix merged_from, then re-bless" becomes "Fix merged_from, then: <next>".
- The non-applying problem reads
  `<id>: overlay.patch no longer applies to <source> — run: <rebase>` followed by git's text.
- The missing-overlay problems in `collectProblems` and in the `assemble.ts` mirror both use
  `ejectCommand(..., body === "patch" ? "patch" : "overlay")`; the mirror's missing `--patch` is the
  mismatch this closes.
- `sync.ts`: OVERLAY, PATCH, and MERGE SOURCE lines end with
  `review: <git diff>; then npm run build names the eject step`. The PATCH tag becomes
  `PATCH — a change to a file overlay.patch edits stops the build; changes elsewhere in the item flow through`,
  replacing the wrong "absorbed if it misses the patched region".

## 6. X7: sources deleted upstream

`tools/lib/manifest.ts`:

```ts
export const UNKNOWN_SOURCE_RULE =
  "the current pin has no such source. If upstream deleted it, remove the item from the manifest — an exclude is not a tombstone — and give the reason in the commit message and, when another item succeeds it, in that item's comment. If upstream moved it, repoint source:. An exclude for a source that only a later pin has lands with or after that pin move.";
```

| Site | Message |
|---|---|
| `validate.ts` section 1 | `<plugin>: unknown source <src> — <rule>` |
| `build.ts` `collectProblems` and `assemble.ts` mirror | `<plugin>: source not found in external/: <src> — <rule>` |
| `sync.ts` `goneLine`, taken | `... SOURCE GONE<where> — nothing auto-updates: the next build stops with "source not found in external/"; <rule>` |
| `sync.ts` `goneLine`, excluded | `... SOURCE GONE<where> — the build ignores it (excluded), but validate rejects an unknown source: <rule>` |

No new validator check: validate already rejects an unknown source, taken or excluded, so an exclude
cannot land before the pin that has its source. The timing rule is canon.

## 7. Deleted-name universe

`tools/lib/ledger.ts`:

```ts
export function everListedNames(root: string): string[];
```

- Runs `git -C <root> log --format= -p -U0 --no-ext-diff --no-textconv -- docs/ledger.json`.
- Collects every added top-level key (`/^\+ {2}"([^"]+)": \{$/gm`, the `writeLedger`
  `JSON.stringify(…, 2)` shape) and keeps its last `/` segment, which handles the historical
  `<plugin>/<name>` and current `<plugin>/<kind>/<name>` keys.
- Returns `[]` when git fails (no repository, no history).
- `validate.ts` adds the names to `estateNames` (handoff templates H2–H4 and the item-root path
  check). `sync.ts` `syncCandidateNames(ledger, ownSkills, everListed)` unions them; `main()` passes
  `everListedNames(root)`.
- A comment in `writeLedger` cross-references the regex, because a format change must move both.
- Limit: a shallow or archived checkout knows fewer names; a name upstream deleted before any
  ledger listed it is unknown; and only output names are kept, so a taken item renamed with
  `name:` (today only `ask-matt` -> `ask-deniz`) keeps its output name, not its upstream one. CI's
  `validate` job uses `fetch-depth: 0`.
- Measured effect at `6a32ef0`: the 10 extra names have no mention in the three trees; the plan
  proves validate's findings are unchanged.

## 8. Path-claim punctuation

`tools/lib/refs.ts` `claimPath` returns the path minus one trailing `.`, `,`, `;`, or `:`
(`/[.,;:]$/`). Only `validate` reads `claim.path`; `rewrite.ts` respells by `segmentIndex` and
`segment`, so emitted bytes cannot change. `path:42` line suffixes remain unsupported and are a
documented limit.

## 9. Canon written in this change

| File | Change |
|---|---|
| `docs/engineering/workflow.md` | The rebase ceremony; bless refuses a non-applying patch; the one-ceremony sentence; the deleted-source rule and exclude timing in "Upstream sync flow" |
| `curation/SCHEMA.md` | The exclude rule's exception for upstream-deleted sources; `--patch --rebase` in the eject sentence; body ownership names rebase |
| `docs/architecture/references-and-linking.md` | Path grammar sentence; estate names gain ledger history; sync candidate universe; the deleted-names limit is rewritten |
| `docs/ROADMAP.md` | W1 in progress with its pending implementation gaps per symbol; X6 moves to W3; the drift-scan premise corrected; the dotnet count updated; the Known Gaps W1 closes stay, reworded where the decision moved |

`docs/architecture/transformation-and-emission.md` is unchanged here: build output does not change,
and only its `overlay.ts` line anchors move, which the closeout task refreshes.

## 10. Invariants and proof

- Generated output byte-identical after the full generated-output row and its idempotence rerun.
- `npm run validate` output identical to the baseline captured before the first task.
- An unchanged working copy recuts to a byte-identical patch (plan W1.11 R0); the lock is unchanged
  by W1 itself.
- The real submodule pins (`git submodule status`) are unchanged at the end.
- A dry run against a temporary scratchpad checkout of `aspire-skills` at `da1230c` shows
  `aspire-orchestration` replaying clean and recutting with identical `+`/`-` lines, and
  `aspire-deployment` stopping with one `SKILL.md` conflict, a refused cut, a refused bless, and a
  build message that names `--patch --rebase`.

## 11. Risks accepted

- A rebase cut stamps merge-source drift without `--bless --yes`; the replay printed every diff
  first, which is the same show-then-record ceremony.
- A missing base blob (shallow or blobless submodule, a hand-made patch with no index line) degrades
  the replay to `--reject`; the report says so.
- `git apply --3way` tries a direct apply first; no misplaced hunk was seen in 81 files, which is
  evidence, not proof. If one appears, the per-file step switches to `git merge-file`.
- The ledger-history universe depends on history depth and on the ledger's two-space key format.
- A deliberately authored line that is seven `<`, `|`, or `>` followed by a space or the line end
  (a seven-deep Markdown quote, for example) is refused by the cut.
