---
name: test-driven-development
description: Use when implementing any feature or bugfix, before writing
  implementation code — the red-green loop, the seam to test at, and one slice
  at a time
---

# Test-Driven Development (TDD)

## Overview

Write the test first. Watch it fail. Write minimal code to pass.

**Core principle:** If you didn't watch the test fail, you don't know if it tests the right thing.

Two things decide whether the loop produces tests worth keeping, and neither is the loop itself:
**where** you test — the seam — and **how much** you take per cycle — one slice.

**Violating the letter of the rules is violating the spirit of the rules.**

## When to Use

**Always:**
- New features
- Bug fixes
- Refactoring
- Behavior changes

**Exceptions (ask your human partner):**
- Throwaway prototypes
- Generated code
- Configuration files

Thinking "skip TDD just this once"? Stop. That's rationalization.

## The Iron Law

```
NO PRODUCTION CODE WITHOUT A FAILING TEST FIRST
```

Write code before the test? Delete it. Start over.

**No exceptions:**
- Don't keep it as "reference"
- Don't "adapt" it while writing tests
- Don't look at it
- Delete means delete

Implement fresh from tests. Period.

## Seams — Where the Test Goes

A **seam** is the public boundary you observe behavior at without reaching inside: the interface a
caller actually uses. Tests live at seams, never against internals. Code can change entirely; a test
written at a seam shouldn't. It reads like a specification — "user can checkout with valid cart"
names a capability — and survives refactors because it doesn't care about internal structure.

**Name the seam before writing the test.** Every cycle, say which boundary this test observes. If
you cannot name one, you are about to test an internal, and the test will break on the next
refactor while sleeping through real bugs.

**Put the seam to your human partner when the choice is consequential** — several boundaries would
work, the obvious one is expensive or slow, or testing there would drive a design change. You can't
test everything; agreeing the seams is how effort lands on critical paths and complex logic instead
of on every edge case. Ask: "What's the public interface, and which seams should we test?"

A too-shallow seam gives false confidence: it exercises a stand-in rather than the pattern as it
occurs at the call site. **If no correct seam exists, that itself is a finding** — the architecture
is preventing the behavior from being locked down. Say so rather than testing an internal instead.

## One Slice at a Time

One seam, one test, one minimal implementation per cycle. Each test is a **tracer bullet**: it
responds to what the last cycle taught you.

**Never write all the tests first, then all the implementation.** Bulk tests verify *imagined*
behavior — you test the shape of things rather than what a caller experiences, the tests go
insensitive to real changes, and you commit to a test structure before understanding the
implementation you are about to write.

## Red-Green-Refactor

```dot
digraph tdd_cycle {
    rankdir=LR;
    seam [label="SEAM\nName the boundary", shape=box, style=filled, fillcolor="#ffe0b2"];
    red [label="RED\nWrite failing test", shape=box, style=filled, fillcolor="#ffcccc"];
    verify_red [label="Verify fails\ncorrectly", shape=diamond];
    green [label="GREEN\nMinimal code", shape=box, style=filled, fillcolor="#ccffcc"];
    verify_green [label="Verify passes\nAll green", shape=diamond];
    refactor [label="REFACTOR\nClean up", shape=box, style=filled, fillcolor="#ccccff"];
    next [label="Next slice", shape=ellipse];

    seam -> red;
    red -> verify_red;
    verify_red -> green [label="yes"];
    verify_red -> red [label="wrong\nfailure"];
    green -> verify_green;
    verify_green -> refactor [label="yes"];
    verify_green -> green [label="no"];
    refactor -> verify_green [label="stay\ngreen"];
    verify_green -> next;
    next -> seam;
}
```

### RED - Write Failing Test

Write one minimal test showing what should happen, at the seam you just named.

<Good>
```typescript
test('retries failed operations 3 times', async () => {
  let attempts = 0;
  const operation = () => {
    attempts++;
    if (attempts < 3) throw new Error('fail');
    return 'success';
  };

  const result = await retryOperation(operation);

  expect(result).toBe('success');
  expect(attempts).toBe(3);
});
```
Clear name, tests real behavior, one thing
</Good>

