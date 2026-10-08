import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { stringify } from "yaml";
import { buildAll } from "./build.ts";
import { digestModulePayload, loadModuleManifest } from "./lib/opencode-bundle.ts";
import { loadManifest } from "./lib/manifest.ts";
import { loadLock, lockKey, saveLock, stampFiles } from "./lib/overlay.ts";
import { codexPluginPath, makeRepo, opencodeIdPath, opencodeModulePath } from "./testutil.ts";
import { scanOpenCodeIds, skillToolHandles, validateRepo } from "./validate.ts";

// The fixture curates sp/skills/beta twice, so the rewrite (last-write-wins) points alpha's
// model-edge at an AGENT — an edge the model cannot traverse, and undeclared besides. That debt is
// the linker working, not a regression: it is tolerated by name so every OTHER error class stays a
// hard zero on a clean build.
const FIXTURE_DEBT = ["model-edge to a target the model cannot reach: beta-agent", "undeclared dependency: beta-agent"];

function writeOwnTargetCase(root: string, body: string, dependsOn: string[] = ["my-own"]): void {
  rmSync(join(root, "overlays"), { recursive: true, force: true });
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    `---\nname: alpha\ndescription: Alpha\n---\n${body}\n`,
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      ...(dependsOn.length ? [`    depends_on: [${dependsOn.join(", ")}]`] : []),
    ].join("\n")}\n`,
  );
}

test("a clean build has no errors beyond the fixture's own model-edge debt", () => {
  const root = makeRepo();
  buildAll(root);
  const errors = validateRepo(root).filter(
    (f) => f.level === "error" && !FIXTURE_DEBT.some((known) => f.message.includes(known)),
  );
  assert.deepEqual(errors, []);
});

test("unknown manifest source is an error", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, "curation", "deniz-broken.yaml"),
    "plugin:\n  name: deniz-broken\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/nope\n",
  );
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === "error" && f.message.includes("sp/skills/nope")));
});

test("leftover upstream reference is a warning", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: d\n---\n\nStill uses superpowers:some-skill here.\n",
  );
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === "warn" && f.message.includes("superpowers:some-skill")));
  assert.ok(!findings.some((f) => f.message.includes("unknown reference namespace superpowers")));
});

test("unknown reference namespace warns once", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: d\n---\n\nUse unknown-market:tool here.\n",
  );

  const hits = validateRepo(root).filter(
    (f) => f.level === "warn" && f.message.includes("unknown reference namespace unknown-market"),
  );
  assert.equal(hits.length, 1);
  assert.match(hits[0]?.message ?? "", /1 occurrence/);
});

test("unknown namespace warning aggregates occurrences and caps sorted example paths", () => {
  const root = makeRepo();
  buildAll(root);
  const paths = [
    join(root, "plugins", "deniz-process", "skills", "my-own", "SKILL.md"),
    join(root, "plugins", "deniz-process", "skills", "gamma", "SKILL.md"),
    join(root, "plugins", "deniz-process", "skills", "delta", "SKILL.md"),
    join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"),
  ];
  for (const path of paths) {
    writeFileSync(path, `${readFileSync(path, "utf8")}\nUse unknown-market:tool here.\n`);
  }

  const hits = validateRepo(root).filter(
    (f) => f.level === "warn" && f.message.includes("unknown reference namespace unknown-market"),
  );
  assert.equal(hits.length, 1);
  const message = hits[0]?.message ?? "";
  assert.match(message, /4 occurrences/);
  const alpha = "plugins/deniz-process/skills/alpha/SKILL.md";
  const delta = "plugins/deniz-process/skills/delta/SKILL.md";
  const gamma = "plugins/deniz-process/skills/gamma/SKILL.md";
  assert.ok(message.indexOf(alpha) < message.indexOf(delta));
  assert.ok(message.indexOf(delta) < message.indexOf(gamma));
  assert.ok(!message.includes("plugins/deniz-process/skills/my-own/SKILL.md"));
});

test("known prose address does not produce an unknown namespace warning", () => {
  const root = makeRepo();
  buildAll(root);
  const proseAddresses = [
    "active:properties",
    "app:main",
    "azurite:latest",
    "display:flex",
    "fqdn:fqdn",
    "fqdn:properties",
    "id:id",
    "key:value",
    "location:location",
    "mailcatcher:latest",
    "main:app",
    "my-app:latest",
    "name:name",
    "resource:api",
    "severity:debug",
    "severity:error",
    "start:dev",
    "state:properties",
    "state:provisioning",
    "state:state",
    "status:error",
    "system:redis",
    "timestamp:properties",
    "type:type",
  ];
  writeFileSync(
    join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"),
    `---\nname: alpha\ndescription: d\n---\n\nKnown prose: ${proseAddresses.join(" ")}\n`,
  );

  const hits = validateRepo(root).filter(
    (f) => f.level === "warn" && f.message.includes("unknown reference namespace"),
  );
  assert.deepEqual(hits, []);
});

test("known prose address allowlist remains exact", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: d\n---\n\nUnrecognized qualifier: severity:info\n",
  );

  const hits = validateRepo(root).filter(
    (f) => f.level === "warn" && f.message.includes("unknown reference namespace severity"),
  );
  assert.equal(hits.length, 1);
});

// Two plugins can each curate the same upstream skill without either manifest looking wrong, but
// Claude Code addresses a skill by name alone, so only one of the two would ever be reachable.
test("duplicate output name across plugins is rejected by build preflight", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-other.yaml"),
    "plugin:\n  name: deniz-other\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n",
  );
  assert.throws(
    () => buildAll(root),
    /duplicate output name alpha from deniz-other \(sp\/skills\/alpha\) and deniz-process \(sp\/skills\/alpha\)/,
  );
});

test("duplicate output identity within one plugin is an error", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/beta",
      "    name: shared",
      "  - source: sp/skills/delta",
      "    name: shared",
    ].join("\n")}\n`,
  );

  const findings = validateRepo(root);
  assert.ok(
    findings.some(
      (f) =>
        f.level === "error" &&
        f.message.includes("duplicate output identity skill:shared") &&
        f.message.includes("sp/skills/beta") &&
        f.message.includes("sp/skills/delta"),
    ),
    `expected a same-plugin identity error, got ${JSON.stringify(findings, null, 2)}`,
  );
});

