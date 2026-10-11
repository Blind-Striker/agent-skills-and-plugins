---
record_id: opencode-installer-v0.6.0-2026-10-12
date: 2026-10-12
repo_head: a484e020404f21eaacd0e2467eec459768199851
kind: runtime-smoke
summary: Package 0.6.0 carries the schema-2 OpenCode 2 Bundles of Process 0.8.1, General 0.13.0, Akka 0.4.1, and Aspire 0.6.0 with the unchanged 0.5.0 installer, and passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only and a publication run, then an independent draft-asset check and an unauthenticated public re-download on the Windows workstation.
isolation_ok: true
source_ref: a484e020404f21eaacd0e2467eec459768199851
package_name: deniz-agent-skills-0.6.0.tgz
package_size: 1009355
package_sha256: 03001500a35faed88532f5c654c42a34a5f0322a87e29bc83ce99cb15dcbfd8c
transport: public-release
release_tag: installer-v0.6.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.6.0
release_target: a484e020404f21eaacd0e2467eec459768199851
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38176017586
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38175738473
---

# OpenCode installer v0.6.0

## Scope

This is a new Package and Release, not a replacement of the 0.5.0 asset. Its Bundles and Install
state stay schema 2. It carries the Module output after the dotnet-skills `v1.7.1` sync and the
fixes of the first curation sanity panel: Process 0.8.1, General 0.13.0, Akka 0.4.1, and Aspire
0.6.0, with 112 skills (30 `manual`), two subagents, no commands, and 327 owned files. The extra
owned file is `sdk-container-publishing`'s real-world examples in General. The Module requirement
graph is unchanged. The installer JavaScript under `dist/` is byte-identical to the 0.5.0 source, so
this Release changes no installer behavior. The rules are owned by
[distribution and installation](../../../docs/architecture/distribution-and-installation.md) and
[transformation and emission](../../../docs/architecture/transformation-and-emission.md).

## Method

Source commit `a484e02` only bumps the Package version and adds a preparation notice to the README
on top of `04b3969`; its [push CI](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38175588515)
passed. Its `opencode/` and `dist/` trees are byte-identical to `04b3969`, whose
[push CI](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38174995457) also
passed.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38175738473)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=a484e020404f21eaacd0e2467eec459768199851`,
   `release_tag=installer-v0.6.0`, `publish=false`, and `replace_existing=false`. It scanned the
   source's full history with digest-pinned Gitleaks; ran tests, typecheck, lint, format,
   public-safety, build, inventory, and validate; confirmed two generation rounds leave the source
   clean; packed on Linux; verified the exact payload, Bundle hashes and digests, and tar executable
   modes; and ran the Package with an isolated HOME/XDG/npm profile and `OPENCODE_CONFIG_DIR` unset:
   zero-write Plan, Apply-all, and status.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows workstation.
3. A draft Release targeting the exact source commit was created with no assets.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38176017586)
   repeated step 1 with `publish=true`, then uploaded the verified artifact to the draft Release,
   downloaded it again, compared bytes, and reran the Package verifier. Both jobs succeeded. It built
   the same size and SHA-256 as the candidate run.
5. The draft asset, downloaded independently, matched the SHA-256, was byte-identical to the
   candidate artifact, and passed the Package verifier.
6. The draft was published. The tag `installer-v0.6.0` resolves to the source commit; asset ID
   `631840474` holds `deniz-agent-skills-0.6.0.tgz`, 1,009,355 bytes, with the SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,009,355 bytes and SHA-256
   and passed the Package verifier.

## Modules

Every Module digest in the tarball equals its `opencode/<module>/manifest.json` at the source
commit. Akka's version and digest are unchanged from 0.5.0: no Akka byte moved in any harness.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.8.1` | `sha256:72efa399de431e465ad493fbe2fea025032d35dc1642238df85663f5acff5c63` |
| `deniz-dotnet-general` | `0.13.0` | `sha256:c22aa95c68cb6f33dede5773c0bc75e761e134bba0dbe13102fbdc406adffde9` |
| `deniz-dotnet-akka` | `0.4.1` | `sha256:f5fb32c8341258fc72e21a0cd24a8d8cc14cc201116f130cc93d8b941a10e7ad` |
| `deniz-dotnet-aspire` | `0.6.0` | `sha256:f3d3d8f8545f35cb1d04784b602f75f9df64773f81757c108021bca76e2eea95` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot. It contains a 0.6.0 preparation notice,
the then-current verified 0.5.0 recipe, and a link to the live repository recipe. The Package digest
can only be recorded after packing, so the post-publication README, the `tools/repository-docs.test.ts`
pins, the workflow's `release_tag` default, and this record live in a later commit and were not
repacked into the asset.

The previous [0.5.0 record](2026-10-11-opencode-installer-v0.5.0.md) remains historical evidence.
Its asset ID `631062069`, 1,005,203 bytes, and SHA-256
`b61f3ac1a867082850c8d7c35b383aaad3155ec3724ac98529822c2de12e51fb` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The Windows checks establish digest identity and Package verification,
  not POSIX execution there.
- No OpenCode process, model run, or TUI was part of this proof.
- This record covers no real-profile installation or update, and no update from an existing 0.5.0
  Install state.
- Fresh all-Module installation does not prove arbitrary-subset Selection, every update or remove
  scenario, or upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable; the recorded digest detects replacement but cannot prevent it.
