import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { existsSync, lstatSync, mkdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { buildAll } from "./build.ts";
import { parseDoc, serializeDoc } from "./lib/frontmatter.ts";
import { verifyModuleManifest } from "./lib/opencode-bundle.ts";
import { stampFiles, stampMergeFiles } from "./lib/overlay.ts";
import { makeRepo, opencodeModulePath } from "./testutil.ts";

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
  // each Module is one OpenCode bundle: skills/, commands/, agents/ — all plural — plus its manifest
  const moduleRoot = opencodeModulePath(root, "deniz-process");
  assert.ok(existsSync(join(moduleRoot, "skills", "alpha", "SKILL.md")));
  assert.ok(existsSync(join(moduleRoot, "skills", "my-own", "SKILL.md")));
  const cmd = parseDoc(readFileSync(join(moduleRoot, "commands", "deniz-beta.md"), "utf8"));
  assert.equal(cmd.frontmatter.description, "Beta overlay");
  // each tree carries the reference spelling its own harness resolves: OpenCode has no plugin
  // concept, so the qualified form would resolve to nothing there
  const alpha = readFileSync(join(moduleRoot, "skills", "alpha", "SKILL.md"), "utf8");
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
  const ocAgent = parseDoc(readFileSync(join(moduleRoot, "agents", "beta-agent.md"), "utf8"));
  assert.equal(ocAgent.frontmatter.mode, "subagent");
  assert.equal(ocAgent.frontmatter.description, "Beta upstream");
  assert.equal("model" in ocAgent.frontmatter, false);
  assert.ok(report.includes("opencode agent beta-agent.md: dropped frontmatter keys: model"));

  // one deterministic manifest per Module: named after the plugin, versioned from curation, hashing
  // the final bytes of every file — itself excluded
  assert.ok(existsSync(join(moduleRoot, "manifest.json")));
  assert.ok(!existsSync(join(root, "opencode", "skills")), "no committed flat aggregate");
  const manifest = JSON.parse(readFileSync(join(moduleRoot, "manifest.json"), "utf8"));
  assert.equal(manifest.module, "deniz-process");
  assert.equal(manifest.version, "0.1.0");
  for (const path of [
    "skills/alpha/SKILL.md",
    "skills/my-own/SKILL.md",
    "skills/gamma/SKILL.md",
    "skills/delta/SKILL.md",
    "skills/delta/references/notes.md",
    "commands/deniz-beta.md",
    "agents/beta-agent.md",
  ]) {
    assert.ok(path in manifest.files, `manifest lists ${path}`);
  }
  assert.ok(!("manifest.json" in manifest.files), "the manifest does not list itself");
  assert.deepEqual(verifyModuleManifest(moduleRoot, manifest), []);
});