test("duplicate plugin.name values are an error that lists both manifest paths", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "other.yaml"),
    "plugin:\n  name: deniz-process\n  description: Other\n  version: 0.1.0\nitems: []\n",
  );

  const findings = validateRepo(root);
  assert.ok(
    findings.some(
      (f) =>
        f.level === "error" &&
        f.message.includes("duplicate plugin.name deniz-process") &&
        f.message.includes("curation/deniz-process.yaml") &&
        f.message.includes("curation/other.yaml"),
    ),
    `expected a duplicate-plugin error, got ${JSON.stringify(findings, null, 2)}`,
  );
});

test("the same output name in different artifact kinds is invalid after Codex flattening", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/delta",
      "    name: shared",
      "  - source: sp/skills/beta",
      "    name: shared",
      "    as: command",
    ].join("\n")}\n`,
  );
  assert.throws(() => buildAll(root), /flattened Codex skill collision/);
});

// marketplace.json is what a harness reads to find the plugins; a build that half-failed, or a
// hand-deleted plugin dir, leaves it advertising directories that are not there.
test("marketplace.json listing a plugin that is not built is an error", () => {
  const root = makeRepo();
  buildAll(root);
  rmSync(join(root, "plugins", "deniz-process"), { recursive: true });
  const findings = validateRepo(root);
  assert.ok(
    findings.some(
      (f) => f.level === "error" && f.message.includes("marketplace lists deniz-process but plugins/deniz-process"),
    ),
    `expected a marketplace mismatch error, got ${JSON.stringify(findings, null, 2)}`,
  );
});

test("Codex validation reports missing and extra plugin roots", () => {
  const root = makeRepo();
  buildAll(root);
  rmSync(codexPluginPath(root, "deniz-process"), { recursive: true });
  mkdirSync(codexPluginPath(root, "unexpected"), { recursive: true });

  const findings = validateRepo(root);
  assert.ok(findings.some((finding) => finding.message.includes("missing Codex Plugin root codex/deniz-process")));
  assert.ok(findings.some((finding) => finding.message.includes("unexpected directory under codex/: unexpected")));
});

test("Codex validation rejects an escaping marketplace source path", () => {
  const root = makeRepo();
  buildAll(root);
  const path = join(root, ".agents", "plugins", "marketplace.json");
  const marketplace = JSON.parse(readFileSync(path, "utf8"));
  marketplace.plugins[0].source.path = "./../escape";
  writeFileSync(path, `${JSON.stringify(marketplace, null, 2)}\n`);

  const findings = validateRepo(root);
  assert.ok(findings.some((finding) => finding.message.includes("escapes the repository root")));
});

test("Codex validation rejects manifest drift and unsupported skill frontmatter", () => {
  const root = makeRepo();
  buildAll(root);
  const pluginManifest = codexPluginPath(root, "deniz-process", ".codex-plugin", "plugin.json");
  const plugin = JSON.parse(readFileSync(pluginManifest, "utf8"));
  plugin.version = "9.9.9";
  writeFileSync(pluginManifest, `${JSON.stringify(plugin, null, 2)}\n`);
  const skill = codexPluginPath(root, "deniz-process", "skills", "alpha", "SKILL.md");
  writeFileSync(skill, readFileSync(skill, "utf8").replace("description:", "model: opus\ndescription:"));

  const findings = validateRepo(root);
  assert.ok(findings.some((finding) => finding.message.includes("plugin.json does not match curation")));
  assert.ok(findings.some((finding) => finding.message.includes("unsupported frontmatter: model")));
});

test("Codex validation rejects a skill description beyond the native limit", () => {
  const root = makeRepo();
  buildAll(root);
  const skill = codexPluginPath(root, "deniz-process", "skills", "alpha", "SKILL.md");
  writeFileSync(
    skill,
    readFileSync(skill, "utf8").replace("description: Alpha curated", `description: ${"x".repeat(1025)}`),
  );

  const findings = validateRepo(root);
  assert.ok(findings.some((finding) => finding.message.includes("description exceeds 1024 characters")));
});

test("Codex validation enforces the invocation policy matrix", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    [
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
      "",
    ].join("\n"),
  );
  buildAll(root);
  const manualPolicy = readFileSync(
    codexPluginPath(root, "deniz-process", "skills", "beta", "agents", "openai.yaml"),
    "utf8",
  );
  rmSync(codexPluginPath(root, "deniz-process", "skills", "beta", "agents", "openai.yaml"));
  for (const name of ["alpha", "delta", "gamma"]) {
    const agents = codexPluginPath(root, "deniz-process", "skills", name, "agents");
    mkdirSync(agents, { recursive: true });
    writeFileSync(join(agents, "openai.yaml"), manualPolicy);
  }

  const messages = validateRepo(root).map((finding) => finding.message);
  assert.ok(messages.some((message) => message.includes("beta/agents/openai.yaml is required for manual")));
  for (const name of ["alpha", "delta", "gamma"]) {
    assert.ok(messages.some((message) => message.includes(`${name}/agents/openai.yaml must be absent`)));
  }
});

test("Codex validation rejects dangling and unrendered namespaced references", () => {
  const root = makeRepo();
  buildAll(root);
  const skill = codexPluginPath(root, "deniz-process", "skills", "alpha", "SKILL.md");
  writeFileSync(
    skill,
    `${readFileSync(skill, "utf8")}\nUse $deniz-process:missing, /$deniz-process:beta-agent, and deniz-process:beta-agent. Price: $25.\n`,
  );

  const messages = validateRepo(root).map((finding) => finding.message);
  assert.ok(messages.some((message) => message.includes("dangling Codex reference $deniz-process:missing")));
  assert.ok(messages.some((message) => message.includes("unrendered Codex reference deniz-process:beta-agent")));
  assert.ok(messages.some((message) => message.includes("invalid Codex pointer spelling /$")));
  assert.equal(
    messages.some((message) => message.includes("$25")),
    false,
  );
});

test("Codex validation detects a same-plugin cross-kind flattening collision", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    [
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    name: shared",
      "  - source: sp/skills/beta",
      "    as: command",
      "    name: Shared",
      "",
    ].join("\n"),
  );

  assert.ok(validateRepo(root).some((finding) => finding.message.includes("case-colliding flattened Codex skill")));
});

// A symlink can only reach plugins/ by hand or from a future build regression; either way the
// committed output stops being portable, so validate must fail rather than warn.
test("a symlink in built output is an error", (t) => {
  const root = makeRepo();
  buildAll(root);
  const link = join(root, "plugins", "deniz-process", "skills", "alpha", "fixtures");
  try {
    symlinkSync(join(root, "external", "sp", "skills", "beta"), link, "dir");
  } catch {
    t.skip("creating symlinks requires elevated privileges on this platform");
    return;
  }
  const findings = validateRepo(root);
  assert.ok(
    findings.some(
      (f) =>
        f.level === "error" &&
        f.message.includes("must not contain symlinks") &&
        f.message.includes("plugins/deniz-process/skills/alpha/fixtures"),
    ),
    `expected a symlink error, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// The failure ADR-0001's guardrails exist to prevent, arriving through the one door they do not
