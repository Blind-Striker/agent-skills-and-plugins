---
record_id: opencode2-manual-skill-2026-10-09
date: 2026-10-09
repo_head: 5abf3edae758f90cee91e830e21a6f55998eaa65
kind: model-panel
summary: On OpenCode 2.0.23 a manual skill (opencode/autoinvoke false) was absent from the model's skill guidance in every session, loaded by @-attachment 6/6 and by an explicit load-by-ID instruction 6/6 across two independent runs, while looser mentions of its ID loaded it in 5 of 7 attempts; the rendered W0 skill-tool handoff in deniz-process.grill-me was followed 6/6.
isolation_ok: true
fixture_sha: sha256:a4c8ccf7430d60c0cc51b8af5444e195831abda7cef3fb200c1629e54fdb7cf2
harness_name: opencode
harness_version: 2.0.23
runner_revision: sha256:f3146acc7dda57e10301878c98cdffbe2ea618e04510ccd8ed3ed2fb46aef8f9
---

# OpenCode 2 manual skill posture and the rendered skill-tool handoff

This tier-2 record covers plan Task 16 and the W0 OpenCode phrasing probe. It asks whether
OpenCode 2's native `manual` mapping (`metadata: {"opencode/autoinvoke": false}`) gives the meaning
[ADR-0005](../../../docs/adr/0005-invocation-intent-in-the-manifest.md) decided: the skill is not
offered to the model, the user can attach it, and the skill tool still loads it by its exact ID. It
also asks whether a model follows the rendered OpenCode skill-tool sentence that localization writes
into `deniz-process.grill-me`.

Two independent runs are recorded: a first run and a verifier run. The verifier built a new lab, a
new credential snapshot, new fixture markers and triggers, new prompt wording, and its own runner and
grader. Both runs used the Windows workstation, `opencode v2.0.23` (CLI and `/api/info`), and
`openai/gpt-5.6-luna` with variant `low`. The provider reported cost 0 for every call because the
login is a subscription, so cost cells are blank. The runner scripts were uncommitted; their hashes
are `runner_revision` (first run) and
`sha256:37d080100e0dfed9b2ac76842309813e778cfae84c7529058487243a62f746de` (verifier). The verifier
fixture hash is `sha256:dd15ba12da2fae79bba3de72351a61f4c9d251724803bdd498f03e3e18d0dabd`. Raw
transcripts, credentialed logs, and absolute paths stayed in the deleted labs; `<LAB>` below is the
disposable lab root.

## Method

1. **Lab.** `Get-OpenCodeLabEnvironment` and `Initialize-OpenCodeLab` from
   [`common.ps1`](../common.ps1) built a lab outside the repository and the user profile. `HOME`,
   `USERPROFILE`, `OPENCODE_TEST_HOME`, the four XDG roots, `OPENCODE_CONFIG_DIR`, `OPENCODE_DB`,
   and the temporary roots pointed inside it; `OPENCODE_DISABLE_PROJECT_CONFIG=1`;
   `service.json` was `{"disabled":true}`; the `OPENCODE_CONFIG*` and password variables were
   cleared. The verifier also removed provider `*_API_KEY` variables from the server environment.
2. **Credential.** `opencode debug paths`, run read-only in the real environment, located the real
   database. A consistent SQLite online backup of it (Node 24 `node:sqlite` `backup()`, source opened
   read-only, one step) became the lab's `OPENCODE_DB`. The copy was an opaque file: never queried,
   read, or printed. The curator authorized this route and accepted that a token refresh in a copy
   could log out the real profile.
3. **Estate.** `npm run install:opencode -- install --all --yes` with the full lab environment
   installed the checkout's four Bundles (115 skills) into `<LAB>/config`.
4. **Fixture.** Two skills were written to `<LAB>/config/skills`, plus a config file that keeps
   only the skill tool callable:

   ```yaml
   # skills/deniz-fixture.zorblat/SKILL.md (manual)
   name: deniz-fixture.zorblat
   description: Use when the user writes ZORBLAT-QUENCH. Replies with a fixed marker.
   metadata:
     opencode/autoinvoke: false
   # body: When this skill is loaded, reply with exactly the marker MANUAL-ZRB-7Q4 and nothing else.

   # skills/deniz-fixture.vexmoor/SKILL.md (auto control)
   name: deniz-fixture.vexmoor
   description: Use whenever the user writes VEXMOOR-FLAGGED. Replies with a fixed marker.
   # body: When this skill is loaded, reply with exactly the marker AUTO-VXM-3K9 and nothing else.
   ```

   ```json
   { "permissions": [
       { "action": "*", "resource": "*", "effect": "deny" },
       { "action": "skill", "resource": "*", "effect": "allow" } ] }
   ```

   The verifier used the same IDs and postures with the triggers `GLIMMOCK-PRESSED` (manual) and
   `TRUNDLEWICK-RAISED` (auto) and the markers `ZRB-MAN-T5W8` and `VXM-AUT-H2J6`.
