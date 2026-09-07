---
record_id: codex-plugin-behaviour-2026-09-07
date: 2026-09-07
repo_head: 83c09fa415f342f00d923f8fe137c0d7ad8203de
kind: model-panel
summary: Luna exercised native invocation policy, handoff, bundled references, and generated skills in the full Codex plugin catalog.
isolation_ok: true
fixture_sha: sha256:d5eeca4a5bbc8c8ebf0b419ea00af6be7f9e70e798368833ccbe14d4c9b7365e
harness_name: Codex CLI
harness_version: 0.153.4
runner_revision: sha256:d1f1bb18e3bab476499a3e6cd8f2722f709f291b0668f15f6d95b3714c97fc17
---

# Codex plugin behavioural panel

This tier-2 record captures one isolated run of [`codex-matrix.ps1`](../codex-matrix.ps1). The
runner, fixture, generated Codex output, and curation changes were uncommitted worktree content on
the `repo_head` above; their exact runner and fixture hashes are retained in frontmatter. Raw JSONL,
the seeded credential file, and absolute paths remain outside the repository in the external lab.

All calls pinned `gpt-5.6-luna` with `low` reasoning. The harness reported token usage but no billed
amount, so cost cells are blank. The runner installed the fixture and all four generated plugins
before model calls: 117 generated `SKILL.md` files and 27 manual-policy `agents/openai.yaml` files.

## Results

| model | probe_id | status | cost | tools_observed | notes |
|---|---|---|---:|---|---|
| `gpt-5.6-luna@low` | `liveness` | pass |  | none | exact `LIVE` |
| `gpt-5.6-luna@low` | `explicit-auto` | pass |  | native skill load | exact `CODEX-AUTO-RAN` |
| `gpt-5.6-luna@low` | `explicit-manual` | pass |  | native skill load | exact `CODEX-MANUAL-RAN` |
| `gpt-5.6-luna@low` | `explicit-both` | pass |  | native skill load | exact `CODEX-BOTH-RAN` |
| `gpt-5.6-luna@low` | `cross-skill-handoff` | pass |  | native skill load, command execution | installed skill loaded a second installed skill |
| `gpt-5.6-luna@low` | `bundled-reference` | pass |  | native skill load, command execution | installed skill read its packaged reference |
| `gpt-5.6-luna@low` | `implicit-manual-1` | pass |  | none | manual marker absent |
| `gpt-5.6-luna@low` | `implicit-auto-1` | pass |  | native skill load | auto marker present |
| `gpt-5.6-luna@low` | `implicit-both-1` | pass |  | native skill load | both marker present |
| `gpt-5.6-luna@low` | `implicit-manual-2` | pass |  | none | manual marker absent |
| `gpt-5.6-luna@low` | `implicit-auto-2` | pass |  | native skill load | auto marker present |
| `gpt-5.6-luna@low` | `implicit-both-2` | pass |  | native skill load | both marker present |
| `gpt-5.6-luna@low` | `implicit-manual-3` | pass |  | none | manual marker absent |
| `gpt-5.6-luna@low` | `implicit-auto-3` | pass |  | native skill load | auto marker present |
| `gpt-5.6-luna@low` | `implicit-both-3` | pass |  | native skill load | both marker present |
| `gpt-5.6-luna@low` | `uninstalled-negative` | pass |  | none | uninstalled marker absent |
| `gpt-5.6-luna@low` | `generated-explicit-manual` | pass |  | native skill load | `deniz-process:using-superpowers` returned its exact template |
| `gpt-5.6-luna@low` | `generated-implicit-large-catalog` | pass |  | native skill load | selected SIMD guidance and returned `ConditionalSelect` |

Across the repeated fixture probes, manual implicit selection was 0/3, auto was 3/3, and both was
3/3. These are bounded observations, not general selection-rate guarantees. Explicit addressability
passed for all three policies. The uninstalled negative control did not leak.

Every model call emitted the same catalog-pressure warning: skill descriptions were shortened to
fit the skills context budget, while every skill remained visible. The generated implicit probe
still found the relevant skill in the 117-skill installed estate. The warning is therefore a real
operational constraint, not evidence that this particular catalog dropped a skill.

## Sanitized event excerpts

Paths are reduced to `<ISOLATED_PLUGIN_CACHE>`. Natural-language preambles are retained only when
they show which skill the model selected.

### Explicit policy controls

```text
explicit-auto    -> CODEX-AUTO-RAN
explicit-manual  -> CODEX-MANUAL-RAN
explicit-both    -> CODEX-BOTH-RAN
```

### Cross-skill handoff

```text
agent: using codex-handoff-ibis; it requires codex-auto-zebra
command: read <ISOLATED_PLUGIN_CACHE>/codex-handoff-ibis/SKILL.md -> exit 0
command: read <ISOLATED_PLUGIN_CACHE>/codex-auto-zebra/SKILL.md -> exit 0
agent: CODEX-AUTO-RAN
```

### Bundled reference

```text
agent: using codex-reference-fox; reading its bundled reference
command: read <ISOLATED_PLUGIN_CACHE>/codex-reference-fox/references/marker.md -> exit 0
tool output: The literal marker is CODEX-REFERENCE-RAN.
agent: CODEX-REFERENCE-RAN
```

### Repeated implicit and negative controls

```text
manual repeats: no applicable installed skill; marker count 0/3
auto repeats: codex-auto-zebra selected; marker count 3/3
both repeats: codex-both-lemur selected; marker count 3/3
uninstalled negative: no installed skill applies; leak marker absent
```

### Generated estate

```text
$deniz-process:using-superpowers -> Using [skill] to [purpose]
.NET SIMD non-idempotent overlapping-tail prompt -> ConditionalSelect
```

## Harness corrections found before the recorded run

Two preliminary attempts exposed test-harness assumptions rather than package failures:

- `codex plugin list --available` also included OpenAI's built-in remote catalog in an otherwise
  fresh isolated profile. Assertions now filter by the marketplace under test.
- `--ignore-user-config` hid installed plugins on this CLI release even though the help text frames
  it as a `config.toml` control. A read-only non-interactive call could see skill metadata but could
  not open installed skill bodies outside the project. The recorded run instead relies on the
  disposable `CODEX_HOME` for config isolation and uses `--approve-for-me --add-dir
  <ISOLATED_PLUGIN_CACHE>`, granting reviewed workspace access only to the lab project and isolated
  cache. No machine-wide sandbox bypass was used.

## Isolation

- The isolated `CODEX_HOME` and working project were below `<LAB>`, outside both repository and real
  user profile.
- The real profile's Codex config, installed-plugin record, marketplaces, and cache were
  byte-identical before and after the run.
- Repository porcelain status was identical before and after the run.
- The negative plugin remained uninstalled; the positive fixture was removed at the end.

ChatGPT desktop discovery, Codex IDE standalone-skill transport, and remote Git marketplace upgrade
were not exercised by this CLI panel.