// watch: the build only consults an overlay when the item says `body:`, so dropping that line
// ships pristine upstream with every hash check and patch check simply never running.
test("an overlay directory with no body: on its item is an error", () => {
  const root = makeRepo();
  buildAll(root);
  // delta is curated as a plain passthrough — no body: line anywhere in the manifest
  mkdirSync(join(root, "overlays", "deniz-process", "delta"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "delta", "SKILL.md"),
    "---\nname: delta\ndescription: Delta edited\n---\n\nEdited body.\n",
  );
  const findings = validateRepo(root);
  assert.ok(
    findings.some(
      (f) => f.level === "error" && f.message.includes("deniz-process/delta") && f.message.includes("body:"),
    ),
    `expected an ignored-overlay error, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// A renamed item, or a hand-made directory, leaves an overlay no item can ever reach.
test("an overlay directory matching no item is an error", () => {
  const root = makeRepo();
  buildAll(root);
  mkdirSync(join(root, "overlays", "deniz-process", "ghost"), { recursive: true });
  writeFileSync(join(root, "overlays", "deniz-process", "ghost", "SKILL.md"), "---\nname: ghost\n---\n\nBody.\n");
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("deniz-process/ghost")),
    `expected an unmatched-overlay error, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// Bookkeeping rot rather than a bypass: nothing reads the entry, but it claims a guard exists.
test("a lock entry with no overlay directory is a warning", () => {
  const root = makeRepo();
  buildAll(root);
  const lock = loadLock(root);
  lock[lockKey("deniz-process", "vanished")] = { source: "sp/skills/delta", files: { "SKILL.md": "deadbeef" } };
  saveLock(root, lock);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "warn" && f.message.includes("deniz-process/vanished")),
    `expected a stale-lock warning, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// `eject --patch` cuts the patch and then deletes the working copy it was cut from. Anything left
// beside overlay.patch is dead weight the build never reads — and the next person to edit it will
// not be told their edit does nothing.
test("a patch overlay holding a stranded working copy is a warning", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, "overlays", "deniz-process", "gamma", "SKILL.md"),
    "---\nname: gamma\ndescription: stranded\n---\n\nNever read.\n",
  );
  const findings = validateRepo(root);
  assert.ok(
    findings.some(
      (f) => f.level === "warn" && f.message.includes("deniz-process/gamma") && f.message.includes("SKILL.md"),
    ),
    `expected a stranded-working-copy warning, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// Codex flattens every resolved kind to a skill, so invocation remains meaningful even when the
// Claude/OpenCode shape is a command or agent.
test("invocation on a converted item controls the Codex skill without a dead-field warning", () => {
  const root = makeRepo();
  const manifest = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    manifest,
    readFileSync(manifest, "utf8").replace("    as: agent", "    as: agent\n    invocation: manual"),
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.equal(
    findings.some((f) => f.level === "warn" && f.message.includes("beta-agent") && f.message.includes("invocation")),
    false,
  );
  assert.ok(
    readFileSync(
      join(root, "codex", "deniz-process", "skills", "beta-agent", "agents", "openai.yaml"),
      "utf8",
    ).includes("allow_implicit_invocation: false"),
  );
});

// A conversion reads exactly one file out of the source and drops the rest anyway, so an omit
// list there is describing work the conversion already did.
test("omit on a converted item is a warning", () => {
  const root = makeRepo();
  const manifest = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    manifest,
    readFileSync(manifest, "utf8").replace("    as: agent", '    as: agent\n    omit:\n      - "references/**"'),
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "warn" && f.message.includes("beta-agent") && f.message.includes("omit")),
    `expected an omit-on-conversion warning, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// A typo'd pattern removes nothing and says nothing — the file you meant to drop ships.
test("an omit pattern that matches nothing is a warning", () => {
  const root = makeRepo();
  const manifest = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    manifest,
    readFileSync(manifest, "utf8").replace(
      "  - source: sp/skills/delta",
      '  - source: sp/skills/delta\n    omit:\n      - "refrences/**"',
    ),
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "warn" && f.message.includes("refrences/**")),
    `expected a dead-omit-pattern warning, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// The exec bit cannot survive a Windows checkout, so a script curated there is committed 100644
// while a Linux rebuild produces 0755 — CI's freshness gate fails on the mode diff, and worse, the
// shipped script is not executable for anyone who installs the plugin. git's index is the only
// place the bit still exists on such a checkout, so both trees have to be real repositories here.
test("a built file whose upstream is executable must be recorded executable", () => {
  const root = makeRepo();
  const sp = join(root, "external", "sp");
  writeFileSync(join(sp, "skills", "delta", "run.sh"), "#!/bin/sh\necho hi\n");
  execFileSync("git", ["init", "-q", "."], { cwd: sp });
  execFileSync("git", ["add", "-A"], { cwd: sp });
  // marks the bit in the index only, so the fixture behaves identically on Windows and Linux
  execFileSync("git", ["update-index", "--chmod=+x", "skills/delta/run.sh"], { cwd: sp });
  execFileSync("git", ["init", "-q", "."], { cwd: root });
  buildAll(root);
  execFileSync("git", ["add", "-A", "--", "plugins", "opencode"], { cwd: root });
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("run.sh") && f.message.includes("executable")),
    `expected an exec-bit error, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// The rule must stay quiet when nothing upstream is executable, and when there is no index to read.
test("no exec-bit finding on a clean fixture", () => {
  const root = makeRepo();
  buildAll(root);
  assert.ok(!validateRepo(root).some((f) => f.message.includes("executable")));
});

// The fixture curates sp/skills/beta twice (command + agent) and gives alpha a dead
// frontmatter name, so a clean build is warn-worthy without being error-worthy.
test("curation footguns are warnings on a clean build", () => {
  const root = makeRepo();
  buildAll(root);
  const warnings = validateRepo(root)
    .filter((f) => f.level === "warn")
    .map((f) => f.message);
  assert.ok(
    warnings.some(
      (msg) =>
        msg.includes("sp/skills/beta") &&
        msg.includes("deniz-process:deniz-beta") &&
        msg.includes("deniz-process:beta-agent"),
    ),
    `expected a duplicate-source warning, got ${JSON.stringify(warnings, null, 2)}`,
  );
  assert.ok(
    warnings.some((msg) => msg.includes("deniz-process/alpha") && msg.includes("sneaky")),
    `expected a dead frontmatter.name warning, got ${JSON.stringify(warnings, null, 2)}`,
  );
});

test("linker: a model-edge to a manual target is an error, named by its Claude cause first", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha", // body says: use the superpowers:beta skill (model-edge)
      "    depends_on: [beta]",
      "  - source: sp/skills/beta",
      "    invocation: manual",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("model-edge to a target the model cannot reach")),
    JSON.stringify(findings, null, 2),
  );
});

test("linker: a pointer to a model-only target is an error", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\nSuggest /superpowers:beta to the user.\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "  - source: sp/skills/beta",
      "    invocation: auto", // user-invocable: false in Claude; the error comes from Claude alone
    ].join("\n")}\n`,
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("pointer to a target the user cannot reach")),
    JSON.stringify(findings, null, 2),
  );
});

// A manual target is already unreachable through Claude, whose cause the linker names first; only an
// absent item whose upstream carries the hide key is Claude-reachable but OpenCode-hidden.
test("linker: a model-edge to an OpenCode-hidden skill names the hide key", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\n\nUse superpowers:delta next.\n",
  );
  writeFileSync(
    join(root, "external", "sp", "skills", "delta", "SKILL.md"),
    "---\nname: delta\ndescription: Delta upstream\nmetadata:\n  opencode/autoinvoke: false\n---\n\nDelta body.\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n    depends_on: [delta]\n  - source: sp/skills/delta\n",
  );
  buildAll(root);
  const errors = validateRepo(root)
    .filter((f) => f.level === "error")
    .map((f) => f.message);
  assert.ok(
    errors.some((m) =>
      m.includes(
        "model-edge to a target the model cannot reach: delta (hidden from the OpenCode model (opencode/autoinvoke))",
      ),
    ),
    errors.join("\n"),
  );
});

