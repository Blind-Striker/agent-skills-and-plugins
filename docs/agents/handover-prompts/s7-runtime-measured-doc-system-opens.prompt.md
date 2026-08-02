# S7 — The Runtime Half Got Measured; the Documentation System Opens

Date: 2026-08-01

You are entering after a session that ran no curation at all. It built a probing lab, measured the
runtime behaviour of the closed `deniz-process` module across two harnesses and eight model/harness
combinations, **retracted a finding it had itself committed two hours earlier**, and ended with the
user opening a question the repo has not faced yet: whether its documentation categories and its ADR
rules still fit what the work has become.

That last item is the next session's first agenda item, by the user's explicit request.

## First Principle

> Treat every claim here as **current-as-of-authoring (2026-08-01)** and verify it against the live
> repository before acting: `git log --oneline -12`, the six gates, and the canonical documents.

This session earned that sentence the hard way. Five measurement errors were made and four of them
produced confident, wrong statements before they were caught — two of those reached committed
documents. The details are in **Pitfalls** below, and they are the most useful part of this file.

## What Just Happened

Four commits, all documentation, all pushed (`5622716..2641039`). No `curation/`, `overlays/` or
`tools/` change. The output trees are untouched and byte-identical.

| Concern | What changed |
|---|---|
| **A probing lab exists** | `E:\harness-probe-lab` — isolated homes for both harnesses, scripted matrices, a preflight that refuses to start, raw results. Local-only, never committed. Section below. |
| **Claude Code has deterministic non-interactive introspection** | `stream-json`'s `init` event carries `slash_commands`, `skills`, `plugins`, `mcp_servers`, `memory_paths`. `harness-probing.md` said none existed; corrected. |
| **OpenCode commands are scriptable** | `opencode run --command <name>` invokes one and resolves the name before any model call. The research note said command behaviour "cannot be exercised non-interactively"; corrected, with the control that caught the imitation (a slash in message text is *not* expanded, yet a model will often infer the intent and look like it ran). |
| **The invocation contract is measured in both directions** | Claude Code user surface = `manual` + `both`; model surface = `auto` + `both`; intersection the `both` items, union everything taken. ADR-0005's table, observed on real output rather than fixtures. |
| **A committed finding was retracted** | "A declared edge can be inert" — deleted from the roadmap, and the ADR-0008 consequence built on it rewritten. It was a sample of one, and the one was contaminated. |
| **Propensity has numbers** | Reached by another body's prose vs. reached by its own description: the two are not close. Rates in `docs/research/skill-invocation-across-harnesses.md`; the *shape* only in ADR-0008. |
| **The two upstreams disagree about delivery** | mattpocock composes (a trigger names the knowledge skill); superpowers amplifies (a SessionStart hook this repo does not ship). Recorded in `skill-framework-landscape.md`. |
| **`validate`'s sibling-path warning is narrower than it looked** | Models resolve `../<item>/…` against the *skill* directory, and the parked bundle preserves that layout, so the climb lands. Roadmap gap narrowed rather than deleted. |

## Current State You Should Assume Until Rechecked

- **HEAD** (`master`): `2641039`, pushed, CI green on it.
- **Gates:** six green; `validate` 0 errors / 3 warnings — the standing converted-command warnings,
  a recorded decision, not rot.
- **Curation:** unchanged. `deniz-process` closed; three dotnet manifests still hold one starter each.
- **Budget state:** OpenRouter credit ~$3.55 remaining (the user topped it up for this session).
  `opencode-go` connector is **at its monthly limit until ~2026-08-05**; kimi and deepseek were
  moved to `moonshotai/` and `deepseek/` metered routes. GitHub Copilot is a **company account and
  is off-limits for experiments** by the user's instruction.
- **Local-only:** the lab at `E:\harness-probe-lab`, containing real credentials (copied
  `.credentials.json` and `auth.json`). Not a git repo; keep it that way.

## The Lab

`E:\harness-probe-lab`, outside the repo and outside `%TEMP%` deliberately — OpenCode discovery
walks up from the working directory, and a lab under the user profile silently collects the real
`~/.agents` and `~/.claude` trees on the way.

```
lab.ps1            Start-ClaudeLab / Start-OpenCodeLab / Sync-Lab   (dot-source in a dedicated shell)
verify.ps1         isolation proof, 12 checks; -Deep adds two Claude -p calls
matrix.ps1         OpenCode probe matrix: -DryRun, preflight, per-run timeout, liveness check
claude-matrix.ps1  Claude Code repeat matrix, same discipline
variants.ps1       what reasoning variants each model actually declares
probe.ps1          single Claude Code probe
RESULTS.md         raw observations, every table, verbatim quotes
results-*.txt      raw result lines per probe, plus .text transcripts
project/scratch-repo   3-commit fixture, baseline 50b9065
```

