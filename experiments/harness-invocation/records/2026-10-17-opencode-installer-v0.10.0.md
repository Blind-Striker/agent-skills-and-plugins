---
record_id: opencode-installer-v0.10.0-2026-10-17
date: 2026-10-17
repo_head: f42772bd25d226bbb6d0f8de53a53800f3bd8f5a
kind: runtime-smoke
summary: Package 0.10.0 carries the schema-2 OpenCode 2 Bundles of Process 0.12.2, General 0.14.1, Akka 0.4.1, and Aspire 0.6.0 with the unchanged 0.5.0 installer, and passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only and a publication run, then an independent draft-asset check and an unauthenticated public re-download on the Windows workstation.
isolation_ok: true
source_ref: f42772bd25d226bbb6d0f8de53a53800f3bd8f5a
package_name: deniz-agent-skills-0.10.0.tgz
package_size: 1037775
package_sha256: 83f425c4f9999b3f5a4e926808e2245e2efd21a6568017f5e1bae8e792576698
transport: public-release
release_tag: installer-v0.10.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.10.0
release_target: f42772bd25d226bbb6d0f8de53a53800f3bd8f5a
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/39041524020
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/39041340125
---

# OpenCode installer v0.10.0

## Scope

This is a new Package and Release, not a replacement of the 0.9.0 asset. Its Bundles and Install
state stay schema 2. It carries Process 0.12.2, whose only change from 0.12.1 is
`delegating-to-worker-models`' `references/prompt-template.md`: an optional slot that names
pre-approved follow-on changes as part of the plan, and coordinator checklist items for measured
budget caps and pre-approval of foreseeable mechanical fallout. General 0.14.1, Akka 0.4.1, and
Aspire 0.6.0 are unchanged. The Bundles hold 114 skills (30 `manual`), two subagents, and no
commands. The Module requirement graph is unchanged. The installer JavaScript under `dist/` is
byte-identical to the 0.9.0 source, so this Release changes no installer behavior. The rules are
owned by [distribution and installation](../../../docs/architecture/distribution-and-installation.md)
and [transformation and emission](../../../docs/architecture/transformation-and-emission.md).

## Method

Source commit `f42772b` only bumps the Package version and adds a preparation notice to the README
on top of `0dc4fbd`. Between the 0.9.0 source `ef1d3a1` and `f42772b`, `opencode/` differs only in
Process's manifest and that one reference file.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/39041340125)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=f42772bd25d226bbb6d0f8de53a53800f3bd8f5a`,
   `release_tag=installer-v0.10.0`, `publish=false`, and `replace_existing=false`. It scanned the
   source's full history with digest-pinned Gitleaks; ran tests, typecheck, lint, format,
   public-safety, build, inventory, and validate; confirmed two generation rounds leave the source
   clean; packed on Linux; verified the exact payload, Bundle hashes and digests, and tar executable
   modes; and ran the Package with an isolated HOME/XDG/npm profile and `OPENCODE_CONFIG_DIR` unset:
   zero-write Plan, Apply-all, and status.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows workstation, and
   its SHA-256 matched the run's recorded identity.
3. A draft Release targeting the exact source commit was created with no assets.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/39041524020)
   repeated step 1 with `publish=true`, then uploaded the verified artifact to the draft Release,
   downloaded it again, compared bytes, and reran the Package verifier. Both jobs succeeded.
5. The draft asset, downloaded independently, was byte-identical to the candidate artifact and
   passed the Package verifier.
6. The draft was published. The tag `installer-v0.10.0` resolves to the source commit; asset ID
   `650706660` holds `deniz-agent-skills-0.10.0.tgz`, 1,037,775 bytes, with the SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,037,775 bytes and SHA-256
   and passed the Package verifier.

## Modules

Every Module digest in the tarball equals its `opencode/<module>/manifest.json` at the source
commit. General's, Akka's, and Aspire's versions and digests are unchanged from 0.9.0: none of their
bytes moved in any harness.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.12.2` | `sha256:8dece05d2e7cba425df5e9b3b6276f367d1cf52293d411f0b580331018e48610` |
| `deniz-dotnet-general` | `0.14.1` | `sha256:92b2e9409b9fe2c8813ab79ffe0da0592db5a90471ec6d52f7d590193285253e` |
| `deniz-dotnet-akka` | `0.4.1` | `sha256:f5fb32c8341258fc72e21a0cd24a8d8cc14cc201116f130cc93d8b941a10e7ad` |
| `deniz-dotnet-aspire` | `0.6.0` | `sha256:f3d3d8f8545f35cb1d04784b602f75f9df64773f81757c108021bca76e2eea95` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot. It contains a 0.10.0 preparation
notice, the then-current verified 0.9.0 recipe, and a link to the live repository recipe. The
Package digest can only be recorded after packing, so the post-publication README, the
`tools/repository-docs.test.ts` pins, the workflow's `release_tag` default, and this record live in
a later commit and were not repacked into the asset.

The previous [0.9.0 record](2026-10-17-opencode-installer-v0.9.0.md) remains historical evidence.
Its asset ID `650602008`, 1,037,571 bytes, and SHA-256
`fdf1bc1111f1e02879c484a754c9606eaa82c18f86b39cca3de23f7b8339da03` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The Windows checks establish digest identity and Package verification,
  not POSIX execution there.
- No OpenCode process, model run, or TUI was part of this proof. The Process 0.12.2 template change
  was checked only by structural tests and the pilot runs that motivated it.
- This record covers no real-profile installation or update, and no update from an existing 0.9.0
  Install state.
- Fresh all-Module installation does not prove arbitrary-subset Selection, every update or remove
  scenario, or upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable; the recorded digest detects replacement but cannot prevent it.
