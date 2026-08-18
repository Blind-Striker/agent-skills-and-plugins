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

Read `skills/dotnet-aot-compat/BODY.md` from the active OpenCode configuration root before doing anything else.
For a project-local install, use `.opencode/skills/dotnet-aot-compat/BODY.md`; for a global install, use `~/.config/opencode/skills/dotnet-aot-compat/BODY.md`.
Follow that file as this command's full instructions.

Arguments: $ARGUMENTS