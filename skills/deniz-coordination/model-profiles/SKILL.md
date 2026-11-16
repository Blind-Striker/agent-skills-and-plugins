---
name: model-profiles
description: Use when the user, a brief, or the repository addendum names these model profiles for delegated work. Says which model takes which role, how to brief it, the worker block it gets, and where to look when you review its work.
---

# Model profiles

One curator's profiles of the models they delegate to, from field runs. They take effect only when the
user, a brief, or the repository addendum names them for the work; loading this file is not that
request. `deniz-coordination:delegating-to-worker-models` holds the method; this file only fills in
its model slots.

Coordinator-only; never sent to a worker. Each model's profile says what the model is strong at, how
to brief it, and where to look when you review its work. A model's worker block lives in the file its
row names under "Worker file" and goes to that model's worker beside the worker contract, for the
roles in its row. A model or role without a worker file gets only the worker contract, and you review
its first runs more strictly; its profile grows from evidence.

| Model | Profile | Worker file | Roles | Cost (relative) | Review focus |
|---|---|---|---|---|---|
| Claude | none | none | coordinator, design, research, final review, implementer (not measured) | not measured | the coordinator's own judgement: check its plan against the request |
| GPT Sol | `references/sol.md` | `references/sol-worker.md` | implementer, planner under an approved plan, second reviewer | 1x | overbuilding in new code; relocation that loses an error path; stale "passed" lines |
| GPT Sol Fast | `references/sol.md` | `references/sol-worker.md` | as GPT Sol | metered against a subscription quota, so no money figure; one three-checkpoint child: 534 model calls, 238M input tokens, 99.4% of them cached | as GPT Sol; also a gate step reported done that was skipped |
| GPT Astra | `references/astra.md` | none | coordinator only, never a worker | high | as a coordinator: drift from the request, decisions taken without the user |
| Grok | none | `references/grok-worker.md`, review tasks only | reviewer only (concurrency) | about 8-9x | its "can't happen" verdicts: check them, never take them as final |

The route that reaches each model is configuration, not part of this index, and the mechanism decides
which models a task can use: the index ranks, it does not route.

Effort: on the measured cases a higher effort setting added cost and found nothing new; a different
model family did. Prefer a second family over more effort.

Not recommended as reviewers on current evidence: models that cleared defective code as sound in a
calibration run (the list is in the research note, not here).