Isolation is asymmetric and this matters. **Claude Code:** `CLAUDE_CONFIG_DIR` replaces the config
root, so one variable is enough; mount the plugin with `--plugin-dir` and — load-bearing —
`--add-dir` on the same path, or bundled files are unreadable and a subagent silently falls back to
the skill body. **OpenCode:** `OPENCODE_CONFIG_DIR` only *adds* a search location, so isolation is a
relocated `USERPROFILE` + `HOME` + `XDG_CONFIG_HOME` + `XDG_DATA_HOME` with the built tree mounted
as the global config and no `plugin:` key anywhere. Verification has a positive control on each
side: isolated OpenCode lists exactly one skill (`customize-opencode`); isolated Claude Code with
nothing mounted lists only its own bundled skills.

`docs/agents/harness-probing.md` is canonical for the method. The lab is one instance of it.

## Pitfalls — Five Measurement Errors, and What Each One Cost

Read this section before you run anything. Four of the five produced a confident wrong statement,
and two of those reached committed documents.

1. **A fixture that was never reset.** The first probe harness reran against a working tree an
   earlier probe had already modified: the function under test was already written, so the ceremony
   had nothing to drive and did not invoke the skill it names. That single observation became "a
   declared edge can be inert", reached ADR-0008 and the roadmap, and survived two commits before
   repeats disproved it. `Reset-Scratch` now does `git reset --hard <baseline>` plus `clean -fdx` —
   checkout-and-clean is not enough, because the ceremony under test ends with "commit your work"
   and obeys it.
2. **`-match` is case-insensitive in PowerShell.** A smoke test checked for `OK` in the output and
   passed on `Token refresh failed: 401`, because `Token` contains `ok`. Two dead provider legs were
   reported as live. Use `-cmatch`, a nonsense marker (`ZEBRA-OK`), and read the JSON event stream
   rather than prose.
3. **Variable names are case-insensitive too.** `$leg = $LEG[$legName]` overwrote the table it was
   reading, so every leg after the first silently ran on the default model. Two result rows were
   labelled with models that never ran. Every result line now echoes the model the harness actually
   used, read back from the run's own init event.
4. **A flag that is accepted is not a flag that is applied.** `--variant` (OpenCode) and `--effort`
   (Claude Code) both accept nonsense silently. Two runs used default effort while claiming
   `xhigh`. What a model actually offers is in `opencode models <provider> --verbose` under
   `variants`; the preflight now checks every requested level against that list.
5. **A background task diagnosed but never killed.** Its bug was found, the script was fixed, and
   the *running* job was left alone — it kept writing garbage into the next run's results file for
   forty minutes. The preflight now refuses to start when an `opencode` process is running or a
   results file already exists.

Two further design errors, in probe *targeting* rather than mechanics, both on the same probe:
asking a one-file question by invoking a 150-line ceremony (a model that ignored "skip ahead" ran
the whole workflow and stalled), then asking for "the reviewer template" in a body that links four
of them (every leg read the nearest). Both are retired in `matrix.ps1` with the reason beside them.

The common root of all of these is one sentence: **"it did not error" was treated as "it worked."**
The preflight exists to close that path, and it is worth extending rather than trusting memory.

## Insights Worth Carrying

- **Reachability and propensity are different properties, measured differently.** Reachability is
  mechanical and exact — a flag, a listing, an artifact. Propensity is a selection the model makes,
  and nothing in either harness forces it. The repo's machine can prove the first and says nothing
  about the second, which is exactly what ADR-0008 claims.
- **An invocation is not the discipline it carries.** In the one run of twelve where the edge went
  unwalked, the model wrote its tests first anyway, from the host body's prose. What a missed edge
  actually costs is the target's *unique* content, not the practice. Any ratio measured here counts
  skill invocations, not behaviour, and should be labelled that way.
- **`depends_on` is a set of possibilities, not a checklist.** The linker reads it as "do these
  addresses exist"; a session reads it as "which of these fits". A body declaring two edges usually
  walks the one the situation calls for. Reading an unwalked declared edge as a defect is what
  produced the retracted finding.
