---
record_id: codex-plugin-structural-2026-09-07
date: 2026-09-07
repo_head: 83c09fa415f342f00d923f8fe137c0d7ad8203de
kind: harness-structural
summary: Codex CLI installed the fixture and all four generated plugins in an isolated CODEX_HOME.
isolation_ok: true
---

# Codex plugin marketplace, installation, and cache structure

This tier-1 record captures a credential-free structural run of
[`codex-matrix.ps1`](../codex-matrix.ps1) on `codex-cli 0.153.4`. The runner was an uncommitted
worktree addition on the `repo_head` above; the generated Codex output and fixture marketplace were
from that same worktree. Raw JSON and absolute paths remain in the external lab.

## Isolation

- `CODEX_HOME` was a new directory below `<LAB>`, outside the repository and real user profile.
- The working directory was `<LAB>/project`.
- The initial marketplace list was empty.
- Before and after snapshots of the real profile's Codex config, plugin installation records,
  marketplace records, and plugin cache were byte-identical.
- Repository porcelain status was identical before and after the run.
- Every reported installed cache path was below the isolated `CODEX_HOME`.

## Results

| Probe | Observed result | Status |
|---|---|---|
| Add fixture marketplace | `codex-probe-marketplace`, local source | pass |
| List available fixture plugins | `codex-probe`, `codex-negative-control` | pass |
| Install positive fixture | only `codex-probe@codex-probe-marketplace` installed | pass |
| Negative control | `codex-negative-control` remained uninstalled | pass |
| Remove positive fixture | installed fixture count returned to zero | pass |
| Add repository marketplace | `deniz-skills`, local source | pass |
| List generated plugins | all four expected plugin names available | pass |
| Install generated plugins | all four expected plugin names installed | pass |
| Inspect isolated generated cache | 117 `SKILL.md`; 27 `agents/openai.yaml` | pass |

The four generated plugin names were `deniz-dotnet-akka`, `deniz-dotnet-aspire`,
`deniz-dotnet-general`, and `deniz-process`.

## Command surface exercised

All commands used their JSON output mode:

```text
codex plugin marketplace list --json
codex plugin marketplace add <FIXTURE_MARKETPLACE> --json
codex plugin list --available --json
codex plugin add codex-probe@codex-probe-marketplace --json
codex plugin list --json
codex plugin remove codex-probe@codex-probe-marketplace --json
codex plugin marketplace add <REPOSITORY_ROOT> --json
codex plugin list --available --marketplace deniz-skills --json
codex plugin add <PLUGIN>@deniz-skills --json
codex plugin list --marketplace deniz-skills --json
```

## Unmeasured boundary

No model call was made. The isolated home had no `auth.json`, and `OPENAI_API_KEY` was absent, so
explicit invocation, implicit selection propensity, manual-policy suppression, initial-list budget
warnings, cross-skill reference execution, and ChatGPT desktop discovery remain unmeasured. The
runner has a bounded `-Behavioural` path for a later credentialed isolated run; this record does not
turn structural installation into a runtime-support claim.
