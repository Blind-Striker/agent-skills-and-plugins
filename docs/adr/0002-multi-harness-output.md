# ADR-0002: Harness-native output instead of a common format

Date: 2026-10-08
Status: Accepted

## Context

Claude Code, OpenCode, and Codex share the `SKILL.md` convention but differ in packaging, artifact
shape, invocation controls, namespacing, and supported metadata. A common output format would either
leak harness-specific fields or reduce the targets to their lowest common denominator.

The harnesses also differ in how a user or model addresses an installed artifact. Claude Code and
Codex address plugin content through a plugin namespace that the harness supplies. OpenCode 2
discovers files in a flat config tree and derives each ID from the file path alone: a skill's ID is
the name of the folder that directly holds its `SKILL.md`, and skills with the same ID replace each
other, last source wins ([OpenCode 2 evidence](../research/opencode-2-target.md)).
Nothing in that layout carries the Module, so a bare name competes with built-in, third-party, and
project-local artifacts of the same name.

OpenCode 2 is not an incremental release of OpenCode 1. Skills are no longer slash commands, a
manual posture is native skill metadata, and agent frontmatter with keys outside the native set goes
through a version-1 compatibility migrator. Output that serves both lines needs either two OpenCode
emitters or a shape that fits neither line well.

## Decision

- One neutral authored source produces separate harness-native Claude Plugin, OpenCode Bundle, and
  Codex Plugin output. There is no common emitted format that any harness must interpret.
- The OpenCode target is OpenCode 2 only. OpenCode 1 is not a target, and no compatibility layer
  serves it.
- Each target receives only shapes and metadata it understands. An unrepresentable feature is
  dropped and reported rather than approximated or lost silently.
- A target may expose a capability that the neutral manifest intentionally leaves unspecified. That
  is a native realization rather than an approximation; the resolved target surface remains
  reviewable beside the authored intent.
- Every output tree is ready native content, not input to a runtime adapter. Installation may
  compose emitted files, but it does not reinterpret bodies, invocation, metadata, or references.
- Every target addresses an artifact inside its Module. Claude Code and Codex supply that namespace
  natively, so their output names stay concise and carry no Module prefix. OpenCode 2 has no
  namespace for file-discovered artifacts, so every OpenCode ID is `<plugin>.<name>`, for example
  `deniz-process.brainstorming`. The ID is the skill folder name, the command or agent file name
  without `.md`, and the text a user types after `@` or `/`. A skill's frontmatter `name` equals
  its ID, because the model sees both while the skill tool resolves only the ID.
- Bare output names stay unique across all Modules as curation policy and follow one portable-name
  grammar. `<plugin>.<name>` is therefore one-to-one with the neutral `plugin:name` symbol, and
  dependency resolution, linking, and required-Module derivation stay keyed by bare name. Identity
  is also checked in each target's native namespace: Claude Code keeps artifact kinds apart,
  OpenCode 2 offers skills and agents in one `@` list, and Codex flattens every resolved kind to a
  plugin-local skill and therefore rejects same-name cross-kind collisions within that plugin.

A common target format and runtime adaptation were rejected because both would move harness
differences out of the emitter and either leak unsupported concepts or reduce the outputs to their
lowest common denominator. Support for OpenCode 1 beside OpenCode 2 was rejected for the same
reason: the two lines disagree on skill, command, and invocation shape, so a compatibility layer is
either a second emitter or a common denominator of the two.

A universal Module prefix in Claude Code and Codex output names remains rejected: it would tax every
user-facing name with a namespace that those harnesses already supply. For OpenCode, these
alternatives to `<plugin>.<name>` were rejected:

- **Flat bare names.** They keep names short, but a bare ID can hide, or be hidden by, a built-in,
  third-party, or project-local artifact with the same name, and the user cannot see which Module
  an artifact belongs to.
- **Nested folders** such as `skills/<plugin>/<name>/SKILL.md`. OpenCode 2 takes a skill ID from the
  leaf folder only, so nesting gives no namespace to skills. Nested commands and agents get
  `<plugin>/<name>`, but that spelling differs from skills, and the terminal UI does not reopen its
  `/` suggestions for a name that contains an inner `/`.
- **A colon separator** that matches Claude Code. `:` is not a legal character in a Windows file
  name, so colon IDs are possible only through an OpenCode runtime plugin that registers the
  artifacts itself. That replaces file Bundles and byte-preserving installation with the runtime
  adapter that this decision rejects.
- **Other separators.** `_` and `--` register, but a reader cannot tell them apart from the hyphens
  inside names; `@` and `/` break suggestion matching. `.` works for discovery, the model's skill
  list, the skill tool's exact-ID lookup, `@` attachment, and `/` submission, and it is outside the
  neutral `ns:name` reference grammar, so a leaked neutral reference stays detectable.

Current emitter and body mechanics live in
[Transformation and emission](../architecture/transformation-and-emission.md); the supported
OpenCode version floor and current Package, Destination, Selection, `OPENCODE_CONFIG_DIR`, and
installer mechanics live in
[Distribution and installation](../architecture/distribution-and-installation.md). Reference
localization follows [ADR-0008](0008-references-are-symbols.md), and the per-harness reference
spelling lives in [References and linking](../architecture/references-and-linking.md). The
portable-name grammar lives in [`curation/SCHEMA.md`](../../curation/SCHEMA.md).

## Consequences

- No harness is constrained by what another can express, at the cost of reviewing intentionally
  different output trees.
- A new harness requires a new emitter that answers the transformation contract, not a redesign of
  the authored source.
- A reported drop turns an incompatibility into a curation decision instead of a latent runtime
  surprise.
- A native capability left unspecified by neutral intent may differ across targets without becoming
  a reported loss; the ledger must still show what each emitter actually produced.
- One OpenCode line keeps one OpenCode emitter that can use native metadata. The cost is reach:
  OpenCode 2 is a parallel opt-in line while OpenCode's default channels still install OpenCode 1,
  OpenCode 1 users are not supported, and output installed under OpenCode 1 degrades, for example
  manual skills become visible to the model.
- An OpenCode ID shows its Module, and our artifacts neither hide nor are hidden by a same-named
  built-in, third-party, or project-local artifact. Cross-Module collisions cannot occur in any
  target's address; Codex keeps cross-plugin identity through its plugin namespace.
- OpenCode IDs are longer than the bare names, and one item has a different spelling in each
  harness. Fuzzy ranking in the OpenCode terminal UI puts `@deniz-process.x` below a bare
  third-party `x` until the user types the prefix.
- A bare name in prose is not an OpenCode ID. A model that passes a bare handle to the skill
  tool gets no skill, or a third-party skill with that name. Load-bearing handles must therefore be
  authored references, and validation must check that every rendered OpenCode ID resolves to an
  emitted artifact.
- A `.` in a skill's frontmatter `name` is outside the Agent Skills name character set. OpenCode
  does not check it, but another tool that reads the OpenCode tree could reject it.
- Global bare-name uniqueness is a curation constraint that the OpenCode namespace alone would not
  need. It keeps dependency resolution and linking unqualified.
- Reconsider the OpenCode spelling if OpenCode gains a native namespace for file-discovered
  artifacts. Reconsider global bare-name uniqueness only if it becomes the limiting curation
  constraint.