// A bundle file that corresponds to a committed plugins/MODULE path keeps its Git index mode, so
// an executable bit already guarded in generated output survives packaging; the resolver defaults
// to 100644 for every path with no committed counterpart.
test("module manifests inherit executable modes from the plugin tree", () => {
  const root = makeRepo();
  // own skills copy everything, so a script planted here survives the wipe-and-re-emit
  writeFileSync(join(root, "skills", "deniz-process", "my-own", "run.sh"), "#!/bin/sh\necho ok\n");
  buildAll(root);
  // index the emitted plugin tree and mark the script executable — the index, not the worktree,
  // is what the build reads modes from
  execFileSync("git", ["init", "-q", "."], { cwd: root });
  execFileSync("git", ["add", "plugins"], { cwd: root, stdio: "ignore" });
  execFileSync("git", ["update-index", "--chmod=+x", "plugins/deniz-process/skills/my-own/run.sh"], {
    cwd: root,
    stdio: "ignore",
  });

  buildAll(root);
  const moduleRoot = opencodeModulePath(root, "deniz-process");
  const manifest = JSON.parse(readFileSync(join(moduleRoot, "manifest.json"), "utf8"));
  assert.equal(manifest.files["skills/my-own/run.sh"].mode, "100755", "the plugin counterpart's mode travels");
  assert.equal(manifest.files["skills/alpha/SKILL.md"].mode, "100644");
  assert.equal(manifest.files["commands/deniz-beta.md"].mode, "100644");
  assert.deepEqual(verifyModuleManifest(moduleRoot, manifest), []);
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
  const oc = parseDoc(readFileSync(opencodeModulePath(root, "deniz-process", "commands", "alpha.md"), "utf8"));

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

  // OpenCode: the dial is which artifact exists, inside the Module bundle
  assert.ok(existsSync(opencodeModulePath(root, "deniz-process", "skills", "alpha", "SKILL.md")));
  assert.ok(!existsSync(opencodeModulePath(root, "deniz-process", "commands", "alpha.md")), "auto is model-only");

  assert.ok(existsSync(opencodeModulePath(root, "deniz-process", "commands", "beta.md")), "manual is a command");
  assert.ok(
    !existsSync(opencodeModulePath(root, "deniz-process", "skills", "beta", "SKILL.md")),
    "a manual item must not also be a model-reachable skill",
  );
  // ...but its bundled files still need a home the command body can point at, and a directory
  // with no SKILL.md is ignored by OpenCode's discovery — measured, see the research note.
  assert.ok(
    existsSync(opencodeModulePath(root, "deniz-process", "skills", "beta", "references", "notes.md")),
    "bundled files are parked where the command can reach them",
  );
  assert.ok(
    existsSync(opencodeModulePath(root, "deniz-process", "skills", "beta", "BODY.md")),
    "bundled manual beta parks its body beside the bundle",
  );

  assert.ok(existsSync(opencodeModulePath(root, "deniz-process", "skills", "delta", "SKILL.md")), "both emits a skill");
  assert.ok(existsSync(opencodeModulePath(root, "deniz-process", "commands", "delta.md")), "both emits a command too");
  assert.ok(
    !existsSync(opencodeModulePath(root, "deniz-process", "skills", "delta", "BODY.md")),
    "both does not park a body",
  );
});

