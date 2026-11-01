Read when you write a task file or need a field's default or validation error.

# Task files

Write UTF-8 JSONC with `schemaVersion: 2`. Comments and trailing commas are allowed. Member names
are case-sensitive. Unknown members fail deserialization, including the removed `fallback` and
`maxMinutes` fields. Typed objects accept and ignore `$schema` and `$comment`. Version 1 is not read;
the conversion diagnostic says to add version 2, give every task a kind, select a backend, rename
`maxMinutes` to `timeoutMinutes`, and delete `fallback`.

Use an editor tool to write the file. Run `plan` before `run`; both validate before dispatch.
See [CLI](cli.md) for invocation and [configuration](config.md) for layering.

## Top level

| Field | Type | Default | Validation and meaning |
|---|---|---|---|
| `schemaVersion` | integer | Required, `2` | Other old versions: `schemaVersion must be 2; version 1 is not read`. A newer version: `written by a newer dispatch; update the plugin`. |
| `repo` | string path | None | Relative to the task file, or absolute. `--repo` overrides it. Repository tasks require one: `task needs repo or --repo`. An explicit target must be a Git working tree. |
| `run` | string | Task filename without extension and a trailing `.tasks` (`x.tasks.jsonc` gives `x`) | `run` uses `--run` first. Name must match `^[a-z0-9][a-z0-9-]{0,40}$`; the harness prefixes the UTC calendar day to form the recorded run id. |
| `maxParallel` | integer | `3`, after lower layers | Range 1 through 8: `maxParallel from <layer> must be between 1 and 8`. |
| `defaults` | object | No overrides | Only the preference fields below, plus `defaultRoutes` and `dir`. It cannot supply arbitrary task fields such as `base`, `prompt`, `scope`, `prepare` or `verify`. |
| `policy` | object | No added restrictions | Accepts `deny`, `escalate`, `scope`, `maxAccess`, `networkAllowed`, `instructions`. See [configuration](config.md). |
| `contracts` | string array | `[]` | File paths prepended to every task. See [Contracts](#contracts) for resolution and plan errors. |
| `sharedPaths` | string array | `[]` | Exempts these paths from writer scope-overlap detection only. Refuses blank, absolute, parent paths and `*`, `?`, `!`: `sharedPaths must contain repository-relative paths, not globs or parent paths`. It grants no write permission. |
| `tasks` | task array | Required | `tasks must be a non-empty array`. Every entry needs `kind`; ids must be unique. |

`promptFile`, contract paths, `input.itemsFile` and `result.schema` resolve from the task file's
folder, or may be absolute. `dir` has a different base: the repository root.

## Task identity and content

| Field | Type | Default | Validation and meaning |
|---|---|---|---|
| `id` | string | Required | `id must match ^[a-z0-9][a-z0-9-]{0,39}$`; duplicate ids fail. Use portable folder names. |
| `tags` | string array | `[]` | Optional caller-owned labels, such as a work item and `scan-first` or `scan-again`. Each must match `^[a-z0-9][a-z0-9-]{0,39}$`; otherwise `tag <tag> must match ^[a-z0-9][a-z0-9-]{0,39}$`. They do not affect execution and are retained in `task.json`'s spec for `usage --by tag`. |
| `kind` | string enum | Required | `code`, `review`, `research`, `mechanical`, `structured`; otherwise `kind must be one of code, review, research, mechanical, structured`. |
| `route` | string | Effective backend's `defaultRoutes` entry | Exactly one effective route or `agent`: `name exactly one of route or agent after merging`. The catalog must offer the model and variant or effort. |
| `agent` | string | None | opencode roster agent with a pinned model. Codex: `codex refuses agent and persona`. An inherited default route also counts against the exactly-one rule. Manual-class agents are refused. |
| `persona` | string | `build` for an opencode route | `persona goes with route, not with agent`. Must exist and must not be manual-class. Codex refuses it. A user-defined persona needs `profile: full`: `clean` (the default for every task kind) loads no user agents, and plan refuses with `persona <x> not found: profile clean loads no user agents`. |
| `prompt` | string | None | Exactly one of `prompt` and `promptFile`; otherwise `name exactly one of prompt or promptFile`. Whitespace-only text: `prompt is empty`. |
| `promptFile` | string path | None | Same exclusivity as `prompt`; missing file: `promptFile not found: <path>`. |
| `contracts` | string array | `[]` | File paths added after top-level contracts and before CLI contracts. Same resolution and validation as top-level [Contracts](#contracts). |
| `base` | string Git ref | Required for a non-structured worktree without `reviewOf` or `continueOf` | `base is required (a branch, tag or commit of the repo)` or `base <ref> does not resolve in <repo>`. A remote-tracking base is fetched before resolution; plan and run print `INFO <task>: base <base> resolves to <12 hex digits> (fetched from <remote>)`, omitting the suffix for local branches, tags and SHAs (no fetch). A prompt naming a different base commit produces a warning, not a refusal; [plan](cli.md#plan-tasksjsonc---repo-path---contract-path---policy-file) owns matching and warning text. In-place refuses it. |
| `reviewOf` | string task id | None | Starts from that task's branch after it finishes. Refuses self or missing target: `reviewOf names no other task`; refuses chains: `reviewOf chains are not supported`. With `base`: `a reviewOf task starts from the reviewed task's branch; omit base`. A non-review kind produces a warning. Use a worktree author. |
| `continueOf` | string `<run>/<task>` | None | Task-entry only. A new code or mechanical attempt from an eligible failed task's collected work; `prompt` or `promptFile` is its amendment. Refuses `base`, `reviewOf` and effective in-place workspace. See [Continuations](#continuations). |
| `scope` | string array | `[]` | `code and mechanical tasks declare a scope` when absent or empty. Structured tasks refuse it. Every changed path on review or research is out of scope regardless of this field. |
| `prepare` | string array | No commands | Task-entry only, never `defaults`. Unset/empty runs and records nothing, including for structured tasks. Every non-structured kind accepts it; a non-empty structured value refuses with `prepare is forbidden for structured tasks`. See [Preparation](#preparation). |
| `verify` | string array | No commands | Runs after collection for a succeeded non-structured task. Structured tasks refuse it. A missing or unsupported shell is a plan error. |
| `advisoryVerify` | string array | No commands | Runs after `verify`, in the same shell and folder, and is recorded with `advisory: true`, but a failure never fails the task: it adds the `advisory-verify-failed` warning. For a check that fails by design on this change (a baseline the coordinator owns), so a dependent `reviewOf` task still starts. Structured tasks refuse it. |
| `shell` | string enum | `pwsh` on Windows, `bash` elsewhere | `bash` or `pwsh`, resolved on PATH. Other values: `prepare shell: shell must be bash or pwsh` without verify or advisory-verify commands, otherwise `verify shell: shell must be bash or pwsh`; structured: `shell is forbidden for structured tasks`. Review and research can also use it. |
| `dir` | string relative directory | `defaults.dir`, then `.` | In-place: existing directory inside the same repository working tree. Errors: `in-place dir must be relative to and inside the repository`, `in-place dir does not exist`, `in-place dir belongs to another working tree`. Worktree tasks do not use it. Project-enabled structured tasks use their task entry's `dir`. |
| `input` | object | Required for structured | See structured fields below. Non-structured tasks do not consume structured input. |
| `result` | object | Required for structured | See structured fields below. |

## Preferences

These fields may appear in user or repository `preferences`, task-file `defaults`, or a task.
Corresponding command-line flags, where available, win last. A backend has no tool default for a task.

| Field | Type | Tool default | Validation and meaning |
|---|---|---|---|
| `backend` | string enum | Required after layering | `opencode` or `codex`; invalid choices name the value, layer and allowed values. |
| `effort` | string | Route suffix or catalog default | Non-empty. A suffix and a different `effort` in the same layer fail: `route variant and effort disagree in <layer>`. A later layer wins. |
| `timeoutMinutes` | integer | `60` | Range 1 through 720: `timeoutMinutes from <layer> must be between 1 and 720`. Each verify command separately has a 30-minute timeout. |
| `profile` | string enum | `clean` for every task kind | `clean` or `full`; explicit values at any layer override. Actual clean isolation may be partial; see [backends](backends.md). |
| `project` | string enum | `off` for structured, otherwise `on` | `on` or `off`. Controls backend repository context. Non-structured tasks still need a repository and workspace. |
| `access` | string enum | `workspace` for code and mechanical, otherwise `read-only` | `read-only`, `workspace`, `full`. An explicit value above `maxAccess` fails and names both layers; the tool default is clamped. |
| `network` | string enum | `on` | `on` or `off`. An explicit `on` forbidden by policy fails; the tool default is clamped. Codex refuses `full` access with network `off`. |
| `workspace` | string enum | `worktree` | `worktree` or `in-place`. Structured tasks use scratch or a named project folder instead of Git workspace preparation. |
| `tempRoot` | string, absolute path | Unset | Private temp root; relative or empty values fail with `tempRoot must be an absolute path`. No CLI option; [configuration](config.md#temporary-folders) owns layering and provenance. |
| `environment` | object of variable names to strings | `{}` | Merge by name with value/source provenance; invalid layers and harness-owned names refuse planning. No CLI flag; [configuration](config.md#configured-environment). |
| `privateFolders` | string array of absolute paths | `[]` | Layers join; enforced only on Linux fenced opencode, with automatic harness-cache protection. No CLI flag; [configuration](config.md#private-folders). |
| `commit` | string enum | `none` in-place; forced `harness` for worktrees | `none` or `harness`. A worktree task entry naming it fails: `task commit is only valid in-place`. Structured tasks never commit. |
| `rawPrompt` | boolean | `false` | Sends only task text with a normalized final newline. Contracts, standing instructions or structured input conflict: `rawPrompt conflicts with contracts, instructions or structured input`. Rules and scope checks still apply. |
| `defaultRoutes` | map of backend to string, defaults only | No route | In task-file `defaults`, user or repository preferences. Backend-specific routes provide defaults, not a backend's implicit model selection. |

The `readOnlyShell` preference is described in
[configuration](config.md#read-only-shell-preference).

## Routes and kind defaults

opencode routes use `<provider>/<model>[#<variant>]`; `agent` selects a roster entry instead.
Codex routes use `<model>[#<effort>]`. Records and allow-lists qualify them with the backend.
An omitted suffix and `#default` have the same allow-list identity. A default route is still an
explicit configured choice; the harness never lets a backend choose an unspecified model.

The resolver takes the last route's model and the last explicit suffix or `effort`. A later route
without a suffix does not clear an earlier effort. Do not combine a default route with `agent`.

| Kind | Profile | Project | Access | Working folder | Reason |
|---|---|---|---|---|---|
| `code` | `clean` | `on` | `workspace` | Worktree | Needs repository context and writes. |
| `mechanical` | `clean` | `on` | `workspace` | Worktree | Makes bounded repository edits. |
| `review` | `clean` | `on` | `read-only` | Worktree | Reads repository context; every change fails scope. |
| `research` | `clean` | `on` | `read-only` | Worktree | Reads repository context; every change fails scope. |
| `structured` | `clean` | `off` | `read-only` | Projectless scratch | Transforms supplied items without Git settlement. |

For backend-qualified task routes, see
[configured backend coverage](cli.md#configured-backend-coverage).

## Contracts

Prepend order is top-level `contracts`, task `contracts`, then repeatable `--contract` flags in CLI
order. The harness resolves paths, deduplicates each task's list by absolute path and preserves the
first occurrence. It reads each distinct file once during planning within a command. It removes a
leading BOM, preserves the whole file's text, including line endings and final-newline presence,
and adds separators between contracts. The caller selects the files; none are added by default.
The harness depends on no other skill and does not discover contract files.

Task-file paths resolve from the task file's folder or may be absolute; `--contract` requires an
absolute path. An entry beginning with `@` at any level refuses planning with
`contract aliases were removed; name the file path: <entry>`, before file or cache lookup.
Missing or unreadable files and invalid UTF-8 also refuse planning; contract content is strictly
decoded as UTF-8.

Contracts precede standing instructions, task text, and structured items and schema. Both backends
receive the assembled text unchanged as one user input. `task.json` records each path, source,
SHA-256 of the original file bytes including any BOM, and the first line with trailing whitespace
trimmed and cut to 120 characters. Frozen configuration carries the same metadata. The `prompt` event records
contract hashes. No contract files are copied into the ledger.

The harness's own standing instructions name the working folder, any deny rules, the declared scope,
read-only access, that nobody answers questions and, except for structured tasks, the status line:
the worker ends its final message with `Status: done`, `Status: stopped` (it stopped on a rule or a
contradiction) or `Status: blocked` (a missing tool, access or input kept it from the work).
Settlement reads the last such line; see `workerStatus` in [the ledger](ledger.md).

`resume --continue` of a started opencode session sends a fixed continuation line and does not read
the files again. An unprompted task with contracts cannot be reconstructed from recorded hashes:
it fails with `original contract text is unavailable; cannot start an unprompted task from the ledger`.

## Scope and workspaces

With `tempRoot`, the private temporary directory is `<tempRoot>/<run>/<task>`, for worktree,
in-place and structured workers. `<run>` is the dated recorded run id. Workers and ordinary and
advisory verify commands receive that same directory in `TEMP`, `TMP` and `TMPDIR`; verify still
runs in the task's working folder. Unset, the layout stays `<worktree root>/<run>/.tmp/<task>`
(or `<worktree root>/.dispatch-tmp/<task>` without a run). `plan` prints paths without creating them.

Scopes use repository-relative paths and globs. A plain path covers itself and descendants; `*`
and `?` stay within a segment, `**` crosses directories, and `**/` also matches no directory.
Backslashes normalize to slashes, leading `./` and `/` are removed, and a trailing slash means all
descendants. A leading `!` excludes a match. Brackets and braces are literal. Each restriction
layer's scope is another ceiling. Settle uses the repository's `core.ignorecase`; overlap checks
use Windows case-insensitivity and case-sensitivity elsewhere and compare positive patterns.

Only code and mechanical tasks participate in writer overlap checks. `sharedPaths` removes named
paths from that overlap calculation, and the run event records that the coordinator merges them.
Two in-place writers in one run are refused even with disjoint scopes. Other in-place tasks run
one at a time in task-file order. Worktree paths and branches must not already exist.

A `reviewOf` task runs only if its author finished done with a succeeded outcome and a new commit.
Otherwise the scheduler cancels the review. Plan checks that the target exists and has no review
chain; it does not ensure that the author will produce a worktree commit.

In-place tasks refuse `base` and `reviewOf`. Before dispatch, the harness snapshots tracked and
untracked non-ignored files through a temporary index without changing the operator's index.
With `commit: none`, settle diffs the end snapshot against the start snapshot, including new
untracked files and excluding pre-existing edits. Concurrent operator edits enter that diff too.

With `commit: harness`, plan and run refuse a dirty working tree or an unborn HEAD. If it becomes
dirty before task start, the task fails with `workspace-dirty` before a worker starts. On a clean
folder the harness commits on the current branch with hooks disabled. Worktree tasks commit on their
own branch. The harness never stages `.dispatch/`; its changes always fail scope. Verify runs in
the task folder in a scrubbed environment, outside backend sandboxing, with no shell profiles.

For in-place tasks, put `dir` and `commit` in the task entry when their validation matters. The
current planner checks the entry values while preparation uses the effective directory and commit
settings, so a value inherited from defaults or CLI may reach a later start check instead.

An in-place run refuses a worktree or ledger root inside the repository. `clean` removes its temp
and temporary index, but leaves the in-place folder, edits and current branch alone.

## Continuations

Use `continueOf: "<recorded-run>/<task>"` in a new run. It is not a preference, default, config
field or CLI flag. The run component uses lowercase letters, digits and hyphens, beginning with a
letter or digit; the task component follows the normal task-id grammar. Exactly one forward slash
is required. Absolute paths, traversal, backslashes and extra components refuse with
`continueOf must name <run>/<task> using safe run and task names`.
Resolution uses only the currently configured ledger root (including `--ledger`), and the usual
full-id/unique-bare-name rules. Ambiguous names list matches; missing runs and tasks name the missing
evidence. The source must be a distinct recorded run, not another task in the new run. Bare-name
matching excludes the current run, including when a task-file name is reused on a later day.

Only `code` and `mechanical` accept the field. Local diagnostics are `continueOf requires kind code
or mechanical`, `continueOf refuses base`, `continueOf refuses reviewOf`, and `continueOf refuses
in-place workspace`. Mandatory scope, contracts, routes, shells, overlap and workspace-collision
checks still apply. An invalid continuation does not also require `base`.

The source kind must be `code` or `mechanical`; otherwise the shared plan/preparation check refuses
with `continueOf <run>/<task>: its kind <kind> holds no work to continue`.
The source's event-derived state and result must both be failed, with consistent identities. It
must have a recorded harness-managed worktree and harness commit mode, no `zombie` failure and no
current unconfirmed-worker evidence. Eligibility is parsed `workerStatus: stopped` or `blocked`,
or otherwise a recorded `commit` (a committed failed target, commonly a verify failure).
For a source without stopped/blocked worker status, a `commit-failed` failure or an `outOfScope`
path absent from `filesChanged` refuses: `continueOf <run>/<task>: its uncommitted work is not in the
ledger: <sorted paths or commit-failed>`. The paths are ordinal-sorted; `commit-failed` is used only
when there are no such paths. An ordinary verify failure with no leftovers remains eligible.
The latest settlement must have a matching `collected` event and readable `final.md`.
For stopped/blocked work, a nonempty collection must name exactly `wip.patch`, with matching file
count and an existing artifact; inconsistent metadata, stray or missing WIP, and incomplete
collection refuse. No patch is valid for a completed empty snapshot.

Source refusals identify a `consistent final failed result`, a `zombie or unconfirmed worker`, a
missing `harness-managed worktree with harness commit mode`, an ineligible target, or `source
collection evidence is incomplete or inconsistent`. Both the original `workspace.baseSha` and the
creation commit must resolve as commits in the destination repository; missing objects are not
fetched. Every inherited `filesChanged` path must pass the new task's effective scope, exclusions
and restriction ceilings, using repository `core.ignorecase`, plus protected `.dispatch` checks.
One diagnostic lists all offenders in ordinal order: `continueOf inherited paths outside scope: ...`.
Successful planning prints `INFO <task>: continues <resolved-run>/<task> from <12 hex digits>`.
If the source `run.json` records a different repository path, plan warns:
`<task>: continueOf <run>/<task> was recorded in <repo>, not this repository; commit ids resolve, but its base ref may mean another commit here`.
It remains a warning because worktrees of the same repository have different paths.

Preparation rechecks the source using the new task's frozen settings. A stopped/blocked source starts
at its original `baseSha` and replays WIP with `git apply --index --whitespace=nowarn -- <patch>`;
otherwise it starts at the source's result commit with no replay. Replayed changes are staged but
uncommitted, and HEAD stays at the worker's `startSha`. The inherited `baseRef` and original
`baseSha` remain the settlement baseline, so another stop collects the cumulative change, including
earlier committed and uncommitted work, not just the latest amendment.

The preservation contract is the collected Git tree: tracked edits, deletions, non-ignored untracked
files and worker commits, not ignored outputs or original staging state. Git conversion, attributes
and filters still apply. Binary hunks are replayed without text conversion by the harness. LFS carries
pointer blobs and submodules carry gitlink IDs, not payloads, submodule-local edits or object transport;
normal object/filter availability is required. No submodule initialization is added. Replay uses the
new run's Git configuration baseline and all ordinary write protections, never the source's baseline.
A case-only rename on a case-insensitive file system cannot be replayed and fails preparation with
Git's message. A worker's untracked nested repository arrives as an empty folder and a commit id,
not its working files or object database.

The new prompt names the resolved source, fences its complete `final.md` safely, then includes the
amendment. The new task's contracts and standing instructions precede that composed task text;
`rawPrompt` still omits them. It uses a new worker, branch and worktree, not the source session.
The first composed line says collected changes are already in the folder only when the source has
`wip.patch` or a `commit`; otherwise it says the earlier attempt left no changes. The standing start
line distinguishes the new worker's start commit from the original `workspace.baseSha`, which
remains the baseline for judging the final tree.
Source ledgers are read without creating directories, locks or repairing torn tails. No source lock
is taken: a final target stays readable while sibling tasks remain supervised. A queued task uses
the latest eligible source evidence when prepared, not a plan-time lease.

The source worktree may already be cleaned if retained evidence and required commits still resolve.
After the successful `worktree` event, recovery uses only the new record/workspace and does not
reread or replay the source. A saved workspace without that ready event refuses with `continueOf
workspace preparation is incomplete; refusing recovery`. Apply failure retains owned workspace and
composed text for inspection/cleanup, writes the existing workspace-start error/final events with
Git's diagnostic, and runs no worker, shell prepare, collection or verify; `result.json` is not
guaranteed. Unprompted tasks with contracts retain the existing contract-body recovery limitation.

`collect`, `status` and `clean` stay run-local and never traverse `continueOf`. Cleanup of either run
does not own the other's worktree or branch. Inherited `baseRef` can disappear when the source used
a reviewed branch; ordinary non-force cleanup then remains conservative, not guaranteed to succeed.
Manual deletion, forced pruning and unavailable LFS/submodule objects are not covered.

## Preparation

`prepare` is an ecosystem-neutral list of shell commands. After workspace preparation and the
allow-list check, it runs before launch construction or worker creation, while the task remains
`dispatching` and holds its concurrency slot. Run-level probes and validation are unchanged.
Commands run sequentially as the operator, outside backend sandboxing, in the workspace, with the
task's `bash` or `pwsh` shell and no shell profile. They use the scrubbed worker environment, resolved
configured `environment` and recorded private task temp, with below-normal priority
(`nice -n 10` on Unix), and a 30-minute timeout per command. `timeoutMinutes` does not apply;
the worker's timeout clock still starts after prompting. Unlike verify, prepare stops at the first
nonzero exit, timeout or start exception. It tells the worker nothing automatically.

Within the workspace, preparation may change ignored outputs only. Before commands, a temporary-index snapshot captures
tracked and non-ignored untracked content, including existing in-place dirt, and HEAD is read
(`unborn` is valid). Both remain in memory. After successful commands, both are compared again.
Tree changes use `git diff --name-only --no-renames <before> <after>`, ordinal-sorted,
retaining 20 comma-separated paths, then `, and <k> more`. A HEAD-only change reports `prepare moved HEAD from <a> to <b>`.
Ignored `obj/`, `bin/`, `node_modules/` and `.pkgs/` outputs do not count when the repository ignores them.

Cancellation is checked before each command, during execution, before the workspace check and
immediately before worker creation. Precedence is cancel, command failure (skip the check), Git
failure, then workspace change. Tasks without nonempty prepare keep their existing cancellation path.
Success alone completes preparation and enters the ordinary worker lifecycle. Other outcomes settle
without a session, collection, commit, verification or worker calls; the workspace stays for inspection.
Evidence, exact terminal outcomes and failure codes: [ledger](ledger.md#prepare-evidence-and-outcomes).

Preparation is skipped when already done or when a session exists. A never-started prepare can run
on `resume --continue`; an interrupted attempt is never replayed. Recovery and operator steps:
[resume](cli.md#resume-run---continue---ledger-dir). Dependency recipes and process limits:
[guide](guide.md#dependencies-and-the-sandbox).

## Structured tasks

Structured tasks refuse `prepare`, `scope`, `verify`, `advisoryVerify`, `shell`, `reviewOf`, `agent`, `persona` and raw prompts.
With `project: off`, the harness chooses scratch outside any ancestor containing `.git`; neither
repository nor base is needed. With `project: on`, it uses the named repository folder. Structured
tasks never commit or verify.

| Field | Type | Default | Validation and meaning |
|---|---|---|---|
| `input.items` | array of objects | None | Exactly one of `items` or `itemsFile`: `structured input needs exactly one of items or itemsFile`. |
| `input.itemsFile` | string path to array | None | Same exclusivity; file must parse as an array. |
| `input.idField` | string | `id` | Every input object needs a unique string at this key: `input: each item must have a unique string <field>`. |
| `result.schema` | string path | Required | Reads a JSONC schema with an object root: `schema#: expected an object root`. Unsupported keywords fail planning. |
| `result.itemsAt` | string JSON Pointer | Required | Empty string selects the root array; otherwise starts with `/`, using `~0` and `~1` escaping. Invalid result options: `structured result needs schema, a JSON Pointer itemsAt and identity exactly-once or many`. |
| `result.identity` | string enum | `exactly-once` | `exactly-once` requires every input id exactly once. `many` permits omissions and repeats. Both reject unknown ids and non-string ids. |
| `result.immutable` | string array | `[]` | Names of direct fields present on every input and deeply equal on every corresponding answer. Missing input field: `input: id "<id>" lacks immutable field "<field>"`. |

Return one JSON document, or exactly one fenced `json` block. The harness rejects duplicate JSON
members. It applies the schema, then identity and immutable checks; failures carry JSON Pointers.
Valid output is normalized into `deliverable.json`. An invalid recollection deletes an older valid
deliverable. Frozen `input.json` and `result.schema.json` have hashes in `config.json`; recovery
uses those copies and fails validation on a hash mismatch.

### Supported schema subset

These keywords follow JSON Schema 2020-12 meaning. All others are refused as
`schema#<pointer>: <keyword>: unsupported keyword`. `format` is an annotation, not a check.

| Keyword or form | Type | Default when absent | Validation |
|---|---|---|---|
| Boolean subschema | boolean | No added check | Accepted below the object root. `false` rejects every value. |
| `type` | string or array of unique strings | Any type | Object, array, string, number, integer, boolean or null. Invalid list: `expected a type name or a non-empty array of unique type names`. |
| `properties` | object of schemas | No named checks | `expected an object of schemas`. |
| `required` | array of unique strings | No required members | `expected an array of unique member names`. |
| `additionalProperties` | schema or boolean | Allowed | Must be a schema. |
| `items` | one schema or boolean | No element check | Array-form schemas fail: `expected an object or boolean schema; array-form items is not supported`. |
| `enum` | array | No choice check | `expected an array`. |
| `const` | any JSON value | No constant check | Deep equality. |
| `anyOf` | non-empty array of schemas | No alternatives | `expected a non-empty array of schemas`; cycles without an instance step fail. |
| `$ref` | string local schema pointer | No reference | Must resolve to a schema visited by the validator: `expected a resolvable local schema pointer`. Remote references fail. Cycles without an instance step fail. |
| `$defs` | object of schemas | No definitions | `expected an object of schemas`. |
| `definitions` | object of schemas | No definitions | Same as `$defs`. |
| `minLength` | non-negative integer | No lower bound | Counts Unicode code points; `expected a non-negative integer`. |
| `maxLength` | non-negative integer | No upper bound | Same counting and error as `minLength`. |
| `minimum` | number | No lower bound | Inclusive; `expected a number`. |
| `maximum` | number | No upper bound | Inclusive; `expected a number`. |
| `exclusiveMinimum` | number | No lower bound | Exclusive; `expected a number`. |
| `exclusiveMaximum` | number | No upper bound | Exclusive; `expected a number`. |
| `$schema` | annotation | None | Root only: `allowed at the root only`. |
| `$id` | annotation | None | Root only; does not enable remote resolution. |
| `$comment` | annotation | None | Accepted and ignored. |
| `title` | annotation | None | Accepted and ignored. |
| `description` | annotation | None | Accepted and ignored. |
| `default` | annotation | None | Accepted and ignored. |
| `examples` | annotation | None | Accepted and ignored. |
| `deprecated` | annotation | None | Accepted and ignored. |
| `readOnly` | annotation | None | Accepted and ignored. |
| `writeOnly` | annotation | None | Accepted and ignored. |
| `format` | annotation | None | Accepted and ignored. |

## Examples

Replace route and branch placeholders with catalog entries and an existing ref. Supply the target
repository with an absolute `--repo` argument.

Code followed by review on the other backend:

```jsonc
{
  "schemaVersion": 2,
  "tasks": [
    { "id": "edit-guide", "kind": "code", "backend": "codex", "route": "<model>#<effort>",
      "base": "<branch>", "prompt": "Fix the typo in the guide.", "scope": ["docs/guide/"],
      "verify": ["<verification command>"] },
    { "id": "review-guide", "kind": "review", "backend": "opencode", "route": "<provider>/<other-model>",
      "reviewOf": "edit-guide", "prompt": "Review the guide change. Return findings without editing files." }
  ]
}
```

In-place mechanical edit without a commit:

```jsonc
{
  "schemaVersion": 2,
  "tasks": [
    { "id": "normalize-guide", "kind": "mechanical", "backend": "codex", "route": "<model>",
      "workspace": "in-place", "dir": ".", "commit": "none", "scope": ["docs/guide/"],
      "prompt": "Normalize the guide's whitespace." }
  ]
}
```

Structured transformation, with a schema in `answer.schema.json` beside the task file:

```jsonc
{
  "schemaVersion": 2,
  "tasks": [
    { "id": "label-items", "kind": "structured", "backend": "codex", "route": "<model>",
      "prompt": "Add a short label to each item, preserving its id and text.",
      "input": { "items": [{ "id": "item-a", "text": "Example" }] },
      "result": { "schema": "answer.schema.json", "itemsAt": "/answers", "immutable": ["text"] } }
  ]
}
```

```json
{
  "type": "object",
  "required": ["answers"],
  "additionalProperties": false,
  "properties": {
    "answers": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["id", "text", "label"],
        "additionalProperties": false,
        "properties": { "id": { "type": "string" }, "text": { "type": "string" }, "label": { "type": "string" } }
      }
    }
  }
}
```

## Resolving plan errors

Field tables name the local errors. Configuration errors identify their source layer; fix that
layer rather than adding a weaker later policy. Live validation also refuses missing or disabled
models, unsupported effort, unpinned or manual-class agents, and unready Windows sandboxing.
Choose a listed route or prepare the backend, then plan again.

| Diagnostic | Cause | Fix |
|---|---|---|
| `<a> and <b> share scope (<patterns>)` | Writer globs overlap outside `sharedPaths`. | Split the scope or explicitly coordinate the shared paths. |
| `<a> and <b> write in the same in-place working tree` | Two in-place code or mechanical tasks. | Split the runs or use separate worktrees. |
| `worktree <path> already exists; clean it first` | A previous task used the computed path. | Recover and clean the old run before retrying. |
| `branch dispatch/<run>/<task> already exists; clean it first` | The task branch exists, possibly under another ledger root. | Recover that run or choose a new run name. |
| `in-place workspace refuses a worktree root inside the repository` | Snapshot would capture generated workspaces. | Move the configured worktree root outside the repository. |
| `in-place workspace refuses a ledger root inside the repository` | Snapshot would capture ledger writes. | Select an external ledger root. |
| `structured scratch needs a projectless folder` | Every candidate has a `.git` ancestor. | Make the system temp or state location projectless. |
| `reviewer and author share lineage <lineage>` | Same-backend live checks identify the same model lineage; this is information, not a refusal. | The caller owns review policy and may select a different reviewer lineage. Cross-backend checks do not compare lineage. |
