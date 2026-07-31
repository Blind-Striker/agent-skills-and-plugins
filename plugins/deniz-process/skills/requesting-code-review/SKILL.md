---
name: requesting-code-review
description: Use when completing a task, finishing a feature, before merging, or
  when asked to review changes since a commit, branch or tag — pins the range,
  then reports Standards and Spec separately
---

# Requesting Code Review

Dispatch a code reviewer subagent to catch issues before they cascade. The reviewer gets precisely
crafted context for evaluation — never your session's history.

**Core principle:** Review early, review often.

The review runs on **two axes**, and they are never merged:

- **Standards** — does the code conform to this repo's documented standards, and to the smell
  baseline the reviewer template carries?
- **Spec** — does the code faithfully implement what was actually asked for?

A change can pass one and fail the other, which is the whole reason to keep them apart. See
[Why two axes](#why-two-axes).

## When to Request Review

**Mandatory:**
- After each task in subagent-driven development
- After completing major feature
- Before merge to main

**Optional but valuable:**
- When stuck (fresh perspective)
- Before refactoring (baseline check)
- After fixing complex bug

**On request:** "review the branch", "review since `main`", "review what changed since that tag".
Same process — the fixed point is whatever the human named.

## 1. Pin the Range

Every review is a diff between `HEAD` and a fixed point. Get it from the human if they named one,
otherwise derive it:

```bash
BASE_SHA=$(git merge-base main HEAD)   # a branch; or HEAD~1, a tag, a task's starting commit
HEAD_SHA=$(git rev-parse HEAD)
git log --oneline $BASE_SHA..$HEAD_SHA
```

**Confirm the range before dispatching anything:** the ref resolves (`git rev-parse`) and the diff
is non-empty. A bad ref should fail here, in one place, not inside a subagent that has already
burned its context discovering it.

For a branch review prefer the three-dot form (`git diff BASE...HEAD`) so the comparison is against
the merge-base rather than against whatever `main` has since collected.

## 2. Find the Spec

What the change was *supposed* to do, in this order:

1. **The plan or task**, when one is driving the work — in subagent-driven development this is the
   plan file, and it is the spec. Stop here.
2. **Issue references in the commit messages** (`#123`, `Closes #45`) — fetch them the way this
   repo's issue-tracker notes describe. If that convention has not been set up here, tell the human
   to open `/deniz-process:setup-matt-pocock-skills` rather than guessing at a tracker.
3. **A path the human passed.**
4. **A spec or PRD** under `docs/`, `specs/` or `.scratch/` matching the branch or feature.
5. **Ask.** If they say there is no spec, the Spec axis reports "no spec available" — it does not
   quietly grade the change against your own assumptions.

## 3. Find the Standards

Whatever this repo documents about how code should be written — `CONTRIBUTING.md`,
`CODING_STANDARDS.md`, the agent contract, a house style file. Pass the paths to the reviewer.

You do **not** need to supply a baseline: [code-reviewer.md](code-reviewer.md) carries a fixed set
of code smells that applies even when a repo documents nothing. Two rules bind it, and the template
states them to the reviewer: **the repo overrides** — a documented standard always wins, and where
it endorses something the baseline would flag, the smell is suppressed; and **every baseline smell
is a judgement call**, never a hard violation.

## 4. Dispatch

Fill the template at [code-reviewer.md](code-reviewer.md) and dispatch a `general-purpose`
subagent. **Placeholders:** `{DESCRIPTION}`, `{SPEC}`, `{STANDARDS_SOURCES}`, `{BASE_SHA}`,
`{HEAD_SHA}`.

**One reviewer by default.** It holds both axes and reports them separately.

**Two reviewers when the Spec axis is a genuinely different document** — an issue, a PRD, a spec
file that the Standards sources are not. Then send one message with two `Agent` calls, each getting
the range and only its own axis's material, and neither seeing the other's brief. Isolation is the
point: two long documents in one context is where one axis starts masking the other.

When the plan *is* the requirements — the common case, and every review inside subagent-driven
development — a second subagent would read the same document twice for the same range. Dispatch one.

## 5. Read the Report

Both axes come back under their own headings. **Do not merge or rerank them, and do not pick a
single worst issue across both.** Severity within an axis is the reviewer's job; ordering the two
axes against each other is exactly the masking this separation exists to prevent.

A merge verdict is still one decision — but it must name what each axis contributed to it.

## 6. Act on Feedback

- Fix Critical issues immediately
- Fix Important issues before proceeding
- Note Minor issues for later
- Push back if the reviewer is wrong, with technical reasoning

How to take the feedback — what to accept, what to question, how to push back without
capitulating or getting defensive — is the deniz-process:receiving-code-review skill's job. It
applies to a subagent's findings exactly as it applies to a human's.

## Common Rationalizations

| Excuse | Reality |
|--------|---------|
| "I'll just review the diff myself instead of dispatching a reviewer" | You're the coordinator — reviewing the diff inline burns the context window you need to keep driving the work. Dispatch a reviewer subagent: the diff and the evaluation live in its context, and only the findings come back to you. |
| "The reviewer needs my whole session history to understand the change" | Hand it precisely crafted context, never your session's history. That keeps the reviewer on the work product, not your thought process. |
| "There's no written spec, so I'll review against what I think it should do" | Then you are reviewing your own assumptions and calling it a Spec axis. Say "no spec available" and let the Standards axis carry the review. |
| "Standards findings are nitpicks next to a missing requirement" | That ranking is the masking. Report both; a change can follow every standard and implement the wrong thing. |

## Red Flags

**Never:**
- Skip review because "it's simple"
- Dispatch before confirming the range resolves and is non-empty
- Ignore Critical issues
- Proceed with unfixed Important issues
- Collapse the two axes into one ranked list
- Argue with valid technical feedback

**If reviewer wrong:**
- Push back with technical reasoning
- Show code/tests that prove it works
- Request clarification

## Why two axes

A change can pass one axis and fail the other:

- Code that follows every standard but implements the wrong thing → **Standards pass, Spec fail.**
- Code that does exactly what was asked but breaks the project's conventions → **Spec pass,
  Standards fail.**

Reporting them separately stops one from masking the other.

See template at: [code-reviewer.md](code-reviewer.md)
