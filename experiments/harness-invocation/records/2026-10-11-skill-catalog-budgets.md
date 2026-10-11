---
record_id: skill-catalog-budgets-2026-10-11
date: 2026-10-11
repo_head: c7e9f90fbb9e3b0d19b71f9fe4a4f8834e8194e6
kind: catalog-budget
summary: Over their default skill-listing budgets, Claude Code 2.1.296 showed 39 and 30 of the 82 model-reachable deniz skills by name only in two profiles, and Codex CLI 0.153.4 cut 78 of 84 deniz descriptions to 139-143 characters, losing all 35 exclusion clauses; OpenCode 2.0.26 has no budget. Raising Claude's skillListingBudgetFraction to 0.02 restored all 82 descriptions in four profiles, and Codex's max_context_tokens at its 10,000 cap moved the cut to 546-737 characters, where 1 to 8 clauses are still lost.
isolation_ok: false
harness_name: claude-code, codex, opencode
harness_version: Claude Code 2.1.296 (default-budget captures; after the raise also 2.1.273 and 2.1.286); Codex CLI 0.153.4 (after the raise also 0.159.3 and 0.160.0); OpenCode 2.0.26
runner_revision: sha256:6a46eed44c09879556510830c7114593088ef4396e0f9d7fef5b0b3670972eb3
---

# Skill-listing budgets across the three harnesses

This tier-2 record measures how much of each model-reachable deniz description reaches the model
when a harness lists skills under a budget. The installed estate was Process 0.8.0, General
0.12.0, Akka 0.4.1, and Aspire 0.5.1, whose `plugins/`, `opencode/`, and `codex/` trees are
byte-identical at `repo_head` and at the `installer-v0.5.0` rollout source
([rollout record](2026-10-11-v0.5.0-rollout.md)). No model or LLM call was made.

