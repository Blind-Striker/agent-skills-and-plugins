import assert from "node:assert/strict";
import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { stringify } from "yaml";
import { buildAll } from "../build.ts";
import { addOriginalSkill, makeRepo, opencodeIdPath } from "../testutil.ts";
import { validateRepo } from "../validate.ts";
import { loadManifest } from "./manifest.ts";

function optionalRepo(body: string, optional: string[] = ["remote"]): string {
  const root = makeRepo();
  rmSync(join(root, "overlays"), { recursive: true, force: true });
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    stringify({
      plugin: { name: "deniz-process", description: "Process", version: "0.1.0" },
      items: [{ source: "sp/skills/alpha", ...(optional.length ? { optional_refs: optional } : {}) }],
    }),
  );
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    `---\nname: alpha\ndescription: Alpha\n---\n${body}\n`,
  );
  writeFileSync(
    join(root, "curation", "deniz-general.yaml"),
    stringify({
      plugin: { name: "deniz-general", description: "General", version: "0.1.0" },
      items: [],
    }),
  );
  addOriginalSkill(root, "remote", { plugin: "deniz-general", declaration: { invocation: "both" } });
  return root;
}

const guarded = "## General\nApplies only when the `deniz-general` Module is installed.\n\nUse deniz-general:remote.";
const errors = (root: string): string[] =>
  validateRepo(root)
    .filter((f) => f.level === "error")
    .map((f) => f.message);

function changeManifest(
  root: string,
  module: string,
  change: (manifest: ReturnType<typeof loadManifest>) => void,
): void {
  const path = join(root, "curation", `${module}.yaml`);
  const manifest = loadManifest(path);
  change(manifest);
  writeFileSync(path, stringify(manifest));
}

