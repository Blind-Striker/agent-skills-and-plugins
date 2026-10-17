---
name: delegating-to-worker-models
description: Use when you are about to write a task prompt or task file for an
  out-of-process worker model from another model family, dispatch it, verify its
  output, or review it. Covers what never to delegate, the walls preflight, the
  worker contract the dispatcher prepends, verification on the committed tree,
  two-family review, and the fix-up loop. Not for in-process subagents of your
  own harness.
---

# Delegating to worker models

The current calibration for handing implementation, planning, and review work to a worker model of
another family through a dispatcher. Whether to delegate is the user's routing decision; this skill
covers how. The rules come from field runs and one controlled comparison; the worker profiles in
`references/models/` are the part that changes as models change.

## Never give the worker

- Deciding whether something earns its place: whether a file, flag, check, or abstraction should exist.
- Scope, ownership, cross-lane ordering, maintainer policy, and canon or decision wording.
- Suppressions, baseline, ratchet, and threshold decisions; baseline mappings without a mechanical oracle.
- A choice between equally valid designs, API taste, and judging whether evidence supports a claim.
- Pushes, merges, filing issues, and the words "green" or "verified".
- Anything where "verified" rests on the worker's own report.

## Dispatcher contract

Any dispatcher works if it has these properties. The repository addendum names the actual command.

1. One isolated worktree per task, from a stated base commit.
2. A policy deny-list (push, installs, remote shells, never-read files, unneeded MCP servers) with
   word-bounded patterns, and escalation for desktop or browser control.
3. It prepends the worker contract and the model file verbatim (`--contract <path>`, repeatable) and
   records each file's version and sha256 in the run log.
4. It runs the verify commands on the committed tree after the session; that run is the evidence. A verify
   run after a stop that came before any preparation is marked not meaningful.
5. It records tool calls and skill loads in its ledger.
6. Its log head is readable within the first minute of a run.
7. It refuses two tasks of one run that share a scope path.
8. It can collect a run whose runner was killed, and it cleans up only after a merge proof.
9. Each session gets a scrubbed environment (credential variables removed) and a per-task temp folder
   (`TEMP`, `TMP`, `TMPDIR`), and neither the session nor the verify builds join a long-lived shared build
   server: such a server keeps the environment of whoever started it.

A repository addendum marks any property its dispatcher does not have yet as "target", so nobody assumes
the ledger proves what it does not.

## Before every dispatch

1. **Walls preflight.** Read the repository addendum's Walls field, then pick one:
   - (a) The repository has walls: put them in the verify array.
   - (b) It has none: bring ephemeral walls through the verify array from the profile in
     `references/walls/<language>.md` (pinned tools, a scratch cache, no install, no repository change).
     Run them on the base first and gate only new findings and touched files; a new file is gated whole.
   - (c) Neither is possible: make plan-first mandatory, tighten the budget, add a reviewer, and tell the
     user that walls would help. Installing walls into a repository for good is the user's decision.
   The thresholds are yours; record them, and any change, with a reason.
2. **Verify the verify.** Run every verify command once on the base and see a non-empty, meaningful
   result; a test run that reports zero tests is never a pass. Check that every test filter, class, or path
   it names exists. A change to a type, a public name, or a shared fixture runs the whole test project that
   holds inventories or registries, not a filtered subset.
3. **Prove the base.** `git merge-base --is-ancestor <prerequisite> <base>` for every prerequisite. Put
   that SHA, not a branch name, in the task file's base: a branch moves while tasks wait in the queue.
   The worker only confirms that its HEAD equals the stated base.
4. **Write the task file with an editor tool**, never a shell heredoc (backslashes collapse).
5. **Plan-first** for M-size or larger work, any new type, more than three production files, or a new file
   expected to exceed about 150 lines. The worker writes the plan with a required section "Contradictions
   and spec defects found"; you amend and approve it; the code task runs against the approved plan. For an
   S task, optionally send the filled prompt once in read-only mode to scan for contradictions.
6. **Fill the prompt** from `references/prompt-template.md`. Write only the per-task fill-in; the dispatcher
   adds the contract. Every value the repository owns elsewhere comes in as an input, never as an anchor
   to a document. Every term gets an in-scope and an out-of-scope example.

## During the run

- Read the log head within the first minute. A waiter matches the prompt line and any refusal or error
  line, never only the final summary.
- A worker cannot be messaged. Late information is applied at review: restore the affected files to the
  base and prove the diff is empty.
- Cap concurrent heavy jobs to what the host's memory allows. Collect a killed run before you restart it.

## After the run

1. **The verify log is the evidence.** The worker's "passed" lines are not. Rerun the gates at the branch
   head yourself before any local check.
2. **A stop is the product working.** Read its reason. Most stops and failures of a template run trace back
   to the coordinator's prompt (an abstract slot, a coordinator duty pasted into the worker prompt, a name
   that does not exist): read the spec the worker received first, and fix the prompt, not the model file.
3. **Review with two families.** Your own read, plus a fresh read-only worker reviewer that gets the
   requirements and the diff but not your assessment. Rubric: defects first, then anything not asked for.
   For a refactor, both families compare the base and the new code behaviour by behaviour.
   For new lock, race, or cancellation-lifetime code, add a third family if one is available
   (`references/models/README.md`). The merge queue and CI stay the last net for cross-change effects.
   For your own read, follow `$deniz-process:requesting-code-review`. When a review should fan out into
   waves, suggest that the user start `$deniz-process:verified-waves`; it never starts itself.
4. **Fix-up loop.** Turn findings into a per-file cut list with line ranges and the replacement. A worker
   fix pass reports each item done or rejected with figures. Spot-check the blockers yourself.

## References

- `references/worker-contract.md`: what every worker gets, verbatim (the dispatcher prepends it).
- `references/models/README.md`: the model index (route, cost, review focus) and each model's file.
- `references/prompt-template.md`: the per-task fill-in, with sections by task type.
- `references/work-types.md`: read when the task is new code or a refactor.
- `references/test-oracles.md`: read when the task writes or changes tests.
- `references/walls/README.md`: read during the walls preflight.
- `references/failure-modes.md`: read when you review, and after any surprise.
- `references/repo-addendum-template.md`: read when a repository has no addendum yet. Its first required
  field is the line that makes this skill load: "Before you write a worker prompt or review worker output,
  load `$deniz-process:delegating-to-worker-models`."
