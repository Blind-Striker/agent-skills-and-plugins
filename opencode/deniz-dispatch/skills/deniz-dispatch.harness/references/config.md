Read when you configure layers, policy rules, the route allow-list or storage locations.

# Configuration

Config files use JSONC with required integer `schemaVersion: 2`. Comments and trailing commas are
allowed. Unknown typed members fail; `$schema` and `$comment` are ignored. Old versions fail and
newer versions require a harness update. User config is optional. Repository maintainers own
`.dispatch/config.jsonc`, which the harness reads from committed `HEAD`, not the working copy.
A differing working copy produces a warning and is not applied.

## Files and locations

The config and state folders live outside the installed skill. The `deniz-dispatch` component below
is the package directory name.

| Location | Type | Default | Validation and purpose |
|---|---|---|---|
| Windows config root | directory | `%APPDATA%/deniz-dispatch` | Resolved through the OS application-data folder API. Holds `config.jsonc`. |
| Windows state root | directory | `%LOCALAPPDATA%/deniz-dispatch` | Resolved through the OS local-application-data API. |
| Linux config root | directory | `$XDG_CONFIG_HOME/deniz-dispatch`, otherwise `~/.config/deniz-dispatch` | Only an absolute XDG override is used. |
| Linux state root | directory | `$XDG_STATE_HOME/deniz-dispatch`, otherwise `~/.local/state/deniz-dispatch` | Only an absolute XDG override is used. |
| macOS config root | directory | Same XDG rules as Linux | Does not use a separate platform-specific preferences directory. |
| macOS state root | directory | Same XDG rules as Linux | Same fallback as Linux. |
| `DISPATCH_HOME` | environment variable, absolute directory | Unset | Replaces both roots with `<value>/config` and `<value>/state`; relative value: `DISPATCH_HOME must be an absolute path`. |
| User config | JSONC file | `<config root>/config.jsonc` | Missing is allowed; unreadable or invalid is an error naming the source and layer. |
| Repository config | JSONC file | `<repo>/.dispatch/config.jsonc` at `HEAD` | Missing is allowed. Changes take effect after commit. |
| Ledger root | directory | `<state root>/ledgers` | User `locations.ledgerRoot`, then absolute CLI `--ledger`, override it. |
| Worktree root | directory | `<state root>/worktrees` | User `locations.worktreeRoot` overrides it. |
| Control root | directory | `<state root>/control` | Must be outside, and must not contain, the repository, worktree root or ledger root. Overlap refuses `run`. |

