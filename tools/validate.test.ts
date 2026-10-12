import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, renameSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { stringify } from "yaml";
import { buildAll } from "./build.ts";
import { digestModulePayload, loadModuleManifest } from "./lib/opencode-bundle.ts";
import { loadManifest } from "./lib/manifest.ts";
import { loadLock, lockKey, saveLock, stampFiles } from "./lib/overlay.ts";
import { scanRefs } from "./lib/refs.ts";
import {
  addOriginalHarnessNotes,
  addOriginalSkill,
  codexPluginPath,
  makeHarnessFilesRepo,
  makeRepo,
  opencodeIdPath,
  opencodeModulePath,
  restampFrontmatter,
} from "./testutil.ts";
import { scanOpenCodeIds, validateRepo } from "./validate.ts";

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
  addOriginalSkill(root, "my-own");
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
  addOriginalSkill(root, "my-own");
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

// diagnosing-superpowers cites evidence as `path:line` and builds a GitHub search query with
// `repo:` and `is:` qualifiers. The scanner reads all three as addresses, so the allowlist is what
// keeps them silent.
test("diagnosing-superpowers' citation and search-qualifier lookalikes do not warn", () => {
  const root = makeRepo();
  buildAll(root);
  const prose = [
    "**Core principle:** Every finding cites `path:line`. No citation, no finding.",
    '  "https://api.github.com/search/issues?q=repo:obra/superpowers+is:issue+<url-encoded terms>&per_page=10"',
  ].join("\n");
  assert.deepEqual(
    scanRefs(prose)
      .map((ref) => ref.address)
      .sort(),
    ["is:issue", "path:line", "repo:obra"],
  );
  writeFileSync(
    join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"),
    `---\nname: alpha\ndescription: d\n---\n\n${prose}\n`,
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

test("a skill description over 1,024 characters is an error in every harness tree; 1,024 passes", () => {
  const trees = [
    [
      "plugins/deniz-process/skills/alpha/SKILL.md",
      (root: string) => join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"),
    ],
    [
      "opencode/deniz-process/skills/deniz-process.alpha/SKILL.md",
      (root: string) => opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md"),
    ],
    [
      "codex/deniz-process/skills/alpha/SKILL.md",
      (root: string) => codexPluginPath(root, "deniz-process", "skills", "alpha", "SKILL.md"),
    ],
  ] as const;
  for (const [label, path] of trees) {
    for (const length of [1024, 1025]) {
      const root = makeRepo();
      buildAll(root);
      const skill = path(root);
      writeFileSync(
        skill,
        readFileSync(skill, "utf8").replace("description: Alpha curated", `description: ${"x".repeat(length)}`),
      );

      const hits = validateRepo(root).filter((f) => f.message.includes("description is"));
      if (length === 1024) {
        assert.deepEqual(hits, [], label);
      } else {
        assert.deepEqual(
          hits.map((f) => [f.level, f.message]),
          [["error", `${label}: description is 1025 characters, over the 1024-character skill limit`]],
          label,
        );
      }
    }
  }
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

test("a frontmatter stamp is not lock rot until its item stops needing one", () => {
  const root = makeRepo();
  buildAll(root);
  const stale = (findings: ReturnType<typeof validateRepo>) =>
    findings.filter((f) => f.level === "warn" && f.message.includes("overlays/overlays.lock.json records"));
  assert.deepEqual(stale(validateRepo(root)), [], "alpha's override stands, with no overlay directory");
  const p = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    p,
    readFileSync(p, "utf8").replace("    frontmatter:\n      description: Alpha curated\n      name: sneaky\n", ""),
  );
  assert.ok(
    stale(validateRepo(root)).some((f) =>
      f.message.includes(
        "records a frontmatter stamp for deniz-process/alpha, but that item has no frontmatter override without a body: — drop the entry",
      ),
    ),
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

// An original skill has no submodule: its script's bit lives in this repository's own index.
test("a built file whose original source is executable must be recorded executable in every tree", () => {
  const root = makeRepo();
  addOriginalSkill(root, "own-run", {
    files: { "scripts/run.sh": "#!/bin/sh\necho hi\n", "scripts/notes.txt": "x\n" },
  });
  execFileSync("git", ["init", "-q", "."], { cwd: root });
  execFileSync("git", ["add", "-A", "--", "skills"], { cwd: root });
  execFileSync("git", ["update-index", "--chmod=+x", "skills/deniz-process/own-run/scripts/run.sh"], { cwd: root });
  buildAll(root);
  execFileSync("git", ["add", "-A", "--", "plugins", "opencode", "codex"], { cwd: root });
  const outputs = [
    "codex/deniz-process/skills/own-run/scripts/run.sh",
    "plugins/deniz-process/skills/own-run/scripts/run.sh",
    "opencode/deniz-process/skills/deniz-process.own-run/scripts/run.sh",
  ];
  const execErrors = () => errorsOf(root).filter((m) => m.includes("executable"));
  assert.deepEqual(
    execErrors(),
    outputs.map(
      (out) =>
        `${out}: the original skills/deniz-process/own-run/scripts/run.sh is executable but this copy is recorded 100644 — run: git update-index --chmod=+x ${out}`,
    ),
  );

  execFileSync("git", ["update-index", "--chmod=+x", ...outputs], { cwd: root });
  assert.deepEqual(execErrors(), []);
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
    `---\nname: delta\ndescription: Delta upstream\n---\n\nReal: [notes](references/notes.md).\nIllustrative: [forms](FORMS.md) and [glossary](./src/ordering/GLOSSARY.md).\n`,
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
  for (const quiet of ["FORMS.md", "GLOSSARY.md"]) {
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
  restampFrontmatter(root);
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

test("H1: a skill-tool handle must be a namespaced fact; stray skill-tool prose fails", () => {
  const bareItems = [
    "  - source: sp/skills/alpha",
    "    invocation: manual",
    "  - source: sp/skills/beta",
    "    invocation: auto",
  ];
  const bare = ocErrors('Call the Skill tool with "beta".', bareItems);
  assert.ok(
    bare.some((m) => m.includes('skill-tool handle "beta" is not a namespaced fact')),
    bare.join("\n"),
  );
  assert.ok(!bare.some((m) => m.includes("Claude-only vocabulary")), "a recognized call renders, so it never leaks");
  const promoted = ocErrors('Call the Skill tool with "superpowers:beta".', [
    "  - source: sp/skills/alpha",
    "    invocation: manual",
    "    depends_on: [beta]",
    "  - source: sp/skills/beta",
    "    invocation: auto",
  ]);
  assert.ok(
    !promoted.some((m) => m.includes("skill-tool") || m.includes("Claude-only vocabulary")),
    promoted.join("\n"),
  );
  const stray = ocErrors("Use the Skill tool to load beta.", bareItems);
  assert.ok(
    stray.some((m) => m.includes('"skill tool" outside a recognized skill-tool call')),
    stray.join("\n"),
  );
  assert.equal(
    stray.filter((m) => m.includes('Claude-only vocabulary "Skill tool"')).length,
    2,
    "opencode/ and codex/",
  );
});

test("V: Claude-only dispatch words in a generated tree are errors", () => {
  const errors = ocErrors("Body.", ["  - source: sp/skills/alpha"], (root) => {
    const file = codexPluginPath(root, "deniz-process", "skills", "alpha", "SKILL.md");
    writeFileSync(file, `${readFileSync(file, "utf8")}\nUse the Task tool with subagent_type set.\n`);
  });
  for (const word of ["Task tool", "subagent_type"]) {
    assert.ok(
      errors.some((m) => m.includes(`Claude-only vocabulary "${word}" in codex/`)),
      errors.join("\n"),
    );
  }
});

test("V: an unbackticked general-purpose subagent renders per harness and leaks into neither tree", () => {
  const errors = ocErrors("Dispatch a fresh general-purpose subagent with this prompt.", [
    "  - source: sp/skills/alpha",
  ]);
  assert.ok(!errors.some((m) => m.includes("Claude-only vocabulary")), errors.join("\n"));
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "delta", "SKILL.md"),
    "---\nname: delta\ndescription: D\n---\nDispatch a fresh general-purpose subagent with this prompt.\n",
  );
  buildAll(root);
  const line = (file: string): string | undefined =>
    readFileSync(file, "utf8")
      .split("\n")
      .find((l) => l.startsWith("Dispatch"));
  assert.equal(
    line(join(root, "plugins", "deniz-process", "skills", "delta", "SKILL.md")),
    "Dispatch a fresh general-purpose subagent with this prompt.",
  );
  assert.equal(
    line(opencodeIdPath(root, "deniz-process", "skill", "delta", "SKILL.md")),
    "Dispatch a fresh `general` subagent with this prompt.",
  );
  assert.equal(
    line(codexPluginPath(root, "deniz-process", "skills", "delta", "SKILL.md")),
    "Dispatch a fresh subagent with this prompt.",
  );
});

test("V: a general-purpose subagent inside a code span ships whole and is rejected in both trees", () => {
  const errors = ocErrors("Then `dispatch a general-purpose subagent` with the prompt.", [
    "  - source: sp/skills/alpha",
  ]);
  for (const tree of ["opencode", "codex"]) {
    assert.ok(
      errors.some((m) => m.includes(`Claude-only vocabulary "general-purpose subagent" in ${tree}/`)),
      errors.join("\n"),
    );
  }
  assert.ok(!errors.some((m) => m.includes("`general` subagent")), errors.join("\n"));
});

test("V: OpenCode renders a known todo phrase and rejects any other todo word outside code", () => {
  const errors = ocErrors(
    [
      "Create a todo per step.",
      "",
      "Then add a todo for the review. Track each task as a to-do.",
      "",
      "Create a TODO per step, but insert `// TODO: guard` where the guard belongs:",
      "",
      "```csharp",
      "// TODO: guard param",
      "```",
    ].join("\n"),
    ["  - source: sp/skills/alpha"],
  );
  const todo = errors.filter((m) => m.includes("todo word"));
  const message = (line: number, word: string): string =>
    `opencode/deniz-process/skills/deniz-process.alpha/SKILL.md:${line}: todo word "${word}" outside code in opencode/ — OpenCode 2 has no todo tool: reword it, write a code marker as code, or add a row to the OpenCode todo table (references-and-linking.md Harness phrasing)`;
  assert.deepEqual(todo, [message(8, "todo"), message(8, "to-do"), message(10, "TODO")], errors.join("\n"));
  assert.ok(!errors.some((m) => m.includes("codex/") && m.includes("todo")), "Codex keeps upstream's todo words");
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "delta", "SKILL.md"),
    "---\nname: delta\ndescription: D\n---\nCreate a todo per step.\n",
  );
  buildAll(root);
  const line = (file: string): string | undefined =>
    readFileSync(file, "utf8")
      .split("\n")
      .find((l) => l.endsWith("."));
  assert.equal(line(join(root, "plugins", "deniz-process", "skills", "delta", "SKILL.md")), "Create a todo per step.");
  assert.equal(
    line(opencodeIdPath(root, "deniz-process", "skill", "delta", "SKILL.md")),
    "Keep a visible Markdown checklist with one line per step, and tick each line as it is done.",
  );
  assert.equal(line(codexPluginPath(root, "deniz-process", "skills", "delta", "SKILL.md")), "Create a todo per step.");
});

test("V: only using-superpowers' OpenCode tool notes may name the removed todo tool", () => {
  const root = makeRepo();
  const notes = "# OpenCode notes\n\nOpenCode 2 has no todo tool. The OpenCode 1 `todowrite` tool was removed.\n";
  const skill = join(root, "external", "sp", "skills", "using-superpowers");
  mkdirSync(join(skill, "references"), { recursive: true });
  writeFileSync(join(skill, "SKILL.md"), "---\nname: using-superpowers\ndescription: U\n---\nRead the notes.\n");
  writeFileSync(join(skill, "references", "harness-tools.md"), notes);
  writeFileSync(join(root, "external", "sp", "skills", "delta", "references", "notes.md"), notes);
  const manifest = join(root, "curation", "deniz-process.yaml");
  writeFileSync(manifest, `${readFileSync(manifest, "utf8")}  - source: sp/skills/using-superpowers\n`);
  buildAll(root);
  const todo = validateRepo(root)
    .filter((f) => f.level === "error" && f.message.includes("todo word"))
    .map((f) => f.message.slice(0, f.message.indexOf(":", f.message.indexOf(".md"))));
  assert.deepEqual(todo, ["opencode/deniz-process/skills/deniz-process.delta/references/notes.md"]);
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

test("H2–H4: a bare estate name in a handoff template fails; a fact and an outside name pass", () => {
  const bare = ocErrors("Load `beta` first. Use the `docker` skill.", [
    "  - source: sp/skills/alpha",
    "  - source: sp/skills/beta",
    "    invocation: auto",
  ]);
  assert.ok(
    bare.some((m) => m.includes("load-bearing handoff names `beta` bare")),
    bare.join("\n"),
  );
  assert.ok(!bare.some((m) => m.includes("`docker`")), "outside the estate");
  const promoted = ocErrors("Load `superpowers:beta` first.", [
    "  - source: sp/skills/alpha",
    "    depends_on: [beta]",
    "  - source: sp/skills/beta",
    "    invocation: auto",
  ]);
  assert.ok(!promoted.some((m) => m.includes("load-bearing handoff")), promoted.join("\n"));
});

test("H2–H4: an excluded name is reported as not emitted", () => {
  const errors = ocErrors("Use the `beta` skill.", [
    "  - source: sp/skills/alpha",
    "  - source: sp/skills/beta",
    "    exclude: true",
  ]);
  assert.ok(
    errors.some((m) => m.includes("names `beta`, which this estate does not emit")),
    errors.join("\n"),
  );
});

test("H2–H4: a name a committed ledger once listed stays in the estate", () => {
  const history = (root: string) => {
    const g = (...a: string[]) =>
      execFileSync("git", ["-c", "user.name=t", "-c", "user.email=t@example.test", "-c", "core.autocrlf=false", ...a], {
        cwd: root,
        stdio: "ignore",
      });
    const ledger = join(root, "docs", "ledger.json");
    const built = readFileSync(ledger, "utf8");
    g("init", "-q");
    writeFileSync(ledger, `${JSON.stringify({ "deniz-process/skill/gone": { source: "sp/skills/gone" } }, null, 2)}\n`);
    g("add", "docs/ledger.json");
    g("commit", "-qm", "gone listed");
    writeFileSync(ledger, built);
    g("add", "docs/ledger.json");
    g("commit", "-qm", "gone deleted");
  };
  const items = ["  - source: sp/skills/alpha"];
  assert.ok(
    ocErrors("Use the `gone` skill.", items, history).some((m) =>
      m.includes("names `gone`, which this estate does not emit"),
    ),
  );
  assert.ok(!ocErrors("Use the `gone` skill.", items).some((m) => m.includes("`gone`")), "no history, no name");
});

test("P: a landing climb fails closed, and a path to a manual item's body fails", () => {
  const errors = ocErrors(
    "See `../beta/references/` and `../gone/x.md` and `../delta/references/notes.md`. Read `../beta/SKILL.md` and `../beta/`.",
    [
      "  - source: sp/skills/alpha",
      "  - source: sp/skills/beta",
      "    invocation: manual",
      '    omit: ["references/**"]',
      "  - source: sp/skills/delta",
    ],
  );
  const spelled = {
    plugins: "../beta/references/",
    opencode: "../deniz-process.beta/references/",
    codex: "../beta/references/",
  };
  for (const [tree, path] of Object.entries(spelled)) {
    assert.ok(
      errors.some((m) => m.includes(`sibling path ${path} does not resolve in ${tree}/`)),
      `${tree}\n${errors.join("\n")}`,
    );
    assert.ok(
      errors.some((m) => m.includes(`sibling path ../gone/x.md does not resolve in ${tree}/`)),
      tree,
    );
  }
  const body = errors.filter((m) => m.includes("reaches manual item beta's body"));
  assert.equal(body.length, 6, `SKILL.md and the folder itself, in three trees\n${errors.join("\n")}`);
  assert.ok(!body.some((m) => m.includes("references/")), "a bundled file is not the body");
  assert.ok(!errors.some((m) => m.includes("references/notes.md")), "a resolving climb passes");
});

test("P: a path to a manual item's bundled file other than its body passes in every tree", () => {
  const errors = ocErrors("Read `../beta/references/notes.md` and [notes](../beta/references/notes.md).", [
    "  - source: sp/skills/alpha",
    "  - source: sp/skills/beta",
    "    invocation: manual",
  ]);
  assert.ok(!errors.some((m) => m.includes("references/notes.md") || m.includes("beta's body")), errors.join("\n"));
});

test("P: a resolving sibling link with a fragment or query passes in every tree", () => {
  const errors = ocErrors("See [notes](../delta/references/notes.md#part) and `../delta/references/notes.md?raw`.", [
    "  - source: sp/skills/alpha",
    "  - source: sp/skills/delta",
  ]);
  assert.ok(!errors.some((m) => m.includes("references/notes.md")), errors.join("\n"));
});

test("P: a claim followed by , ; or : resolves", () => {
  const errors = ocErrors(
    "See ../beta/SKILL.md, then ../beta/SKILL.md; and ../beta/SKILL.md: done. Also ../gone/x.md; end.",
    ["  - source: sp/skills/alpha", "  - source: sp/skills/beta", "    invocation: auto"],
  );
  // `beta/SKILL.md` also matches OpenCode's respelled `../deniz-process.beta/SKILL.md`.
  assert.ok(!errors.some((m) => m.includes("beta/SKILL.md")), errors.join("\n"));
  for (const tree of ["plugins", "opencode", "codex"]) {
    assert.ok(
      errors.some((m) => m.includes(`sibling path ../gone/x.md does not resolve in ${tree}/`)),
      `${tree}\n${errors.join("\n")}`,
    );
  }
});

test("P: an item-root path resolves in every tree once OpenCode respells it", () => {
  const errors = ocErrors("Read `skills/delta/references/notes.md` and `skills/delta/missing.md`.", [
    "  - source: sp/skills/alpha",
    "  - source: sp/skills/delta",
  ]);
  assert.ok(
    !errors.some(
      (m) => m.includes("skills/delta/references/notes.md") || m.includes("deniz-process.delta/references/notes.md"),
    ),
    errors.join("\n"),
  );
  assert.equal(errors.filter((m) => m.includes("missing.md does not resolve")).length, 3);
});

test("P: a script's landing climb fails closed like a Markdown one", () => {
  const root = makeRepo();
  const scripts = join(root, "external", "sp", "skills", "alpha", "scripts");
  mkdirSync(scripts, { recursive: true });
  writeFileSync(join(scripts, "run.sh"), "#!/bin/sh\ncat ../../gone/x.md ../../delta/references/notes.md\n");
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n  - source: sp/skills/delta\n",
  );
  buildAll(root);
  const errors = validateRepo(root)
    .filter((f) => f.level === "error")
    .map((f) => f.message);
  for (const tree of ["plugins", "opencode", "codex"]) {
    assert.ok(
      errors.some((m) => m.includes(`scripts/run.sh:2: sibling path ../../gone/x.md does not resolve in ${tree}/`)),
      `${tree}\n${errors.join("\n")}`,
    );
  }
  assert.ok(!errors.some((m) => m.includes("delta/references/notes.md")), "a resolving script climb passes");
});

test("P: a climb anchored at the script's directory is respelled for OpenCode and fails closed", () => {
  const root = makeRepo();
  const scripts = join(root, "external", "sp", "skills", "alpha", "scripts");
  mkdirSync(scripts, { recursive: true });
  writeFileSync(
    join(scripts, "run.sh"),
    '#!/bin/sh\nd="$(cd "$(dirname "$0")/../../delta/references" && pwd)"\ncat "$(dirname "$0")/../../gone/x.md"\n',
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n  - source: sp/skills/delta\n",
  );
  buildAll(root);
  assert.match(
    readFileSync(opencodeIdPath(root, "deniz-process", "skill", "alpha", "scripts", "run.sh"), "utf8"),
    /"\$\(dirname "\$0"\)\/\.\.\/\.\.\/deniz-process\.delta\/references"/,
  );
  const errors = validateRepo(root)
    .filter((f) => f.level === "error")
    .map((f) => f.message);
  for (const tree of ["plugins", "opencode", "codex"]) {
    assert.ok(
      errors.some((m) => m.includes(`scripts/run.sh:3: sibling path ../../gone/x.md does not resolve in ${tree}/`)),
      `${tree}\n${errors.join("\n")}`,
    );
  }
  assert.ok(!errors.some((m) => m.includes("delta/references")), `an anchored climb resolves\n${errors.join("\n")}`);
});

test("P: a Markdown link that is no claim still may not reach a manual item's body", () => {
  // `./../beta/` starts inside a longer path, so it is no landing climb; R1 reads it as a link.
  const errors = ocErrors("See [beta](./../beta/SKILL.md) and [notes](./../beta/references/notes.md).", [
    "  - source: sp/skills/alpha",
    "  - source: sp/skills/beta",
    "    invocation: manual",
  ]);
  for (const tree of ["plugins", "codex"]) {
    assert.ok(
      errors.some(
        (m) => m.startsWith(`${tree}/`) && m.includes("path ./../beta/SKILL.md reaches manual item beta's body"),
      ),
      `${tree}\n${errors.join("\n")}`,
    );
  }
  assert.ok(!errors.some((m) => m.includes("references/notes.md")), errors.join("\n"));
});

test("an unknown source, taken or excluded, names the deleted-source rule", () => {
  for (const extra of [[], ["    exclude: true"]]) {
    const root = makeRepo();
    const p = join(root, "curation", "deniz-process.yaml");
    writeFileSync(
      p,
      `${readFileSync(p, "utf8")}  - source: sp/skills/vanished\n${extra.join("\n")}${extra.length ? "\n" : ""}`,
    );
    const msg = validateRepo(root).find((f) => f.message.includes("unknown source sp/skills/vanished"));
    assert.ok(msg, "reported");
    assert.match(msg.message, /remove the item from the manifest — an exclude is not a tombstone/);
    assert.match(msg.message, /in that item's comment/);
    assert.match(msg.message, /lands with or after that pin move/);
  }
});

test("harness files: each tree holds its own file at the output path and no mapped source", () => {
  const root = makeHarnessFilesRepo();
  const p = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    p,
    readFileSync(p, "utf8").replace("    harness_files:\n", "    depends_on: [gamma]\n    harness_files:\n"),
  );
  buildAll(root);
  const errors = () =>
    validateRepo(root)
      .filter((f) => f.level === "error")
      .map((f) => f.message);
  const before = errors();
  assert.ok(!before.some((m) => m.includes("Claude-only vocabulary")), "the Claude notes reach only the Claude tree");
  assert.ok(!before.some((m) => m.includes("delta")), before.join("\n"));

  rmSync(opencodeIdPath(root, "deniz-process", "skill", "delta", "references", "tools.md"));
  writeFileSync(codexPluginPath(root, "deniz-process", "skills", "delta", "references", "claude.md"), "stray\n");
  const after = errors();
  assert.ok(
    after.some((m) =>
      m.includes(
        "opencode/deniz-process/skills/deniz-process.delta/references/tools.md: harness_files places references/opencode.md here, but the file is missing",
      ),
    ),
    after.join("\n"),
  );
  assert.ok(
    after.some((m) =>
      m.includes(
        "codex/deniz-process/skills/delta/references/claude.md: a harness_files source ships under its own name",
      ),
    ),
    after.join("\n"),
  );
});

/** makeHarnessFilesRepo with each harness's notes rewritten and delta's depends_on set. */
function harnessFactsRepo(
  notes: Partial<Record<"claude" | "codex" | "opencode", string>>,
  dependsOn: string[],
): string {
  const root = makeHarnessFilesRepo();
  const refs = join(root, "external", "sp", "skills", "delta", "references");
  for (const [harness, text] of Object.entries(notes)) {
    writeFileSync(join(refs, `${harness}.md`), `# Notes\n\n${text}\n`);
  }
  const p = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    p,
    readFileSync(p, "utf8").replace(
      "    harness_files:\n",
      `    depends_on: [${dependsOn.join(", ")}]\n    harness_files:\n`,
    ),
  );
  return root;
}

const errorsOf = (root: string): string[] =>
  validateRepo(root)
    .filter((f) => f.level === "error")
    .map((f) => f.message);

test("linker: a fact only another harness's file holds counts toward depends_on", () => {
  const declared = harnessFactsRepo({ claude: "See superpowers:gamma.", opencode: "Then load superpowers:alpha." }, [
    "alpha",
    "gamma",
  ]);
  buildAll(declared);
  const passing = errorsOf(declared);
  assert.ok(!passing.some((m) => m.includes("delta")), passing.join("\n"));

  const undeclared = harnessFactsRepo({ claude: "See superpowers:gamma.", codex: "Then load superpowers:alpha." }, [
    "gamma",
  ]);
  buildAll(undeclared);
  const failing = errorsOf(undeclared);
  assert.ok(
    failing.includes(
      "deniz-process/delta: undeclared dependency: alpha — add it to depends_on in curation/deniz-process.yaml",
    ),
    failing.join("\n"),
  );
});

test("linker: a fact in one harness's file is reached in that harness's tree", () => {
  const root = harnessFactsRepo(
    {
      claude: "See superpowers:gamma.",
      opencode: "Then load superpowers:alpha.",
      codex: "Then load superpowers:alpha.",
    },
    ["alpha", "gamma"],
  );
  const p = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    p,
    readFileSync(p, "utf8").replace(
      "  - source: sp/skills/alpha\n",
      "  - source: sp/skills/alpha\n    invocation: manual\n",
    ),
  );
  buildAll(root);
  const errors = errorsOf(root).filter((m) => m.includes("model-edge to a target the model cannot reach: alpha"));
  assert.deepEqual(
    errors.map((m) => m.slice(0, m.indexOf(": model-edge"))),
    [
      "opencode/deniz-process/skills/deniz-process.delta/references/tools.md",
      "codex/deniz-process/skills/delta/references/tools.md",
    ],
  );
  assert.match(errors[0] ?? "", /\(hidden from the OpenCode model \(opencode\/autoinvoke\)\)/);
  assert.match(errors[1] ?? "", /\(allow_implicit_invocation: false in the Codex tree\)/);
});

test("H and I: another harness's own file is held to the handoff and interpreter checks", () => {
  const root = harnessFactsRepo(
    {
      claude: "See superpowers:gamma.",
      opencode: 'Call the Skill tool with "gamma".',
      codex: "Load `gamma` first, then run `scripts/check --all`.",
    },
    ["gamma"],
  );
  const scripts = join(root, "external", "sp", "skills", "delta", "scripts");
  mkdirSync(scripts, { recursive: true });
  writeFileSync(join(scripts, "check"), "#!/usr/bin/env bash\necho ok\n");
  buildAll(root);
  const errors = errorsOf(root);
  const opencode = "opencode/deniz-process/skills/deniz-process.delta/references/tools.md:3";
  const codex = "codex/deniz-process/skills/delta/references/tools.md:3";
  for (const expected of [
    `${opencode}: skill-tool handle "gamma" is not a namespaced fact`,
    `${codex}: load-bearing handoff names \`gamma\` bare`,
    `${codex}: runs bundled script scripts/check by bare path — write \`bash scripts/check\``,
  ]) {
    assert.ok(
      errors.some((m) => m.startsWith(expected)),
      `${expected}\n${errors.join("\n")}`,
    );
  }
});

test("harness files: a tree whose harness maps null holds nothing at the output path", () => {
  const root = makeHarnessFilesRepo("null");
  buildAll(root);
  writeFileSync(opencodeIdPath(root, "deniz-process", "skill", "delta", "references", "tools.md"), "stray\n");
  const errors = validateRepo(root)
    .filter((f) => f.level === "error")
    .map((f) => f.message);
  assert.ok(
    errors.some((m) =>
      m.includes(
        "opencode/deniz-process/skills/deniz-process.delta/references/tools.md: harness_files maps no opencode source here, but the file exists",
      ),
    ),
    errors.join("\n"),
  );
});

test("I: a body that runs a bundled script by bare path is an error; naming the interpreter passes", () => {
  const root = makeRepo();
  const delta = join(root, "external", "sp", "skills", "delta");
  mkdirSync(join(delta, "scripts"), { recursive: true });
  writeFileSync(join(delta, "scripts", "check"), "#!/usr/bin/env bash\necho ok\n");
  writeFileSync(join(delta, "scripts", "lint.py"), "print('ok')\n");
  writeFileSync(
    join(delta, "SKILL.md"),
    [
      "---",
      "name: delta",
      "description: D",
      "---",
      "Run `scripts/check --all` first.",
      "Then run `python3 scripts/lint.py` (on Windows, `py -3 scripts/lint.py`).",
      "The helper `scripts/check` is only mentioned here.",
      "",
    ].join("\n"),
  );
  buildAll(root);
  const errors = validateRepo(root)
    .filter((f) => f.level === "error")
    .map((f) => f.message)
    .filter((m) => m.includes("bundled script"));
  assert.deepEqual(errors, [
    "plugins/deniz-process/skills/delta/SKILL.md:6: runs bundled script scripts/check by bare path — write `bash scripts/check`",
  ]);
});

test("an original skill without a declaration is a validate error", () => {
  const root = makeRepo();
  addOriginalSkill(root, "my-own");
  buildAll(root);
  mkdirSync(join(root, "skills", "deniz-process", "stray"), { recursive: true });
  writeFileSync(join(root, "skills", "deniz-process", "stray", "SKILL.md"), "---\nname: stray\ndescription: S\n---\n");

  assert.ok(
    errorsOf(root).includes(
      "skills/deniz-process/stray/ has no declaration — add original_skills: stray: with its invocation to curation/deniz-process.yaml",
    ),
  );
});

// Moves a real directory out of skills/ and links it back in place.
function linkInPlace(root: string, path: string): boolean {
  const real = join(root, "elsewhere", path.replaceAll("/", "-"));
  mkdirSync(join(root, "elsewhere"), { recursive: true });
  renameSync(join(root, path), real);
  try {
    symlinkSync(real, join(root, path), "dir");
    return true;
  } catch {
    return false;
  }
}

for (const path of ["skills/deniz-process", "skills/deniz-process/my-own"]) {
  test(`validate names a symlinked ${path} instead of reading through it`, (t) => {
    const root = makeRepo();
    addOriginalSkill(root, "my-own");
    buildAll(root);
    if (!linkInPlace(root, path)) {
      t.skip("creating symlinks requires elevated privileges on this platform");
      return;
    }

    const errors = errorsOf(root);
    assert.ok(
      errors.includes(
        `${path} is a symlink — original skills are real directories, and the build never follows a link`,
      ),
      errors.join("\n"),
    );
  });
}

test("validate names an original SKILL.md whose frontmatter does not parse", () => {
  const root = makeRepo();
  addOriginalSkill(root, "my-own");
  buildAll(root);
  writeFileSync(
    join(root, "skills", "deniz-process", "my-own", "SKILL.md"),
    "---\nname: [my-own\ndescription: Mine\n---\n\nMine.\n",
  );

  const errors = errorsOf(root);
  assert.ok(
    errors.some((m) => m.startsWith("skills/deniz-process/my-own/SKILL.md: frontmatter does not parse — ")),
    errors.join("\n"),
  );
});

test("validate names a dangling symlink under skills/<plugin>/ instead of throwing", (t) => {
  const root = makeRepo();
  addOriginalSkill(root, "my-own");
  buildAll(root);
  try {
    symlinkSync(join(root, "skills", "deniz-process", "gone"), join(root, "skills", "deniz-process", "ghost"), "dir");
  } catch {
    t.skip("creating symlinks requires elevated privileges on this platform");
    return;
  }

  const errors = errorsOf(root);
  assert.ok(
    errors.includes(
      "skills/deniz-process/ghost is a symlink — original skills are real directories, and the build never follows a link",
    ),
    errors.join("\n"),
  );
});

test("a manual original skill's Codex policy is expected and checked", () => {
  const root = makeRepo();
  addOriginalSkill(root, "own-manual", { declaration: { invocation: "manual" } });
  buildAll(root);
  assert.deepEqual(
    errorsOf(root).filter((m) => m.includes("own-manual")),
    [],
  );

  rmSync(codexPluginPath(root, "deniz-process", "skills", "own-manual", "agents", "openai.yaml"));

  assert.ok(
    errorsOf(root).includes(
      "codex/deniz-process/skills/own-manual/agents/openai.yaml is required for manual invocation",
    ),
  );
});

test("a model fact into a manual original skill fails in every tree with that tree's cause", () => {
  const root = harnessFactsRepo(
    {
      claude: "Then load deniz-process:own-manual.",
      opencode: "Then load deniz-process:own-manual.",
      codex: "Then load deniz-process:own-manual.",
    },
    ["own-manual"],
  );
  addOriginalSkill(root, "own-manual", { declaration: { invocation: "manual" } });
  buildAll(root);
  const errors = errorsOf(root).filter((m) => m.includes("model-edge to a target the model cannot reach: own-manual"));
  assert.deepEqual(
    errors.map((m) => m.slice(0, m.indexOf(": model-edge"))),
    [
      "plugins/deniz-process/skills/delta/references/tools.md",
      "opencode/deniz-process/skills/deniz-process.delta/references/tools.md",
      "codex/deniz-process/skills/delta/references/tools.md",
    ],
  );
  assert.match(errors[0] ?? "", /\(disable-model-invocation in the Claude tree\)/);
  assert.match(errors[1] ?? "", /\(hidden from the OpenCode model \(opencode\/autoinvoke\)\)/);
  assert.match(errors[2] ?? "", /\(allow_implicit_invocation: false in the Codex tree\)/);
});

test("harness files of an original skill: each tree holds only its own file, and Claude words stay in Claude", () => {
  const root = makeRepo();
  addOriginalHarnessNotes(root);
  buildAll(root);
  const before = errorsOf(root);
  assert.ok(!before.some((m) => m.includes("Claude-only vocabulary")), "the Claude notes reach only the Claude tree");
  assert.ok(!before.some((m) => m.includes("own-notes")), before.join("\n"));

  rmSync(opencodeIdPath(root, "deniz-process", "skill", "own-notes", "references", "tools.md"));
  writeFileSync(codexPluginPath(root, "deniz-process", "skills", "own-notes", "references", "claude.md"), "stray\n");
  const after = errorsOf(root);
  assert.ok(
    after.includes(
      "opencode/deniz-process/skills/deniz-process.own-notes/references/tools.md: harness_files places references/opencode.md here, but the file is missing",
    ),
    after.join("\n"),
  );
  assert.ok(
    after.includes(
      "codex/deniz-process/skills/own-notes/references/claude.md: a harness_files source ships under its own name — each tree gets it only at its output path",
    ),
    after.join("\n"),
  );
});

/**
 * makeRepo plus an original skill own-src whose per-harness notes ship at references/tools.md, each
 * written like harnessFactsRepo's so the sentence sits on line 3.
 */
function ownNotesRepo(
  notes: Record<"claude" | "codex" | "opencode", string>,
  declaration: Record<string, unknown>,
): string {
  const root = makeRepo();
  addOriginalSkill(root, "own-src", {
    files: Object.fromEntries(
      Object.entries(notes).map(([harness, text]) => [`references/${harness}.md`, `# Notes\n\n${text}\n`]),
    ),
    declaration: {
      ...declaration,
      harness_files: {
        "references/tools.md": {
          claude: "references/claude.md",
          codex: "references/codex.md",
          opencode: "references/opencode.md",
        },
      },
    },
  });
  return root;
}

test("linker: an original skill's undeclared and stale edges name its declaration", () => {
  const root = makeRepo();
  addOriginalSkill(root, "own-src", {
    declaration: { invocation: "auto", depends_on: ["gamma"] },
    body: "Load superpowers:delta.\n",
  });
  buildAll(root);
  const errors = errorsOf(root);
  for (const expected of [
    "deniz-process/own-src: undeclared dependency: delta — add it to original_skills.own-src.depends_on in curation/deniz-process.yaml",
    "deniz-process/own-src: stale depends_on: gamma — no model-edge in any tree's shipped body references it",
  ]) {
    assert.ok(errors.includes(expected), `${expected}\n${errors.join("\n")}`);
  }
});

test("linker: an original skill's dangling fact fails in every tree", () => {
  const root = makeRepo();
  addOriginalSkill(root, "own-src", {
    declaration: { invocation: "auto" },
    body: "Load deniz-process:nowhere.\n",
  });
  buildAll(root);
  const errors = errorsOf(root);
  for (const expected of [
    "plugins/deniz-process/skills/own-src/SKILL.md: dangling reference deniz-process:nowhere — no built output has that name",
    "opencode/deniz-process/skills/deniz-process.own-src/SKILL.md: output namespace leaked into opencode/: deniz-process:nowhere",
    "codex/deniz-process/skills/own-src/SKILL.md: unrendered Codex reference deniz-process:nowhere",
  ]) {
    assert.ok(errors.includes(expected), `${expected}\n${errors.join("\n")}`);
  }
});

test("linker: a fact only an original skill's own harness file holds is linked in that tree", () => {
  const undeclared = ownNotesRepo(
    {
      claude: "See superpowers:gamma.",
      opencode: "Then load superpowers:alpha.",
      codex: "Then load superpowers:delta.",
    },
    { invocation: "auto", depends_on: ["gamma"] },
  );
  buildAll(undeclared);
  const failing = errorsOf(undeclared);
  for (const expected of [
    "deniz-process/own-src: undeclared dependency: alpha — add it to original_skills.own-src.depends_on in curation/deniz-process.yaml",
    "deniz-process/own-src: undeclared dependency: delta — add it to original_skills.own-src.depends_on in curation/deniz-process.yaml",
  ]) {
    assert.ok(failing.includes(expected), `${expected}\n${failing.join("\n")}`);
  }

  const handoff = ownNotesRepo(
    {
      claude: "See superpowers:gamma.",
      opencode: 'Call the Skill tool with "gamma".',
      codex: "See superpowers:gamma.",
    },
    { invocation: "auto", depends_on: ["gamma"] },
  );
  buildAll(handoff);
  const errors = errorsOf(handoff);
  const expected =
    'opencode/deniz-process/skills/deniz-process.own-src/references/tools.md:3: skill-tool handle "gamma" is not a namespaced fact';
  assert.ok(
    errors.some((m) => m.startsWith(expected)),
    `${expected}\n${errors.join("\n")}`,
  );
});

test("linker: a model fact from an original skill into a manual original fails in every tree", () => {
  const fact = "Then load deniz-process:own-manual.";
  const root = ownNotesRepo(
    { claude: fact, opencode: fact, codex: fact },
    { invocation: "auto", depends_on: ["own-manual"] },
  );
  addOriginalSkill(root, "own-manual", { declaration: { invocation: "manual" } });
  buildAll(root);
  const errors = errorsOf(root).filter((m) => m.includes("model-edge to a target the model cannot reach: own-manual"));
  assert.deepEqual(
    errors.map((m) => m.slice(0, m.indexOf(": model-edge"))),
    [
      "plugins/deniz-process/skills/own-src/references/tools.md",
      "opencode/deniz-process/skills/deniz-process.own-src/references/tools.md",
      "codex/deniz-process/skills/own-src/references/tools.md",
    ],
  );
  assert.match(errors[0] ?? "", /\(disable-model-invocation in the Claude tree\)/);
  assert.match(errors[1] ?? "", /\(hidden from the OpenCode model \(opencode\/autoinvoke\)\)/);
  assert.match(errors[2] ?? "", /\(allow_implicit_invocation: false in the Codex tree\)/);
});

test("P: a Markdown link into a manual original's body fails as a curated one does", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: A\n---\nSee [m](./../own-manual/SKILL.md) and [n](./../own-manual/references/notes.md).\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n",
  );
  addOriginalSkill(root, "own-manual", {
    declaration: { invocation: "manual" },
    files: { "references/notes.md": "Notes.\n" },
  });
  buildAll(root);
  const errors = errorsOf(root);
  for (const tree of ["plugins", "codex"]) {
    assert.ok(
      errors.some(
        (m) =>
          m.startsWith(`${tree}/`) && m.includes("path ./../own-manual/SKILL.md reaches manual item own-manual's body"),
      ),
      `${tree}\n${errors.join("\n")}`,
    );
  }
  assert.ok(!errors.some((m) => m.includes("references/notes.md")), errors.join("\n"));
});
