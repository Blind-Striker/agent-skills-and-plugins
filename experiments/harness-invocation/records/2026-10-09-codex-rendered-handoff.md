---
record_id: codex-rendered-handoff-2026-10-09
date: 2026-10-09
repo_head: 5abf3edae758f90cee91e830e21a6f55998eaa65
kind: model-panel
summary: On Codex CLI 0.153.4, $deniz-process:grill-me injected its rendered one-line handoff body and the model then read and followed deniz-process:grilling 6/6 across two independent runs, and the manual grill-me was absent from the advertised catalog; the handoff's causal role is not isolated, because a no-token "Grill me" prompt also loaded the auto grilling skill.
isolation_ok: true
fixture_sha: sha256:58a1bc9d5d781db0119b1fe3ed88b480240a406024f2f81187633857fdc4f45c
harness_name: Codex CLI
harness_version: 0.153.4
runner_revision: sha256:dbfef85a7e3b7d754fa1224aa4ae110fb4b91a02f2186b057b12acade1e48112
---

# Codex rendered skill handoff (W0)

This tier-2 record asks whether a model on Codex follows the handoff that localization renders for
Codex: the generated `deniz-process:grill-me` skill, a `manual` item, has the one-line body
``Invoke `$deniz-process:grilling`.``. The fixture is the checkout's own `codex/deniz-process`
Plugin at version 0.7.0, unchanged. `fixture_sha` is the SHA-256 of
`git ls-tree -r <repo_head> -- codex/deniz-process` (111 entries), so it can be recomputed from
the repository.

Two independent runs are recorded: a first run and a verifier run with a new lab, a new credential
copy, a new `CODEX_HOME`, and its own runner and analyzer. Both used `codex-cli 0.153.4` and
`gpt-5.6-luna` with `model_reasoning_effort="low"`. The CLI reported token usage but no billed
amount, so cost cells are blank. The runner scripts were uncommitted; their hashes (SHA-256 of the
concatenated scripts) are `runner_revision` for the first run (`run.ps1`, `analyze.ps1`) and
`sha256:f95672c63828f800932efd1213af4ff24ca9b0041488cf6cb3428985122ed291` for the verifier
(`run.ps1`, `extra.ps1`, `analyze.js`). Raw JSONL, rollouts, the credential copy, and absolute paths
stayed in the deleted labs.

## Method

The method mirrors [`codex-matrix.ps1`](../codex-matrix.ps1) and the
[2026-09-07 behavioural record](2026-09-07-codex-plugin-behaviour.md).

1. **Lab.** A disposable `CODEX_HOME=<LAB>/codex-home` and an empty project `<LAB>/project`,
   outside the repository and the real profile. The only file taken from the real Codex home was
   the login file, copied as an opaque file: never read, hashed, grepped, or printed. The curator
   authorized this route.
2. **Install.** `codex plugin marketplace list --json` was empty in the fresh home.
   `codex plugin marketplace add <REPO> --json` added the repository marketplace `deniz-skills`
   (`.agents/plugins/marketplace.json`), and `codex plugin add deniz-process@deniz-skills --json`
   installed version 0.7.0 into the isolated plugin cache. No `--ignore-user-config` and no
   machine-wide sandbox bypass were used. A dry run printed every argument vector first.
3. **Calls.** Every model call was
   `codex exec --json --ephemeral --ignore-rules --model gpt-5.6-luna --config model_reasoning_effort="low" --skip-git-repo-check --approve-for-me --add-dir <ISOLATED_PLUGIN_CACHE> --cd <LAB>/project -`
   with the prompt on stdin and a 240 s kill timeout. The verifier dropped `--ephemeral` for the
   liveness call and `explicit-grill-me-3`, so their rollouts persisted inside the isolated home
   for introspection.
4. **Prompts.** The explicit prompt was
   `$deniz-process:grill-me My plan: in a small TypeScript CLI, rename the src/utils folder to src/helpers and update every import in one commit.`
   The control was the same plan plus `What do you think?`, with no `$` token and no "grill" word.
   The verifier added two prompts without a `$` token: grill-me's own description wording
   (`I want a relentless interview to sharpen my plan or design. <plan>`) and
   `Grill me about this. <plan>`.
5. **Grading.** The analyzer read `command_execution` items, agent messages, event types, and
   usage. An explicit attempt passes when a shell read of the installed `grilling/SKILL.md` exits 0
   and the reply uses grilling's round format (numbered ❓ **Qn** questions, each with a ➡️
   recommendation), which comes from the grilling body and not from grill-me.
6. **Cleanup.** `codex plugin remove deniz-process@deniz-skills` exited 0, then the lab was deleted.

## Results: first run

