---
record_id: opencode-installer-v0.22.0-2026-11-23
date: 2026-11-23
repo_head: 3cf6f804e1d4297abfd46fd8b7ee38cc24adf86f
kind: runtime-smoke
summary: Package 0.22.0 carries the schema-2 OpenCode 2 Bundles of Process 0.15.6, General 0.19.1, Akka 0.5.0, Aspire 0.7.2, and Coordination 0.3.3 with the unchanged installer. It passed Linux source, tar-mode, and isolated Plan/Apply/status checks in a build-only run and in a publication run. On the Windows host it then passed an independent draft-asset check and an unauthenticated public re-download.
isolation_ok: true
source_ref: 3cf6f804e1d4297abfd46fd8b7ee38cc24adf86f
package_name: deniz-agent-skills-0.22.0.tgz
package_size: 1222022
package_sha256: 102f2db3e38a5925535e7e833cdd04bce6e24e24f6e22c56c7dac099d7ced5e8
transport: public-release
release_tag: installer-v0.22.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.22.0
release_target: 3cf6f804e1d4297abfd46fd8b7ee38cc24adf86f
workflow_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/45103594327
candidate_run: https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/45103204804
---

# OpenCode installer v0.22.0

## Scope

This is a new Package and Release. It does not replace the 0.21.0 asset. Its Bundles and Install
state stay schema 2, and it carries the same five Modules as 0.21.0. Two of them changed:

- **Process 0.15.5 -> 0.15.6.** Antigravity-aware prose updates across `ask-deniz/references/dotnet.md`,
  `setup-matt-pocock-skills/SKILL.md`, and `writing-for-agents/SKILL-MECHANICS.md` landed with the
  native Antigravity Plugin target (`#40`, `d30d2b1f`).
- **Coordination 0.3.1 -> 0.3.3.** Native Antigravity emission (`0.3.2`), followed by `0.3.3`:
  - `CodexHome` parses `config.toml`'s top-level `windows.sandbox` with `Tomlyn 2.10.1` semantic
    validation (supporting dotted-key, table, and inline-table forms while rejecting duplicate keys);
  - dispatch harness Codex CLI pin and 39 schema snapshots moved from `0.167.0` to `0.171.0`
    (`openai/codex#56396` remains open upstream);
  - dispatch harness `OpenCodeDotNet.Sdk` upgraded from `1.1.0` to `2.0.1` (opencode pin `2.3.2`),
    with internal-reconnect gap recovery in `OpenCodeHost` and a `FakeTimeProvider` regression test;
  - `model-profiles` refreshed with the coding capability scale, GLM 5.3 concurrency review,
    DeepSeek V4.1 Flash, curator routing defaults, and retired Grok; `t3-delegation` and
    `delegating-to-worker-models` document the `T3_ACP_MCP_NODE` `acp-mcp-call` fallback.

General, Akka, and Aspire are unchanged. Between the 0.21.0 source `73ee1fc` and `3cf6f80`, `dist/`
does not differ, so this Release changes no installer behavior.

## Method

Source commit `3cf6f80` sits on `master` above `943311e8` (`chore: prepare Package 0.22.0`),
`3e075c97` (Antigravity budget/truncation docs), and `d30d2b1f` (PR `#40`, native Antigravity Plugin
target). Locally on Windows at `3cf6f80`, all Node gates (`build`, `inventory`, `validate`,
`npm test`, `typecheck`, `lint`, `format:check`, `check:public-safety`, plus a byte-identical
idempotence rerun), `dotnet build dispatch.cs`, usage exit (`2`), locked Release test build,
`CodexSchemaTests` P2 against installed `codex 0.171.0` (1/1 passed), and the full non-functional
`tests/dispatch` suite (1,866 total: 1,815 succeeded, 0 failed, 51 OS-skipped) passed. On `3cf6f80`
itself, the
[`validate` run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/45103159341)
passed; the [`dispatch` run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/45103159415)
was still in progress when this record was written.

