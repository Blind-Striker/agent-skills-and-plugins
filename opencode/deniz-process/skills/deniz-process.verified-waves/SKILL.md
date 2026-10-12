---
name: deniz-process.verified-waves
description: Run one task as verified multi-agent waves. Plans the fan-out,
  sends findings to fresh skeptics, keeps a ledger, and reports N of M done;
  small tasks stay solo.
metadata:
  opencode/autoinvoke: false
---

# Verified Waves

You run one task as waves of sub-agents. You plan, dispatch, record, and verify; the children do
the per-item work. One ledger records the run, every child gets a self-contained brief and returns a
fixed report, and every finding that matters goes to a fresh skeptic before you report it.

## What invoking this means

- The user invoked this skill, so this is their explicit request for parallel sub-agents on this
  task.
- If the invocation carries no task, ask for the task and stop there: start no child and create no
  ledger until the user answers.
- Read `references/harness-tools.md` first. It names this harness's tools for starting, waiting on,
  continuing, and closing children, says where the ledger lives, and says which permission prompts
  to expect.
- Then apply the small-task test. The task is **small** when it is one item (one edit or one
  question, in one place), or when each step needs the previous step's result. For a small task,
  say that it is small and why, and do it directly: no plan, no ledger, no child. Otherwise, list
  the independent items and fan out.

## Plan before any dispatch

Write the plan in chat before the first child starts:

1. **Goal**: one sentence.
2. **Done when**: binary checks, each one PASS or FAIL when it is run.
3. **Stop when**: the conditions that end the run early, such as a failed gate or the agent total
   reached.
4. **Shape**: one from the next section.
5. **Items**: each with an id, a kind (read | write | verify | gate | critic), and its write set
   (`none` for a read). An id is a lowercase letter, then lowercase letters, digits, or `_`, at most
   32 characters, such as `audit_04`.
6. **Waves**: which items run in which wave, the agent total with skeptics, gates, and critics
   counted, and the ceiling on live children: 6, or the harness limit when that is lower.
7. **Verification plan**: which findings get skeptics, and which checks each gate runs.

The agent total you announce is your budget, together with any limit the user sets. Set no other
limit and no deadline.

After you write the plan, start the first wave in the same turn; wait for the user only where a
yes/no question below applies.

Ask one yes/no question, and wait for the answer, before each of these: deleting or overwriting
files, a force-push, a publish, anything that touches credentials, a broad codemod, and going over
the announced agent total.

## Pick the shape

Pick the one dominant shape before you write briefs:

- **Sweep**: read-only children cover disjoint areas, and their findings are the output.
- **Fan-out then synthesis**: children gather, and one agent merges what they found.
- **Review, verify, fix**: reviewers find, skeptics test each finding, writers fix what survives.
- **Build waves with gates**: writer waves on disjoint write sets, with a gate child after each.
- **Draft and critic**: one child drafts, a fresh child critiques, and the drafter revises.
- **Judge panel**: several children answer one question independently, and one judge compares.

When the raw results are the deliverable, the run ends at the fan-out, with no synthesizer. Ranking
or merging needs one agent that sees every candidate. Verify each item as soon as its producer
reports, not after the whole wave.

## The ledger

The ledger is one Markdown file, `<ledger-dir>/ledger.md`. `<ledger-dir>` is
`verified-waves/<run-id>/` under the location that `references/harness-tools.md` names, outside the
working tree; `<run-id>` is short and unique, such as the date, the time, and a slug. Before you
create `<ledger-dir>`, check that it does not exist. If it does, pick another run-id, for example by
adding a counter: a new run never writes into an existing ledger directory, and only a restart of
the same run continues in its own. Tell the user the path. The ledger is never committed.

Create the ledger and its `results/` directory before the first dispatch, so any permission prompt
for that location comes up once, before children run.

The ledger holds:

- A header: goal, done-when checks, stop-when conditions, waves, agent total, and ceiling.
- One row per item, under this header:

  ```
  | id | kind | write set | status | handle | result path |
  ```

  `handle` is the harness's identifier for the child. The status is one of `PENDING`, `RUNNING`,
  `DONE`, `PARTIAL`, `BLOCKED`, `FAILED`, or `UNVERIFIED`.
- An append-only log: one line for each dispatch, result, retry, verdict, and cap, with its item id.

Update the ledger at each event, not at the end.

## Briefs and reports

A child starts without your context. Give each child one brief in this form, every field filled:

```
ITEM <id> — read | write | verify | gate | critic
GOAL: <one action>. The parent will use your result to <decision>.
CONTEXT: <exact paths, prior findings; shared contract pasted unchanged>
SCOPE: read-only | write only: <paths>   (either way, also your results file)
DO: <musts>   DO NOT: commit, push, edit outside scope, weaken/delete tests, ask the user
[writers] Other agents edit other files now; do not revert or reformat changes you did not make.
DONE WHEN: <binary observable check>
LAST STEP: write <ledger-dir>/results/<id>.md: first the REPORT block you return, unchanged, then any detail past 40 lines
RETURN: the report below, ≤40 lines
Cannot proceed → STATUS: BLOCKED with the exact question.
```