<Bad>
```typescript
test('retry works', async () => {
  const mock = jest.fn()
    .mockRejectedValueOnce(new Error())
    .mockRejectedValueOnce(new Error())
    .mockResolvedValueOnce('success');
  await retryOperation(mock);
  expect(mock).toHaveBeenCalledTimes(3);
});
```
Vague name, tests mock not code
</Bad>

**Requirements:**
- One behavior
- Clear name
- Real code (no mocks unless unavoidable)

### Verify RED - Watch It Fail

**MANDATORY. Never skip.**

```bash
npm test path/to/test.test.ts
```

Confirm:
- Test fails (not errors)
- Failure message is expected
- Fails because feature missing (not typos)

**Test passes?** You're testing existing behavior. Fix test.

**Test errors?** Fix error, re-run until it fails correctly.

### GREEN - Minimal Code

Write simplest code to pass the test.

<Good>
```typescript
async function retryOperation<T>(fn: () => Promise<T>): Promise<T> {
  for (let i = 0; i < 3; i++) {
    try {
      return await fn();
    } catch (e) {
      if (i === 2) throw e;
    }
  }
  throw new Error('unreachable');
}
```
Just enough to pass
</Good>

<Bad>
```typescript
async function retryOperation<T>(
  fn: () => Promise<T>,
  options?: {
    maxRetries?: number;
    backoff?: 'linear' | 'exponential';
    onRetry?: (attempt: number) => void;
  }
): Promise<T> {
  // YAGNI
}
```
Over-engineered
</Bad>

Don't add features, refactor other code, or "improve" beyond the test.

### Verify GREEN - Watch It Pass

**MANDATORY.**

```bash
npm test path/to/test.test.ts
```

Confirm:
- Test passes
- Other tests still pass
- Output pristine (no errors, warnings)

**Test fails?** Fix code, not test.

**Other tests fail?** Fix now.

### REFACTOR - Clean Up

After green only, and only what this slice touched:

- Remove duplication
- Improve names
- Extract helpers

Keep tests green. Don't add behavior.

**Structural work is not part of this step.** Reshaping a module, moving a boundary, splitting a
class that grew wrong — that is review's judgement, not the loop's, and doing it here quietly turns
one slice into an unreviewable change. Note it and take it to the
requesting-code-review skill.

### Repeat

Next seam, next failing test, next slice.

## Good Tests

| Quality | Good | Bad |
|---------|------|-----|
| **Minimal** | One thing. "and" in name? Split it. | `test('validates email and domain and whitespace')` |
| **Clear** | Name describes behavior | `test('test1')` |
| **Shows intent** | Demonstrates desired API | Obscures what code should do |
| **At a seam** | Public interface, survives refactors | Private methods, internal collaborators, call counts |

When writing or changing any test, read [writing-good-tests.md](writing-good-tests.md) for the rules
that keep tests honest:
- Name the production change that would make the test fail — before writing it
- Assert on real behavior, never on mock behavior
- Derive expected values independently, never with the code under test
- Mock at system boundaries only; keep what the test depends on real
- Keep test-only code in test utilities, out of production classes

## Common Rationalizations

