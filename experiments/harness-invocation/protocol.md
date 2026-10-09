# Harness probing

Date: 2026-09-07

This protocol owns the repeatable method for finding out what a harness actually does. Committed
observations live in [`records/`](records/); research such as
[skill-invocation-across-harnesses.md](../../docs/research/skill-invocation-across-harnesses.md)
synthesizes and cites those records rather than owning the method or observations.

## Why this exists

Every emitter in this repo encodes an assumption about a harness. Documentation has been wrong about
those assumptions more than once — `OPENCODE_CONFIG_DIR` is documented as covering agents, commands,
modes and plugins, and covers skills too; `OPENCODE_DATA_DIR` is documented and does nothing in
1.18.7. Inference has been wrong more often than the documentation.

**Measure before you write it down.** A claim in `docs/research/` should name the version it was
measured on. A claim that was reasoned rather than run should say so.

## Build a lab

Use an empty directory outside both the repository and the real user profile. A lab holds three
things: fixture skills to probe with, the isolated harness homes, and a results file.

```
<lab-root>/
  fixtures/          probe skills, tracked
  .claude-home/      CLAUDE_CONFIG_DIR      (gitignore — holds credentials)
  .opencode-home/    OpenCode lab root: home, XDG roots, config/ (OPENCODE_CONFIG_DIR), database (gitignore)
  codex-run-*/       isolated CODEX_HOME plus raw JSON (gitignore)
  installer-local/   fresh packed-package profile and npm cache (gitignore)
  installer-release/ fresh Release-package profile and npm cache (gitignore)
  RESULTS.md
```

Give every probe a **nonsense trigger word** (`ZEBRA-FLAGGED`) and a body that instructs a literal
reply (`H1-RAN`). That is what separates "the model chose to fire this skill" from "the model is
being agreeable", and it lets a probe survive a noisy listing.

## Isolate

The harnesses isolate differently. Getting this wrong wastes a round.

| Harness | Variable | Behaviour |
|---|---|---|
| Claude Code | `CLAUDE_CONFIG_DIR` | **replaces** the config root. Setting it is enough |
| OpenCode 2 | `OPENCODE_CONFIG_DIR` | **replaces** the global config root (measured on 2.0.23). The installer composes into it, and the CLI reads its `service.json` there |
| OpenCode 2 | `HOME`, `USERPROFILE`, `OPENCODE_TEST_HOME` | anchor the always-on `~/.claude/skills` and `~/.agents/skills` compatibility roots. Relocate all three |
| OpenCode 2 | `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `XDG_STATE_HOME`, `XDG_CACHE_HOME`, `OPENCODE_DB` | data, state, cache, logs, and the database. Relocate all of them; upstream's own `packages/cli/script/service-smoke.ts` uses the same set |
| OpenCode 2 | `OPENCODE_DISABLE_PROJECT_CONFIG=1` | skips the project walk, which otherwise collects `.claude`, `.agents`, `.opencode`, and `opencode.json(c)` from every ancestor of the working directory up to the filesystem root |
| Codex | `CODEX_HOME` | replaces config, auth, plugin records, marketplace records, cache, and run-local temporary state for the CLI |

`Get-OpenCodeLabEnvironment` in [`common.ps1`](common.ps1) holds the OpenCode set for one lab root,
and `Use-OpenCodeIsolation` applies it to the shell for `<lab>/.opencode-home`. The evidence for
each row is in
[OpenCode 2 as the only target](../../docs/research/opencode-2-target.md#2-configuration-roots-project-walk-and-the-background-service).

Three OpenCode-specific traps:

- **The background service ignores your shell.** By default the OpenCode 2 CLI talks to a
  long-lived managed service on a fixed port, and that service reads the environment it started
  with, so `OPENCODE_CONFIG_DIR` or `XDG_*` in the caller's shell never reaches it. Write
  `{"disabled":true}` to `<OPENCODE_CONFIG_DIR>/service.json`, which makes CLI commands start a
  private server, or run your own `opencode serve --hostname 127.0.0.1 --port <port>` with the lab
  environment and a password, and stop it in a `finally` block.
- **`~/.claude/skills` and `~/.agents/skills` are always on.** OpenCode 2 has no switch that turns
  them off, so relocating home is the isolation. Put the lab outside the repository and the real
  profile, or keep the project walk off: with the walk on, a lab below the real home still collects
  the real `~/.claude/skills` as an ancestor `.claude` directory.
- **One-shot introspection races the location load.** The CLI has no skill or command listing, and
  `opencode debug agents` or `opencode api --standalone skill.list` returned empty lists, because
  the first request arrives before the location has loaded. Ask a persistent `serve` and poll.

Claude Code keeps credentials inside its config dir, so a fresh one demands a new login. A lab may
be seeded once with `.credentials.json` on Windows or Linux, as the authentication docs describe,
but do not clone that rotating OAuth file per attempt: concurrent copies are reported to invalidate
one another (<https://github.com/anthropics/claude-code/issues/76561>). For unattended automation,
the documented mechanism is `claude setup-token` plus `CLAUDE_CODE_OAUTH_TOKEN`
(<https://code.claude.com/docs/en/authentication>). For an OpenCode TUI session the equivalent is
a credential in the isolated database. OpenCode 2 keeps credentials in `OPENCODE_DB`; a migration
imports a legacy `<XDG_DATA_HOME>/opencode/auth.json` when it creates the database
(`packages/core/src/database/migration/20260805200742_import_legacy_credentials.ts`, source only,
unmeasured). Never track either file.

Verify the isolation before trusting a result, with the positive control in the same breath: an
isolated OpenCode 2.0.23 `serve` with an empty config lists only the built-in skills `opencode` and
`report`, the built-in commands `init` and `review`, and the built-in agents `build`, `plan`,
`general`, `explore`, `compaction`, `title`, and `summary` (measured with
[`oc2-discovery.ps1`](oc2-discovery.ps1), which also fails when a skill resolves outside the lab;
built-ins report `/builtin/<id>.md`); an isolated Claude Code with nothing mounted lists only the
harness's own bundled skills, and its `init` event reports the mounted plugins and
`mcp_servers: 0`. A behavioural panel also disables and checks auto memory as described below.

## Prove installer composition

The repository checkout and the shipped package are separate seams. `Sync-Lab` uses the checkout
entrypoint against the lab's `OPENCODE_CONFIG_DIR`. A release claim uses the packed `deniz-skills`
bin, never the TypeScript source. Run the free subsystem checks first:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1
```

