# References and linking

Date: 2026-10-09

## Responsibility

This document owns the current reference model from an assembled body through localization,
linking, dependency declarations, and ledger review. It does not define curation fields; authoring
syntax remains in [`curation/SCHEMA.md`](../../curation/SCHEMA.md). The reason strings are treated as
symbols and checked in tiers is [ADR-0008](../adr/0008-references-are-symbols.md); the reason each
harness gets its own spelling is [ADR-0002](../adr/0002-multi-harness-output.md); what `manual`
means on each harness is [ADR-0005](../adr/0005-invocation-intent-in-the-manifest.md).

This document states the decided OpenCode 2 reference model. Where the implementation has not
caught up, the gap and its responsible files are tracked in
[`docs/ROADMAP.md`](../ROADMAP.md#known-gaps).

## One grammar, three evidence tiers

[`tools/lib/refs.ts`](../../tools/lib/refs.ts) is the shared namespaced-reference scanner used by
rewriting, validation, and ledger generation. It also owns the skill-tool call spans, the handoff
templates, and the path claims described below, so rewriting and validation read one grammar.
Detection happens while identity is still explicit.
OpenCode's dotted IDs and Codex's converged `$` spelling are rendered output: checks on those trees
confirm that a rendered ID resolves, but they never re-derive the full neutral symbol semantics.

### Facts

A lowercase namespaced spelling `namespace:name` is a model-edge fact. A leading slash,
`/namespace:name`, changes the fact to a user-pointer; the slash is direction metadata and is not
part of the address itself. The scanner rejects token fragments, chained addresses, URLs, and
uppercase/snake-case lookalikes rather than guessing. OpenCode's `<plugin>.<name>` separator is not
part of this grammar, so rendered OpenCode IDs never re-enter fact detection.

In an owned body, the spelling states the runtime audience:

- a model-edge requires a target the model can reach in every generated harness tree;
- a user-pointer requires a target the user can reach in every generated harness tree.

After localization, an own namespace is authoritative build state. A missing own target or an
audience mismatch is an error. A recognized upstream namespace left unreplaced is a per-reference
warning. Other namespaces warn once with occurrence count and example paths, except for a narrow
exact-address allowlist of known prose lookalikes. Unknown namespaces are not silently promoted into
dependencies.

### Paths

Relative paths are deterministic, but the linker reports only breakage the transformation can
reasonably have caused:

- a sibling-item climb such as `../other-item/...` must still land after rename, exclusion,
  omission, or conversion;
- a missing same-item file is a finding when the upstream item still contains that file;
- other same-item paths that upstream never contained remain ordinary illustrative prose.

Markdown link targets are read from Markdown files. Two **path claims** are read from every bundled
text file in a skill folder, Markdown or not, so scripts are included. A bundled text file is one
with no NUL byte whose bytes survive a UTF-8 round trip; any other file is binary and is never read
or rewritten. A claim's path runs to the next whitespace, quote, backtick, `)`, `]`, `#`, or `?`, so
a link's fragment or query is not part of the path, with one trailing `.` dropped, and neither claim
may continue a longer path. A `<segment>` is a portable
lowercase name or an OpenCode ID `<plugin>.<name>`, so the OpenCode tree's respelled claims are read
back with the same grammar and judged by the bare name the ID carries.

- A **landing climb** is `(../)+<segment>/` whose `../` count equals the file's depth below its skill
  folder plus one, so it lands on the shared `skills/` directory, with no character from
  `[A-Za-z0-9._/-]` before it. It fails closed: its full path must resolve in
  that tree whatever the segment names, because a climb onto the skills directory has no
  illustrative reading. Uppercase segments such as `../Api` are not claims.
- An **item-root path** is `skills/<segment>/` with no character from `[A-Za-z0-9._/~$-]` before
  it, so a consumer path such as `.agents/skills/x/` or `~/.claude/skills/x/` is not one. It
  resolves against the tree's skills root, the layout the harness installs. It is checked only when
  its segment is an estate name (see [Handoff templates](#handoff-templates)); an authoring example
  such as `skills/testing/` names nothing and stays prose.

No path, whether a claim or a Markdown link, may land in the folder of another skill whose Claude
output carries `disable-model-invocation: true`; an agent is not such an item, even where Codex
emits it as a skill folder. A path is a read, so it would let the model read a
body that Claude forbids it to load. Point the human at such an item with a `/ns:name`
user-pointer. An item's own folder is exempt.

Every OpenCode skill folder is named by its OpenCode ID, so the OpenCode tree respells both claims
when their segment names an emitted skill: `../<name>/` becomes `../<plugin>.<name>/` and
`skills/<name>/` becomes `skills/<plugin>.<name>/`, using the target item's own Module, in every
bundled text file of a skill folder. A file is rewritten only when its bytes change, and its
executable mode carries through. The respelled path addresses the installed layout, in which every
Module's skills share one `skills/` directory. Claude Code and Codex keep the bare folder name,
which is their folder name. The linker then checks the respelled path like any other. These checks
are the relative-path rules (L8, R1, R2, and the path-claim rule P) in
[`validateRepo`](../../tools/validate.ts); a landing climb inside a Markdown link is judged once, by
P.

A skill-relative path in an item resolved `as: command` can stop resolving from the command location
in a harness that keeps the command shape. The converted-command warning class, which reported a
path that works from a skill copy but not from an additional command copy, retires with the implicit
OpenCode conversions it covered: global-root stubs, `both` duplicate commands, and inline `manual`
commands. No item is resolved `as: command` today; an explicit `as: command` item with bundled files
would reopen the question of that warning.

### Candidates

A known output name appearing as a standalone bare word is a candidate, not a fact. `/name` without
a namespace is still candidate-tier prose. Candidate matching deliberately over-reports ordinary
words, so candidates are surfaced for human reading and never enter `depends_on`, linker success, or
the ledger merely because a patch touched the surrounding body.

The intended automatic surface is the pin-change report in `sync`, which compares candidate hits
before and after a changed upstream `SKILL.md`. Promotion requires an authored namespaced spelling,
not confidence in a heuristic.

### Handoff templates

Some shapes tell the model to load another skill, so they are load-bearing. A closed set of handoff
templates is checked on the canonical Claude tree, `plugins/`, where every fact still carries a
namespace. A bare name in one of them is a build error, not a candidate:

| Template | Form | Payload rule |
|---|---|---|
| Skill-tool call | `<verb> the Skill tool with "<p>"`; `<verb> the Skill tool twice, for "<p>" and "<p>"`; or the generic `<verb> the Skill tool for` when no `"` follows. `<verb>` is `Call`, `call`, `calls`, or `calling`. | Fail closed: each `<p>` is exactly one model-edge fact in an own namespace, and any `skill tool`, in any letter case, outside one of these forms is an error. |
| Imperative named skill | `Load`, `Use`, `Follow`, `Invoke`, or `Call`, with either initial case, earlier in the same sentence than `` `<name>` `` followed by whitespace and `skill` | A bare estate name is an error. |
| Direct load | `Load` or `Invoke`, with either initial case and a word boundary before it (so `Re-invoke` counts), then whitespace and `` `<name>` ``, continued through `` , `<name>` ``, `` and `<name>` ``, or `` or `<name>` `` | A bare estate name is an error. |
| Route | `→`, optional `**`, then `` `<name>` `` | A bare estate name is an error. |

A sentence ends at `.`, `;`, `:`, `!`, `?`, `|`, a blank line, or a newline followed by a list,
table, heading, or quote marker. A `<name>` is a backticked lowercase portable name, so a fact,
which carries a colon, never matches. The estate names are every scanned upstream component
address and frontmatter name, every manifest item's output name with excluded items included, and
every original skill. A name outside that set, such as the `docker` CLI or an external
`dotnet-inspect` skill, stays silent. A hit on an emitted name is fixed by promotion: author
`ns:name` with its `depends_on` entry, or `/ns:name` when the human is the audience. A hit on an
estate name that is not emitted is rerouted or removed.

These are not templates and stay candidates: a "see the `x` skill" reference, a ``(use `x`)`` or
``Use `x` `` routing hint without the word `skill`, an unbackticked imperative such as "invoke the
writing-plans skill", ``Hand off to `x` ``, and a capitalized fact.

## Localization

The rewrite map keys the scanner's upstream namespace and filesystem address to the resolved output
name. A renamed item therefore changes the symbol target, not just its destination filename.
Original skills add their authored `<plugin>:<top-level-directory>` identity to the same map, so a
curated item can guard an original-skill target without inventing an upstream address. Each harness
renders the same neutral fact in its own spelling:

| Harness | Model-edge | User-pointer |
|---|---|---|
| Claude Code | `<plugin>:<output-name>` | `/<plugin>:<output-name>` |
| OpenCode | `<plugin>.<output-name>` | `@<plugin>.<output-name>` for a skill or agent target; `/<plugin>.<output-name>` for a command target |
| Codex | `$<plugin>:<output-name>` | `$<plugin>:<output-name>` |

OpenCode attaches skills and agents with `@` and runs only commands with `/`, so the pointer's
neutral leading slash becomes the prefix that matches the target's kind. Codex rendering consumes a
pointer's neutral leading slash instead of producing `/$...`; model and pointer remain separate
semantic facts even though their final spelling converges. The map and in-place rewrite are
[`tools/lib/rewrite.ts`](../../tools/lib/rewrite.ts).

All three trees are rewritten from their own pre-localized copies. OpenCode and Codex are not
produced by adapting already localized Claude text. Codex output is rejected when it holds a
dangling, unrendered, or `/$` reference; the OpenCode checks follow.

Curating one upstream source more than once is currently last-write-wins in the source-address map.
Validation warns with both outputs because every upstream fact for that source will localize to the
last item; the warning is not a proof that the ambiguity is harmless.

### Harness phrasing

Two phrasings name a harness tool, so localization renders each as a unit around its neutral facts,
before the facts themselves are rendered. Authors write upstream's Claude Code wording, and the
Claude Code rendering is identity. Every other sentence is left as written. The renderer and its
closed tables are `localize` in [`tools/lib/rewrite.ts`](../../tools/lib/rewrite.ts); the skill-tool
call grammar is the template in [Handoff templates](#handoff-templates). Phrasing is rendered in
Markdown files only.

The skill-tool call keeps its verb in Claude Code and OpenCode. Codex maps `Call`, `call`, `calls`,
and `calling` to `Invoke`, `invoke`, `invokes`, and `invoking`. Shown after fact rendering:

| Form | Claude Code | OpenCode 2 | Codex |
|---|---|---|---|
| with | `Call the Skill tool with "deniz-process:grilling"` | ``Call the `skill` tool with "deniz-process.grilling"`` | ``Invoke `$deniz-process:grilling` `` |
| twice | `call the Skill tool twice, for "deniz-process:grilling" and "deniz-process:domain-modeling"` | ``call the `skill` tool twice, for "deniz-process.grilling" and "deniz-process.domain-modeling"`` | ``invoke `$deniz-process:grilling` and `$deniz-process:domain-modeling` `` |
| generic | `call the Skill tool for` | ``call the `skill` tool for`` | `invoke` |

Subagent dispatch words are a closed table, matched exactly:

| Claude Code (upstream) | OpenCode 2 | Codex |
|---|---|---|
| `Subagent (general-purpose)` | `Subagent (general)` | `Subagent` |
| `` `general-purpose` ``, whitespace, `subagent` | `` `general` ``, the same whitespace, `subagent` | `subagent` |
| `` `Agent` call `` or `` `Agent` calls `` | `` `subagent` call `` or `` `subagent` calls `` | `subagent call` or `subagent calls` |

The tool names come from recorded evidence. OpenCode 2's model tool `skill` takes an exact skill
ID, its subagent tool is `subagent`, and its built-in general subagent is `general`
([OpenCode 2 research](../research/opencode-2-target.md), sections 6 and 8). Codex has no skill
tool: explicit selection is `$plugin:skill`
([Codex surfaces](../research/codex-native-plugin-and-skill-surfaces.md#skill-invocation)).
This repository's research records no Codex subagent tool or agent type, so Codex wording names
neither.

Whether a model follows a rendered sentence is runtime evidence, bounded to one model
(`gpt-5.6-luna` at low effort):

- **OpenCode 2.** With `@deniz-process.grill-me` attached, the model followed its rendered
  ``Call the `skill` tool with "deniz-process.grilling"`` and loaded the dotted ID in every attempt
  of two independent runs. The same prompt without the attachment made no grilling call, but that
  control also dropped the word "grill", which the auto target `grilling` triggers on, so the
  sentence is followed without being isolated as the cause
  ([record](../../experiments/harness-invocation/records/2026-10-09-opencode2-manual-skill.md)).
- **Codex.** A fixture body ``Invoke `$plugin:skill` `` handoff was followed
  ([record](../../experiments/harness-invocation/records/2026-09-07-codex-plugin-behaviour.md)).
  The rendered `$deniz-process:grill-me` handoff was injected and its target followed in every
  attempt of two runs, but its target `grilling` is an auto skill that a plain "Grill me" prompt
  also loads, so those runs do not show that the handoff alone causes the load
  ([record](../../experiments/harness-invocation/records/2026-10-09-codex-rendered-handoff.md)).
- The OpenCode `Subagent (general)` label and the tool-free Codex dispatch wording are unmeasured.
  The open measurements are in [Known Gaps](../ROADMAP.md#known-gaps).

A phrasing that neither table covers is caught by the
[harness vocabulary check](#harness-vocabulary-check) instead of leaking.

## OpenCode ID checks

`validate` applies these rules to every Markdown file in `opencode/`:

- **No own namespace survives.** An own `namespace:name` address left in `opencode/` is an error;
  localization must have rendered it as an OpenCode ID.
- **Every rendered ID resolves.** Every rendered `<own-plugin>.<name>`, bare or behind `@` or `/`,
  must name an emitted OpenCode ID.
- **Every skill-tool handle is a fact.** A quoted handle in a skill-tool call is load-bearing, and a
  bare one never names an OpenCode ID, because every OpenCode ID carries its Module. The
  [skill-tool call template](#handoff-templates) requires each handle to be a namespaced fact on
  the canonical tree, so the linker resolves it and OpenCode renders it as `<plugin>.<name>`, which
  the rendered-ID rule above then checks.

These checks walk the whole `opencode/` tree, so they also cover text that originates in original
skills.

## Harness vocabulary check

`validate` rejects Claude Code tool vocabulary in every Markdown file under `opencode/` and `codex/`:
`skill tool` in any letter case, `Subagent (general-purpose)`, `` `general-purpose` ``,
`general-purpose` followed by whitespace and `agent` or `subagent`, `` `Agent` ``, `Agent tool`,
`Task tool`, and `subagent_type`. The rendered OpenCode ``the `skill` tool`` does not match, because
a backtick separates the words. The patterns sit beside the phrasing tables in
[`tools/lib/rewrite.ts`](../../tools/lib/rewrite.ts), and every non-Claude rendering in those tables
must pass them. A new upstream phrasing therefore fails the build instead of reaching a harness that
has no such tool; the fix is a curated rewording or a new table row.

## `depends_on` and audience reachability

`depends_on` is the manifest-side declaration of model-edge facts only. It contains resolved output
names, while bodies keep neutral namespaced addresses. The linker derives model targets from all
Markdown shipped by each non-excluded manifest item and enforces exact agreement in both directions:
an undeclared fact and a declaration with no shipped fact are both errors. User-pointers, paths, and
candidates are not dependency declarations.

For each fact, the linker checks the canonical neutral kind and asks whether the target has the
required audience surface. Claude reachability comes from emitted invocation flags and artifact
posture. OpenCode model reachability comes from an advertised skill, one without the
`opencode/autoinvoke: false` hiding key; OpenCode user reachability comes from any skill or agent,
which the user attaches with `@`, or a command, which the user runs with `/`. Codex reachability
comes from the skill plus its implicit policy, while explicit invocation remains available for every
skill. This is a generated-estate link, not a runtime call graph (the reference-linking section of
[`validateRepo`](../../tools/validate.ts)).

Reachability is not propensity. A green link proves that the intended audience has a mechanism to
reach the target; it cannot prove that a model will select it, follow a pointer, or obey the target's
discipline. Those are runtime observations governed by the
[harness invocation protocol](../../experiments/harness-invocation/protocol.md), with bounded evidence
in the committed [records](../../experiments/harness-invocation/records/README.md).

## Ledger semantics

`docs/ledger.json` is regenerated after all three trees have their final reference spelling. Each
non-excluded manifest item has one key shaped
`<plugin>/<resolved-output-kind>/<output-name>`. The key keeps the bare output name; the OpenCode ID
is not part of it. Each item emits one OpenCode artifact of its resolved kind, so a skill resolved as
`manual` remains under `/skill/` and its OpenCode artifact list records a skill.

Each entry projects the review-relevant state: source, declared invocation and body mode,
merge-source addresses, declared dependencies, emitted artifact kinds, description, own fact edges
in each harness spelling, emitted Claude boolean invocation flags, OpenCode identity
(`<plugin>.<name>`), whether an OpenCode skill is advertised to the model (`advertised`, false when
it carries the hiding key), OpenCode drops and metadata transformations, and Codex identity, source/resolved/emitted kinds, material kind transformation, invocation
capabilities, policy files, metadata drops, and body transformations. OpenCode and Codex edges are
respelled from the known neutral facts rather than rediscovered from final text.

The ledger is deterministic but intentionally incomplete. It does not serialize complete emitted
files, path-tier findings, candidates, overlay stamp filenames, or Module install dependencies.
Original skills under `skills/` are also outside the manifest-item loop. Bundle integrity and
installation state have their own manifests and are owned by
[Distribution and installation](distribution-and-installation.md).

## Proof boundary and current limits

- `validate` links the complete generated Claude Plugin, Codex Plugin, and OpenCode Module estate.
  An installer Selection is a later, independent boundary. Full-estate linking does not prove that
  every subset is a valid Selection or
  that selected Bundles are item/API compatible. The installer separately presence-checks recorded
  `requiredModules` against the Selection a request would produce; that is not automatic expansion
  and is owned by [Distribution and installation](distribution-and-installation.md).
- Same output names in different artifact kinds are rejected within one plugin because Codex flattens
  every resolved kind into one skill namespace. Across plugins, Codex identities and OpenCode IDs are
  qualified, but `depends_on`, linker target state, and `requiredModules` derivation are keyed by
  the bare output name; repository-wide bare-name uniqueness is what keeps those keys unambiguous
  ([`curation/SCHEMA.md`](../../curation/SCHEMA.md)).
- Linker target state is name-only and records audience reachability rather than target kind. A
  `both` target can satisfy either audience edge, so a semantically wrong target kind may remain a
  human review concern even when linkage is green.
- The `sync` CLI derives its candidate universe from the output-name segments of ledger keys
  (`<plugin>/<kind>/<name>`) plus authored original-skill directory names. Its candidate-edge report
  is still bounded by the tier: it compares known output names across one changed `SKILL.md`, so it
  surfaces edges for reading and never promotes one to a fact, a declaration, or build state.
- The namespaced fact scanner is lowercase-only. Capitalized spellings can evade fact detection.
- Bare-name composition can work at runtime in a harness that resolves bare names while remaining
  deliberately unguarded outside the handoff templates. Its success does not make candidates
  authoritative after the fact. In OpenCode a bare name matches none of this repository's IDs; the
  templates are the checked load-bearing forms. They are closed and English: a load-bearing sentence
  outside them is not caught, and a rephrased skill-tool sentence fails rather than passes.
- The estate names come from the current scan, manifests, and original skills. A name deleted
  upstream at a pin move leaves them once its manifest item leaves, so a new bare mention of it is
  not caught; a handoff already promoted to a fact still fails as a dangling reference when its
  target is deleted. How deletions stay recorded is tracked in
  [Known Gaps](../ROADMAP.md#known-gaps).
- Rendered harness phrasing proves only that the sentence names the harness's own tool. Whether a
  model follows it is runtime evidence, bounded as described under
  [Harness phrasing](#harness-phrasing).
- Original skills under `skills/` are guarded edge targets: curated items can author their
  `<plugin>:<directory>` fact, localize it for all three harnesses, and declare the output name in
  `depends_on`. The derived-edge source scan still walks manifest items only, so references
  originating in an original skill are not scanned or declared
  ([`validateRepo`](../../tools/validate.ts)).
- Relative-path checks are attribution-aware, not a general Markdown link checker. A silent path may
  still be wrong upstream, and a broken path in an `as: command` body may still require a body or
  shape decision. Path claims are read only in skill folders; a command or agent file carries no
  claim. A claim's depth is measured from the file that holds it, so a script climb that the shell
  resolves against its working directory is still judged and respelled as if it resolved against
  the file; no bundled non-Markdown file holds a claim at the current pins.
- The linker proves symbol existence and audience reachability, not model behavior. Runtime samples
  remain version-, model-, prompt-, and repetition-bounded evidence rather than deterministic rates.
