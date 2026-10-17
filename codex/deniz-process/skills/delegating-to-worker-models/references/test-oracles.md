# Test oracles: what the coordinator specifies

The worker contract carries the generic oracle rules. This file is for the coordinator: what each test
task's prompt must name so the worker cannot pick a weak oracle.

- **Name the mutant class** for every new bound ("a no-op that sends nothing", "a sort by the wrong key").
  The mutant is the true no-op: a valid success shape with no effect.
- **Assertion-strengthening tasks:** require proof that the mutant survives on the base and is killed
  after.
- **Order fixtures:** require entries that a wrong key would reorder.
- **Refusals:** require the full ordered error list unless the plan names a filter; a filter hides
  cascades.
- **Races:** a randomized race is not an oracle; require a deterministic case beside it.
- **Expected values:** never computed with the helper under test.
- **Existing tests are oracles:** a change to an existing test's expected result needs the plan or your
  amendment; the worker reports every such edit old -> new.
- **Real processes in tests:** a test that starts a real shell or tool resolves it by absolute path and is
  skipped off its target operating system.
- **Language rules** (timers, ambient-context hold points, analyzer-rejected mutants) live in the language
  module's test references; quote the ones that apply into the prompt.