That suite creates fresh temporary XDG roots and requires Plan to leave the Destination absent and
Apply to write nothing outside `skills/`, `commands/`, `agents/`, and `.deniz-skills/`. It also
requires the `oc2-discovery.ps1` dry run to place every OpenCode root below its lab. Its separate lab
checks prove the experiment runners reach the end of their dry-run paths.

### Local packed package

Run from a dedicated PowerShell after dot-sourcing `lab.ps1`. The profile, OpenCode data, and npm
cache all stay under the external lab, and `OPENCODE_CONFIG_DIR` is the profile's `config/`.

```powershell
. .\experiments\harness-invocation\lab.ps1
$profile = Join-Path $LAB "installer-local"
$packDir = Join-Path $profile "package"
Remove-Item $profile -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $packDir -Force | Out-Null

npm --prefix $REPO run build
$packed = npm pack $REPO --json --pack-destination $packDir | ConvertFrom-Json
$package = Join-Path $packDir $packed.filename
$digest = (Get-FileHash $package -Algorithm SHA256).Hash.ToLowerInvariant()

$labEnv = Get-OpenCodeLabEnvironment -Root $profile
foreach ($name in $script:OpenCodeLabClearedVariables) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
foreach ($name in $labEnv.Keys) { Set-Item "Env:$name" $labEnv[$name] }
$env:npm_config_cache = Join-Path $profile ".npm-cache"
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
Set-Location (Join-Path $LAB "project")

npm exec --yes --package $package -- deniz-skills install --all
if (Test-Path $env:OPENCODE_CONFIG_DIR) { throw "Plan wrote the Destination" }
npm exec --yes --package $package -- deniz-skills install --all --yes
npm exec --yes --package $package -- deniz-skills status
pwsh -NoProfile -File (Join-Path $REPO "experiments\harness-invocation\oc2-discovery.ps1") -Lab $profile
```

The first packed invocation may populate the isolated npm cache; it must not create the Destination.
After Apply, derive the expected skill, command, and agent IDs from the installed Native tree and
assert from the discovery output:

- `skills` holds every installed `skills/<id>/` directory, plus the built-ins `opencode` and
  `report`, and every `manual` item is `advertised: false`;
- the discovery run passed its containment check, so every non-built-in skill resolved below the
  lab;
- `commands` and `agents` hold exactly the installed command and agent IDs plus the built-ins; and
- `opencode debug paths`, run in the same environment after discovery has written `service.json`,
  reports config, data, cache, and state roots below the profile.

Record the OpenCode version, selected Modules, package SHA-256, Native-tree counts, Install-state
path, and each assertion. A package-cache path proves only npm materialization; discovery must resolve
from the installed Native tree.

### Pinned GitHub Release

