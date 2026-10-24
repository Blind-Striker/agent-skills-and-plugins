Read when you need to understand how each mode, rule or limit maps to opencode and Codex.

# Backend behaviour

Select a backend and a catalog route for every task, directly or through configuration. Neither
backend supplies a task's implicit model. opencode also supports pinned roster agents and personas.
The two backends receive the same assembled user message. See [task files](task-file.md) for prompt
composition and [configuration](config.md) for restrictions.

## Mode mapping

| Setting | Type and default | opencode | Codex | Validation |
|---|---|---|---|---|
| `profile` | Enum; `clean` for structured or read-only Codex tasks, otherwise `full`; explicit values override | Full uses installed context. Clean redirects config and builds a minimal profile. | Full uses user home. Clean uses temporary `CODEX_HOME` when possible. | `clean` or `full`; partial clean fallback is recorded. |
| `project` | Enum; `off` for structured, otherwise `on` | Off disables project config through environment switches. | Off disables project docs and marks the working directory untrusted. | `on` or `off`. Only structured project-off tasks get projectless scratch. |
| `access` | Enum; `workspace` for code/mechanical, otherwise `read-only` | Linux/macOS: read-only and workspace use the [accident guard](#opencode-accident-guard), recorded `sandbox`; read-only keeps its shell and denies `edit`. Windows: unfenced; read-only denies `edit` and, with `readOnlyShell: decline`, every shell call (no `git`), recorded `policy`; workspace is `none`. Full is unfenced, recorded `none`, on every OS. | Read-only and workspace use an OS sandbox, recorded `sandbox`; full uses danger-full-access, recorded `none`. | Must respect `maxAccess`. Windows Codex sandboxed tasks require ready setup. Missing opencode fence readiness refuses plan/run, never falls back. |
| `network` | Enum; `on` | Off denies `webfetch` and `websearch`, recorded `policy`; on is `none`. | Read-only/workspace set `networkAccess` from the resolved value on every turn. Full has no sandbox network field. | `on` or `off`; full plus off is refused on Codex. |
| `workspace` | Enum; `worktree` | Core prepares the folder. | Same core workspace rules. | `worktree` or `in-place`; structured tasks bypass Git workspace preparation. |

opencode uses bubblewrap (`bwrap`) on Linux and `sandbox-exec` on macOS as an
[accident guard](#opencode-accident-guard) for read-only and workspace access; Windows remains unfenced.
Workspace scope is a settle-time check, not a filesystem boundary.
Both backends' workspace writable roots include the task folder, task temp and configured
[`writableRoots`](config.md#writable-roots). Full access removes Codex's sandbox or opencode's accident guard. An unfenced
opencode shell can write despite an edit deny and modify harness state. On every OS, opencode
network-off is tool-level only: a shell can reach the network despite web-tool denies. Verify runs
outside backend sandboxing as the user on both backends.

## Rules, asks and results

| Behaviour | Type and default | opencode | Codex |
|---|---|---|---|
| Deny rule | Tool/resource pattern; no rules by default | Applies at each matching tool call. | Applies only when approval is requested to leave the sandbox. |
| Escalation rule | Tool/resource pattern; no rules by default | Matching ask waits for the operator. | Matching approval request waits for the operator. |
| Unmatched ask, including MCP tools | Decline for read-only tasks on both backends | Read-only: reject; otherwise `once`. | Decline with feedback. |
| Read-only write request | Immediate rejection | Core refuses any observed write ask; edit deny also applies. | Core refuses edit or write-permission approvals. |
| Operator reply | `once` or `reject` | Backend receives a one-shot permission reply. | Backend receives accept or decline, never a session-wide approval. |
| Reject feedback | Supplied message or default decline | Message travels with the rejection. | Decline first, then `turn/steer` with feedback; a failed steer does not undo the decline. |
| Form question | No-human feedback | Cancels the form. | Answers user-input questions by question id; declines MCP elicitations. |
| Structured output | Validated after completion | Text output; common validator extracts the document. | Adds schema-constrained output on `turn/start`; common validator still checks identity and immutable fields. |
| Recovery | Never rerun recorded ids | Unfenced: reattach to stored session; `resume --continue` can re-prompt interrupted work. Fenced: a task whose private server is gone reports `lost`, with no reconstruction or continuation after a supervisor restart. | Started sessions report `lost` during recovery and cannot be re-prompted; recorded cancel/timeout takes precedence in settlement. |
| Cost and tokens | Absent when unavailable | Collects reported usage and cost. | Collects token usage if reported; no cost estimate is synthesized. |

A deny wins over an escalation. Rule restrictions cannot be weakened by later config. On Codex,
planning warns per rule that enforcement is only at the approval gate. Commands permitted by the
sandbox need no ask; with workspace access and network on, a `git push` deny is not a push boundary.
Tool-resource patterns match text, not aliases, script contents or parsed shell operations.

For the read-only shell behaviour, read
[configuration](config.md#read-only-shell-preference). For catalog and probe coverage, read
[configured backend coverage](cli.md#configured-backend-coverage).

## opencode accident guard

Stage 1's fence stops a worker's accidental writes outside its writable set. It is an **accident
guard**, not a security boundary against a worker that tries to escape; a real boundary is stage 2.
The recorded `sandbox` enforcement value and the CLI's `sandboxed shell` label do not imply more.
It does not stop:

- Loopback connections to other opencode servers, including unfenced servers: the harness's own
  servers and the operator's background service. The SDK needs loopback connectivity.
- Writes to the shared opencode data folder, including the session database and snapshot Git folders.
- IPC to other Unix sockets (for example `/var/run/docker.sock`) and system services (for example macOS `launchctl`).
- Signals to the user's processes; there is no PID namespace.
- Detached processes that outlive the task; see the descendant lifecycle below.
- Network access.

**Descendant lifecycle.** The harness ends the server only through the
SDK's disposal and counts it stopped once the SDK reports the server's exit. A confirmed stop proves
that the opencode server process exited, not that every process it started did. The SDK keeps
upstream opencode's behaviour: on Windows a server's grandchildren leave its job object, and on
POSIX tool processes run in their own sessions and a server that exits successfully leaves its group
alone. A worker's build or test process can therefore outlive the task. The harness cannot contain
them without SDK support; check for leftover processes after a cancelled or timed-out opencode task.

## opencode process and profile

The harness starts private `opencode serve` processes for the run and subscribes to their event
streams. Each fenced task has its own server, keyed by run/task; releasing its worker, a failed
creation, or settlement in any final state disposes that server. An exited fenced server loses only
its own task immediately; three consecutive failed `/active` polls do the same, with success resetting
the count. Unfenced tasks share servers keyed by profile/project modes. It disables file watchers.
On Windows it resolves the native
executable behind a recognized package-manager shim; user config can provide an absolute executable
path. Planning (`plan`, `run` and selfcheck's clean-profile check) refuses a server whose reported
version differs from pin `2.1.4`, as the Codex backend refuses a CLI off its pin; there is no
override, because the SDK decodes only the pinned wire contract and another version can lose events.
Selfcheck also reports the pin as its own check and checks stored session rules without sending a
prompt. Normal planning validates the catalog at each task's location.

Clean mode creates a minimal config under the control folder, retaining required provider/model
definitions and the configured shell. It redirects `OPENCODE_CONFIG_DIR`, clears direct config
overrides and disables global skill compatibility loading. With clean profile and project on,
repository skills in `.claude` and `.agents` are also suppressed by that switch. Project off adds
`OPENCODE_CONFIG_PROJECT_DISABLE=1` and `OPENCODE_DISABLE_PROJECT_CONFIG=1`.

Clean isolation is best effort. System configuration, managed preferences, provider remote config,
lazily discovered subfolder instructions and the user's session database remain possible sources of
context. The harness reports the requested profile; it does not prove all those sources absent.

Sessions inherit harness denies on the edit tool for the control folder and that run's ledger.
Fenced servers allow writes to opencode's data and state folders (`XDG_DATA_HOME/opencode` and
`XDG_STATE_HOME/opencode`, otherwise the usual home fallbacks), task temp and `/dev`. The user's
opencode cache and config folders and the server's profile root are not granted write access.
Each fenced server gets a private `XDG_CACHE_HOME` and `TMPDIR`/`TEMP`/`TMP` under task temp.
Workspace access also allows the workspace, Git common folder and configured writable roots.
The opencode data `bin` path is read-only (on Linux, overlaid when it exists); existing Git common
`hooks` and `config` paths are also overlaid read-only after the writable grants. Every writable root is checked again at launch using real paths; a root
inside, equal to or containing the control or ledger root refuses the start, including Git common
folders and links. A config write needed at server startup fails closed.
macOS grants no global temporary folders and resolves every profile write grant and deny to its real
path. Linux mounts a private `/tmp` and rebinds a read-only task's directory and its Git common folder
(when under `/tmp`) read-only so they remain visible. Unfenced shell and MCP tools may write control
and ledger state. A backend inactivity interruption can leave a task `interrupted`; use
`resume --continue` to re-prompt it only while its server is live. The harness itself does not set a separate 60-minute
inactivity timer; its task timeout is independent.

After a done settlement the supervisor removes the session. Unfenced failed sessions stay available
for collection until cleanup; fenced failed sessions remain stored, but their disposed private server
is not reconstructed for collection or continuation. `clean` releases recorded sessions, using an unfenced shared host when a
fenced server is gone (the session database is shared), and searches for orphan sessions using
run/task metadata. Server processes end with their owning scope; session persistence does not make
the supervising process durable.

Fence readiness is checked once per harness process: Linux requires `bwrap` on PATH and a successful
`bwrap --ro-bind / / --dev /dev --ro-bind /proc /proc --tmpfs /tmp --bind <temp> <temp> -- true`;
macOS requires a successful
`sandbox-exec -p "(version 1)(allow default)" /usr/bin/true`. Ubuntu 24.04 needs the operator to set
`kernel.apparmor_restrict_unprivileged_userns=0` as root; the harness never changes it. A failed check
refuses fenced work, without an unfenced fallback. Launch uses the absolute fence executable resolved
by readiness. On Linux, an existing `$XDG_RUNTIME_DIR` is hidden with a private tmpfs to block its
user D-Bus and systemd sockets. This remains only an [accident guard](#opencode-accident-guard).

## Codex process and sandbox

The backend runs `codex app-server` over stdio and requires CLI pin `0.163.0`. Its protocol contract
tests cover expected request shapes; installed version and sign-in are also checked before use.
The probe checks account state and paginates the model catalog. It reads Windows readiness, but
the base selfcheck does not fail on readiness alone. Plan/run refuse read-only or workspace tasks
unless Windows reports `ready`; the operator sets the sandbox up, never the harness.

Access maps to `read-only`, `workspace-write` and `danger-full-access` at thread start, and the
backend checks the returned sandbox type. Each `turn/start` supplies the resolved sandbox policy
and network value. The selected effort comes from the route or the catalog's default. Structured
turns also supply the output schema. Approval, tool, retry and token events feed the common ledger.

The `workspace-write` sandbox lets a task write only inside its workspace. A task that must write
elsewhere (the system temp folder included, where many build and test tools keep caches) or start
`wsl.exe` fails there.

**Known limit: .NET and MSYS tools in the Windows sandbox.** In both of the sandbox's Windows modes,
measured and reported upstream, `dotnet build` and `dotnet test` fail on the NuGet cache, MSBuild
temp files, named pipes and project-reference evaluation, often with no diagnostic, and MSYS-based
tools (Git Bash, `git submodule`) fail on shared memory (openai/codex#13183, #39927, #15295,
#21715). Several of these failures exit 0: a submodule stays empty and the step still passes. Read
a sandboxed command's output, never its exit code alone. A Windows task that must build or test
.NET runs with `access: full`, chosen explicitly; the ledger records that it ran without the
sandbox. On Windows, opencode's `workspace` is already unfenced, so the same task runs there without
`access: full` and with the same exposure. On Linux/macOS, use fenced workspace access and the
[build-cache recipe](config.md#writable-roots) instead of removing the fence for build caches.

On Linux and macOS, sandbox prerequisites depend on the installed runtime and host. A fake protocol
test or build does not prove enforcement; a Functional test that tries an outside write must pass
on the actual OS before making that claim. The effect of reject steering on model behaviour also
requires a Functional test.

### Clean Codex home

Clean mode hardlinks the user's sign-in file and available Windows sandbox setup files into a
harness-owned temporary home on the user's `CODEX_HOME` volume: it tries system temp first, then a
sibling folder beside the user home. It writes minimal configuration, disables skill instructions and app/plugin features,
preserves the selected Windows sandbox mode, and checks a random marker through the home reported
by app-server. The worker state records the folder and ownership marker; cleanup refuses an
unrecognized path or marker and removes the temporary home only after the process tree has stopped.

If the sign-in file is unavailable, the hardlink fails, the folder already exists, the probe fails
or the reported home cannot prove the marker, the backend can use the user home and records
`settings.profile.actual: clean-partial` plus a reason. When neither location works, the reason names
the source and candidate volumes and their failures. Inspect actual profile rather than treating a requested clean profile as proof
that user configuration did not load.

For project off, Codex disables project docs and marks the working directory untrusted. Structured
project-off tasks use empty scratch outside repositories; other task kinds keep their core-created
workspace. The backend does not replace every project-off code workspace with an empty directory.

The note on inherited exec-policy rules is described under
[inherited Codex rules on read-only tasks](cli.md#inherited-codex-rules-on-read-only-tasks).

**Known limit: sandbox-denied commands on macOS and Linux.** The pinned Codex sends no command item
for a shell call it runs from its code mode when the sandboxed session fails to open, as a
sandbox-denied write does. Such an attempt leaves no tool event in the ledger, although the sandbox
refused it. A request to rerun the command outside the sandbox still arrives as an approval request:
it follows the deny and escalation rules, reaches the operator when a rule escalates it, and is
recorded with its outcome. On Windows the denied attempt is recorded as a failed tool event. The
upstream report is openai/codex#56396; check it before a Codex pin moves.

Codex records process identity and descendants so recovery can stop the recorded process tree.
It does not attach to an old active thread for more prompts. Without a recorded cancellation or
timeout, either resume mode settles a started non-final Codex task as failed with outcome `lost`;
recorded stop requests retain their cancelled or timeout outcome. Use a new run id for a retry. An
unconfirmed hard stop produces `zombie`, and the harness skips commit and verify while the worker
may still write.
