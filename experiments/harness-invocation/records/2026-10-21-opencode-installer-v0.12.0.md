---
record_id: opencode-installer-v0.12.0-2026-10-21
date: 2026-10-21
repo_head: 63b29a5d3ae2de4ea52404633dd0c66efeb071aa
kind: runtime-smoke
summary: Package 0.12.0 carries the schema-2 OpenCode 2 Bundles of Process 0.13.1, General 0.14.1, Akka 0.4.1, and Aspire 0.6.0 with the unchanged 0.5.0 installer, and passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only and a publication run, then an independent draft-asset check and an unauthenticated public re-download on the Windows workstation.
isolation_ok: true
source_ref: 63b29a5d3ae2de4ea52404633dd0c66efeb071aa
package_name: deniz-agent-skills-0.12.0.tgz
package_size: 1046209
package_sha256: 200f8b576eab3aa3ff526ffdab15c6d5da58e72b831bb72542c01fa3dba24b62
transport: public-release
release_tag: installer-v0.12.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.12.0
release_target: 63b29a5d3ae2de4ea52404633dd0c66efeb071aa
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/39546118405
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/39545913348
---

# OpenCode installer v0.12.0

## Scope

This is a new Package and Release, not a replacement of the 0.11.0 asset. Its Bundles and Install
state stay schema 2. It carries Process 0.13.1, whose only change from 0.13.0 is one bullet in
`writing-plans`' File Structure: verification and one-off operations use the tools the repository
and platform already have, and a plan names no new project, script, harness, tool in another
language, or evidence document for them unless the request asks for one. General 0.14.1, Akka 0.4.1, and
Aspire 0.6.0 are unchanged. The Bundles hold 118 skills (33 `manual`), two subagents, and no
commands. The Module requirement graph is unchanged. The installer JavaScript under `dist/` is
byte-identical to the 0.11.0 source, so this Release changes no installer behavior. The rules are
owned by [distribution and installation](../../../docs/architecture/distribution-and-installation.md)
and [transformation and emission](../../../docs/architecture/transformation-and-emission.md).

## Method

Source commit `63b29a5` only bumps the Package version and adds a preparation notice to the README
on top of `e796c17`. Between the 0.11.0 source `c3a3b2c` and `63b29a5`, `opencode/` differs only in
Process's manifest and `writing-plans`' `SKILL.md`, and `dist/` does not differ.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/39545913348)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=63b29a5d3ae2de4ea52404633dd0c66efeb071aa`,
   `release_tag=installer-v0.12.0`, `publish=false`, and `replace_existing=false`. It scanned the
   source's full history with digest-pinned Gitleaks; ran tests, typecheck, lint, format,
   public-safety, build, inventory, and validate; confirmed two generation rounds leave the source
   clean; packed on Linux; verified the exact payload, Bundle hashes and digests, and tar executable
   modes; and ran the Package with an isolated HOME/XDG/npm profile and `OPENCODE_CONFIG_DIR` unset:
   zero-write Plan, Apply-all, and status.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows workstation, and
   its SHA-256 matched the run's recorded identity.
3. A draft Release targeting the exact source commit was created with no assets.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/39546118405)
   repeated step 1 with `publish=true`, then uploaded the verified artifact to the draft Release,
   downloaded it again, compared bytes, and reran the Package verifier. Both jobs succeeded.
5. The draft asset, downloaded independently, was byte-identical to the candidate artifact and
   passed the Package verifier.
6. The draft was published. The tag `installer-v0.12.0` resolves to the source commit; asset ID
   `661387093` holds `deniz-agent-skills-0.12.0.tgz`, 1,046,209 bytes, with the SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,046,209 bytes and SHA-256
   and passed the Package verifier.

## Modules

Every Module digest in the tarball equals its `opencode/<module>/manifest.json` at the source
commit. General's, Akka's, and Aspire's versions and digests are unchanged from 0.11.0: none of their
bytes moved in any harness.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.13.1` | `sha256:7670b8f031f824a9fa488b23351d3e41594348acf8cff8954da26272a9997272` |
| `deniz-dotnet-general` | `0.14.1` | `sha256:92b2e9409b9fe2c8813ab79ffe0da0592db5a90471ec6d52f7d590193285253e` |
| `deniz-dotnet-akka` | `0.4.1` | `sha256:f5fb32c8341258fc72e21a0cd24a8d8cc14cc201116f130cc93d8b941a10e7ad` |
| `deniz-dotnet-aspire` | `0.6.0` | `sha256:f3d3d8f8545f35cb1d04784b602f75f9df64773f81757c108021bca76e2eea95` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot. It contains a 0.12.0 preparation
notice, the then-current verified 0.11.0 recipe, and a link to the live repository recipe. The
Package digest can only be recorded after packing, so the post-publication README, the
`tools/repository-docs.test.ts` pins, the workflow's `release_tag` default, and this record live in
a later commit and were not repacked into the asset.

The previous [0.11.0 record](2026-10-20-opencode-installer-v0.11.0.md) remains historical evidence.
Its asset ID `660322395`, 1,046,091 bytes, and SHA-256
`4c79776d2339585f4c598273fc9499af7042c151508ba40f0cd3f283702220f0` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The Windows checks establish digest identity and Package verification,
  not POSIX execution there.
- No OpenCode process, model run, or TUI was part of this proof. The Process 0.13.1 change was
  checked only by structural tests here; the plan-only panel that motivated it is not part of this
  record.
- This record covers no real-profile installation or update, and no update from an existing 0.11.0
  Install state.
- Fresh all-Module installation does not prove arbitrary-subset Selection, every update or remove
  scenario, or upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable; the recorded digest detects replacement but cannot prevent it.