1. A build-only [candidate run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/45103204804)
   of the manual [`release-package`](../../../.github/workflows/release-package.yml) workflow ran
   with explicit `source_ref=3cf6f804e1d4297abfd46fd8b7ee38cc24adf86f`,
   `release_tag=installer-v0.22.0`, `publish=false`, and `replace_existing=false`. It succeeded:
   - the digest-pinned full-history Gitleaks scan;
   - the repository gate;
   - two clean generation rounds;
   - the Linux pack with exact-payload, Bundle-digest, and tar-mode verification;
   - the isolated zero-write Plan, Apply-all, and status with `OPENCODE_CONFIG_DIR` unset.
2. The downloaded candidate artifact passed `npm run verify:package` on the Windows host. Its size
   and SHA-256 matched the ones the run recorded.
3. A draft Release targeting the exact source commit was created with no assets and the release
   notes as its body.
4. The [publication run](https://github.com/Blind-Striker/agent-skills-and-plugins/actions/runs/45103594327)
   repeated step 1 with `publish=true`, then uploaded the verified artifact to the draft Release.
   Both jobs succeeded.
5. The draft asset, downloaded independently, was byte-identical to the candidate artifact and passed
   the Package verifier.
6. The draft was published as the latest Release. The tag `installer-v0.22.0` resolves to the source
   commit. Asset ID `781316584` holds `deniz-agent-skills-0.22.0.tgz`, 1,222,022 bytes, with the
   SHA-256 above.
7. The public URL, downloaded without authentication, returned the same 1,222,022 bytes and SHA-256,
   and passed the Package verifier.

## Modules

The Package verifier checks each Module digest in the tarball. The digests below are read from
`opencode/<module>/manifest.json`.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.15.6` | `sha256:c8c8d7bbe6bb0ca62ce8bc115e9fab82b7dae939fdb54afed16523bed83de383` |
| `deniz-dotnet-general` | `0.19.1` | `sha256:a18755e9eaf6a6cf180dbc209a77cfb5ebe1be5f46f77a6ba0c65cfdc638ce08` |
| `deniz-dotnet-akka` | `0.5.0` | `sha256:b24bce29f66ff24f471eaa69872af49f54208f17b81bcc6187e95668b048473e` |
| `deniz-dotnet-aspire` | `0.7.2` | `sha256:353dc261ef238254e85b6d7fdc02b60e34aa970b1bbe06e1344c05fd45f0d1e2` |
| `deniz-coordination` | `0.3.3` | `sha256:598d32f2f1255bbd1191c3718968cb1ca793c35c96550b8a4517e4fe5c9ca6ea` |

## Source Snapshot Boundary

The tarball's README is exactly the release-source snapshot: a 0.22.0 preparation notice, the
then-current verified 0.21.0 recipe, and a link to the live repository recipe. The Package digest
can be recorded only after packing, so a later commit holds:

- the post-publication README;
- the `tools/repository-docs.test.ts` pins;
- the workflow's `release_tag` default;
- this record.

None of them was repacked into the asset.

The previous [0.21.0 record](2026-11-17-opencode-installer-v0.21.0.md) remains historical evidence.
Its asset ID `760296859`, 1,220,179 bytes, and SHA-256
`dcc32e0cfdc1a4efbf8f5605d42eb4e99581b2b9e1d2cffc8d96e7b8b0a4ccbd` were not replaced.

## Proof Limits

- The Linux workflow checks establish Package integrity and fresh all-Module installer behavior with
  `OPENCODE_CONFIG_DIR` unset. The local checks ran on Windows only: the Package verifier and the
  byte comparisons, not an installation. No macOS host verified this asset.
- This proof ran no real OpenCode or Codex model run, no TUI, and no live Antigravity runtime panel
  beyond the earlier Windows probes; `CodexSchemaTests` P2 verified the `0.171.0` schema against the
  installed Codex CLI, and the fake-server tests exercised `OpenCodeDotNet.Sdk 2.0.1` reconnect
  recovery without a real opencode 2.3.2 model turn.
- This record covers no real-profile OpenCode installation or update, and no update from an existing
  0.21.0 Install state.
- A fresh all-Module installation does not prove an arbitrary-subset Selection, every update or
  remove scenario, or an upgrade from an earlier schema-1 Release, which is refused.
- Release assets remain mutable. The recorded digest detects a replacement but cannot prevent it.
