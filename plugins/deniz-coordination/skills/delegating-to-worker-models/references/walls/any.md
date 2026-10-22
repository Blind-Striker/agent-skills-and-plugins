# Walls for any language

## Clone gate

`npx -y jscpd@5.4.1 --min-tokens 50 --format <language> --reporters json --output <scratch> --silent <source
folder>`. Run it on the base; the base's clone set is the baseline. A new clone involving a touched or new
file fails. Count clones by the file pair and the fragment, not by line numbers.

## Line gate

Physical lines of a new file, or net lines (added minus deleted against the base) of a touched file, against
the task's budget.

## Forbidden-construct grep

A regular expression over new and touched files for the constructs the task forbids (for example
hand-written lexers, process walkers through native APIs). Ratchet on the base count for touched files.

## Cache

Point the package managers' caches at a scratch folder outside the worktree (`npm_config_cache`,
`UV_CACHE_DIR`), and warm them once before the run.
