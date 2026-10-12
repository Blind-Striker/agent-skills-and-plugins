---
record_id: opencode-installer-v0.7.1-2026-10-12
date: 2026-10-12
repo_head: bab5826c44999b873e7a8554ce5df07e6b889d87
kind: runtime-smoke
summary: Package 0.7.1 carries the schema-2 OpenCode 2 Bundles of Process 0.10.1, General 0.14.0, Akka 0.4.1, and Aspire 0.6.0 with the unchanged 0.5.0 installer, and passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only and a publication run, then an independent draft-asset check and an unauthenticated public re-download on the Windows workstation.
isolation_ok: true
source_ref: bab5826c44999b873e7a8554ce5df07e6b889d87
package_name: deniz-agent-skills-0.7.1.tgz
package_size: 1015883
package_sha256: 8cbb8a455ee22bd029b264bafa4b4ab104f0ff98f6c469c397a56c6e2efe4e88
transport: public-release
release_tag: installer-v0.7.1
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.7.1
release_target: bab5826c44999b873e7a8554ce5df07e6b889d87
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38229335635
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38228605957
---

# OpenCode installer v0.7.1

## Scope

This is a new Package and Release, not a replacement of the 0.7.0 asset. Its Bundles and Install
state stay schema 2. It carries the Process patch 0.10.1, which changes only `verified-waves`'s
`SKILL.md` and `references/harness-tools.md`: a restart dispatches a still-running item again as a
new child after it inspects the item's write set, a check that the sandbox refuses is BLOCKED and
never FAIL, the model ends its turn after a yes/no question, and the parent never does an item's
work. General 0.14.0, Akka 0.4.1, and Aspire 0.6.0 are unchanged. The Bundles hold 113 skills (31
`manual`), two subagents, no commands, and 329 owned files, as in 0.7.0. The Module requirement
graph is unchanged. The installer JavaScript under `dist/` is byte-identical to the 0.7.0 source,
so this Release changes no installer behavior. The rules are owned by
[distribution and installation](../../../docs/architecture/distribution-and-installation.md) and
[transformation and emission](../../../docs/architecture/transformation-and-emission.md).

## Method

Source commit `bab5826` only bumps the Package version and adds a preparation notice to the README
on top of `bdee06a`; its [push CI](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38228388583)
passed. Its `opencode/` and `dist/` trees are byte-identical to `bdee06a`, whose
[push CI](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38227512458) also
passed. Between the 0.7.0 source `d73d521` and `bab5826`, `opencode/` differs only in Process's
manifest and the two changed `verified-waves` files.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38228605957)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=bab5826c44999b873e7a8554ce5df07e6b889d87`,
   `release_tag=installer-v0.7.1`, `publish=false`, and `replace_existing=false`. It scanned the
   source's full history with digest-pinned Gitleaks; ran tests, typecheck, lint, format,
   public-safety, build, inventory, and validate; confirmed two generation rounds leave the source
   clean; packed on Linux; verified the exact payload, Bundle hashes and digests, and tar executable
   modes; and ran the Package with an isolated HOME/XDG/npm profile and `OPENCODE_CONFIG_DIR` unset:
   zero-write Plan, Apply-all, and status.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows workstation.
3. A draft Release targeting the exact source commit was created with no assets.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38229335635)
   repeated step 1 with `publish=true`, then uploaded the verified artifact to the draft Release,
   downloaded it again, compared bytes, and reran the Package verifier. Both jobs succeeded. It built
   the same size and SHA-256 as the candidate run.
5. The draft asset, downloaded independently, matched the SHA-256, was byte-identical to the
   candidate artifact, and passed the Package verifier.
6. The draft was published. The tag `installer-v0.7.1` resolves to the source commit; asset ID
   `633289583` holds `deniz-agent-skills-0.7.1.tgz`, 1,015,883 bytes, with the SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,015,883 bytes and SHA-256
   and passed the Package verifier.

## Modules

Every Module digest in the tarball equals its `opencode/<module>/manifest.json` at the source
commit. General's, Akka's, and Aspire's versions and digests are unchanged from 0.7.0: none of their
bytes moved in any harness.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.10.1` | `sha256:6e2cc45defa3957ffc5ac75e236a675aea7dd9f6e860fb557208d22451eb4e69` |
| `deniz-dotnet-general` | `0.14.0` | `sha256:6d06970b0f982357819c273b445f92b26b904365928539c113adcc6469bb8903` |
| `deniz-dotnet-akka` | `0.4.1` | `sha256:f5fb32c8341258fc72e21a0cd24a8d8cc14cc201116f130cc93d8b941a10e7ad` |
| `deniz-dotnet-aspire` | `0.6.0` | `sha256:f3d3d8f8545f35cb1d04784b602f75f9df64773f81757c108021bca76e2eea95` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot. It contains a 0.7.1 preparation notice,
the then-current verified 0.7.0 recipe, and a link to the live repository recipe. The Package digest
can only be recorded after packing, so the post-publication README, the `tools/repository-docs.test.ts`
pins, the workflow's `release_tag` default, and this record live in a later commit and were not
repacked into the asset.

The previous [0.7.0 record](2026-10-12-opencode-installer-v0.7.0.md) remains historical evidence.
Its asset ID `633108042`, 1,015,224 bytes, and SHA-256
`d0c36fd0b01631da062b6f71c61401e1161bcdea9a25fb8823105978f8b0967b` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The Windows checks establish digest identity and Package verification,
  not POSIX execution there.
- No OpenCode process, model run, or TUI was part of this proof.
- This record covers no real-profile installation or update, and no update from an existing 0.7.0
  Install state.
- Fresh all-Module installation does not prove arbitrary-subset Selection, every update or remove
  scenario, or upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable; the recorded digest detects replacement but cannot prevent it.