test("a bundled manual command parks its body and points at the parked bundle", () => {
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
      "    invocation: manual",
    ].join("\n")}\n`,
  );
  const report = buildAll(root);

  const bodyPath = opencodeModulePath(root, "deniz-process", "skills", "beta", "BODY.md");
  assert.ok(existsSync(bodyPath), "the full body is parked beside its bundle");
  assert.ok(
    !existsSync(opencodeModulePath(root, "deniz-process", "skills", "beta", "SKILL.md")),
    "manual stays undiscoverable",
  );

  const upstream = parseDoc(readFileSync(join(root, "external", "sp", "skills", "beta", "SKILL.md"), "utf8"));
  assert.equal(readFileSync(bodyPath, "utf8"), upstream.body, "BODY.md is the complete parsed skill body");

  const command = parseDoc(readFileSync(opencodeModulePath(root, "deniz-process", "commands", "beta.md"), "utf8"));
  // the stub keeps the installed spelling: Module directories are package layout only
  const expectedStub = [
    "Read `skills/beta/BODY.md` from the active OpenCode configuration root before doing anything else.",
    "For a project-local install, use `.opencode/skills/beta/BODY.md`; for a global install, use `~/.config/opencode/skills/beta/BODY.md`.",
    "Follow that file as this command's full instructions.",
    "",
    "Arguments: $ARGUMENTS",
  ].join("\n");
  assert.equal(command.body.trim(), expectedStub);
  assert.doesNotMatch(
    command.body,
    /@(?:\.opencode|~\/\.config)/,
    "the path is prose, not a project-root-only @ reference",
  );
  assert.ok(!command.body.includes(upstream.body.trim()), "the command does not paste the full ceremony");

  const parking = report.filter((line) => line.includes("beta") && line.includes("parked"));
  assert.equal(parking.length, 1, `expected one beta parking report, got ${JSON.stringify(report)}`);
  const parkingLine = parking[0] as string;
  assert.match(parkingLine, /body parked at skills\/beta\/BODY\.md/);
  assert.match(parkingLine, /\([^)]*references\/notes\.md[^)]*\)/);
  assert.doesNotMatch(parkingLine, /^WARN/);
  assert.doesNotMatch(parkingLine, /not rewritten/);
  assert.doesNotMatch(parkingLine, /\([^)]*BODY\.md[^)]*\)/);
});

test("a bundle-less manual conversion preserves the pre-wave command and leaves no parking husk", () => {
  const root = makeRepo();
  const upstream = parseDoc(readFileSync(join(root, "external", "sp", "skills", "beta", "SKILL.md"), "utf8"));
  rmSync(join(root, "external", "sp", "skills", "beta", "references"), { recursive: true, force: true });
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
  const expectedCommand = serializeDoc({
    frontmatter: { description: upstream.frontmatter.description },
    body: upstream.body,
  });
  const report = buildAll(root);

  assert.equal(readFileSync(opencodeModulePath(root, "deniz-process", "commands", "beta.md"), "utf8"), expectedCommand);
  assert.ok(
    !existsSync(opencodeModulePath(root, "deniz-process", "skills", "beta")),
    "bundle-less manual leaves no skill directory",
  );
  assert.equal(
    report.some((line) => line.includes("beta") && line.includes("parked")),
    false,
    "no parking report is emitted",
  );
});

test("both preserves the pre-wave skill and command documents without a parked body", () => {
  const root = makeRepo();
  const upstream = parseDoc(readFileSync(join(root, "external", "sp", "skills", "delta", "SKILL.md"), "utf8"));
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/delta",
      "    invocation: both",
    ].join("\n")}\n`,
  );
  const expectedSkill = serializeDoc(upstream);
  const expectedCommand = serializeDoc({
    frontmatter: { description: upstream.frontmatter.description },
    body: upstream.body,
  });
  const report = buildAll(root);

  assert.equal(readFileSync(opencodeModulePath(root, "deniz-process", "skills", "delta", "SKILL.md"), "utf8"), expectedSkill);
  assert.equal(readFileSync(opencodeModulePath(root, "deniz-process", "commands", "delta.md"), "utf8"), expectedCommand);
  assert.ok(
    !existsSync(opencodeModulePath(root, "deniz-process", "skills", "delta", "BODY.md")),
    "both does not emit BODY.md",
  );
  assert.equal(
    report.some((line) => line.includes("delta") && line.includes("parked")),
    false,
    "no parking report is emitted",
  );
});

test("manual bundle links repoint to BODY.md at every relative depth", () => {
  const root = makeRepo();
  const betaDir = join(root, "external", "sp", "skills", "beta");
  writeFileSync(join(betaDir, "SKILL.md"), `${readFileSync(join(betaDir, "SKILL.md"), "utf8")}[body-self](SKILL.md)\n`);
  writeFileSync(join(betaDir, "README.md"), "[dot](./SKILL.md)\n");
  writeFileSync(join(betaDir, "references", "notes.md"), "[parent](../SKILL.md)\n");
  mkdirSync(join(betaDir, "references", "nested"), { recursive: true });
  writeFileSync(join(betaDir, "references", "nested", "deep.md"), "[deep](../../SKILL.md)\n");
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

  const body = readFileSync(opencodeModulePath(root, "deniz-process", "skills", "beta", "BODY.md"), "utf8");
  const readme = readFileSync(opencodeModulePath(root, "deniz-process", "skills", "beta", "README.md"), "utf8");
  const notes = readFileSync(opencodeModulePath(root, "deniz-process", "skills", "beta", "references", "notes.md"), "utf8");
  const deep = readFileSync(
    opencodeModulePath(root, "deniz-process", "skills", "beta", "references", "nested", "deep.md"),
    "utf8",
  );
  assert.match(body, /BODY\.md/);
  assert.match(readme, /\.\/BODY\.md/);
  assert.match(notes, /\.\.\/BODY\.md/);
  assert.match(deep, /\.\.\/\.\.\/BODY\.md/);
  for (const content of [body, readme, notes, deep]) {
    assert.doesNotMatch(content, /SKILL\.md/);
  }
});

