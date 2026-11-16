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
- It stops cleanly on the stop conditions a brief names: a validator rule, a naming rule, a budget
  projection, a clone gate.
- Resumed by its service after a restart, it carries its last task on unsupervised, including a
  fix-and-amend loop after its own gate fails; nothing tells it the run has changed. Check after every
  restart whether its session still runs.
- The verification tools it writes count too much: a mutation runner that read any non-zero exit as a
  kill, and a watchdog over a whole generator run that reported a false hang. Read the tool before
  you trust a count it reports.
- On a 1M-context route, one child that spans several checkpoints grows to about 780K tokens of
  context and repeats the same check dozens of times. Give it one checkpoint per child.
- As a contradiction scanner of briefs it regularly finds several valid findings per brief.

The worker block in [sol-worker.md](sol-worker.md) goes to the worker verbatim.
