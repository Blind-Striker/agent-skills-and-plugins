import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { buildAll } from "./build.ts";
import { loadLock, lockKey, saveLock, stampFiles } from "./lib/overlay.ts";
import { makeRepo } from "./testutil.ts";
import { validateRepo } from "./validate.ts";

// The fixture curates sp/skills/beta twice, so the rewrite (last-write-wins) points alpha's
// model-edge at an AGENT — an edge the model cannot traverse, and undeclared besides. That debt is
// the linker working, not a regression: it is tolerated by name so every OTHER error class stays a
// hard zero on a clean build.
const FIXTURE_DEBT = ["model-edge to a target the model cannot reach: beta-agent", "undeclared dependency: beta-agent"];

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
});

// Two plugins can each curate the same upstream skill without either manifest looking wrong, but
// Claude Code addresses a skill by name alone, so only one of the two would ever be reachable.
test("duplicate output name across plugins is an error", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-other.yaml"),
    "plugin:\n  name: deniz-other\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n",
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("duplicate skill name across plugins: alpha")),
    `expected a duplicate-name error, got ${JSON.stringify(findings, null, 2)}`,
  );
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

// ADR-0005: the field applies to items emitted as skills. An upstream command or agent is
// user-invoked by nature, so stating an intent there describes nothing.
test("invocation on a converted item is a warning", () => {
  const root = makeRepo();
  const manifest = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    manifest,
    readFileSync(manifest, "utf8").replace("    as: agent", "    as: agent\n    invocation: manual"),
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "warn" && f.message.includes("beta-agent") && f.message.includes("invocation")),
    `expected an invocation-on-conversion warning, got ${JSON.stringify(findings, null, 2)}`,
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

test("linker: a model-edge to a manual target is an error in both trees", () => {
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
      "    invocation: auto", // OpenCode gets no command for beta -> nothing for a user to type
    ].join("\n")}\n`,
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("pointer to a target the user cannot reach")),
    JSON.stringify(findings, null, 2),
  );
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

test("linker: an output namespace leaking into opencode/ is an error", () => {
  const root = makeRepo();
  buildAll(root);
  // simulate the historical bug: a hand-authored output-space reference surviving into opencode
  writeFileSync(
    join(root, "opencode", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: x\n---\nUse the deniz-process:beta skill.\n",
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

test("linker: a command body naming a missing parked file is an error", () => {
  const root = makeRepo();
  writeFileSync(join(root, "external", "sp", "skills", "beta", "notes.md"), "bundled\n");
  writeFileSync(
    join(root, "external", "sp", "skills", "beta", "SKILL.md"),
    "---\nname: beta\ndescription: Beta upstream\n---\nRead skills/beta/other.md first.\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/beta",
      "    invocation: manual",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("skills/beta/other.md")),
    JSON.stringify(findings, null, 2),
  );
});

test("linker: an own skill colliding with a curated item in the same plugin is an error", () => {
  const root = makeRepo();
  mkdirSync(join(root, "skills", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "skills", "deniz-process", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: own\n---\nOwn.\n",
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("own skill") && f.message.includes("alpha")),
    JSON.stringify(findings, null, 2),
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
