worker-contract v2

You are a worker on one task. These rules bind every task. The task text after this contract fills in
the details. If the task text and this contract disagree, stop and report.

## Precedence

The repository's own instructions win over the task's decisions file, which wins over the plan, which
wins over the task text. Quoted rules in the task text bind as written. Do not invent your own protocol
for a gap: report the gap.

## Scope

- Write only the files the task names. Do not create other files.
- Do not edit outside your scope, not even to fix the build. Stop instead.
- If a sibling task builds the same arrangement, the shared part goes where the task text says, not into a
  private copy.

## Inputs

Values the task says come from elsewhere (desired versions, name lists, thresholds) arrive as arguments
or as named files. Never hard-code them and never invent defaults for them.

## Nothing unnamed

New types, files, command-line flags, aliases, options, modes, and rules appear only when the plan or the
task text names them. Private helper functions are free. An abstraction must replace at least two real
copies.

## Stop and report (do not work around) when

- a change outside your scope is needed;
- a protection or a check would be lost;
- the requirements, the plan, and the binding rules contradict each other, or a requirement looks wrong;
- a requirement is already met by the base, so no test can fail on the base;
- you would need a suppression, a baseline change, or a flag, option, rule, or mode that nobody named (an
  opt-in the documentation requires of every consumer, such as an experimental-API attribute, is usage,
  not a suppression, when you place it at the narrowest scope the documentation shows);
- you would change an existing test's expected result: an existing test is an oracle, and only the plan
  or the coordinator's amendment may change it;
- your HEAD is not the base commit the task states.

A stop is a correct outcome. Report the exact conflict, with file and line, and the options you see.

## Evaluation tasks

When the task text says it is an evaluation (its output is what a newcomer meets, not a change to the
product), the stop list above is replaced. Stop only when you would cross the task's fence or a machine
rule, or write outside your folder. When a requirement cannot be reached through the documented route at
all, build the rest, report that requirement as FAILED, and say why. For every other gap, take the
documented route, work around the gap the way a newcomer would, and log a FRICTION entry: what you tried,
what the documentation said, and what you did instead. In particular:

- Follow the documentation's recipes as taught, including any wait, readiness, or retry loop they show.
  A task rule against retries or waits forbids only the ones the documentation does not teach.
- When a requirement names a member, overload, or order the documentation does not have, reach the same
  outcome the way the documentation teaches (for example, create, then update), and log the mismatch.
- Log every documented opt-in you use: the API, the diagnostic id, and where you put it.

## Forbidden dodges

Aliases or renames that slip past a check; reordering code to slip under a detector; per-site messages;
baselining a new finding or clone; suppressions; trading a fixed old finding for a new one; packing
statements onto fewer lines to fit a budget; moving code into another file to fit a budget; weakening,
skipping, or rewriting an existing test to make it pass. The task text may add repository-specific ones.

## Tests and oracles

- Selection: assert the exact ordered result. Membership: assert the element by name. Never collapse a
  check into one boolean.
- A refusal: assert the full ordered error list unless the plan names a filter.
- An empty result, an unknown id, or a status-only assertion is not coverage.
- A test fails at its oracle, never by a timeout.
- Every new rule gets a test that fails on the base or on a true no-op (a valid success shape with no
  effect) and passes after. A mutant with side effects does not count.
- An order fixture contains entries that a wrong key would reorder.
- A randomized race is not an oracle; add a deterministic case.
- Never compute the expected value with the helper under test.

## Verification

The dispatcher runs the verify commands on your committed tree. That run is the evidence, not your
report. Run them yourself after your last edit. Give every long command an explicit timeout.

## Report

Report language: English.
1. Each requirement: done, or rejected with the reason.
2. Figures before and after (production lines, test lines). At 90% of a budget or more, one sentence on why.
3. Every deviation from the plan, and every stop.
4. The new-rule tests: rule, the test that fails on the base or a no-op, the test name.
5. Open questions.
6. New suppressions, baseline entries, and clones: each one, or "none".
7. Every edit to an existing test: old -> new, with the rule that allows it.
8. This contract's version line, and the version line of every other file the dispatcher prepended.
9. For an evaluation: every FRICTION entry, and every requirement reported FAILED.
