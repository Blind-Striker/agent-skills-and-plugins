---
record_id: opencode-installer-v0.17.0-2026-11-07
date: 2026-11-07
repo_head: aa4d9eee558fb7df6f83b06001af62ec224bf2f8
kind: runtime-smoke
summary: Package 0.17.0 carries the schema-2 OpenCode 2 Bundles of Process 0.14.3, General 0.17.0, Akka 0.4.1, Aspire 0.6.0, and Coordination 0.1.11 with the unchanged installer. It passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only run and in a publication run. On the Windows workstation it then passed an independent draft-asset check and an unauthenticated public re-download.
isolation_ok: true
source_ref: aa4d9eee558fb7df6f83b06001af62ec224bf2f8
package_name: deniz-agent-skills-0.17.0.tgz
package_size: 1084535
package_sha256: c303183219b8f4e1f861635155e0777918a23dcdc76c49a6050f1cf3f6b84790
transport: public-release
release_tag: installer-v0.17.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.17.0
release_target: aa4d9eee558fb7df6f83b06001af62ec224bf2f8
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/42400402117
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/42399981436
---

# OpenCode installer v0.17.0

## Scope

This is a new Package and Release, not a replacement of the 0.16.0 asset. Its Bundles and Install
state stay schema 2. It carries the same five Modules as 0.16.0. Three of them changed:

- **General 0.17.0.** It adds the `csharp-15` skill. The skill is third-party material by Nick
  Chapsas / Dometrain, kept verbatim. It carries no license, and Dometrain distributes it free of
  charge. It is outside the MIT License: its `NOTICE.md` ships beside it, and the root
  `THIRD_PARTY_NOTICES.md` lists it.
- **Process 0.14.3.** `ask-deniz`'s .NET map routes `net11.0` and C# 15 feature work to
  `deniz-dotnet-general:csharp-15`.
- **Coordination 0.1.11.** The OpenCode Bundle changes only its version: its Module digest equals
  0.1.8's. The `harness` skill 0.1.11 ships only to Claude Code and Codex
  ([ADR-0010](../../../docs/adr/0010-per-harness-item-emission.md)).
- **Akka 0.4.1 and Aspire 0.6.0.** Unchanged.

The Bundles hold 122 skills, two subagents, and no commands. The Module requirement graph is
unchanged. `dist/` does not differ between the 0.16.0 source `170e782` and `aa4d9ee`, so this
Release changes no installer behavior.

## Method

Source commit `aa4d9ee` is the "prepare Package 0.17.0" commit (pull request #21). It bumps the
Package version and adds a preparation notice to the README. Its parent `fc43ce3` is the head of
pull request #20, which shipped General 0.17.0 and Process 0.14.3. Before that, pull request #19
shipped Coordination 0.1.11.

All eight checks of pull request #21 passed on `aa4d9ee`. Its secrets job failed once while pulling
the Gitleaks image from ghcr.io, and passed on rerun. `master` was then fast-forwarded to `aa4d9ee`,
so the Release targets the exact commit that CI and both workflow runs built. A GitHub rebase merge
would have rewritten that SHA.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/42399981436)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=aa4d9eee558fb7df6f83b06001af62ec224bf2f8`,
   `release_tag=installer-v0.17.0`, `publish=false`, and `replace_existing=false`. It ran the same
   gates as for 0.16.0, and its run succeeded:
   - the digest-pinned full-history Gitleaks scan;
   - the repository gate;
   - two clean generation rounds;
   - the Linux pack with the exact-payload, Bundle-digest, and tar-mode verification;
   - the isolated zero-write Plan, Apply-all, and status with `OPENCODE_CONFIG_DIR` unset.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows workstation. Its
   SHA-256 matched the one the run recorded.
3. A draft Release targeting the exact source commit was created with no assets and the release
   notes as its body.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/42400402117)
   repeated step 1 with `publish=true`, then uploaded the verified artifact to the draft Release.
   Both jobs succeeded.
5. The draft asset, downloaded independently, was byte-identical to the candidate artifact
   (`cmp`) and passed the Package verifier.
6. After the fast-forward, the draft was published as the latest Release. The tag
   `installer-v0.17.0` resolves to the source commit. Asset ID `721529664` holds
   `deniz-agent-skills-0.17.0.tgz`, 1,084,535 bytes, with the SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,084,535 bytes and SHA-256,
   and passed the Package verifier.

## Modules

Akka and Aspire keep their 0.16.0 versions and digests, and Coordination keeps its digest. The
Package verifier checks each Module digest in the tarball. The digests below are read from
`opencode/<module>/manifest.json`.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.14.3` | `sha256:f61224b89ca52fd54ff232d1967197751e1cc51f522b709d7f4a40b1cb3e82af` |
| `deniz-dotnet-general` | `0.17.0` | `sha256:c0228c324f9a0e5099ed19a7cae59b33f96567a3312028ee8a8296bbb5fa5288` |
| `deniz-dotnet-akka` | `0.4.1` | `sha256:f5fb32c8341258fc72e21a0cd24a8d8cc14cc201116f130cc93d8b941a10e7ad` |
| `deniz-dotnet-aspire` | `0.6.0` | `sha256:f3d3d8f8545f35cb1d04784b602f75f9df64773f81757c108021bca76e2eea95` |
| `deniz-coordination` | `0.1.11` | `sha256:2e34321eb4b3abd544ecd705aec1743b9cc09d6b1c46b9b8e5b71d57929fd66e` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot. It contains three things: a 0.17.0
preparation notice, the then-current verified 0.16.0 recipe, and a link to the live repository
recipe. The Package digest can be recorded only after packing, so a later commit holds:

- the post-publication README;
- the `tools/repository-docs.test.ts` pins;
- the workflow's `release_tag` default;
- this record.

None of them was repacked into the asset.

The previous [0.16.0 record](2026-11-01-opencode-installer-v0.16.0.md) remains historical evidence.
Its asset ID `699030548`, 1,067,124 bytes, and SHA-256
`3ef9e9419b6b8a964005a03cb6dd62524d1a92d9cefadd09adfea443254fe44b` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The Windows checks establish digest identity and Package verification,
  not POSIX execution there.
- This proof ran no OpenCode process, model run, or TUI. No model exercised a changed skill here.
  The `csharp-15` skill's content was not reviewed.
- This record covers no real-profile installation or update, and no update from an existing 0.16.0
  Install state.
- Fresh all-Module installation does not prove an arbitrary-subset Selection, every update or remove
  scenario, or an upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable. The recorded digest detects replacement but cannot prevent it.