test("linker: a pointer to an auto skill is an error only through Claude", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\nSuggest /superpowers:beta to the user.\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n  - source: sp/skills/beta\n    invocation: auto\n",
  );
  buildAll(root);
  const errors = validateRepo(root)
    .filter((f) => f.level === "error")
    .map((f) => f.message);
  assert.ok(
    errors.some((m) => m.includes("user-invocable: false in the Claude tree")),
    errors.join("\n"),
  );
  assert.ok(!errors.some((m) => m.includes("no OpenCode artifact")), "OpenCode users can attach any skill");
});

test("path rules: an OpenCode climb into another Module resolves through the installed layout", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\nSee [delta](../delta/SKILL.md).\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-other.yaml"),
    "plugin:\n  name: deniz-other\n  description: O\n  version: 0.1.0\nitems:\n  - source: sp/skills/delta\n",
  );
  buildAll(root);
  assert.match(
    readFileSync(opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md"), "utf8"),
    /\(\.\.\/deniz-other\.delta\/SKILL\.md\)/,
  );
  const r1 = validateRepo(root).filter((f) => f.message.includes("does not resolve in opencode/"));
  assert.deepEqual(r1, [], "the installed layout shares one skills/ directory across Modules");
});

test("linker: depends_on is enforced in both directions", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha", // body has a model-edge to beta, but declares gamma instead
      "    depends_on: [gamma]",
      "  - source: sp/skills/beta",
      "  - source: sp/skills/gamma",
    ].join("\n")}\n`,
  );
  mkdirSync(join(root, "external", "sp", "skills", "gamma"), { recursive: true });
  writeFileSync(
    join(root, "external", "sp", "skills", "gamma", "SKILL.md"),
    "---\nname: gamma\ndescription: Gamma upstream\n---\nBody.\n",
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("undeclared dependency: beta")),
    JSON.stringify(findings, null, 2),
  );
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("stale depends_on: gamma")),
    JSON.stringify(findings, null, 2),
  );
});

test("linker: a declared original-skill target passes", () => {
  const root = makeRepo();
  writeOwnTargetCase(root, "Load deniz-process:my-own.");
  buildAll(root);
  assert.ok(!validateRepo(root).some((f) => f.level === "error"));
});

test("linker: an original-skill fact without depends_on is undeclared", () => {
  const root = makeRepo();
  writeOwnTargetCase(root, "Load deniz-process:my-own.", []);
  buildAll(root);
  assert.ok(validateRepo(root).some((f) => f.message.includes("undeclared dependency: my-own")));
});

test("linker: original-skill depends_on without a fact is stale", () => {
  const root = makeRepo();
  writeOwnTargetCase(root, "No handoff.");
  buildAll(root);
  assert.ok(validateRepo(root).some((f) => f.message.includes("stale depends_on: my-own")));
});

test("linker: removing an original-skill target leaves a dangling fact", () => {
  const root = makeRepo();
  writeOwnTargetCase(root, "Load deniz-process:my-own.");
  buildAll(root);
  rmSync(join(root, "plugins", "deniz-process", "skills", "my-own"), { recursive: true, force: true });
  rmSync(opencodeIdPath(root, "deniz-process", "skill", "my-own"), { recursive: true, force: true });
  assert.ok(validateRepo(root).some((f) => f.message.includes("dangling reference")));
});

test("linker: an output namespace leaking into opencode/ is an error", () => {
  const root = makeRepo();
  buildAll(root);
  // simulate the historical bug: a hand-authored output-space reference surviving into opencode
  writeFileSync(
    opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md"),
    "---\nname: deniz-process.alpha\ndescription: x\n---\nUse the deniz-process:beta skill.\n",
  );
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("output namespace leaked into opencode/")),
    JSON.stringify(findings, null, 2),
  );
});

test("linker: a dangling namespaced reference is an error", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: x\n---\nUse the deniz-process:nonexistent skill.\n",
  );
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("dangling reference")),
    JSON.stringify(findings, null, 2),
  );
});

test("an own skill colliding with a curated item is rejected by build preflight", () => {
  const root = makeRepo();
  mkdirSync(join(root, "skills", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "skills", "deniz-process", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: own\n---\nOwn.\n",
  );
  assert.throws(
    () => buildAll(root),
    /duplicate output name alpha from deniz-process \(sp\/skills\/alpha\) and deniz-process \(skills\/deniz-process\/alpha\)/,
  );
});

test("provenance: a name or a date in a manifest comment is an error; a description keeps its branding", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "# reviewed by Deniz on 2026-07-31",
      "plugin:",
      "  name: deniz-process",
      '  description: "Deniz curated set"', // a value, not a comment — exempt
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "  - source: sp/skills/beta",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const hits = validateRepo(root).filter((f) => f.level === "error" && f.message.includes("stamps no names"));
  assert.equal(hits.length, 2, JSON.stringify(hits, null, 2)); // the name and the date, nothing for the value
});

test("provenance: overlay bodies are scanned; the lock is exempt", () => {
  const root = makeRepo();
  mkdirSync(join(root, "overlays", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: x\n---\nRewritten by Deniz on 2026-07-31.\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    body: overlay",
      "  - source: sp/skills/beta",
    ].join("\n")}\n`,
  );
  const lock = {
    "deniz-process/alpha": {
      source: "sp/skills/alpha",
      files: stampFiles(join(root, "external", "sp", "skills", "alpha"), ["SKILL.md"]),
    },
  };
  writeFileSync(join(root, "overlays", "overlays.lock.json"), `${JSON.stringify(lock, null, 2)}\n`);
  const hits = validateRepo(root).filter((f) => f.level === "error" && f.message.includes("stamps no names"));
  assert.equal(hits.length, 2); // name + date in the overlay body; the lock's own content never scanned
});

