# Resuming From an Existing Ledger

Part of deniz-process.subagent-driven-development. This plan's workspace
already has a ledger at `<workspace>/progress.md`.

If the ledger's first line names your plan file, tasks with a `Task <N>:
complete` line are DONE — do not re-dispatch them; resume at the first
task without one. A task whose last line is a fix round is mid-loop:
resume the loop at the next round. A ledger whose first line names a
different plan file — or a stray ledger at the old flat path
`.superpowers/sdd/progress.md` — is another plan's progress: leave it in
place and start your own, fresh.

An existing ledger's `Executor:` line is the workflow this plan runs. If
it names deniz-process.executing-plans, continue with that skill instead,
unless your human partner chose this one now. A ledger with no
`Executor:` line keeps the executor that loaded it; add the line. A
resumed plan is never re-sized: SKILL.md's size gate does not apply to it.
