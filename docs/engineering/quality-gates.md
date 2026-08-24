# Engineering quality gates

Date: 2026-08-24

This document owns completion evidence: which repository commands apply, what CI proves, what still
requires human review, and which words those results authorize. Script implementations remain
canonical in [`package.json`](../../package.json); this document names their required interfaces
rather than restating their definitions.

## Command matrix

The matrix is additive. When a change matches more than one row, run the union of the rows.

| Change trigger | Required local commands | Additional proof |
|---|---|---|
| Repository tooling under `tools/`, or its TypeScript, package, formatter, or linter configuration | `npm test`; `npm run typecheck`; `npm run lint`; `npm run format:check`; `npm run check:public-safety` | Review failures and warnings; do not weaken a gate to make the change pass |
| Installer, packaging, emitter, or other generated-output behavior; `curation/`, overlays, root license/notices, or original `skills/`; an upstream submodule pin | The five tooling commands, then `npm run build`; `npm run inventory`; `npm run validate` | Perform the idempotence rerun and generated-output review below |
| Prose protected by `tools/repository-docs.test.ts` | `npm test` | Retarget the assertion in the same change when protected prose intentionally moves |
| Harness-invocation experiment scripts, method, or prose protected by the subsystem selftest | The applicable repository row above, plus `pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1 -SkipLab` | Use the full prepared-lab selftest only for the claims described below |
| Other documentation-only changes | No narrower local command is prescribed | Check dates, links, ownership, relays, contradictions, and claim loss manually; CI still runs its full gate |

Changing a command definition can also change packaging or generated behavior; in that case the
generated-output row applies. A focused command may be run while iterating, but it does not replace
the applicable completion row.

These gates produce per-change evidence. A prior green local or CI run is not standing repository
health recorded in the roadmap; rerun every applicable gate for the change being completed.

## What CI enforces

The current workflow is [`.github/workflows/validate.yml`](../../.github/workflows/validate.yml). On
pull requests and pushes to `master`, it checks out recursive submodules, installs with Node 24 and
`npm ci`, then runs:

```text
npm test
npm run typecheck
npm run lint
npm run format:check
npm run build
npm run inventory
npm run validate
npm run check:public-safety
```

A separate least-privilege job checks out full superproject history and runs Gitleaks with redacted
reporting and no uploaded report artifact. The validation job scans current tracked and untracked
authored/generated files for Gmail identities, user-profile paths, and machine-specific workspace
paths; only the named synthetic detector fixture is exempt. Commit identity is not repository
documentation and is outside that current-tree policy. Submodule histories remain upstream
responsibility, while copied generated output and Package content remain inside this repository's
build and package gates.

After generation, CI mechanically stages only `plugins/`, `opencode/`, `dist/`, `.claude-plugin/`,
`docs/inventory.md`, and `docs/ledger.json`, and fails if that staged generated state differs from
the commit. `.claude-plugin/` is staged as a directory; the only current file under it is the generated
`.claude-plugin/marketplace.json`, which is a never-edit review surface. That proves one clean
regeneration matches the checked-in generated estate.

CI does **not** prove that generation is idempotent across a second run, that an intentional
generated diff is semantically correct, that warnings are acceptable, that documentation has one
owner, or that a harness selects and follows an artifact at runtime. It also does not run the
harness-invocation subsystem selftest. Secret scanning lowers risk; it does not prove that no secret
class exists outside its rules.

## Human-only generation proof

For every generated-output change:

1. Run the generated-output command row once and inspect the resulting generated diff.
2. Preserve that post-generation diff as the comparison point.
3. Run `npm run build` and `npm run inventory` a second time.
4. Confirm the second run adds no change: the generated diff after the rerun is byte-for-byte the
   same as before it. A dirty tree with intended first-run output is valid evidence; merely observing
   that it is still dirty is not.

Review the generated estate rather than trusting green commands. Inspect both harness trees, every
changed `opencode/*/manifest.json`, `.claude-plugin/marketplace.json`, emitted installer files under
`dist/`, `docs/inventory.md`, and `docs/ledger.json`. Confirm that each change follows from an
authored input, that no expected artifact disappeared, and that reported drops, warnings, invocation
posture, artifact shape, references, and file modes are understood.

The CI stale-output check is mechanical; this review is the human semantic gate. Neither substitutes
for the other.

## Completion-claim vocabulary

Use the narrowest statement supported by evidence:

- **Tests pass** means `npm test` completed successfully.
- **Typechecks**, **lints**, or **format-clean** name only the corresponding command result.
- **Builds** means `npm run build` completed; it does not mean validation, idempotence, installation,
  or runtime behavior succeeded.
- **Validates** means `npm run validate` completed against the generated estate; warnings and the
  proof limits in [References and linking](../architecture/references-and-linking.md#proof-boundary-and-current-limits)
  and [Transformation and emission](../architecture/transformation-and-emission.md#current-limits)
  still require review.
- **Generated output is current** means the intended authored inputs were regenerated and the
  resulting generated diff was reviewed.
- **Generation is idempotent** additionally means the second build and inventory run added no diff.
- **Works** requires evidence at the behavior boundary being claimed. A build or link check alone
  never proves harness discovery, model selection, installation on an untested environment, or skill
  compliance.
- **CI is green** is reserved for an actual successful CI run, not a locally reconstructed command
  sequence.
- **Complete** means every applicable command and human-only check ran, and any skipped, unavailable,
  failing, or warning-producing check is reported explicitly.

## Documentation guards

`tools/repository-docs.test.ts`, executed by `npm test`, protects selected root README, package
research, and lab claims whose wording encodes a repository safety property. Before relocating such
text, inspect that test and update its target while keeping the invariant.

`experiments/harness-invocation/selftest.ps1` contains a separate documentation-regression section
for experiment protocol and the agent reference-audit playbook. It is outside npm test and CI. A
green npm test therefore says nothing about those assertions, and a green experiment selftest says
nothing about unguarded repository documentation. Documentation maintenance policy remains in
[documentation.md](documentation.md#guarded-prose-and-contradictions).

## Experiment selftest boundary

The portable, free subsystem gate is:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1 -SkipLab
```

It covers deterministic script, fixture, installer-isolation, machine-path, and documentation
regressions without requiring the prepared external lab. When a change affects lab resolution or a
runner's dry-run wiring and the isolated lab is available, also run:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1
```

The full selftest still spends no model tokens and does not establish model behavior. Token-bearing
harness runs, human permission observations, and committed measurement claims follow the experiment
protocol and records schema; they are evidence-producing activities, not CI or general completion
gates.
