# Repository addendum template

Each repository where an agent coordinates workers keeps one short addendum next to its binding rules.
The skill reads it; repository truth stays in the repository.

| Field | Content |
|---|---|
| Load line (required) | "Before you write a worker prompt or review worker output, load `deniz-coordination:delegating-to-worker-models`." Put it in the repository's agent instructions too. |
| Worker mechanism | the default mechanism when the user names none: the host's own subagents, a cross-harness subagent, or a dispatch tool with its command and configuration |
| Model profiles | the profile list this repository's work uses, such as `deniz-coordination:model-profiles`, or "none" |
| Binding rules | what the repository declares binding: its agent instructions, its contributing rules, its decision records, and any reference implementation it follows. A question these settle is not the user's to decide, except a change to what done means, to a public API, or a reserved action |
| Report language | the language worker reports are written in; English when none is named |
| Merge path | an integration branch, or one change per task from a pinned base through a merge queue |
| Work type | new code, refactor, or both |
| Walls | the repository's own gates and how to run them; or "none" (the preflight then brings ephemeral walls) |
| Constructs | expected and forbidden constructs |
| Inputs owned elsewhere | where desired values live, and how a task receives them |
| Output semantics | the output contract a task must honour |
| Environment | runtime floor and edges, operating systems, preparation steps, long-command timeouts |
| Verify commands | exact commands; the runtimes they must also pass on; categories excluded so the exit code means something |
| Test rules | language or repository rules to quote into test tasks |
| Repository dodges | additions to the contract's forbidden dodges |
| No-go areas | files, services, or data no worker touches |
