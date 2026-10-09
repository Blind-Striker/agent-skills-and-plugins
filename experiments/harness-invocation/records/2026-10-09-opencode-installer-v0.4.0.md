---
record_id: opencode-installer-v0.4.0-2026-10-09
date: 2026-10-09
repo_head: a21615e3e7485b3a218f849173f3c91018e38c1f
kind: runtime-smoke
summary: Package 0.4.0 carries the schema-2 OpenCode 2 Bundles of Process 0.7.0, General 0.10.0, Akka 0.4.0, and Aspire 0.4.0 and passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only and a publication run, then an unauthenticated public re-download and a PowerShell README-recipe Plan/Apply/status into an isolated OPENCODE_CONFIG_DIR on the Windows workstation.
isolation_ok: true
source_ref: a21615e3e7485b3a218f849173f3c91018e38c1f
package_name: deniz-agent-skills-0.4.0.tgz
package_size: 885480
package_sha256: 5108a3ee3673196891644370bb92f538743cdcafad57db7876d8a40f0bf95dce
transport: public-release
release_tag: installer-v0.4.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.4.0
release_target: a21615e3e7485b3a218f849173f3c91018e38c1f
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/37979151451
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/37978787182
---

# OpenCode installer v0.4.0

## Scope

This is a new Package and Release, not a replacement of the 0.3.0 asset. It is the first Release
whose Bundles and Install state are schema 2 and whose Bundles carry OpenCode 2 output: one artifact
per item at its `<plugin>.<name>` ID, `manual` skills hidden with `opencode/autoinvoke: false`, and
no command stubs or parked bodies. It also ships dependency-aware Module Selection (presence-only),
the `OPENCODE_CONFIG_DIR` Destination rule, and the post-Apply warning on Windows for
`anomalyco/opencode#47505`. The rules are owned by
[distribution and installation](../../../docs/architecture/distribution-and-installation.md) and
[transformation and emission](../../../docs/architecture/transformation-and-emission.md).

## Method

Source commit `a21615e` only bumps the Package version and adds a preparation notice to the README
on top of `5b36879`; its [push CI](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/37978778846)
passed. Its `opencode/` and `dist/` trees are byte-identical to `aa2de1e`, the checkout installed by
the [profile migration](2026-10-09-opencode2-profile-migration.md).

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/37978787182)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=a21615e3e7485b3a218f849173f3c91018e38c1f`,
   `release_tag=installer-v0.4.0`, `publish=false`, and `replace_existing=false`. It scanned the
   source's full history with digest-pinned Gitleaks; ran tests, typecheck, lint, format,
   public-safety, build, inventory, and validate; confirmed two generation rounds leave the source
   clean; packed on Linux; verified the exact payload, Bundle hashes and digests, and tar executable
   modes; and ran the Package with an isolated HOME/XDG/npm profile and `OPENCODE_CONFIG_DIR` unset:
   zero-write Plan, Apply-all, and status.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows workstation.
3. A draft Release targeting the exact source commit was created with no assets.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/37979151451)
   repeated step 1 with `publish=true`, then uploaded the verified artifact to the draft Release,
   downloaded it again, compared bytes, and reran the Package verifier. Both jobs succeeded. It built
   the same size and SHA-256 as the candidate run.
5. The draft asset, downloaded independently, matched the SHA-256 and passed the Package verifier.
6. The draft was published. The tag `installer-v0.4.0` resolves to the source commit; asset ID
   `625929610` holds `deniz-agent-skills-0.4.0.tgz`, 885,480 bytes, with the SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 885,480 bytes and SHA-256
   and passed the Package verifier.

## README recipe in PowerShell

On the Windows workstation, PowerShell 7.6 with `npm` resolving to the `npm.ps1` shim ran the
README's PowerShell block verbatim, with `TEMP`, the npm cache, and `OPENCODE_CONFIG_DIR` pointing
into a fresh lab folder. The digest check passed, the Plan and the Apply both printed the lab folder
as the Destination, the Apply ended with the Windows watcher warning, and `status` listed all four
Modules `current` with no lock and no Recovery. The lab held schema-2 Install state with 285 owned
files, 115 skill folders, two agents, and no `commands/` folder. A separate Plan-only run against an
unused `OPENCODE_CONFIG_DIR` exited 0 and did not create that folder. The checkout installer's
`status` against the lab also listed all four Modules `current`. The SHA-256 of the real profile's
Install state was the same before and after.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.7.0` | `sha256:a902e274d69a5caee7b3f3d7cc40c224c50c8885cae40d850f075aee1951068f` |
| `deniz-dotnet-general` | `0.10.0` | `sha256:80688637e4230445d04326d05fd789b9e2c3ec77ccb03e3830e79208aa59a4d2` |
| `deniz-dotnet-akka` | `0.4.0` | `sha256:f5fb32c8341258fc72e21a0cd24a8d8cc14cc201116f130cc93d8b941a10e7ad` |
| `deniz-dotnet-aspire` | `0.4.0` | `sha256:6f4d92ddad0e37cc0eee0ae63e1e03e1cdb1b88877002bb6060dba0f7f746ab5` |

The recipe quotes the `'--'` after the npm options. A probe on the same machine showed why: through
`npm.ps1`, a bare `--` is consumed by PowerShell, so the packaged command received only `install`
while `--all --yes` went to npm; with `'--'` quoted, or through `npm.cmd`, it received
`install --all --yes`. The checkout's `npm run <script> -- ...` form behaves the same way. The
[profile migration record](2026-10-09-opencode2-profile-migration.md) holds the original finding.

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot. It contains a 0.4.0 preparation notice,
the then-current verified 0.3.0 recipe, and a link to the live repository recipe. The Package digest
can only be recorded after packing, so the post-publication README, the `tools/repository-docs.test.ts`
pins, the workflow's `release_tag` default, and this record live in a later commit and were not
repacked into the asset.

The previous [0.3.0 record](2026-09-06-opencode-installer-v0.3.0.md) remains historical evidence.
Its asset ID `546864656`, 924,792 bytes, and SHA-256
`a6e5c309cd4739684d908c9bae224941272c57471f278b9a738dac53f704ef22` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The Windows recipe run establishes the PowerShell recipe and the
  `OPENCODE_CONFIG_DIR` Destination on one machine, not POSIX execution there.
- No OpenCode process, model run, or TUI was part of this proof. OpenCode 2 discovery is measured in
  the [discovery record](2026-10-09-opencode2-discovery.md), not repeated here.
- The real profiles were not installed from this Package. They hold the same Bundle bytes from the
  checkout through the [profile migration](2026-10-09-opencode2-profile-migration.md).
- Fresh all-Module installation does not prove arbitrary-subset Selection, every update or remove
  scenario, or upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable; the recorded digest detects replacement but cannot prevent it.
