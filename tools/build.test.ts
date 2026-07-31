import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { existsSync, lstatSync, mkdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { buildAll } from "./build.ts";
import { parseDoc } from "./lib/frontmatter.ts";
import { makeRepo } from "./testutil.ts";

test("buildAll compiles plugins with overrides, overlays, conversions, rewrites", () => {
  const root = makeRepo();
  const report = buildAll(root);
  assert.ok(report.length > 0);
  assert.ok(report.includes("deniz-process: skill alpha <- sp/skills/alpha"));
  assert.ok(report.includes("WARN deniz-process/deniz-beta: dropped in skill->command conversion: references"));

  // skill copied with frontmatter override + reference rewrite. beta is curated twice (command +
  // agent) and buildRewriteMap is last-write-wins per source, so alpha's ref maps to the agent name.
  const alpha = parseDoc(readFileSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"), "utf8"));
  assert.equal(alpha.frontmatter.description, "Alpha curated");
  // forced name wins over item.frontmatter's name: sneaky — dir name and rewrite map both use outName
  assert.equal(alpha.frontmatter.name, "alpha");
  assert.match(alpha.body, /deniz-process:beta-agent/);
  assert.doesNotMatch(alpha.body, /superpowers:beta/);

  // skill -> command conversion with overlay body
  const cmd = parseDoc(readFileSync(join(root, "plugins", "deniz-process", "commands", "deniz-beta.md"), "utf8"));
  assert.equal(cmd.frontmatter.description, "Beta overlay");
  assert.match(cmd.body, /Overlay body/);

  // own skill copied
  assert.ok(existsSync(join(root, "plugins", "deniz-process", "skills", "my-own", "SKILL.md")));

  // plugin.json + marketplace.json
  const pj = JSON.parse(readFileSync(join(root, "plugins", "deniz-process", ".claude-plugin", "plugin.json"), "utf8"));
  assert.equal(pj.name, "deniz-process");
  const mp = JSON.parse(readFileSync(join(root, ".claude-plugin", "marketplace.json"), "utf8"));
  assert.equal(mp.plugins[0].source, "./plugins/deniz-process");
});

test("buildAll emits opencode tree and reports dropped keys", () => {
  const root = makeRepo();
  const report = buildAll(root);
  // directory names are the ones OpenCode documents: skills/, commands/, agents/ — all plural
  assert.ok(existsSync(join(root, "opencode", "skills", "alpha", "SKILL.md")));
  assert.ok(existsSync(join(root, "opencode", "skills", "my-own", "SKILL.md")));
  const cmd = parseDoc(readFileSync(join(root, "opencode", "commands", "deniz-beta.md"), "utf8"));
  assert.equal(cmd.frontmatter.description, "Beta overlay");
  // each tree carries the reference spelling its own harness resolves: OpenCode has no plugin
  // concept, so the qualified form would resolve to nothing there
  const alpha = readFileSync(join(root, "opencode", "skills", "alpha", "SKILL.md"), "utf8");
  assert.doesNotMatch(alpha, /deniz-process:beta-agent/);
  assert.match(alpha, /(^|[^:\w-])beta-agent\b/);
  assert.match(
    readFileSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"), "utf8"),
    /deniz-process:beta-agent/,
  );

  // skill -> agent conversion, plugins side: forced name, item.frontmatter carried, description from source
  const agentPath = join(root, "plugins", "deniz-process", "agents", "beta-agent.md");
  assert.ok(existsSync(agentPath));
  const agent = parseDoc(readFileSync(agentPath, "utf8"));
  assert.equal(agent.frontmatter.name, "beta-agent");
  assert.equal(agent.frontmatter.model, "opus");
  assert.equal(agent.frontmatter.description, "Beta upstream");

  // opencode side keeps description + mode only; model is dropped and reported, never silently lost
  const ocAgent = parseDoc(readFileSync(join(root, "opencode", "agents", "beta-agent.md"), "utf8"));
  assert.equal(ocAgent.frontmatter.mode, "subagent");
  assert.equal(ocAgent.frontmatter.description, "Beta upstream");
  assert.equal("model" in ocAgent.frontmatter, false);
  assert.ok(report.includes("opencode agent beta-agent.md: dropped frontmatter keys: model"));
});

// ADR-0006 axis 3. The OpenCode skill path was a verbatim copy of the Claude one, so Claude-only
// frontmatter arrived as dead metadata with no drop report, and cross-references kept Claude's
// <plugin>:<name> spelling — which OpenCode cannot resolve, since it addresses a skill by its name.
test("the OpenCode skill path adapts rather than mirrors", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha", // body references superpowers:beta
      "    invocation: manual", // a Claude-only key the OpenCode copy must not carry
      "  - source: sp/skills/beta",
    ].join("\n")}\n`,
  );
  const report = buildAll(root);

  const claude = parseDoc(readFileSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"), "utf8"));
  const oc = parseDoc(readFileSync(join(root, "opencode", "commands", "alpha.md"), "utf8"));

  // Claude keeps its own dial; OpenCode has no use for it and must not be handed it
  assert.equal(claude.frontmatter["disable-model-invocation"], true);
  assert.equal("disable-model-invocation" in oc.frontmatter, false, "Claude-only keys must not travel");
  assert.ok(
    report.some((l) => l.includes("alpha") && l.includes("disable-model-invocation")),
    `every dropped key is reported, never silently lost — got ${JSON.stringify(report, null, 2)}`,
  );

  // each tree gets the reference spelling its own harness resolves
  assert.match(claude.body, /deniz-process:beta/, "Claude addresses a plugin skill namespaced");
  assert.doesNotMatch(oc.body, /deniz-process:beta/, "that spelling is meaningless to OpenCode");
  assert.match(oc.body, /(^|[^:\w-])beta\b/, "OpenCode addresses a skill by its bare name");
});

// ADR-0005: one word per item says who pulls the trigger, and each emitter derives its own
// mechanism — a frontmatter flag in Claude Code, a choice of artifact in OpenCode.
test("invocation sets the Claude flags and picks the OpenCode artifact", () => {
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
      "    invocation: auto",
      "  - source: sp/skills/beta",
      "    invocation: manual",
      "  - source: sp/skills/delta",
      "    invocation: both",
      "  - source: sp/skills/gamma", // states nothing — must stay untouched
    ].join("\n")}\n`,
  );
  buildAll(root);
  const fm = (...p: string[]) => parseDoc(readFileSync(join(root, ...p), "utf8")).frontmatter;

  // Silence is not a default: an item that states no intent keeps upstream's frontmatter
  const gamma = fm("plugins", "deniz-process", "skills", "gamma", "SKILL.md");
  assert.equal("user-invocable" in gamma, false, "absent must not imply auto");
  assert.equal("disable-model-invocation" in gamma, false);

  // Claude Code: one artifact, the dial is frontmatter
  const alpha = fm("plugins", "deniz-process", "skills", "alpha", "SKILL.md");
  assert.equal(alpha["user-invocable"], false);
  assert.equal("disable-model-invocation" in alpha, false);
  const beta = fm("plugins", "deniz-process", "skills", "beta", "SKILL.md");
  assert.equal(beta["disable-model-invocation"], true);
  assert.equal("user-invocable" in beta, false);
  const delta = fm("plugins", "deniz-process", "skills", "delta", "SKILL.md");
  assert.equal("user-invocable" in delta, false, "both sets neither key");
  assert.equal("disable-model-invocation" in delta, false);

  // OpenCode: the dial is which artifact exists
  assert.ok(existsSync(join(root, "opencode", "skills", "alpha", "SKILL.md")));
  assert.ok(!existsSync(join(root, "opencode", "commands", "alpha.md")), "auto is model-only");

  assert.ok(existsSync(join(root, "opencode", "commands", "beta.md")), "manual is a command");
  assert.ok(
    !existsSync(join(root, "opencode", "skills", "beta", "SKILL.md")),
    "a manual item must not also be a model-reachable skill",
  );
  // ...but its bundled files still need a home the command body can point at, and a directory
  // with no SKILL.md is ignored by OpenCode's discovery — measured, see the research note.
  assert.ok(
    existsSync(join(root, "opencode", "skills", "beta", "references", "notes.md")),
    "bundled files are parked where the command can reach them",
  );

  assert.ok(existsSync(join(root, "opencode", "skills", "delta", "SKILL.md")), "both emits a skill");
  assert.ok(existsSync(join(root, "opencode", "commands", "delta.md")), "both emits a command too");
});

