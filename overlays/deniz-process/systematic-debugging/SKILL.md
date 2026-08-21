---
name: systematic-debugging
description: Use when encountering any bug, test failure, performance regression, or unexpected behavior, before proposing fixes
---

# Systematic Debugging

## Overview

**Core principle:** build a feedback loop, then find the root cause — no fixes before both.
Symptom fixes are failure. A tight pass/fail signal for the bug is what bisection, hypotheses,
and instrumentation all consume; without one, no amount of staring at code will save you.

**Violating the letter of this process is violating the spirit of debugging.**

## The Iron Law

```
NO FIXES WITHOUT A RED FEEDBACK LOOP AND A ROOT CAUSE
```

If you haven't completed Phases 1–3, you cannot propose fixes.

## When to Use

Use for ANY technical issue: test failures, bugs in production, unexpected behavior, performance
regressions, build failures, integration issues.

**Use this ESPECIALLY when:**

- Under time pressure (emergencies make guessing tempting)
- "Just one quick fix" seems obvious
- You've already tried multiple fixes
- The previous fix didn't work
- You don't fully understand the issue

**Don't skip when:** the issue seems simple (simple bugs have root causes too), you're in a hurry
(systematic is faster than thrashing), or someone wants it fixed NOW.

## Redact

This skill has you show commands, outputs and captured artifacts. **Redact every secret first**:
write `<REDACTED>` in its place. Build loops against env vars, so the credential stays in the
environment rather than in what you show. Captured artifacts carry auth headers: quote only the
lines that carry the signal.

If the redacted output is not enough to diagnose the bug, say so and ask your human partner.

## Phase 1 — Build a Feedback Loop

**This is the skill. Everything else is mechanical.** If you have a **tight** pass/fail signal
that goes red on *this* bug, you will find the cause. If you don't, stop — build one first.

Spend disproportionate effort here. Be aggressive. Be creative. Refuse to give up.

### Ways to construct one — in roughly this order

1. **Failing test** at whatever seam reaches the bug — unit, integration, e2e.
2. **Curl / HTTP script** against a running dev server.
3. **CLI invocation** with a fixture input, diffing stdout against a known-good snapshot.
4. **Headless browser script** (Playwright / Puppeteer) — drives the UI, asserts on
   DOM/console/network.
5. **Replay a captured trace.** Save a real request / payload / event log to disk; replay it
   through the code path in isolation.
6. **Throwaway harness.** Spin up a minimal subset of the system (one service, mocked deps) that
   exercises the bug code path with a single call.
7. **Property / fuzz loop.** If the bug is "sometimes wrong output", run 1000 random inputs and
   look for the failure mode.
8. **Bisection harness.** If the bug appeared between two known states (commit, dataset,
   version), automate "boot at state X, check, repeat" so you can `git bisect run` it.
9. **Differential loop.** Run the same input through old vs new version (or two configs) and
   diff outputs.
10. **Human-in-the-loop script.** Last resort. If a human must click, drive *them* with a
    structured checklist script whose captured output feeds back to you — still a loop, just a
    slow one.

### Tighten the loop

Treat the loop as a product:

- **Faster** — cache setup, skip unrelated init, narrow the scope.
- **Sharper** — assert on the specific symptom, not "didn't crash".
- **More deterministic** — pin time, seed RNG, isolate filesystem, freeze network.

A 30-second flaky loop is barely better than no loop; a 2-second deterministic one is a
debugging superpower.

### Non-deterministic bugs

The goal is not a clean repro but a **higher reproduction rate**. Loop the trigger 100×,
parallelise, add stress, narrow timing windows, inject sleeps. A 50%-flake is debuggable; 1% is
not — keep raising the rate until it is.

### When you genuinely cannot build a loop

Stop and say so explicitly. List what you tried. Ask your human partner for (a) access to the
environment that reproduces it, (b) a redacted captured artifact (HAR file, log dump, core dump,
recording with timestamps), or (c) permission to add temporary production instrumentation.
Do **not** proceed to hypothesise without a loop.

### Completion criterion — one command that goes red