// Parking is the price of `manual`, and only `manual` pays it. Reporting it for `both` — whose
// directory is a live skill with its own SKILL.md — made most of this warning class false, which
// costs the build report the thing it exists for (ADR-0002: no silent loss, hence no fake loss).
test("only a manual conversion reports parked files", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/beta", // bundles references/notes.md
      "    invocation: manual",
      "  - source: sp/skills/delta", // bundles references/notes.md too
      "    invocation: both",
    ].join("\n")}\n`,
  );
  const report = buildAll(root);
  const parked = (name: string) => report.filter((l) => l.includes("parked") && l.includes(name));
  assert.equal(parked("beta").length, 1, `manual loses its skill shape and must say so — ${JSON.stringify(report)}`);
  assert.match(parked("beta")[0] as string, /body parked at skills\/beta\/BODY\.md/);
  assert.deepEqual(parked("delta"), [], "both keeps a discoverable skill — nothing is parked");
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
  assert.ok(!existsSync(opencodeModulePath(root, "deniz-process", "skills", "delta", "references")));
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

test("a duplicate output identity within one plugin aborts the build", () => {
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

  assert.throws(() => buildAll(root), /duplicate output identity skill:shared.*sp\/skills\/beta.*sp\/skills\/delta/);
});

test("duplicate plugin.name values abort before deleting existing output", () => {
  const root = makeRepo();
  buildAll(root);
  const alpha = join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md");
  writeFileSync(
    join(root, "curation", "other.yaml"),
    "plugin:\n  name: deniz-process\n  description: Other\n  version: 0.1.0\nitems: []\n",
  );

  assert.throws(
    () => buildAll(root),
    /duplicate plugin\.name deniz-process.*curation\/deniz-process\.yaml.*curation\/other\.yaml/,
  );
  assert.ok(existsSync(alpha), "previous build output must survive a duplicate plugin.name");
});

test("the same output name in different artifact kinds builds both and keeps both ledger entries", () => {
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

  buildAll(root);
  assert.ok(existsSync(join(root, "plugins", "deniz-process", "skills", "shared", "SKILL.md")));
  assert.ok(existsSync(join(root, "plugins", "deniz-process", "commands", "shared.md")));

  const ledger = JSON.parse(readFileSync(join(root, "docs", "ledger.json"), "utf8"));
  assert.equal(ledger["deniz-process/skill/shared"].source, "sp/skills/delta");
  assert.equal(ledger["deniz-process/command/shared"].source, "sp/skills/beta");
  assert.equal("deniz-process/shared" in ledger, false);
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

  execFileSync(
    process.execPath,
    [join(import.meta.dirname, "eject.ts"), "deniz-process", "gamma", "--bless", "--yes"],
    { cwd: root, stdio: "ignore" },
  );
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

test("an upstream-backed file added to an overlay requires re-blessing", () => {
  const root = makeRepo();
  const overlay = join(root, "overlays", "deniz-process", "deniz-beta");
  mkdirSync(join(overlay, "references"), { recursive: true });
  writeFileSync(join(overlay, "references", "notes.md"), "Owned replacement note.\n");

  assert.throws(
    () => buildAll(root),
    /deniz-beta.*references\/notes\.md.*bless/s,
    "an overlay target backed by upstream must not sit outside the lock",
  );
});

test("an upstream-backed target added to a patch requires re-blessing", () => {
  const root = makeRepo();
  writeFileSync(join(root, "external", "sp", "skills", "gamma", "notes.md"), "Upstream note.\n");
  const patch = join(root, "overlays", "deniz-process", "gamma", "overlay.patch");
  writeFileSync(
    patch,
    `${readFileSync(patch, "utf8")}diff --git a/notes.md b/notes.md
