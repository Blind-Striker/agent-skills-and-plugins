# Transformation and emission

Date: 2026-08-24

## Responsibility

This document owns the current compile-time mechanics: how authored inputs become separate Claude
Code and OpenCode artifacts, where body ownership sits, and where compilation hands off to
installation and runtime. Distribution terms retain the precise meanings in
[`CONTEXT.md`](../../CONTEXT.md); manifest grammar and authoring choices remain in
[`curation/SCHEMA.md`](../../curation/SCHEMA.md).

The rationale is split across the ADRs: [ADR-0001](../adr/0001-submodule-manifest-overlay-architecture.md)
explains the source/overlay/generated boundary, [ADR-0002](../adr/0002-multi-harness-output.md) explains
separate harness-native output, [ADR-0005](../adr/0005-invocation-intent-in-the-manifest.md) explains
the neutral invocation dial, and [ADR-0006](../adr/0006-output-is-a-transformation.md) with
[ADR-0007](../adr/0007-control-beats-fidelity.md) explains why the result is judged by curated intent
rather than upstream fidelity.

## The three phases

The product crosses three distinct phases:

1. **Compile-time transformation.** `external/`, `curation/`, `overlays/`, and any original
   `skills/` are resolved into committed Plugin and Bundle trees. Invocation, shape, frontmatter,
   body ownership, reference spelling, and target fit are settled here.
2. **Install-time composition.** The OpenCode installer verifies ready-made Bundles and composes the
   selected files into the global Native tree without parsing or adapting their content. Claude Code
   installs the independently emitted Plugins through its marketplace. OpenCode composition is
   owned by [Distribution and installation](distribution-and-installation.md).
3. **Skill runtime.** A harness discovers or invokes the installed artifact, and its shipped
   instructions run. Work a skill performs in the consumer repository, including its own setup
   wizard or file conventions, belongs to runtime; the compiler does not pre-execute or redesign it.

The repository therefore owns both compilation and its shipped OpenCode installer, but neither
phase reinterprets a skill after emission.

## Authored and generated boundaries

- Upstream worktrees under `external/` are scanned but never authored into. Curation manifests,
  overlays and their lock, and original skills are the authored transformation layer.
- `plugins/`, `opencode/`, `dist/`, `.claude-plugin/marketplace.json`, `docs/inventory.md`, and
  `docs/ledger.json` are generated and committed. They are review surfaces and consumable output,
  not edit surfaces.
- One curation manifest produces one same-named Plugin and Module. The marketplace points at the
  Plugin; the Bundle keeps the Module's `skills/`, `commands/`, and `agents/` paths separate until
  installation.
- A source skill is parsed and serialized even when no body override is present. The compiler does
  not promise byte identity with upstream; the Bundle's post-emission bytes establish the later
  installation identity.

## Resolution and body assembly