test("buildAll and validateRepo accept a guarded optional model fact without a mandatory Module", () => {
  const root = optionalRepo(guarded);
  buildAll(root);
  assert.deepEqual(errors(root), []);
  assert.deepEqual(
    JSON.parse(readFileSync(join(root, "opencode", "deniz-process", "manifest.json"), "utf8")).requiredModules,
    [],
  );
  assert.match(
    readFileSync(opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md"), "utf8"),
    /Use deniz-general\.remote/,
  );
  assert.match(
    readFileSync(join(root, "codex", "deniz-process", "skills", "alpha", "SKILL.md"), "utf8"),
    /Use \$deniz-general:remote/,
  );
});

test("buildAll refuses an unguarded optional fact before deleting any old output", () => {
  const root = optionalRepo("Use deniz-general:remote.");
  for (const tree of ["plugins", "opencode", "codex"]) {
    mkdirSync(join(root, tree), { recursive: true });
    writeFileSync(join(root, tree, "sentinel.txt"), "keep me");
  }
  assert.throws(() => buildAll(root), /optional.*remote.*guard.*deniz-general/);
  for (const tree of ["plugins", "opencode", "codex"]) {
    assert.equal(readFileSync(join(root, tree, "sentinel.txt"), "utf8"), "keep me");
  }
});

test("validateRepo independently rejects a removed emitted guard", () => {
  const root = optionalRepo(guarded);
  buildAll(root);
  const path = join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md");
  writeFileSync(
    path,
    readFileSync(path, "utf8").replace("Applies only when the `deniz-general` Module is installed.", "Always applies."),
  );
  assert.ok(errors(root).some((message) => /optional.*remote.*guard.*deniz-general/.test(message)));
});

test("optional declarations require an existing different-Module target and an actual shipped fact", () => {
  for (const [body, names, pattern] of [
    [guarded, ["absent"], /unknown optional_refs target absent/],
    [guarded, ["alpha"], /optional_refs target alpha.*same Module/],
    ["No reference.", ["remote"], /stale optional_refs: remote/],
    [guarded.replace("deniz-general:remote", "deniz-process:remote"), ["remote"], /namespace.*deniz-process:remote/],
  ] as const) {
    const root = optionalRepo(body, [...names]);
    assert.throws(() => buildAll(root), pattern);
  }
  const root = optionalRepo(guarded);
  buildAll(root);
  changeManifest(root, "deniz-process", (manifest) => {
    const item = manifest.items[0];
    assert.ok(item);
    item.optional_refs = ["absent"];
  });
  assert.ok(errors(root).some((message) => /unknown optional_refs target absent/.test(message)));
});

test("cross-Module user pointers into a nonrequired Module must declare optional intent", () => {
  const root = optionalRepo(guarded.replace("Use deniz-general:remote", "Use /deniz-general:remote"), []);
  assert.throws(() => buildAll(root), /undeclared optional reference.*remote/);
  const built = optionalRepo(guarded.replace("Use deniz-general:remote", "Use /deniz-general:remote"));
  buildAll(built);
  changeManifest(built, "deniz-process", (manifest) => {
    delete manifest.items[0]?.optional_refs;
  });
  assert.ok(errors(built).some((message) => /undeclared optional reference.*remote/.test(message)));
});

test("optional model facts cannot reach a manual target but guarded user pointers can", () => {
  const root = optionalRepo(guarded);
  changeManifest(root, "deniz-general", (manifest) => {
    const remote = manifest.original_skills?.remote;
    assert.ok(remote);
    remote.invocation = "manual";
  });
  assert.throws(() => buildAll(root), /model-edge.*remote.*cannot reach/);
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    `---\nname: alpha\ndescription: Alpha\n---\n${guarded.replace("Use deniz-general:remote", "Use /deniz-general:remote")}\n`,
  );
  buildAll(root);
  assert.deepEqual(errors(root), []);
  assert.match(
    readFileSync(opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md"), "utf8"),
    /Use @deniz-general\.remote/,
  );
  const path = join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md");
  writeFileSync(path, readFileSync(path, "utf8").replace("/deniz-general:remote", "deniz-general:remote"));
  assert.ok(errors(root).some((message) => /model-edge.*remote/.test(message)));
});

test("an HTML code heading imitation cannot guard an optional fact", () => {
  const body =
    "<code>\n## General\nApplies only when the `deniz-general` Module is installed.\n</code>\nUse deniz-general:remote.";
  const root = optionalRepo(body);
  assert.throws(() => buildAll(root), /optional.*remote.*guard/);
});

test("section guards inherit through descendants and end at real siblings, not literal headings", () => {
  for (const body of [
    guarded.replace("Use", "### Nested\nUse"),
    guarded.replace("Use", "```md\n## Literal sibling\n```\nUse"),
    guarded.replace("Use", "    ## Literal sibling\n\nUse"),
    guarded.replace("Use", "> ## Quoted sibling\n\nUse"),
    guarded.replace(/\n/g, "\r\n"),
    "## deniz-general:remote\nApplies only when the `deniz-general` Module is installed.",
  ]) {
    const root = optionalRepo(body);
    buildAll(root);
    assert.deepEqual(errors(root), [], body);
  }
  for (const body of [
    guarded.replace("deniz-general`", "deniz-process`"),
    guarded.replace("deniz-general`", "deniz-unknown`"),
    guarded.replace("Applies only", "Intervening prose.\nApplies only"),
    guarded.replace("Use", "## Sibling\nUse"),
    guarded.replace("Use", "# Parent\nUse"),
    `\`\`\`md\n${guarded}\n\`\`\`\nUse deniz-general:remote.`,
    "> ## General\n> Applies only when the `deniz-general` Module is installed.\nUse deniz-general:remote.",
    "    ## General\n    Applies only when the `deniz-general` Module is installed.\n\nUse deniz-general:remote.",
  ]) {
    assert.throws(() => buildAll(optionalRepo(body)), /optional.*remote.*guard/, body);
  }
  const root = optionalRepo(guarded);
  const source = join(root, "external", "sp", "skills", "alpha", "SKILL.md");
  writeFileSync(
    source,
    readFileSync(source, "utf8").replace("description: Alpha", "description: deniz-general:remote"),
  );
  assert.throws(() => buildAll(root), /optional.*remote.*guard/);
});

test("ledger sorts optionalRefs for original skills and keeps them separate from mandatory dependencies", () => {
  const root = optionalRepo("No references.", []);
  addOriginalSkill(root, "zebra", { plugin: "deniz-general" });
  addOriginalSkill(root, "router", {
    declaration: { invocation: "both", optional_refs: ["zebra", "remote"] },
    body: `${guarded}\nUse /deniz-general:zebra.\n`,
  });
  buildAll(root);
  assert.deepEqual(errors(root), []);
  const ledger = JSON.parse(readFileSync(join(root, "docs", "ledger.json"), "utf8"));
  assert.deepEqual(ledger["deniz-process/skill/router"].optionalRefs, ["remote", "zebra"]);
  assert.equal(ledger["deniz-process/skill/router"].dependsOn, undefined);
  assert.equal(ledger["deniz-general/skill/remote"].optionalRefs, undefined);
  assert.deepEqual(ledger["deniz-process/skill/router"].claude.edges, {
    model: ["deniz-general:remote"],
    pointer: ["deniz-general:zebra"],
  });
  assert.deepEqual(ledger["deniz-process/skill/router"].opencode.edges, {
    model: ["deniz-general.remote"],
    pointer: ["@deniz-general.zebra"],
  });
});

test("an excluded target cannot satisfy optional_refs even while its old output still exists", () => {
  const root = optionalRepo(guarded);
  rmSync(join(root, "skills", "deniz-general", "remote"), { recursive: true });
  changeManifest(root, "deniz-general", (manifest) => {
    delete manifest.original_skills;
    manifest.items = [{ source: "sp/skills/beta", name: "remote", invocation: "both" }];
  });
  buildAll(root);
  changeManifest(root, "deniz-general", (manifest) => {
    const item = manifest.items[0];
    assert.ok(item);
    item.exclude = true;
  });
  assert.throws(() => buildAll(root), /unknown optional_refs target remote/);
  assert.ok(errors(root).some((message) => /unknown optional_refs target remote/.test(message)));
});

test("validateRepo resolves a fact in its named namespace, not another Module with the same bare name", () => {
  const root = optionalRepo("No reference.", []);
  buildAll(root);
  const path = join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md");
  writeFileSync(path, `${readFileSync(path, "utf8")}\nUse /deniz-process:remote.\n`);
  assert.ok(errors(root).some((message) => /dangling reference deniz-process:remote/.test(message)));
});

test("a Module required by an independent edge still wins over another item's optional intent", () => {
  const root = optionalRepo(guarded);
  addOriginalSkill(root, "required-router", {
    declaration: { invocation: "both", depends_on: ["remote"] },
    body: "Use deniz-general:remote.",
  });
  addOriginalSkill(root, "ordinary-pointer", { body: "Use /deniz-general:remote." });
  addOriginalSkill(root, "local-pointer", { body: "Use /superpowers:alpha." });
  buildAll(root);
  assert.deepEqual(errors(root), []);
  assert.deepEqual(
    JSON.parse(readFileSync(join(root, "opencode", "deniz-process", "manifest.json"), "utf8")).requiredModules,
    ["deniz-general"],
  );
  const path = join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md");
  writeFileSync(
    path,
    readFileSync(path, "utf8").replace("Applies only when the `deniz-general` Module is installed.", "Always."),
  );
  assert.ok(
    errors(root).some((message) => /optional.*remote.*guard/.test(message)),
    "another mandatory edge does not remove this edge's guard requirement",
  );
});

test("harness-only optional files check only their effective audience and never unshipped variants", () => {
  for (const original of [false, true]) {
    const root = optionalRepo("No reference.", original ? [] : ["remote"]);
    changeManifest(root, "deniz-general", (manifest) => {
      const remote = manifest.original_skills?.remote;
      assert.ok(remote);
      remote.invocation = "auto";
    });
    const body = guarded.replace("Use deniz-general:remote", "Use /deniz-general:remote");
    const mapping = { "references/notes.md": { claude: null, opencode: null, codex: "references/codex.md" } };
    let source: string;
    if (original) {
      addOriginalSkill(root, "router", {
        declaration: { invocation: "both", optional_refs: ["remote"], harness_files: mapping },
        files: { "references/codex.md": body },
      });
      source = join(root, "skills", "deniz-process", "router", "references", "codex.md");
    } else {
      source = join(root, "external", "sp", "skills", "alpha", "references", "codex.md");
      mkdirSync(join(root, "external", "sp", "skills", "alpha", "references"), { recursive: true });
      writeFileSync(source, body);
      changeManifest(root, "deniz-process", (manifest) => {
        const item = manifest.items[0];
        assert.ok(item);
        item.harness_files = mapping;
      });
    }
    buildAll(root);
    assert.deepEqual(errors(root), [], String(original));
    writeFileSync(source, "No references.\n");
    assert.throws(() => buildAll(root), /stale optional_refs: remote/);
    assert.ok(errors(root).some((message) => /stale optional_refs: remote/.test(message)));
  }
});

test("validateRepo checks optional guards in current native OpenCode and Codex output too", () => {
  for (const harness of ["opencode", "codex"] as const) {
    const root = optionalRepo(guarded);
    buildAll(root);
    const path =
      harness === "opencode"
        ? opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md")
        : join(root, "codex", "deniz-process", "skills", "alpha", "SKILL.md");
    writeFileSync(
      path,
      readFileSync(path, "utf8").replace("Applies only when the `deniz-general` Module is installed.", "Always."),
    );
    assert.ok(errors(root).some((message) => message.includes(harness) && /optional.*remote.*guard/.test(message)));
  }
});

test("prospective preflight does not read optional facts in frontmatter that no harness ships", () => {
  const root = optionalRepo(guarded);
  const path = join(root, "external", "sp", "skills", "alpha", "SKILL.md");
  writeFileSync(
    path,
    readFileSync(path, "utf8").replace("description: Alpha", "description: Alpha\nunread-key: deniz-general:remote"),
  );
  buildAll(root);
  assert.deepEqual(errors(root), []);
});

test("a conditional fact in a converted item's Codex-only bundled Markdown is linked in Codex alone", () => {
  const root = optionalRepo("No reference.");
  changeManifest(root, "deniz-process", (manifest) => {
    const item = manifest.items[0];
    assert.ok(item);
    item.as = "agent";
  });
  changeManifest(root, "deniz-general", (manifest) => {
    const remote = manifest.original_skills?.remote;
    assert.ok(remote);
    remote.invocation = "auto";
  });
  const dir = join(root, "external", "sp", "skills", "alpha", "references");
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, "notes.md"), guarded.replace("Use deniz-general:remote", "Use /deniz-general:remote"));
  buildAll(root);
  assert.deepEqual(errors(root), []);
  const ledger = JSON.parse(readFileSync(join(root, "docs", "ledger.json"), "utf8"));
  assert.deepEqual(ledger["deniz-process/agent/alpha"].codex.edges.pointer, ["$deniz-general:remote"]);
  assert.deepEqual(ledger["deniz-process/agent/alpha"].claude.edges.pointer, []);
});

