import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { assembleItem } from "./lib/assemble.ts";
import { parseDoc } from "./lib/frontmatter.ts";
import { loadManifest } from "./lib/manifest.ts";
import { resolveItem } from "./lib/resolve.ts";
import { scanSubmodule } from "./lib/scan.ts";

const root = join(import.meta.dirname, "..");

function body(name: string): string {
  const manifest = loadManifest(join(root, "curation", "deniz-process.yaml"));
  const components = scanSubmodule(join(root, "external"), "superpowers");
  const item = manifest.items.find(
    (candidate) => resolveItem(root, manifest.plugin.name, candidate, components).outName === name,
  );
  assert.ok(item, `missing curated ${name}`);
  const staging = mkdtempSync(join(tmpdir(), "process-scaling-"));
  try {
    const assembled = assembleItem(root, staging, manifest, item, components);
    assert.ok(assembled, `${name} must be taken`);
    return parseDoc(readFileSync(join(assembled.dir, "SKILL.md"), "utf8")).body;
  } finally {
    rmSync(staging, { recursive: true, force: true });
  }
}

function section(text: string, heading: string): string {
  const start = text.indexOf(`## ${heading}\n`);
  assert.ok(start >= 0, `missing ${heading} gate`);
  const end = text.indexOf("\n## ", start + 1);
  return text.slice(start, end < 0 ? undefined : end).replace(/\s+/g, " ");
}

test("writing-plans sizes work before creating a plan artifact", () => {
  const text = body("writing-plans");
  const gate = section(text, "Size before ceremony");
  assert.match(gate, /one sitting/);
  assert.match(gate, /ordinary implementation/);
  assert.match(gate, /do not create a plan document/);
  assert.match(gate, /explicitly asks for a written plan/);
  assert.ok(text.indexOf("## Size before ceremony") < text.indexOf("## File Structure"));
});

test("writing-plans preserves execution authority without a duplicate approval gate", () => {
  const handoff = section(body("writing-plans"), "Execution Handoff");
  assert.match(handoff, /plan-only request/);
  assert.match(handoff, /already requested execution/);
  assert.match(handoff, /do not ask for the same approval/);
  assert.match(handoff, /material change/);
  assert.match(handoff, /inline execution/);
  assert.doesNotMatch(handoff, /wait for that review before implementation/);
});

test("SDD gates per-task orchestration without weakening its selected heavy workflow", () => {
  const text = body("subagent-driven-development");
  const gate = section(text, "Size before orchestration");
  assert.match(gate, /explicitly chose/);
  assert.match(gate, /substantial independent tasks/);
  assert.match(gate, /small or coupled/);
  assert.match(gate, /ordinary inline work/);
  assert.match(gate, /single final review/);
  assert.match(gate, /do not create a plan, ledger, or child/);
  assert.ok(text.indexOf("## Size before orchestration") < text.indexOf("## The Process"));
  assert.match(section(text, "The Task Loop"), /even a one-line fix runs the whole suite/);
});

test("selected SDD honors an already approved current checkout", () => {
  const setup = section(body("subagent-driven-development"), "Setup");
  assert.match(setup, /approved current checkout/);
  assert.match(setup, /do not ask again/);
  assert.match(setup, /isolation is requested/);
  assert.doesNotMatch(setup, /Ensure the work happens in an isolated workspace/);
});

test("inline execution does not turn small work or an approved checkout into another ceremony", () => {
  const text = body("executing-plans");
  const gate = section(text, "Size before execution setup");
  assert.match(gate, /one sitting/);
  assert.match(gate, /do not create another plan or execution workspace/);
  assert.match(gate, /one final review/);
  const setup = section(text, "Setup");
  assert.match(setup, /approved current checkout/);
  assert.match(setup, /do not ask again/);
  assert.doesNotMatch(setup, /Ensure the work happens in an isolated workspace/);
});

test("worktree setup needs an isolation reason and preserves creation consent", () => {
  const text = body("using-git-worktrees");
  const gate = section(text, "Isolation before setup");
  assert.match(gate, /existing working-directory choice/);
  assert.match(gate, /requested or a concrete conflict/);
  assert.match(gate, /return without creating a worktree/);
  assert.match(gate, /do not ask again/);
  assert.ok(text.indexOf("## Isolation before setup") < text.indexOf("## Step 0"));
  assert.match(section(text, "Step 0: Detect Existing Isolation"), /ask for consent before creating a worktree/);
});
