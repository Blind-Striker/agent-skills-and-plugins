# GPT Sol

For the coordinator: Sol is a strong implementer once the task is fixed, and weak at design and
decisions. Give it a decided task, keep every choice between designs with you, and approve its plans
before it builds. Known readings:

- It treats every sentence as binding and stops at the first conflict between two of them, so a
  contradiction in the prompt costs a run.
- It reads a limit as a budget: "at most 6 at once" became "6 in total". Give every number its unit and
  scope.
- It prescribes tooling at plan time (a probe project, a script in another language, an evidence
  document) and then builds it faithfully. Cut those items when you approve the plan.
- It overbuilds new code: hand-written parsers, defensive code over its own inputs.

The worker block in [sol-worker.md](sol-worker.md) goes to the worker verbatim.
