# Walls

Mechanical gates in the verify step. Read during the walls preflight in SKILL.md.

`any.md` holds the gates every language shares: the clone gate, the line gate and the
forbidden-construct grep. A language's own walls (its analyzers, formatter, scanners and their traps)
live in that language's skill module; load its walls profile beside this file when one is installed.

## Rules

- **Ratchet, not absolute.** Run every wall on the base first. Gate only findings that are new by identity:
  the tuple (rule, file, normalized message), without line numbers, which shift with every edit. A touched
  old file is gated on new findings only; a new file is gated whole. Keep a count check as a cheap second
  line. Where a tool's output cannot be matched by identity, its profile says "count ratchet only", and the
  reviewer checks that tool's findings by hand.
- **Pinned versions** in every ephemeral command, so two runs compare.
- **Thresholds are the coordinator's.** The profiles give defaults; a change is recorded with its reason.
- **Cost:** no install and no repository change, but wall-clock time, a network fetch on a cold cache, and
  an occasional false positive.
- **What walls do and do not do:** in the measured comparison they removed lint findings but did not reduce
  the size of new code; the prompt mechanisms did that. The worker sees the walls in its verify step, so
  part of the gain may be the worker iterating against their feedback.
