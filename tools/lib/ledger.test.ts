import assert from "node:assert/strict";
import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { buildAll } from "../build.ts";
import { makeRepo } from "../testutil.ts";

test("the build writes a deterministic ledger describing each item's resolved state per harness", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha", // fixture body references superpowers:beta
      "    invocation: auto",
      "    depends_on: [beta]",
      "  - source: sp/skills/beta",
      "    invocation: manual",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const ledger = JSON.parse(readFileSync(join(root, "docs", "ledger.json"), "utf8"));

  const alpha = ledger["deniz-process/alpha"];
  assert.equal(alpha.source, "sp/skills/alpha");
  assert.equal(alpha.invocation, "auto");
  assert.deepEqual(alpha.claude.artifacts, ["skill"]);
  assert.deepEqual(alpha.claude.edges.model, ["deniz-process:beta"]);
  assert.deepEqual(alpha.opencode.edges.model, ["beta"]);
  assert.deepEqual(alpha.dependsOn, ["beta"]); // the manifest's declaration, beside the derived edges

  const beta = ledger["deniz-process/beta"];
  assert.deepEqual(beta.claude.artifacts, ["skill"]); // Claude: manual is still a skill, flagged
  assert.deepEqual(beta.opencode.artifacts, ["command"]); // OpenCode: manual is a command, no skill
  assert.deepEqual(alpha.opencode.dropped, ["user-invocable"]); // auto's Claude flag has no OpenCode home
  assert.deepEqual(beta.opencode.dropped, ["disable-model-invocation", "name"]); // command keeps description only

  // determinism: a second build produces byte-identical content
  const first = readFileSync(join(root, "docs", "ledger.json"), "utf8");
  buildAll(root);
  assert.equal(readFileSync(join(root, "docs", "ledger.json"), "utf8"), first);
});