test("nested guards can establish another Module scope and return to the ancestor at its sibling", () => {
  const root = optionalRepo(
    `${guarded}\n### Extra\nApplies only when the \`deniz-extra\` Module is installed.\nUse /deniz-extra:extra.\n### General again\nUse deniz-general:remote.`,
    ["extra", "remote"],
  );
  writeFileSync(
    join(root, "curation", "deniz-extra.yaml"),
    stringify({ plugin: { name: "deniz-extra", description: "Extra", version: "0.1.0" }, items: [] }),
  );
  addOriginalSkill(root, "extra", { plugin: "deniz-extra", declaration: { invocation: "manual" } });
  buildAll(root);
  assert.deepEqual(errors(root), []);
});

test("upstream optional targets localize to their unique output owner and excluded or ambiguous names fail", () => {
  const root = optionalRepo(guarded.replace("deniz-general:remote", "superpowers:beta"));
  rmSync(join(root, "skills", "deniz-general", "remote"), { recursive: true });
  changeManifest(root, "deniz-general", (manifest) => {
    delete manifest.original_skills;
    manifest.items = [{ source: "sp/skills/beta", name: "remote", invocation: "both" }];
  });
  buildAll(root);
  assert.deepEqual(errors(root), []);
  assert.match(
    readFileSync(opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md"), "utf8"),
    /Use deniz-general\.remote/,
  );
  addOriginalSkill(root, "remote", { plugin: "deniz-process" });
  assert.throws(() => buildAll(root), /duplicate output name remote/);
});

test("unshipped Markdown variants cannot satisfy an optional declaration", () => {
  const root = optionalRepo("No references.");
  const dir = join(root, "external", "sp", "skills", "alpha", "references");
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, "variant.md"), guarded);
  changeManifest(root, "deniz-process", (manifest) => {
    const item = manifest.items[0];
    assert.ok(item);
    item.harness_files = { "references/literal.txt": { claude: null, opencode: null, codex: "references/variant.md" } };
  });
  assert.throws(() => buildAll(root), /stale optional_refs: remote/);
});

test("rendered guard checks do not promote an unrelated dotted prose name into an optional fact", () => {
  const root = optionalRepo(`${guarded}\n## Ordinary prose\nAn example.remote identifier is not an estate fact.`);
  buildAll(root);
  assert.deepEqual(errors(root), []);
});
