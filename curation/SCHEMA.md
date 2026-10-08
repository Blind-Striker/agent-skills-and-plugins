# Curation manifest authoring

Authoring guide — behaviour authority is `tools/`.

`curation/<plugin>.yaml` records what enters a plugin and the intent of each transformation. Before
deciding an item, run `npm run inventory` and use `docs/inventory.md`; every take, rejection, rename,
or modification needs a why-comment beside that item. Keep deliberate rejections as
`exclude: true` rather than deleting them from the manifest.

Each manifest has plugin metadata and an item list:

```yaml
plugin:
  name: <output-plugin-name>
  description: <marketplace-description>
  version: <plugin-version>
items:
  - source: <submodule>/<component-path>
```

## Source attribution

`curation/attribution.json` records the public name, repository, license identifier, copyright line,
and root license filename for every submodule used by a non-excluded primary item or `merged_from`
source. The build rejects a used source with no entry or missing upstream license file. It emits only
the sources each Module actually uses, copies their license bytes directly from `external/`, and
keeps excluded-only sources out of that Module's notices.

The repository's root `LICENSE` covers original work and modifications. It does not replace upstream
terms. Root `THIRD_PARTY_NOTICES.md` credits the complete source estate; generated Claude Plugin,
OpenCode Bundle, and Codex Plugin notice packs are source-specific distribution output and must
never be edited directly.

## Choose the lowest-cost mechanism

Several fields can express similar-looking changes. Use the lowest rung that states the intent;
body ownership adds review cost whenever upstream moves.

| Intent | Field or mechanism | Author-facing cost |
|---|---|---|
| Do not take the item | `exclude: true` | Keep the reason beside it; no output is emitted |
| Take it without some files | `omit:` | Patterns can go stale; `validate` warns when one matches nothing |
| Change metadata | `frontmatter:` | No upstream-staleness guard |
| Change the output name | item-level `name:` | References and output identity use the new name |
| Change who triggers it | `invocation:` | Harness-neutral intent; each emitter chooses its mechanism |
| Change the artifact shape | `as:` | Independent of who triggers it |
| Edit part of a skill body | `body: patch` | Touched upstream files are hash-stamped; drift blocks the build |
| Own replacement files | `body: overlay` | Replaced upstream files are hash-stamped; later upstream improvements do not flow into owned files |

