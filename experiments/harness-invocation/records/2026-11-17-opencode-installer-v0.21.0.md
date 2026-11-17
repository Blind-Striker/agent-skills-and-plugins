---
record_id: opencode-installer-v0.21.0-2026-11-17
date: 2026-11-17
repo_head: 73ee1fcaa574969f6917ed0faf1ecd6f25ccf4f8
kind: runtime-smoke
summary: Package 0.21.0 carries the schema-2 OpenCode 2 Bundles of Process 0.15.5, General 0.19.1, Akka 0.5.0, Aspire 0.7.2, and Coordination 0.3.1 with the unchanged installer. It passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only run and in a publication run. On the Windows host it then passed an independent draft-asset check and an unauthenticated public re-download.
isolation_ok: true
source_ref: 73ee1fcaa574969f6917ed0faf1ecd6f25ccf4f8
package_name: deniz-agent-skills-0.21.0.tgz
package_size: 1220179
package_sha256: dcc32e0cfdc1a4efbf8f5605d42eb4e99581b2b9e1d2cffc8d96e7b8b0a4ccbd
transport: public-release
release_tag: installer-v0.21.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.21.0
release_target: 73ee1fcaa574969f6917ed0faf1ecd6f25ccf4f8
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/44155537656
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/44154826684
---

# OpenCode installer v0.21.0

## Scope

This is a new Package and Release. It does not replace the 0.20.0 asset. Its Bundles and Install
state stay schema 2, and it carries the same five Modules as 0.20.0. Two of them changed:

- **General 0.19.0 -> 0.19.1.** The `dotnet-slopwatch` overlay patch makes the skill run the
  repository's pinned slopwatch (`dotnet tool run slopwatch`) when the tool manifest lists it, and the
  global tool only otherwise. It carries slopwatch 0.5.0 (released 2026-11-16), which the upstream
  body predates: the version-2 baseline and its regeneration, SW021 (the `dynamic` ban), and the
  generated-code default. The ask-first guard also covers regenerating the baseline,
  `[SlopwatchSuppress]` and config suppressions.
- **Coordination 0.3.0 -> 0.3.1.** `t3-delegation` says that a message to another session carries the
  sender's `environmentId` and `threadId` and asks for a `t3_thread_send` reply, and where each id
  comes from.

Process, Akka and Aspire are unchanged. Between the 0.20.0 source `e4bd7b0` and `73ee1fc`, `dist/`
does not differ, so this Release changes no installer behavior.

## Method

Source commit `73ee1fc` is the "prepare Package 0.21.0" commit on `master`, merged from pull request
#38. It bumps the Package version and adds a preparation notice to the README. Its parents are pull
requests #36 (Coordination 0.3.1) and #37 (General 0.19.1). Each of the three pull requests passed all
eight CI checks at the head that was merged; #38's head `4305d67` has the same tree as `73ee1fc`. On
#37, macOS dispatch shard 1 was twice not acquired by a hosted runner (capacity) and passed on its
third run. On `73ee1fc` itself, the
[`validate` run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/44154810360)
passed; the [`dispatch` run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/44154810555)
was still queued when this record was written.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/44154826684)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=73ee1fcaa574969f6917ed0faf1ecd6f25ccf4f8`,
   `release_tag=installer-v0.21.0`, `publish=false`, and `replace_existing=false`. It ran the same
   gates as for 0.20.0 and succeeded:
   - the digest-pinned full-history Gitleaks scan;
   - the repository gate;
   - two clean generation rounds;
   - the Linux pack with exact-payload, Bundle-digest, and tar-mode verification;
   - the isolated zero-write Plan, Apply-all, and status with `OPENCODE_CONFIG_DIR` unset.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows host. Its size
   and SHA-256 matched the ones the run recorded.
3. A draft Release targeting the exact source commit was created with no assets and the release
   notes as its body.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/44155537656)
   repeated step 1 with `publish=true`, then uploaded the verified artifact to the draft Release.
   Both jobs succeeded.
5. The draft asset, downloaded independently, was byte-identical to the candidate artifact (`cmp`)
   and passed the Package verifier.
6. The draft was published as the latest Release. The tag `installer-v0.21.0` resolves to the source
   commit. Asset ID `760296859` holds `deniz-agent-skills-0.21.0.tgz`, 1,220,179 bytes, with the
   SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,220,179 bytes and SHA-256,
   and passed the Package verifier.

## Modules

The Package verifier checks each Module digest in the tarball. The digests below are read from
`opencode/<module>/manifest.json`.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.15.5` | `sha256:7d3bb7d69641e798943b13e1ce45244f3100697d3fc6d0d2a89056c8cfed6281` |
| `deniz-dotnet-general` | `0.19.1` | `sha256:a18755e9eaf6a6cf180dbc209a77cfb5ebe1be5f46f77a6ba0c65cfdc638ce08` |
| `deniz-dotnet-akka` | `0.5.0` | `sha256:b24bce29f66ff24f471eaa69872af49f54208f17b81bcc6187e95668b048473e` |
| `deniz-dotnet-aspire` | `0.7.2` | `sha256:353dc261ef238254e85b6d7fdc02b60e34aa970b1bbe06e1344c05fd45f0d1e2` |
| `deniz-coordination` | `0.3.1` | `sha256:2af6291922f0b7c6086c5630717f4ff38bdb6b01977508c0dee51c38086b21eb` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot: a 0.21.0 preparation notice, the
then-current verified 0.20.0 recipe, and a link to the live repository recipe. The Package digest
can be recorded only after packing, so a later commit holds:

- the post-publication README;
- the `tools/repository-docs.test.ts` pins;
- the workflow's `release_tag` default;
- this record.

None of them was repacked into the asset.

The previous [0.20.0 record](2026-11-17-opencode-installer-v0.20.0.md) remains historical evidence.
Its asset ID `756823937`, 1,219,421 bytes, and SHA-256
`a14010d1e0c68472bd510c050df10ac80704f8bda0c876a3c43b2cd1ad3bbce6` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The local checks ran on Windows only: the Package verifier and the
  byte comparisons, not an installation. No macOS host verified this asset.
- This proof ran no OpenCode process, model run, or TUI, and no model exercised a changed skill. No
  slopwatch binary ran: the 0.5.0 facts in the skill were checked against the slopwatch 0.5.0 release
  notes and README, not against a run of the tool.
- This record covers no real-profile OpenCode installation or update, and no update from an existing
  0.20.0 Install state.
- A fresh all-Module installation does not prove an arbitrary-subset Selection, every update or
  remove scenario, or an upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable. The recorded digest detects a replacement but cannot prevent it.
