---
record_id: opencode-installer-v0.8.0-2026-10-13
date: 2026-10-13
repo_head: 0ef9d41dc8d3d4b2adeec8409ccb0b6c3a1e7e13
kind: runtime-smoke
summary: Package 0.8.0 carries the schema-2 OpenCode 2 Bundles of Process 0.11.0, General 0.14.0, Akka 0.4.1, and Aspire 0.6.0 with the unchanged 0.5.0 installer, and passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only and a publication run, then an independent draft-asset check and an unauthenticated public re-download on the Windows workstation.
isolation_ok: true
source_ref: 0ef9d41dc8d3d4b2adeec8409ccb0b6c3a1e7e13
package_name: deniz-agent-skills-0.8.0.tgz
package_size: 1021864
package_sha256: f8fdb96562d234d78f08d9506b94fe8e0aade028c735d0615ec94f9c4ddb72c5
transport: public-release
release_tag: installer-v0.8.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.8.0
release_target: 0ef9d41dc8d3d4b2adeec8409ccb0b6c3a1e7e13
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38378409640
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38378079175
---

# OpenCode installer v0.8.0

## Scope

This is a new Package and Release, not a replacement of the 0.7.1 asset. Its Bundles and Install
state stay schema 2. It carries Process 0.11.0: size and authority gates in `writing-plans`,
`subagent-driven-development`, `executing-plans`, and `using-git-worktrees`; process weight as the
agent's judgment with one question only when it is genuinely torn; a resumed plan's executor
recorded in its ledger; one default-branch question before the first commit when no approval
exists; `ask-deniz` as a `both` router over every installed Module, with its .NET, Aspire, and Akka
routes in `references/dotnet.md`; and six `verified-waves` instruction corrections plus a
sequential-is-not-small rule. General 0.14.0, Akka 0.4.1, and Aspire 0.6.0 are unchanged. The
Bundles hold 113 skills (30 `manual`; `ask-deniz` is no longer manual), two subagents, and no commands;
Process ships one more file than in 0.7.1, `ask-deniz`'s `references/dotnet.md`. The Module
requirement graph is unchanged: `ask-deniz`'s .NET routes are optional references, so Process still
requires no other Module. The installer JavaScript under `dist/` is byte-identical to the 0.7.1
source, so this Release changes no installer behavior. The rules are owned by
[distribution and installation](../../../docs/architecture/distribution-and-installation.md) and
[transformation and emission](../../../docs/architecture/transformation-and-emission.md).

## Method

Source commit `0ef9d41` only bumps the Package version and adds a preparation notice to the README
on top of `2770218`, the head of the merged Process 0.11.0 pull request; its
[push CI](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38378050175) passed.
Between the 0.7.1 source `bab5826` and `0ef9d41`, `opencode/` differs only in Process's manifest,
seven changed Process files, and the added `references/dotnet.md`.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38378079175)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=0ef9d41dc8d3d4b2adeec8409ccb0b6c3a1e7e13`,
   `release_tag=installer-v0.8.0`, `publish=false`, and `replace_existing=false`. It scanned the
   source's full history with digest-pinned Gitleaks; ran tests, typecheck, lint, format,
   public-safety, build, inventory, and validate; confirmed two generation rounds leave the source
   clean; packed on Linux; verified the exact payload, Bundle hashes and digests, and tar executable
   modes; and ran the Package with an isolated HOME/XDG/npm profile and `OPENCODE_CONFIG_DIR` unset:
   zero-write Plan, Apply-all, and status.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows workstation.
3. A draft Release targeting the exact source commit was created with no assets.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/38378409640)
   repeated step 1 with `publish=true`, then uploaded the verified artifact to the draft Release,
   downloaded it again, compared bytes, and reran the Package verifier. Both jobs succeeded.
5. The draft asset, downloaded independently, matched the SHA-256, was byte-identical to the
   candidate artifact, and passed the Package verifier.
6. The draft was published. The tag `installer-v0.8.0` resolves to the source commit; asset ID
   `636578645` holds `deniz-agent-skills-0.8.0.tgz`, 1,021,864 bytes, with the SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,021,864 bytes and SHA-256
   and passed the Package verifier.

## Modules

Every Module digest in the tarball equals its `opencode/<module>/manifest.json` at the source
commit. General's, Akka's, and Aspire's versions and digests are unchanged from 0.7.1: none of their
bytes moved in any harness.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.11.0` | `sha256:fd246032b77e85f165f252fbdb99d4abe59e801853de9203d96699009f016117` |
| `deniz-dotnet-general` | `0.14.0` | `sha256:6d06970b0f982357819c273b445f92b26b904365928539c113adcc6469bb8903` |
| `deniz-dotnet-akka` | `0.4.1` | `sha256:f5fb32c8341258fc72e21a0cd24a8d8cc14cc201116f130cc93d8b941a10e7ad` |
| `deniz-dotnet-aspire` | `0.6.0` | `sha256:f3d3d8f8545f35cb1d04784b602f75f9df64773f81757c108021bca76e2eea95` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot. It contains a 0.8.0 preparation notice,
the then-current verified 0.7.1 recipe, and a link to the live repository recipe. The Package digest
can only be recorded after packing, so the post-publication README, the `tools/repository-docs.test.ts`
pins, the workflow's `release_tag` default, and this record live in a later commit and were not
repacked into the asset.

The previous [0.7.1 record](2026-10-12-opencode-installer-v0.7.1.md) remains historical evidence.
Its asset ID `633289583`, 1,015,883 bytes, and SHA-256
`8cbb8a455ee22bd029b264bafa4b4ab104f0ff98f6c469c397a56c6e2efe4e88` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The Windows checks establish digest identity and Package verification,
  not POSIX execution there.
- No OpenCode process, model run, or TUI was part of this proof. The Process 0.11.0 behavior was
  checked before release only by structural tests and a short interpretation probe (fresh-context
  agents reading the emitted Claude bodies), not by harness invocation measurements.
- This record covers no real-profile installation or update, and no update from an existing 0.7.1
  Install state.
- Fresh all-Module installation does not prove arbitrary-subset Selection, every update or remove
  scenario, or upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable; the recorded digest detects replacement but cannot prevent it.
