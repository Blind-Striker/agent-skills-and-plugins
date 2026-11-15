---
record_id: opencode-installer-v0.19.0-2026-11-15
date: 2026-11-15
repo_head: 021b49e8d4165f9d07d88b06e4c450ca6c7a48eb
kind: runtime-smoke
summary: Package 0.19.0 carries the schema-2 OpenCode 2 Bundles of Process 0.15.4, General 0.19.0, Akka 0.5.0, Aspire 0.7.2, and Coordination 0.2.0 with the unchanged installer. It passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only run and in a publication run. On the Linux host it then passed an independent draft-asset check and an unauthenticated public re-download.
isolation_ok: true
source_ref: 021b49e8d4165f9d07d88b06e4c450ca6c7a48eb
package_name: deniz-agent-skills-0.19.0.tgz
package_size: 1215062
package_sha256: 3805a74dc3fda990a20c0e6461da4ee59cd9e37409c8f92ef7196461b75f840d
transport: public-release
release_tag: installer-v0.19.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.19.0
release_target: 021b49e8d4165f9d07d88b06e4c450ca6c7a48eb
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/43758704606
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/43758311177
---

# OpenCode installer v0.19.0

## Scope

This is a new Package and Release. It does not replace the 0.18.0 asset. Its Bundles and Install
state stay schema 2, and it carries the same five Modules as 0.18.0. Two of them changed:

- **Coordination 0.1.12 -> 0.2.0.**
  - 0.1.13 to 0.1.15 move the dispatch harness to OpenCodeDotNet.Sdk 1.1.0 and the OpenCode 2.3.0
    pin, classify an off-pin version (a patch runs with a warning, anything else needs the override),
    and list models before agents, because at 2.3.0 only the model listing waits for plugin
    activation. The harness ships only in the Claude Code and Codex Plugins, so these change no
    OpenCode Bundle byte except the version.
  - 0.2.0 makes the coordinator name no delegation tool: the craft gives each piece a model first and
    then any mechanism that reaches it, and carries the host-subagent notes per harness. It adds
    `t3-delegation`, the craft mapped onto T3 Code's delegation tools, in every harness.
- **Process 0.15.3 -> 0.15.4.** `ask-deniz` routes to `t3-delegation`.

General, Akka and Aspire are unchanged. Between the 0.18.0 source `d5d7db9` and `021b49e`, `dist/`
does not differ, so this Release changes no installer behavior.

## Method

Source commit `021b49e` is the "prepare Package 0.19.0" commit on `master`. It bumps the Package
version and adds a preparation notice to the README. Its parents are `af8d1d8` (Coordination 0.2.0,
Process 0.15.4) and `27bd1ef` (Coordination 0.1.15). These were pushed to `master` directly, with no
pull request, so the Release targets the commit that the `validate` and `dispatch` workflows and
both release runs built. The [`dispatch` run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/43758304381)
on `021b49e` passed all six shards on Windows, Linux and macOS.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/43758311177)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=021b49e8d4165f9d07d88b06e4c450ca6c7a48eb`,
   `release_tag=installer-v0.19.0`, `publish=false`, and `replace_existing=false`. It ran the same
   gates as for 0.18.0 and succeeded:
   - the digest-pinned full-history Gitleaks scan;
   - the repository gate;
   - two clean generation rounds;
   - the Linux pack with exact-payload, Bundle-digest, and tar-mode verification;
   - the isolated zero-write Plan, Apply-all, and status with `OPENCODE_CONFIG_DIR` unset.
2. The downloaded candidate artifact passed `npm run verify:package` on the Linux host. Its SHA-256
   matched the one the run recorded.
3. A draft Release targeting the exact source commit was created with no assets and the release
   notes as its body.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/43758704606)
   repeated step 1 with `publish=true`, then uploaded the verified artifact to the draft Release.
   Both jobs succeeded.
5. The draft asset, downloaded independently, was byte-identical to the candidate artifact (`cmp`)
   and passed the Package verifier.
6. The draft was published as the latest Release. The tag `installer-v0.19.0` resolves to the source
   commit. Asset ID `752275692` holds `deniz-agent-skills-0.19.0.tgz`, 1,215,062 bytes, with the
   SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,215,062 bytes and SHA-256,
   and passed the Package verifier.

## Modules

The Package verifier checks each Module digest in the tarball. The digests below are read from
`opencode/<module>/manifest.json`.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.15.4` | `sha256:37500c4141dd10ce4a10149087924aaeb5d948d31fe9555c49bd78e36aaad0eb` |
| `deniz-dotnet-general` | `0.19.0` | `sha256:c37c1b950b99016b1a5e0a785db5cb4cef26408a55c6fc8d5b6ce4708431aaa5` |
| `deniz-dotnet-akka` | `0.5.0` | `sha256:b24bce29f66ff24f471eaa69872af49f54208f17b81bcc6187e95668b048473e` |
| `deniz-dotnet-aspire` | `0.7.2` | `sha256:353dc261ef238254e85b6d7fdc02b60e34aa970b1bbe06e1344c05fd45f0d1e2` |
| `deniz-coordination` | `0.2.0` | `sha256:3acae617744593c426278ec43a2ec9ae7f7052588534435868a4e4864fc1dd3b` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot: a 0.19.0 preparation notice, the
then-current verified 0.18.0 recipe, and a link to the live repository recipe. The Package digest
can be recorded only after packing, so a later commit holds:

- the post-publication README;
- the `tools/repository-docs.test.ts` pins;
- the workflow's `release_tag` default;
- this record.

None of them was repacked into the asset.

The previous [0.18.0 record](2026-11-10-opencode-installer-v0.18.0.md) remains historical evidence.
Its asset ID `732639507`, 1,213,142 bytes, and SHA-256
`3a6db6bf18a199d031edc57033749c9288fdedf901a3c7cc3fe30b91056a2227` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The local checks ran on Linux only; no Windows or macOS host verified
  this asset.
- This proof ran no OpenCode process, model run, or TUI, and no model exercised a changed skill. The
  coordinator and craft change was measured only as a five-run text simulation of a T3 session
  before and after the change, recorded in the commit that made it, not by a live coordinator run.
- The harness pin move is proved by its .NET suite on the CI operating systems and, on the Linux
  host, by a selfcheck whose pin check passed against OpenCode 2.3.0 without the drift override. The
  harness ships in the Claude Code and Codex Plugins, not in this Package.
- This record covers no real-profile OpenCode installation or update, and no update from an existing
  0.18.0 Install state.
- A fresh all-Module installation does not prove an arbitrary-subset Selection, every update or
  remove scenario, or an upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable. The recorded digest detects a replacement but cannot prevent it.