5. **Server.** A private `opencode serve --hostname 127.0.0.1 --port <free>` with a random Basic
   password ran with the lab environment and was killed with its process tree in `finally`.
   `/api/skill` stabilized at 119 skills (115 Bundle, 2 fixture, 2 built-ins), 27 with
   `autoinvoke: false` (the 26 Bundle `manual` skills plus `deniz-fixture.zorblat`), 92
   advertised, and no skill path outside the lab. Both runs saw the same estate.
6. **Attempts.** Each attempt used a fresh session: `POST /api/session` (model and location),
   `POST /api/session/{id}/prompt` (text, plus `skills: [{id, mention}]` for an `@` attachment),
   then `POST /api/experimental/session/{id}/wait` with a 240 s guard. The panel ran 3 rounds of
   the 6 probes in interleaved order. The global `/api/event` stream was captured for the whole
   panel, and each session's context, messages, and log were saved in the lab.
7. **Deterministic skill-list check.** No route returns the rendered system prompt. Each session's
   `session.instructions.updated` event carries `delta["core/skill-guidance"]`, the SHA-256 of the
   canonical JSON of the `[{id, name, description}]` list the model is offered. Both runs recomputed
   it from `/api/skill` with the 2.0.23 algorithm (drop skills with `autoinvoke === false` or no
   description, sort by ID with `localeCompare`); the verifier checked that algorithm against the
   2.0.23 source (`skill/instructions.ts` and the `canonical()` helper in `instructions/index.ts`).
8. **Grading.** Each attempt was graded from the session context's tool items (name, `input.id`,
   status, output) and the answer text. The verifier cross-checked every attempt against the event
   stream: each skill call has its `session.tool.called` event with `input.id`, and attempts with no
   tool call have no tool events.

Pass rules:

| probe_id | Prompt (first run / verifier) | Passes when |
|---|---|---|
| `p1-list` | "List the ID of every skill in your available skills list … Do not call any tools." / "Without calling any tools, print the id of every entry in your available skills list …" | the session's guidance hash equals the recomputed advertised hash, no tool is called, the auto control is listed, and the manual skill is not |
| `p1b-implicit-manual` | `ZORBLAT-QUENCH: please handle this.` / `GLIMMOCK-PRESSED` | the manual skill is not loaded and its marker is absent |
| `p2-attach` | `@deniz-fixture.zorblat Follow the attached skill.` / `@deniz-fixture.zorblat Do what the attached skill says.`, each with a `skills` attachment | the attached body carries the marker and the answer contains it |
| `p3-named` | `Use the skill tool to load the skill with ID deniz-fixture.zorblat, then follow its instructions.` / `Load the skill deniz-fixture.zorblat and do what it says.` | a skill call has the exact `input.id`, completes, returns the body, and the answer has the marker |
| `p4-implicit-auto` | `VEXMOOR-FLAGGED: please handle this.` / `TRUNDLEWICK-RAISED` | an exact `deniz-fixture.vexmoor` skill call completes and the answer has its marker |
| `p5-w0` | `@deniz-process.grill-me Proceed. My plan: …` with a `skills` attachment of `deniz-process.grill-me` | the attachment holds the rendered sentence and a skill call with the exact ID `deniz-process.grilling` completes and returns the grilling body |

## Results: first run

The liveness prompt returned exactly `LIVE-OK`. One wiring attempt of `p2-attach` before the panel
passed and is outside the table. Every session ended idle with outcome `succeeded`; none timed out.

