---
record_id: verified-waves-claude-2026-10-12
date: 2026-10-12
repo_head: 8bc8da3bf070d14f568d1000c1b5b428473f0f0f
kind: model-panel
summary: In an isolated Claude Code 2.1.296 config with the checkout's Process 0.10.0 plugin mounted, the init event listed deniz-process:verified-waves and an unmounted control listed no deniz item; P0's model-surface half was skipped, because no credential was supplied out of band and this run did not seed the lab from the real profile, and P1 to P8 are not run, by curator decision, because Claude Code uses its native Workflow tool, so the panel measured nothing about the skill's behaviour in Claude Code.
isolation_ok: true
fixture_sha: 0a83bab
harness_name: claude-code
harness_version: 2.1.296
runner_revision: sha256:0ea49b2506e04c3bb9f742f65697b53deba16432257afb1b5aa094c301172085
---

# verified-waves forward-test panel: Claude Code

This tier-2 record is the Claude Code leg of the
[verified-waves panel](../verified-waves-panel.md). The panel owns the method, the prompts, and the
answer key. `runner_revision` is the SHA-256 of `verified-waves-panel.md` followed by
`fixtures/make-fixture.ps1`, both read at `repo_head` with a clean tree, as the panel's Recording
section specifies. `<LAB>` below is the external lab root, outside the repository and the user
profile. Raw event streams stay in the lab.

## Proof limit

Only P0's structural half ran. Every prompt that needs a model turn is `skipped`:

- **P1 to P8: not run, by curator decision.** Claude Code uses its native Workflow tool, so the
  curator decided that the panel's model prompts are not run in Claude Code. This run also had no
  person at the terminal for the operator steps of P4, P5, P6, and P7c.
