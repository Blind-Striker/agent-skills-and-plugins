---
record_id: opencode-installer-v0.3.0-2026-09-06
date: 2026-09-06
repo_head: 127159518c956fc39406809abce11bab1afb8b28
kind: runtime-smoke
summary: Package 0.3.0 includes General 0.9.0 and Aspire 0.3.2 and passed Linux source, tar-mode, isolated Plan/Apply/status, and remote asset checks before public publication.
isolation_ok: true
source_ref: 127159518c956fc39406809abce11bab1afb8b28
package_name: deniz-agent-skills-0.3.0.tgz
package_size: 924792
package_sha256: a6e5c309cd4739684d908c9bae224941272c57471f278b9a738dac53f704ef22
transport: public-release
release_tag: installer-v0.3.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.3.0
release_target: 127159518c956fc39406809abce11bab1afb8b28
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/34018422257
---

# OpenCode installer v0.3.0

## Scope

This is a new Package and Release, not a replacement of the 0.2.0 asset. It includes the General
recuration from `4446fa5`, Aspire's reviewed merged source at `c9d042e`, and the installer fix that
makes a plan-only request with pending Recovery return nonzero because no requested Plan was made.
Dependency-aware Module Selection is not part of this release.

## Method

Source commit `1271595` passed [push CI](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/34018296159).
A draft Release targeting that exact commit was created with no assets. The manual
[`release-package`](../../../.github/workflows/release-package.yml) workflow then ran with explicit
`source_ref=127159518c956fc39406809abce11bab1afb8b28`, `release_tag=installer-v0.3.0`,
`publish=true`, and `replace_existing=false`. Steps 1-6 ran in that workflow; steps 7-8 were
independent publication checks:

1. Scan the source commit's full history with digest-pinned Gitleaks.
2. Run the repository tests, typecheck, lint, format, public-safety, build, inventory, and validate.
3. Confirm two generation rounds leave the source checkout clean.
4. Pack on Linux and verify the exact payload, Bundle hashes/digests, and tar executable modes.
5. Run the Package with an isolated HOME/XDG/npm profile: zero-write Plan, Apply-all, and status.
6. Upload that verified artifact to the draft Release, download it again, compare bytes, and rerun
   the Package verifier.
7. Independently download the draft asset and check its SHA-256 and Package payload/hash/tar modes.
8. Publish the draft after these checks, then download the public URL without authentication and
   verify the same SHA-256 and Package payload/hash/tar modes again.

Both workflow jobs succeeded. The public tag resolves to the source commit; asset ID `546864656`
contains `deniz-agent-skills-0.3.0.tgz`, 924,792 bytes, with the SHA-256 recorded above.

## Module Identity

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.5.0` | `sha256:20e214a70016ecb1b69bfc8620b179a3b2b93124926f95678e96467a10eed06c` |
| `deniz-dotnet-general` | `0.9.0` | `sha256:cac201056290e377884757e4c1b7bd889867b79596d87d5f17a128a089662312` |
| `deniz-dotnet-akka` | `0.3.0` | `sha256:3f2fe3b1d3023075f30fe0ba6c335331f35c9641de406e24318956a9c96b047d` |
| `deniz-dotnet-aspire` | `0.3.2` | `sha256:6b2ee1b5fd06b59e1a9152846ee196f6c6433d345591f079e90e5c76d64b8fcd` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot. It contains a 0.3.0 preparation notice,
the then-current verified 0.2.0 recipe, and a link to the live repository recipe. The new Package
digest could only be recorded after packing; the post-publication README and its regression guard
therefore live in a later commit. They were not repacked into the same asset.

The post-publication experiment selftest exposed a stale live-ledger baseline from before
`vectorization`: 86 skill entries instead of 87. Review of the ledger and emitted entry-point diff
confirmed that one addition, with 38 commands, 19 Process model-invocable entries, and 14 parked
entries unchanged. The closeout updates only that expected count, not the derivation or historical
measurement records. The emitted tree has 88 skills because the original `writing-tunit-tests`
skill is outside the manifest-item ledger. This test-only correction is not in the Package source.

The previous [0.2.0 correction record](2026-08-25-opencode-installer-v0.2.0-posix-correction.md) remains
historical evidence. Its asset ID `529336219`, size 902,189 bytes, and SHA-256
`4ce23817052317b80926a6cd0aed7063364e9625c012f22080bfb887727286be` were preserved.

## Proof Limits

- Linux workflow checks establish Package integrity and fresh all-Module installer behavior. Local
  verification additionally checks the downloaded tar archive on Windows, not POSIX execution there.
- No real user-profile installation, model run, or TUI was exercised. Model selection, instruction
  following, and permission behavior remain outside this proof.
- Fresh all-Module installation does not prove dependency closure for arbitrary subsets or every
  update/remove scenario. The current Selection limitation remains documented.
- No claim is made that all upstream Aspire CLI, TypeScript, deployment, or testing examples were
  executed in consumer environments. Upstream metadata 0.0.2 is not an upstream Release claim.
- Release assets remain mutable; the recorded digest detects replacement but cannot prevent it.
