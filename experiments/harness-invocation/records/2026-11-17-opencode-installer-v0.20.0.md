---
record_id: opencode-installer-v0.20.0-2026-11-17
date: 2026-11-17
repo_head: e4bd7b089988e1ea25a2eb710b1ec7f07aa668ad
kind: runtime-smoke
summary: Package 0.20.0 carries the schema-2 OpenCode 2 Bundles of Process 0.15.5, General 0.19.0, Akka 0.5.0, Aspire 0.7.2, and Coordination 0.3.0 with the unchanged installer. It passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only run and in a publication run. On the Windows host it then passed an independent draft-asset check and an unauthenticated public re-download.
isolation_ok: true
source_ref: e4bd7b089988e1ea25a2eb710b1ec7f07aa668ad
package_name: deniz-agent-skills-0.20.0.tgz
package_size: 1219421
package_sha256: a14010d1e0c68472bd510c050df10ac80704f8bda0c876a3c43b2cd1ad3bbce6
transport: public-release
release_tag: installer-v0.20.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.20.0
release_target: e4bd7b089988e1ea25a2eb710b1ec7f07aa668ad
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/43977587468
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/43945858111
---

# OpenCode installer v0.20.0

## Scope

This is a new Package and Release. It does not replace the 0.19.0 asset. Its Bundles and Install
state stay schema 2, and it carries the same five Modules as 0.19.0. Two of them changed:

- **Coordination 0.2.0 -> 0.3.0.**
  - The curator's model table, per-model notes and worker blocks move out of
    `delegating-to-worker-models` into a new `model-profiles` skill. The craft names no model on its
    own: the model comes from the user, a routing, or named profiles.
  - `coordinator` becomes model-invocable (`both`) with a narrow description. Authorization comes
    from the request, not from loading the skill.
  - The craft gains "A worker's lifetime" (size, usage records, restarts, settling from records,
    cleanup), classifies evidence by its cause, and splits a worker's checks from the coordinator's
    gate in worker contract v10. `t3-delegation` gains the usage fields, the restart check, safe
    worktree removal and reading long threads from their end.
  - The dispatch harness ships only in the Claude Code and Codex Plugins; the Bundle carries no
    harness byte.
- **Process 0.15.4 -> 0.15.5.** `ask-deniz` routes to `model-profiles`, and its coordinator row is a
  plain model fact.

General, Akka and Aspire are unchanged. Between the 0.19.0 source `021b49e` and `e4bd7b0`, `dist/`
does not differ, so this Release changes no installer behavior.

## Method

Source commit `e4bd7b0` is the "prepare Package 0.20.0" commit on `master`, merged from pull request
#34. It bumps the Package version and adds a preparation notice to the README. Its parent `f8ca33a`
is pull request #33 (Coordination 0.3.0, Process 0.15.5). Both pull requests passed all eight CI
checks before merge. On `e4bd7b0`, the [`validate` run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/43945839192)
passed. In the [`dispatch` run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/43945839119),
five shards passed and the macOS shard 2 was cancelled because no hosted runner acquired it (a
macOS arm64 capacity annotation), so it ran no test. A rerun of that shard passed, so all six shards
passed on Windows, Linux and macOS.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/43945858111)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=e4bd7b089988e1ea25a2eb710b1ec7f07aa668ad`,
   `release_tag=installer-v0.20.0`, `publish=false`, and `replace_existing=false`. It ran the same
   gates as for 0.19.0 and succeeded:
   - the digest-pinned full-history Gitleaks scan;
   - the repository gate;
   - two clean generation rounds;
   - the Linux pack with exact-payload, Bundle-digest, and tar-mode verification;
   - the isolated zero-write Plan, Apply-all, and status with `OPENCODE_CONFIG_DIR` unset.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows host. Its size
   and SHA-256 matched the ones the run recorded.
3. A draft Release targeting the exact source commit was created with no assets and the release
   notes as its body.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/43977587468)
   repeated step 1 with `publish=true`, then uploaded the verified artifact to the draft Release.
   Both jobs succeeded.
5. The draft asset, downloaded independently, was byte-identical to the candidate artifact (`cmp`)
   and passed the Package verifier.
6. The draft was published as the latest Release. The tag `installer-v0.20.0` resolves to the source
   commit. Asset ID `756823937` holds `deniz-agent-skills-0.20.0.tgz`, 1,219,421 bytes, with the
   SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,219,421 bytes and SHA-256,
   and passed the Package verifier.

## Modules

The Package verifier checks each Module digest in the tarball. The digests below are read from
`opencode/<module>/manifest.json`.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.15.5` | `sha256:7d3bb7d69641e798943b13e1ce45244f3100697d3fc6d0d2a89056c8cfed6281` |
| `deniz-dotnet-general` | `0.19.0` | `sha256:c37c1b950b99016b1a5e0a785db5cb4cef26408a55c6fc8d5b6ce4708431aaa5` |
| `deniz-dotnet-akka` | `0.5.0` | `sha256:b24bce29f66ff24f471eaa69872af49f54208f17b81bcc6187e95668b048473e` |
| `deniz-dotnet-aspire` | `0.7.2` | `sha256:353dc261ef238254e85b6d7fdc02b60e34aa970b1bbe06e1344c05fd45f0d1e2` |
| `deniz-coordination` | `0.3.0` | `sha256:ef7aa33eaa856b21e107f12b092b281dc58631dd5c961240f34439fc34fc100c` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot: a 0.20.0 preparation notice, the
then-current verified 0.19.0 recipe, and a link to the live repository recipe. The Package digest
can be recorded only after packing, so a later commit holds:

- the post-publication README;
- the `tools/repository-docs.test.ts` pins;
- the workflow's `release_tag` default;
- this record.

None of them was repacked into the asset.

The previous [0.19.0 record](2026-11-15-opencode-installer-v0.19.0.md) remains historical evidence.
Its asset ID `752275692`, 1,215,062 bytes, and SHA-256
`3805a74dc3fda990a20c0e6461da4ee59cd9e37409c8f92ef7196461b75f840d` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The local checks ran on Windows only: the Package verifier and the
  byte comparisons, not an installation. No macOS host verified this asset.
- This proof ran no OpenCode process, model run, or TUI, and no model exercised a changed skill. The
  Coordination changes were reviewed as text by two independent reviews and a consumer review from a
  running lead, not measured by a live coordinator run.
- This record covers no real-profile OpenCode installation or update, and no update from an existing
  0.19.0 Install state.
- A fresh all-Module installation does not prove an arbitrary-subset Selection, every update or
  remove scenario, or an upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable. The recorded digest detects a replacement but cannot prevent it.
