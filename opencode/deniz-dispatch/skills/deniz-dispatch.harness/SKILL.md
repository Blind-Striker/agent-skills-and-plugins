---
name: deniz-dispatch.harness
description: Use when you run the bundled dispatch harness to send a task file
  to worker models through OpenCode or Codex, check or resume a run, answer an
  escalated ask, cancel or collect a task, or read its ledger. The harness's
  commands, task file, modes, configuration, ledger and exit codes; whether and
  how to delegate is delegating-to-worker-models' judgment.
---

# Dispatch harness

From this skill's folder, build before every harness command:

```text
dotnet build dispatch.cs
dotnet dispatch.cs -- <command> [arguments]
```

If the build exits non-zero, nothing started. Only after a successful build do the harness's exit
codes describe the command's result. Pass the target repository with `--repo` (`routes` also accepts
`--dir`), or set `repo` in the task file. Command-line paths must be absolute; paths inside a task
file remain relative to that file. In PowerShell, use `$PWD.ProviderPath` to construct absolute paths.