test("provenance: a patch's context lines are upstream's — only added lines are ours", () => {
  const root = makeRepo();
  mkdirSync(join(root, "overlays", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "alpha", "overlay.patch"),
    [
      "diff --git a/SKILL.md b/SKILL.md",
      "--- a/SKILL.md",
      "+++ b/SKILL.md",
      "@@ -1,3 +1,3 @@",
      " Deniz appears upstream here",
      "-old line",
      "+new line, no stamps",
    ].join("\n"),
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    body: patch",
      "  - source: sp/skills/beta",
    ].join("\n")}\n`,
  );
  const hits = validateRepo(root).filter((f) => f.level === "error" && f.message.includes("stamps no names"));
  assert.equal(hits.length, 0, JSON.stringify(hits, null, 2));
});

// The build reads merge stamps only for an item that says `body:`, so a declaration without one is
// a guard nothing consults — the same silent bypass an unclaimed overlay is, spelled differently.
test("merged_from on an item with no body: is an error", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    merged_from: [sp/skills/beta]",
      "  - source: sp/skills/beta",
      "    exclude: true",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === "error" && f.message.includes("merged_from without body:")));
});

test("a merged_from source that is not in external/ is an error", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    body: overlay",
      "    merged_from: [sp/skills/nowhere]",
    ].join("\n")}\n`,
  );
  const hits = validateRepo(root).filter((f) =>
    f.message.includes("merged_from source not found in external/: sp/skills/nowhere"),
  );
  assert.equal(hits.length, 1, JSON.stringify(hits, null, 2));
});

