---
record_id: opencode-installer-v0.2.0-posix-correction-2026-08-25
date: 2026-08-25
repo_head: 286576c651ee26e2bdf0b075ef1bb2a7541f47c0
kind: runtime-smoke
summary: The public installer-v0.2.0 Package was rebuilt on Linux, replaced after explicit authorization, and verified from the public Release through manifest-backed tar modes, zero-write Plan, Apply, status, and remote byte identity.
isolation_ok: true
source_ref: 8867fc4077494a4bedb32f9f75ea251acbf8b81a
package_name: deniz-agent-skills-0.2.0.tgz
package_size: 902189
package_sha256: 4ce23817052317b80926a6cd0aed7063364e9625c012f22080bfb887727286be
transport: public-release
release_tag: installer-v0.2.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.2.0
release_target: 8867fc4077494a4bedb32f9f75ea251acbf8b81a
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/32861187335
---

# OpenCode installer v0.2.0 POSIX correction

## Scope

This record covers the corrective replacement of the public `installer-v0.2.0` asset. The original
Windows-packed tarball had the correct 336 file paths and bytes but stored every file as mode `0644`.
Seven `deniz-process` files were declared `100755` by the Bundle manifest, so Package verification
failed before Plan on Linux. Windows verification did not observe the defect because the installer
deliberately ignores POSIX mode bits there.

The replacement retained the same Release tag, target commit, Package name, and Package content. It
changed the tar transport modes, Package size, and Package SHA-256. The old asset was unused outside
the review downloads and was replaced only after explicit curator authorization.

## Method

The manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran on GitHub's
Ubuntu runner. It checked out current workflow tools separately from Package source commit `8867fc4`,
then:

1. scanned the source commit's full history with the digest-pinned Gitleaks image;
2. ran tests, typecheck, lint, format check, public-safety, build, inventory, and validate;
3. confirmed two build/inventory rounds left the source checkout clean;
4. packed the source with Linux npm;
5. verified the exact tar payload, Module digests, Bundle file hashes, Package bin mode, and every tar
   mode against the embedded Bundle manifests;
6. ran the exact Package through zero-write Plan, Apply-all, and status in an isolated HOME/XDG/npm
   profile;
7. uploaded the workflow artifact, deleted the old same-named Release asset, and uploaded the verified
   artifact;
8. downloaded the public asset again, compared it byte-for-byte with the workflow artifact, and ran
   the Package verifier again.

Build-only run `32860717331` passed before publication. Publishing run
[`32861187335`](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/32861187335)
passed both build and publish jobs. A separate ephemeral Linux container then downloaded the public
URL and ran `deniz-skills status` successfully with empty Selection, no lock, and no Recovery.

## Package identity

| Field | Observed value |
|---|---|
| Release target | `8867fc4077494a4bedb32f9f75ea251acbf8b81a` |
| Package | `deniz-agent-skills-0.2.0.tgz` |
| Size | 902,189 bytes |
| SHA-256 | `4ce23817052317b80926a6cd0aed7063364e9625c012f22080bfb887727286be` |
| Bundle files whose executable identity was restored | 7 |

The selected Modules exercised by the workflow were the four Bundles emitted at the Release target:

| Module | version | Module digest |
|---|---|---|
| `deniz-dotnet-akka` | `0.2.1` | `sha256:3f2fe3b1d3023075f30fe0ba6c335331f35c9641de406e24318956a9c96b047d` |
| `deniz-dotnet-aspire` | `0.3.1` | `sha256:1a0c713441fb23d1daf8081469c876ebcb5e130902f1259436416983f061d51e` |
| `deniz-dotnet-general` | `0.8.1` | `sha256:08a550d2a9ce274849b5d84fee6da8b5e3424c9ff950fa6c60aee736e010a29c` |
| `deniz-process` | `0.4.1` | `sha256:20e214a70016ecb1b69bfc8620b179a3b2b93124926f95678e96467a10eed06c` |

## Boundaries

- The workflow and independent public-URL check prove Package integrity and installer operation on
  Linux. The prior Windows evidence remains in the 2026-08-18 installer record.
- No model or TUI was run. Model selection, instruction following, parked-body reads, and human
  permission behavior remain unmeasured.
- The test used all Modules. Arbitrary-subset dependency closure remains a separate recorded limit.
- GitHub Release assets remain mutable. The repository-recorded digest detects another replacement;
  it does not prevent one.