`isolation_ok: false`: the Claude Code and Codex captures read the curator's real profiles,
because the listing under test is the one those profiles build. They wrote nothing to a profile
except the authorized setting raise described [below](#settings-raised-after-the-measurement).
The OpenCode check ran against an isolated `opencode serve`. The scripts were uncommitted;
`runner_revision` is the SHA-256 of these files concatenated in this order: `capture_proxy.py`,
`cc_listing.py`, `cc_sim.py`, `codex_catalog.py`, `codex_replicate.py`, `curation_items.py`,
`oc_guidance.py`, `oc_perm.py`, `oc_serve_skills.ps1`, `scenarios.py`, `slice.py`, then
`codex_check.py`. Raw captures, debug logs, and absolute paths stay outside the repository.

## Mechanisms and sources

**Claude Code 2.1.296.** Read from the native bundle (constants `var FBo=0.01,Q5t=4,$Bo=200000;var
BBo=1536`; functions `wtt`, `n3t`, and `J5t`; caller `OLr` builds the `skill_listing` attachment)
and the documentation: [skills, "Skill descriptions are cut
short"](https://code.claude.com/docs/en/skills#skill-descriptions-are-cut-short), the settings
reference for
[`skillListingBudgetFraction`](https://code.claude.com/docs/en/settings-reference#skilllistingbudgetfraction)
and
[`skillListingMaxDescChars`](https://code.claude.com/docs/en/settings-reference#skilllistingmaxdescchars),
and the [environment variable](https://code.claude.com/docs/en/env-vars)
`SLASH_COMMAND_TOOL_CHAR_BUDGET`.

- The budget in characters is `SLASH_COMMAND_TOOL_CHAR_BUDGET` when set, else the context window
  (200,000 when unknown) times the model's bytes per token times `skillListingBudgetFraction`,
  default 0.01. Bytes per token is 4 only for a legacy model set (claude-3, Opus 4.0 to 4.6, Sonnet
  4, Haiku 4.5) and 3 for every other model, `claude-opus-5-5` included, so Opus 5.5 with a 1M
  window gets 30,000 characters. The documentation's 8,000-character fallback holds only for a
  4-byte model.
- An entry is `- name: description`, with ` - when_to_use` appended when present, capped at
  `skillListingMaxDescChars` (1,536) with an ellipsis.
- Over budget, bundled prompt skills stay whole and every other entry drops to `- name`. Whole
  descriptions then come back greedily in usage-score order (usage count times a decay that
  halves each week, floored at 0.1, from the profile's skill-usage state), ties in load order:
  plugin order, then alphabetical. A description is never cut partway, so never-used plugins
  loaded last, here Akka and Aspire, lose theirs first.
- The setting is in the settings schema of 2.1.296, 2.1.286, and 2.1.273 as
  `skillListingBudgetFraction: number().gt(0).lte(1).optional()`, and it is read from the merged
  settings, so a user settings file applies.

**Codex CLI 0.153.4.** Read from `openai/codex` at tag `rust-v0.153.4` (commit `3d2ee51`):

- `codex-rs/ext/skills/src/render.rs:17-25` holds the constants: 2% of the context window, 4 bytes
  per token, an 8,000-character default when the window is unknown, a 1,024-character description
  cap (applied at `:1156-1174`), and `MAX_CONFIGURED_SKILL_METADATA_TOKEN_BUDGET = 10_000`.
  `skill_metadata_budget` (`:127-153`) applies a configured `[skills] max_context_tokens` with
  `.min()` against that cap; `codex-rs/config/src/skills_config.rs:40-43` documents the cap. The
  cap and the setting are unchanged at `rust-v0.159.3` (`01fc69f`) and `rust-v0.160.0`
  (`a956835`).
- Each catalog line is `- {name}: {description} ({kind}: {locator})` and costs `ceil(bytes / 4)`
  tokens with its newline; locators are short aliases against a root table. When the whole catalog
  does not fit, `allocate_skill_lines` (`:325-366`) keeps every line and
  `allocate_description_chars` (`:408-447`) hands out description characters one at a time,
  round-robin, so every long description is cut to the same prefix and loses its tail first.
- A plugin's skills render with the full `description`
  (`codex-rs/ext/skills/src/world_state_catalogs.rs:251-262`); a `metadata.short-description` is
  ignored for them. Every bundled model of 0.153.4, 0.159.3,
  and 0.160.0 has a 272,000-token window, and a configured model unknown to the catalog falls back
  to the same window (`codex-rs/models-manager/src/model_info.rs:143,172`), so the default budget
  is 5,440 tokens.
- A `manual` skill (`allow_implicit_invocation: false`) is absent from the catalog.

**OpenCode 2.0.26.** `packages/core/src/skill/instructions.ts:16-33,73-90` at `anomalyco/opencode`
v2.0.26 lists every skill that is neither denied nor `autoinvoke: false` with its full
description, sorted by ID. There is no budget and no truncation.

## Method

1. **Claude Code.** A local HTTP endpoint wrote only the request body's `model`, `system`,
   `messages`, and tool names and descriptions, never a header, and answered every request with a
   non-retryable 400, so no model call happened. Each profile ran
   `claude -p "Reply OK." --model opus --no-session-persistence --debug-file <file>` with
   `ANTHROPIC_BASE_URL` pointed at it. The listing is the message block that starts "The following
   skills are available for use with the Skill tool:". Each entry was compared, as an exact string,
   with the installed `SKILL.md`. A replication of the algorithm (`cc_sim.py`) matched the capture
   130 of 130 and 110 of 110 entries, then ran what-if scenarios.
2. **Codex.** `codex debug prompt-input "Reply OK."` prints the first-turn input of a real exec
   session without a model call; its first developer message is the `<skills_instructions>`
   catalog. A replication of `render.rs` (`codex_replicate.py`, later `codex_check.py`) matched 97
   of 97 lines, and after the raise 97 of 97, 88 of 88, and 89 of 89.
3. **OpenCode.** An isolated `opencode serve` with the estate installed; its skill guidance was
   compared with the installed files.
4. An exclusion clause is a description's `DO NOT USE`, `Do not use`, `Not for`, `Do not`, or
   `Exclude` phrase, in any of the case forms the estate writes; it is lost when it starts past the
   shown prefix.

## Results at the default budgets

Claude Code 2.1.296, model `claude-opus-5-5` with a 1M window, budget 30,000 characters:

| Profile | Listing entries | Full listing chars | deniz model-reachable | Full | Name only |
|---|---:|---:|---:|---:|---:|
| A | 130 | 67,539 | 82 | 43 | 39 |
| B | 110 | 52,287 | 82 | 52 | 30 |

Akka was name-only 5 of 5 in both profiles, and Aspire 6 and 8 of its 8 model-reachable skills. No
deniz description was cut partway. What-ifs on the replication: every deniz description at about
190 characters or fewer (profile A) or 240 (profile B) lists all 82 whole; a fraction of 0.02 lists
all 82 in both; moving 14 to 24 items to `manual` barely helps (45 of 68 whole in profile A).

Codex CLI 0.153.4, budget 5,440 tokens: 125 for the root table and 5,315 for lines, whose full
descriptions would cost 11,213:

| Catalog lines | deniz lines | deniz cut | Cut length | deniz with an exclusion clause | Clause lost |
|---:|---:|---:|---|---:|---:|
| 97 (5 system, 8 other bundled, 84 deniz) | 84 | 78 | 139-143 | 35 | 35 |

The 84 deniz lines are 82 skills plus the two agents Codex emits as skills. The earliest exclusion
clause starts at character 145, the median at 429, and the latest, `aot-trimming`'s, at 755. With
every deniz description at 139 characters or fewer, the replication cuts none; moving 16 items out
of the catalog only raises the cut to 192-195, still losing 27 of 28 clauses.

OpenCode 2.0.26 advertised the 82 deniz skills with descriptions identical to the installed files;
the rendered guidance is about 48,000 characters.

## Settings raised after the measurement

The curator raised both budgets on every machine in use, as a stopgap until the descriptions are
shortened:

- Claude Code: `skillListingBudgetFraction: 0.02` in the user settings of each of four profiles.
- Codex: `[skills] max_context_tokens = 10000` in each of three Codex configurations. That is the
  hard cap: the smallest budget at which no deniz description is cut was 10,467 to 11,338 tokens on
  the three, and the 84 deniz lines alone cost 10,010 plus the root table. Raising the context
  window instead was not done, because it also changes compaction.

Captured again in the same way:

| Harness | Version | Result for deniz |
|---|---|---|
| Claude Code, profile A | 2.1.296 | 67,539 > 60,000 chars, still over budget; all 82 whole, other plugins lose |
| Claude Code, profile B | 2.1.296 | 110 entries, within budget; all 82 whole |
| Claude Code, third profile | 2.1.273 | 95 entries, 41,588 chars; all 82 whole |
| Claude Code, fourth profile | 2.1.286 | 96 entries, 41,885 chars; all 82 whole |
| Codex | 0.153.4 | 97 lines; 23 cut at 546-549; 8 of 35 clauses lost: `aspire-orchestration`, `aspireify`, `aot-trimming`, `check-bin-obj-clash`, `coverage-analysis`, `item-management`, `msbuild-modernization`, `target-authoring` |
| Codex | 0.160.0 | 88 lines; 13 cut at 733-737; 1 of 35 lost: `aot-trimming` |
| Codex | 0.159.3 | 89 lines; 15 cut at 700-704; 1 of 35 lost: `aot-trimming` |

A fixed 10,000 tokens is lower than the 2% default for a model whose window exceeds 500,000
tokens; no bundled model has one at these versions.

## Excerpts

Claude Code debug log, default budget, profile A then profile B:

```text
[DEBUG] Sending 130 skills via attachment (initial)
[WARN] Skill listing over budget: 130 skills, 67539 chars > 30000 budget — descriptions will be truncated. Run /skills to disable some, or raise skillListingBudgetFraction in settings.
[DEBUG] Sending 110 skills via attachment (initial)
[WARN] Skill listing over budget: 110 skills, 52287 chars > 30000 budget — descriptions will be truncated. Run /skills to disable some, or raise skillListingBudgetFraction in settings.
```

Profile A at a fraction of 0.02:

```text
[WARN] Skill listing over budget: 130 skills, 67539 chars > 60000 budget — descriptions will be truncated. Run /skills to disable some, or raise skillListingBudgetFraction in settings.
```

Profile B's listing, default budget: whole entries for Process, name-only entries for Akka and
Aspire.

```text
- deniz-process:receiving-code-review: Use when receiving code review feedback, before implementing suggestions, especially if feedback seems unclear or technically questionable - requires technical rigor and verification, not performative agreement or blind implementation
- deniz-dotnet-akka:akka-hosting-actor-patterns
- deniz-dotnet-akka:akka-net-aspire-configuration
- deniz-dotnet-akka:akka-net-best-practices
- deniz-dotnet-akka:akka-net-management
- deniz-dotnet-akka:akka-net-testing-patterns
- deniz-dotnet-aspire:aspire-deployment
```

Codex catalog lines at the default budget, each description cut to the shared prefix before its
locator:

```text
- deniz-dotnet-general:aot-trimming: Apply .NET trimming and Native AOT semantics to code being written or reviewed where trim/AOT analysis is already in effect: the trimming/AOT (file: r3/aot-trimming/SKILL.md)
- deniz-dotnet-general:test-gap-analysis: Performs report-only pseudo-mutation analysis of existing tests: find caller-visible production-code changes that current assertions would  (file: r3/test-gap-analysis/SKILL.md)
- deniz-process:writing-for-agents: Writing documents an agent consumes. Use when designing or rewording a skill, an AGENTS.md or CLAUDE.md, or an agent prompt for how reliably (file: r4/writing-for-agents/SKILL.md)
```

## Interpretation and limits

1. Two of the three harnesses lose this estate's trigger text at their defaults, in different
   ways: Claude Code drops whole descriptions by usage, and Codex keeps every description but only
   its first ~140 characters. A boundary written at the end of a description survives neither.
2. The raised settings are per-machine and do not travel with the Plugins. Even raised, Codex cuts
   at 546 to 737 characters, so a clause past that point is still lost.
3. These are listing measurements. They show what the model is offered, not which skill it
   selects.
