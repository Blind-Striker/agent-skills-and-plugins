---
record_id: opencode-installer-v0.18.0-2026-11-10
date: 2026-11-10
repo_head: d5d7db9d3acddc58bb30281d0d411a9b433bd811
kind: runtime-smoke
summary: Package 0.18.0 carries the schema-2 OpenCode 2 Bundles of Process 0.15.3, General 0.19.0, Akka 0.5.0, Aspire 0.7.2, and Coordination 0.1.12 with the unchanged installer. It passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only run and in a publication run. On the Windows workstation it then passed an independent draft-asset check and an unauthenticated public re-download.
isolation_ok: true
source_ref: d5d7db9d3acddc58bb30281d0d411a9b433bd811
package_name: deniz-agent-skills-0.18.0.tgz
package_size: 1213142
package_sha256: 3a6db6bf18a199d031edc57033749c9288fdedf901a3c7cc3fe30b91056a2227
transport: public-release
release_tag: installer-v0.18.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.18.0
release_target: d5d7db9d3acddc58bb30281d0d411a9b433bd811
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/42883629499
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/42883036093
---

# OpenCode installer v0.18.0

## Scope

This is a new Package and Release. It does not replace the 0.17.0 asset. Its Bundles and Install
state stay schema 2, and it carries the same five Modules as 0.17.0. Four of them changed:

- **All curated upstreams moved to their current tips.**
  - Upstreams: superpowers v7.0.2, mattpocock-skills, dotnet/skills, aspire-skills v0.0.5,
    dotnet-skills, asd-ste100, blazing-fast-dotnet, ponytail, and humanlayer-skills.
  - Review: after the four sync waves, a curation sanity panel with reference audits ran on the
    merged estate. It had 15 reviewers on GPT Sol through the dispatch harness. The curator approved
    its findings, which landed together with two .NET 11 items taken at .NET 11 general availability.
- **Process 0.14.3 -> 0.15.3.**
  - superpowers v7.0.2: brainstorming, writing-plans re-sizing, the split of
    subagent-driven-development and executing-plans into references, and writing-skills as `both`.
  - mattpocock-skills: the fresh-context rule, and `software-factory` taken as `manual`.
  - The approved panel fixes.
- **General 0.17.0 -> 0.19.0.**
  - msbuild reads build logs through a connected binlog MCP server first and replays them otherwise.
  - Eight new items: `find-repeated-work-within-one-build`, `eliminate-redundant-restores`,
    `dotnet-breaking-changes`, `test-value-evidence`, `process-api-net11`, `system-text-json-net11`,
    and the manual `troubleshoot-binlog-mcp` and `migrate-dotnet10-to-dotnet11`.
  - Corrected EF Core guidance, coverage-analysis on the scripts' JSON output, and the panel fixes.
- **Aspire 0.6.0 -> 0.7.2.** It takes `aspire-terminals`, fits Codex's description limit, uses the
  already-registered Aspire MCP server, and carries the panel fixes.
- **Akka 0.4.1 -> 0.5.0.** It adds a TUnit guard in testing and an Aspire router pointer in the
  cookbook.
- **Coordination 0.1.12.** Its OpenCode Bundle changes only its version: its Module digest equals
  0.1.11's.

The Bundles hold 132 skills, two subagents, and no commands. Between the 0.17.0 source `aa4d9ee`
and `d5d7db9`, `dist/` does not differ, so this Release changes no installer behavior.

## Method

Source commit `d5d7db9` is the "prepare Package 0.18.0" commit (pull request #29). It bumps the
Package version and adds a preparation notice to the README. Its parent, `63012f5`, is the head of
pull request #28, which landed the panel fixes and the .NET 11 items. Before that, pull requests
#24 to #27 landed the four sync waves.

All eight checks of pull request #29 passed on `d5d7db9`. Its `dotnet (macos-latest, 2)` shard was
cancelled once: no runner was allocated within 15 minutes and no step ran. It passed on rerun.
`master` was then fast-forwarded to `d5d7db9`, so the Release targets the exact commit that CI and
both workflow runs built. A GitHub rebase merge would have rewritten that SHA.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/42883036093)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=d5d7db9d3acddc58bb30281d0d411a9b433bd811`,
   `release_tag=installer-v0.18.0`, `publish=false`, and `replace_existing=false`. It ran the same
   gates as for 0.17.0 and succeeded:
   - the digest-pinned full-history Gitleaks scan;
   - the repository gate;
   - two clean generation rounds;
   - the Linux pack with exact-payload, Bundle-digest, and tar-mode verification;
   - the isolated zero-write Plan, Apply-all, and status with `OPENCODE_CONFIG_DIR` unset.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows workstation. Its
   SHA-256 matched the one the run recorded.
3. A draft Release targeting the exact source commit was created with no assets and the release
   notes as its body.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/42883629499)
   repeated step 1 with `publish=true`, then uploaded the verified artifact to the draft Release.
   Both jobs succeeded.
5. The draft asset, downloaded independently, was byte-identical to the candidate artifact (`cmp`)
   and passed the Package verifier.
6. The draft was published as the latest Release. The tag `installer-v0.18.0` resolves to the source
   commit. Asset ID `732639507` holds `deniz-agent-skills-0.18.0.tgz`, 1,213,142 bytes, with the
   SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,213,142 bytes and SHA-256,
   and passed the Package verifier.

## Modules

Coordination keeps its 0.17.0 digest. The Package verifier checks each Module digest in the
tarball. The digests below are read from `opencode/<module>/manifest.json`.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.15.3` | `sha256:430335985d9b524e7ab0a50b05dcc0cf3c3f8f73225caff4ffd0da42027a2aee` |
| `deniz-dotnet-general` | `0.19.0` | `sha256:c37c1b950b99016b1a5e0a785db5cb4cef26408a55c6fc8d5b6ce4708431aaa5` |
| `deniz-dotnet-akka` | `0.5.0` | `sha256:b24bce29f66ff24f471eaa69872af49f54208f17b81bcc6187e95668b048473e` |
| `deniz-dotnet-aspire` | `0.7.2` | `sha256:353dc261ef238254e85b6d7fdc02b60e34aa970b1bbe06e1344c05fd45f0d1e2` |
| `deniz-coordination` | `0.1.12` | `sha256:2e34321eb4b3abd544ecd705aec1743b9cc09d6b1c46b9b8e5b71d57929fd66e` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot: a 0.18.0 preparation notice, the
then-current verified 0.17.0 recipe, and a link to the live repository recipe. The Package digest
can be recorded only after packing, so a later commit holds:

- the post-publication README;
- the `tools/repository-docs.test.ts` pins;
- the workflow's `release_tag` default;
- this record.

None of them was repacked into the asset.

The previous [0.17.0 record](2026-11-07-opencode-installer-v0.17.0.md) remains historical evidence.
Its asset ID `721529664`, 1,084,535 bytes, and SHA-256
`c303183219b8f4e1f861635155e0777918a23dcdc76c49a6050f1cf3f6b84790` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The Windows checks establish digest identity and Package verification,
  not POSIX execution there.
- This proof ran no OpenCode process, model run, or TUI, and no model exercised a changed skill. The
  curation sanity panel judged the emitted text, not runtime behavior.
- This record covers no real-profile installation or update, and no update from an existing 0.17.0
  Install state.
- A fresh all-Module installation does not prove an arbitrary-subset Selection, every update or
  remove scenario, or an upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable. The recorded digest detects a replacement but cannot prevent it.