- **P0's model-surface half: no credential.** The lab's `CLAUDE_CONFIG_DIR` held no credential,
  and neither `ANTHROPIC_API_KEY` nor `CLAUDE_CODE_OAUTH_TOKEN` was supplied out of band. The only
  other source on the machine is the real profile's rotating OAuth file. This run's safety rule
  forbids touching the curator's real profile, and the protocol warns that concurrent copies of
  that file can invalidate one another ([Isolate](../protocol.md#isolate)). So the lab was not
  seeded from it.

To complete P0, seed the lab once out of band (`claude setup-token` and
`CLAUDE_CODE_OAUTH_TOKEN`, or a lab-only login) and run its `-p` enumeration with the unmounted
control, as the panel describes.

## Method

1. **Lab.** `<LAB>/.claude-home` was `CLAUDE_CONFIG_DIR`. `CLAUDE_CODE_DISABLE_AUTO_MEMORY=1`
   was set, and `TEMP`, `TMP`, and `TMPDIR` pointed at `<LAB>/.tmp`, as the panel's Setup
   specifies. The process environment was an allowlist: system variables, a minimal `Path`, and
   those lab variables. No token, API key, or Claude Code session variable from the operator's
   shell reached it.
2. **Fixture.** `make-fixture.ps1 -Set waves` built `<LAB>/project/waves-repo` at `0a83bab`, the
   value in `WAVES_BASELINE`. `npm test` passed there (6 tests). The process started in that
   directory.
3. **P0, structural half.** One call with the plugin mounted, and one unmounted control:

   ```text
   claude --output-format stream-json --verbose -p hi --plugin-dir <checkout>/plugins/deniz-process --add-dir <checkout>/plugins/deniz-process
   claude --output-format stream-json --verbose -p hi
   ```

   Both emitted their `system`/`init` event before the login check ended the call.

## Results

| model | probe_id | status | cost | tools_observed | notes |
|---|---|---|---:|---|---|
| none | P0 | skipped |  | none | Structural half observed: `init` lists `deniz-process:verified-waves` in `slash_commands` and `skills`, and `plugins` names `deniz-process` 0.10.0 (`deniz-process@inline`); the unmounted control lists no `deniz-*` item. The model-surface half (a `-p` enumeration of model-callable skills) needs a model turn: no credential |
| none | P1 | skipped |  |  | not run, curator decision: Claude Code uses its native Workflow tool |
| none | P2 | skipped |  |  | not run, curator decision: Claude Code uses its native Workflow tool |
| none | P3 | skipped |  |  | not run, curator decision: Claude Code uses its native Workflow tool |
| none | P4 | skipped |  |  | not run, curator decision: Claude Code uses its native Workflow tool |
| none | P5a | skipped |  |  | not run, curator decision: Claude Code uses its native Workflow tool |
| none | P5b | skipped |  |  | not run, curator decision: Claude Code uses its native Workflow tool |
| none | P6 | skipped |  |  | not run, curator decision: Claude Code uses its native Workflow tool |
| none | P7a | skipped |  |  | not run, curator decision: Claude Code uses its native Workflow tool |
| none | P7b | skipped |  |  | not run, curator decision: Claude Code uses its native Workflow tool |
| none | P7c | skipped |  |  | not run, curator decision: Claude Code uses its native Workflow tool |
| none | P8 | skipped |  |  | not run, curator decision: Claude Code uses its native Workflow tool |

## Excerpts

The mounted call's `init` event, cut to the fields the panel reads:

```json
{"type":"system","subtype":"init","cwd":"<LAB>\\project\\waves-repo","mcp_servers":[],"permissionMode":"auto",
 "tools":["Task","Bash","CronCreate","CronDelete","CronList","DesignSync","Edit","EnterWorktree","ExitWorktree","Glob","Grep","ListAgents","NotebookEdit","Read","ReportFindings","ScheduleWakeup","SendMessage","Skill","TaskStop","ToolSearch","WebFetch","WebSearch","Workflow","Write"],
 "slash_commands":["…","deniz-process:using-superpowers","deniz-process:verified-waves","deniz-process:wait-what","…"],
 "apiKeySource":"none","claude_code_version":"2.1.296",
 "plugins":[{"name":"deniz-process","source":"deniz-process@inline","version":"0.10.0"},{"name":"cc-plugin-agents-md","path":"builtin"},{"name":"cc-plugin-telemetry","path":"builtin"},{"name":"cc-plugin-plugin-authoring","path":"builtin"}]}
```

The call then ended with the synthetic assistant text `Not logged in · Please run /login` and a
`result` event with `is_error: true` and `total_cost_usd: 0`.

## Notes

- **Isolation.** The positive control holds: with nothing mounted, `plugins` lists only the three
  built-in plugins and `mcp_servers` is empty; with the plugin mounted, only `deniz-process` is
  added. `apiKeySource` is `none` in both. The `init` event of 2.1.296 carries no `memory_paths`
  field, and no memory Markdown file appeared under `<LAB>/.claude-home`.
- **Ledger and permission prompts.** None: no model turn ran, so no ledger directory was created
  and no prompt appeared. The listing of `verified-waves` directories below `<LAB>` stayed empty.
- **Dry run.** Isolation, the fixture baseline, and P0's structural half were confirmed. The ledger
  location, the P4 stop, and the P5 stop and reopen were not confirmed, because each needs a model
  turn.
- **Observed: the subagent tool's name.** The `init` tool list holds `Task` and no `Agent`, with
  `Workflow` present. That matches the documentation and does not contradict the skill's Claude
  notes, which say "background `Agent` calls" for the D4 fallback. The
  [Agent SDK TypeScript reference](https://code.claude.com/docs/en/agent-sdk/typescript#agent)
  says: "Tool name: `Agent`. The previous name `Task` is still accepted as an alias, and the
  `tools` array in the `SDKSystemMessage` init message currently lists this tool as `Task` for
  backward compatibility." The [sub-agents documentation](https://code.claude.com/docs/en/sub-agents)
  dates the rename to 2.1.63 ("Restrict which subagents can be spawned") and shows
  `--disallowedTools "Agent(Explore)"` ("Disable specific subagents"). Both were read on
  2026-10-12. No model turn ran here, and P7b is not run (curator decision), so
  `--disallowedTools Agent` is unexercised.
- **Observed: the permission mode.** A fresh config reported `permissionMode: "auto"` in `-p` mode.
  The panel's P7c needs Manual mode; P7c is not run here (curator decision).
