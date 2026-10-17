# Failure modes

Read when you review, and after any surprise. Severity is the harm when the failure is missed.

## Worker failure modes

| # | Failure | Example | Severity / frequency | Countermeasure |
|---|---|---|---|---|
| 1 | Unplanned behaviour change in a refactor | six changes, one a crash, with byte-identical output and every test green | High / rare, invisible to gates | restore-base rule; per-behaviour two-family review |
| 2 | Self-reported verification is wrong | "93/93" on a final tree that was red | High / repeated | verify log on the committed tree; coordinator reruns gates |
| 3 | Existing test rewritten to fit a wrong spec | a positive fixture changed so an unquoted literal secret is missed; gates green, report "Done" | High / seen without a STOP list | contract: an existing test is an oracle |
| 4 | Weak mutant, tautological tests | a mutant that still emitted the event hid a stall; order fixtures a wrong key would not reorder | High / repeated | test-oracles.md |
| 5 | Test-design concurrency races | ambient-context holds on unrelated continuations; "next created timer" reads; a released port rebound by a parallel test | High / repeated in concurrency work | the language module's race rules, quoted; deterministic case; full suite |
| 6 | Overbuilding new code | hand-written parsers, several process trackers, defensive code over its own inputs | Medium / common in open-ended new code | plan-first, budget, named constructs, concrete hazards |
| 7 | Relocation loses an error path | parsing moved from a shell pipeline into the host language: a tab-split regression and a dead "empty output" error | Medium / seen without the model file | "change code where it is" |
| 8 | Invented policy, flags, modes, protocol | a "GET-only" rule that blocked most checks; a protocol that skipped the clone gate | Medium / common | nothing unnamed; precedence; STOP |
| 9 | Over-caution that breaks semantics | an invariant reported as an always-fact row; every output a fact | Medium / common in new code | status semantics in the prompt; do-not-stop list |
| 10 | Values owned elsewhere hard-coded | supported versions and name lists in code; an expected location judged against a constant | Medium / common when the spec gives anchors | inputs owned elsewhere; "report a fact" |
| 11 | Environment edges unstated | path semantics changed in a newer runtime; a default shell timeout killed a long command; a bare shell name resolved to another system | Medium / common | environment section; absolute paths and OS skips in tests |
| 12 | Loose words read strictly | an abstract definition stopped a task; "touched" undefined | Medium / occasional | in-scope and out-of-scope examples |
| 13 | Stop rule over-triggers | the same benign conflict stopped a task three times; each stop costs a rerun | Low / occasional | do-not-stop list |
| 14 | Cross-change interaction invisible per change | a clone between two sibling changes | Medium / rare | merge queue and CI |
| 15 | Report in the wrong language | a report written in the user's chat language | Low / occasional | "Report language: English" in the contract |

## Coordinator failure modes

- A waiter that read only the final summary missed a run refused in its first second.
- Task files written with a heredoc broke JSON.
- Verify commands never run beforehand: one named a nonexistent class, one was broken by shell expansion.
- Six spec and template defects shipped in one batch; the STOP list caught every one, the old prompt none.
- A coordinator check (base ancestry) placed in the worker prompt, where the policy denied it.
- A prompt named a registry that does not exist, and a runtime requirement only implied by the verify list.
- Most stops on the template trace back to the coordinator's prompt: fix the prompt, not the model file.
- Dispatched builds joined a long-lived build server started by another session, which ignored the task's
  temp folder.
- Deny patterns without word boundaries caught read-only siblings.
- An unmeasured cause written into a task as fact.
- Two example rows written where an invariant was needed.
- A worktree edited while its own check ran there.
- A probe whose failed step had already restored a file was trusted.
