---
name: model-profiles
description: Use when the user, a brief, or the repository addendum names these
  model profiles for delegated work. Says which model takes which role, how to
  brief it, the worker block it gets, and where to look when you review its
  work.
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

The coding rows are ranked by coding capability, highest first; "Scale" gives the effort setting and
the coding score. The ranking is not a cost order: most rows have no measured cost.

| Model | Scale | Profile | Worker file | Roles | Cost (relative) | Review focus |
|---|---|---|---|---|---|---|
| Gemini 4 Neon | high, 60 | none | none | top-tier coding, review, coordinator; reserved for the hardest work | not measured | first runs, more strictly |
| Claude Opus 5.5 | high, 58 | none | none | design, critique, cross-family review, any task that suits GPT-6.2 Sol at high | not measured | the coordinator's own judgement: check its plan against the request |
| GPT-6.2 Sol | high, 57; `xhigh` for very hard design or review | `references/sol.md` | `references/sol-worker.md` | primary implementer, fix-up, contradiction scan, research, cross-family review, planner under an approved plan | 1x | overbuilding in new code; relocation that loses an error path; stale "passed" lines |
| GPT-6.1 Terra | max, 51 | none | none | lighter and medium coding, deep technical research, economical review | not measured | first runs, more strictly |
| GLM 5.3 | high | none | `references/concurrency-review-worker.md`, concurrency review tasks only (lock, race, or cancellation-lifetime code) | third-family reviewer for concurrency and other high-risk changes, only while its quota lasts | metered against a quota | its "can't happen" verdicts: check them, never take them as final |
| DeepSeek V4.1 Flash | high | none | none | well-defined mechanical tasks and scans only; never a reviewer | not measured | first runs, more strictly |
| GPT Astra | not ranked | `references/astra.md` | none | coordinator-only fallback, never a worker | high | as a coordinator: drift from the request, decisions taken without the user |
| Grok | retired | none | none | none: provider disabled, no credit; GLM 5.3 took over its concurrency review | about 8-9x when it ran | none |

Economy is a routing principle apart from the ranking, and it applies per role within the eligible
rows. Route each implementation task to the most economical row whose roles cover it: routine
implementation never escalates to Gemini 4 Neon or Claude Opus 5.5.

Every review is cross-family: pick the most economical eligible reviewer from a model family other
than the author's, even when it ranks above the author (Claude Opus 5.5 reviews routine GPT-6.2 Sol
code). The economy rule never blocks a required cross-family review. When no eligible reviewer from
another family is reachable, the delegation craft's same-family fallback applies, with the gap noted.

Restricted roles: DeepSeek, Muse, and Luna are never reviewers, and Muse is never an implementer. Add
no route the curator has not approved, and never swap a route silently to save cost.

The route that reaches each model is configuration, not part of this index, and the mechanism decides
which models a task can use: the index ranks, it does not route. The curator's own routes stand apart
in the next section.

Effort: on the measured cases a higher effort setting added cost and found nothing new; a different
model family did. Prefer a second family over more effort.

Not recommended as reviewers on current evidence: models that cleared defective code as sound in a
calibration run (the list is in the research note, not here).

## Curator routing defaults

These routes take effect only when the curator's profiles are named for the work, like the rest of
this file.

- GPT-6.2 Sol, GPT-6.1 Terra, and GPT Astra when it runs as the coordinator fallback: the Codex
  harness (`codex`) on the default service tier; the `priority` tier only when speed is needed.
- Claude Opus 5.5: the Antigravity harness first; the Claude Agent harness when that fails.
- GLM 5.3: OpenCode through `zai-coding-plan/glm-5.3`, only while its quota lasts; never through
  `opencode-go`.
- DeepSeek V4.1 Flash: OpenCode through `opencode-go/deepseek-v4.1-flash`.