The remote transport is a Release asset pinned to a tag and target commit and verified by SHA-256,
not a Git package spec. GitHub reports Releases as non-immutable, so the recorded hash detects but
does not prevent an authorized replacement; treat the download-hash comparison as the identity
check. Only after the tag and asset exist with explicit authorization, download them into a new
external-lab directory. Never replace an existing asset except for an explicitly authorized
correction after that exact asset fails the release gate; record both identities and re-run the full
remote verification. Substitute the release record's exact tag, asset name, and repository-recorded digest:

```powershell
$profile = Join-Path $LAB "installer-release"
Remove-Item $profile -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $profile -Force | Out-Null
gh release download <installer-tag> --repo Blind-Striker/agent-skills-and-plugins `
  --pattern "<package-name>.tgz" --dir $profile
$package = Join-Path $profile "<package-name>.tgz"
$releaseDigest = (Get-FileHash $package -Algorithm SHA256).Hash.ToLowerInvariant()

$labEnv = Get-OpenCodeLabEnvironment -Root $profile
foreach ($name in $script:OpenCodeLabClearedVariables) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
foreach ($name in $labEnv.Keys) { Set-Item "Env:$name" $labEnv[$name] }
$env:npm_config_cache = Join-Path $profile ".npm-cache"
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
Set-Location (Join-Path $LAB "project")

npm exec --yes --package $package -- deniz-skills install --all --yes
npm exec --yes --package $package -- deniz-skills status
pwsh -NoProfile -File (Join-Path $REPO "experiments\harness-invocation\oc2-discovery.ps1") -Lab $profile
```

Require `$releaseDigest -eq $digest`, then repeat the local package's discovery and `debug paths`
assertions. The selected Modules, Module digests, Native-tree hashes, and Install
state must match the local packed run. A download or upload was not measured unless that exact
command ran; source review is not Release evidence.

### Human permission observation

Automated introspection does not answer whether a skill's first read of a file bundled beside its
`SKILL.md` interrupts the operator. The source allows `external_directory` reads below the global
config root without a prompt; that is unmeasured. In the isolated project, open the TUI with
`Start-OpenCodeLab`, attach one installed skill that bundles a file with `@<id>`, instruct it to
stop after naming the bundled file it read, and record the literal prompt or “no prompt observed.”
Also record whether approval is one-shot, session-scoped, or persistent. Do not run against the real
profile, and do not write a positive or negative permission claim when no human performed the
observation.

On Windows, additionally record that the package and npm cache remained below the external lab,
`OPENCODE_CONFIG_DIR` and every other relocated root named the lab, every resolved non-built-in
skill was below the lab, and `debug paths` named only relocated roots. Sanitize absolute paths in
the committed record; retain raw logs only in the external lab.

### Codex native marketplace and plugin discovery

Use [`codex-matrix.ps1`](codex-matrix.ps1) for the Codex leg. It creates a new `CODEX_HOME` below the
external lab unless the operator supplies an already-created lab-contained home. Its tracked local
marketplace carries nonsense positive and negative plugins plus `auto`, `manual`, and `both`
invocation fixtures, a cross-skill handoff, and a bundled-reference probe.

The credential-free structural path must exercise marketplace add/list, available-plugin listing,
plugin add/list/remove, and all four generated plugins. Record the Codex version, exact plugin IDs,
installed cache containment, native skill and manual-policy counts, repository status identity, and
byte identity of the real profile's Codex plugin state. These observations prove distribution and
installation structure, not model discovery.

Run `-Behavioural` only when the isolated home already contains `auth.json` or `OPENAI_API_KEY` is
present. The runner sends prompts through stdin to preserve literal `$plugin:skill` spelling and
uses `codex exec --json --ephemeral --ignore-rules --approve-for-me`, with the disposable plugin
cache passed through `--add-dir`. That grants reviewed workspace access only to the external-lab
project and isolated plugin cache, which Codex must read to load full skill bodies and bundled
references. The disposable `CODEX_HOME` supplies configuration isolation. Do not add
`--ignore-user-config`: on Codex CLI 0.153.4 that flag also hides the isolated profile's installed
plugins from skill discovery.
Pin both `-Model` and `-ReasoningEffort`; the defaults are `gpt-5.6-luna` and `low`. Require a
one-token liveness pass before the panel; pair the manual implicit negative with its explicit
positive, and record repeated `auto` and `both` outcomes as propensity rather than a guarantee. With
`-GeneratedPlugins`, keep the fixture installed while all four repository plugins load so the same
nonsense controls measure catalog pressure, then probe one generated manual skill explicitly and
one generated auto skill implicitly. Also require the installed cross-skill handoff, bundled
reference, and uninstalled-plugin negative controls. Kill and record timeouts. Keep credentialed
JSONL only in the external lab.

## Probe cheaply

Prefer a harness's own introspection to asking a model what it can see. It is free, deterministic,
and does not depend on the model reporting honestly.

OpenCode 2 has no skill or command listing in its CLI; `debug` has only `agents`, `config`, and
`paths`. Ask an isolated `serve` instead:

```
GET /api/skill     every registered skill: id, name, description, autoinvoke, path
GET /api/command   every server command: built-ins, file commands, MCP prompts
GET /api/agent     every agent, with its mode
GET /api/info      the serving version
```

Each list route takes `location[directory]=<project>` and answers for that location; Basic auth
uses the user `opencode` and the server's password. [`oc2-discovery.ps1`](oc2-discovery.ps1) does
all of this for one lab and prints the result as JSON:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/oc2-discovery.ps1 -Lab <lab-root>
```

