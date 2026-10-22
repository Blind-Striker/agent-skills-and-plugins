# GPT Astra

Coordinator only; never a worker, because its cost is too high for implementation work. It may
coordinate when the usual coordinator is unavailable.

For whoever starts an Astra session as coordinator:

- Start it at high effort; use the highest setting only for hard design work. A coordinator at medium
  effort drifted from the user's request in the field.
- Start it with `/deniz-coordination:coordinator`, so the goal rules load: the verbatim request, labelled decisions, and
  the phase-boundary check. Its known failure is restating the goal in its own words and then grading
  every check against that restatement.
- Configure the default child model for its subagents, so a child never inherits Astra.

For its reviewer: check its plan and its final report against the user's own words, and check every
decision it took without the user against the coordinator's reserved list.
