# Models

Coordinator-only index; never sent to a worker. Each model's profile says what the model is strong
at, how to brief it, and where to look when you review its work. A model's worker block lives in the
file its row names under "Worker file" and goes to that model's worker beside the worker contract,
for the roles in its row. A model or role without a worker file gets only the worker contract, and
you review its first runs more strictly; its profile grows from evidence.

| Model | Profile | Worker file | Roles | Cost (relative) | Review focus |
|---|---|---|---|---|---|
| Claude | none | none | coordinator, design, research, final review | not measured | the coordinator's own judgement: check its plan against the request |
| GPT Sol | `sol.md` | `sol-worker.md` | implementer, planner under an approved plan, second reviewer | 1x | overbuilding in new code; relocation that loses an error path; stale "passed" lines |
| GPT Sol Fast | `sol.md` | `sol-worker.md` | as GPT Sol | not measured against 1x; a review about $0.2-0.5 | as GPT Sol; also a gate step reported done that was skipped |
| GPT Astra | `astra.md` | none | coordinator only, never a worker | high | as a coordinator: drift from the request, decisions taken without the user |
| Grok | none | `grok-worker.md`, review tasks only | reviewer only (concurrency) | about 8-9x | its "can't happen" verdicts: check them, never take them as final |

The route that reaches each model is configuration, not part of this index.

Effort: on the measured cases a higher effort setting added cost and found nothing new; a different
model family did. Prefer a second family over more effort.

Not recommended as reviewers on current evidence: models that cleared defective code as sound in a
calibration run (keep the list in the research note, not here).
