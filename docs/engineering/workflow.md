# Repository workflow

Date: 2026-10-11

This document owns task-triggered repository flow: where edits belong, how a curation session moves
from catalog to reviewed output, how upstream changes enter, and how a task closes. Manifest field
grammar remains in [`curation/SCHEMA.md`](../../curation/SCHEMA.md), verification evidence remains in
[quality-gates.md](quality-gates.md), and documentation lifecycle remains in
[documentation.md](documentation.md).

## Collaboration and change scope

Prefer the smallest correct change over a broad refactor. Work through the task's existing owner and
touch only the authored sources needed to satisfy it. A change whose purpose is not to change
behavior must preserve runtime, build, curation, and installer behavior.

## Work in the authored layer

Treat repository areas according to the action they permit:

- `external/` is read-only upstream evidence. Change a pin through the upstream-sync flow; never
  author inside a submodule worktree.
- `curation/`, `overlays/` and `overlays/overlays.lock.json`, original `skills/`, `tools/`, `docs/`,
  `experiments/`, and root public metadata such as `LICENSE` and `THIRD_PARTY_NOTICES.md` are authored
  inputs.
- `plugins/`, `opencode/`, `codex/`, `dist/`, `.claude-plugin/marketplace.json`,
  `.agents/plugins/marketplace.json`, `docs/inventory.md`, and `docs/ledger.json` are generated
  review surfaces. Change their inputs and regenerate; never patch them directly.

The current compile and emission mechanics are described in
[Transformation and emission](../architecture/transformation-and-emission.md). These boundaries are
workflow constraints even when the desired generated edit looks trivial.

## Start a curation session from the catalog

Initialize the submodules when a checkout does not have them, then follow
[`curation/SCHEMA.md`](../../curation/SCHEMA.md)'s inventory-before-decision and nearby-reason gate.
Use the regenerated catalog to establish the scanner-visible candidate set, but open the actual
upstream body, bundled files, and relevant dependency closure before judging an item. The catalog is
an index, not a substitute for reading the source.

Curation is a human judgement boundary. An agent may inventory, compare upstream bodies, trace
references, identify harness constraints, and recommend an option. It must not silently decide an
ambiguous item's trigger, shape, ownership, or module placement. Present the options and a
recommendation to the curator, then encode the chosen intent and reason. Consider the Claude Code
Plugin, OpenCode Module, and Codex Plugin together for every item rather than treating one as a
later port.

## Manifest and body-edit flow

Use a manifest-only change when it fully expresses the chosen intent. When body content must change,
create the body-edit surface with `eject`; do not construct an overlay directory by hand.

For a full-file overlay:

```text
npm run eject -- <plugin> <item>
```

Set the manifest to use the emitted overlay, edit the ejected source-named file or directory, then
regenerate. For a surgical skill patch, the flow has two passes:

```text
npm run eject -- <plugin> <item> --patch
# edit the working copy
npm run eject -- <plugin> <item> --patch
```

The second pass cuts `overlay.patch` with eight lines of context, verifies that it applies, removes
the temporary working-copy files, and records the upstream stamp. Then set the manifest to use the
patch and regenerate. Shape limits and merge-source declaration rules remain in `curation/SCHEMA.md`
and the command's own diagnostics.

A cut refuses, and writes nothing, while the working copy holds replay residue: an added `.rej`
file, or an added line that is a diff3 conflict marker (seven `<`, `|`, or `>`, then a space or the
line end). It checks the new patch before it replaces the old one, so a refused cut leaves both the
working copy and `overlay.patch` untouched.

To change an existing patch, or to carry it onto a moved upstream, reopen it instead of starting
over:

```text
npm run eject -- <plugin> <item> --patch --rebase
# resolve, review, and edit the working copy
npm run eject -- <plugin> <item> --patch
```

`--rebase` first prints every recorded-versus-current upstream diff, for the primary and every
declared merge source, exactly as `--bless` does. It then lays the current upstream item down as the
working copy and replays the old `overlay.patch` onto it one file at a time, reporting each file:

- `applied`: the old edit still applies as it was cut.
- `merged`: upstream moved inside the patched region and Git merged both sides without a conflict.
  Read the merge.
- `CONFLICT`: both sides changed the same lines. The file holds diff3 markers: `<<<<<<< ours` is
  current upstream, `||||||| base` is what the patch was cut against, and `>>>>>>> theirs` is the
  old patch's result.
- `REJECTED`: no three-way base was available, or the edit has no place to land; the hunks that did
  not apply are in `<file>.rej`.
- `GONE`: upstream deleted the file; the file's whole old diff is in `<file>.rej`.

The command exits non-zero while any conflict, reject, or deleted target remains. The old
`overlay.patch` stays in place until the cut, so deleting the working-copy files abandons the
rebase. Resolve every conflict, delete each `.rej`, and review the working copy before cutting. A
clean replay is textual, not semantic: whether the old edits still serve the item's intent at the new
upstream is the curator's review. The cut stamps the primary and every declared merge source, and
that stamp is the review record of the rebase, so no bless follows it. When upstream already carries
every edit, the working copy equals upstream and the cut refuses: the patch has nothing left to own,
so delete the overlay directory, its lock entry, and the item's `body: patch`. `--patch --force`
remains the way to discard the old patch and start again from pristine upstream.

