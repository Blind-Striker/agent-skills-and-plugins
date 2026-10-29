---
record_id: opencode-installer-v0.15.0-2026-10-29
date: 2026-10-29
repo_head: 485a9670703a174e1b949c9b6a9f72691500b337
kind: runtime-smoke
summary: Package 0.15.0 carries the schema-2 OpenCode 2 Bundles of Process 0.14.2, General 0.16.0, Akka 0.4.1, Aspire 0.6.0, and Coordination 0.1.7 with the unchanged installer, and passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only and a publication run, then an independent draft-asset check and an unauthenticated public re-download on the Windows workstation.
isolation_ok: true
source_ref: 485a9670703a174e1b949c9b6a9f72691500b337
package_name: deniz-agent-skills-0.15.0.tgz
package_size: 1066927
package_sha256: 952b88776c1c85cfca4cf9249aca7bc03405362bbac3304923c83c600c9159eb
transport: public-release
release_tag: installer-v0.15.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.15.0
release_target: 485a9670703a174e1b949c9b6a9f72691500b337
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/40931911513
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/40931349652
---

# OpenCode installer v0.15.0

## Scope

This is a new Package and Release, not a replacement of the 0.14.0 asset. Its Bundles and Install
state stay schema 2. It carries the same five Modules as 0.14.0, and only Coordination changed:

- **Coordination 0.1.7.** It changes the text of `delegating-to-worker-models` and four of its
  references (`models/README.md`, `repo-addendum-template.md`, `test-oracles.md`, and
  `worker-contract.md`). The OpenCode text of `coordinator` and `verified-waves` is unchanged. The
  `harness` skill and the coordinator's tool notes still ship only to Claude Code and Codex
  ([ADR-0010](../../../docs/adr/0010-per-harness-item-emission.md)).
- **Process 0.14.2, General 0.16.0, Akka 0.4.1, and Aspire 0.6.0.** Unchanged.

The Bundles hold 121 skills, two subagents, and no commands. The Module requirement graph is
unchanged. Between the 0.14.0 source `754593d` and `485a967`, `dist/` does not differ, so this
Release changes no installer behavior.

## Method

Source commit `485a967` is the "prepare Package 0.15.0" commit (pull request #12), fast-forwarded
onto `master` after all five checks passed on it. It bumps the Package version and adds a
preparation notice to the README. Its parent `1ee73f4` is the head of pull request #10, which shipped
the Module versions above.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/40931349652)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=485a9670703a174e1b949c9b6a9f72691500b337`,
   `release_tag=installer-v0.15.0`, `publish=false`, and `replace_existing=false`. It ran the same
   gates as for 0.14.0, and each one passed:
   - the digest-pinned full-history Gitleaks scan;
   - the repository gate;
   - two clean generation rounds;
   - the Linux pack with the exact-payload, Bundle-digest, and tar-mode verification;
   - the isolated zero-write Plan, Apply-all, and status with `OPENCODE_CONFIG_DIR` unset.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows workstation, with
   the SHA-256 recorded above.
3. A draft Release targeting the exact source commit was created with no assets and the release
   notes as its body.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/40931911513)
   repeated step 1 with `publish=true`. It then uploaded the verified artifact to the draft Release,
   downloaded it again, compared the bytes, and reran the Package verifier. Both jobs succeeded.
5. The draft asset, downloaded independently, was byte-identical to the candidate artifact and
   passed the Package verifier.
6. The draft was published as the latest Release. The tag `installer-v0.15.0` resolves to the source
   commit. Asset ID `689523067` holds `deniz-agent-skills-0.15.0.tgz`, 1,066,927 bytes, with the
   SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,066,927 bytes and SHA-256
   and passed the Package verifier.

## Modules

Every Module digest in the tarball equals its `opencode/<module>/manifest.json` at the source
commit. Process, General, Akka, and Aspire keep their 0.14.0 versions and digests.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.14.2` | `sha256:690792ff8625fdac8a1034fa063101479f6855e70f03e990de09801195db411f` |
| `deniz-dotnet-general` | `0.16.0` | `sha256:87c8751a58a9c5571c5acc6becf6a1e9525a8ff776e243d826ef840675c903f1` |
| `deniz-dotnet-akka` | `0.4.1` | `sha256:f5fb32c8341258fc72e21a0cd24a8d8cc14cc201116f130cc93d8b941a10e7ad` |
| `deniz-dotnet-aspire` | `0.6.0` | `sha256:f3d3d8f8545f35cb1d04784b602f75f9df64773f81757c108021bca76e2eea95` |
| `deniz-coordination` | `0.1.7` | `sha256:6baf2b67d2431370ce20c57ba2d0421287cc7116872290b924f11e5a3503b7c1` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot. It contains three things: a 0.15.0
preparation notice, the then-current verified 0.14.0 recipe, and a link to the live repository
recipe. The Package digest can be recorded only after packing, so a later commit holds:

- the post-publication README;
- the `tools/repository-docs.test.ts` pins;
- the workflow's `release_tag` default;
- this record.

None of them was repacked into the asset.

The previous [0.14.0 record](2026-10-26-opencode-installer-v0.14.0.md) remains historical evidence.
Its asset ID `679434093`, 1,066,210 bytes, and SHA-256
`edea2916d7a9fd1e2fe0bd2cd6c4f3e9b703fb3ca3614c75e88b268647241702` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The Windows checks establish digest identity and Package verification,
  not POSIX execution there.
- No OpenCode process, model run, or TUI was part of this proof. No changed skill was exercised by a
  model here.
- This record covers no real-profile installation or update, and no update from an existing 0.14.0
  Install state.
- Fresh all-Module installation does not prove arbitrary-subset Selection, every update or remove
  scenario, or upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable; the recorded digest detects replacement but cannot prevent it.
