---
name: delegating-to-worker-models
description: Use when you hand a piece of work (implementation, a plan,
  research, or a review) to another agent, write its prompt, check its output,
  or review it. Covers what stays with you, assigning work by strength, the
  walls preflight, prompts without contradictions, the worker contract and model
  profiles, evidence from your own run, review, and the fix-up loop.
---

# Delegating to worker models

You keep the judgement; other agents do the work. Assign work by strength: judgement goes to the
strongest reasoner you have, implementation to the strongest implementer. `references/models/` says
which model is strong at what, how to brief it, and where to look when you review it. The mechanism
is any subagent tool you have; the rules below hold for all of them.

## What stays with you

- Deciding whether something earns its place: whether a file, flag, check, or abstraction should exist.
- Scope, ownership, cross-lane ordering, maintainer policy, and canon or decision wording.
- Suppressions, baseline, ratchet, and threshold decisions; baseline mappings without a mechanical oracle.
- A choice between equally valid designs, API taste, and judging whether evidence supports a claim.
- Pushes, merges, filing issues, and the words "verified" and "green". Green means the head SHA, its
  required checks passed, and the change mergeable: a conflicted change runs no main CI and only looks
  green.
- Anything where "verified" rests on the worker's own report.

## Before every hand-off

1. **Walls preflight.** Read the repository addendum's Walls field, then pick one:
   - (a) The repository has walls: put them in the verify step.
   - (b) It has none: bring ephemeral walls through the verify step from the profile in
     `references/walls/<language>.md` (pinned tools, a scratch cache, no install, no repository change).
     Run them on the base first and gate only new findings and touched files; a new file is gated whole.
   - (c) Neither is possible: make plan-first mandatory, tighten the budget, add a reviewer, and tell the
     user that walls would help. Installing walls into a repository for good is the user's decision.
   The thresholds are yours; record them, and any change, with a reason.
2. **Verify the verify.** Run every verify command once on the base and see a non-empty, meaningful
   result; a test run that reports zero tests is never a pass. Check that every test filter, class, or path
   it names exists. A change to a type, a public name, or a shared fixture runs the whole test project that
   holds inventories or registries, not a filtered subset. Verify where the result is used: every
   operating system, drive, and path depth it meets, or tests that do not depend on them.
3. **Prove the base.** `git merge-base --is-ancestor <prerequisite> <base>` for every prerequisite, and
   put that SHA in the task. The worker only confirms that its HEAD equals the stated base.
4. **Plan-first** for M-size or larger work, any new type, more than three production files, or a new file
   expected to exceed about 150 lines. The worker writes the plan with a required section "Contradictions
   and spec defects found"; you amend and approve it; the code task runs against the approved plan. For an
   S task, optionally send the filled prompt once in read-only mode to scan for contradictions. Before you
   approve a plan, cut what the request did not ask for: verification and one-off checks use the tools
   the repository already has, and a one-off check is a command in a step, not a new file.
5. **Fill the prompt** from `references/prompt-template.md`. Write only the per-task fill-in. Every
   worker gets `references/worker-contract.md` and its model's worker block verbatim: when your
   mechanism prepends files, name them; otherwise paste them in yourself. Every value the repository
   owns elsewhere comes in as an input, never as an anchor to a document. Every term gets an in-scope
   and an out-of-scope example.
6. **No contradiction leaves your desk.** Read what the worker will read as one text: the contract, the
   model block, the plan, every Read-first document, and your fill-in. A worker resolves a conflict
   toward the binding written text and stops, so close every path before you send:
   - Every case you can foresee has exactly one permitted path. Write a permission as a rule over what may
     change, never as line ranges or "as in an earlier task".
   - Your prompt outranks the plan, so check each rule you write against the plan section it cites.
   - Derive the Scope line from the requirements: name the file where each requirement's hook lands.
   - In a mechanical task, quote the exact list or count; never write "every X" beside an expected count.
   - A done-when that needs several commands at once says how to run them in the foreground.
   - Every prohibition traces to the request, the repository, or a measured hazard, never to a
     preference of yours or of an earlier plan.
   - The mechanical fallout of a planned change (an analyzer limit, a moved member) is named in the plan as
     pre-approved, not left to the contract's out-of-plan stop.
   - Overriding an older document is an amendment that names the document and the point. A free-text
     exemption beside a binding rule is a contradiction, not an override.
   - Every figure is measured: budget caps from a projection, the base as a SHA, a detector's exemptions
     checked against its own positive tests.

   A stop on any of these is cheap, but it is your defect, not the worker's.

## During the run

- Late information you cannot send to a running worker is applied at review: restore the affected
  files to the base and prove the diff is empty.
- Cap concurrent heavy jobs to what the host's memory allows, and never run two builds in one
  working tree.

## After the run

1. **Evidence is a run you or your tool made** on the committed tree. The worker's "passed" lines are
   not. Rerun the gates at the branch head yourself before any local check.
2. **A stop is the product working.** Read its reason. Most stops trace back to your prompt (an abstract
   slot, a coordinator duty pasted into the worker prompt, a name that does not exist): read the spec the
   worker received first, and fix the prompt, not the model profile.
3. **Prove the cause before you hand out a fix.** When the cause is unproven, the task is "reproduce,
   prove it with evidence, fix it where it lives". A raised timeout, a retry, or a serialized test is not
   a fix for a flaky test.
4. **Review.** A worker never judges its own work; the worker's profile says where to look. Your own read follows `$deniz-process:requesting-code-review`. Low-risk scaffolding and
   mechanical work needs your read only. Code and spec work always adds a fresh reviewer from a family
   other than the author's, who gets the request, the requirements, and the diff, but not your
   assessment: the other family catches what the author's family misses. The family claim rests on the
   record of the model that ran, never on the route you asked for: on every dispatch-harness task the
   claim rests on, `routeVerified: true`; an author that is a session writing by hand, not a task, is
   identified by that session's own model. Rubric:
   defects first, then gaps against the request, then anything not asked for, then tests that pass for a
   reason production lacks. For a refactor, both reads compare the base and the new code behaviour by
   behaviour. For new lock, race, or cancellation-lifetime code, add a third family if one is available.
   The merge queue and CI stay the last net for cross-change effects.
5. **Fix-up loop.** Turn findings into a per-file cut list with line ranges and the replacement. A worker
   fix pass reports each item done or rejected with figures. Spot-check the blockers yourself. A fix pass
   toward the same done-when is not new scope.
6. **Land.** When parallel tasks from one base conflict, a land task cherry-picks them onto the current
   tip, says which side wins, resolves toward the newer behaviour, and reruns the suite.

## References

- `references/worker-contract.md`: what every worker gets, verbatim.
- `references/models/README.md`: the model index (roles, cost, review focus) and each model's profile.
- `references/prompt-template.md`: the per-task fill-in, with sections by task type.
- `references/work-types.md`: read when the task is new code, a refactor, or an evaluation.
- `references/test-oracles.md`: read when the task writes or changes tests.
- `references/walls/README.md`: read during the walls preflight.
- `references/failure-modes.md`: read when you review, and after any surprise.
- `references/repo-addendum-template.md`: read when a repository has no addendum yet.
