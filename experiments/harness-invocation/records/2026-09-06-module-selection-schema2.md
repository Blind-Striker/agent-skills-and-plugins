---
record_id: module-selection-schema2-2026-09-06
date: 2026-09-06
repo_head: 8be80489cd721b08f0ffa3bee711d8348d1e0ac1
kind: runtime-smoke
summary: Schema-2 Package 0.3.0 from 8be80489 passed Linux build-only release-package proof and independent verify:package; it is not the public installer-v0.3.0 asset.
isolation_ok: true
source_ref: 8be80489cd721b08f0ffa3bee711d8348d1e0ac1
package_name: deniz-agent-skills-0.3.0.tgz
package_size: 925968
package_sha256: 94e0dc168201aba4fd24bddd08b9c41253ae61308ab5fa6808f59d61534bb01a
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/34043083242
---

# Schema-2 Module Selection Package proof

## Scope

This is exact-commit Linux Package proof for dependency-aware Module Selection. It is a build-only
schema-2 artifact of Package version 0.3.0, not a public Release, not a replacement of
`installer-v0.3.0`, and not a real-profile install. `repo_head` is the feature source HEAD. This
record does not predict a later documentation-closeout commit.

Feature source is four pushed commits:

| Commit | Subject |
|---|---|
| `acdf457eb000920ed61ce0f42fa54c95a9801334` | feat: derive Module requirements from curation |
| `22d226ef88e4cf105a7665e4f11b4c7e6c622fa2` | feat: bind Module requirements to schema-2 identity |
| `34d7f87c06f31472eccafb9fba7fd63e6c8fe91e` | feat: reject incomplete Module Selections |
| `8be80489cd721b08f0ffa3bee711d8348d1e0ac1` | test: verify dependency-aware installation safety |

The independently reviewed tree `6e111fbcdcfae83d3401e89a9db27d898d8d001f` equals that source HEAD
tree. Final Grok review reported no Critical or Important findings; two documentation Minors were
fixed and re-reviewed.

## Method

Parent local gate on the feature source: `npm test` 461 total, 458 passed, 3 Windows skips;
typecheck, lint, format:check, and public-safety passed; `selftest.ps1 -SkipLab` green with 3 lab
skips. Worker raw generation produced 644 files identical across two rounds. One early worker npm
`ECOMPROMISED` failure was followed by passing isolated and full reruns and the parent's fresh full
run; the cause was not established, and tests were not weakened.

Two GitHub runs:

1. Push CI [`34043067190`](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/34043067190)
   succeeded (validate and secrets).
2. Manual [`release-package`](../../../.github/workflows/release-package.yml) run
   [`34043083242`](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/34043083242)
   succeeded with `head`/`source_ref` `8be80489cd721b08f0ffa3bee711d8348d1e0ac1`. The build job
   completed every step: full-history source gate, repository gate, two clean generations, exact tar
   verifier, isolated zero-write Plan, Apply-all, and status. The publish job was SKIPPED. Workflow
   inputs were `publish=false` and `replace_existing=false`; `release_tag=installer-v0.3.0` was unused
   because publish did not run.

The parent downloaded that workflow artifact and independently ran `npm run verify:package` on the
contained `deniz-agent-skills-0.3.0.tgz`: PASS.

## Package identity

Three different objects share nearby names. Do not treat them as one digest.

| Object | Identity |
|---|---|
| Workflow ZIP artifact | ID `9992289211`, name `release-package-8be80489cd721b08f0ffa3bee711d8348d1e0ac1`, expires `2026-10-06T15:42:44Z`, digest `e81f60d11d38f07795f107f225f5d5e8d608054dfeff6f482fc61f906147a489` |
| Schema-2 build-only Package (this record) | `deniz-agent-skills-0.3.0.tgz`, 925,968 bytes, SHA-256 `94e0dc168201aba4fd24bddd08b9c41253ae61308ab5fa6808f59d61534bb01a` |
| Public schema-1 Release Package | GitHub Release `installer-v0.3.0`, source `127159518c956fc39406809abce11bab1afb8b28`, asset `546864656`, 924,792 bytes, SHA-256 `a6e5c309cd4739684d908c9bae224941272c57471f278b9a738dac53f704ef22` |

The ZIP digest identifies the workflow archive, not the tgz. The schema-2 tgz uses the same filename
as the public Release asset and is not that file. Live API checks left the v0.3.0 and v0.2.0 Release
IDs, digests, sizes, and timestamps unchanged. Public identity remains in the
[v0.3.0 release record](2026-09-06-opencode-installer-v0.3.0.md).

## Module identity

| Module | Version | Module digest | requiredModules |
|---|---|---|---|
| `deniz-process` | `0.5.1` | `sha256:5009aa5724801c22ce26cde00058071ae8883959f01f41b2efe02ee9931ee463` | `[]` |
| `deniz-dotnet-general` | `0.9.1` | `sha256:af9f5595afc68035cad633567be03f6c49fce32f3ade2309164a7e9c64361a01` | `deniz-dotnet-aspire` |
| `deniz-dotnet-akka` | `0.3.1` | `sha256:c7d80c7e5456f65b312dc0c1a1e5fae0e870a5e50e90e5316e526d64983d57a3` | `deniz-dotnet-aspire`, `deniz-dotnet-general` |
| `deniz-dotnet-aspire` | `0.3.3` | `sha256:ad25d730986b2b563b2660082d54c31e4a0280a731af232f07d1156deac536d5` | `[]` |

## Proof limits

- Isolated Plan wrote nothing; Apply-all and status ran in an isolated profile. No real user-profile
  installation was performed.
- `verify:package` and the Package graph check prove each recorded required Module is present in the
  tarball. Isolated Apply-all exercises a complete Selection, not rejection of an incomplete one.
- This proves required-Module presence only. It does not prove version-range resolution, cross-version
  item or API compatibility, or model selection, instruction following, or permission behavior.
- No Package was published and no Release asset was replaced.
- The workflow ZIP digest is not the tgz digest. Same filename as the public 0.3.0 Package does not
  make the files the same.
- The early worker `ECOMPROMISED` cause was not established.
- The workflow artifact expires `2026-10-06T15:42:44Z`.
