---
record_id: antigravity-plugin-panel-2026-11-20
date: 2026-11-20
repo_head: 791937a69adbf4202b600ad99259e23de16a4778
kind: model-panel
summary: The fixture and generated plugins validated and were model-reported; qualified manual invocation hit a headless permission denial.
isolation_ok: false
fixture_sha: db32e3d6c55ca0c4b9f0de39dacfe55e2bcd7fce9d987e69d01aa93874c50cfa
harness_name: agy
harness_version: 1.5.13
runner_revision: sha256:727db3b581c3463a2be3bad4a139ec831a0802fe1bfabb168127e558f5fcb5e4
---

# Antigravity plugin validation and runtime panel

This tier-2 record covers three Windows runs of [`antigravity-matrix.ps1`](../antigravity-matrix.ps1)
with `gemini-3.8-flash-low`: `-Mode all`, `-Mode behavior`, then `-Mode all` after an empty-file
restoration fix. The runner and fixture were uncommitted worktree additions at `repo_head`; the five
generated Antigravity plugins were also uncommitted worktree output. The final run used
`common.ps1` SHA-256 `d71afc9e1a68361c20f80757052f9487f8552fb3de6918941d5681da82226180`.
The fixture digest hashes UTF-8, LF-joined lines of sorted relative path, a space, and lowercase
SHA-256 of each file, with no final newline. Raw responses and absolute paths remain in the external
lab.

## Profile and measurement boundary

`agy 1.5.13` ignores `GEMINI_HOME` for plugin discovery. The runner temporarily pointed the real
`~/.gemini/config/plugins.json` at the fixture and then the five generated folders; it restored the
manifest's prior absence after each run. The `config/plugins/` directory was absent before and
after, and no plugin install was performed. The working directory was a fresh external lab project.
This is not a fully isolated profile: the real settings and loose/built-in customizations remained
available. An independent `agy` process was observed during the follow-up, so concurrent visibility
of the temporary manifest cannot be ruled out. `isolation_ok: false` records this boundary, not a
failure of the observed manifest restoration. The model's listing is self-report, not a captured
system prompt or tool schema; hidden means absent from that response.

## Structural results

The first and final `all` runs each returned exit 0 from `agy plugin validate --strict --json` for
every folder. Each validator result contained one informational missing-logo note, with no errors
or warnings. Counts below are filesystem counts, not model-discovery counts.

| Plugin | Skills | Manual skills | Continuations | Native agents |
|---|---:|---:|---:|---:|
| `probe-plugin` fixture | 2 | 1 | 0 | 1 |
| `deniz-coordination` | 6 | 1 | 0 | 0 |
| `deniz-dotnet-akka` | 5 | 0 | 1 | 1 |
| `deniz-dotnet-aspire` | 10 | 1 | 0 | 0 |
| `deniz-dotnet-general` | 69 | 11 | 6 | 1 |
| `deniz-process` | 45 | 22 | 0 | 0 |
| Generated total | 135 | 35 | 7 | 2 |

## Behavioral results

The runner's status is based on the canary or named items in the response. All attempts below exited
0 and none timed out. A blank response with a permission denial still fails the canary check; it
does not establish whether qualified expansion would succeed with a suitable permission rule. No
cost was reported by the CLI, and no tool calls were captured in text output.

| model | probe_id | status | tools_observed | notes |
|---|---|---|---|---|
| `gemini-3.8-flash-low` | `all-1/fixture-discovery` | pass | not captured | auto and specialist listed; manual absent |
| `gemini-3.8-flash-low` | `all-1/qualified-manual` | fail | read requested, denied | no canary |
| `gemini-3.8-flash-low` | `all-1/bare-manual` | pass | not captured | canary returned |
| `gemini-3.8-flash-low` | `all-1/estate-discovery` | pass | not captured | brainstorming and both native agents listed; verified-waves absent |
| `gemini-3.8-flash-low` | `behavior-2/fixture-discovery` | pass | not captured | auto and specialist listed; manual absent |
| `gemini-3.8-flash-low` | `behavior-2/qualified-manual` | fail | read requested, denied | no canary |
| `gemini-3.8-flash-low` | `behavior-2/bare-manual` | pass | not captured | canary returned |
| `gemini-3.8-flash-low` | `behavior-2/estate-discovery` | pass | not captured | brainstorming and both native agents listed; verified-waves absent |
| `gemini-3.8-flash-low` | `all-3/fixture-discovery` | pass | not captured | auto and specialist listed; manual absent |
| `gemini-3.8-flash-low` | `all-3/qualified-manual` | fail | read requested, denied | no canary |
| `gemini-3.8-flash-low` | `all-3/bare-manual` | pass | not captured | canary returned |
| `gemini-3.8-flash-low` | `all-3/estate-discovery` | pass | not captured | brainstorming and both native agents listed; verified-waves absent |

Both `all` runs exited 1 because the qualified canary assertion failed; the `behavior` repeat also
exited 1. As a separate diagnostic, `/antigravity-probe-plugin:probe-manual` using the fixture
directory name instead of the manifest name produced the same headless read denial and no output;
this does not distinguish the remaining causes. The diagnostic also restored the manifest's absence.

## Sanitized excerpts

Final fixture-discovery response, with built-ins retained to show context:

```text
Skills: agy-customizations, antigravity-guide, probe-auto, run-workflow, swarm_monitor
Subagents: self, research, image-generator, probe-specialist
```

Final bare manual response: `AGY-MANUAL-CANARY`. Final qualified response had empty stdout; stderr
said a `read_file` permission was required and automatically denied because headless mode cannot
prompt. The final estate response included `brainstorming`, `akka-net-specialist`, and
`roslyn-incremental-generator-specialist`; it did not include `verified-waves`.

## Separate T3 boundary

This panel did not test T3 Code. The earlier
[Tier 0 observations](../../../docs/research/antigravity-native-plugin-and-skill-surfaces.md#t3-code-acp-agy_acp_server-145-vs-agy-1513)
report that T3's `agy_acp_server 1.4.5` discovered loose skills but ignored the native plugin
manifest and native agents. Do not transfer the standalone CLI result to that server.