A skill with `autoinvoke: false` stays registered and loadable; it is only not offered to the
model. Skills are not slash commands in OpenCode 2, so a skill never appears in `/api/command`.

Claude Code answers the same questions non-interactively, through the event stream:

```
claude --output-format stream-json --verbose -p "hi"
```

- The **`system`/`init`** event carries `slash_commands`, `skills`, `plugins`, `agents`,
  `mcp_servers` and `apiKeySource`. That is the user surface and the mount state, printed by the
  harness itself — one cheap call answers "what is installed" and "did anything leak" at once.
  Claude Code 2.1.220 also emits an undocumented `memory_paths` object whose `auto` value names the
  auto-memory directory even when it is empty; path presence is not evidence that memory loaded.
  For a behavioural panel, set the documented `CLAUDE_CODE_DISABLE_AUTO_MEMORY=1`, archive any
  existing `projects/*/memory/*.md`, require advertised paths to remain under the isolated config,
  and verify no memory markdown appears during the run. Auto-memory behavior and its disable switch
  are documented at <https://code.claude.com/docs/en/memory>.
- Every **`tool_use`** block is in the stream, so *did the model invoke this skill* is an observed
  event (`Skill` with the target's name) rather than a claim to be trusted.
- The **model** surface is the one thing `init` does not give: `init.skills` lists what is
  slash-addressable, not what the model can call. Enumerate it with a `-p` prompt asking for the
  exact names, and pair it with an unmounted run as the control.
- `claude -p "/ns:name <args>"` **invokes** a skill, arguments included — so a `manual` item's user
  surface is measurable without a TUI. What stays TUI-only is how the `/` menu *reads*.

Two flags are load-bearing rather than convenient. `--plugin-dir` mounts a plugin for one session
with no marketplace install, and `--add-dir` on that same path is what makes its bundled files
readable — without it a body's own template read returns denied and a subagent silently falls back
to the skill body. `--max-budget-usd` caps a runaway ceremony, but a run it truncates exits 1 and
emits no `result` event, so a script must treat missing result text as "capped", not as "empty".

On the OpenCode side, OpenCode 2's `opencode run` keeps `--format json` for the event stream and
`--auto` to approve permissions that are not explicitly denied, but has no `--command` or
`--variant` flag: `-m` takes `provider/model#variant` (`packages/cli/src/commands/commands.ts`,
source). One check is still free:

- **`opencode auth list`** prints which providers hold credentials, free and without a model call —
  the cheapest way to find a route to a model before assuming one needs paying for.

Whatever runs the panel, run its wiring once with nothing attached first. Every measurement error in
the round that produced these notes came from launching a long job whose code path had never been
executed: a table overwritten because PowerShell variable names are case-insensitive (`$leg` and
`$LEG` are one variable, so later legs silently ran on the default model), a smoke test that passed
on a failure because `-match` is case-insensitive too and `Token refresh failed` contains "ok", a
scratch repo that drifted because the ceremony under test ends with "commit your work" and
`checkout`+`clean` does not undo a commit, and a background job diagnosed but never killed, which
kept writing into the next run's results file for forty minutes. A dry-run mode plus a preflight
that refuses to start — names resolve, fixture at its baseline commit, the output path can be
claimed atomically, every variant declared — costs minutes and catches all of them. Do not reject
processes by harness name: unrelated interactive sessions are ordinary on a shared machine.

## What cannot be probed this way

Less than it first appears. Both harnesses expose their user surface to a script — Claude Code
through `-p "/ns:name"`, OpenCode 2 through its server, where a session attaches a skill by ID
(`POST /api/experimental/session/:id/skill`) without a model call. What is left for a
human is how the thing *reads*: whether a `/` menu entry is findable and its description honest,
whether a long command body pasted as the user's message is a wall of text, whether a folder-access
prompt lands as an interruption. Those are judgements, and the operator's opinion is the datum.

The trap on the way there is that the *wrong* invocation looks like the right one. Text that names
a skill reaches the model as prose — `opencode run` builds no skill attachment — yet a model will
often infer the intent from the words and invoke something plausible, which reads exactly like an
explicit invocation. The control that separates them costs nothing: send a name that exists nowhere.
The skill attachment route answers an unknown ID with 404 before any model call; a prose request
for `zzz-nope` is answered cheerfully.

For the genuinely human half, write the probe, hand the operator a numbered table of *what to type*
and *what it decides*, and record what they report verbatim. Do not paraphrase an observation into a
conclusion in the same step.

## Traps

- **The probe inherits your shell's environment.** An OpenCode 1 round concluded that OpenCode does
  not read `.claude/skills/`. It did; the shell had `OPENCODE_DISABLE_CLAUDE_CODE_SKILLS=1` set from
  the machine's profile. OpenCode 2 reads `OPENCODE_CONFIG_PROJECT_DISABLE` before
  `OPENCODE_DISABLE_PROJECT_CONFIG`, and `OPENCODE_CONFIG` or `OPENCODE_CONFIG_CONTENT` add config
  from outside the lab. Print the variables that bear on the result, or clear them explicitly.
- **Quantity claims need counting.** "Upstream bodies are full of Claude tool names" survived into a
  document; the real count across 206 files was four, all of them C#'s `Task`. A rewrite built on
  that claim would have corrupted three skills to fix nothing.
- **Reset the fixture between runs, or you measure the previous probe's residue.** A round concluded
  that a skill never invoked the one its body names, and the finding reached an ADR and the roadmap.
  It was an artifact: an earlier probe had already written the function under test into the working
  tree, so the task was done before the run began and there was nothing left to drive. Resetting
  means `git reset --hard <baseline>` plus `clean -fdx`, not checkout-and-clean — the ceremony under
  test may well end with "commit your work", and it obeys.
- **A behavioural claim needs repeats before it is written down.** The same finding was a sample of
  one, and repeating it across two harnesses and several models reversed it. Invocation is a
  propensity with a tail; one observation of a miss establishes nothing, and this class of claim is
  cheap to disprove and expensive to retract.
- **A provider that refuses does not fail — it hangs.** On OpenCode 1, a connector at its monthly
  limit left `opencode run` alive and idle; the error never reached the event stream, only
  `<data>/log/opencode.log` as a `stream error` line. Two runs sat dead for 39 and 17 minutes, and
  the second one blocked a whole queue. Never invoke a harness unguarded: wrap every call in a
  timeout that kills the process, mark a killed run as *timed out* rather than letting it read as a
  refusal, and open the preflight with a one-token liveness call per leg. Flat CPU plus an
  unchanged fixture is the signature; the log is the confirmation.
- **Check that the prompt isolates the thing you meant to measure.** One probe asked a one-file
  question by invoking a 150-line ceremony, so a model that ignored "skip ahead" ran the whole
  workflow and stalled at a human checkpoint nobody was there to answer. Its replacement asked for
  "the reviewer template" in a body that links four of them, and every leg read the nearest one —
  answering a question that had not been asked. Name the target by its role, forbid the work, and
  read the tool *inputs* rather than the tool names, because the path a model actually opened is
  the only evidence of how it resolved a reference.
- **Where an artifact lands is part of the question.** OpenCode reads `~/.claude/skills/`, which
  sounds like it reaches Claude Code plugins. It does not — plugins install under
  `~/.claude/plugins/cache/…`. Probe the real install path, not the one that sounds right.
- **A server that answers has not necessarily loaded.** OpenCode 2 registers its built-in skills
  before the file scan finishes: one discovery run's first non-empty `/api/skill` answer held only
  the two built-ins while 115 installed skills were still loading. Poll until the list is stable,
  not merely non-empty.
- **A negative needs a positive beside it.** "Not discovered" and "discovered, but I looked in the
  wrong place" are indistinguishable without a control that *is* found.

## Recording

Committed tier-1 structured records and tier-2 sanitized excerpts live only in
[`records/`](records/). Give each record a `record_id`. Research documents synthesize those records
and link the supporting `record_id`s rather than duplicating evidence.

Full raw transcripts and credentialed event logs remain in the external lab, outside both the
repository and the real user profile. Never commit them. If a measurement contradicts an existing
research claim, add a superseding record and correct the research in place.
