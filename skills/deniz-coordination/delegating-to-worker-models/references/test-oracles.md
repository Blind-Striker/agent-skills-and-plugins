# Test oracles: what the coordinator specifies

The worker contract carries the generic oracle rules. This file is for the coordinator: what each test
task's prompt must name so the worker cannot pick a weak oracle.

## Proof by risk

The repository's normal gate at the branch head is the default proof for every change. A heavier proof
belongs to its risk class below, or to a hunt for a flaky test, and to nothing else: a worker that
reads this table as a checklist over-proves and burns the run.

| Risk class | Proof the prompt requires |
|---|---|
| A bug fix | A test that is red on the base and green after the fix |
| A new bound or rule | A named mutant (below) that the new test kills |
| A race or timing bug | A deterministic case as the oracle. A load loop before and after the fix is evidence, never the oracle. A manual or virtual clock never spends a real-time budget: advance it only after a real-time check of the same predicate, or a poll stepped on every armed timer burns its whole budget in zero real time |
| A lifetime or leak fix | A resource probe (handles, processes, memory) before and after |
| A change that ships to several targets | The proof runs on every target: operating system, runtime, target framework |
| A test conversion | The converted test kills the same mutant as the original |

## What each test prompt names

- **Name the mutant class** for every new bound ("a no-op that sends nothing", "a sort by the wrong key").
  The mutant is the true no-op: a valid success shape with no effect. A mutant counts only when the
  mutated build compiles; commit the change before you mutate, so reverting a mutant cannot wipe it.
  Before you name a mutant, name what the oracle reads that the mutant changes. A mutant the oracle cannot
  see (a deduplicating set, a value captured before the change) proves nothing. Name no kill site narrower
  than the test: a mutant is killed when the test fails within its bound, at the operation or at the
  oracle; only a hang or a timeout-only failure is no kill.
- **Assertion-strengthening tasks:** require proof that the mutant survives on the base and is killed
  after. A mutant is a temporary product edit, so the scope must allow it: name every product line the
  mutants touch as a pre-approved, revert-after edit, and require an empty product diff at the end.
  A blanket "do not edit src/" beside a mutant requirement makes the proof a guaranteed stop.
- **Order fixtures:** require entries that a wrong key would reorder.
- **Refusals:** require the full ordered error list unless the plan names a filter; a filter hides
  cascades.
- **Expected values:** never computed with the helper under test.
- **Existing tests are oracles:** a change to an existing test's expected result needs the plan or your
  amendment; the worker reports every such edit old -> new. Write the amendment as a rule ("every
  fixture that lists the probe's commands may add the new query; no other expectation changes").
- **Detectors and validators:** before you write an exemption, read the detector's own positive tests.
  Quote the measured hit list, and state that existing positive oracles win over a conflicting exemption.
- **Fakes:** a fake copies production's limits, gaps included. A fake stricter or looser than production
  lets a test pass for a reason production lacks.
- **Real processes in tests:** a test that starts a real shell or tool resolves it by absolute path and is
  skipped off its target operating system.
- **Language rules** (timers, ambient-context hold points, analyzer-rejected mutants) live in the language
  module's test references; quote the ones that apply into the prompt.
