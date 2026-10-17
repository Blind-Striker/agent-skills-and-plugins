dotnet-test-oracles v1

# Oracle rules for .NET tests

Rules for tests whose oracle depends on time, concurrency, process boundaries, or a mutant. Each rule
closes a way a test passes while the code under it is wrong, or fails for a reason unrelated to it.
A repository's own instructions win over this file.

## Time

- With a manual time provider (`FakeTimeProvider`, or the repository's own), wait until the timer for
  the specific bound is armed: the timer due at now + bound. Never wait for "the next created timer";
  another timer can be created first, and the test then reads the wrong one. If the time provider
  cannot report that a timer is armed (`FakeTimeProvider` cannot), the repository adds that seam
  first. Advancing the clock before the bound's timer is armed is a race: the test can pass without
  the bound ever being observed.
- Assert both edges: not fired at bound − ε, fired at the bound. The mutant for a timing bound is "the
  bound removed, signature kept".
- A test fails at its oracle, never by a timeout.

## Concurrency

- Put a hold point on the subject's own path, through a product hook. Never hold through ambient
  context (`AsyncLocal`, `ExecutionContext` flow): it also fires on unrelated continuations.
- Never throw inside an `AsyncLocal` change handler, a cancellation callback, or a timer callback. An
  exception there can crash the whole test host. Record the result and assert on the test thread.
- A randomized race is not an oracle. Add a deterministic case beside it.

## Process and I/O boundaries

- A test regex over trusted input uses `Regex.InfiniteMatchTimeout`: a wall-clock match timeout fires
  on a stalled CI runner and reads like catastrophic backtracking.
- A fixture stream that stands in for a pipe faults its pending reads on `Dispose`, the way a real pipe
  ends them. On .NET Framework, a `ReadLineAsync` without a cancellation token is ended only by that;
  without it, the test waits for an outer timeout and reports a timing artifact.
- A test that starts a real process resolves the executable by absolute path and is skipped off its
  target operating system.

## Assertions and mutants

- Assert the exact selection, or membership by the named element. Never collapse a check into one
  boolean, such as `IsTrue(items.Any(...))`.
- Every mutant compiles. A mutant the analyzers reject counts as a second guard; report it as such. A
  run with the analyzers off is a demonstration, not evidence.