| Excuse | Reality |
|--------|---------|
| "Too simple to test" | Simple code breaks. Test takes 30 seconds. |
| "I'll test after" | Tests written after pass immediately — which proves nothing. They may test the wrong thing, test the implementation instead of the behavior, or miss the edge case you forgot. You never watched it fail, so you never proved it can catch the bug. Test-first forces that failure. |
| "Tests after achieve same goals (spirit not ritual)" | Tests-after answer "what does this do?"; tests-first answer "what should this do?" Tests written after are biased by the code you already wrote — you verify the cases you remembered, not the ones you'd have discovered. Coverage without proof the tests work. |
| "Already manually tested" | Manual testing is ad-hoc: no record of what you covered, no way to re-run it when the code changes, easy to forget cases under pressure. "Worked when I tried it" ≠ comprehensive. Automated tests run the same way every time. |
| "Deleting X hours is wasteful" | Sunk cost fallacy — that time is already spent either way. The real choice: rewrite with TDD (high confidence) vs. keep it and bolt tests on after (low confidence, likely bugs). Keeping code you can't trust is the waste. |
| "Keep as reference, write tests first" | You'll adapt it. That's testing after. Delete means delete. |
| "Need to explore first" | Fine. Throw away exploration, start with TDD. |
| "Test hard = design unclear" | Listen to test. Hard to test = hard to use. |
| "TDD will slow me down" | TDD IS the pragmatic path: catches bugs before commit, prevents regressions, lets you refactor without fear. "Pragmatic" shortcuts mean debugging in production — slower, not faster. |
| "Manual test faster" | Manual doesn't prove edge cases. You'll re-test every change. |
| "Existing code has no tests" | You're improving it. Add tests for existing code. |
| "I'll write all the tests, then implement" | Bulk tests verify imagined behavior. One slice at a time. |
| "No clean seam, I'll test the internal instead" | The missing seam is the finding. Say so; don't bury it in a brittle test. |

## Red Flags - STOP and Start Over

- Code before test
- Test after implementation
- Test passes immediately
- Can't explain why test failed
- Can't name the seam the test observes
- Writing a batch of tests before any implementation
- Restructuring a module inside the REFACTOR step
- Tests added "later"
- Rationalizing "just this once"
- "I already manually tested it"
- "Tests after achieve the same purpose"
- "It's about spirit not ritual"
- "Keep as reference" or "adapt existing code"
- "Already spent X hours, deleting is wasteful"
- "TDD is dogmatic, I'm being pragmatic"
- "This is different because..."

**All of these mean: Delete code. Start over with TDD.**

## Example: Bug Fix

**Bug:** Empty email accepted

**SEAM** — the form submission interface callers use, not the validator inside it.

**RED**
```typescript
test('rejects empty email', async () => {
  const result = await submitForm({ email: '' });
  expect(result.error).toBe('Email required');
});
```

**Verify RED**
```bash
$ npm test
FAIL: expected 'Email required', got undefined
```

**GREEN**
```typescript
function submitForm(data: FormData) {
  if (!data.email?.trim()) {
    return { error: 'Email required' };
  }
  // ...
}
```

**Verify GREEN**
```bash
$ npm test
PASS
```

**REFACTOR**
Extract validation for multiple fields if needed — inside this slice only.

## Verification Checklist

Before marking work complete:

- [ ] Every new behavior has a test at a named seam
- [ ] Watched each test fail before implementing
- [ ] Each test failed for expected reason (feature missing, not typo)
- [ ] Wrote minimal code to pass each test
- [ ] One slice per cycle — no batch of tests written ahead of implementation
- [ ] All tests pass
- [ ] Output pristine (no errors, warnings)
- [ ] Tests use real code (mocks only if unavoidable)
- [ ] Edge cases and errors covered

Can't check all boxes? You skipped TDD. Start over.

## When Stuck

| Problem | Solution |
|---------|----------|
| Don't know how to test | Write wished-for API. Write assertion first. Ask your human partner. |
| Can't find a seam | Say so — it is a finding about the design, not a reason to test an internal. |
| Test too complicated | Design too complicated. Simplify interface. |
| Must mock everything | Code too coupled. Use dependency injection. |
| Test setup huge | Extract helpers. Still complex? Simplify design. |

## Debugging Integration

Bug found? Don't start here — start with the systematic-debugging skill, which builds
the red signal first and finds the root cause. It comes back to this skill for the regression test,
at a seam that exercises the real bug pattern. Never fix bugs without a test.

## Final Rule

```
Production code → test exists and failed first
Otherwise → not TDD
```

No exceptions without your human partner's permission.
