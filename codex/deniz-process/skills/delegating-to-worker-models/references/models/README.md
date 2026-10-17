# Worker models

Coordinator-only index; never sent to a worker. The dispatcher may read the mapping below to choose the
model file it prepends. A model without a profile gets only the worker contract, and the coordinator
reviews its first runs more strictly; its file grows from evidence.

| Model | Route | File | Roles | Cost (relative) | Review focus |
|---|---|---|---|---|---|
| GPT Sol | `openai/gpt-6.1-sol-1m#high` | `sol.md` | implementer, planner, reviewer | 1x | overbuilding in new code; relocation that loses an error path; stale "passed" lines |
| Grok | `xai/grok-4.7#high` | `grok.md` | reviewer only (concurrency) | about 8-9x | its "can't happen" verdicts: check them, never take them as final |

Effort: on the measured cases a higher effort setting added cost and found nothing new; a different
model family did. Prefer a second family over more effort.

Not recommended as reviewers on current evidence: models that cleared defective code as sound in a
calibration run (keep the list in the research note, not here).
