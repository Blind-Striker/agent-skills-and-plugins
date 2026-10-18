# Worker-model delegation evidence

Date: 2026-10-17

This dated note holds the evidence behind the original skill
[`delegating-to-worker-models`](../../skills/deniz-process/delegating-to-worker-models/SKILL.md). The
skill carries the current rules; this note carries the runs, numbers, and calibration they came from.
Facts are from runs on 2026-10-15 to 2026-10-17 in two repositories (an estate configuration
repository and a .NET SDK); opinions are marked.

## Field runs (GPT-6.1 Sol, `openai/gpt-6.1-sol-1m#high`, through one dispatcher)

- Estate, round 2: 35 new read-only probes; 34 succeeded, 1 timed out with usable code; $39.5. Output
  16,782 probe lines; an Opus review found no hard blockers but about 2,800 removable lines (hand-written
  parsers, seven process trackers, defensive code), invented flags and rules, an invariant turned into an
  always-fact row, and hard-coded baseline values.
- Estate, round 3 (fix and simplify from cut lists): 35 tasks, $18.5, 16,782 -> 14,247 lines, all flagged
  items closed. A later per-behaviour review (five fresh Sol reviewers, one Claude triage) found 57
  High/Medium behaviour changes: 50 accepted (planned cuts or unrealistic inputs), 3 canon questions, 4 real
  regressions, one of which the Sol reviewers missed and the Claude triage found by running it on the target
  machine.
- SDK, about 40-50 runs of refactoring, test hardening, and generator work under a strict canon with
  mechanical gates: little overbuilding; stops on real spec defects when given a STOP list; six hidden
  behaviour changes in one refactor that byte-identical output and every test missed, found only by two
  reviewers comparing base and new per behaviour.

## Controlled comparison (estate, 2026-10-17)

Three arms, the same tasks, two replicates (indicative only, n = 2 per arm):
- A: a "keep it simple" prompt with hard rules, no budget, no STOP list.
- B: the worker contract plus the model file plus a filled task prompt.
- C: B plus ephemeral Python walls in the verify array (ruff, pyright basic, vulture, bandit, jscpd, a line
  gate, a forbidden-construct grep; fix tasks ratcheted on the base counts).

Tasks: one new probe (about 8 checks), two bounded fixes in existing probes.

| | A | B | C |
|---|---|---|---|
| New probe, production lines | 438, 426 | 228, 203 | 235 |
| New probe, ruff findings | 25, 18 | 4, 11 | 0 |
| Fix tasks, size | similar across arms | similar | similar |
| Stops on real spec defects | 0 | 4 | 5 (one budget stop) |
| Cost (all tasks) | $3.66 | $4.19 | $3.59 |

Blind Opus review per task (arms hidden), best to worst:
- new probe: C, B, B, A, A (A: every output a fact, network code the spec forbade, one variant producing
  nothing under the documented empty argument);
- fix 1: B, B, C, C, A, A (A1 rewrote an existing positive test so an unquoted literal secret is missed;
  the reviewer flagged it as a blocker);
- fix 2: B, C, B, A, A (A and one B moved shell parsing into the host language and lost an error path).

The coordinator's spec and template had six defects; B and C stopped on each at $0.14-0.19 and 1-2 minutes;
A stopped on none and shipped around them.

Reading (opinion): the prompt mechanisms drive size and behaviour; the walls drive lint cleanliness. The
worker saw the walls, so part of C's effect may be iteration against their feedback.

## Review calibration (SDK, 2026-10-15)

14 past real defects with 16 answer keys; each route reviewed the commit before the fix, blind; HIT /
PARTIAL / MISS under one rubric. Claude 69% (0 confidently wrong); Sol `#high` 69% (0 wrong, $6.30); Grok 4.7
66% (1 wrong, $53.80, the only route to hit the hardest concurrency keys); Muse 50%; GLM 44%; DeepSeek 38% (3
wrong: cleared defective code). No route found a layering defect unprompted. Sol `#xhigh` on the 5 hardest
cases found nothing `#high` missed and cost 1.2-1.6x. Caveats: 16 keys (1 key is about 6 points); Claude
graded Claude with 4 HITs spot-checked; equivalent, not byte-identical prompts; review only.

## Pilot log (2026-10-17)

Stops from the first day of running the shipped template in both repositories. Every one was
coordinator-caused and every worker stop was correct (estate 7 of 7, SDK 8 of 8 over-triggers on the
coordinator side; no unjustified worker stop). The skill folds them into one rule, the no-contradiction
pass, instead of one rule per case.

| Case | Cost | What closed it |
|---|---|---|
| Fix-up prompt: a free-text "caps bound scope, not review fixes" beside a binding checkpoint on the whole change's caps | $0.21, no change | caps stated as a delta on the fix-up base |
| Task file base `master` while the prompt stated a SHA; a push landed mid-queue; 2 of 5 workers stopped on the HEAD check | about $0.14 | the proven SHA in the task file |
| Test permission given as line ranges; a third fixture listing the same commands was outside them | one rerun | a rule over what may change |
| A planned removal pushed a method past an analyzer's 80-line limit; out-of-plan stop | $1.36 | the fallout pre-approved in the plan |
| Budget checkpoint fired three times on a guessed estimate | three reruns | caps from a measured projection |
| Secret-detector false positives: five successive specs removed a positive oracle, missed a hit, exempted the detector's own positives, forbade the asked-for tests, and left a literal with no permitted change | about $0.86 over six runs, zero edits | the measured hit list, "existing positive oracles win", and a coordinator-named fallback |
| "Prepare as an earlier task" while an older Read-first plan forbade the restore | $0.44, no edit | each step as its command, plus an amendment naming the superseded point |
| Outsider evaluation (12 builders from public documentation only): 4 of 5 first-wave builders stopped under the implementation stop list, on a documented experimental-API opt-in read as a suppression, on a coordinator-named shape the documentation lacks, and on "no retries" against a documented readiness loop | about $0.5-1 each | an evaluation clause in the worker contract (v2): stop only at the fence, FAILED for unreachable requirements, FRICTION entries for the rest |
| Outsider evaluation, second wave under the runner's consumer-app rules (later folded into the v2 evaluation clause): zero contract-shaped stops across seven apps and three redispatches. Logged, not ruled: one criterion asked for an unobservable fact (reported FAILED on that slot only); builders took a coordinator isolation shortcut literally and hit a real tool defect; one builder exceeded the model file's 250-line checkpoint with a reason, which the evaluation clause now allows | n/a | the evaluation clause now also replaces model-file stop rules |
| "A compiling product mutant per assertion" beside "do not edit src/"; the second run's exception listed some mutated lines but not the parameter-name sites the assertions pinned | $0.59 over two runs | mutated lines named as pre-approved, revert-after edits; an empty product diff at the end |

Reading (opinion): the STOP list makes coordinator defects cheap and visible, so a single pilot stop is
not by itself a reason for a new rule.

## Open measurements

- Whether dispatched workers load global user instructions (one report came back in the user's chat
  language) and project skills; needs the dispatcher's tool-call and skill-load ledger.
- The hit rate of a read-only contradiction scan against in-run stops.
- Whether walls reduce size on their own, without the prompt mechanisms.
