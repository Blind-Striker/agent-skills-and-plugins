import assert from "node:assert/strict";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { buildAll } from "../build.ts";
import { makeRepo, opencodeIdPath } from "../testutil.ts";

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

  const alpha = ledger["deniz-process/skill/alpha"];
  assert.equal(alpha.source, "sp/skills/alpha");
  assert.equal(alpha.invocation, "auto");
  assert.deepEqual(alpha.claude.artifacts, ["skill"]);
  assert.deepEqual(alpha.claude.edges.model, ["deniz-process:beta"]);
  assert.deepEqual(alpha.opencode, {
    artifacts: ["skill"],
    identity: "deniz-process.alpha",
    advertised: true,
    edges: { model: ["deniz-process.beta"], pointer: [] },
    dropped: [],
  });
  assert.deepEqual(alpha.dependsOn, ["beta"]); // the manifest's declaration, beside the derived edges
  assert.deepEqual(alpha.codex, {
    artifacts: ["skill"],
    identity: "deniz-process:alpha",
    sourceKind: "skill",
    resolvedKind: "skill",
    emittedKind: "skill",
    implicit: "enabled",
    explicit: "available",
    policyFiles: [],
    edges: { model: ["$deniz-process:beta"], pointer: [] },
    dropped: [],
    bodyTransformations: [],
  });

  const beta = ledger["deniz-process/skill/beta"];
  assert.deepEqual(beta.claude.artifacts, ["skill"]); // Claude: manual is still a skill, flagged
  assert.ok(existsSync(opencodeIdPath(root, "deniz-process", "skill", "beta", "SKILL.md")));
  assert.ok(!existsSync(opencodeIdPath(root, "deniz-process", "command", "beta")));
  // OpenCode 2: manual stays a skill at its ID path and is hidden, not reshaped into a command.
  assert.deepEqual(beta.opencode, {
    artifacts: ["skill"],
    identity: "deniz-process.beta",
    advertised: false,
    edges: { model: [], pointer: [] },
    dropped: [],
  });
  assert.equal(beta.codex.implicit, "disabled");
  assert.equal(beta.codex.explicit, "available");
  assert.deepEqual(beta.codex.policyFiles, ["agents/openai.yaml"]);

  // determinism: a second build produces byte-identical content
  const first = readFileSync(join(root, "docs", "ledger.json"), "utf8");
  buildAll(root);
  assert.equal(readFileSync(join(root, "docs", "ledger.json"), "utf8"), first);
});

test("the ledger projects OpenCode 2 identity, advertisement, and dotted edges", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: A\n---\n\nUse superpowers:delta. Tell the user /superpowers:beta.\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: P",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    invocation: manual",
      "    depends_on: [delta]",
      "  - source: sp/skills/beta",
      "    invocation: both",
      "  - source: sp/skills/delta",
      "    invocation: auto",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const ledger = JSON.parse(readFileSync(join(root, "docs", "ledger.json"), "utf8"));
  const alpha = ledger["deniz-process/skill/alpha"].opencode;
  assert.deepEqual(alpha.artifacts, ["skill"]);
  assert.equal(alpha.identity, "deniz-process.alpha");
  assert.equal(alpha.advertised, false);
  assert.deepEqual(alpha.edges, { model: ["deniz-process.delta"], pointer: ["@deniz-process.beta"] });
  assert.equal("parked" in alpha, false);
  assert.equal(ledger["deniz-process/skill/delta"].opencode.advertised, true);
});

test("the ledger records Codex kind transformation and neutral-source metadata drops", () => {
  const root = makeRepo();

  buildAll(root);

  const ledger = JSON.parse(readFileSync(join(root, "docs", "ledger.json"), "utf8"));
  const agent = ledger["deniz-process/agent/beta-agent"];
  assert.equal(agent.codex.sourceKind, "skill");
  assert.equal(agent.codex.resolvedKind, "agent");
  assert.equal(agent.codex.emittedKind, "skill");
  assert.equal(agent.codex.kindTransformation, "agent->skill");
  assert.deepEqual(agent.codex.dropped, ["model"]);
});

test("the ledger records only boolean invocation flags from emitted Claude skills", () => {
  const root = makeRepo();
  const addSkill = (name: string, frontmatter: string) => {
    const dir = join(root, "external", "sp", "skills", name);
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, "SKILL.md"), `---\nname: ${name}\ndescription: ${name}\n${frontmatter}---\n\nBody.\n`);
  };
  addSkill("upstream-flag", "disable-model-invocation: true\n");
  addSkill("nonboolean-flag", 'user-invocable: "yes"\n');

  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    invocation: auto",
      "  - source: sp/skills/beta",
      "    invocation: manual",
      "  - source: sp/skills/delta",
      "    invocation: both",
      "  - source: sp/skills/gamma",
      "  - source: sp/skills/upstream-flag",
      "  - source: sp/skills/nonboolean-flag",
      "  - source: sp/skills/upstream-flag",
      "    as: command",
      "    name: upstream-flag-command",
      "  - source: sp/skills/upstream-flag",
      "    as: agent",
      "    name: upstream-flag-agent",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const ledger = JSON.parse(readFileSync(join(root, "docs", "ledger.json"), "utf8"));

  assert.deepEqual(ledger["deniz-process/skill/alpha"].claude.flags, { "user-invocable": false });
  assert.deepEqual(ledger["deniz-process/skill/beta"].claude.flags, { "disable-model-invocation": true });
  assert.equal("flags" in ledger["deniz-process/skill/delta"].claude, false, "both emits neither flag");
  assert.equal("flags" in ledger["deniz-process/skill/gamma"].claude, false, "absent intent adds no flag");
  assert.deepEqual(ledger["deniz-process/skill/upstream-flag"].claude.flags, {
    "disable-model-invocation": true,
  });
  assert.equal("flags" in ledger["deniz-process/skill/nonboolean-flag"].claude, false);
  assert.equal("flags" in ledger["deniz-process/command/upstream-flag-command"].claude, false);
  assert.equal("flags" in ledger["deniz-process/agent/upstream-flag-agent"].claude, false);
});
