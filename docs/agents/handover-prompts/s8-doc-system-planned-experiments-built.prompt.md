# S8 — The Documentation System Got Decided and Planned; the Probe Harness Got Built

Date: 2026-08-02

You are entering after a session that **committed nothing**. It reviewed the documentation system
s7 handed over, reached a decision on it, built the harness-probing subsystem as real working code,
and wrote an implementation plan that survived three independent review rounds and one full rewrite.
All of that output is **untracked**, in `docs/superpowers/`, and a `git clean -fdx` destroys it.

Read that sentence again before you run anything.

## First Principle

> Treat every claim here as **current-as-of-authoring (2026-08-02)** and verify it against the live
> repository before acting: `git log --oneline -5`, `git status`, the six gates, and
> `pwsh -File docs/superpowers/plans/experiments-build/selftest.ps1 -SkipLab`.

This session earned that the same way s7 did, in a new register. Its own plan claimed something was
"confirmed" that had only been *reasoned*, and the claim was false. Details in **Pitfalls**.

## What Just Happened

No commits. No curation. No change to `curation/`, `overlays/`, `tools/`, `plugins/` or `opencode/`.

| Concern | Where it stands |
|---|---|
| **The documentation system is decided** | Four categories under `docs/` plus one executable subsystem (`experiments/`), separated by *read vs. run*. `docs/agents/` is dissolved. Handovers are transient. Documentation is machine-checked nowhere, deliberately. |
| **The razor is defined** | An ADR's Consequences carries only costs accepted at decision time — *"choosing this, we accepted X"*. Two questions apply it fast: would a re-curation force this edit? would a harness version bump? The prose form is the rule; the questions are the shortcut. |
| **The probe harness is built** | 16 files at `docs/superpowers/plans/experiments-build/`: one shared module, a self-test, a deterministic fixture generator, and all seven runners ported off their hardcoded machine paths. Not landed — Wave 1 Task 1 copies it. |
| **The instrument is proven** | 17 self-test checks green with and without a lab; ten mutations each turn the named check red; one real end-to-end run at **$0.0034** shows the echoed model, dot-formatted cost and the tool-use the recorded round observed. |
| **The plan exists** | `docs/superpowers/plans/2026-08-01-docs-system-restructure.md`, v3, 16 tasks, five waves, zero back-references. |
| **The lab is untouched** | Same files, same fixture at `50b9065`, round evidence intact. Still not a git repo — keep it that way. It sits outside the repository and outside the user profile; `Get-LabRoot` resolves it (`$env:HARNESS_LAB`, then a drive-root sibling, then a throw). |

## Current State You Should Assume Until Rechecked

- **HEAD** (`master`): `609544a`, **unpushed**, one ahead of `origin/master` (`2641039`). CI is green
  on `2641039`; `609544a` has no CI result because it was never pushed. That has been true since s7.
- **Gates:** six green. `npm test` → 111 pass. `npm run validate` → **0 errors / 3 warnings**, the
  standing converted-command and parked-bundle warnings, a recorded decision.
- **Working tree:** `M .gitignore` and `?? .wrongstack/` — both pre-existing, unrelated, not this
  session's. Plus `?? docs/superpowers/`, which is this session's entire output.
- **Curation:** unchanged. `deniz-process` closed; three dotnet manifests hold one starter each.
- **Local-only:** the lab, holding real credentials in `.claude-home/` and `.opencode-home/`. Never
  read those two files; never track them.

## Pitfalls — What This Session Got Wrong, and What Each Cost

This is the section worth your time. Every entry happened.

1. **Writing code into a markdown plan and asserting it works.** Two full plan versions specified
   PowerShell in prose. Three review rounds found roughly forty defects in them, and **none** of
   those were found by re-reading. Building the same code and running it found six more in an hour,
   two of which no reviewer could have caught. The fix is now the method: build first, then write the
   plan from what worked.

2. **Claiming "confirmed" for something inferred.** The plan said *"the wrapped-comment grep needs
   `-z` (confirmed)"*. The wrap had been *looked at*; the grep had never been run. `-z` only changes
   the record separator — `\n` in BRE/ERE is a literal `n` — so it returns 0 and the correct form is
   `-P`. A false verification claim is worse than the defect it hides, because it defeats the next
   audit. v3's rule: where a step claims verification, the command's output is printed beneath it.