| model | probe_id | status | cost | tools_observed | notes |
|---|---|---|---:|---|---|
| `gpt-5.6-luna@low` | `liveness` | pass |  | none | exact `LIVE` |
| `gpt-5.6-luna@low` | `explicit-grill-me-1` | pass |  | read `grilling/SKILL.md` (exit 0); 2 failed workspace scans | "I'm using the grilling skill …"; Q1–Q6 rounds; grill-me `SKILL.md` not shell-read |
| `gpt-5.6-luna@low` | `explicit-grill-me-2` | pass |  | read `grilling/SKILL.md` (exit 0); 3 failed workspace scans | grilling announced; Q1–Q4 rounds |
| `gpt-5.6-luna@low` | `explicit-grill-me-3` | pass |  | read `grilling/SKILL.md` (exit 0); 1 failed, 1 passing workspace scan | grilling announced; Q1–Q3 rounds |
| `gpt-5.6-luna@low` | `control-no-grill-1` | pass |  | read `brainstorming/SKILL.md` (exit 0); workspace scans | grilling not loaded; implicitly chose the auto `deniz-process:brainstorming` and gave a plain review |

## Results: verifier run

| model | probe_id | status | cost | tools_observed | notes |
|---|---|---|---:|---|---|
| `gpt-5.6-luna@low` | `liveness` | pass |  | none | exact `LIVE` |
| `gpt-5.6-luna@low` | `explicit-grill-me-1` | pass |  | one command read `grill-me/SKILL.md` and `grilling/SKILL.md` (exit 0); 1 failed scan | grilling announced; Q1 rounds |
| `gpt-5.6-luna@low` | `explicit-grill-me-2` | pass |  | read `grilling/SKILL.md` (exit 0); 2 failed scans | grilling announced; Q1 rounds |
| `gpt-5.6-luna@low` | `explicit-grill-me-3` | pass |  | read `grilling/SKILL.md` (exit 0); scans | grilling announced; Q1 rounds; rollout persisted |
| `gpt-5.6-luna@low` | `control-no-grill-1` | pass |  | none | no skill and no command; a plain 134-token answer |
| `gpt-5.6-luna@low` | `manual-implicit-negative-1` | pass |  | read `grilling/SKILL.md` (exit 0 after one wrong-path try) | grill-me's description wording did not select the manual `grill-me`; the auto `grilling` loaded |
| `gpt-5.6-luna@low` | `grill-word-no-token-1` | fail |  | read `grilling/SKILL.md` (exit 0) | handoff-isolation control: passes only if grilling does not load without the handoff. It loaded, with Q1 rounds |

Across both runs, the explicit `$deniz-process:grill-me` prompt led to a successful read of the
grilling body and a reply in grilling's format 6 of 6 times. The no-grill controls loaded grilling
0 of 2 times. The model's own workspace scans failed in several attempts (PowerShell quoting errors
and the empty project); these are model-side command errors that did not affect the observation.

## Interpretation

1. **The handoff is rendered, injected, and followed.** Codex injects an explicitly `$`-mentioned
   skill as a user-role `<skill>` message. The persisted rollout of verifier `explicit-grill-me-3`
   contains grill-me's rendered body ``Invoke `$deniz-process:grilling`.`` in that message (excerpt
   below). It does not appear in the `--json` stdout events, so the first run's observation that
   the grill-me body never appears as an event is true of stdout only; in verifier attempt 1 the
   model also shell-read `grill-me/SKILL.md` itself. The model then read the grilling body and
   followed it 6 of 6.
2. **The handoff alone is not shown to cause the load.** The first run concluded that its control
   proved the grilling load came from the handoff rather than from implicit selection. The verifier
   refuted that conclusion: `grilling` is an auto skill whose description matches "any 'grill'
   trigger phrases", the explicit prompt's own token contains "grill", and both
   `Grill me about this.` and grill-me's description wording loaded grilling without any `$` token.
   These probes cannot separate the handoff body from implicit selection. Isolating it needs a
   handoff fixture whose source and target names share no trigger words, as in the protocol's
   nonsense-trigger fixture; that measurement is an open gap.
3. **Manual is not advertised.** The verifier's persisted rollouts list the model's skill catalog:
   the five Codex system skills and exactly the 20 `deniz-process` skills without
   `allow_implicit_invocation: false`. All 17 manual skills, `grill-me` among them, are absent, and
   `grilling` is present. grill-me's description wording did not select it implicitly (1 of 1).
4. **Implicit selection varies.** The two plain-review controls behaved differently: one
   implicitly loaded `deniz-process:brainstorming`, the other loaded nothing. Both loaded no
   grilling.

These are bounded observations on one model and effort, not a general rate.

### Account-level remote plugins

With the copied login present, `codex plugin list` in the isolated home showed 8
`openai-curated-remote` plugins as installed and enabled, before and after the marketplace add:
`github`, `google-drive`, an account-provisioned `app-<id>`, `openai-templates`, `pages`, `sites`,
`plugin-management`, and `work-pets`. Their files were cached in the isolated home. The
[2026-09-07 behavioural record](2026-09-07-codex-plugin-behaviour.md) noted OpenAI's remote catalog
only under `--available`. The first run inferred from the listing that these plugins widened the
model's catalog; the verifier's rollouts show they did not: the `<skills_instructions>` catalog
held no `openai-curated-remote` skill, and a separate `<recommended_plugins>` user message named 15
remote plugins that are not installed. None of their skills was loaded in any attempt.

