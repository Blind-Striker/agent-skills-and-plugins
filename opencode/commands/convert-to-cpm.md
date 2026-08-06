---
description: >
  Convert .NET projects and solutions (.sln, .slnx) to NuGet Central Package
  Management (CPM) using Directory.Packages.props. USE FOR: converting to CPM,
  centralizing or aligning NuGet package versions across multiple projects,
  inlining MSBuild version properties from Directory.Build.props into
  Directory.Packages.props, resolving version conflicts or mismatches across a
  solution or repository, updating or bumping or syncing package versions across
  projects. Also activate when packages are out of sync, drifting, or
  inconsistent -- even without the user mentioning CPM. Provides baseline build
  capture, version conflict resolution, build validation with binlog comparison,
  and a structured post-conversion report. DO NOT USE FOR: packages.config
  projects (must migrate to PackageReference first) or repositories that already
  have CPM fully enabled.
---

Read `skills/convert-to-cpm/BODY.md` from the active OpenCode configuration root before doing anything else.
For a project-local install, use `.opencode/skills/convert-to-cpm/BODY.md`; for a global install, use `~/.config/opencode/skills/convert-to-cpm/BODY.md`.
Follow that file as this command's full instructions.

Arguments: $ARGUMENTS