- **A body is a strong suggestion, not a program.** Models skipped steps, ignored "go straight to
  X", and reached the same end by other routes. Compliance varies far more than reachability does.
- **The measurement instrument is part of the finding.** The path a model actually opened — read
  from the tool *input*, not the tool name — was the only thing that explained how a supposedly
  broken relative link resolved. Tool names alone would have left a plausible wrong story standing.

## The Documentation System — Next Session's First Agenda Item

The user's position, in his framing, recorded because it is the starting point rather than a
conclusion:

> The ADRs have started to drift out of being ADRs — item names and measurements have crept in,
> where an ADR should be about the codebase, the machinery, the process. The repo probably now
> needs a knowledge base, a research tier, an experiment tier and a playbook tier, and there is no
> playbook today partly because adding document categories was discouraged. The ADR rules
> themselves should be reviewed, and the ADRs may be worth rewriting from scratch, tools- and
> process-focused only.

One discriminator was agreed during the discussion and is worth keeping whatever else changes:

> **Would this sentence need editing if a curation decision changed?** If yes, it is not ADR
> material — an ADR should survive a re-curation of the entire set.

Measured against that test at the end of this session: only ADR-0008 contained curated item names,
in two places — one an illustrative spelling example inside the Decision (defensible), one a
present-tense Context claim that had gone stale (removed). The larger drift was *numbers*, added
this session while correcting a wrong claim; the counts now live in `docs/research/` and ADR-0008
keeps only the shape.

Open questions nobody has answered yet, listed without a recommendation because the user should
shape them: what an "experiment" tier would hold that `research/` does not; whether raw run data
belongs in the repo at all or stays in a lab; whether a playbook is agent-facing (`docs/agents/`)
or shared; whether ADR-0003, which fixed the current structure, is itself the thing being rewritten.

## Other Open Work

Unordered on purpose — the user chooses, and the previous session's handover was criticised for
ranking these.

- **Manual TUI round.** Everything mechanical is now scriptable, so what is left for a human is
  what a human is the instrument for: whether a `/` menu entry is findable and its description
  honest, whether a long command body pasted as the user's message reads as a wall, whether a
  folder-access prompt lands as an interruption. Opinion is the datum there.
- **Queued probe.** Does stating the intent without naming the skill fire the discipline? The
  propensity number was measured against requests that named no intent at all, which is the harsher
  test; the curator's actual usage states intent.
- **Per-module curation.** Three dotnet modules hold one starter each. The `deniz-process` playbook
  probably does not transfer — those upstreams are single-vendor and overlap by subject rather than
  by job, so naming and scope replace the merge question. The aspire router repair (roadmap item 2)
  sits inside that work.
- **Curation sanity panel** (roadmap item 6) — advisory subagents returning judgement, never a gate.
  Its input is now real: two merged bodies with recorded intent, plus a runtime record of how they
  actually steer sessions.
- **Machine migration** (roadmap item 4) and the **OpenCode installer** (item 5). The installer is
  less urgent than it looked — see the narrowed sibling-path gap.

## Locked Policy Recap

- `external/`, `plugins/`, `opencode/` are never hand-edited; output including `docs/ledger.json`
  regenerates and is committed; `npm run inventory` in the same commit as any curation change.
- Curation decisions are the user's. Both failure modes are rejected: no silent pre-decisions, no
  item-by-item question loops. One holistic, opinionated proposal with the genuine debates flagged.
- Never shrink the work to fit a guard. When a need and a rule disagree, the rule is rewritten in
  place.
- Provenance: no curator names, no dates in the curation layer — `validate` enforces it.
- Docs are first-class and land in the same change as what they describe. Every fact has one
  canonical home; anything else is a relay.

## How the User Works, and What He Expects From You

He thinks by talking, in prose, and reacts to a whole proposal rather than a question loop. He
pushes back with design inside the push — when he objects, the objection usually contains the
answer, and this session's two best corrections came from him ("did you check the variant?", "you
can get that from the CLI"). He asked explicitly that a handover not bias its successor.

So: **do not inherit this file's conclusions as settled.** Several of them replaced earlier
conclusions from the same session. If you read something here that the repository or a fresh
measurement contradicts, the measurement wins and the correction is worth more than the original
claim. You are expected to form your own view, to review what is written rather than extend it, and
to say plainly when you think a previous session — including this one — got something wrong.

The most valuable thing this session produced is not a number. It is the demonstration that a
confident, well-argued, committed claim can be an artifact of an unreset fixture, and that the only
defence is a control, a repeat, and a preflight that refuses to start.
