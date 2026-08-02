# ADR-0006: The output is a transformation, not a subset

Date: 2026-08-02
Status: Accepted

## Context

Selection is the cheapest thing this repo does. Upstream already publishes usable skills; if all
that were wanted, one would install the upstream plugins. What upstream does **not** let you change
is who pulls the trigger, what shape the artifact takes, and whether any of it makes sense in a
harness it was never written for. That is the product.

## Decision

Curated output is a **transformation** of upstream along three independent axes. An item's position
on each is a curation decision, recorded in `curation/*.yaml`, and every axis is resolved **per
harness** — the harnesses disagree about what these words mean, and the manifest stays neutral.

**Axis 1 — Invocation: who pulls the trigger.** `invocation: auto | manual | both`, per
[ADR-0005](0005-invocation-intent-in-the-manifest.md). Upstream skills overwhelmingly fire on their
own; taking that back is the single behaviour this repo exists for.

**Axis 2 — Shape: what artifact it becomes.** An upstream skill may be emitted as a skill, a
command, or an agent. Shape is **not** derived from invocation and is **not** inherited from
upstream: `as:` states it explicitly when the default is wrong. Axis 1 answers *who starts it*,
axis 2 answers *what it is* — conflating them costs the ability to say "a command that the model
may also reach", which both harnesses can express.

**Axis 3 — Fit: whether the result is idiomatic where it lands.** An item written against one
harness's conventions must arrive at the other as something that harness understands. This is the
emitter's job, never the reader's, and it covers at least:

- **Cross-references** spelled the way the target harness resolves them — Claude Code's
  `<plugin>:<name>` namespacing is meaningless to OpenCode, which reaches skills by bare name.
- **Frontmatter** filtered to what the target recognises, with every dropped key reported.
- **Body content** where the two cannot be reconciled mechanically.

Per harness, the axes resolve like this:

| | Claude Code | OpenCode |
|---|---|---|
| Invocation | frontmatter flags on one artifact (`disable-model-invocation`, `user-invocable`) | a *choice of artifact* — skills are model-only by construction, commands are the only user-invocable surface |
| Shape | `skills/`, `commands/`, `agents/` inside a plugin; skills and commands behave alike | `skills/`, `commands/`, `agents/` — genuinely different concepts with different frontmatter |
| Fit | plugin-namespaced references, plugin manifest | flat namespace, no plugin concept, its own command/agent frontmatter |

The adapter model follows the useful constraint demonstrated by
[wshobson/agents](https://github.com/wshobson/agents): target incompatibilities belong in emitters,
not in every authoring decision. This forbids shortcuts that bypass an axis:

- **Mirroring one harness's tree into another.** A copy is not an adapter. If a harness's output is
  byte-identical to another's, axis 3 has not run.
- **Deriving shape from upstream.** "It is a skill upstream, so it is a skill here" removes axis 2.
- **Treating invocation as a Claude Code frontmatter concern.** It is the primary axis, and on
  OpenCode it is expressed by choosing a different artifact entirely.
- **Emitting a key the target harness ignores, silently.** Unrecognised frontmatter reaching an
  output tree is a reportable drop, not a harmless passenger.
- **Answering "the harness cannot express this" with passthrough.** Adapting an item that does not
  fit is the job. Where it genuinely cannot be adapted, the build reports it and a curation
  decision follows — silence is not an option.

Treating this as only ADR-0002's native-output choice was rejected because harness fit is just one
of the three axes. Leaving the contract only in implementation detail was also rejected; it is the
product definition relayed by `AGENTS.md`.

## Consequences

- `invocation` and `as` can disagree in useful ways and can also state combinations a harness cannot
  express. Emitters must not guess at those combinations.
- Shape and fit being per-harness means the output trees are intentionally not comparable; review
  has to inspect both.
- Axis 3 has no upper bound. "Idiomatic in the target harness" is a judgement that grows as the
  harnesses grow, so new incompatibilities create new emitter work or explicit curation decisions.
- Adapting content the emitter cannot reconcile mechanically implies per-harness body edits, which
  the overlay mechanism (ADR-0001) does not currently express — it produces one body for both
  trees. That gap is now a named cost of this decision rather than an oversight.
- A new harness stays localized to a new emitter but is not free: it must
  answer all three axes, not just the output format.