Phase 1 is done when you can name **one command** — a script path, a test invocation, a curl —
that you have **already run at least once** (show the invocation and its output, redacted), and that is:

- [ ] **Red-capable** — it drives the actual bug code path and asserts the **user's exact
      symptom**, so it goes red on this bug and green once fixed.
- [ ] **Deterministic** — same verdict every run (flaky bugs: a pinned, high reproduction rate).
- [ ] **Fast** — seconds, not minutes.
- [ ] **Agent-runnable** — you can run it unattended.

If you catch yourself reading code to build a theory before this command exists, **stop —
jumping straight to a hypothesis is the exact failure this skill prevents.**

## Phase 2 — Reproduce and Minimise

Run the loop. Watch it go red. Confirm:

- [ ] The failure is the one the **user** described — not a different failure that happens to be
      nearby. Wrong bug = wrong fix.
- [ ] It reproduces across multiple runs (or at a high enough rate to debug against).
- [ ] You captured the exact symptom (error message, wrong output, timing) so later phases can
      verify the fix addresses *it*.

Then shrink the repro to the **smallest scenario that still goes red**: cut inputs, callers,
config, data, and steps **one at a time**, re-running the loop after each cut. Done when **every
remaining element is load-bearing** — removing any one makes the loop go green. A minimal repro
shrinks the hypothesis space and becomes the regression test later.

## Phase 3 — Investigate the Root Cause

**Before forming any hypothesis:**

1. **Read error messages carefully.** Complete stack traces, line numbers, error codes — they
   often contain the exact answer.
2. **Check recent changes.** Git diff, recent commits, new dependencies, config changes,
   environmental differences.
3. **Gather evidence in multi-component systems.** When the path crosses components
   (CI → build → signing, API → service → database), instrument each boundary — log what enters
   and exits, verify config propagation — and run once to see WHERE it breaks before asking why.
4. **Trace data flow.** Where does the bad value originate? What called this with it? Keep
   tracing up to the source — see `root-cause-tracing.md` in this directory. Fix at the source,
   not at the symptom.
5. **Find the pattern.** Locate similar working code in the same codebase; if implementing a
   reference pattern, read it COMPLETELY. List every difference between working and broken,
   however small — don't assume "that can't matter".

## Phase 4 — Hypothesise and Test

Generate **3–5 ranked hypotheses** before testing any. Single-hypothesis generation anchors on
the first plausible idea.

Each hypothesis must be **falsifiable** — state its prediction:

> "If <X> is the cause, then <changing Y> will make the loop go green / <changing Z> will make
> it worse."

If you cannot state the prediction, it's a vibe — discard or sharpen it.

**Show the ranked list to your human partner before testing.** They often re-rank instantly
("we just deployed a change to #3") or have already ruled some out. Don't block on it — proceed
with your ranking if they're away.

Test minimally: the **smallest possible change**, one variable at a time. Didn't work? Form a
NEW hypothesis — don't stack fixes. Don't know? Say "I don't understand X" and investigate —
never pretend to know.

## Phase 5 — Instrument

Each probe must map to a specific prediction from Phase 4. **Change one variable at a time.**

1. **Debugger / REPL inspection** if the environment supports it — one breakpoint beats ten logs.
2. **Targeted logs** at the boundaries that distinguish hypotheses.
3. Never "log everything and grep".

**Tag every debug log** with a unique prefix, e.g. `[DEBUG-a4f2]` — cleanup becomes a single
grep. Untagged logs survive; tagged logs die.

**Performance regressions:** logs are usually the wrong tool. Establish a baseline measurement
(timing harness, profiler, query plan), then bisect. Measure first, fix second.

## Phase 6 — Fix

1. **Regression test first — at a correct seam.** Turn the minimised repro into a failing test
   using the superpowers:test-driven-development skill. A correct seam exercises the real bug
   pattern as it occurs at the call site; a too-shallow seam gives false confidence. **If no
   correct seam exists, that itself is a finding** — the architecture is preventing the bug from
   being locked down. Document it and raise it with your human partner.
2. **Implement a single fix** at the root cause. ONE change; no "while I'm here" improvements,
   no bundled refactoring.