// R1 (L8): a path into a sibling item is an edge spelled as a path. Renaming, excluding or
// omitting the target breaks it in silence — and a merge does all three.
test("a relative path into a sibling item that no longer has the file is an error", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\n\nSee [notes](../beta/references/notes.md).\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "  - source: sp/skills/beta",
      "    omit:", // the very file alpha points at
      '      - "references/**"',
    ].join("\n")}\n`,
  );
  buildAll(root);
  // OpenCode respells the climb onto the ID folder (../deniz-process.beta/...), so match the tail.
  const hits = validateRepo(root).filter((f) => f.level === "error" && f.message.includes("beta/references/notes.md"));
  assert.equal(hits.length, 3, `all three trees, once each — ${JSON.stringify(hits, null, 2)}`);
});

// R2 (L8): the noise this rule exists to not make. Upstream bodies are full of illustrative paths
// that never resolved anywhere; only a file OUR build dropped is our problem.
test("a link to a file the build dropped is an error; a path upstream never had is silent", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "delta", "SKILL.md"),
    `---\nname: delta\ndescription: Delta upstream\n---\n\nReal: [notes](references/notes.md).\nIllustrative: [forms](FORMS.md) and [ctx](./src/ordering/CONTEXT.md).\n`,
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/delta",
      "    omit:",
      '      - "references/**"',
    ].join("\n")}\n`,
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.equal(
    findings.filter((f) => f.level === "error" && f.message.includes("references/notes.md")).length,
    3,
    `dropped by omit, in all three trees — ${JSON.stringify(findings, null, 2)}`,
  );
  for (const quiet of ["FORMS.md", "CONTEXT.md"]) {
    assert.equal(
      findings.filter((f) => f.message.includes(quiet)).length,
      0,
      `${quiet} never existed upstream — reporting it is the warning nobody reads`,
    );
  }
});

