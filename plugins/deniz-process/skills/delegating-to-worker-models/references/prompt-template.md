# Task prompt template

The coordinator fills this in; the dispatcher prepends the worker contract and the model file. Drop the
sections a task type does not need. Keep every slot concrete: a slot left abstract is read in its
strictest form.

```text
## Goal
<one sentence>. Make the smallest change that satisfies the requirements below.
R1 <...>
R2 <...>

## Read first
<paths: the approved plan, the spec, the files to change, the binding documents>.
Binding rules, quoted: "<...>"
Base: <SHA>. Confirm that HEAD equals it before you edit.

## Scope
Write only: <paths>.
Do not edit: <sibling-lane paths>.
Shared code for an arrangement a sibling task also builds goes in: <path>.

## Budget
Production <N> lines, tests <M> lines. Lines are physical lines; for a change to existing files, net lines
against the base.
<Fix-up pass:> caps are a delta on top of <base SHA> (production +<N>, tests +<M>). Report this pass's
figures and the total for the whole change.

## Inputs owned elsewhere
<value>: arrives as <argument | file>. <value>: ...
<Fixed lists the reads need (places to look, keys to read) are part of the reads, not owned values.>

## Definitions
<term>: <meaning>. In scope: <worked example>. Out of scope: <worked example>.

## Constructs
Use: <named standard library calls, libraries, patterns>.
Do not write: <forbidden constructs>.

## Ordering and invariants
<"check finite state before waiting on an event">; <rules written as invariants, not as example rows>.

## Status and output semantics            (code that reports results)
<which outputs judge (pass/fail) and which only report facts, and why>

## Pre-approved follow-on changes          (optional)
The plan names these, so they are not changes outside it: <mechanical fallout of a planned change, e.g.
"a length or complexity limit triggered by a planned move: extract a private method in the same file,
keeping order, arguments, and results; no new type, no suppression">

## Repository dodges and stop conditions  (optional)
<repository-specific additions to the contract's lists>

## Tests and oracles                       (tasks that write or change tests)
<the mutant class for each new bound, e.g. "a no-op that sends nothing">; <any rule from the repository
addendum or a language reference, quoted>

## Refactor only                           (changes to existing behaviour-preserving code)
Re-measure at the actual base before editing. Restore the base behaviour exactly unless the plan lists the
change. Byte-identical output proves only the pinned inputs.

## Environment
<runtime floor and edges, operating systems, preparation steps, long-command timeouts>
<each runtime or target-framework requirement on its own line, e.g. "Run the tests on BOTH net10.0 and
net472"; a requirement only implied by the verify list is skipped>

## Verify
The dispatcher runs: <commands>. <Walls: command and limits, when present.>
```

Coordinator checklist before sending (not part of the prompt): the verify commands ran once on the base
with a meaningful result; every name they reference exists; the base contains every prerequisite; the
task file was written with an editor tool; no other task in the run shares a scope path; the plan is
approved where plan-first applies; the budget caps come from a measured projection, and where there is
none, plan-first produced one before the caps were set; every mechanical fallout you can foresee from a
planned change (an analyzer length or complexity limit, a moved member) is pre-approved in its slot.
