# Harness probing

Date: 2026-07-31

How to find out what a harness actually does. The findings live in
[skill-invocation-across-harnesses.md](../research/skill-invocation-across-harnesses.md); this is the
method that produces them, and it is here rather than there because only an agent running the
experiment needs it.

## Why this exists

Every emitter in this repo encodes an assumption about a harness. Documentation has been wrong about
those assumptions more than once — `OPENCODE_CONFIG_DIR` is documented as covering agents, commands,
modes and plugins, and covers skills too; `OPENCODE_DATA_DIR` is documented and does nothing in
1.18.7. Inference has been wrong more often than the documentation.

**Measure before you write it down.** A claim in `docs/research/` should name the version it was
measured on. A claim that was reasoned rather than run should say so.

## Build a lab

Any empty directory works; nothing here depends on where it lives. A lab holds three things: fixture
skills to probe with, the isolated harness homes, and a results file.

```
<lab>/
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
| OpenCode | `USERPROFILE` (Windows) | **relocates everything** — home, data, config, cache, state. `HOME` alone does not |

Claude Code keeps credentials inside its config dir, so a fresh one demands a new login. Copy only
`.credentials.json` across — nothing else, or the isolation brings the real plugins with it. Never
track that file.

Verify the isolation before trusting a result: an isolated OpenCode lists exactly one skill, the
built-in `customize-opencode`.

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

Claude Code has `/skills` in-session. There is no measured non-interactive equivalent yet — if you
find one, record it here.

## What cannot be probed this way

Some behaviour only exists at runtime, and needs a human at a TUI:

- **`opencode run` expands neither slash commands nor `@file`.** Both are TUI-level. A command
  invoked through `run` arrives as literal text and the model goes hunting for it on disk.
- **Claude Code's `/` menu** — whether a skill is listed, and whether invoking it works.

For these, write the probe, hand the operator a numbered table of *what to type* and *what it
decides*, and record what they report verbatim. Do not paraphrase an observation into a conclusion
in the same step.

## Traps

- **The probe inherits your shell's environment.** A round concluded that OpenCode does not read
  `.claude/skills/`. It does; the shell had `OPENCODE_DISABLE_CLAUDE_CODE_SKILLS=1` set from the
  machine's profile. Print the variables that bear on the result, or clear them explicitly.
- **Quantity claims need counting.** "Upstream bodies are full of Claude tool names" survived into a
  document; the real count across 206 files was four, all of them C#'s `Task`. A rewrite built on
  that claim would have corrupted three skills to fix nothing.
- **Where an artifact lands is part of the question.** OpenCode reads `~/.claude/skills/`, which
  sounds like it reaches Claude Code plugins. It does not — plugins install under
  `~/.claude/plugins/cache/…`. Probe the real install path, not the one that sounds right.
- **A negative needs a positive beside it.** "Not discovered" and "discovered, but I looked in the
  wrong place" are indistinguishable without a control that *is* found.

## Recording

Raw observation goes to the lab's `RESULTS.md`: one line per probe, what was done and what was seen.
Durable fact graduates to `docs/research/`, with the harness version. If a measurement contradicts
something already written there, correct it in place and say in the commit message what was wrong
and why — a corrected claim is worth more than a claim nobody tested.
