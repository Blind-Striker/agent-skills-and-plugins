---
record_id: opencode-installer-v0.13.0-2026-10-22
date: 2026-10-22
repo_head: 904dc68bb762ad0ec74262f5fc32f15980ec3916
kind: runtime-smoke
summary: Package 0.13.0 carries the schema-2 OpenCode 2 Bundles of Process 0.14.0, General 0.14.1, Akka 0.4.1, Aspire 0.6.0, and the first Coordination 0.1.0 with the unchanged installer, and passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only and a publication run, then an independent draft-asset check and an unauthenticated public re-download on the Windows workstation.
isolation_ok: true
source_ref: 904dc68bb762ad0ec74262f5fc32f15980ec3916
package_name: deniz-agent-skills-0.13.0.tgz
package_size: 1052296
package_sha256: 82f8ba5e4b4f144737ecce2b66f43b9890c3002ada97102f0343f58d85297177
transport: public-release
release_tag: installer-v0.13.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.13.0
release_target: 904dc68bb762ad0ec74262f5fc32f15980ec3916
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/39843006845
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/39842391187
---

# OpenCode installer v0.13.0

## Scope

This is a new Package and Release, not a replacement of the 0.12.0 asset. Its Bundles and Install
state stay schema 2. It adds the Coordination Module, `deniz-coordination` 0.1.0
([ADR-0009](../../../docs/adr/0009-coordination-ships-in-its-own-module.md)), which requires
Process. Its OpenCode Bundle holds `coordinator`, `delegating-to-worker-models`, and
`verified-waves`. The `harness` skill ships only to Claude Code and Codex
([ADR-0010](../../../docs/adr/0010-per-harness-item-emission.md)), so no OpenCode Bundle carries
its .NET source. Process 0.14.0 no longer holds `delegating-to-worker-models` or `verified-waves`.
Its `requesting-code-review` and `writing-plans` also changed. General 0.14.1, Akka 0.4.1, and
Aspire 0.6.0 are unchanged.

The Bundles hold 119 skills (34 `manual`), two subagents, and no commands. The Module requirement
graph gains one edge: Coordination requires Process. The installer JavaScript under `dist/` is
byte-identical to the 0.12.0 source, so this Release changes no installer behavior. The rules are
owned by [distribution and installation](../../../docs/architecture/distribution-and-installation.md)
and [transformation and emission](../../../docs/architecture/transformation-and-emission.md).

## Method

Source commit `904dc68` is the merge of pull request #2. Its tree is identical to `77320d2`, the
"prepare Package 0.13.0" commit, which bumps the Package version and adds a preparation notice to
the README. Between the 0.12.0 source `63b29a5` and `904dc68`, `dist/` does not differ.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/39842391187)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=904dc68bb762ad0ec74262f5fc32f15980ec3916`,
   `release_tag=installer-v0.13.0`, `publish=false`, and `replace_existing=false`. It ran these
   gates, each of which passed:
   - It scanned the source's full history with digest-pinned Gitleaks.
   - It ran tests, typecheck, lint, format, public-safety, build, inventory, and validate.
   - It confirmed that two generation rounds leave the source clean.
   - It packed the Package on Linux, then verified the exact payload, the Bundle hashes and
     digests, and the tar executable modes.
   - It ran the Package with an isolated HOME/XDG/npm profile and `OPENCODE_CONFIG_DIR` unset:
     zero-write Plan, Apply-all, and status.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows workstation. Its
   SHA-256 is the one recorded above. The run's step summary is not exposed through the API, so the
   identity check is the byte comparison in step 5.
3. A draft Release targeting the exact source commit was created with no assets and the release
   notes as its body.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/39843006845)
   repeated step 1 with `publish=true`. It then uploaded the verified artifact to the draft Release,
   downloaded it again, compared the bytes, and reran the Package verifier. Both jobs succeeded.
5. The draft asset, downloaded independently, was byte-identical to the candidate artifact and
   passed the Package verifier.
6. The draft was published. The tag `installer-v0.13.0` resolves to the source commit. Asset ID
   `666906688` holds `deniz-agent-skills-0.13.0.tgz`, 1,052,296 bytes, with the SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,052,296 bytes and SHA-256
   and passed the Package verifier.

## Modules

Every Module digest in the tarball equals its `opencode/<module>/manifest.json` at the source
commit. General's, Akka's, and Aspire's versions and digests are unchanged from 0.12.0.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.14.0` | `sha256:f90fdded794301bcc88d4118ed1eaf3057200c107d628e078ebd9cb69a5ec49e` |
| `deniz-dotnet-general` | `0.14.1` | `sha256:92b2e9409b9fe2c8813ab79ffe0da0592db5a90471ec6d52f7d590193285253e` |
| `deniz-dotnet-akka` | `0.4.1` | `sha256:f5fb32c8341258fc72e21a0cd24a8d8cc14cc201116f130cc93d8b941a10e7ad` |
| `deniz-dotnet-aspire` | `0.6.0` | `sha256:f3d3d8f8545f35cb1d04784b602f75f9df64773f81757c108021bca76e2eea95` |
| `deniz-coordination` | `0.1.0` | `sha256:afa615f9344a24034179b20df90aab1e3aa62ba3e55a5a8cee91c4fb01da54e8` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot. It contains three things: a 0.13.0
preparation notice, the then-current verified 0.12.0 recipe, and a link to the live repository
recipe. The Package digest can only be recorded after packing. So a later commit holds the
post-publication README, the `tools/repository-docs.test.ts` pins, the workflow's `release_tag`
default, and this record, and none of them was repacked into the asset.

The previous [0.12.0 record](2026-10-21-opencode-installer-v0.12.0.md) remains historical evidence.
Its asset ID `661387093`, 1,046,209 bytes, and SHA-256
`200f8b576eab3aa3ff526ffdab15c6d5da58e72b831bb72542c01fa3dba24b62` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The Windows checks establish digest identity and Package verification,
  not POSIX execution there.
- No OpenCode process, model run, or TUI was part of this proof. Neither the Coordination skills nor
  the Process changes were exercised by a model here.
- This record covers no real-profile installation or update, no update from an existing 0.12.0
  Install state, and no Selection that moves the two skills from Process to Coordination.
- Fresh all-Module installation does not prove arbitrary-subset Selection, every update or remove
  scenario, or upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable; the recorded digest detects replacement but cannot prevent it.