Packaged Windows hosts may see a private application-data location. Set `DISPATCH_HOME` to an
external location when the host needs stable shared state. Deep default worktree paths can exceed
Windows path limits; choose a short `locations.worktreeRoot` or enable Git `core.longpaths`.
`selfcheck` prints the resolved state, worktree and control roots and warns when `core.longpaths`
is not true. `DISPATCH_HOME` moves only the harness's own folders: keep the backends' own homes
(`CODEX_HOME`, opencode's data root under `XDG_DATA_HOME`) where they are, because their sign-in
lives there; the harness gives each worker a clean profile itself when `profile: clean` asks.

## File fields

| Field | Type | Default | Validation and scope |
|---|---|---|---|
| `schemaVersion` | integer | Required, `2` | `schemaVersion must be 2; version 1 is not read`; newer: `written by a newer dispatch; update the plugin`. |
| `preferences` | object | No overrides | User and repository only. Fields follow the preference table in [task files](task-file.md), except `dir`; also accepts `maxParallel` and `defaultRoutes`. |
| `restrictions` | object | No added restrictions | Fields below. |
| `instructions` | string array | `[]` | Accumulate in layer order; identical text appears once. Blank text: `<layer>: instructions must be non-empty`. |
| `locations` | object | Default roots | User only: `<layer>: locations is user-layer-only`. |
| `backends` | object | Built-in executable discovery | User only: `<layer>: backends is user-layer-only`. Only `opencode` and `codex` members exist. |
| `preferences.maxParallel` | integer | `3` | Last configured value, task-file top level, then `--max`; must be 1 through 8. |
| `preferences.defaultRoutes` | map of backend to string | No default routes | Keys must be `opencode` or `codex`: `<layer>: defaultRoutes keys must be opencode or codex`. Values use backend notation without a qualification prefix. |
| `locations.ledgerRoot` | string path | `<state root>/ledgers` | Absolute or starts with `~`; otherwise `locations.ledgerRoot must be an absolute path (or ~ in user locations)`. |
| `locations.worktreeRoot` | string path | `<state root>/worktrees` | Same expansion and validation as ledger root. |
| `backends.opencode.executable` | string path | Discover `opencode` | Absolute: `<layer>: backends executable must be an absolute path`. |
| `backends.codex.executable` | string path | Discover `codex` | Same absolute-path validation. |
| `backends.opencode.env` | array of variable names | `[]` | Re-adds named scrubbed values to backend processes. Names match `^[A-Za-z_][A-Za-z0-9_]*$`; otherwise `<layer>: backends env must contain environment variable names`. |
| `backends.codex.env` | array of variable names | `[]` | Same name validation and passthrough behaviour. |

Repository config cannot request `preferences.access: full`: `repo: preferences.access full is
forbidden`. User config, a task or CLI may request it if restrictions permit it. A `--policy` file
has the config shape but may contain only restrictions and instructions. It refuses preferences,
locations, backends, `restrictions.routes` and `restrictions.scrub`.

### Read-only shell preference

| Field | Type | Default | Meaning |
|---|---|---|---|
| `readOnlyShell` | string enum preference | `decline` | `decline` rejects shell approval for a read-only task. `escalate` lets a matching escalation rule reach the operator. |

Set it in user or repository preferences, task-file defaults, the task, or CLI, following the usual
preference order. Opt into `escalate` for a reviewed command that must run outside the sandbox.
A deny still wins, and read-only write requests stay declined. With `escalate`, a shell request
that matches no escalation rule is still declined, and every further request raises a new ask.
On the command line the flag is `--read-only-shell <value>`. A `structured` task resolves to
`decline`; setting `escalate` on one is a plan error. The resolved value and its layer appear in
`plan` output and in `task.json`.

## Layering

Preferences replace earlier values in this order: tool defaults, user config, committed repository
config, task-file defaults, task entry, CLI. Parallelism uses the task file's top-level
`maxParallel`, not `defaults.maxParallel`. Route model and effort have separate provenance.

Restrictions accumulate from user, repository, task-file `policy`, then CLI `--policy`. Deny and
escalate lists append; each scope is another ceiling; maximum access takes the most restrictive
value; `networkAllowed: false` cannot be lifted. Instructions deduplicate by exact text.
Only user and repository route lists participate in intersection.

A tool default above a restriction is clamped and records the limiting layer. An explicit preference
above a restriction fails planning and names both layers. For example, a repository
`maxAccess: read-only` clamps a code task's default workspace access. An explicit task
`access: workspace` instead fails with `access from task exceeds maxAccess from repo`.

Frozen `config.json` records effective values and sources. Recovery uses frozen settings and
policy. `resume --continue` reloads the current user and committed repository allow-list before
starting or re-prompting work; other policy stays frozen.

## Restrictions and rules

| Field | Type | Default | Validation and effect |
|---|---|---|---|
| `deny` | rule array | `[]` | First matching deny rejects; it wins over escalation. |
| `escalate` | rule array | `[]` | First matching escalation queues an ask unless access or a deny blocks it. |
| `scope` | string array | No ceiling | Every layer must permit a changed path. Positive patterns include; `!` patterns exclude. Scope grammar is in [task files](task-file.md). |
| `maxAccess` | string enum | `full` | `read-only`, `workspace`, `full`, in increasing order; bad value: `invalid maxAccess: <value>`. |
| `networkAllowed` | boolean | `true` | Any `false` limits effective network to `off`. |
| `routes` | qualified route array | Unrestricted if all lists absent | User and repository only. Empty means no allowed route. CLI policy refuses this member. |
| `scrub` | array of regular expressions | Built-in scrub only | User and repository only. Adds case-insensitive variable-name patterns. Invalid regex: `<layer>: invalid scrub pattern: <detail>`. |
| Rule `name` | string | Required | Unique across deny and escalate within its layer, non-blank. Effective name becomes `<layer>/<name>`. |
| Rule `action` | string wildcard | Required | Non-blank; examples are `shell`, `edit`, `webfetch`, `websearch`, `permissions`. |
| Rule `resources` | non-empty string array | Required | Every pattern is non-blank. Invalid rule: `<layer>: every rule needs a unique name, action and non-empty resources`. |

Policy wildcards match the whole action and any one resource. `*` matches any text, `?` one
character, backslashes normalize to slashes, and a trailing ` *` also matches no suffix. Windows
matching ignores case; other systems compare case. These are text rules, not shell parsing.

opencode applies rules to tool calls with `policy` enforcement. Codex applies them only when a call
requests approval to leave its sandbox; records use `approval-gate`. Planning warns for each such
Codex rule. A workspace command with network enabled can push without reaching that gate.
An unmatched opencode ask receives `once`; an unmatched Codex ask is declined with feedback.
Form questions are cancelled. Replies never create standing approvals.

## Route allow-list

| Entry form | Type | Default | Validation |
|---|---|---|---|
| `opencode:<provider>/<model>[#<variant>]` | exact qualified route | No suffix means default | Must parse as provider/model; whitespace and empty components fail. |
| `opencode:<agent>` | exact roster agent | No implicit agent | Name matches `^[A-Za-z0-9][A-Za-z0-9._-]*$`; invalid: `invalid agent route: <entry>`. Live checks refuse manual-class or unpinned agents. |
| `codex:<model>[#<effort>]` | exact qualified route | No suffix means default | Model and optional effort contain no whitespace, colon or extra `#`; invalid: `invalid route: <entry>`. |

The harness normalizes `#default` away before intersection. Lists are exact, case-sensitive sets,
not wildcard policies. With both lists present, only their intersection survives. With one, that
list applies. With neither, routes are unrestricted but tasks still need a route or agent. A present
empty list permits nothing; a list with no entry for a backend excludes that backend.

## Example policy

The bundled [example policy](../examples/policy.jsonc) is opt-in and supplies no automatic default.
Its fifteen shell patterns cover plain and executable-suffixed commands, options before `push`,
path prefixes and a closing quote on an executable path:

| Rule | Resource pattern |
|---|---|
| `no-git-push` | `git push*` |
| `no-git-push` | `git * push*` |
| `no-git-push` | `git.exe push*` |
| `no-git-push` | `git.exe * push*` |
| `no-git-push` | `*/git push*` |
| `no-git-push` | `*/git * push*` |
| `no-git-push` | `*/git.exe push*` |
| `no-git-push` | `*/git.exe * push*` |
| `no-git-push` | `*/git.exe" push*` |
| `no-git-push` | `*/git.exe" * push*` |
| `no-gh` | `gh *` |
| `no-gh` | `gh.exe *` |
| `no-gh` | `*/gh *` |
| `no-gh` | `*/gh.exe *` |
| `no-gh` | `*/gh.exe" *` |

Option-first patterns also deny commands such as `git stash push`, `git log --grep push` and later
words containing ` push`, including commit messages. Patterns begin at the start of the whole
command, so `cd x && git push`, aliases and scripts can escape these examples. They also do not
cover every quoted path spelling. On Codex they act only at the approval gate.

## Environment and remaining limits

The harness scrubs secret-like variable names, configured name patterns and credential-bearing URLs
from its root environment. Proxy variables are exempt from URL-value scrubbing, so proxy credentials
can remain readable. It logs removed names, never values. Backend `env` passthrough deliberately
restores named values to that backend process; backend child commands may inherit them. Git and
verify do not receive this passthrough. No backend executable environment override exists.

Task commands receive private temp variables, `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false` and disabled
MSBuild server reuse. Verify also disables node reuse and shared compilation. Git's private runner
receives the root-scrubbed environment, whose name pattern removes `SSH_AUTH_SOCK` too. Tasks cannot
rely on an inherited SSH agent socket. `HOME`, `USERPROFILE`, installed tools and other
machine state are not isolation boundaries.

Verify runs worker code as the user outside sandboxing on both backends. It can write the control
folder or influence other tasks. opencode's shell can write the ledger and control folder despite
edit-tool denies. The Git configuration guard detects relevant changes and disables hooks, but a
same-user process can race the check or populate a hooks folder before Git uses it. In-place diffs
also include anyone else's concurrent edits. Treat these as operational limits.
