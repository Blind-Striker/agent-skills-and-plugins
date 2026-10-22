Read with this skill's `SKILL.md`; this file is part of its instructions.

## Workers

Start workers with `subagent`. This harness reaches several model families natively, so
implementation and second-family review both go through it. When the user's configuration
names an agent for delegated work, such as a routing agent, use it and follow its rules for
`model`; otherwise leave `model` unset.

## Background runs

Start a long worker with `background: true`. A completion is not a check-in: read it and take the
next step.