// Under the filename rule an absent file is deliberate — the list comes from the overlay, and a
// later appearance is drift. A file a human NAMED is a claim, and a misspelled claim stamps null:
// a guard over nothing, with the all-null check silent because the other names stamped fine.
test("a merged_from files entry naming a file the source lacks is a warning", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    body: overlay",
      "    merged_from:",
      "      - source: sp/skills/beta",
      "        files: [SKILL.md, references/note.md]", // the file is notes.md
    ].join("\n")}\n`,
  );
  const findings = validateRepo(root);
  const hits = findings.filter((f) => f.level === "warn" && f.message.includes("references/note.md"));
  assert.equal(hits.length, 1, JSON.stringify(findings, null, 2));
  assert.equal(
    findings.filter((f) => f.message.includes("SKILL.md, which is not there")).length,
    0,
    "a name that resolves must stay silent",
  );
});

test("module manifest: a tampered file is a hash mismatch", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md"), "tampered\n");
  assert.ok(validateRepo(root).some((f) => f.level === "error" && f.message.includes("hash mismatch")));
});

test("module manifest: a missing listed file is an error", () => {
  const root = makeRepo();
  buildAll(root);
  rmSync(opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md"));
  assert.ok(validateRepo(root).some((f) => f.level === "error" && f.message.includes("missing")));
});

test("module manifest: an extra unlisted file is an error", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(join(opencodeModulePath(root, "deniz-process"), "extra.txt"), "extra\n");
  assert.ok(validateRepo(root).some((f) => f.level === "error" && f.message.includes("not listed")));
});

test("module manifest: malformed manifest.json is an error", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(join(opencodeModulePath(root, "deniz-process"), "manifest.json"), "{not json");
  assert.ok(validateRepo(root).some((f) => f.level === "error" && f.message.includes("invalid Module manifest")));
});

test("module manifest: a second Module whose destination path differs only by case is an error", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, "curation", "other.yaml"),
    "plugin:\n  name: Deniz-Process\n  description: d\n  version: 0.1.0\nitems: []\n",
  );
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && /alias/i.test(f.message)),
    JSON.stringify(findings, null, 2),
  );
});

test("module manifest: an unexpected Module directory is an error", () => {
  const root = makeRepo();
  buildAll(root);
  mkdirSync(join(root, "opencode", "stray"));
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("unexpected directory under opencode/: stray")),
    JSON.stringify(findings, null, 2),
  );
});

test("module manifest: a missing expected Module root is an error", () => {
  const root = makeRepo();
  buildAll(root);
  rmSync(opencodeModulePath(root, "deniz-process"), { recursive: true });
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("missing Module root opencode/deniz-process")),
    JSON.stringify(findings, null, 2),
  );
});

test("module manifest: a missing manifest in an existing root is an error", () => {
  const root = makeRepo();
  buildAll(root);
  rmSync(join(opencodeModulePath(root, "deniz-process"), "manifest.json"));
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("opencode/deniz-process/manifest.json is missing")),
    JSON.stringify(findings, null, 2),
  );
  assert.equal(
    findings.filter((f) => f.message.includes("missing Module root opencode/deniz-process")).length,
    0,
    "an existing root with no manifest is not a missing Module",
  );
});

test("module manifest: a Module name mismatch is an error", () => {
  const root = makeRepo();
  buildAll(root);
  const path = join(opencodeModulePath(root, "deniz-process"), "manifest.json");
  const manifest = JSON.parse(readFileSync(path, "utf8")) as { module: string };
  manifest.module = "deniz-other";
  writeFileSync(path, `${JSON.stringify(manifest, null, 2)}\n`);
  const findings = validateRepo(root);
  assert.ok(
    findings.some(
      (f) =>
        f.level === "error" &&
        f.message.includes("Module name deniz-other") &&
        f.message.includes("does not match deniz-process"),
    ),
    JSON.stringify(findings, null, 2),
  );
});

test("module manifest: a Module version mismatch is an error", () => {
  const root = makeRepo();
  buildAll(root);
  const path = join(opencodeModulePath(root, "deniz-process"), "manifest.json");
  const manifest = JSON.parse(readFileSync(path, "utf8")) as { version: string };
  manifest.version = "9.9.9";
  writeFileSync(path, `${JSON.stringify(manifest, null, 2)}\n`);
  const findings = validateRepo(root);
  assert.ok(
    findings.some(
      (f) =>
        f.level === "error" && f.message.includes("Module version 9.9.9") && f.message.includes("does not match 0.1.0"),
    ),
    JSON.stringify(findings, null, 2),
  );
});

test("module manifest: a dangling top-level symlink under opencode/ returns findings instead of throwing", (t) => {
  const root = makeRepo();
  buildAll(root);
  const link = join(root, "opencode", "ghost");
  try {
    symlinkSync(join(root, "opencode", "does-not-exist"), link);
  } catch {
    t.skip("creating symlinks requires elevated privileges on this platform");
    return;
  }
  let findings: ReturnType<typeof validateRepo> | undefined;
  assert.doesNotThrow(() => {
    findings = validateRepo(root);
  });
  assert.ok(findings, "validateRepo must return findings");
  assert.ok(
    findings.some(
      (f) =>
        f.level === "error" && f.message.includes("must not contain symlinks") && f.message.includes("opencode/ghost"),
    ),
    JSON.stringify(findings, null, 2),
  );
});

test("module manifest: re-signed requiredModules still must match curation", () => {
  const root = makeRepo();
  const path = join(root, "curation", "deniz-process.yaml");
  const consumer = loadManifest(path);
  const alpha = consumer.items.find((item) => item.source === "sp/skills/alpha");
  const delta = consumer.items.find((item) => item.source === "sp/skills/delta");
  assert.ok(alpha && delta);
  alpha.depends_on = ["provider"];
  delta.exclude = true;
  writeFileSync(path, stringify(consumer));
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha\n---\nUse superpowers:delta.\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-provider.yaml"),
    stringify({
      plugin: { name: "deniz-provider", description: "Provider", version: "1.0.0" },
      items: [{ source: "sp/skills/delta", name: "provider", invocation: "auto" }],
    }),
  );
  buildAll(root);
  const manifestPath = join(opencodeModulePath(root, "deniz-process"), "manifest.json");
  const manifest = loadModuleManifest(manifestPath);
  manifest.requiredModules = [];
  manifest.digest = digestModulePayload(manifest.files, manifest.requiredModules);
  writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("required Modules do not match curation")),
    JSON.stringify(findings, null, 2),
  );
});

test("rendered OpenCode IDs are tokens, not path segments", () => {
  const plugins = ["deniz-process"];
  const ids = (t: string) => scanOpenCodeIds(t, plugins).map((x) => `${x.prefix}${x.id}`);
  assert.deepEqual(ids("Use deniz-process.grilling, @deniz-process.handoff or /deniz-process.wizard."), [
    "deniz-process.grilling",
    "@deniz-process.handoff",
    "/deniz-process.wizard",
  ]);
  assert.deepEqual(ids("See ../deniz-process.prototype/SKILL.md and x/deniz-process.y and deniz-process.yaml/"), []);
  assert.deepEqual(ids("deniz-process.a_b"), []);
});

test("skill-tool handles cover the three measured forms", () => {
  assert.deepEqual(skillToolHandles('Call the Skill tool with "deniz-process.grilling".'), ["deniz-process.grilling"]);
  assert.deepEqual(skillToolHandles('Call the Skill tool twice, for "grilling" and "domain-modeling".'), [
    "grilling",
    "domain-modeling",
  ]);
  assert.deepEqual(skillToolHandles('a subagent that calls the Skill tool with "research". Use when'), ["research"]);
  assert.deepEqual(skillToolHandles("call the Skill tool for whichever skills the block names"), []);
  assert.deepEqual(
    skillToolHandles(
      'Call the Skill tool with "codebase-design" for the vocabulary ("the interface is the test surface")',
    ),
    ["codebase-design"],
  );
});

/** alpha carries `body`; extra manifest lines follow the alpha item. Returns error messages. */
function ocErrors(body: string, items: string[], mutate?: (root: string) => void): string[] {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    `---\nname: alpha\ndescription: A\n---\n${body}\n`,
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${["plugin:", "  name: deniz-process", "  description: P", "  version: 0.1.0", "items:", ...items].join("\n")}\n`,
  );
  buildAll(root);
  mutate?.(root);
  return validateRepo(root)
    .filter((f) => f.level === "error")
    .map((f) => f.message);
}

