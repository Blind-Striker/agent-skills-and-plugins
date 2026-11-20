# Coordination tools in this harness

## Dispatch harness and verified waves

Applies only when the `deniz-coordination` Module is installed.

| Skill | For |
|---|---|
| `deniz-coordination:harness` | Running worker models out of process through OpenCode or Codex using task files, run records, and a ledger. It runs when you or a coordinator you started asks for workers. |
| **`/deniz-coordination:verified-waves`** | A task big enough to split. It plans the fan-out in chat, runs waves of subagents, sends every finding to a fresh skeptic, and reports how many items are done. You start it. |
