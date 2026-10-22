# ADR-0010: An item can be emitted for some harnesses only

Date: 2026-10-22
Status: Accepted

## Context

Every non-excluded item ships to all three harnesses. `exclude` acts on every harness at once.
`harness_files` swaps whole files per harness, but `SKILL.md` cannot be one of its paths, and a
mapping that is `null` for every harness is rejected. So no manifest field can express "this skill
does not belong in this harness".
[ADR-0007](0007-control-beats-fidelity.md) requires that an item's fate be decided for all three
trees at once, and [ADR-0006](0006-output-is-a-transformation.md) resolves each axis per harness.
"Absent from this tree" is one of those per-harness outcomes, but today it cannot be stated.

Two items in `deniz-coordination` ([ADR-0009](0009-coordination-ships-in-its-own-module.md)) need it.
`verified-waves` gives OpenCode and Codex a verified multi-agent ceremony that the user starts. Claude
Code already has a native workflow tool the user opts into, so it does not carry the skill. The
roadmap already anticipated this split: Claude Code gets the scripted form of a playbook, and
OpenCode and Codex get the model-driven form of the same playbook. The dispatch `harness` starts
worker models of other families, and OpenCode already reaches many families natively, so it ships to
Claude Code and Codex only. The need is not specific to these items: any skill that supplies what one
harness has natively meets the same problem.

## Decision

An item, or an original-skill declaration, can carry an optional allowlist
`harnesses: [<harness>, ...]`. When the key is absent, the item ships to all three harnesses, so
every existing manifest keeps its output byte for byte. When the key is present, an unlisted harness
gets nothing from the item: no skill, command or agent, no marketplace or Bundle entry, and no
ledger or inventory row for that tree. Absence is decided in the emitted trees, because Claude Code
and Codex read their Plugin trees from the marketplace without the installer.

The key states a decision for all three trees, as ADR-0007 requires. It is never a "not yet
ported" marker. Like `exclude`, it carries its reason beside it. A list of all three is an error:
"everywhere" has one spelling, the absent key, so narrowing an item always shows in a diff as an
added key, never as an edited list.

Each tree must prove the result is whole. The rules extend ones that exist today:

- **Manifest.** The list is non-empty, contains no duplicates, and names only known harnesses.
  `harness_files` on the item may name only listed harnesses; an unlisted harness counts as `null`.
- **Reachability is checked where a fact ships.** The linker already asks whether a fact's target
  has the required audience surface in every tree that ships the fact: all three trees for a file
  every tree ships, and only its own harness for a `harness_files` source
  ([References and linking](../architecture/references-and-linking.md#depends_on-and-audience-reachability)).
  An item that a tree does not emit has no surface in that tree. So a fact may name a narrowed item
  only from files that ship exactly in trees that emit it. The usual way is a `harness_files`
  source for those harnesses. For example, a skill that ships everywhere names the harness from a
  notes file that only the Claude Code and Codex trees ship. `depends_on` stays one list per item,
  and the two-way agreement with the facts stays exact. The target is listed because some tree
  ships the fact; reachability is checked only in those trees.
- **Links, paths and guarded facts.** The same per-tree reading applies to Markdown links, path
  claims and `optional_refs`. In a tree that lacks the target, a reference to it is an error that
  names the allowlist.
- **Module requirements.** `requiredModules` is derived per harness, from the `depends_on` edges
  whose facts that tree ships.
- **Empty Modules.** A Module that emits nothing for a harness is an error, not an empty Plugin or
  Bundle.
- **Names.** Bare output names stay unique across the whole repository, whatever the allowlist.
  The linker and `depends_on` keep one identity per name.

Rejected:

- Installer-side filtering at Selection time. Claude Code and Codex install their Plugins from the
  marketplace without this installer, so the skill would still reach those trees.
- A denylist (`not_harnesses:`). A new harness would silently receive every item. An allowlist makes
  the author say which harnesses an item is meant for.
- Allowing `SKILL.md` in `harness_files` with `null`. A file-swap mechanism would then decide whether
  an item exists, and the emitters, marketplaces and ledger would each have to infer absence from a
  missing file.
- `depends_on` per harness. The per-file reading above already says which trees need a target, so a
  second, per-harness list could only drift from it.
- A per-harness Module split, such as Claude-only and OpenCode-only versions of a Module. That
  doubles the Package, Release and Selection surface for a difference of one or two skills.
- Writing "not for Claude Code" in the skill body. The skill would still load and still use
  description budget in the harness that should not have it.

## Consequences

- One Module version can now emit different contents per harness. The Package and Release stay as
  they are, but generated-tree review must read each tree on its own, not one tree as the pattern
  for the other two.
- Trigger and description probes become per tree. A description tuned in one tree no longer proves
  the others.
- First uses, in `deniz-coordination`:
  - `verified-waves`: `harnesses: [opencode, codex]`. Its Claude Code tool notes and their test go,
    and its `harness_files` mapping loses the `claude` side.
  - `harness`: `harnesses: [claude, codex]`.
  - `coordinator` ships everywhere and names the harness only from a notes file that the Claude Code
    and Codex trees ship. That is the case the reachability rule above exists for.
  - In Process, `ask-deniz` ships everywhere, so its `verified-waves` and `harness` rows move into
    files that only the matching trees ship.
- Records under `experiments/` and notes under `docs/research/` are dated evidence. They keep the
  old paths and names, and nobody "fixes" them.
- Work owned by this ADR:
  - the manifest loader and `curation/SCHEMA.md`;
  - the three emitters, the marketplaces and the OpenCode Bundle manifest;
  - the ledger and inventory, and `requiredModules` derivation;
  - the `validate` and linker rules above.

  Tests come first. `docs/architecture/transformation-and-emission.md` and
  `references-and-linking.md` change in the same commit.
- Changing an item's list later, for example when OpenCode or Codex gains a native workflow tool, is
  a manifest edit with its reason, not a new decision.
- Reversal trigger: if no item keeps using the allowlist beyond a migration, remove the key and go
  back to one surface per Module.
