# Harness probing

Date: 2026-07-31

How to find out what a harness actually does. The findings live in
[skill-invocation-across-harnesses.md](../../docs/research/skill-invocation-across-harnesses.md); this is the
method that produces them.

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
  .opencode-home/    OPENCODE_CONFIG_DIR    (gitignore)
  isolated-home/     USERPROFILE            (gitignore)
  RESULTS.md
```

Give every probe a **nonsense trigger word** (`ZEBRA-FLAGGED`) and a body that instructs a literal
reply (`H1-RAN`). That is what separates "the model chose to fire this skill" from "the model is
being agreeable", and it lets a probe survive a noisy listing.

## Isolate

The two harnesses isolate differently. Getting this wrong wastes a round.

| Harness | Variable | Behaviour |
|---|---|---|
| Claude Code | `CLAUDE_CONFIG_DIR` | **replaces** the config root. Setting it is enough |
| OpenCode | `OPENCODE_CONFIG_DIR` | only **adds** a search location — the global config and package cache still load |
| OpenCode | `USERPROFILE` (Windows) | relocates what `opencode debug paths` *reports* — **not** what discovery *reads*. The global config mount follows `XDG_CONFIG_HOME`, falling back to the real profile; set both |

Three OpenCode-specific traps, each of which cost a round:

- **Put the lab outside the repository and real profile.** Discovery walks up from the working
  directory past any git boundary, so a lab under either location can silently collect real
  configuration trees on the way up.
- **The package cache cannot be mounted over.** Packages from a real config's `plugin:` list
  outrank `OPENCODE_CONFIG_DIR` and the global config dir; same-named probes resolve to the
  package. Isolate by keeping every `plugin:` key out of the lab home. Not by withholding the
  config file: OpenCode writes its own `opencode.jsonc` (schema line only) plus a `.gitignore`
  into an empty config dir on first run. That file declares no packages, so the isolation holds —
  but "give it no config" cannot be held literally, and a lab that checks for the file's absence
  will report a leak that is not there.
- **`opencode debug paths` is not an isolation check.** It reports relocated roots that discovery
  does not use. Trust the listing (`debug skill`), never `paths`.

Claude Code keeps credentials inside its config dir, so a fresh one demands a new login. A lab may
be seeded once with `.credentials.json` on Windows or Linux, as the authentication docs describe,
but do not clone that rotating OAuth file per attempt: concurrent copies are reported to invalidate
one another (<https://github.com/anthropics/claude-code/issues/76561>). For unattended automation,
the documented mechanism is `claude setup-token` plus `CLAUDE_CODE_OAUTH_TOKEN`
(<https://code.claude.com/docs/en/authentication>). For an OpenCode TUI session the equivalent is
`~/.local/share/opencode/auth.json`, copied into the isolated data dir. Never track either file.

Verify the isolation before trusting a result, with the positive control in the same breath: an
isolated OpenCode lists exactly one skill, the built-in `customize-opencode`; an isolated Claude
Code with nothing mounted lists only the harness's own bundled skills, and its `init` event reports
the mounted plugins and `mcp_servers: 0`. A behavioural panel also disables and checks auto memory as
described below.

## Probe cheaply

Prefer a harness's own introspection to asking a model what it can see. It is free, deterministic,
and does not depend on the model reporting honestly.

```
opencode debug skill     every resolved skill as JSON, with its `location`
opencode debug config    resolved config, including discovered commands
opencode debug paths     resolved home / data / config / cache / state roots
```

```powershell
(opencode debug skill | Out-String | ConvertFrom-Json) |
  ForEach-Object { "{0,-24} {1}" -f $_.name, $_.location }
```

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

On the OpenCode side the equivalents are `--format json` for the event stream, `--auto` to pass
permission gates, `-m` to pin the model and `--variant` for reasoning effort. Two of those bite:

- **`--variant` is not validated.** An effort a model does not offer is dropped in silence and the
  run proceeds at the default, so a clean exit proves nothing. What each model offers is in
  `opencode models <provider> --verbose` under `variants`; check the panel against that list before
  running it, not after.
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
through `-p "/ns:name"`, OpenCode through `opencode run --command <name> [args]` with `--format
json`, `--auto` to pass a permission gate and `-m`/`--variant` to pin the model. What is left for a
human is how the thing *reads*: whether a `/` menu entry is findable and its description honest,
whether a long command body pasted as the user's message is a wall of text, whether a folder-access
prompt lands as an interruption. Those are judgements, and the operator's opinion is the datum.

The trap on the way there is that the *wrong* invocation looks like the right one. A slash inside
`opencode run`'s message text is never expanded — it reaches the model as prose — yet a model will
often infer the intent from the words and invoke something plausible, which reads exactly like a
command that ran. The control that separates them costs nothing: send a name that exists nowhere.
`--command zzz-nope` fails before any model call; `/zzz-nope` is answered cheerfully.

For the genuinely human half, write the probe, hand the operator a numbered table of *what to type*
and *what it decides*, and record what they report verbatim. Do not paraphrase an observation into a
conclusion in the same step.

## Traps

- **The probe inherits your shell's environment.** A round concluded that OpenCode does not read
  `.claude/skills/`. It does; the shell had `OPENCODE_DISABLE_CLAUDE_CODE_SKILLS=1` set from the
  machine's profile. Print the variables that bear on the result, or clear them explicitly.
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
- **A provider that refuses does not fail — it hangs.** A connector at its monthly limit left
  `opencode run` alive and idle; the error never reached the event stream, only
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
- **A negative needs a positive beside it.** "Not discovered" and "discovered, but I looked in the
  wrong place" are indistinguishable without a control that *is* found.

## Recording

Committed tier-1 structured records and tier-2 sanitized excerpts live only in
[`records/`](records/). Give each record a `record_id`. Research documents synthesize those records
and link the supporting `record_id`s rather than duplicating evidence.

Full raw transcripts and credentialed event logs remain in the external lab, outside both the
repository and the real user profile. Never commit them. If a measurement contradicts an existing
research claim, add a superseding record and correct the research in place.