An upstream move under an existing overlay or patch, or under the stamped `frontmatter:` override
of an item with no `body:` ([`curation/SCHEMA.md`](../../curation/SCHEMA.md#body-ownership-and-merge-sources)),
is a review event, not a hash-update chore. Run:

```text
npm run eject -- <plugin> <item> --bless
```

Read every displayed upstream diff. If drift exists, the command stops before changing the lock;
rerun with `--bless --yes` only after that review. Blessing records the current primary and declared
merge inputs as reviewed. It does not merge new upstream content into an owned overlay or prove that
the body still serves its curation intent; for a frontmatter override, reread the new body and
rewrite the override first when it no longer describes it. A patch that no longer applies to the
current upstream cannot be blessed: `--bless` refuses before it shows any drift, even with `--yes`,
and names the rebase.

These are the only two review ceremonies, and every message names them in one spelling. The build
and `eject` name `npm run eject -- <plugin> <item> --bless` while the overlay or patch still applies,
and `npm run eject -- <plugin> <item> --patch --rebase` when a patch no longer applies; no message
appends `--yes`, which is typed only after `--bless` has shown the drift. `npm run sync` names no
eject step: it points at the upstream diff and leaves the step to the next build.

## Upstream sync flow

`npm run sync [submodule]` is the deliberate, manual route for moving one or all upstream pins. It
reports affected curated and merged sources and points at the old-to-new upstream diff. Treat the
report as a review aid, not a complete impact proof: inspect the submodule diff, regenerate the
inventory, reopen affected source bodies and manifests, and review any posture, reference, overlay,
or patch impact.

After a pin move, resolve overlay drift through the bless or rebase flow, make any newly required
curation decisions with nearby reasons, regenerate all three harness outputs, and run the
generated-output gate in [quality-gates.md](quality-gates.md#command-matrix). Complete its
generated-output review before accepting the update.

A manifest records only sources the current pin has, and `validate` rejects an unknown source,
taken or excluded. When a pin move deletes a source, remove its item in the same change: an exclude
is not a tombstone. Give the reason in the commit message and, when another item succeeds it, in that
item's comment. If upstream moved the source instead, repoint `source:`. In the other direction, an
exclude for a source that only a later pin has lands with or after that pin move, never before it.
`validate`, the build, and `sync` say this rule in their unknown-source messages. A removed item's
output name stays known to the handoff and candidate checks through the committed ledger's history
([References and linking](../architecture/references-and-linking.md#handoff-templates)).

## Bump the Module version with the bytes

A Module's `plugin.version` in `curation/<plugin>.yaml` moves in the same change that moves that
Module's emitted bytes, so two builds never share a version:

- **patch** when the emitted content moved but the Module's surface did not — an upstream body
  flowed on a pin move, an overlay or patch was edited, a `frontmatter:` override changed.
- **minor** when the surface itself moved — an item was taken, excluded, or renamed, or its
  `invocation`, `as`, or declared dependencies changed.
- **major** is unused while the estate is pre-1.0.

Bump only the Modules whose own bytes changed; a pin move that reaches one Module does not version
the others. The installer records both version and Bundle digest, so drift is still detected when a
version is forgotten — the version exists so a human comparing two installs is not misled, not as
the integrity mechanism.

**To be reviewed later:** nothing enforces this. `validate` could compare a Module's committed
Bundle digest against the last version that shipped it and fail when bytes moved under an unchanged
version, which would turn the policy into a gate. Decide that when the estate stops changing every
wave; see [`docs/ROADMAP.md`](../ROADMAP.md).

## Review a curation wave

Deterministic generation and validation establish syntax, output identity, declared references, and
other implemented checks. They do not decide whether two skills compete, whether an overlay still
expresses the recorded intent, or whether candidate bare-name references matter. After a curation
wave, before declaring a Module closed, and after a pin move, use the agent-only
[reference-audit playbook](../agents/reference-audit-playbook.md) to surface the reference questions
for the curator. Trigger competition, body intent, and harness fit belong to the agent-only
[curation sanity panel playbook](../agents/curation-sanity-panel.md), which says when it runs. Both
inform judgement; neither authorizes an agent to change curation.

## Documentation closeout

Close every implementation, curation, tooling, or experiment task with a documentation pass:

- update current architecture or engineering canon when its described behavior or practice changed;
- update item-level intent beside the manifest item rather than creating a detached curation diary;
- revise an ADR only when the decision and its trade-off changed or a new decision passes the
  [ADR admission gate](../adr/README.md#when-to-write-an-adr);
- put current status, next work, and known implementation gaps in `docs/ROADMAP.md`;
- preserve bounded findings as research or experiment evidence rather than current policy; and
- consume completed handovers and relocate durable conclusions from temporary plans according to
  [documentation.md](documentation.md#handovers-and-plans).

Documentation is part of the same change as the behavior it describes. Do not close a task with a
known stale current claim and defer its correction as follow-up documentation.