--- a/notes.md
+++ b/notes.md
@@ -1 +1 @@
-Upstream note.
+Patched note.
`,
  );

  assert.throws(
    () => buildAll(root),
    /gamma.*notes\.md.*bless/s,
    "a newly stampable patch target must not sit outside the lock",
  );
});

test("a lock path removed from the overlay target set requires re-blessing", () => {
  const root = makeRepo();
  const lockPath = join(root, "overlays", "overlays.lock.json");
  const lock = JSON.parse(readFileSync(lockPath, "utf8"));
  lock["deniz-process/deniz-beta"].files = stampFiles(join(root, "external", "sp", "skills", "beta"), [
    "SKILL.md",
    "references/notes.md",
  ]);
  writeFileSync(lockPath, `${JSON.stringify(lock, null, 2)}\n`);

  assert.throws(
    () => buildAll(root),
    /deniz-beta.*references\/notes\.md.*bless/s,
    "a stale lock target must not survive after the overlay stops replacing it",
  );
});

test("an overlay-only added file stays outside the lock target set", () => {
  const root = makeRepo();
  const overlay = join(root, "overlays", "deniz-process", "deniz-beta");
  writeFileSync(join(overlay, "curator-notes.md"), "Owned file with no upstream counterpart.\n");

  buildAll(root);
  assert.ok(existsSync(join(root, "plugins", "deniz-process", "commands", "deniz-beta.md")));
});

test("a pure-add patch target stays outside the lock target set", () => {
  const root = makeRepo();
  const patch = join(root, "overlays", "deniz-process", "gamma", "overlay.patch");
  writeFileSync(
    patch,
    `${readFileSync(patch, "utf8")}diff --git a/added.md b/added.md
new file mode 100644
--- /dev/null
+++ b/added.md
@@ -0,0 +1 @@
+Added only by the patch.
`,
  );

  buildAll(root);
  assert.equal(
    readFileSync(join(root, "plugins", "deniz-process", "skills", "gamma", "added.md"), "utf8"),
    "Added only by the patch.\n",
  );
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
  const oc = readFileSync(opencodeModulePath(root, "deniz-process", "skills", "alpha", "SKILL.md"), "utf8");
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

// A merged body's ingredients are guarded exactly like its primary (ADR-0001): the source that
// moved is named, because "something upstream changed" is unactionable when a body has several.
test("a drifted merge source stops the build, naming the source that moved", () => {
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
      "    merged_from: [sp/skills/beta]",
      "  - source: sp/skills/beta",
      "    exclude: true",
    ].join("\n")}\n`,
  );
  // what a blessed merge looks like on disk: the overlay directory, plus a lock entry stamping the
  // primary and every declared source
  mkdirSync(join(root, "overlays", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: merged\n---\nMerged body.\n",
  );
  const lock = {
    "deniz-process/alpha": {
      source: "sp/skills/alpha",
      files: stampFiles(join(root, "external", "sp", "skills", "alpha"), ["SKILL.md"]),
      mergeSources: {
        "sp/skills/beta": stampMergeFiles(join(root, "external", "sp", "skills", "beta"), ["SKILL.md"]),
      },
    },
  };
  writeFileSync(join(root, "overlays", "overlays.lock.json"), `${JSON.stringify(lock, null, 2)}\n`);
  buildAll(root); // clean: primary and merge source both match their stamps
  // the ledger carries the declaration, so an item's resolved state names the bodies it was made of
  const ledger = JSON.parse(readFileSync(join(root, "docs", "ledger.json"), "utf8"));
  assert.deepEqual(ledger["deniz-process/skill/alpha"].mergedFrom, ["sp/skills/beta"]);

  writeFileSync(
    join(root, "external", "sp", "skills", "beta", "SKILL.md"),
    "---\nname: beta\ndescription: moved\n---\nMoved.\n",
  );
  assert.throws(() => buildAll(root), /merge source changed under the overlay \(sp\/skills\/beta: SKILL\.md\)/);
});

