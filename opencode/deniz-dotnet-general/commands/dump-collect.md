---
description: "Configure and collect crash dumps for modern .NET applications.
  USE FOR: enabling automatic crash dumps for CoreCLR or NativeAOT, capturing
  dumps from running .NET processes, setting up dump collection in Docker or
  Kubernetes, using dotnet-dump collect or createdump. DO NOT USE FOR: analyzing
  or debugging dumps, post-mortem investigation with lldb/windbg/dotnet-dump
  analyze, profiling or tracing, or for .NET Framework processes."
---

Resolve the global OpenCode configuration root as `$XDG_CONFIG_HOME/opencode` when `$XDG_CONFIG_HOME` is set; otherwise use `~/.config/opencode`.
Read `skills/dump-collect/BODY.md` under that global root before doing anything else.
Follow that file as this command's full instructions.

Arguments: $ARGUMENTS