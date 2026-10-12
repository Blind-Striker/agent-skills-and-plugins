---
record_id: verified-waves-codex-2026-10-12
date: 2026-10-12
repo_head: 8bc8da3bf070d14f568d1000c1b5b428473f0f0f
kind: model-panel
summary: In a fresh lab CODEX_HOME, Codex CLI 0.162.1 installed all four generated plugins, and the installed deniz-process 0.10.0 verified-waves skill carries allow_implicit_invocation false (P0 pass); every model prompt, the multi-agent V2 run included, was skipped, because no credential was supplied out of band and the real profile's auth was not copied, so the panel measured nothing about the skill's behaviour in Codex.
isolation_ok: true
fixture_sha: 0a83bab
harness_name: codex
harness_version: codex-cli 0.162.1
runner_revision: sha256:0ea49b2506e04c3bb9f742f65697b53deba16432257afb1b5aa094c301172085
---

# verified-waves forward-test panel: Codex

This tier-2 record is the Codex leg of the [verified-waves panel](../verified-waves-panel.md). The
panel owns the method, the prompts, and the answer key. `runner_revision` is the SHA-256 of
`verified-waves-panel.md` followed by `fixtures/make-fixture.ps1`, both read at `repo_head` with a
clean tree. `<LAB>` below is the external lab root, outside the repository and the user profile.
Raw JSON stays in the lab.

## Proof limit

Only P0 ran. Every prompt that needs a model turn is `skipped`:

- **No credential.** The lab `CODEX_HOME` held no `auth.json`, and `OPENAI_API_KEY` was not
  supplied out of band. The runbook forbids copying credentials from the real profile, and this
  run's safety rule forbids touching it. No local open-weights provider was reachable either
  (nothing listened on the default Ollama or LM Studio ports), so `--oss` was no route.
- **No operator at a TUI.** The panel asks for an interactive session, and P4 and P5 need an
  operator who stops one child or the harness. The dry run could not confirm Codex's per-child stop
  without a model session.

To complete this leg, seed `auth.json` in the lab `CODEX_HOME` out of band, or supply
`OPENAI_API_KEY`, and run P1 to P8 and the V2 run of P2 from the TUI, as the panel describes.

## Method

1. **Lab.** `codex-matrix.ps1 -DryRun -GeneratedPlugins` walked the runner, and then
   `codex-matrix.ps1 -GeneratedPlugins` ran the credential-free structural panel. It created a fresh
   `CODEX_HOME` at `<LAB>/codex-run-<stamp>` and passed every assertion, as its `assertions` block
   records:

   ```json
   {"initialMarketplaceCount":0,
    "generatedAvailable":["deniz-dotnet-akka","deniz-dotnet-aspire","deniz-dotnet-general","deniz-process"],
    "generatedInstalled":["deniz-dotnet-akka","deniz-dotnet-aspire","deniz-dotnet-general","deniz-process"],
    "generatedSkillCount":115,"generatedManualPolicyCount":31,"fixtureInstalledAfterRemove":0,
    "realCodexPluginStateUnchanged":true,"repositoryStateUnchanged":true}
   ```

2. **Fixture.** `make-fixture.ps1 -Set waves` built `<LAB>/project/waves-repo` at `0a83bab`, the
   value in `WAVES_BASELINE`. `npm test` passed there (6 tests).
3. **P0.** The installed copy of `verified-waves` sits at
   `<CODEX_HOME>/plugins/cache/deniz-skills/deniz-process/0.10.0/skills/verified-waves/`, with
   `SKILL.md`, `references/harness-tools.md`, and `agents/openai.yaml`. The installed
   `agents/openai.yaml` is byte-identical to `codex/deniz-process/skills/verified-waves/agents/openai.yaml`
   at `repo_head`.

## Results

| model | probe_id | status | cost | tools_observed | notes |
|---|---|---|---:|---|---|
| none | P0 | pass |  | none | installed `agents/openai.yaml` has `policy.allow_implicit_invocation: false`; `short_description` is the 43-character first sentence of the description |
| none | P1 | skipped |  |  | no credential; see Proof limit |
| none | P2 | skipped |  |  | no credential; run 1 of 2 |
| none | P2 | skipped |  |  | no credential; run 2 of 2 |
| none | P3 | skipped |  |  | no credential |
| none | P4 | skipped |  |  | no credential, and the per-child stop was not confirmed in a dry run |
| none | P5a | skipped |  |  | no credential, and no operator to kill and reopen the harness |
| none | P6 | skipped |  |  | no credential |
| none | P7 | skipped |  |  | no credential (`[agents] enabled = false` run) |
| none | P8 | skipped |  |  | no credential |
| none | P2-v2 | skipped |  |  | no credential (`features.multi_agent_v2=true` run) |

## Excerpts

The installed `agents/openai.yaml`:

```yaml
interface:
  display_name: "Verified Waves"
  short_description: "Run one task as verified multi-agent waves."
  default_prompt: "Use $deniz-process:verified-waves for this task."
policy:
  allow_implicit_invocation: false
```

## Notes

- **Isolation.** The structural runner's own checks hold: the fresh home started with no
  marketplace, the real profile's Codex plugin state was byte-identical before and after, and the
  repository status did not change.
- **Ledger and permission prompts.** None: no model turn ran, so no ledger directory was created
  and no prompt appeared. The listing of `verified-waves` directories below `<LAB>` stayed empty.
- **Dry run.** Isolation, the fixture baseline, and P0 were confirmed. The ledger location in the
  Codex sandbox, the P4 stop, and the P5 stop and reopen were not confirmed, because each needs a
  model session.
- **Multi-agent version.** Unmeasured. Which multi-agent tools a preset serves (D3) needs a model
  session; the V2 run of P2 is the panel's probe for it.
