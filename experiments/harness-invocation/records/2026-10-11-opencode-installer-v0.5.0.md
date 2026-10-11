---
record_id: opencode-installer-v0.5.0-2026-10-11
date: 2026-10-11
repo_head: 1d10fa191bca50b23c9810d08d343ff18f674645
kind: runtime-smoke
summary: Package 0.5.0 carries the schema-2 OpenCode 2 Bundles of Process 0.8.0, General 0.12.0, Akka 0.4.1, and Aspire 0.5.1 plus the Windows rename retry, and passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only and a publication run, then an independent draft-asset check and an unauthenticated public re-download on the Windows workstation.
isolation_ok: true
source_ref: 1d10fa191bca50b23c9810d08d343ff18f674645
package_name: deniz-agent-skills-0.5.0.tgz
package_size: 1005203
package_sha256: b61f3ac1a867082850c8d7c35b383aaad3155ec3724ac98529822c2de12e51fb
transport: public-release
release_tag: installer-v0.5.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.5.0
release_target: 1d10fa191bca50b23c9810d08d343ff18f674645
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38150472334
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38150228218
---

# OpenCode installer v0.5.0

## Scope

This is a new Package and Release, not a replacement of the 0.4.0 asset. Its Bundles and Install
state stay schema 2. It carries the Module output after the upstream sync waves: Process 0.8.0,
General 0.12.0, Akka 0.4.1, and Aspire 0.5.1, with 112 skills (30 `manual`), two subagents, no
commands, and 326 owned files. General now records Process as a required Module beside Aspire. The
installer gains one behavior: on Windows every lock and transaction rename retries a transient
`EPERM`, `EACCES`, or `EBUSY` for about one second while the destination is absent. The rules are
owned by [distribution and installation](../../../docs/architecture/distribution-and-installation.md)
and [transformation and emission](../../../docs/architecture/transformation-and-emission.md).

## Method

Source commit `1d10fa1` only bumps the Package version and adds a preparation notice to the README
on top of `e6aceae`; its [push CI](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38150094831)
passed. Its `opencode/` and `dist/` trees are byte-identical to `e6aceae`, whose
[push CI](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38149413690) also
passed.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38150228218)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=1d10fa191bca50b23c9810d08d343ff18f674645`,
   `release_tag=installer-v0.5.0`, `publish=false`, and `replace_existing=false`. It scanned the
   source's full history with digest-pinned Gitleaks; ran tests, typecheck, lint, format,
   public-safety, build, inventory, and validate; confirmed two generation rounds leave the source
   clean; packed on Linux; verified the exact payload, Bundle hashes and digests, and tar executable
   modes; and ran the Package with an isolated HOME/XDG/npm profile and `OPENCODE_CONFIG_DIR` unset:
   zero-write Plan, Apply-all, and status.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows workstation.
3. A draft Release targeting the exact source commit was created with no assets.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38150472334)
   repeated step 1 with `publish=true`, then uploaded the verified artifact to the draft Release,
   downloaded it again, compared bytes, and reran the Package verifier. Both jobs succeeded. It built
   the same size and SHA-256 as the candidate run.
5. The draft asset, downloaded independently, matched the SHA-256, was byte-identical to the
   candidate artifact, and passed the Package verifier.
6. The draft was published. The tag `installer-v0.5.0` resolves to the source commit; asset ID
   `631062069` holds `deniz-agent-skills-0.5.0.tgz`, 1,005,203 bytes, with the SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,005,203 bytes and SHA-256
   and passed the Package verifier.

## Modules

Every Module digest equals its `opencode/<module>/manifest.json` at the source commit. Akka's
digest is unchanged from 0.4.0: its OpenCode bytes did not move, and its version moved with its
Claude Code and Codex output.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.8.0` | `sha256:73afadffedf575940b72a266bf59312b1ee4135683302e80eb276f185ee78b3b` |
| `deniz-dotnet-general` | `0.12.0` | `sha256:eb941b7299c799b8b9922c080a8b353073040b57f9fdb7b53788f993ede45292` |
| `deniz-dotnet-akka` | `0.4.1` | `sha256:f5fb32c8341258fc72e21a0cd24a8d8cc14cc201116f130cc93d8b941a10e7ad` |
| `deniz-dotnet-aspire` | `0.5.1` | `sha256:f36d6607ef28788471c75892264371541dc155d2d95325bae817ab8e5e1d270c` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot. It contains a 0.5.0 preparation notice,
the then-current verified 0.4.0 recipe, and a link to the live repository recipe. The Package digest
can only be recorded after packing, so the post-publication README, the `tools/repository-docs.test.ts`
pins, the workflow's `release_tag` default, and this record live in a later commit and were not
repacked into the asset.

The previous [0.4.0 record](2026-10-09-opencode-installer-v0.4.0.md) remains historical evidence.
Its asset ID `625929610`, 885,480 bytes, and SHA-256
`5108a3ee3673196891644370bb92f538743cdcafad57db7876d8a40f0bf95dce` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The Windows checks establish digest identity and Package verification,
  not POSIX execution there.
- No OpenCode process, model run, or TUI was part of this proof.
- This record covers no real-profile installation or update, and no update from an existing 0.4.0
  Install state.
- The Windows rename retry is established by source tests with an injected rename host, not by a
  reproduced transient handle in this run.
- Fresh all-Module installation does not prove arbitrary-subset Selection, every update or remove
  scenario, or upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable; the recorded digest detects replacement but cannot prevent it.