### Other harness events

Each non-ephemeral call that ran commands wrote a second rollout with source
`{"subagent": {"other": "guardian"}}`: the automatic approvals reviewer that `--approve-for-me`
enables. The session sandbox mode was read-only. Input per explicit attempt was about 42,000 to
74,000 tokens, mostly cached.

## Sanitized excerpts

Paths are reduced to `<LAB>`, `<ISOLATED_PLUGIN_CACHE>`, and `<REPO>`; the shell is shown as
`pwsh`.

### Verifier `explicit-grill-me-3`: rollout user-role messages

```text
[recommended_plugins + environment_context block; plugin names only, 15 not-installed remote plugins]
$deniz-process:grill-me My plan: in a small TypeScript CLI, rename the src/utils folder to src/helpers and update every import in one commit.
<skill>
<name>deniz-process:grill-me</name>
<path><ISOLATED_PLUGIN_CACHE>\deniz-skills\deniz-process\0.7.0\skills\grill-me\SKILL.md</path>
---
name: grill-me
description: A relentless interview to sharpen a plan or design.
---

Invoke `$deniz-process:grilling`.

</skill>
```

### Verifier liveness rollout: advertised catalog (names only)

```text
<skills_instructions> … ### Available skills
imagegen, openai-docs, plugin-creator, skill-creator, skill-installer,
deniz-process:asd-ste100, deniz-process:brainstorming, deniz-process:codebase-design,
deniz-process:dispatching-parallel-agents, deniz-process:domain-modeling,
deniz-process:executing-plans, deniz-process:finishing-a-development-branch,
deniz-process:grilling, deniz-process:prototype, deniz-process:receiving-code-review,
deniz-process:requesting-code-review, deniz-process:research,
deniz-process:resolving-merge-conflicts, deniz-process:subagent-driven-development,
deniz-process:systematic-debugging, deniz-process:test-driven-development,
deniz-process:using-git-worktrees, deniz-process:verification-before-completion,
deniz-process:writing-for-agents, deniz-process:writing-plans
(no deniz-process:grill-me; no openai-curated-remote skill)
```

### First run `explicit-grill-me-1`

```text
command: pwsh -Command "Get-Content -Raw '<ISOLATED_PLUGIN_CACHE>\deniz-skills\deniz-process\0.7.0\skills\grilling\SKILL.md'" -> exit 0
  output head: ---\nname: grilling\ndescription: Grill the user relentlessly about a plan, decision, or idea. …
command: pwsh -Command "rg --files … rg -n \"src/utils|…\" …" -> exit 1 (ParserError from the model's quoting)
command: pwsh -Command "rg --files … rg -n 'utils|helpers|…' ." -> exit 1 (empty project)
agent: I'm using the grilling skill to stress-test the rename plan before any implementation. …
agent: ❓ **Q1 — Scope**: Is this strictly a filesystem/import rename, …
       ➡️ **Recommended:** Update every repository reference, …
```

### First run `control-no-grill-1`

```text
user: My plan: in a small TypeScript CLI, rename the src/utils folder to src/helpers and update every import in one commit. What do you think?
command: pwsh -Command "Get-Content -Raw '<ISOLATED_PLUGIN_CACHE>\deniz-skills\deniz-process\0.7.0\skills\brainstorming\SKILL.md'" -> exit 0
agent: I'm using the brainstorming skill because you're asking for a plan review before implementation. …
agent: This is a sound, tightly scoped plan. A single commit is appropriate …
```

### Verifier `grill-word-no-token-1`: the confound

```text
user: Grill me about this. My plan: in a small TypeScript CLI, rename the src/utils folder to src/helpers and update every import in one commit.
command: pwsh -Command "Get-Content -Raw '<ISOLATED_PLUGIN_CACHE>/deniz-skills/deniz-process/0.7.0/skills/grilling/SKILL.md'" -> exit 0
agent: I'm using the grilling skill to stress-test the rename plan, especially its hidden assumptions, blast radius, and rollback story.
agent: ❓ **Q1 — Scope**: Is this purely a structural rename, …
       ➡️ Keep it structural only; …
```

## Isolation

- Each runner's `finally` block compared SHA-256 snapshots of 2,101 real Codex files
  (`config.toml`, `plugins/installed_plugins.json`, `plugins/marketplaces.json`, and the plugin
  cache) and reported them unchanged, with the repository status unchanged. The verifier also
  confirmed that no real session file was added (643 before and after) and that the real login file
  was present before and after (existence only).
- Before deletion each lab held exactly one copied login file, and no process command line
  referenced the lab. Both labs and both dry-run labs were deleted and proven absent; no login file
  and no JSONL or rollout file remained in the session scratch area.
- The first run's sanitizer initially replaced every word matching the account name, which also
  mangled the plugin names in the evidence. It was narrowed to profile-path segments and the
  evidence regenerated. The final evidence holds no host path, account name, or token.
- The repository stayed clean at `repo_head`; nothing was committed or pushed.
