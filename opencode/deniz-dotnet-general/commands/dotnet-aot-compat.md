---
description: >
  Make .NET projects compatible with Native AOT and trimming by systematically
  resolving IL trim/AOT analyzer warnings. USE FOR: making projects
  AOT-compatible, fixing trimming warnings, resolving IL warnings (IL2026,
  IL2070, IL2067, IL2072, IL3050), adding DynamicallyAccessedMembers
  annotations, enabling IsAotCompatible. DO NOT USE FOR: publishing native AOT
  binaries, optimizing binary size, replacing reflection-heavy libraries with
  alternatives. INVOKES: no tools — pure knowledge skill.
---

Resolve the global OpenCode configuration root as `$XDG_CONFIG_HOME/opencode` when `$XDG_CONFIG_HOME` is set; otherwise use `~/.config/opencode`.
Read `skills/dotnet-aot-compat/BODY.md` under that global root before doing anything else.
Follow that file as this command's full instructions.

Arguments: $ARGUMENTS