# ADR-0006: The output is a transformation, not a subset

Date: 2026-07-31
Status: Accepted

## Context

Agents onboarding onto this repository keep forming the same wrong model: that curation means
picking upstream skills and copying them, with a few frontmatter tweaks. Everything they read early
supports that reading — the manifests look like allow-lists, the skill path of the build is a file
copy, and `opencode/skills/` is today byte-identical to the built Claude skills.

The consequence is not academic. Proposals arrive that assume the OpenCode tree mirrors the Claude
tree, that an item's artifact type is fixed by whatever upstream made it, and that "who invokes
this" is a Claude Code frontmatter detail rather than the point. Each one has to be corrected by
hand, from scratch, every session.

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

The model is taken from [wshobson/agents](https://github.com/wshobson/agents), whose governing
sentence is the design target: *each adapter handles incompatibilities mechanically — authors don't
need to know the per-harness rules to write portable content.* Its Codex adapter converts commands
into skills because Codex has no command concept; the conversion runs in whichever direction the
target needs.

### What this forbids

These are the shortcuts that keep getting proposed. Each is a regression against the axis it
bypasses, however convenient it looks:

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

### Alternatives considered

- **Keep the purpose statement mechanical and let agents read the ADRs.** Rejected empirically: they
  do not, in time. `AGENTS.md` is what loads at session start, so the model an agent forms comes
  from there. It now carries a short statement of this contract and links here.
- **Fold this into ADR-0002.** Rejected: ADR-0002 decides *harness-native output instead of a common
  format*, which is one axis of three. This is the contract the whole machine is measured against.

## Consequences

- The manifest gains a second orthogonal dial. `invocation` and `as` can disagree in useful ways,
  and can also be set to combinations one harness cannot express — `validate` has to catch those
  rather than let an emitter guess.
- Shape being per-harness means the two output trees stop being comparable. Reasoning about the
  build requires reading both emitters, and reviewing a curation change means checking both trees.
- Axis 3 has no upper bound. "Idiomatic in the target harness" is a judgement that grows as the
  harnesses grow, and each new incompatibility is discovered by using the output, not by reading a
  spec. The build report and `validate` are where that discovery has to surface.
- Adapting content the emitter cannot reconcile mechanically implies per-harness body edits, which
  the overlay mechanism (ADR-0001) does not currently express — it produces one body for both
  trees. That gap is now a named cost of this decision rather than an oversight.
- A third harness stays cheap in principle (a new emitter, ADR-0002) but is no longer free: it must
  answer all three axes, not just the output format.