Use item-level `name:`, not `frontmatter.name`; the build forces output identity after frontmatter
overrides (in OpenCode, a skill's `name` is its namespaced ID). Use `body:` only with an overlay
directory created by `npm run eject -- <plugin> <item>` (`--patch` for a patch). A patch applies
only to skill-shaped output; a command or agent conversion needs a full-file overlay. For a
conversion overlay, keep the upstream body filename (`SKILL.md` for a source skill, or the source
command/agent filename); the build reads that one file.

Every manifest must have a repository-unique `plugin.name`. Plugin names and output names follow one
portable-name rule for every harness: lowercase letters and digits in single-hyphen-separated runs
(`^[a-z0-9]+(-[a-z0-9]+)*$`), so no `.`, `_`, `:`, or doubled hyphen. Within a manifest, each
non-excluded item must have a unique artifact-kind/output-name pair. Codex emits every resolved kind
into one plugin-local skill namespace, so output names must also be unique across kinds within a
manifest; `skill:review` and `command:review` would collide as Codex skills and fail preflight.
Across manifests, a bare output name must be unique in the whole repository, whatever its kind:
`depends_on`, the linker, and `requiredModules` derivation identify a target by its bare name.

Each harness qualifies the bare name with its plugin. Claude Code and Codex address an item through
their plugin namespace. OpenCode has no plugin concept, so every OpenCode ID is `<plugin>.<name>`:
`skills/deniz-process.brainstorming/SKILL.md` is attached as `@deniz-process.brainstorming`, and a
command `commands/deniz-process.handoff.md` runs as `/deniz-process.handoff`. The portable-name rule
keeps that dotted ID one-to-one with its plugin and name. The emitted paths are owned by
[Transformation and emission](../docs/architecture/transformation-and-emission.md); reference
rendering is owned by [References and linking](../docs/architecture/references-and-linking.md).

Curating the same upstream source into more than one item is legal but ambiguous for references:
the rewrite map is keyed by upstream address and the last manifest item wins. `validate` warns so
the duplicate can be confirmed as intentional or removed.

## `omit`

`omit` is a list of item-relative, POSIX-spelled glob patterns:

```yaml
omit:
  - CREATION-LOG.md
  - "tests/**"
```

The build filters the upstream copy before applying an overlay or patch and prunes empty
directories. An `omit` pattern that removes a patch target is contradictory and stops the build.
Use `omit` for authoring residue, fixtures, or other files the shipped item does not need; do not
take ownership of the whole body merely to remove a file.

## `invocation` and `as` are orthogonal

`invocation` says which initiation capabilities an item requires or forbids. `as` says its resolved
Claude/OpenCode artifact shape. One does not derive the other; Codex adapts every resolved kind to a
skill and therefore still consumes invocation on an item resolved as a command or agent.

| `invocation` | Capability intent | Claude Code skill output | OpenCode 2 skill output | Codex output |
|---|---|---|---|---|
| absent | passthrough or target default | preserve upstream posture | skill; an upstream `disable-model-invocation: true` becomes `metadata: {"opencode/autoinvoke": false}` | skill with target defaults |
| `auto` | implicit required; explicit unspecified | model-only skill | advertised skill; explicit `@` attach remains natively available | ordinary skill; explicit remains natively available |
| `manual` | implicit forbidden; explicit required | user-only skill | unadvertised skill with `metadata: {"opencode/autoinvoke": false}`; the user attaches it with `@` | skill with `allow_implicit_invocation: false` |
| `both` | implicit and explicit required | skill available to both audiences | one advertised skill the user can attach with `@`; no command | ordinary skill |

Absent is not a default value: it records no curation intent, so upstream Claude frontmatter passes
through, OpenCode renders an upstream `disable-model-invocation: true` with its native key, and Codex
uses its native target default. Neither Codex nor OpenCode 2 has a policy that preserves implicit
selection while forbidding explicit invocation, so `auto` does not mean model-only there; this is a
native capability resolution, not a silent approximation.

OpenCode 2 has no skill-level switch that forbids a model-initiated load: its native key only keeps
the skill out of the list the model is offered, and the skill tool can still load a registered ID.
There, `manual` therefore means unadvertised, not forbidden
([ADR-0005](../docs/adr/0005-invocation-intent-in-the-manifest.md)). The compensating authoring rule
is that model-reachable text must not name a `manual` item's OpenCode ID; `validate` enforces the
[manual-ID leak rule](../docs/architecture/references-and-linking.md#opencode-id-checks) owned by
References and linking.

Invocation never changes artifact shape. Use `as: command` or `as: agent` when the Claude/OpenCode
artifact itself must change regardless of trigger intent. `as: command` is the per-item escape hatch
for an item that truly needs a command surface, a `/<plugin>.<name>` entry in OpenCode that takes
`$ARGUMENTS`; its body then lives in one command file, so skill-relative paths in it may stop
resolving.

## Body ownership and merge sources

Both body modes are blessed in `overlays/overlays.lock.json`. The build checks the stamped upstream
files and stops on drift until the edit is reviewed and re-blessed. A body assembled from additional
upstream items declares every source with `merged_from:` and is invalid without `body:`.

```yaml
body: overlay
merged_from:
  - upstream/skills/a
  - source: upstream/skills/b
    files: [SKILL.md, tests.md, mocking.md]
```

A bare address uses the **same-filename rule**: the files replaced by the overlay, or touched by the
patch, are looked up under the same names in that source. Use the object form when the merge drew
from differently named files. Its `files:` list replaces the same-filename rule for that source and
must name the actual inputs; a missing declared file warns because its stamp guards nothing.

Re-blessing stamps the primary and all declared merge sources together. Before checking content
hashes, the build requires the primary lock keys to exactly match the upstream-backed overlay files
or patch targets that `eject --bless` would stamp. Overlay-only additions and pure-add patch targets
have no upstream counterpart, so they remain outside that set.

## Dependencies

`depends_on:` lists output names reached by model-edge facts in the shipped body. Author those facts
in neutral upstream spelling (`namespace:name`); use `/namespace:name` when the body points the human
at a user surface. Each emitter renders the fact in its own harness's spelling.

A body line that tells the model to load another skill by a quoted handle, such as
`Skill tool with "grilling"`, is a load-bearing model edge. Author that handle as a namespaced fact
with a matching `depends_on` entry; a bare handle names an ID that does not exist in OpenCode, where
every ID is qualified. Current per-harness spelling, localization, linking, reachability, path,
candidate, and ledger mechanics, including the OpenCode ID checks, live in
[References and linking](../docs/architecture/references-and-linking.md). The reason for the symbol
tiers and two-way declaration trade-off is [ADR-0008](../docs/adr/0008-references-are-symbols.md).
