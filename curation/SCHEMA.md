# Curation manifest authoring

Authoring guide — behaviour authority is `tools/`.

`curation/<plugin>.yaml` records what enters a plugin and the intent of each transformation. Before
deciding an item, run `npm run inventory` and use `docs/inventory.md`; every take, rejection, rename,
or modification needs a why-comment beside that item. Keep deliberate rejections as
`exclude: true` rather than deleting them from the manifest. An exclude records a source the current
pin has; a source deleted upstream leaves the manifest instead, with its reason in the commit message
and, when one exists, in its successor item's comment
([Upstream sync flow](../docs/engineering/workflow.md#upstream-sync-flow)).

Each manifest has plugin metadata, optional [`original_skills`](#original_skills) declarations, and
an item list:

```yaml
plugin:
  name: <output-plugin-name>
  description: <marketplace-description>
  version: <plugin-version>
original_skills:
  <skill-directory-name>:
    invocation: auto|manual|both
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
| Change metadata | `frontmatter:` | On an item with no `body:`, the upstream document it rewrites is hash-stamped; drift blocks the build |
| Change the output name | item-level `name:` | References and output identity use the new name |
| Change who triggers it | `invocation:` | Harness-neutral intent; each emitter chooses its mechanism |
| Ship a different file per harness at one path | `harness_files:` | Each tree gets only its own harness's source; the sources ship under no name of their own |
| Change the artifact shape | `as:` | Independent of who triggers it |
| Edit part of a skill body | `body: patch` | Touched upstream files are hash-stamped; drift blocks the build |
| Own replacement files | `body: overlay` | Replaced upstream files are hash-stamped; later upstream improvements do not flow into owned files |

Use item-level `name:`, not `frontmatter.name`; the build forces output identity after frontmatter
overrides (in OpenCode, a skill's `name` is its namespaced ID). Use `body:` only with an overlay
directory created by `npm run eject -- <plugin> <item>` (`--patch` for a patch; `--patch --rebase`
reopens an existing patch on current upstream). A patch applies
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
`depends_on`, `optional_refs`, the linker, and `requiredModules` derivation identify a target by its bare name.

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

## `harness_files`

`harness_files`, on an item or an [original-skill declaration](#original_skills), maps an
item-relative output path to the file each harness ships there:

```yaml
harness_files:
  references/harness-tools.md:
    claude: references/claude-code-tools.md
    codex: references/codex-tools.md
    opencode: references/opencode-tools.md
```

Name all three harnesses. A source is an item-relative path in the assembled item, so it can come
from upstream or from the item's patch or overlay; for an original skill it is a path inside its
`skills/<plugin>/<name>/` directory. `null` states that the harness ships no file at that path. Each
tree gets its own harness's source at the output path, and no tree ships a mapped source under its
own name, so a body points every harness at the one output path. The emitters place the files after
body assembly and before localization, so each tree's harness phrasing, reference rendering, and
vocabulary check read only that tree's own file
([Transformation and emission](../docs/architecture/transformation-and-emission.md#harness-emission)).

The manifest loader rejects a harness it does not know, a path that leaves the item folder,
`SKILL.md` on either side, an output path that is also a mapped source, and a mapping whose
harnesses are all `null`. The build stops before deleting output when the item is not
skill-shaped, when a non-null source is not in the assembled item (an `omit` can remove one) or not
in an original skill's directory, or when the item already ships a file at the output path.
`validate` checks every tree for its own file at the output path and for any mapped source shipped
under its own name. A `null` leaves a dead path in that tree alone: a Markdown link or a
[path claim](../docs/architecture/references-and-linking.md#paths) that resolves to the output path
in the tree of a harness that maps it `null` is an error naming the mapping, on a curated item and an
original skill alike. Give that harness a source, or link the output path only from files that
harness does not ship, such as the other harnesses' own sources. How `depends_on` and reachability
read each harness's source is owned by
[References and linking](../docs/architecture/references-and-linking.md#depends_on-and-audience-reachability).

## `original_skills`

An original skill is a directory `skills/<plugin>/<name>/` with a `SKILL.md`, written in this
repository rather than taken from upstream. `original_skills` in `curation/<plugin>.yaml` declares
each one, keyed by its directory name:

```yaml
original_skills:
  writing-tunit-tests:
    invocation: auto
```

The SKILL.md owns content and description, and its body still owns the model-edge facts themselves;
the declaration owns invocation, the `depends_on` list that declares the body's mandatory model-edge facts,
the `optional_refs` list of conditional facts, and `harness_files`. A declaration is not a second body manifest: it holds `invocation`,
[`depends_on`](#dependencies), [`optional_refs`](#optional_refs), and [`harness_files`](#harness_files) only, and the manifest loader
rejects any other key by name, the curated-item keys `source`, `name`, `as`, `frontmatter`, `omit`,
`body`, `merged_from`, and `exclude` included. `invocation` is required, because an original has no
upstream posture to pass through, and it maps to each harness as the
[invocation table](#invocation-and-as-are-orthogonal) states. The declaration alone owns the
skill's invocation posture in every harness. Each emitter adapts an original skill as it adapts a
curated one, so the declaration decides its Claude invocation keys, OpenCode's hiding key, and the
Codex policy file. The SKILL.md may author neither `user-invocable` nor `disable-model-invocation`,
nor a `metadata` that holds `opencode/autoinvoke`, and the directory may hold no
`agents/openai.yaml`.

The directory is the skill's identity in every harness. The build stops before it deletes any
output, and `validate` reports an error, when:

- a directory under `skills/<plugin>/` has no declaration;
- a declaration has no directory;
- a `skills/<plugin>/` folder matches no manifest's `plugin.name`;
- a declared directory has no `SKILL.md`;
- a SKILL.md frontmatter does not parse as YAML;
- a SKILL.md `name` differs from its directory name;
- a SKILL.md authors `user-invocable`, `disable-model-invocation`, or a `metadata` that holds
  `opencode/autoinvoke`, which the declaration owns;
- the directory holds `agents/openai.yaml`, the Codex policy file, which the declaration owns;
- `skills/<plugin>` or `skills/<plugin>/<name>` is a symlink, which the build never follows.

A file directly in `skills/<plugin>/`, such as `NOTICE.md`, is not a skill: the build ignores it,
and it never ships.

Mark an original skill's script executable in this repository's Git index
(`git update-index --chmod=+x`); `validate` holds every tree's copy to that mode
([Finalization and handoff](../docs/architecture/transformation-and-emission.md#finalization-and-handoff)).

## Bundled scripts

A body that tells the model to run a script its skill folder ships names the interpreter, because a
packager can strip the executable bit and a Windows shell honors no shebang. Upstream superpowers'
`writing-skills` states the same rule. The convention:

| Script | Write |
|---|---|
| `.sh`, or a `bash` or `sh` shebang | `bash scripts/x.sh` |
| `.ps1` | `pwsh -File scripts/x.ps1` |
| `.js`, `.cjs`, `.mjs`, or a `node` shebang | `node scripts/x.js` |
| `.py`, or a `python3` shebang | `python3 scripts/x.py`, with the Windows form `py -3 scripts/x.py` beside it |

`validate` checks every Markdown file of a skill folder in the Claude tree, whose prose the other
trees share, and each other harness's own `harness_files` source, whose paths resolve as if it sat
in the Claude folder. Inline code is a code span as the harness phrasing finds one, so a span may
wrap a line or open with more than one backtick
([References and linking](../docs/architecture/references-and-linking.md#harness-phrasing)). A run
is inline code that starts with the script's path and carries arguments, or that a run verb
(`run`, `re-run`, `execute`) precedes on its line, and a path at command position on a fenced line
or after a separator such as `&&` in inline code. Inline code
that only names the path is a mention. The path resolves from the Markdown file's folder or the
skill folder; a `<skill-directory>/`-style placeholder or a leading `./` is spelling.

An interpreter word before the path names the interpreter, and only the convention's words name it
rightly: `bash`, `pwsh -File`, `node`, `python3`, and `py -3`, each with any flags of its own. Every
other word the check knows is a wrong interpreter, and its message names the convention's form:
`sh`, `zsh`, `dash`, `ksh`, `python`, `python2`, a versioned `python3.x`, `py` without `-3`,
`powershell`, `cmd`, an `.exe` spelling of any of these, and any other letter case. A run in the
current shell needs no exec bit, so its fence names the interpreter. In a fence labelled `bash`,
`source` and `.` run the script in bash. In a fence labelled `powershell`, `pwsh`, `ps1`, or `ps`,
the call operator `&`, dot-sourcing `.`, and the bare path all run a `.ps1` in PowerShell. Bash
sourcing a script of another interpreter is a wrong interpreter, and PowerShell hands a script of
another interpreter to its exec bit, which is a bare run. Anywhere else, inline code and a fence
labelled `sh` or `shell` included, `source` and `.` name no shell and so no interpreter, and the
message says so; there `&` is no call operator, so the path after it is a bare run. The check is
lexical: a run worded outside these forms is not seen, and a script that runs another script is not
read ([`scriptInvocationProblems`](../tools/lib/script-invocation.ts)).

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
On Codex and OpenCode 2 alike, `manual` therefore means not offered to the model, with the user
invoking it explicitly; Claude Code alone blocks model invocation
([ADR-0005](../docs/adr/0005-invocation-intent-in-the-manifest.md)). Text may name a `manual` item's
ID, for example to point the user at it, but a relative path may not reach its body, its
`SKILL.md` ([Dependencies](#dependencies)).

Invocation never changes artifact shape. Use `as: command` or `as: agent` when the Claude/OpenCode
artifact itself must change regardless of trigger intent. `as: command` is the per-item escape hatch
for an item that truly needs a command surface, a `/<plugin>.<name>` entry in OpenCode that takes
`$ARGUMENTS`; its body then lives in one command file, so skill-relative paths in it may stop
resolving.

## Body ownership and merge sources

Both body modes are blessed in `overlays/overlays.lock.json`. The build checks the stamped upstream
files and stops on drift until the edit is reviewed and re-blessed; a patch that no longer applies is
rebased instead, because `eject --bless` refuses to stamp it
([Manifest and body-edit flow](../docs/engineering/workflow.md#manifest-and-body-edit-flow)). A
stamp records the blob of a regular file, and `null` for a declared merge-source name that is absent
or not a regular file; only a regular file appearing there is drift. A body assembled from additional
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

A `frontmatter:` override merges in after body assembly, so an upstream rewrite never makes it
drift on its own. On an item with no `body:`, the override is stamped in the same lock: the entry is
marked `"frontmatter": true` and records only the item's primary upstream document, its `SKILL.md`
or its source command or agent file. A change to that document stops the build until
`npm run eject -- <plugin> <item> --bless` shows it and records it; no overlay directory exists for
such an item. An item with `body:` takes no frontmatter stamp: its override is guarded only as far
as its overlay or patch stamps the same document.

## Dependencies

`depends_on:`, on an item or an [original-skill declaration](#original_skills), lists output names
reached by mandatory model-edge facts in the shipped body. Conditional cross-Module facts use
[`optional_refs`](#optional_refs) instead. Author those facts in neutral upstream spelling
(`namespace:name`), or, for an original-skill target, in the spelling
[Localization](../docs/architecture/references-and-linking.md#localization) gives it; use
`/namespace:name` when the body points the human at a user surface. Each emitter renders the fact
in its own harness's spelling, which is longer than the neutral one. A skill `description` must fit
1,024 characters in every harness's rendered spelling, so a fact there counts at its longest
rendering; the build and `validate` fail on an overflow instead of shortening it, and the fix is a
shorter description in the item's `frontmatter:` override or its patch
([Description length](../docs/architecture/transformation-and-emission.md#description-length)).

A body line that tells the model to load another skill is a load-bearing model edge. Author its
target as a namespaced fact with a matching `depends_on` entry, or a guarded `optional_refs` entry
for a conditional cross-Module edge; a bare name names an ID that does
not exist in OpenCode, where every ID is qualified. `validate` rejects a bare name in these forms:

- The skill-tool call, written in upstream's Claude words: `Call the Skill tool with "ns:name"`,
  `call the Skill tool twice, for "ns:a" and "ns:b"`, or the generic `call the Skill tool for`,
  with the verb `Call`, `call`, `calls`, or `calling`. The build renders the whole sentence in each
  harness's own words. Any other sentence that mentions the skill tool fails, so reword it into one
  of these forms.
- An imperative that names the skill: `Load`, `Use`, `Follow`, `Invoke`, or `Call` ... the
  `` `ns:name` `` skill; `Load` or `Invoke` `` `ns:name` ``; and a `→` `` `ns:name` `` route.

A "see the `name` skill" reference or a routing hint without the word `skill` stays candidate
prose. Write subagent dispatch in upstream's Claude words as well, `Subagent (general-purpose)`, a
`` `general-purpose` `` subagent, or `` `Agent` `` calls, and never another harness's tool name; the
build renders them for each harness. A relative path may climb into a sibling item (`../name/...`)
or name an item root (`skills/name/...`). Into a `manual` item it may reach a bundled file, such as
`../name/references/notes.md`, but never the body: neither `SKILL.md` nor the folder itself. Point
the human at a `manual` item with `/ns:name` instead.

Current per-harness spelling, phrasing, localization, linking, reachability, path, candidate, and
ledger mechanics, including the OpenCode ID and harness vocabulary checks, live in
[References and linking](../docs/architecture/references-and-linking.md). The reason for the symbol
tiers and two-way declaration trade-off is [ADR-0008](../docs/adr/0008-references-are-symbols.md).

## `optional_refs`

`optional_refs:`, on a curated item or an [original-skill declaration](#original_skills), lists
resolved output names of conditional cross-Module model facts or user pointers. A present list must
be nonempty, contain unique portable names, and not overlap that declaration's `depends_on`.
Every target must exist uniquely in the emitted estate and belong to a different Module; an excluded
item is not a target. The declaration and facts across all shipped Markdown agree in both directions.
An unshipped `harness_files` source cannot justify a declaration.

Author the usual neutral `ns:name` model fact or `/ns:name` user pointer under an ATX heading whose
first nonempty body line is exactly:

```text
Applies only when the `<module>` Module is installed.
```

Replace `<module>` with the target's canonical Module name, such as `deniz-dotnet-general`. Every
occurrence, including one in the heading, needs that target Module's guard. Descendant headings inherit
it until the next same- or higher-level ATX heading; a nested section can establish its own Module
guard. Code and quoted heading/guard imitations do not establish or end scope, intervening prose
invalidates a guard, and frontmatter facts cannot be guarded. Optional intent never relaxes namespace
ownership, audience reachability, dangling-reference, or path rules: a model fact still cannot invoke
a manual target, while a pointer can name its user surface.

A cross-Module pointer into a Module not already required by independent `depends_on` declarations
must be optional and guarded. Pointers inside the source Module or into already-required Modules
retain their ordinary no-dependency-declaration behavior. An optional edge remains optional even if
another item independently requires its target Module, and still needs its own guard. Only
`depends_on` derives `requiredModules`; optional model facts and pointers add none. The build checks
prospective shipped content before deleting outputs; `validate` checks current content independently.