// Author-facing files travel with upstream skills — creation logs, pressure tests, fixtures. Until
// now the only way to leave one behind was to own the whole item through an overlay.
test("omit drops matching files from a curated skill", () => {
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
      "    omit:",
      '      - "references/**"',
    ].join("\n")}\n`,
  );
  buildAll(root);
  const dest = join(root, "plugins", "deniz-process", "skills", "delta");
  assert.ok(existsSync(join(dest, "SKILL.md")), "the skill itself still ships");
  assert.ok(!existsSync(join(dest, "references", "notes.md")), "omitted file is gone");
  // an emptied directory is not left behind as a husk
  assert.ok(!existsSync(join(dest, "references")), "emptied directory is pruned");
  // the OpenCode mirror is emitted from plugins/, so it inherits the omission
  assert.ok(!existsSync(join(root, "opencode", "skills", "delta", "references")));
});

// Omitting a file the patch edits would leave the patch nothing to land on. git apply would say so,
// but only after the output tree was already deleted — this belongs in the fail-fast pass.
test("omit that swallows a patch target aborts the build", () => {
  const root = makeRepo();
  const manifest = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    manifest,
    readFileSync(manifest, "utf8").replace("    body: patch", '    body: patch\n    omit:\n      - "SKILL.md"'),
  );
  assert.throws(() => buildAll(root), /gamma.*SKILL\.md/s);
});

// aspire-skills links evals/fixtures out of its skill dir; cpSync would copy that link with its
// target resolved to an absolute local path — a dangling, machine-specific artifact once committed.
test("symlinks inside a curated skill are skipped, not copied", (t) => {
  const root = makeRepo();
  mkdirSync(join(root, "external", "sp", "shared"), { recursive: true });
  writeFileSync(join(root, "external", "sp", "shared", "fixture.txt"), "outside the skill dir\n");
  try {
    symlinkSync(join("..", "..", "shared"), join(root, "external", "sp", "skills", "alpha", "fixtures"), "dir");
  } catch {
    t.skip("creating symlinks requires elevated privileges on this platform");
    return;
  }
  const report = buildAll(root);
  const copied = join(root, "plugins", "deniz-process", "skills", "alpha", "fixtures");
  // nothing at all at that path: not a real dir, not a dangling link
  assert.throws(() => lstatSync(copied), /ENOENT/);
  // the rest of the skill still copied — the filter must reject only the link
  assert.ok(existsSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md")));
  assert.ok(
    report.includes("WARN deniz-process/alpha: skipped symlink external/sp/skills/alpha/fixtures"),
    `expected a skipped-symlink warning, got ${JSON.stringify(report, null, 2)}`,
  );
});

test("missing overlay throws a helpful error", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n    body: overlay\n",
  );
  assert.throws(() => buildAll(root), /overlay/);
});

// The build wipes plugins/ and opencode/ before re-emitting them, so any failure it discovers
// mid-emit destroys committed output and leaves a stale marketplace.json behind. Every
// per-item failure has to be found by the pre-pass, i.e. before the first rmSync.
test("unresolvable items abort the build before any output is deleted, and report all of them", () => {
  const root = makeRepo();
  buildAll(root);
  const alpha = join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md");
  assert.ok(existsSync(alpha));
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    [
      "plugin:",
      "  name: deniz-process",
      "  description: d",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/typo",
      "  - source: sp/skills/alpha",
      "    body: overlay",
      "",
    ].join("\n"),
  );
  assert.throws(() => buildAll(root), /sp\/skills\/typo/);
  // both problems in one message: a build must not have to be re-run once per typo
  assert.throws(() => buildAll(root), /overlays\/deniz-process\/alpha\/ is missing/);
  assert.ok(existsSync(alpha), "previous build output must survive an aborted build");
});

test("an overlay directory missing the file the build reads aborts before deleting output", () => {
  const root = makeRepo();
  buildAll(root);
  const alpha = join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md");
  // deniz-beta converts sp/skills/beta to a command, so the build reads overlays/.../SKILL.md
  rmSync(join(root, "overlays", "deniz-process", "deniz-beta", "SKILL.md"));
  assert.throws(() => buildAll(root), /deniz-beta\/SKILL\.md is missing/);
  assert.ok(existsSync(alpha), "previous build output must survive an aborted build");
});

test("a patch overlay edits the emitted skill and leaves overlay.patch out of the output", () => {
  const root = makeRepo();
  const report = buildAll(root);
  const dir = join(root, "plugins", "deniz-process", "skills", "gamma");
  const body = readFileSync(join(dir, "SKILL.md"), "utf8");
  assert.match(body, /Patched line\./);
  assert.doesNotMatch(body, /Replace this line\./);
  assert.match(body, /Keep this line\./, "unpatched content must survive");
  assert.ok(!existsSync(join(dir, "overlay.patch")), "the patch is an input, never shipped");
  assert.ok(report.includes("deniz-process: skill gamma <- sp/skills/gamma"));
});

// Regression: inside a work tree, `git apply` resolves patch paths against the repository root and
// silently ignores anything outside the cwd — exit 0, nothing patched. A fixture in tmpdir is not a
// repository, so it cannot see this; only a git-initialised one reproduces the real tree.
test("a patch applies when the tree is inside a git repository", () => {
  const root = makeRepo();
  execFileSync("git", ["init", "-q", "."], { cwd: root });
  buildAll(root);
  const body = readFileSync(join(root, "plugins", "deniz-process", "skills", "gamma", "SKILL.md"), "utf8");
  assert.match(body, /Patched line\./, "the patch must land even when the output sits in a repository");
  assert.doesNotMatch(body, /Replace this line\./);
});

// `git apply` would have taken this change silently — it does not touch the patched region. The
// hash is what stops it, and the improvement only lands once a human has blessed it.
test("upstream changing a patched file stops the build, and is absorbed once blessed", () => {
  const root = makeRepo();
  const up = join(root, "external", "sp", "skills", "gamma", "SKILL.md");
  writeFileSync(up, readFileSync(up, "utf8").replace("Far region.", "Far region, improved upstream."));
  assert.throws(() => buildAll(root), /upstream changed under the overlay \(SKILL\.md\)/);

  execFileSync(process.execPath, [join(import.meta.dirname, "eject.ts"), "deniz-process", "gamma", "--bless"], {
    cwd: root,
    stdio: "ignore",
  });
  buildAll(root);
  const body = readFileSync(join(root, "plugins", "deniz-process", "skills", "gamma", "SKILL.md"), "utf8");
  assert.match(body, /Far region, improved upstream\./, "the upstream improvement lands after blessing");
  assert.match(body, /Patched line\./, "our edit must survive");
});

test("a patch that no longer applies aborts before any output is deleted", () => {
  const root = makeRepo();
  buildAll(root);
  const alpha = join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md");
  const up = join(root, "external", "sp", "skills", "gamma", "SKILL.md");
  writeFileSync(up, readFileSync(up, "utf8").replace("Replace this line.", "Upstream rewrote this line."));
  assert.throws(() => buildAll(root), /overlay\.patch no longer applies to sp\/skills\/gamma/);
  assert.ok(existsSync(alpha), "previous build output must survive an aborted build");
});

// A full-file overlay cannot detect upstream moving under it, so the recorded hash has to.
test("upstream drifting under a full-file overlay aborts with the bless command", () => {
  const root = makeRepo();
  buildAll(root);
  const up = join(root, "external", "sp", "skills", "beta", "SKILL.md");
  writeFileSync(up, readFileSync(up, "utf8").replace("Beta body.", "Beta body, rewritten upstream."));
  assert.throws(() => buildAll(root), /upstream changed under the overlay \(SKILL\.md\)/);
  assert.throws(() => buildAll(root), /--bless/);
});

test("a full-file overlay with no lock entry aborts", () => {
  const root = makeRepo();
  rmSync(join(root, "overlays", "overlays.lock.json"));
  assert.throws(() => buildAll(root), /not recorded in overlays\/overlays\.lock\.json/);
});

// An uninitialised clone has empty external/* dirs, which every tool would read as "no upstream
// components exist" — a fresh checkout would otherwise fail with a confusing unknown-source list.
test("an uninitialised submodule aborts with the init command", () => {
  const root = makeRepo();
  mkdirSync(join(root, "external", "not-checked-out"), { recursive: true });
  assert.throws(() => buildAll(root), /submodule update/);
});

test("non-empty hooks.include throws not-implemented and preserves existing output", () => {
  const root = makeRepo();
  buildAll(root);
  assert.ok(existsSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md")));
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: d\n  version: 0.1.0\nitems: []\nhooks:\n  include: [x]\n",
  );
  assert.throws(() => buildAll(root), /not implemented/);
  // guard fires before rmSync — previous build output must survive
  assert.ok(existsSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md")));
});

// ADR-0008: a user-pointer `/ns:name` must localize exactly like a model-edge — `/deniz-process:x`
// in the Claude tree, `/x` in the OpenCode one — because each is the form its harness lets a user type.
test("pointer spellings rewrite in both trees", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\nWhen unsure, suggest /superpowers:beta to the user.\n",
  );
  // The default manifest converts beta to a command AND an agent, so its output name is not `beta`.
  // Both items are curated as plain skills here, which is what a pointer to `beta` has to resolve to.
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
    ].join("\n")}\n`,
  );
  buildAll(root);
  const claude = readFileSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"), "utf8");
  const oc = readFileSync(join(root, "opencode", "skills", "alpha", "SKILL.md"), "utf8");
  assert.match(claude, /\/deniz-process:beta/);
  assert.match(oc, /suggest \/beta to the user/);
});

// One address computation: an upstream agent file carries a double extension, and references spell
// the bare name — so stripping only `.md` keyed the map on `ns:zeta.agent`, which nothing references.
test("an upstream agent named zeta.agent.md is addressed as ns:zeta", () => {
  const root = makeRepo();
  mkdirSync(join(root, "external", "sp", "agents"), { recursive: true });
  writeFileSync(join(root, "external", "sp", "agents", "zeta.agent.md"), "---\nname: zeta\ndescription: Z\n---\nZ.\n");
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\nDispatch superpowers:zeta for this.\n",
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
      "  - source: sp/agents/zeta.agent.md",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const alpha = readFileSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"), "utf8");
  assert.match(alpha, /deniz-process:zeta/);
});
