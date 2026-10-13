import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";

const skillDir = join(import.meta.dirname, "..", "skills", "deniz-process", "verified-waves");

function between(text: string, start: string, end: string): string {
  const from = text.indexOf(start);
  assert.ok(from >= 0, `missing section start: ${start}`);
  const rest = text.slice(from);
  const to = rest.indexOf(end, start.length);
  assert.ok(to >= 0, `missing section end: ${end}`);
  return rest.slice(0, to);
}

test("verified-waves restart is a policy, not a claim that every old child is unreachable", () => {
  const skillRestart = between(readFileSync(join(skillDir, "SKILL.md"), "utf8"), "## Restart", "## Final report");
  assert.doesNotMatch(skillRestart, /cannot be reached/);
  assert.match(skillRestart, /session that started it is\s+still live/);
  assert.match(skillRestart, /After a session restart, do not continue\s+the old child/);
  assert.match(skillRestart, /dispatch it again as a new child/);

  const codex = readFileSync(join(skillDir, "references", "codex-tools.md"), "utf8");
  const codexRestart = between(codex, "## Restart", "## The ledger");
  assert.doesNotMatch(codexRestart, /cannot be reached/);
  assert.match(codexRestart, /`resume_agent` can\s+reopen a saved thread from its rollout/);
  assert.match(codexRestart, /do not use that after a\s+(?:session |parent )?restart/);
  assert.match(codexRestart, /followup_task/);
  assert.match(codexRestart, /dispatch\s+it again as a new child/);
  assert.match(between(codex, "## V1", "## V2"), /only within the live session/);
});

test("verified-waves Claude sandbox notes do not treat every DNS failure as a denial", () => {
  const sandbox = between(
    readFileSync(join(skillDir, "references", "claude-code-tools.md"), "utf8"),
    "## Commands the sandbox refuses",
    "## Restart",
  );
  assert.match(sandbox, /Operation not permitted/);
  assert.match(sandbox, /Read-only file system/);
  assert.match(sandbox, /Could not resolve host/);
  assert.match(sandbox, /Network is unreachable/);
  assert.match(sandbox, /do not\s+treat every such failure as a sandbox denial/s);
  assert.match(sandbox, /confirmed sandbox denial/);
  assert.match(sandbox, /once/);
  assert.match(sandbox, /dangerouslyDisableSandbox/);
  assert.match(sandbox, /Do not disable the sandbox or change its\s+configuration/);
  assert.doesNotMatch(sandbox, /every (?:DNS|network) failure is a sandbox denial/i);
});

test("verified-waves OpenCode question dismissal is not approval", () => {
  const questions = between(
    readFileSync(join(skillDir, "references", "opencode-tools.md"), "utf8"),
    "## Questions",
    "## Commands the sandbox refuses",
  );
  assert.doesNotMatch(questions, /returns only when the user has answered/);
  assert.match(questions, /dismiss/);
  assert.match(questions, /not approval/);
  assert.match(questions, /explicit answer/);
  assert.match(questions, /do not treat (?:a )?dismissal as yes/i);
  assert.doesNotMatch(questions, /dismissal is (?:a )?yes|treat dismissal as approval/i);
});

test("verified-waves Codex approval notes honor the active policy without old on-failure semantics", () => {
  const sandbox = between(
    readFileSync(join(skillDir, "references", "codex-tools.md"), "utf8"),
    "## Commands the sandbox refuses",
    "## Restart",
  );
  assert.match(sandbox, /active approval policy/);
  assert.match(sandbox, /tool schema/);
  assert.match(sandbox, /alias `on-failure`/);
  assert.match(sandbox, /not an automatic retry\s+outside the sandbox/);
  assert.match(sandbox, /`untrusted`/);
  assert.match(sandbox, /`sandbox_approval`/);
  assert.match(sandbox, /require_escalated/);
  assert.match(sandbox, /NOT RUN/);
  assert.match(sandbox, /BLOCKED/);
  assert.match(sandbox, /not FAIL/);
  assert.match(sandbox, /Do not ask to change the approval policy/);
  assert.doesNotMatch(sandbox, /on-failure means (?:an )?automatic/i);
  assert.doesNotMatch(sandbox, /automatically retry outside the sandbox/i);
  assert.doesNotMatch(sandbox, /escalation is allowed under every policy/i);
  assert.doesNotMatch(sandbox, /ask the user to change the approval policy/i);
});

test("verified-waves brief forbids task questions and still allows the escalation prompt", () => {
  const skill = readFileSync(join(skillDir, "SKILL.md"), "utf8");
  const briefs = between(skill, "## Briefs and reports", "## Running waves");
  assert.doesNotMatch(briefs, /DO NOT:[^\n]*ask the user(?! a task or scope question)/);
  assert.match(briefs, /ask the user a task or scope question/);
  assert.match(briefs, /send that question back as STATUS: BLOCKED/);
  assert.match(briefs, /harness escalation prompt[\s\S]{0,80}is allowed/);
  assert.doesNotMatch(briefs, /do not request the harness's escalation/i);
  assert.match(
    between(skill, "## Plan before any dispatch", "## Pick the shape"),
    /Ask one yes\/no question before each of these/,
  );
});

test("verified-waves runbook discovery does not claim the OpenCode temp-dir root moved", () => {
  const runbook = readFileSync(
    join(import.meta.dirname, "..", "experiments", "harness-invocation", "runbook.md"),
    "utf8",
  );
  const discovery = between(runbook, "A rollout checks discovery", "To measure installed output");
  assert.match(discovery, /XDG_DATA_HOME/);
  assert.match(discovery, /XDG_STATE_HOME/);
  assert.match(discovery, /XDG_CACHE_HOME/);
  assert.match(discovery, /OPENCODE_DB/);
  assert.match(discovery, /does not move OpenCode's temp-dir root/);
  assert.match(discovery, /path\.join\(os\.tmpdir\(\), "opencode"\)/);
  assert.match(discovery, /not total write isolation/);
  assert.doesNotMatch(discovery, /Point every root that the server writes/);
  assert.doesNotMatch(discovery, /\| `TEMP` \|/);
  assert.doesNotMatch(discovery, /\| `TMPDIR` \|/);
});
