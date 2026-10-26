# Walls for any language

## Clone gate

Run a clone detector on the base; the base's clone set is the baseline. A new clone involving a touched
or new file fails. Count clones by the file pair and the fragment, not by line numbers.

Example, to verify once in your environment: `npx -y jscpd@5.4.1 --min-tokens 50 --format <language>
--reporters json --output <scratch> --silent <source folder>`.

## Line gate

Physical lines of a new file, or net lines (added minus deleted against the base) of a touched file, against
the task's budget.

## Forbidden-construct grep

A regular expression over new and touched files for the constructs the task forbids (for example
hand-written lexers, process walkers through native APIs). Ratchet on the base count for touched files.

## Cache

Point each ephemeral tool's cache at a scratch folder outside the worktree through the tool's own cache
setting, and warm it once before the run.
