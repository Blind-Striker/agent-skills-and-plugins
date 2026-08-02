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
overrides. Use `body:` only with an overlay directory created by `npm run eject -- <plugin> <item>`
(`--patch` for a patch). A patch applies only to skill-shaped output; a command or agent conversion
needs a full-file overlay. For a conversion overlay, keep the upstream body filename (`SKILL.md`
for a source skill, or the source command/agent filename); the build reads that one file.

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

`invocation` says who pulls the trigger for an item emitted as a skill. `as` says what artifact the
item becomes. One does not derive the other.

| `invocation` | Claude Code output | OpenCode output |
|---|---|---|
| absent | preserve upstream skill posture | skill |
| `auto` | model-only skill | skill |
| `manual` | user-only skill | command |
| `both` | skill available to both audiences | skill and command |

Absent is not a default value: it records no curation intent, so upstream Claude frontmatter passes
through. Set `invocation` only on skill output; `validate` warns when it is combined with a command
or agent shape. Use `as: command` or `as: agent` when the artifact itself must change regardless of
trigger intent.

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

Re-blessing stamps the primary and all declared merge sources together. The current build checks
the paths already present in the lock but does not reconcile a newly changed overlay/patch target
set against it; that enforcement gap is tracked in `docs/ROADMAP.md`.

## Dependencies

`depends_on:` lists output names reached by model-edge facts in the shipped body. Author those facts
in neutral upstream spelling (`namespace:name`); use `/namespace:name` when the body points the human
at a user surface. `validate` requires model-edge declarations in both directions. The complete
symbol and reachability decision is [ADR-0008](../docs/adr/0008-references-are-symbols.md).
