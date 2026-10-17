# Work types

## New code

- Plan-first is the main lever: most avoidable size in new code is a design choice visible at plan time.
- Name the expected constructs and the forbidden ones. State the hazards concretely and say that
  everything else is out of scope; "make it safe" alone produces maximal code.
- One owner per output id: tell each task which ids it emits and which it must not.
- Spell out the status semantics per output: which outputs judge and which only report facts. An output
  that needs a value owned elsewhere either receives it as an input or reports a fact.
- Give a split budget. In the measured comparison, the template's mechanisms halved the size of new code
  against a "keep it simple" prompt; walls removed the lint findings but did not change the size.

## Refactor

- Re-measure at the actual base before editing; counts in a plan go stale.
- Restore the base behaviour exactly unless the plan lists the change; finish what is deliberate. This
  leaves the worker nothing to decide in a fix-up.
- Byte-identical output proves only the pinned inputs. Hidden behaviour changes pass every gate when the
  pinned inputs never reach their paths.
- Review per behaviour against the base (`git show <base>:<file>`), with two families. Fresh worker
  reviewers produce the long list; the coordinator's read removes false alarms and finds what they miss.
- Late information cannot reach a running worker: apply it at review by restoring the affected files to
  the base and proving the diff empty.
- A worker that relocates code into another language or layer loses error paths the old code had. Ask for
  the change where the code is.

## Evaluation

An evaluation measures what an outsider meets: building from public documentation only, exploring an
unfamiliar tool. Its output is friction, not a product change, so the implementation stop list would
stop it at every gap.

- Say in the task text that it is an evaluation; the worker contract's evaluation clause then replaces
  its stop list.
- State the fence concretely: the folder, the machine rules, network and time limits.
- Name only shapes the material under evaluation contains. A shape you name that the documentation lacks
  tests your spec, not the documentation.
- A requirement reported FAILED, with its reason, is a result, not a defect.
- Each FRICTION entry is a finding about the documentation: check it against the documentation before you
  file it.
