# Harness invocation records

Committed measurement evidence lives here. Research documents synthesize records by `record_id`;
full raw transcripts, credentialed event logs, and mutable outputs stay in the external lab outside
the repository and real user profile.

## Evidence tiers

| Tier | Contents | What a reviewer can do without the lab |
|---|---|---|
| 0 | Prose only | Read a report, but not treat it as verified evidence |
| 1 | Structured record using the schema below | Check fields, compare runs, and inspect pass/fail per probe row |
| 2 | Tier 1 plus sanitized transcript or event excerpts | Challenge the interpretation of model wording and observed events |
| 3 | Runnable recipe with secrets supplied out of band | Re-run the measurement |

A new routine measurement requires tier 1 or higher. Any quantitative panel, rate, or fraction
claim, and any claim that changes an ADR Decision or linker behavior, requires tier 2 or higher.
Using the word “verified” follows the same threshold as the underlying claim.

Tier-2 excerpts must retain enough surrounding event or transcript context to support the
interpretation while removing credentials, tokens, machine-specific paths, and unrelated personal
data. Sanitization is not a reason to commit the full raw log.

## Record schema

Name records `YYYY-MM-DD-<slug>.md`. Every record starts with YAML frontmatter containing:

```yaml
---
record_id: <stable-id>
date: YYYY-MM-DD
repo_head: <git-sha>
kind: <record-kind>
summary: <one-line-result>
isolation_ok: true
---
```

Use only applicable additional fields for non-panel record kinds. A `kind: model-panel` record also
requires `fixture_sha`, `harness_name`, `harness_version`, and `runner_revision` in frontmatter, and
a results table with one row per attempt:

| model | probe_id | status | cost | tools_observed | notes |
|---|---|---|---:|---|---|
| `<provider/model>` | `<probe>` | `pass` |  |  |  |

`status` is exactly one of `pass`, `fail`, `timeout`, `invalid`, or `skipped`. Include `cost` only
when billed. A rate denominator includes only rows whose status is `pass` or `fail`, unless the
record explicitly defines a different denominator.

## Supersession

Measurements are append-only. Correct an obsolete record by creating a new file whose frontmatter
adds `supersedes: <record_id>`; do not rewrite the old record body. Research should link the current
record while preserving the supersession chain.