// The filename rule stamps what the OVERLAY owns, so a merge that folded in a source's differently
// named file — matt's tests.md into our SKILL.md — was guarded by nothing, silently. A declared
// files list says where the merge actually drew from, and the guard follows the merge rather than
// the merge being cut down to fit the guard.
test("a declared files list guards a source file the overlay does not own", () => {
  const root = makeRepo();
  // the ingredient: a sibling of beta's SKILL.md, a name the overlay never carries
  writeFileSync(join(root, "external", "sp", "skills", "beta", "references", "notes.md"), "ingredient v1\n");
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
      "        files: [SKILL.md, references/notes.md]",
      "  - source: sp/skills/beta",
      "    exclude: true",
    ].join("\n")}\n`,
  );
  mkdirSync(join(root, "overlays", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: merged\n---\nMerged body, including what notes.md said.\n",
  );
  const beta = join(root, "external", "sp", "skills", "beta");
  const lockWith = (files: string[]) =>
    writeFileSync(
      join(root, "overlays", "overlays.lock.json"),
      `${JSON.stringify(
        {
          "deniz-process/alpha": {
            source: "sp/skills/alpha",
            files: stampFiles(join(root, "external", "sp", "skills", "alpha"), ["SKILL.md"]),
            mergeSources: { "sp/skills/beta": stampMergeFiles(beta, files) },
          },
        },
        null,
        2,
      )}\n`,
    );

  // What the same-filename rule alone would have produced: the overlay owns SKILL.md, so that is
  // all it can stamp. The declaration now says the merge drew from more than that, and a lock that
  // does not hold the difference is guarding nothing — the build has to say so rather than run.
  lockWith(["SKILL.md"]);
  assert.throws(
    () => buildAll(root),
    /declares references\/notes\.md, which the lock does not stamp/,
    "a files list that outgrew its stamp is an unblessed merge",
  );

  lockWith(["SKILL.md", "references/notes.md"]);
  buildAll(root); // clean once the declared list is actually blessed
  writeFileSync(join(beta, "references", "notes.md"), "ingredient v2 — upstream rewrote it\n");
  assert.throws(
    () => buildAll(root),
    /merge source changed under the overlay \(sp\/skills\/beta: references\/notes\.md\)/,
    "drift in a declared file stops the build like any other ingredient",
  );
});

// The declaration and the lock are two halves of one guard: a source nobody stamped is guarded by
// nothing, and saying so is the only way a merge cannot be half-blessed.
test("merged_from declared but not blessed stops the build", () => {
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
      "    merged_from: [sp/skills/beta]",
      "  - source: sp/skills/beta",
      "    exclude: true",
    ].join("\n")}\n`,
  );
  mkdirSync(join(root, "overlays", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: merged\n---\nMerged body.\n",
  );
  const lock = {
    "deniz-process/alpha": {
      source: "sp/skills/alpha",
      files: stampFiles(join(root, "external", "sp", "skills", "alpha"), ["SKILL.md"]),
    },
  };
  writeFileSync(join(root, "overlays", "overlays.lock.json"), `${JSON.stringify(lock, null, 2)}\n`);
  assert.throws(() => buildAll(root), /merge sources are not blessed .* --bless/);
});

// The same-filename rule is what a merge stamp guards by, so an address that shares no filename with
// the overlay stamps all-null — and a null only speaks when a file APPEARS. The bless succeeds, the
// build stays green, and nothing can ever fire: the merge twin of the primary's "lock records no
// upstream file, so nothing guards this overlay".
test("a merge source that shares no filename with the overlay stops the build", () => {
  const root = makeRepo();
  // a source directory with none of the overlay's file names — a wrong address looks exactly like this
  mkdirSync(join(root, "external", "sp", "skills", "epsilon"), { recursive: true });
  writeFileSync(join(root, "external", "sp", "skills", "epsilon", "notes.md"), "no SKILL.md here\n");
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
      "    merged_from: [sp/skills/epsilon]",
    ].join("\n")}\n`,
  );
  mkdirSync(join(root, "overlays", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: merged\n---\nMerged body.\n",
  );
  const mergeStamp = stampMergeFiles(join(root, "external", "sp", "skills", "epsilon"), ["SKILL.md"]);
  // the precondition, stated: blessing this address records absence and nothing else
  assert.deepEqual(mergeStamp, { "SKILL.md": null });
  const lock = {
    "deniz-process/alpha": {
      source: "sp/skills/alpha",
      files: stampFiles(join(root, "external", "sp", "skills", "alpha"), ["SKILL.md"]),
      mergeSources: { "sp/skills/epsilon": mergeStamp },
    },
  };
  writeFileSync(join(root, "overlays", "overlays.lock.json"), `${JSON.stringify(lock, null, 2)}\n`);
  assert.throws(() => buildAll(root), /merge source sp\/skills\/epsilon shares no filename with the overlay/);
});
