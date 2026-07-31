# ADR-0008: References are symbols — one model, a linker, a ledger

Date: 2026-07-31
Status: Accepted

## Context

Cross-item references are strings all the way through the build. Three scanners read them
independently — the rewrite map, `validate`'s leftover-reference pattern, and the coupling-graph
greps recorded in the research docs — and none of them answers the question the output actually
poses: does everything a shipped body names exist, and can its intended audience reach it, in the
tree it shipped to?

The holes this leaves are measured, not hypothetical:

- An overlay authored in output spelling (`deniz-process:x`) ships a dead reference into the
  OpenCode tree with no finding — the leftover scan only knows upstream namespaces.
- The shipped using-superpowers body instructs the model to invoke `deniz-process:brainstorming`,
  a `manual` item the model structurally cannot see
  ([skill-invocation-across-harnesses.md](../research/skill-invocation-across-harnesses.md)).
  Build green, validate 0/0; at runtime the session stumbles, softly and nondeterministically.
- Flipping an item's `invocation` reshapes both trees and can strand every body that names it —
  today that flip is silent.
- On a pin move, upstream flipping its own frontmatter posture on a passthrough item arrives
  unannounced; `sync` reports file paths, not meaning.

The batch-1 rule — a `manual` item is unreachable even from another skill's body, so every
body-invoked target must be `auto`/`both` — lives in manifest comments. The dependency knowledge
lives in greps that over-report because upstream names are ordinary words. Both are enforced by
curator vigilance, which is the one mechanism this repo set out not to rely on. The promise is
that everything an artifact needs at runtime resolves where it lands (AGENTS.md, compile-time
boundary), and the machine does not currently check it.

## Decision

Five parts, decided together (2026-07-31).

**1. One reference model.** A single library extracts every cross-item reference from the final
neutral bodies — post-overlay, pre-rewrite, the address space overlays are authored in. It is the
only reference scanner: the rewrite consumes it, `validate` links against it, `sync` diffs it.
Extracted references come in two tiers:

- **Facts** — namespaced spellings, the upstream address of the target's host
  (e.g. `superpowers:test-driven-development`). Exact, machine-authoritative.
- **Candidates** — bare-name and path matches against known item names. Heuristic by nature
  (upstream names are ordinary words; the greps measurably over-report), so candidates are
  surfaced for human reading and never become build state.

Dependencies are never derived from output trees: the OpenCode tree is namespace-less by design,
so rendered text cannot be parsed back into identity. Detection runs where the namespace exists —
before rendering — and both trees are produced from what it finds.

**2. A spelling convention gives every fact a direction.** Who traverses an edge at runtime
decides its validity rule, so the kind must be machine-readable. It lives in the spelling, the
one place model and human both read:

| Spelling (neutral space) | Kind | Runtime meaning | Valid targets |
|---|---|---|---|
| `ns:name` | **model-edge** | the model invokes the target mid-work | `auto`, `both` |
| `/ns:name` | **user-pointer** | the human is told what to open | `manual`, `both` |

The same rewrite localizes both forms per tree — `deniz-process:x` / bare `x` for model-edges,
`/deniz-process:x` / `/x` for pointers — each the form its harness actually resolves. The
convention binds text this repo authors (overlays, patches, own skills); upstream passthrough
prose stays candidate-tier until an edit touches it, and any touch promotes it into the
convention.

**3. `validate` becomes a linker.** Per output tree, in that tree's own address space: every fact
resolves; every model-edge targets something its audience can invoke (`manual` suppression is
structural — measured); every user-pointer targets something a user can type; referenced parked
files exist. Named blind spot, accepted: a wrong-kind edge to a `both` target validates — wording
is caught by review, not by the machine.

**4. `depends_on` declares the model-edges; both directions are errors.** Each item lists the
output names of its model-edge targets. A declared edge with no matching fact is stale; a fact
with no declaration is undeclared; both stop the build. The manifest speaks output space (our
set's names), bodies speak input space (upstream addresses) — the linker owns the mapping between
them. Dependency targets are not content-hashed: they are passthrough items whose upstream
updates are wanted; guarding merged-in content is `merged_from`'s job
([ADR-0001](0001-submodule-manifest-overlay-architecture.md)). Transcription drafts are generated
from the ledger and reviewed by the curator before they land — the machine proposes, the manifest
records a human decision.

**5. The build emits a ledger.** `docs/ledger.json`, generated and committed like every build
output: per item × harness, the resolved end state — invocation, emitted artifacts and flags,
description, fact-edges in that tree's spelling, dropped frontmatter keys, parked files, body
ownership and merge sources. Serialization is diff-optimized (deterministic order, one fact per
line) because `git diff` on this file is the repo's notification channel: a posture flip — the
curator's or upstream's — a shape change, or an edge change is one legible line in review, and
CI's staleness gate already guarantees the file is never behind the trees it describes.

### Alternatives considered

- **Declare edge kinds in the manifest** (`depends_on: [{target, kind}]`) instead of the spelling
  convention. Rejected: the model never reads the manifest. Runtime behaviour is steered by body
  prose, so a declared `pointer` over an instruct-to-invoke sentence validates green while the
  session still hits the wall — a declared/actual divergence of exactly the class this ADR exists
  to kill. The spelling puts the intent in the one place every audience reads.
- **Derive the dependency graph from built output.** Rejected: bare names in the OpenCode tree are
  words, not identities; parsing them back is the candidate heuristic pretending to be
  authoritative.
- **Warn instead of error on undeclared facts.** Rejected on this repo's own finding: a warning in
  a green build is one nobody reads (ADR-0001), and the declared map's guarantee — never silently
  stale — would be false the day it lands.
- **Hash dependency targets like overlay sources.** Rejected: the ladder (ADR-0001) prices hashing
  as the cost of *owning* content; a reference wants the target's updates to flow.

## Consequences

- Every real edge exists twice by construction — body fact and manifest declaration — and cannot
  diverge silently; the build stops. The cost: a body edit that adds a reference needs a manifest
  edit in the same change. That is the same-commit culture the inventory gate already enforces,
  extended to dependencies.
- Authoring gains one rule: targets are spelled by their upstream address, slashed when the human
  is the audience. Misspellings do not survive — a wrong namespace dangles, a wrong kind hits the
  reachability rules, a missing declaration errors — so the linker's messages teach the
  convention.
- The linker proves **resolvability, not behaviour**. Whether a model actually treats `/ns:name`
  as the user's move is a runtime property, measured the way all runtime properties are here
  ([harness-probing.md](../agents/harness-probing.md)) — event-driven, after body-changing waves
  and harness upgrades, never in CI.
- Candidates shrink monotonically: every curation touch promotes prose into the convention; new
  content is born inside it. The heuristic tier exists to *find* upstream's legacy prose, never to
  judge it.
- Three standing gaps retire structurally rather than by patch: the doubled per-tree warnings
  (one model, findings grouped at the source), the own-skill collision false negative (one symbol
  table across all emissions), and the `.agent.md` address bug (one address computation).
- Own skills have no upstream address. None is referenced today; the refs library defines their
  neutral spelling the day one is, and until then treats them as emission-only symbols.
- `sync` gains meaning on top of paths: posture drift on passthrough items, merge-source hits,
  and candidate-edge diffs against the ledger — the pin-move report speaks in curation terms.
