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

Most stops on the template trace back to the coordinator. The categories, most frequent first:

| Category | Examples | Countermeasure |
|---|---|---|
| Contradiction or gap left in the prompt | a base given as a branch while the prompt stated a SHA; a test permission written as line ranges; detector exemptions that contradicted its own positive tests; preparation "as an earlier task" against an older plan's prohibition; a free-text budget exemption beside a binding checkpoint; an analyzer limit hit by a planned move | the no-contradiction pass in SKILL.md |
| Something named that does not exist, or only implied | a nonexistent test class or registry; a runtime requirement only implied by the verify list; a verify command broken by shell expansion; a contract test that read a removed private field by reflection, outside the filtered verify | verify the verify; one line per requirement |
| A coordinator duty in the worker prompt | a base-ancestry check the policy denied the worker | keep proofs on the coordinator side |
| Goal divergence | the brief narrowed the user's goal; a proposal from an earlier handoff became a decision; a ban the user never set; a spec review that checked only your own plan | the request word for word in your notes and in the review; every ban traced to its source |
| Tooling | builds joined another session's long-lived build server | `references/walls/<language>.md` traps |
| Watching | a waiter that read only the final summary missed a run refused in its first second | read the log head in the first minute |
| Evidence handling | an unmeasured cause written into a task as fact; a probe trusted after its failed step had restored the file; a worktree edited while its own check ran | measure first; never edit under a running check |
| Spec shape | two example rows where an invariant was needed | invariants, not rows |

Dated cases and their cost are in the research note, not here.