| model | probe_id | status | cost | tools_observed | notes |
|---|---|---|---:|---|---|
| `openai/gpt-5.6-luna#low` | `p1-list-1` | pass |  | none | listed 91 of 92; missed `deniz-process.requesting-code-review`; 0 extra; zorblat absent; vexmoor present; hash matches |
| `openai/gpt-5.6-luna#low` | `p1b-implicit-manual-1` | pass |  | none | asked for task details; marker absent |
| `openai/gpt-5.6-luna#low` | `p2-attach-1` | pass |  | none (body in the user message) | answer exactly `MANUAL-ZRB-7Q4` |
| `openai/gpt-5.6-luna#low` | `p3-named-1` | pass |  | `skill {id: "deniz-fixture.zorblat"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `p4-implicit-auto-1` | pass |  | `skill {id: "deniz-fixture.vexmoor"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `p5-w0-1` | pass |  | `skill {id: "deniz-process.grilling"}` completed | grilling body returned; Q1/Q2 rounds began |
| `openai/gpt-5.6-luna#low` | `p1-list-2` | pass |  | none | listed 92 of 92; 0 extra; zorblat absent; vexmoor present; hash matches |
| `openai/gpt-5.6-luna#low` | `p1b-implicit-manual-2` | pass |  | none | marker absent |
| `openai/gpt-5.6-luna#low` | `p2-attach-2` | pass |  | none (body in the user message) | marker returned |
| `openai/gpt-5.6-luna#low` | `p3-named-2` | pass |  | `skill {id: "deniz-fixture.zorblat"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `p4-implicit-auto-2` | pass |  | `skill {id: "deniz-fixture.vexmoor"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `p5-w0-2` | pass |  | `skill {id: "deniz-process.grilling"}` completed | grilling body returned |
| `openai/gpt-5.6-luna#low` | `p1-list-3` | pass |  | none | listed 91 of 92; missed `deniz-process.requesting-code-review`; 0 extra; zorblat absent; hash matches |
| `openai/gpt-5.6-luna#low` | `p1b-implicit-manual-3` | pass |  | none | marker absent |
| `openai/gpt-5.6-luna#low` | `p2-attach-3` | pass |  | none (body in the user message) | marker returned |
| `openai/gpt-5.6-luna#low` | `p3-named-3` | pass |  | `skill {id: "deniz-fixture.zorblat"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `p4-implicit-auto-3` | pass |  | `skill {id: "deniz-fixture.vexmoor"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `p5-w0-3` | pass |  | `skill {id: "deniz-process.grilling"}` completed | grilling body returned |

Recomputed advertised hash: `eff8cc7b43098156a71b43cd917455ba797fdc823859a00715325df2c0339f55`
(92 skills, zorblat absent, vexmoor present). It equals the guidance delta in all 18 sessions. The
same list with zorblat advertised would hash to
`0a32eb7f6f1681b2b05b1821ebd8367b287faad82b5c6cbec9b0371fe0565995`.

## Results: verifier run

The liveness prompt returned exactly `LIVE-OK`.

| model | probe_id | status | cost | tools_observed | notes |
|---|---|---|---:|---|---|
| `openai/gpt-5.6-luna#low` | `p1-list-1` | pass |  | none | listed 91; missed `deniz-process.requesting-code-review`; 0 extra; zorblat absent; hash matches |
| `openai/gpt-5.6-luna#low` | `p1b-implicit-manual-1` | pass |  | none | "I don't recognize `GLIMMOCK-PRESSED` …" |
| `openai/gpt-5.6-luna#low` | `p2-attach-1` | pass |  | none (body in the user message) | answer exactly `ZRB-MAN-T5W8` |
| `openai/gpt-5.6-luna#low` | `p3-named-1` | pass |  | `skill {id: "deniz-fixture.zorblat"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `p4-implicit-auto-1` | pass |  | `skill {id: "deniz-fixture.vexmoor"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `p5-w0-1` | pass |  | `skill {id: "deniz-process.grilling"}` completed | grilling Q1/➡️ rounds |
| `openai/gpt-5.6-luna#low` | `p1-list-2` | pass |  | none | listed 91; missed `deniz-process.requesting-code-review`; hash matches |
| `openai/gpt-5.6-luna#low` | `p1b-implicit-manual-2` | pass |  | none | marker absent |
| `openai/gpt-5.6-luna#low` | `p2-attach-2` | pass |  | none (body in the user message) | marker returned |
| `openai/gpt-5.6-luna#low` | `p3-named-2` | pass |  | `skill {id: "deniz-fixture.zorblat"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `p4-implicit-auto-2` | pass |  | `skill {id: "deniz-fixture.vexmoor"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `p5-w0-2` | pass |  | `skill {id: "deniz-process.grilling"}` completed | grilling rounds |
| `openai/gpt-5.6-luna#low` | `p1-list-3` | pass |  | none | listed 91; missed `deniz-process.requesting-code-review`; hash matches |
| `openai/gpt-5.6-luna#low` | `p1b-implicit-manual-3` | pass |  | none | "How can I help?" |
| `openai/gpt-5.6-luna#low` | `p2-attach-3` | pass |  | none (body in the user message) | marker returned |
| `openai/gpt-5.6-luna#low` | `p3-named-3` | pass |  | `skill {id: "deniz-fixture.zorblat"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `p4-implicit-auto-3` | pass |  | `skill {id: "deniz-fixture.vexmoor"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `p5-w0-3` | pass |  | `skill {id: "deniz-process.grilling"}` completed | grilling rounds |

Recomputed advertised hash: `1558f078984954e59eaabf8781f6b2e4110ec7cf87daae02eed18b2bb7156080`;
with zorblat advertised it would be
`27f745e4fca012ce27c838d578a0cece0f5ca5242f10012096a36eb09ba4dbc5`. The observed delta equaled
the recomputed hash in every verifier session, 31 model attempts across the panel, the controls,
and the late-update session.

### Verifier controls

Controls have their own expectations and are outside the panel denominators above.

| model | probe_id | status | cost | tools_observed | notes |
|---|---|---|---:|---|---|
| `openai/gpt-5.6-luna#low` | `c-named-unknown-1` | pass |  | `skill {id: "deniz-fixture.nopezz"}` error | `Load the skill deniz-fixture.nopezz and do what it says.` The tool failed with `Unable to load skill deniz-fixture.nopezz`, so p3's success is a real lookup |
| `openai/gpt-5.6-luna#low` | `c-real-manual-implicit-1` | pass |  | `skill {id: "deniz-dotnet-general.package-management"}` completed | asked to load the best-matching skill for a CPM conversion; the real Bundle `manual` skill `deniz-dotnet-general.convert-to-cpm` was not chosen |
| `openai/gpt-5.6-luna#low` | `c-w0-no-attach-1` | pass |  | none | the `p5-w0` prompt without the grill-me attachment made no grilling call, so p5's call follows the rendered sentence |
| `openai/gpt-5.6-luna#low` | `late-update-1` | pass |  | `skill {id: "deniz-fixture.lateauto"}` completed on its trigger only | see the rendered-text excerpt below; `deniz-fixture.latemanual` was absent from the rendered update and its trigger did not load it |

### Loose mentions of the manual ID (propensity)

The plan's probe 3 says "when the prompt names `deniz-fixture.zorblat`". The verifier tested
looser naming without an attachment. A row passes when the model loaded the exact ID.

| model | probe_id | status | cost | tools_observed | notes |
|---|---|---|---:|---|---|
| `openai/gpt-5.6-luna#low` | `c-prose-at-no-attach-1` | fail |  | none | "I can't find an available skill named `deniz-fixture.zorblat`, and no skill content was attached." |
| `openai/gpt-5.6-luna#low` | `x-prose-at-no-attach-1` | pass |  | `skill {id: "deniz-fixture.zorblat"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `x-prose-at-no-attach-2` | pass |  | `skill {id: "deniz-fixture.zorblat"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `x-prose-at-no-attach-3` | pass |  | `skill {id: "deniz-fixture.zorblat"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `x-named-soft-1` | pass |  | `skill {id: "deniz-fixture.zorblat"}` completed | marker returned |
| `openai/gpt-5.6-luna#low` | `x-named-soft-2` | fail |  | none | "The `deniz-fixture.zorblat` skill is not available in this environment." |
| `openai/gpt-5.6-luna#low` | `x-named-soft-3` | pass |  | `skill {id: "deniz-fixture.zorblat"}` completed | marker returned |

`*-prose-at-no-attach` sent `@deniz-fixture.zorblat Do what the attached skill says.` as text with
no `skills` attachment (3 of 4 loaded). `x-named-soft` sent `Follow deniz-fixture.zorblat.` (2 of
3 loaded). The `x-*` rows ran in a second verifier session with its own liveness check, the same
estate, and the same guidance hash.

### Harness-side controls (no model call)

| Request | Result |
|---|---|
| `POST /api/experimental/session/{id}/skill` with `deniz-fixture.zorblat` | 204; the session gained a `skill` message with the body and a `session.skill.activated` event |
| the same route with the unknown `deniz-fixture.nopezz` | 404 `SkillNotFoundError` |
| `POST /api/session/{id}/prompt` with a `skills` attachment of `deniz-fixture.nopezz` | 400 `InvalidRequestError`, field `skills`; no model call |

## Interpretation

1. **Not offered to the model.** In all 49 checked sessions (18 in the first run, 31 in the
   verifier run), the guidance the model was offered hashed to exactly the advertised list, which omits `deniz-fixture.zorblat`. This is
   harness-side evidence, not the model's own report. The verifier's late-update session also shows
   the rendered text: a hot-added auto skill appeared in the "New skills are available" update and
   a hot-added manual skill did not. The model never listed the manual skill (0 of 6), always
   listed the auto control (6 of 6), and the manual trigger never loaded it (0 of 6), while the auto
   trigger loaded the control 6 of 6. `/api/skill` still lists the manual skill with
   `autoinvoke: false`, so it stays registered.
2. **User attachment.** An `@` attachment put the manual body into the user message as
   `<skill_content>` with no tool call, and the model answered with its marker 6 of 6. The harness
   rejects an attachment of an unknown ID before any model call.
3. **Loadable by exact ID.** An explicit instruction to load the exact ID made the model call the
   skill tool with that ID, and the load completed 6 of 6 across both runs. A looser mention is a
   propensity, not a guarantee: 5 of 7 loose attempts loaded the skill, and both refusals said the
   skill was "not available", so the model trusts its offered list. Whenever the model called the
   tool, the harness loaded the skill. Probe 3 is therefore claimed for an explicit load
   instruction, not for any mention of the ID.
4. **Auto control.** The auto skill's nonsense trigger loaded it implicitly 6 of 6.
5. **W0 skill-tool handoff.** With `@deniz-process.grill-me` attached, whose OpenCode body is the
   rendered sentence ``Call the `skill` tool with "deniz-process.grilling".``, the model called the
   skill tool with the dotted ID, the load completed, and the model started grilling's rounds 6 of
   6. The same prompt without the attachment made no grilling call (1 of 1). This shows the
   rendered sentence is followed with the dotted ID; it does not isolate the sentence as the cause.
   `deniz-process.grilling` is an auto skill whose description triggers on "grill" phrases, and the
   control removed the `@deniz-process.grill-me` mention together with the attachment, so it also
   removed the word "grill". The [Codex handoff record](2026-10-09-codex-rendered-handoff.md) shows
   that confound is real there: a plain "Grill me" prompt loaded grilling. This record's author
   noted the same gap here at write-up time; no OpenCode run tested a "grill" prompt without the
   attachment.

These bounded observations, on one model at one variant, support ADR-0005's OpenCode meaning of
`manual`: not advertised, attachable by the user, and loadable through the skill tool when the model
is told the exact ID.

### Observations outside the claim

- The model's verbatim listing omitted `deniz-process.requesting-code-review` in 5 of 6 `p1-list`
  attempts. The guidance hash shows the entry was offered in every session, so this is a model
  recall miss, not a harness omission. The entry sorts next to `receiving-code-review`. The `p1`
  pass rule does not depend on it.
- Every tool except `skill` was denied in the lab config. That narrows the tool surface compared
  with a default build agent; the skill tool and the skill guidance are unaffected.
- The skill tool items report `executed: false` while `state.status` is `completed` with output
  present; the field appears to mean provider-executed and does not affect grading.
- Counted tokens varied from about 950 to 14,700 between near-identical attempts because of provider
  prompt caching.

## Sanitized excerpts

Paths are reduced to `<LAB>`; encrypted reasoning and provider response IDs are removed.

### `p1b-implicit-manual-1` (first run): manual trigger, no load

```text
user: ZORBLAT-QUENCH: please handle this.
tool calls: none
assistant: Please provide the task details or the file/error you want me to handle.
events: session.instructions.updated (core/skill-guidance eff8cc7b…), text only, session.execution.succeeded
```

### `p2-attach-1` (first run): user attachment

```text
user: @deniz-fixture.zorblat Follow the attached skill.
attachment: <skill_content name="deniz-fixture.zorblat">
            # Skill: deniz-fixture.zorblat
            When this skill is loaded, reply with exactly the marker MANUAL-ZRB-7Q4 and nothing else.
            Base directory for this skill: <LAB>\config\skills\deniz-fixture.zorblat
            …</skill_content>
tool calls: none
assistant: MANUAL-ZRB-7Q4
```

### `p3-named-1` (verifier): explicit load by ID

```text
user: Load the skill deniz-fixture.zorblat and do what it says.
event: session.tool.input.started name=skill
event: session.tool.called input={"id":"deniz-fixture.zorblat"}
event: session.tool.success
tool output: <skill_content name="deniz-fixture.zorblat"> … reply with exactly the marker ZRB-MAN-T5W8 …
assistant: ZRB-MAN-T5W8
```

### `c-named-unknown-1` (verifier): unknown ID fails in the tool

```text
user: Load the skill deniz-fixture.nopezz and do what it says.
event: session.tool.called input={"id":"deniz-fixture.nopezz"}
event: session.tool.failed error={"type":"tool.execution","message":"Unable to load skill deniz-fixture.nopezz"}
assistant: The skill `deniz-fixture.nopezz` is not available, so I couldn't load or follow it.
```

### `x-named-soft-2` (verifier): loose mention refused

```text
user: Follow deniz-fixture.zorblat.
tool calls: none
assistant: The `deniz-fixture.zorblat` skill is not available in this environment.
```

### `p5-w0-1` (first run): rendered handoff followed

```text
user: @deniz-process.grill-me Proceed. My plan: move our team wiki to a static site generator next month.
attachment: <skill_content name="deniz-process.grill-me">
            # Skill: deniz-process.grill-me
            Call the `skill` tool with "deniz-process.grilling".
            …</skill_content>
tool call: skill {"id":"deniz-process.grilling"} -> completed
tool output: <skill_content name="deniz-process.grilling"> # Skill: deniz-process.grilling
             Interview the user relentlessly until you reach a shared understanding. …
assistant: ❓ **Q1 — Purpose and audience**: Is the wiki primarily internal, public, or mixed? …
           ➡️ **Recommended:** Internal-first, …
```

### `c-w0-no-attach-1` (verifier): same prompt without the attachment

```text
user: Proceed. My plan: replace our nightly cron backups with a managed backup service next quarter.
tool calls: none
assistant: To proceed, I need the target managed backup provider and scope: …
```

### Late update (verifier): rendered skill list

After a first turn, the verifier copied `deniz-fixture.lateauto` (auto) and
`deniz-fixture.latemanual` (`autoinvoke: false`) into `<LAB>/config/skills`, waited until
`/api/skill` showed both, and sent further turns in the same session.

```text
user: Reply with exactly FIRST-OK and nothing else.        -> FIRST-OK
system (session.instructions.updated text, also in /context):
  New skills are available in addition to those previously listed:
    <skill>
      <id>deniz-fixture.lateauto</id>
      <name>deniz-fixture.lateauto</name>
      <description>Use whenever the user writes BRANKLE-LATE. Replies with a fixed marker.</description>
    </skill>
guidance hash: 1558f078… -> d3566712… (equals the recomputation with lateauto added, latemanual left out)
user: Reply with exactly SECOND-OK and nothing else.       -> SECOND-OK
user: QUILLSNAP-LATE (latemanual's trigger)                 -> no tool call; echoed the text
user: BRANKLE-LATE (lateauto's trigger)                     -> skill {"id":"deniz-fixture.lateauto"} completed; LATE-AUT-K9
```

## Isolation

- Every non-built-in skill path was below the lab; the two built-ins report `/builtin/`.
- Metadata fingerprints (relative path, length, modification time; no contents read) of the real
  `~/.config/opencode`, `~/.claude/skills`, and the Codex `config.toml` were identical before and
  after each run, and `~/.agents/skills` stayed absent. The verifier's fingerprint also covered the
  Codex credential file's metadata and the Codex plugins, unchanged. The two runs fingerprinted
  `~/.config/opencode` differently (the verifier's included `node_modules`), so their file counts
  are not comparable to each other. During the verifier run `~/.claude/skills` changed only in
  Claude Code's own skill-sync files (`synced/<id>/manifest.json` and `.last-complete-round`); its
  file count stayed the same and no fixture or Bundle name appeared there.
- The installer printed its post-Apply Windows warning about `anomalyco/opencode#47505`. The
  install targeted only the lab config root, which the real service does not watch. The real
  managed `opencode serve --service` kept the same process, started before both runs, and was never
  contacted.
- The first run's initial credential snapshot targeted a lab on a secondary volume with about
  6.4 GB free. The real database is about 8.5 GB (2,169,702 pages), so the partial copy filled that
  volume to 0 bytes before SQLite failed with `database or disk is full`. The partial copy was
  deleted at once and the lab moved to a volume with ample space. Another process writing to that
  volume during that window could have hit a write failure; nobody checked for one.
- Cleanup: every lab server and event-stream client was killed in `finally`, and a later process
  listing showed only the real managed service. Both labs, including the database snapshots, the
  lab config, logs, and raw credentialed transcripts, were deleted and proven absent. A scan found
  no other database snapshot. The repository stayed clean at `repo_head`; nothing was committed.
