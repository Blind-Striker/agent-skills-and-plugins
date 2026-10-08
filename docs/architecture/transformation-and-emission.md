# Transformation and emission

Date: 2026-10-08

## Responsibility

This document owns the current compile-time mechanics: how authored inputs become separate Claude
Code, OpenCode, and Codex artifacts, where body ownership sits, and where compilation hands off to
installation and runtime. Distribution terms retain the precise meanings in
[`CONTEXT.md`](../../CONTEXT.md); manifest grammar and authoring choices remain in
[`curation/SCHEMA.md`](../../curation/SCHEMA.md).

The rationale is split across the ADRs: [ADR-0001](../adr/0001-submodule-manifest-overlay-architecture.md)
explains the source/overlay/generated boundary, [ADR-0002](../adr/0002-multi-harness-output.md) explains
separate harness-native output, [ADR-0005](../adr/0005-invocation-intent-in-the-manifest.md) explains
the neutral invocation dial, and [ADR-0006](../adr/0006-output-is-a-transformation.md) with
[ADR-0007](../adr/0007-control-beats-fidelity.md) explains why the result is judged by curated intent
rather than upstream fidelity.

This document states the decided OpenCode 2 emission. Where the implementation has not caught up,
the gap and its responsible files are tracked in [`docs/ROADMAP.md`](../ROADMAP.md#known-gaps).

## The three phases

The product crosses three distinct phases:

1. **Compile-time transformation.** `external/`, `curation/`, `overlays/`, and any original
   `skills/` are resolved into committed Claude Plugin, OpenCode Bundle, and Codex Plugin trees.
   Invocation, shape, frontmatter, body ownership, reference spelling, and target fit are settled
   here.
2. **Install-time composition.** The OpenCode installer verifies ready-made Bundles and composes the
   selected files into the global Native tree without parsing or adapting their content. Claude Code
   installs the independently emitted Plugins through its marketplace, and Codex installs its own
   Plugins through `.agents/plugins/marketplace.json`. OpenCode composition is
   owned by [Distribution and installation](distribution-and-installation.md).
3. **Skill runtime.** A harness discovers or invokes the installed artifact, and its shipped
   instructions run. Work a skill performs in the consumer repository, including its own setup
   wizard or file conventions, belongs to runtime; the compiler does not pre-execute or redesign it.

The repository therefore owns both compilation and its shipped OpenCode installer, but neither
phase reinterprets a skill after emission.

## Authored and generated boundaries

- Upstream worktrees under `external/` are scanned but never authored into. Curation manifests,
  `curation/attribution.json`, overlays and their lock, original skills, and the root license and
  notice are the authored transformation layer.
- `plugins/`, `opencode/`, `codex/`, `dist/`, `.claude-plugin/marketplace.json`,
  `.agents/plugins/marketplace.json`, `docs/inventory.md`, and `docs/ledger.json` are generated and
  committed. They are review surfaces and consumable output, not edit surfaces.
- One curation manifest produces one same-named Claude Plugin, OpenCode Module, and Codex Plugin.
  Each marketplace points at its native Plugin tree; the Bundle keeps the Module's `skills/`,
  `commands/`, and `agents/` paths separate until installation.
- A source skill is parsed and serialized even when no body override is present. The compiler does
  not promise byte identity with upstream; the Bundle's post-emission bytes establish the later
  installation identity.

## Resolution and body assembly

Name resolution follows `item.name` -> scanned component name -> source basename; kind resolution
follows `item.as` -> scanned component type -> `skill`. **The scanned source type is therefore the
default when `as:` is absent, never a binding authority.** An explicit shape can replace it. The
current resolver is [`resolveItem`](../../tools/lib/resolve.ts), while the scanner's
source-kind classification is [`scanSubmodule`](../../tools/lib/scan.ts#L69-L106).

A submodule whose only skill lives at its repository root uses the submodule name as its source
address and namespace fallback; its `SKILL.md` remains the component document. Upstream `.git`
metadata is never copied into output, including the machine-path gitdir file a submodule root
contains. Item-level `omit` continues to own runtime files such as installation-only READMEs.

Current implementation support is narrower than the design dial: skill-to-command and
skill-to-agent conversions work, but command-to-skill and agent-to-skill conversions stop in
preflight. That is a current compiler limit, not a rule that upstream kind should govern curation.

Identity preflight runs before generated output is deleted. It rejects duplicate `plugin.name`
values, a name that breaks the portable-name rule, duplicate kind/name identities within one
manifest, same-plugin collisions in Codex's flattened skill namespace, and a bare output name
claimed twice anywhere in the repository, whatever its kind and including names claimed by
original skills. The naming and uniqueness rules themselves, and why bare names stay unique, are
owned by [`curation/SCHEMA.md`](../../curation/SCHEMA.md). Every OpenCode ID carries its Module, so
OpenCode destinations cannot collide across Modules; the repository-wide bare-name check is what
protects the bare-name keys of `depends_on`, the linker, and `requiredModules` derivation. The checks
are [`collectIdentityProblems`](../../tools/lib/resolve.ts), reached through `collectProblems`, and
[`collectCodexEmissionProblems`](../../tools/lib/codex-plugin.ts), both called by
[`buildAll`](../../tools/build.ts) ahead of the delete. Validation separately reports when an
original skill would be copied last and silently overwrite a curated skill of the same name in
emitted output (check L7 in [`validateRepo`](../../tools/validate.ts)).

The compiler assembles a body in this order:

1. Load every manifest and scan every initialized upstream before touching generated trees.
2. Resolve all identities, sources, overlay locks, merge-source stamps, patch applicability, omitted
   patch targets, and unsupported conversions. Problems are aggregated before the previous Plugin
   and Bundle trees are deleted.
3. Copy the source while omitting declared paths and skipping symlinks; prune directories emptied by
   omission.
4. Apply the shared full-file overlay or skill patch, then merge frontmatter and force the resolved
   output identity last ([`normalizePrimary`](../../tools/lib/assemble.ts)).
5. Add original skills to the same neutral assembly, then let each emitter write its own artifact
   tree, metadata, and marketplace.

Each non-excluded primary or merge source must have an entry in `curation/attribution.json`. After
body assembly, the build copies the repository `LICENSE`, writes a source-specific
`THIRD_PARTY_NOTICES.md`, and copies each used upstream license byte-for-byte under
`third_party/<source>/LICENSE` in the Claude Plugin, OpenCode Bundle, and Codex Plugin. Bundle
manifests hash these distribution files with the rest of the final Bundle.

The fail-before-delete and emit order are explicit in [`buildAll`](../../tools/build.ts), with
per-item assembly in [`assembleItems`](../../tools/lib/assemble.ts). Overlay hashes guard every upstream-backed file the
owned body uses, including declared merge inputs; additions with no upstream counterpart are not
pretended to have an upstream stamp. This is review ownership, not a content dependency lock.

There is one neutral assembled item for all three targets. It retains the full selected dependency
closure even when the resolved Claude/OpenCode kind is a single-file command or agent. `body: patch`,
`body: overlay`, `omit`, and `merged_from` all act once before harness emission. The internal
temporary assembly is pipeline state, not a fourth output format, and no finalized target tree is
another target's source. The repository does **not** currently express a per-target body overlay.
When target fit requires irreconcilable prose, that is a named capability gap rather than permission
to hand-edit one generated tree.

## Harness emission

The three emitters consume the same pre-localization assembly and make target decisions
independently. They filter frontmatter, choose native artifact shape and invocation policy, and
rewrite their own copies. Final Claude, OpenCode, or Codex output is never mirrored into another
target. See [`buildAll`](../../tools/build.ts) and [`assembleItems`](../../tools/lib/assemble.ts).

### Claude Code

A resolved skill remains one Plugin skill. If invocation is absent, upstream Claude invocation
frontmatter passes through. A stated invocation replaces both Claude invocation keys: `auto` writes
`user-invocable: false`, `manual` writes `disable-model-invocation: true`, and `both` writes neither.
Commands and agents use their native Plugin paths. Invocation on those resolved shapes does not
alter Claude output, although Codex consumes the same intent after adapting them to skills.

The compiler forces a skill's frontmatter name to its output directory name and an agent's name to
its output file identity. This keeps generated identity, localization, and review state aligned.

### OpenCode

The OpenCode emitter writes OpenCode 2 output. The shapes below need OpenCode 2 at v2.0.4 or later:
that release is the first in which skills are no longer slash commands, and the hiding key used for
`manual` works from v2.0.0. The runtime target, the measured version, and the unsupported OpenCode 1
line are owned by
[Distribution and installation](distribution-and-installation.md#target-opencode-runtime).

**Identity and paths.** Every OpenCode ID is `<plugin>.<name>`; the naming rule is owned by
[`curation/SCHEMA.md`](../../curation/SCHEMA.md). The Bundle places each artifact under that ID:

| Resolved shape | Bundle path | User invocation |
|---|---|---|
| skill | `skills/<plugin>.<name>/SKILL.md`, with the item's surviving files below that folder | `@<plugin>.<name>`, or the `/skills` dialog |
| command | `commands/<plugin>.<name>.md` | `/<plugin>.<name>` |
| agent | `agents/<plugin>.<name>.md` | `@<plugin>.<name>` |

OpenCode 2 derives a skill ID only from the skill's leaf folder name, so the namespace must sit in
that folder name; a nested `skills/<plugin>/<name>/SKILL.md` would register the bare `<name>`
(`packages/core/src/config/plugin/skill-file.ts:45-48` at `anomalyco/opencode@0fd7e28`). A command
or agent name comes from its file path. A skill's frontmatter `name` is forced to the same
namespaced ID: OpenCode treats `name` as a display label and shows the model both the ID and the
label, so a bare label would lead the model to call an ID that does not exist. The rendered
reference spelling, including the rewrite of relative sibling-item paths to these folder names, is
owned by [References and linking](references-and-linking.md#localization).

**Invocation selects frontmatter, never shape.** An item whose resolved shape is a skill emits
exactly one skill folder, whatever its invocation, and nothing under `commands/`:

- `auto` emits a skill that is advertised to the model. OpenCode 2 has no switch that makes a skill
  model-only, so the user can also attach it with `@<plugin>.<name>`; that explicit path is a native
  capability `auto` leaves unspecified, as in Codex.
- `manual` emits the same skill with `metadata: {"opencode/autoinvoke": false}` merged into any
  metadata the document already carries. As in Codex, this means **not offered to the model**: the
  model is not offered the skill, the user attaches it with `@<plugin>.<name>` or the `/skills`
  dialog, and the skill tool can still load the registered ID if the model learns it. The accepted
  rationale is in [ADR-0005](../adr/0005-invocation-intent-in-the-manifest.md).
- `both` emits one plain skill, advertised and attachable, with no duplicate command.
- Absent invocation passes upstream posture through. An upstream `disable-model-invocation: true`
  is rendered with the same `opencode/autoinvoke: false` metadata key, not passed through, because
  OpenCode 2 honors `disable-model-invocation` only from v2.0.23 and the metadata key keeps the
  v2.0.4 floor. An upstream key with no OpenCode equivalent, such as `user-invocable`, is dropped and
  reported.

A stated `auto` or `both` replaces upstream posture and writes no hiding key. An OpenCode skill
keeps only `name`, `description`, and `metadata`, with any hiding key
merged into `metadata` (`OPENCODE_SKILL_KEYS` in
[`tools/lib/opencode-target.ts`](../../tools/lib/opencode-target.ts));
every other key, including `license` and `compatibility`, which the OpenCode 2 skill parser does not
read, is reported by the build rather than silently carried into a target that ignores it
([`adaptOpenCodeSkillDocument`](../../tools/lib/opencode-target.ts), called by
[`emitOpenCode`](../../tools/build.ts)).

**Commands and agents come only from `as:`.** An explicit `as: command` is the per-item escape hatch
for an item that needs `/name` and `$ARGUMENTS`; it emits `commands/<plugin>.<name>.md`, which keeps
only `description`. `as: agent` emits `agents/<plugin>.<name>.md` with only the native OpenCode 2
keys the emitter writes, `description` and `mode: subagent`. An agent never receives `name`:
OpenCode 2 sends any agent file with a non-native key, `name` included, through its OpenCode 1
compatibility migrator, and it drops an agent with an invalid native value, such as a `color` that
is not `#rrggbb`, without a warning. Every dropped key other than the forced `name` is reported
([`emitOpenCode`](../../tools/build.ts)). Invocation on a resolved command or agent does not alter
OpenCode output.

**Shape checks.** `validate` rejects three Bundle shapes OpenCode 2 would misread:

- a phantom skill: a `.md` file directly under `skills/`, or a `SKILL.md` anywhere below
  `skills/<id>/` other than that folder's own. OpenCode 2 scans skill roots with
  `{*.md,**/SKILL.md}` and registers every match as a skill
  (`packages/core/src/config/plugin/skill.ts:121` at `anomalyco/opencode@0fd7e28`);
- a skill whose folder is not an ID `<plugin>.<name>` of its own Module, or whose `SKILL.md`
  frontmatter `name` differs from that folder ID, which would break the `name` = ID rule above;
- an agent file whose frontmatter holds a key that is not native to OpenCode 2, or a `color` that is
  not `#rrggbb`.

### Codex

Every resolved item becomes one native Codex skill under `codex/<plugin>/skills/<name>/`, including
resolved commands and agents. The common assembly keeps their selected dependency files, while
Codex frontmatter is filtered to `name`, `description`, `license`, `allowed-tools`, and `metadata`;
all other keys are reported as drops. Final reference localization can lengthen a description, so
the Codex emitter applies the native 1,024-character limit after localization, reports any
truncation, and records it in the ledger's `metadataTransformations`. No compatibility `commands/`
or `agents/` artifact directory is emitted and no custom-agent TOML is synthesized.

An ordinary Codex skill is implicitly eligible and explicitly addressable. `manual` writes
`agents/openai.yaml` with `policy.allow_implicit_invocation: false`; `auto` and `both` write no
disabling policy. Absent invocation uses the Codex target default. Codex does not expose an
implicit-only skill policy, so `auto` retains native explicit invocation without being mislabeled
model-only.

Each Codex Plugin has `.codex-plugin/plugin.json`, repository and source-specific distribution
metadata, and one entry in `.agents/plugins/marketplace.json`. The repository marketplace is the
native transport for Codex CLI and Codex in the ChatGPT desktop app. IDE Plugin loading and
`.codex/agents/*.toml` distribution are outside this output.

## Finalization and handoff

References, including OpenCode's relative sibling-item paths, are localized only after all three
artifact trees exist, independently for each address space.
Module manifests are then written over final OpenCode bytes. Compile-time `requiredModules` are
derived from declared `depends_on` edges
([`deriveModuleRequirements`](../../tools/lib/resolve.ts)) and recorded by
[`writeOpenCodeManifests`](../../tools/build.ts). The ledger is written last from the
resolved output. The installer reads those emitted lists; it does not re-derive them. The separate
reference contract is
[References and linking](references-and-linking.md).

The [`npm run build` script](../../package.json#L32) subsequently compiles the installer runtime
to committed `dist/` JavaScript using
[`tsconfig.installer.json`](../../tsconfig.installer.json#L4-L18), then formats only that emitted
`dist/` JavaScript. Consumers never run the curation compiler or compile installer TypeScript.

## Current limits

- Per-harness body ownership is absent; one overlay or patch feeds all three emitters. The follow-up
  [Codex estate audit](../research/codex-generated-estate-audit.md) classified slash-shaped text and
  promoted actual skill pointers to namespaced authored facts in that common layer. Each emitter
  localizes those facts, so no Codex-only body-patch seam is currently justified.
- A source command or agent still cannot be resolved as a skill through `as:`. Codex's emitter-level
  adaptation of already resolved commands and agents is supported and retains their closure.
  Non-empty `hooks.include` remains rejected.
- An item resolved `as: command` is a single file in Claude Code and OpenCode. Skill-relative paths
  in its body can cease to resolve from that command location; whether the command surface is worth
  that cost is the per-item `as:` decision. The path check that covers this case is described in
  [References and linking](references-and-linking.md#paths).
- On OpenCode, `manual` hides a skill from the model's list but cannot stop the skill tool from
  loading a registered ID that text names
  ([ADR-0005](../adr/0005-invocation-intent-in-the-manifest.md)).
- Invocation absence deliberately preserves upstream Claude posture, so upstream posture changes can
  flow into output. OpenCode renders an upstream `disable-model-invocation: true` through its native
  hiding key, while an upstream `user-invocable: false` has no OpenCode equivalent and is dropped and
  reported; Codex emits a skill with its target-default policy and does not translate upstream
  Claude flags.
- OpenCode 2's TUI fuzzy ranking lists a namespaced `@<plugin>.<name>` below a bare third-party skill
  of the same `<name>`; typing the Module prefix selects this repository's artifact.
- OpenCode 2 loads `AGENTS.md` and never `CLAUDE.md`. A shipped instruction that writes or relies on
  `CLAUDE.md` does not reach OpenCode; that is a per-item curation question, not an emitter
  transformation.
- The scanner discovers command and agent files only directly under `commands/` and `agents/`;
  grouped subdirectories are missed, while a `commands/` or `agents/` directory nested under a skill
  can be double-counted as a standalone component ([`scanSubmodule`](../../tools/lib/scan.ts#L69-L106)).
- Overlay locks hash file content, not executable mode, so a mode-only upstream change does not force
  a re-bless ([`blobSha`](../../tools/lib/overlay.ts#L44-L46),
  [`stampFiles`](../../tools/lib/overlay.ts#L155-L164)).
- Patch application cannot touch a path at or beyond a symlink: `git apply` rejects those paths while
  emitted copies skip symlinks ([`gitApply`](../../tools/lib/overlay.ts#L110-L140),
  [`skipSymlinks`](../../tools/lib/assemble.ts)).
- Manifest `frontmatter:` overrides have no upstream-staleness guard. They merge into the assembled
  document after body assembly, so an upstream rewrite does not make an old override drift
  ([`normalizePrimary`](../../tools/lib/assemble.ts)). `npm run sync` reports an override whose
  item's `SKILL.md` moved, which is a prompt to reread the body — not a stamp, and nothing stops the
  build.
- Ledger projection semantics and limits are owned by
  [References and linking](references-and-linking.md#ledger-semantics).
- A build report proves what was emitted or dropped. It does not prove that a harness will select a
  skill or that the skill's runtime instructions will be followed.