Name resolution follows `item.name` -> scanned component name -> source basename; kind resolution
follows `item.as` -> scanned component type -> `skill`. **The scanned source type is therefore the
default when `as:` is absent, never a binding authority.** An explicit shape can replace it. The
current resolver is [`tools/lib/resolve.ts`](../../tools/lib/resolve.ts#L25-L42), while the scanner's
source-kind classification is [`tools/lib/scan.ts`](../../tools/lib/scan.ts#L69-L104).

A submodule whose only skill lives at its repository root uses the submodule name as its source
address and namespace fallback; its `SKILL.md` remains the component document. Upstream `.git`
metadata is never copied into output, including the machine-path gitdir file a submodule root
contains. Item-level `omit` continues to own runtime files such as installation-only READMEs.

Current implementation support is narrower than the design dial: skill-to-command and
skill-to-agent conversions work, but command-to-skill and agent-to-skill conversions stop in
preflight. That is a current compiler limit, not a rule that upstream kind should govern curation.

Identity preflight rejects duplicate `plugin.name` values, duplicate kind/name identities within one
manifest, and cross-Module OpenCode destination collisions before generated output is deleted. The
checks include OpenCode destinations claimed by original skills
([`collectIdentityProblems`](../../tools/lib/resolve.ts#L45-L118), pre-delete call in
[`buildAll`](../../tools/build.ts#L53-L64)). Validation separately reports when an original skill
would be copied last and silently overwrite a curated skill of the same name in emitted output
([`tools/validate.ts`](../../tools/validate.ts#L213-L221)).

The compiler assembles a body in this order:

1. Load every manifest and scan every initialized upstream before touching generated trees.
2. Resolve all identities, sources, overlay locks, merge-source stamps, patch applicability, omitted
   patch targets, and unsupported conversions. Problems are aggregated before the previous Plugin
   and Bundle trees are deleted.
3. Copy the source while omitting declared paths and skipping symlinks; prune directories emptied by
   omission.
4. Apply the shared full-file overlay or skill patch, then merge frontmatter and force the resolved
   output identity last.
5. Copy original skills after curated manifest items, then write Plugin metadata and the marketplace.

The fail-before-delete and emit order are explicit in
[`tools/build.ts`](../../tools/build.ts#L37-L97), with per-item assembly in
[`emitItem`](../../tools/build.ts#L327-L407). Overlay hashes guard every upstream-backed file the
owned body uses, including declared merge inputs; additions with no upstream counterpart are not
pretended to have an upstream stamp. This is review ownership, not a content dependency lock.

There is one assembled body for both targets. `body: patch`, `body: overlay`, `omit`, and
`merged_from` all act before harness emission. The repository does **not** currently express a
Claude-only or OpenCode-only body overlay. When target fit requires irreconcilable prose, that is a
named capability gap rather than permission to hand-edit one generated tree.

## Harness emission

The two emitters make target decisions independently. The implementation reuses the pre-reference-
rewrite Plugin staging tree as the common assembled input for OpenCode, but it emits OpenCode before
Claude localization and then filters and rewrites each tree separately. Final Claude output is not
mirrored into OpenCode. See the ordering comment and calls in
[`buildAll`](../../tools/build.ts#L75-L96).

### Claude Code

A resolved skill remains one Plugin skill. If invocation is absent, upstream Claude invocation
frontmatter passes through. A stated invocation replaces both Claude invocation keys: `auto` writes
`user-invocable: false`, `manual` writes `disable-model-invocation: true`, and `both` writes neither.
Commands and agents use their native Plugin paths; invocation on those shapes has no emitted meaning
and validation warns.

The compiler forces a skill's frontmatter name to its output directory name and an agent's name to
its output file identity. This keeps generated identity, localization, and review state aligned.

### OpenCode

OpenCode skills keep only its recognized skill frontmatter. Commands keep their description; agents
keep their description and receive `mode: subagent`. Every dropped frontmatter key is reported by
the build rather than silently carried into a target that ignores it
([`emitOpenCode`](../../tools/build.ts#L541-L583)).

For items whose resolved shape is a skill, invocation selects OpenCode artifacts:

- absent or `auto` emits a skill;
- `manual` emits a command and withholds `SKILL.md`;
- `both` emits both a skill and a command.

A bundled `manual` item parks the parsed body as `skills/<name>/BODY.md` beside its surviving assets;
the directory has no `SKILL.md` and is not a discoverable skill. Its command is a short,
**global-only** stub: it resolves `$XDG_CONFIG_HOME/opencode`, with the normal `~/.config/opencode`
fallback, then reads the parked body and forwards `$ARGUMENTS`. It does not name or support a
project-local `.opencode` path. If no bundled file survives besides `SKILL.md`, no park is emitted
and the command contains the body directly. `both` likewise keeps the command body inline rather
than creating `BODY.md` ([`emitOpenCodeSkill`](../../tools/build.ts#L463-L539), focused assertion in
[`tools/build.test.ts`](../../tools/build.test.ts#L258-L300)).

## Finalization and handoff

References are localized only after both artifact trees exist, independently for each address space.
Module manifests are then written over final OpenCode bytes, and the ledger is written last from the
resolved output. The separate reference contract is
[References and linking](references-and-linking.md).

The [`npm run build` script](../../package.json#L17) subsequently compiles the installer runtime
to committed `dist/` JavaScript using
[`tsconfig.installer.json`](../../tsconfig.installer.json#L4-L18), then formats only that emitted
`dist/` JavaScript. Consumers never run the curation compiler or compile installer TypeScript.

## Current limits

- Per-harness body ownership is absent; one overlay or patch feeds both emitters.
- Command/agent-to-skill conversion and non-empty `hooks.include` are rejected by the current build.
- Bundle-less manual commands and `both` commands still carry an inline body. Skill-relative paths
  can cease to resolve from that command location; the linker reports the cases it can attribute to
  conversion rather than claiming the shape is universally portable.
- Invocation absence deliberately preserves upstream Claude posture, so upstream posture changes can
  flow into output. The OpenCode side still resolves absence to a skill because OpenCode has no
  equivalent upstream posture to preserve.
- The scanner discovers command and agent files only directly under `commands/` and `agents/`;
  grouped subdirectories are missed, while a `commands/` or `agents/` directory nested under a skill
  can be double-counted as a standalone component ([`scanSubmodule`](../../tools/lib/scan.ts#L69-L105)).
- Overlay locks hash file content, not executable mode, so a mode-only upstream change does not force
  a re-bless ([`blobSha`](../../tools/lib/overlay.ts#L38-L46),
  [`stampFiles`](../../tools/lib/overlay.ts#L155-L164)).
- Patch application cannot touch a path at or beyond a symlink: `git apply` rejects those paths while
  emitted copies skip symlinks ([`gitApply`](../../tools/lib/overlay.ts#L100-L149),
  [`skipSymlinks`](../../tools/build.ts#L313-L323)).
- Manifest `frontmatter:` overrides have no upstream-staleness guard. They merge into the emitted
  document after body assembly, so an upstream rewrite does not make an old override drift
  ([`emitItem`](../../tools/build.ts#L371-L383)). `npm run sync` reports an override whose item's
  `SKILL.md` moved, which is a prompt to reread the body — not a stamp, and nothing stops the build.
- Ledger projection semantics and limits are owned by
  [References and linking](references-and-linking.md#ledger-semantics).
- A build report proves what was emitted or dropped. It does not prove that a harness will select a
  skill or that the skill's runtime instructions will be followed.