3. **Verify.** Test passes, no other tests broken, and the Phase 1 loop goes green against the
   original (un-minimised) scenario. Use the superpowers:verification-before-completion skill
   before claiming success.
4. **If the fix doesn't work:** STOP and count. Fewer than 3 attempts → return to Phase 3 with
   the new information. **3 or more → question the architecture:**

   Each fix revealing a new problem elsewhere, fixes needing "massive refactoring", every fix
   creating new symptoms — that pattern is not a failed hypothesis, it is a wrong architecture.
   Stop and discuss fundamentals with your human partner before attempting fix #4.

## Phase 7 — Cleanup and Post-Mortem

Required before declaring done:

- [ ] Original repro no longer reproduces (re-run the Phase 1 loop)
- [ ] Regression test passes (or the absence of a correct seam is documented)
- [ ] All `[DEBUG-...]` instrumentation removed (grep the prefix)
- [ ] Throwaway harnesses and prototypes deleted
- [ ] The hypothesis that turned out correct is stated in the commit message — the next
      debugger learns from it

**Then ask: what would have prevented this bug?** If the answer is architectural (no good test
seam, tangled callers, hidden coupling), raise it with your human partner as its own piece of
work — after the fix is in, when you know the most. See `defense-in-depth.md` for adding
validation at multiple layers once the root cause is known.

If systematic investigation reveals the issue is truly environmental or external: document what
you investigated, implement appropriate handling (retry, timeout, error message), add
monitoring. But 95% of "no root cause" cases are incomplete investigation.

## Red Flags — STOP and Follow the Process

If you catch yourself thinking:

- "Quick fix for now, investigate later"
- "Just try changing X and see if it works"
- Reading code to build a theory **before a red-capable command exists**
- "Add multiple changes, run tests"
- "Skip the test, I'll manually verify"
- "It's probably X, let me fix that"
- "I don't fully understand but this might work"
- Proposing solutions before tracing data flow
- **"One more fix attempt" (when already tried 2+)**
- **Each fix reveals a new problem in a different place**

**ALL of these mean: STOP. Return to the phase you skipped.**

## Common Rationalizations

| Excuse | Reality |
|--------|---------|
| "Issue is simple, don't need process" | Simple issues have root causes too. The process is fast for simple bugs. |
| "Emergency, no time for process" | Systematic debugging is FASTER than guess-and-check thrashing. |
| "A loop is overkill, I can eyeball this" | Without a red signal you cannot even prove the bug is fixed. |
| "Just try this first, then investigate" | The first fix sets the pattern. Do it right from the start. |
| "I'll write the test after confirming the fix" | Untested fixes don't stick. Red first proves the test can catch it. |
| "Multiple fixes at once saves time" | You can't isolate what worked, and it causes new bugs. |
| "Reference too long, I'll adapt the pattern" | Partial understanding guarantees bugs. Read it completely. |
| "I see the problem, let me fix it" | Seeing symptoms ≠ understanding the root cause. |
| "One more fix attempt" (after 2+) | 3+ failures = architectural problem. Question the pattern, don't fix again. |

## Quick Reference

| Phase | Key activity | Done when |
|-------|-------------|-----------|
| **1. Loop** | Construct a tight pass/fail signal | One red-capable, deterministic, fast command — already run |
| **2. Reproduce + minimise** | Watch it go red, shrink the repro | Every remaining element is load-bearing |
| **3. Root cause** | Errors, recent changes, evidence, data flow, patterns | You understand WHAT and WHY |
| **4. Hypothesise** | 3–5 ranked, falsifiable predictions | One confirmed by minimal test |
| **5. Instrument** | One probe per prediction, tagged | Hypotheses distinguished by evidence |
| **6. Fix** | Test-first at a correct seam, single fix | Test green, suite green, loop green |
| **7. Cleanup** | Remove instrumentation, post-mortem | Checklist above complete |

## Supporting Techniques

Available in this directory:

- **`root-cause-tracing.md`** — trace bugs backward through the call stack to the original trigger
- **`defense-in-depth.md`** — add validation at multiple layers after finding the root cause
- **`condition-based-waiting.md`** — replace arbitrary timeouts with condition polling