A skeptic's brief replaces GOAL and CONTEXT with `CLAIM: <finding + evidence>` and
`LENS: reproduce | contract`. The skeptic's report adds `VERDICT: CONFIRMED | REFUTED` with its
evidence, and it writes its results file the same way.

Every child returns this report:

```
REPORT <id>
STATUS: DONE | PARTIAL | BLOCKED | FAILED
SUMMARY: ≤5 lines
FINDINGS: - [high|med|low] <claim> — <file:line | command → output> — confidence <h|m|l>
FILES CHANGED: <paths|none>   CHECKS: <cmd> → PASS | FAIL | NOT RUN (<reason>)
OPEN: <questions|none>        RESULTS: <ledger-dir>/results/<id>.md
```

**The results file.** As its last step, every child writes `<ledger-dir>/results/<id>.md`: first
the same `REPORT <id>` block it returns, unchanged, then any detail past 40 lines. A file of detail
alone is not a results file. That file is the only one a child writes outside its write set, so
"read-only" in a brief means read-only except that file. The results files are what let you rebuild
the ledger from disk.

## Running waves

- Scout only until you can list the items, then delegate the per-item work.
- While children run, do only work that does not overlap theirs. A search you delegated stays
  delegated.
- Keep the live children at or under the ceiling, and count every pending child: skeptics, gates,
  and critics too. Size each wave to the independent items you have, not to the ceiling.
- Writers run in parallel only on disjoint write sets. Overlapping writers run in sequence, unless
  the harness gives each writer an isolated copy of the tree.
- After each write wave, one gate child runs the project's checks and reports each as PASS or FAIL.
- A frozen shared contract, pasted unchanged into every brief, decides which side of a seam is
  wrong.
- Every test keeps its strength: no child weakens or deletes a test, and no child commits.
- "Read-only" is a rule the brief carries. An agent type does not enforce it in every harness.

## Collecting results

- Key each result by its item id, never by its position.
- A result with no well-formed `REPORT <id>` block is FAILED under its id: an empty result, an
  error, a cancellation, and a missing value alike. Record it before you filter or count anything.
- A results file that does not start with `REPORT <id>` is incomplete. Log it, then put the
  well-formed report the child returned at the top of that file, above the child's detail.
- Deduplicate each new finding against everything seen so far, refuted findings included, not only
  against what survived.
- Report coverage as N of M items DONE, and name every item that is not DONE with its status.
- An item whose child failed gets a retry or a report of the failure, never a result you wrote.
- State every cap you applied: a top-N cut, sampling, a skipped area, a shortened wave.

## Verifying

- Send each material finding to a fresh skeptic: a new child, never a continuation of the producer.
  The skeptic tries to refute the finding.
- A high-severity finding, or one an edit will act on, gets two skeptics: one with the lens
  *reproduce* and one with the lens *contract*.
- A refutation needs evidence; doubt alone refutes nothing. A finding is CONFIRMED only when no
  skeptic refutes it.
- When a skeptic fails, the finding is UNVERIFIED: report it so, neither confirmed nor dropped.
- Refuted findings stay in the final report, with their reasons.
- Verifiers and gates run the checks themselves. A worker's account of a check is input, not proof.
- An audit, or a task to find every instance, ends with one fresh completeness critic. It gets the
  scope and the ledger and names missed areas, unverified claims, and unread files. Its gaps get at
  most one more wave.

## Loops and stop conditions

- A discovery loop stops after 2 rounds in a row that find nothing new, or after 3 rounds in all.
- A fix-and-review cycle allows at most 2 re-reviews, each by a fresh reviewer given only the change
  and the open blockers. Only a concern tied to a failing done-when check holds up the work. What
  is still open after the second re-review goes to the user.
- An item that fails twice is BLOCKED.
- Zero findings skip the downstream waves.
- A stop-when condition from the plan ends the run: go to the final report.

## Failures and fallback

- Retry a failed item once, as a new child with a corrected brief. Continue the same child instead
  only when the failure was a misunderstanding.
- On a permission denial, a refused launch included, stop that path and report it. A refusal is
  never a reason to start children another way.
- If children cannot start at all, or the depth limit stops them, say so. Then run the briefs one by
  one yourself, label each output with its item id, write each results file, and state in the final
  report that the verification was not independent.

## Restart

After a crash, a lost context, or a new session, rebuild the run from the ledger; never start it
again from scratch silently.

1. Re-read the ledger.
2. Keep the DONE items.
3. For an item that is not DONE but whose results file holds a complete report, take that report
   and record its status.
4. For a RUNNING item without one, try the harness's own continuation first, as
   `references/harness-tools.md` describes.
5. For a write item, inspect `git status` and `git diff` on its write set before you dispatch it
   again: its work may already be on disk.
6. Dispatch the rest again.
7. Tell the user what you reused and what is running again.

A restart is at-least-once, not exactly-once: an item can run twice.

## Final report

Close or finish every child first. Then report:

- the outcome, against each done-when check;
- the count and the names for each status: DONE, PARTIAL, BLOCKED, FAILED, and UNVERIFIED;
- the files changed;
- each check as PASS, FAIL, or NOT RUN, with the reason for every FAIL and NOT RUN;
- the refuted findings, with their reasons;
- every cap you applied;
- the remaining risk;
- the ledger path.
