---
description: >
  Enable nullable reference types in a C# project and systematically resolve all
  warnings. USE FOR: adopting NRTs in existing codebases, file-by-file or
  project-wide migration, fixing CS8602/CS8618/CS86xx warnings, annotating APIs
  for nullability, cleaning up null-forgiving operators, upgrading dependencies
  with new nullable annotations. DO NOT USE FOR: projects already fully migrated
  with zero warnings (unless auditing suppressions), fixing a handful of
  nullable warnings in code that already has NRTs enabled, suppressing warnings
  without fixing them, C# 7.3 or earlier projects. INVOKES:
  Get-NullableReadiness.ps1 scanner script.
---

Resolve the global OpenCode configuration root as `$XDG_CONFIG_HOME/opencode` when `$XDG_CONFIG_HOME` is set; otherwise use `~/.config/opencode`.
Read `skills/migrate-nullable-references/BODY.md` under that global root before doing anything else.
Follow that file as this command's full instructions.

Arguments: $ARGUMENTS