3. **A guard that matches nothing looks exactly like a clean tree.** The machine-path scan
   the scan returned **0** on a file that hardcoded the lab's absolute path twice, and it was the
   gate protecting a Hard Rule. The working form is
   `[A-Za-z]:[\]`, and the discipline is this repo's own probing rule turned on its own guards:
   **a detector is not trusted until it has caught a known positive.**

4. **The same regex in two languages is not the same regex.** bash needs `[A-Za-z]:[\]`; .NET needs
   `[A-Za-z]:[\\]`. The bash spelling inside PowerShell either errors or silently reads
   `[\]|/home/[a-z]` as one character class and matches nothing — reproducing the no-op above. Never
   sync the two by copy-paste; test both against the control fixture.

5. **A test that cannot distinguish the two things it exists to distinguish.** The `Reset-Scratch`
   check asserted `HEAD == baseline`. Swap `git reset --hard` for `git checkout <sha>` and it still
   passes — checkout *detaches* at that sha, so `rev-parse HEAD` returns the baseline while the
   branch tip still holds the extra commit and the residue stays reachable. This guards the most
   expensive bug this repository has had. Found only by mutation, and now it asserts that HEAD is
   attached and the branch tip is at the baseline.

6. **A green suite while a file was destroyed.** A regex mutation cut `matrix.ps1` from 222 lines to
   28 — probe table, preflight, timeout wrapper, liveness check and run loop all gone — and 15
   checks plus 9 mutations stayed green. A stub parses. A stub has no machine path. A stub defines no
   leg table. Nothing tested that a runner *does* anything. There is now a truncation alarm, and it
   was seen red against that exact destruction.

7. **`re.S` with `(?:.*\n)*?` is catastrophic backtracking.** That is what destroyed the file: with
   DOTALL, `.` matches newlines and the lazy repeat explodes. It ran for two minutes, was moved to
   the background, and applied a partial edit nobody was watching. Use line-based edits on
   structured text.

8. **PowerShell argument passing has two separate traps.** `& $script @($array)` passes the whole
   array to the *first positional parameter*, so `-DryRun` never bound and four real liveness calls
   fired against paid providers. Fixing that to array splatting then bound `-Probes` onto
   `-TimeoutMin`, because an array splat goes positional once a switch is in it. **Hashtable
   splatting is the only unambiguous form.**

9. **`Write-Host` does not go to the pipeline.** It goes to the information stream, so
   `2>&1 | Out-String` misses it entirely, and a dry run that had completed and printed its marker
   was reported as never reaching it. `6>&1` as well.

10. **A plan that defers to a previous version of itself.** v2 said "as in v1" in sixteen places;
    v1 had been overwritten in place and was unrecoverable — untracked, never committed, no stash.
    Twelve of fifteen tasks could not start. Execution is one fresh subagent per task, and a fresh
    subagent has neither the previous file nor the conversation.

11. **Reviewing a thing without opening it.** The first reviewing agent declined to open the lab
    because it holds credentials, then proposed a directory layout for it — copied from a listing,
    missing two files, one of which (`RUNBOOK.md`) was a finished version of the very artifact it
    argued the repo needed. Two named credential files are not a reason to leave a directory unread.

The common root, restated from s7 in this session's own words: **"it did not error" is not "it
worked", and "I reasoned it through" is not "I ran it."**

## Insights Worth Carrying

- **Consequences is where ADRs rot, and the cause is structural.** Context, Decision and Alternatives
  are anchored to the moment the decision was made; Consequences is anchored to *now*, and "now"
  keeps moving — so every session that learns something true adds it there, because it is the only
  section anything fits in. Rot is not confined to it (one Context claim went stale too), which is
  why the prose test rather than the section boundary is the rule.
- **The code is the reference manual, and the property that earns that is proximity, not
  corroboration.** Nothing runs a comment either. But a comment sits in the blast radius of every edit
  to the code it describes, and an ADR paragraph sits in nobody's. Observed across `build.ts`,
  `overlay.ts`, `eject.ts` and `validate.ts`: eight of the mechanics ADR-0001 explains are already
  written beside the code that enforces them, usually better.
- **Documentation drift and build drift are different failures.** A broken reference ships silently
  into an artifact someone installs; that is why the linker exists and why curator vigilance was
  rejected as a mechanism. A doc drift is found by reading, costs a re-read, and is repaired in
  place. The asymmetry is what justifies machine-checking one and not the other — and one exact,
  mechanizable doc check was proposed and **declined on that principle**, so the next session
  inherits a decision rather than an impression.
