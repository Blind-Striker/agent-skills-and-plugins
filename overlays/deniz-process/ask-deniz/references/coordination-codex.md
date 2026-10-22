# Coordination tools in this harness

## Dispatch harness and verified waves

Applies only when the `deniz-coordination` Module is installed.

| Skill | For |
|---|---|
| `deniz-coordination:harness` | Running worker models out of process through OpenCode or Codex: task files, runs, asks, settlement, the ledger. It runs when you ask for it or start the coordinator. |
| **`/deniz-coordination:verified-waves`** | A task big enough to split: it plans the fan-out in chat, runs it as waves of sub-agents, sends every finding to a fresh skeptic, and reports N of M done. You start it. |
