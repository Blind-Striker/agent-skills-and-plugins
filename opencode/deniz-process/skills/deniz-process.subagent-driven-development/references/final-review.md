# Final Review and Finish

Part of deniz-process.subagent-driven-development. Every task in the plan is
complete.

The final whole-branch review gets a package too: run
`bash scripts/review-package PLAN_FILE MERGE_BASE HEAD` by its absolute path,
from the working tree you're executing in (MERGE_BASE = the commit the
branch started from, e.g. `git merge-base main HEAD`) and include the
printed path in the final review dispatch, so the final reviewer reads
one file instead of re-deriving the branch diff with git commands. Dispatch
on the most capable available model (see SKILL.md's Model Selection), using
deniz-process.requesting-code-review's
[code-reviewer.md](../../deniz-process.requesting-code-review/code-reviewer.md). Point it at
the ledger's deferred-minor and parked lines so it can triage which must be
fixed before merge.

Its "Declined to judge" list is yours to triage: every line there is a
ruling you make and ledger — `Final: Ruling: <behavior the reviewer set
aside> — <what a reasonable person using this software gets, and why that
stands or why it is now a finding> — <cost if wrong>`. A line that is now a
finding joins the findings below; nothing on the list is dropped silently.

If the final whole-branch review returns findings, dispatch ONE fix subagent
with the complete findings list — not one fixer per finding — and a
report-file path in the workspace. As in every fix round, it re-runs the
tests covering its fixes and then the project's whole suite, and appends
both runs' output to that report file, which the re-review reads.
Per-finding fixers each rebuild context and re-run suites; a real
session's final-review fix wave cost more than all its tasks combined.
Then run exactly one scoped re-review of the fix wave
(`bash scripts/review-package PLAN_FILE FIX_BASE HEAD` by its absolute path,
from the working tree you're executing in, over the fix range,
[re-review-prompt.md](../re-review-prompt.md)).
Adjudicate any residual findings as in the fix loop's breaker
(references/fix-loop.md): park with rulings, or rule on the load-bearing
ones and ledger what you decided. Only the four things that stop a running
plan (top of SKILL.md) stop you here. There is no second fix wave —
residual load-bearing findings surface to your human partner when
finishing-a-development-branch presents the options.

### Finish

Before you delete anything, collect every ledger line containing `Ruling:` —
preflight rulings, parked findings, breaker adjudications, all of them — into
your final message under "Rulings I made", in the order you made them, each
with what it costs if wrong. The list is exhaustive: if the ledger holds a
ruling, the list holds it. That list is the only place the decisions you
took on your human partner's behalf reach them — they read it and rework
whatever you got wrong. A ruling that dies with the workspace was a decision
made in secret.

When the final whole-branch review is clean and its fixes are merged,
delete this plan's workspace (`rm -rf <workspace>`) — the git history is
the record now. Sibling directories belong to other plans; leave them
alone.

Use deniz-process.finishing-a-development-branch.
