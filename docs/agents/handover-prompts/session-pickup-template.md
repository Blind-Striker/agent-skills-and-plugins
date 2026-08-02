# Session Pickup Prompt — Template

Date: 2026-08-02

> **How to use:** create a pickup only for unfinished cross-session work. Copy this file to
> `s<N>-<short-summary>.prompt.md` (for example, `s1-toolchain-complete.prompt.md`), replace every
> `{{placeholder}}`, consume it against the live repository, and delete it when the follow-up ships.
>
> A pickup is not a status ledger or a policy home. Its body contains only commands for the next
> session to run and deltas to make against `docs/ROADMAP.md`; do not record HEAD, current-state
> snapshots, gate totals, command output, or a policy recap.

---

## Commands to run

List the exact commands needed to establish the live state and continue the scoped work. Do not
paste their output or summarize their results.

1. `git status --short`
2. `git log --oneline -5`
3. `{{scoped inspection or setup command}}`
4. `{{scoped verification command}}`

## Deltas vs `docs/ROADMAP.md`

List only the operational changes to make after the commands above establish them. Link to the
relevant roadmap heading instead of restating repository status.

- `{{ROADMAP heading or item}}`: `{{add, remove, or reword this operational statement}}`
- `{{ROADMAP heading or item}}`: `{{add, remove, or reword this operational statement}}`