- **Audience is a property of a sentence, not of a directory.** `docs/agents/` failed its own test:
  how Claude Code addresses a plugin skill, and that its built-in subagents skip `CLAUDE.md`, are
  facts any reader wants. Sorting directories by audience puts shared knowledge behind a label that
  tells readers to skip it.
- **Three review lenses at different context levels find non-overlapping defect classes.** A
  full-context peer catches reasoning that outran its evidence; a zero-context auditor catches false
  factual claims by running them; an "can a fresh agent execute this" lens catches everything the
  other two consider obvious. Where two of the three found the same thing independently, treat it as
  settled and stop arguing.
- **Mutation testing is what makes a suite a suite.** Ten mutations turned nine checks red and
  exposed one that could not go red at all. A check that has never been seen failing is decoration.
- **Derivation over memory costs independence, and the cost must be named.** `verify.ps1` now reads
  its expected counts from `docs/ledger.json`, so it can no longer catch "the build emitted the wrong
  thing" — only "the mount disagrees with the ledger". One memorised oracle is kept deliberately: an
  instrument with nothing of its own cannot contradict the thing it measures.
- **An unrun check is recorded as not run, never as passed.** Two self-test checks need the lab.
  On a machine without one they are skipped and said to be skipped.

## Recommended Next Step

The user's call, and both options are live.

**Option A — execute the plan.** 16 tasks, five waves, one fresh subagent per task, six gates at
every task boundary. Wave 1 is the lowest-risk (copy exercised files, write four documents); Wave 3
asks the most judgement (nine sentences whose fate the plan deliberately leaves to the executor, each
named so it is not discovered mid-task); Wave 4 is the least reversible because it rewrites the
contract every later session is written against. A task that cannot reach green does not commit — it
stops and reports.

**Option B — land Wave 1 Task 1 only, then go do curation.** One commit puts the built subsystem
into `experiments/harness-invocation/` and makes this session's work survive a `git clean`. The rest
of the doc pass waits. This is worth considering because the doc pass is infrastructure: it unblocks
roadmap item 8 (an ADR deliberately left unwritten until the rules were fixed) and makes later
decisions record cleanly, but it moves no product. Three dotnet modules still hold one starter each,
and the aspire router still names five uncurated targets.

Do not silently pick. Ask.

## Mandatory Grounding (read in this order)

1. `AGENTS.md` — the contract.
2. `docs/superpowers/plans/2026-08-01-docs-system-restructure.md` — the plan, including the full
   text of the rewritten ADR-0003 and the new ADR-0009. If you read one thing beyond this file,
   read those two ADR bodies: they are the end state.
3. `docs/ROADMAP.md` — items 7, 8 and 9 are this thread; 1–6 are the product backlog.
4. `docs/agents/harness-probing.md` — before adding any claim about harness behaviour anywhere.
5. `docs/superpowers/plans/experiments-build/` — the built subsystem. Run its self-test before
   trusting anything about it.

## Locked Policy Recap

- `external/`, `plugins/`, `opencode/` are never hand-edited; output including `docs/ledger.json`
  regenerates and is committed; `npm run inventory` in the same commit as any curation change.
- Curation decisions are the user's. No silent pre-decisions, no item-by-item question loops. One
  holistic, opinionated proposal with the genuine debates flagged.
- Never shrink the work to fit a guard. When a need and a rule disagree, rewrite the rule in place.
- Provenance: no curator names, no dates in `curation/`, `overlays/`, `skills/` — `validate`
  enforces it. `docs/` and `experiments/` are outside that scope and dates there are required.
- No machine-specific paths anywhere in committed content.
- Docs land in the same change as what they describe. Every fact has one canonical home.

## Final Steering Note

The user thinks by talking, in prose, and reacts to a whole proposal rather than a question loop. He
pushes back with design inside the push: this session's two best corrections were his — *"we
absolutely do not test documentation"*, which turned a grudging exemption into a stated principle,
and *"do not look at the documents only, dive into the code"*, which is what exposed that half the
ADR surgery rested on an unchecked assumption.

Do not inherit this file's conclusions as settled. Several of them replaced earlier conclusions from
the same session, and the plan you are handed is the third version of itself. If something here
contradicts the repository or a fresh run, the run wins.

The most useful thing this session produced is not the plan. It is the demonstration that a
carefully argued, thoroughly reviewed document can still be unexecutable — and that the only defence
is to build the thing, run it, and mutate it until you have watched every guard fail.
