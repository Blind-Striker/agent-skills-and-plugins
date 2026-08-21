---
description: Generate an interactive bash wizard that walks a human through
  steps only they can perform. Use when provisioning infrastructure, setting up
  credentials or CI secrets, walking an unfamiliar third-party dashboard, or
  running a one-off migration or cutover. Don't invoke this for steps the agent
  can perform itself.
---

Resolve the global OpenCode configuration root as `$XDG_CONFIG_HOME/opencode` when `$XDG_CONFIG_HOME` is set; otherwise use `~/.config/opencode`.
Read `skills/wizard/BODY.md` under that global root before doing anything else.
Follow that file as this command's full instructions.

Arguments: $ARGUMENTS