test("O1: rendered IDs must resolve with a prefix that matches the target kind", () => {
  // beta as the makeRepo overlay command deniz-beta: a conversion needs a full-file overlay
  const errors = ocErrors(
    "Open /superpowers:beta.",
    [
      "  - source: sp/skills/alpha",
      "  - source: sp/skills/beta",
      "    as: command",
      "    name: deniz-beta",
      "    body: overlay",
    ],
    (root) => {
      const file = opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md");
      assert.match(readFileSync(file, "utf8"), /Open \/deniz-process\.deniz-beta\./);
      writeFileSync(
        file,
        `${readFileSync(file, "utf8")}\nSee @deniz-process.nope and @deniz-process.deniz-beta and /deniz-process.alpha.\n`,
      );
    },
  );
  assert.ok(
    errors.some((m) => m.includes("rendered OpenCode ID @deniz-process.nope does not name an emitted")),
    errors.join("\n"),
  );
  assert.ok(
    errors.some((m) => m.includes("rendered OpenCode ID @deniz-process.deniz-beta")),
    "a command needs /",
  );
  assert.ok(
    errors.some((m) => m.includes("rendered OpenCode ID /deniz-process.alpha")),
    "a skill needs @",
  );
  assert.ok(
    !errors.some((m) => m.includes("rendered OpenCode ID /deniz-process.deniz-beta")),
    "the rendered command pointer resolves",
  );
});

test("O2: a bare skill-tool handle fails; a promoted one passes", () => {
  const bare = ocErrors('Call the Skill tool with "beta".', [
    "  - source: sp/skills/alpha",
    "    invocation: manual",
    "  - source: sp/skills/beta",
    "    invocation: auto",
  ]);
  assert.ok(
    bare.some((m) => m.includes('skill-tool handle "beta" is not an emitted OpenCode skill ID')),
    bare.join("\n"),
  );
  const promoted = ocErrors('Call the Skill tool with "superpowers:beta".', [
    "  - source: sp/skills/alpha",
    "    invocation: manual",
    "    depends_on: [beta]",
    "  - source: sp/skills/beta",
    "    invocation: auto",
  ]);
  assert.ok(!promoted.some((m) => m.includes("skill-tool handle")), promoted.join("\n"));
});

test("O3: model-reachable text must not name a manual item", () => {
  const items = (alphaPosture: string[]) => [
    "  - source: sp/skills/alpha",
    ...alphaPosture,
    "  - source: sp/skills/beta",
    "    invocation: manual",
  ];
  const leak = "model-reachable text names manual item deniz-process.beta";
  assert.ok(
    ocErrors("Tell the user /superpowers:beta.", items(["    invocation: auto"])).some((m) => m.includes(leak)),
  );
  assert.ok(
    ocErrors("Tell the user /superpowers:beta.", items([])).some((m) => m.includes(leak)),
    "absent counts as reachable",
  );
  assert.ok(
    !ocErrors("Tell the user /superpowers:beta.", items(["    invocation: manual"])).some((m) => m.includes(leak)),
  );
  assert.ok(
    ocErrors("Tell the user /superpowers:beta.", items(["    as: agent"])).some((m) => m.includes(leak)),
    "agents are reachable",
  );
});

test("O4–O6: phantom skills, skill name, and agent keys", () => {
  const errors = ocErrors(
    "Body.",
    ["  - source: sp/skills/alpha", "  - source: sp/skills/beta", "    as: agent"],
    (root) => {
      const skills = opencodeModulePath(root, "deniz-process", "skills");
      writeFileSync(join(skills, "stray.md"), "---\nname: stray\ndescription: S\n---\n");
      mkdirSync(join(skills, "deniz-process.alpha", "nested"), { recursive: true });
      writeFileSync(join(skills, "deniz-process.alpha", "nested", "SKILL.md"), "---\nname: n\ndescription: N\n---\n");
      const skill = join(skills, "deniz-process.alpha", "SKILL.md");
      writeFileSync(skill, readFileSync(skill, "utf8").replace("name: deniz-process.alpha", "name: alpha"));
      const agent = opencodeIdPath(root, "deniz-process", "agent", "beta");
      writeFileSync(
        agent,
        readFileSync(agent, "utf8").replace("mode: subagent", "mode: subagent\nname: x\ncolor: info"),
      );
    },
  );
  for (const expected of [
    "skills/stray.md",
    "nested/SKILL.md",
    "name alpha does not equal",
    "agent key name",
    "color info",
  ]) {
    assert.ok(
      errors.some((m) => m.includes(expected)),
      `${expected}\n${errors.join("\n")}`,
    );
  }
});
