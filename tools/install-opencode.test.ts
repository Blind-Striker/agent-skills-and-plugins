import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  cpSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, relative } from "node:path";
import { test } from "node:test";
import { fileURLToPath, pathToFileURL } from "node:url";
import { gunzipSync } from "node:zlib";
import { createModuleManifest, digestFileMap, hashBytes, loadModuleBundles } from "./lib/opencode-bundle.ts";
import { acquireInstallerLock, applyPlan, inspectRecovery } from "./lib/opencode-install-apply.ts";
import { planReconcile, type Plan } from "./lib/opencode-install-plan.ts";
import { loadInstallState, observePath, type InstallState, type ObservedPath } from "./lib/opencode-install-state.ts";
import {
  isDirectEntryPoint,
  parseInstallArgs,
  renderPlan,
  runInstallCli,
  type InstallCliIo,
} from "./install-opencode.ts";

function stateWithOwnedCommand(
  module: string,
  path: string,
  bytes: string,
  version = "0.1.0",
  mode: "100644" | "100755" = "100644",
): InstallState {
  const identity = { sha256: hashBytes(bytes), mode };
  return {
    schemaVersion: 1,
    modules: { [module]: { version, digest: digestFileMap({ [path]: identity }) } },
    files: { [path]: { module, sha256: identity.sha256, mode: identity.mode } },
  };
}

function observeOwnedPaths(
  destination: string,
  state: InstallState,
  files: Record<string, unknown>,
): Record<string, ObservedPath> {
  const observed = Object.create(null) as Record<string, ObservedPath>;
  for (const path of new Set([...Object.keys(state.files), ...Object.keys(files)])) {
    observed[path] = observePath(destination, path);
  }
  return observed;
}

function writeBundle(packageRoot: string, module: string, files: Record<string, string>, version = "0.2.0"): void {
  const root = join(packageRoot, "opencode", module);
  for (const [path, bytes] of Object.entries(files)) {
    const abs = join(root, ...path.split("/"));
    mkdirSync(join(abs, ".."), { recursive: true });
    writeFileSync(abs, bytes);
  }
  const manifest = createModuleManifest(root, module, version, () => "100644");
  writeFileSync(join(root, "manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`);
}

function makeCliFixture(extraModules: Record<string, Record<string, string>> = {}): {
  destination: string;
  io: InstallCliIo;
} {
  const packageRoot = mkdtempSync(join(tmpdir(), "cli-pkg-"));
  const isolatedHome = mkdtempSync(join(tmpdir(), "cli-home-"));
  const xdg = join(isolatedHome, ".config");
  writeBundle(packageRoot, "deniz-process", { "skills/alpha/SKILL.md": "alpha skill\n" });
  for (const [name, files] of Object.entries(extraModules)) {
    writeBundle(packageRoot, name, files);
  }
  const isolatedEnv = { ...process.env };
  delete isolatedEnv.OPENCODE_CONFIG_DIR;
  isolatedEnv.HOME = isolatedHome;
  isolatedEnv.USERPROFILE = isolatedHome;
  isolatedEnv.XDG_CONFIG_HOME = xdg;
  return {
    destination: join(xdg, "opencode"),
    io: {
      packageRoot,
      env: isolatedEnv,
      home: isolatedHome,
      platform: "posix",
    } satisfies InstallCliIo,
  };
}

test("parseInstallArgs accepts repeated --module", () => {
  assert.deepEqual(parseInstallArgs(["install", "--module", "deniz-process", "--module", "deniz-dotnet-general"]), {
    action: "install",
    modules: ["deniz-process", "deniz-dotnet-general"],
    all: false,
    yes: false,
  });
});

test("parseInstallArgs rejects --all with --module", () => {
  assert.throws(() => parseInstallArgs(["install", "--all", "--module", "deniz-process"]), /--all/);
});

test("parseInstallArgs rejects unknown flags", () => {
  assert.throws(() => parseInstallArgs(["install", "--force"]), /unknown flag/);
  assert.throws(() => parseInstallArgs(["install", "--reset"]), /unknown flag/);
  assert.throws(() => parseInstallArgs(["status", "--config-dir", "/tmp"]), /unknown flag/);
});

test("parseInstallArgs rejects --module on update", () => {
  assert.throws(() => parseInstallArgs(["update", "--module", "deniz-process"]), /update/);
});

test("parseInstallArgs rejects status --yes", () => {
  assert.throws(() => parseInstallArgs(["status", "--yes"]), /status/);
});

test("mutations print a plan and require --yes", async () => {
  const fixture = makeCliFixture();
  const preview = await runInstallCli(["install", "--module", "deniz-process"], fixture.io);
  assert.equal(preview.exitCode, 0);
  assert.match(preview.stdout, /Plan: install/);
  assert.equal(existsSync(fixture.destination), false);

  const applied = await runInstallCli(["install", "--module", "deniz-process", "--yes"], fixture.io);
  assert.equal(applied.exitCode, 0);
  assert.ok(existsSync(join(fixture.destination, "skills", "alpha", "SKILL.md")));
  assert.doesNotMatch(`${applied.stdout}${applied.stderr}`, new RegExp(escapeRegExp(fixture.io.packageRoot)));
});

test("repeated --yes install is idempotent and omits a no-op Selection section", async () => {
  const fixture = makeCliFixture();
  const first = await runInstallCli(["install", "--module", "deniz-process", "--yes"], fixture.io);
  assert.equal(first.exitCode, 0);
  const statePath = join(fixture.destination, ".deniz-skills", "install.json");
  const before = readFileSync(statePath);

  const repeated = await runInstallCli(["install", "--module", "deniz-process", "--yes"], fixture.io);

  assert.equal(repeated.exitCode, 0);
  assert.match(repeated.stdout, /Plan: install/);
  assert.doesNotMatch(repeated.stdout, /Selection:/);
  assert.match(repeated.stdout, /No changes\./);
  assert.equal(readFileSync(statePath).equals(before), true);
});

test("repeated --module installs each requested Module", async () => {
  const fixture = makeCliFixture({
    "deniz-dotnet-general": { "skills/other/SKILL.md": "other skill\n" },
  });
  const preview = await runInstallCli(
    ["install", "--module", "deniz-process", "--module", "deniz-dotnet-general"],
    fixture.io,
  );
  assert.equal(preview.exitCode, 0);
  assert.match(preview.stdout, /deniz-process/);
  assert.match(preview.stdout, /deniz-dotnet-general/);
  assert.equal(existsSync(fixture.destination), false);

  const applied = await runInstallCli(
    ["install", "--module", "deniz-process", "--module", "deniz-dotnet-general", "--yes"],
    fixture.io,
  );
  assert.equal(applied.exitCode, 0);
  assert.ok(existsSync(join(fixture.destination, "skills", "alpha", "SKILL.md")));
  assert.ok(existsSync(join(fixture.destination, "skills", "other", "SKILL.md")));
});

test("update reconciles the whole Selection successfully", async () => {
  const fixture = makeCliFixture();
  const installed = await runInstallCli(["install", "--module", "deniz-process", "--yes"], fixture.io);
  assert.equal(installed.exitCode, 0);
  const skill = join(fixture.destination, "skills", "alpha", "SKILL.md");
  writeBundle(fixture.io.packageRoot, "deniz-process", { "skills/alpha/SKILL.md": "updated skill\n" }, "0.3.0");

  const preview = await runInstallCli(["update"], fixture.io);

  assert.equal(preview.exitCode, 0);
  assert.match(preview.stdout, /Plan: update/);
  assert.match(preview.stdout, /Replace:/);
  assert.doesNotMatch(preview.stdout, /Selection:/);
  assert.equal(readFileSync(skill, "utf8"), "alpha skill\n");

  const applied = await runInstallCli(["update", "--yes"], fixture.io);
  assert.equal(applied.exitCode, 0);
  assert.equal(readFileSync(skill, "utf8"), "updated skill\n");
  assert.equal(loadInstallState(fixture.destination).modules["deniz-process"]?.version, "0.3.0");
});

test("--all installs and removes every Module", async () => {
  const fixture = makeCliFixture({
    "deniz-dotnet-general": { "skills/other/SKILL.md": "other skill\n" },
  });

  const installed = await runInstallCli(["install", "--all", "--yes"], fixture.io);

  assert.equal(installed.exitCode, 0);
  assert.deepEqual(Object.keys(loadInstallState(fixture.destination).modules).sort(), [
    "deniz-dotnet-general",
    "deniz-process",
  ]);
  assert.ok(existsSync(join(fixture.destination, "skills", "alpha", "SKILL.md")));
  assert.ok(existsSync(join(fixture.destination, "skills", "other", "SKILL.md")));

  const preview = await runInstallCli(["remove", "--all"], fixture.io);
  assert.equal(preview.exitCode, 0);
  assert.match(preview.stdout, / {2}- deniz-dotnet-general/);
  assert.match(preview.stdout, / {2}- deniz-process/);
  assert.ok(existsSync(join(fixture.destination, "skills", "alpha", "SKILL.md")));

  const removed = await runInstallCli(["remove", "--all", "--yes"], fixture.io);
  assert.equal(removed.exitCode, 0);
  assert.deepEqual(Object.keys(loadInstallState(fixture.destination).modules), []);
  assert.equal(existsSync(join(fixture.destination, "skills", "alpha", "SKILL.md")), false);
  assert.equal(existsSync(join(fixture.destination, "skills", "other", "SKILL.md")), false);
});

test("--all exclusivity is a usage error and does not create the Destination", async () => {
  const fixture = makeCliFixture();
  const result = await runInstallCli(["install", "--all", "--module", "deniz-process"], fixture.io);
  assert.notEqual(result.exitCode, 0);
  assert.match(result.stderr, /--all/);
  assert.equal(existsSync(fixture.destination), false);
});

test("unknown flags exit nonzero without mutation", async () => {
  const fixture = makeCliFixture();
  const result = await runInstallCli(["install", "--force"], fixture.io);
  assert.notEqual(result.exitCode, 0);
  assert.match(result.stderr, /unknown flag/);
  assert.equal(existsSync(fixture.destination), false);
});

test("update with no Selection is a finding and stays read-only", async () => {
  const fixture = makeCliFixture();
  const result = await runInstallCli(["update"], fixture.io);
  assert.notEqual(result.exitCode, 0);
  assert.match(`${result.stdout}${result.stderr}`, /Selection|unknown_module/);
  assert.equal(existsSync(fixture.destination), false);
});

test("update --yes with no Selection leaves Destination absent", async () => {
  const fixture = makeCliFixture();
  const result = await runInstallCli(["update", "--yes"], fixture.io);
  assert.notEqual(result.exitCode, 0);
  assert.match(`${result.stdout}${result.stderr}`, /Selection|unknown_module/);
  assert.equal(existsSync(fixture.destination), false);
});

test("status --yes is a usage error and does not create the Destination", async () => {
  const fixture = makeCliFixture();
  const result = await runInstallCli(["status", "--yes"], fixture.io);
  assert.notEqual(result.exitCode, 0);
  assert.match(result.stderr, /status/);
  assert.equal(existsSync(fixture.destination), false);
});

test("remove drops a missing owned path", async () => {
  const fixture = makeCliFixture();
  const installed = await runInstallCli(["install", "--module", "deniz-process", "--yes"], fixture.io);
  assert.equal(installed.exitCode, 0);
  rmSync(join(fixture.destination, "skills", "alpha", "SKILL.md"));

  const preview = await runInstallCli(["remove", "--module", "deniz-process"], fixture.io);
  assert.equal(preview.exitCode, 0);
  assert.match(preview.stdout, /Plan: remove/);
  assert.match(preview.stdout, /drop-missing-claim|Drop missing/);

  const applied = await runInstallCli(["remove", "--module", "deniz-process", "--yes"], fixture.io);
  assert.equal(applied.exitCode, 0);
  const state = loadInstallState(fixture.destination);
  assert.equal(state.modules["deniz-process"], undefined);
  assert.equal(state.files["skills/alpha/SKILL.md"], undefined);
});

test("remove refuses a Local modification", async () => {
  const fixture = makeCliFixture();
  const installed = await runInstallCli(["install", "--module", "deniz-process", "--yes"], fixture.io);
  assert.equal(installed.exitCode, 0);
  writeFileSync(join(fixture.destination, "skills", "alpha", "SKILL.md"), "edited locally\n");

  const result = await runInstallCli(["remove", "--module", "deniz-process", "--yes"], fixture.io);
  assert.notEqual(result.exitCode, 0);
  assert.match(`${result.stdout}${result.stderr}`, /local_modification/);
  assert.equal(readFileSync(join(fixture.destination, "skills", "alpha", "SKILL.md"), "utf8"), "edited locally\n");
  assert.ok(loadInstallState(fixture.destination).modules["deniz-process"]);
});

test("remove succeeds when an unrelated selected Module has State drift", async () => {
  const fixture = makeCliFixture({
    "deniz-dotnet-general": { "skills/other/SKILL.md": "other skill\n" },
  });
  const installed = await runInstallCli(["install", "--all", "--yes"], fixture.io);
  assert.equal(installed.exitCode, 0);
  const unrelated = join(fixture.destination, "skills", "other", "SKILL.md");
  rmSync(unrelated);

  const removed = await runInstallCli(["remove", "--module", "deniz-process", "--yes"], fixture.io);

  assert.equal(removed.exitCode, 0, `${removed.stdout}${removed.stderr}`);
  const state = loadInstallState(fixture.destination);
  assert.equal(state.modules["deniz-process"], undefined);
  assert.ok(state.modules["deniz-dotnet-general"]);
  assert.ok(state.files["skills/other/SKILL.md"]);
  assert.equal(existsSync(unrelated), false);
  assert.equal(existsSync(join(fixture.destination, "skills", "alpha", "SKILL.md")), false);
});

test("status reports drift without mutation", async () => {
  const fixture = makeCliFixture();
  const installed = await runInstallCli(["install", "--module", "deniz-process", "--yes"], fixture.io);
  assert.equal(installed.exitCode, 0);
  writeFileSync(join(fixture.destination, "skills", "alpha", "SKILL.md"), "drifted\n");

  const result = await runInstallCli(["status"], fixture.io);
  assert.notEqual(result.exitCode, 0);
  assert.match(result.stdout, /Status/);
  assert.match(result.stdout, /local_modification/);
  assert.match(result.stdout, /Lock:/);
  assert.match(result.stdout, /Recovery:/);
  assert.equal(readFileSync(join(fixture.destination, "skills", "alpha", "SKILL.md"), "utf8"), "drifted\n");
});

test("OPENCODE_CONFIG_DIR is refused", async () => {
  const fixture = makeCliFixture();
  const result = await runInstallCli(["status"], {
    ...fixture.io,
    env: { ...fixture.io.env, OPENCODE_CONFIG_DIR: join(tmpdir(), "other-opencode") },
  });
  assert.notEqual(result.exitCode, 0);
  assert.match(result.stderr, /OPENCODE_CONFIG_DIR/);
  assert.equal(existsSync(fixture.destination), false);
});

test("Recovery Apply performs Recovery only and exits", async () => {
  const fixture = makeCliFixture();
  const installed = await runInstallCli(["install", "--module", "deniz-process", "--yes"], fixture.io);
  assert.equal(installed.exitCode, 0);
  const original = readFileSync(join(fixture.destination, "skills", "alpha", "SKILL.md"), "utf8");
  writeBundle(fixture.io.packageRoot, "deniz-process", { "skills/alpha/SKILL.md": "updated skill\n" }, "0.3.0");
  const bundles = (await import("./lib/opencode-bundle.ts")).loadModuleBundles(
    join(fixture.io.packageRoot, "opencode"),
  );
  const bundle = bundles.get("deniz-process");
  assert.ok(bundle);
  const current = loadInstallState(fixture.destination);
  const plan = planReconcile(
    current,
    { "deniz-process": bundle.manifest },
    observeOwnedPaths(fixture.destination, current, bundle.manifest.files),
    { kind: "update", modules: [], all: false, platform: "posix" },
  );
  assert.equal(plan.findings.length, 0);
  const lock = acquireInstallerLock(fixture.destination);
  try {
    assert.throws(
      () => applyPlan(lock, fixture.destination, plan, bundles, { crashAfter: "after-place" }),
      /injected crash after-place/,
    );
  } finally {
    lock.release();
  }
  const leftover = inspectRecovery(fixture.destination);
  assert.equal(leftover?.kind, "rollback");

  const recovered = await runInstallCli(["install", "--module", "deniz-process", "--yes"], fixture.io);
  assert.equal(recovered.exitCode, 0);
  assert.match(recovered.stdout, /Recovery/);
  assert.equal(readFileSync(join(fixture.destination, "skills", "alpha", "SKILL.md"), "utf8"), original);
  assert.equal(inspectRecovery(fixture.destination), null);
  assert.equal(
    loadInstallState(fixture.destination).modules["deniz-process"]?.digest,
    current.modules["deniz-process"]?.digest,
  );
});

test("plan-only update does not acquire the mutation lock", async () => {
  const fixture = makeCliFixture();
  const installed = await runInstallCli(["install", "--module", "deniz-process", "--yes"], fixture.io);
  assert.equal(installed.exitCode, 0);
  const lock = acquireInstallerLock(fixture.destination);
  try {
    const preview = await runInstallCli(["update"], fixture.io);
    assert.equal(preview.exitCode, 0);
    assert.match(preview.stdout, /Plan: update/);
    const blocked = await runInstallCli(["update", "--yes"], fixture.io);
    assert.notEqual(blocked.exitCode, 0);
    assert.match(blocked.stderr, /lock/i);
  } finally {
    lock.release();
  }
});

test("status reports Selection, lock, and Recovery without creating a Destination", async () => {
  const fixture = makeCliFixture();
  const result = await runInstallCli(["status"], fixture.io);
  assert.equal(result.exitCode, 0);
  assert.match(result.stdout, /Status/);
  assert.match(result.stdout, /Lock: none/);
  assert.match(result.stdout, /Recovery: none/);
  assert.equal(existsSync(fixture.destination), false);
});

test("renderPlan prints stable sections and omits package cache paths", () => {
  const identity = { sha256: hashBytes("new\n"), mode: "100644" as const };
  const plan: Plan = {
    request: { kind: "install", modules: ["deniz-process"], all: false, platform: "posix" },
    selectionChanges: { added: ["deniz-process"], removed: [] },
    operations: [
      {
        kind: "add",
        path: "skills/alpha/SKILL.md",
        module: "deniz-process",
        source: "skills/alpha/SKILL.md",
        identity,
      },
      { kind: "replace", path: "commands/alpha.md", module: "deniz-process", source: "commands/alpha.md", identity },
      { kind: "chmod", path: "skills/alpha/run.sh", module: "deniz-process", from: "100644", to: "100755" },
      { kind: "remove", path: "commands/old.md", module: "deniz-process", identity },
      { kind: "drop-missing-claim", path: "commands/gone.md", module: "deniz-process" },
    ],
    transfers: [{ path: "commands/shared.md", fromModule: "deniz-process", toModule: "deniz-dotnet-general" }],
    nextState: stateWithOwnedCommand("deniz-process", "skills/alpha/SKILL.md", "new\n", "0.2.0"),
    findings: [],
  };
  const rendered = renderPlan(plan, join("/tmp", "opencode"));
  assert.match(rendered, /Plan: install/);
  assert.match(rendered, /Selection:/);
  assert.match(rendered, /Add:/);
  assert.match(rendered, /Replace:/);
  assert.match(rendered, /Mode:/);
  assert.match(rendered, /Remove:/);
  assert.match(rendered, /Drop missing claims:/);
  assert.match(rendered, /Ownership transfers:/);
  assert.doesNotMatch(rendered, /[A-Za-z]:\\/);
  assert.doesNotMatch(rendered, /source-package|node_modules/);
});

test("malformed Bundle error omits the package cache path", async () => {
  const fixture = makeCliFixture();
  writeFileSync(join(fixture.io.packageRoot, "opencode", "deniz-process", "manifest.json"), "{");
  const result = await runInstallCli(["install", "--module", "deniz-process"], fixture.io);
  assert.notEqual(result.exitCode, 0);
  const output = `${result.stdout}${result.stderr}`;
  assert.doesNotMatch(output, new RegExp(escapeRegExp(fixture.io.packageRoot)));
  assert.match(output, /opencode\/deniz-process\/manifest\.json/);
  assert.match(output, /invalid Module manifest/);
});

test("missing Bundle tree error omits the package cache path", async () => {
  const fixture = makeCliFixture();
  rmSync(join(fixture.io.packageRoot, "opencode"), { recursive: true, force: true });
  const result = await runInstallCli(["status"], fixture.io);
  assert.notEqual(result.exitCode, 0);
  const output = `${result.stdout}${result.stderr}`;
  assert.doesNotMatch(output, new RegExp(escapeRegExp(fixture.io.packageRoot)));
  assert.match(output, /opencode/);
});

test("blocked Recovery on --yes is decided under the lock and does not Apply", async () => {
  const fixture = makeCliFixture();
  const installed = await runInstallCli(["install", "--module", "deniz-process", "--yes"], fixture.io);
  assert.equal(installed.exitCode, 0);
  const skill = join(fixture.destination, "skills", "alpha", "SKILL.md");
  const original = readFileSync(skill, "utf8");
  writeFileSync(join(fixture.destination, ".deniz-skills", "debris"), "unresolved\n");
  const lockDir = join(fixture.destination, ".deniz-skills", "lock");
  mkdirSync(lockDir);
  writeFileSync(
    join(lockDir, "owner.json"),
    `${JSON.stringify({ pid: deadPid(), startedAt: new Date().toISOString(), token: "dead-lock" })}\n`,
  );

  const result = await runInstallCli(["install", "--module", "deniz-process", "--yes"], fixture.io);
  assert.notEqual(result.exitCode, 0);
  assert.match(result.stdout, /Recovery: blocked/);
  assert.equal(readFileSync(skill, "utf8"), original);
  assert.ok(existsSync(join(fixture.destination, ".deniz-skills", "debris")));
  assert.equal(existsSync(lockDir), false);
});

test("findings are rendered and block Apply", () => {
  const plan: Plan = {
    request: { kind: "remove", modules: ["deniz-process"], all: false, platform: "posix" },
    selectionChanges: { added: [], removed: ["deniz-process"] },
    operations: [],
    transfers: [],
    nextState: stateWithOwnedCommand("deniz-process", "skills/alpha/SKILL.md", "alpha skill\n"),
    findings: [
      {
        code: "local_modification",
        module: "deniz-process",
        path: "skills/alpha/SKILL.md",
        message: "skills/alpha/SKILL.md was modified locally; restore, move, or delete it by hand, then retry",
      },
    ],
  };
  const rendered = renderPlan(plan, join("/tmp", "opencode"));
  assert.match(rendered, /Findings:/);
  assert.match(rendered, /local_modification/);
});

function deadPid(): number {
  const candidate = 2_147_483_647;
  try {
    process.kill(candidate, 0);
  } catch {
    return candidate;
  }
  throw new Error("could not find a dead pid for abandoned-lock tests");
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

// --- direct-entry detection (bin symlinks) ----------------------------------

test("direct-entry detection resolves a bin symlink to the real module", (t) => {
  const dir = mkdtempSync(join(tmpdir(), "entry-detect-"));
  const real = join(dir, "real.js");
  writeFileSync(real, "");
  let link: string;
  try {
    link = join(dir, "bin-link");
    symlinkSync(real, link, "file");
  } catch (error) {
    t.skip(
      `creating file symlinks is not permitted on this host: ${error instanceof Error ? error.message : String(error)}`,
    );
    return;
  }
  assert.equal(isDirectEntryPoint(link, pathToFileURL(real).href), true);
  assert.equal(isDirectEntryPoint(real, pathToFileURL(real).href), true);
  assert.equal(isDirectEntryPoint(join(dir, "other.js"), pathToFileURL(real).href), false);
  assert.equal(isDirectEntryPoint(undefined, pathToFileURL(real).href), false);
  assert.equal(isDirectEntryPoint(join(dir, "missing.js"), pathToFileURL(real).href), false);
});

test("a bin symlink process runs the real installer module", (t) => {
  const root = repoRoot();
  const fixture = mkdtempSync(join(tmpdir(), "bin-symlink-"));
  cpSync(join(root, "dist"), join(fixture, "dist"), { recursive: true });
  cpSync(join(root, "opencode", "deniz-process"), join(fixture, "opencode", "deniz-process"), { recursive: true });
  let link: string;
  try {
    link = join(fixture, "deniz-skills-bin");
    symlinkSync(join(fixture, "dist", "install-opencode.js"), link, "file");
  } catch (error) {
    t.skip(
      `creating file symlinks is not permitted on this host: ${error instanceof Error ? error.message : String(error)}`,
    );
    return;
  }
  const isolatedHome = mkdtempSync(join(tmpdir(), "bin-symlink-home-"));
  const env = { ...process.env };
  delete env.OPENCODE_CONFIG_DIR;
  env.HOME = isolatedHome;
  env.USERPROFILE = isolatedHome;
  env.XDG_CONFIG_HOME = join(isolatedHome, ".config");
  const result = spawnSync(process.execPath, [link, "status"], { encoding: "utf8", env });
  assert.equal(result.status, 0, `${result.stdout ?? ""}\n${result.stderr ?? ""}`);
  assert.match(result.stdout ?? "", /Status/);
});

// --- packed-package tests --------------------------------------------------

const REQUIRED_MODULES = ["deniz-dotnet-akka", "deniz-dotnet-aspire", "deniz-dotnet-general", "deniz-process"];

// The tarball carries only the committed emitted installer and the generated Module Bundles.
// The TS sources stay out: consumers never compile.
const DIST_FILES = [
  "dist/install-opencode.js",
  "dist/lib/opencode-bundle.js",
  "dist/lib/opencode-install-state.js",
  "dist/lib/opencode-install-plan.js",
  "dist/lib/opencode-install-apply.js",
];

function repoRoot(): string {
  return join(dirname(fileURLToPath(import.meta.url)), "..");
}

function npmInvocation(args: string[]): { exe: string; args: string[] } {
  if (process.platform === "win32") {
    // Spawning npm.cmd without a shell is EINVAL on this Node; drive the npm CLI
    // directly so paths with spaces never pass through cmd quoting.
    const cli = join(dirname(process.execPath), "node_modules", "npm", "bin", "npm-cli.js");
    if (!existsSync(cli)) {
      throw new Error("npm-cli.js not found next to node; cannot invoke npm without a shell");
    }
    return { exe: process.execPath, args: [cli, ...args] };
  }
  return { exe: "npm", args };
}

const TAR_BLOCK = 512;

function tarString(field: Buffer): string {
  const end = field.indexOf(0);
  return field.subarray(0, end === -1 ? field.length : end).toString("utf8");
}

function readTarEntries(tgz: string): { path: string; content: Buffer }[] {
  const raw = gunzipSync(readFileSync(tgz));
  const entries: { path: string; content: Buffer }[] = [];
  let offset = 0;
  let pendingName: string | null = null;
  while (offset + TAR_BLOCK <= raw.length) {
    const header = raw.subarray(offset, offset + TAR_BLOCK);
    offset += TAR_BLOCK;
    if (header.every((byte) => byte === 0)) {
      break;
    }
    const name = tarString(header.subarray(0, 100));
    const prefix = tarString(header.subarray(345, 500));
    const size = Number.parseInt(tarString(header.subarray(124, 136)) || "0", 8);
    const typeflag = String.fromCharCode(header[156] ?? 0);
    const content = raw.subarray(offset, offset + size);
    offset += Math.ceil(size / TAR_BLOCK) * TAR_BLOCK;
    if (typeflag === "L") {
      pendingName = tarString(content);
      continue;
    }
    if (typeflag === "K" || typeflag === "x" || typeflag === "g") {
      continue;
    }
    const full = pendingName ?? (prefix.length > 0 ? `${prefix}/${name}` : name);
    pendingName = null;
    if (typeflag === "5" || full.endsWith("/")) {
      continue;
    }
    entries.push({ path: full, content: Buffer.from(content) });
  }
  return entries;
}

function packRootPackage(root: string): { tgz: string; paths: Map<string, Buffer> } {
  const packDir = mkdtempSync(join(tmpdir(), "deniz-pack-"));
  const { exe, args } = npmInvocation(["pack", "--json", "--pack-destination", packDir]);
  const result = spawnSync(exe, args, {
    cwd: root,
    encoding: "utf8",
    env: { ...process.env, npm_config_update_notifier: "false" },
  });
  assert.equal(result.status, 0, `npm pack failed:\n${result.stdout ?? ""}\n${result.stderr ?? ""}`);
  const parsed = JSON.parse(result.stdout ?? "[]") as { filename: string }[];
  assert.equal(parsed.length, 1, "npm pack --json must report exactly one tarball");
  const tgz = join(packDir, parsed[0]?.filename ?? "");
  const paths = new Map<string, Buffer>();
  for (const entry of readTarEntries(tgz)) {
    assert.ok(entry.path.startsWith("package/"), `unexpected tar entry ${entry.path}`);
    paths.set(entry.path.slice("package/".length), entry.content);
  }
  return { tgz, paths };
}

test("packed payload is exactly the emitted installer and the Module Bundles", () => {
  const root = repoRoot();
  const opencodeRoot = join(root, "opencode");
  assert.ok(
    existsSync(join(opencodeRoot, "deniz-process", "manifest.json")),
    "generated opencode/ Module Bundles are missing; run npm run build before the pack tests",
  );
  assert.ok(
    existsSync(join(root, "dist", "install-opencode.js")),
    "committed dist/ installer is missing; run npm run build before the pack tests",
  );

  const { paths } = packRootPackage(root);

  // Exact equality: package metadata, the committed dist/ files, each Module manifest, and
  // exactly the manifest-listed Bundle files. Any extra file under opencode/ fails, and any
  // authoring source (tools/*.ts, external/, plugins/, overlays/, experiments/, docs/) fails.
  const expected = new Set<string>(["package.json", "README.md"]);
  for (const file of DIST_FILES) {
    expected.add(file);
    assert.ok(paths.has(file), `tarball must contain ${file}`);
  }
  const bundles = loadModuleBundles(opencodeRoot);
  for (const required of REQUIRED_MODULES) {
    assert.ok(bundles.has(required), `opencode/ must contain a ${required} Module Bundle`);
  }
  for (const [name, bundle] of bundles) {
    expected.add(`opencode/${name}/manifest.json`);
    for (const path of Object.keys(bundle.manifest.files)) {
      expected.add(`opencode/${name}/${path}`);
    }
  }
  assert.deepEqual([...paths.keys()].sort(), [...expected].sort(), "tarball file set must match exactly");

  for (const file of DIST_FILES) {
    assert.equal(
      paths.get(file)?.equals(readFileSync(join(root, file))),
      true,
      `${file} bytes must match the committed emit`,
    );
  }
  for (const [name, bundle] of bundles) {
    const manifestPath = `opencode/${name}/manifest.json`;
    assert.equal(
      paths.get(manifestPath)?.equals(readFileSync(join(opencodeRoot, name, "manifest.json"))),
      true,
      `${manifestPath} bytes must match the generated manifest`,
    );
    for (const path of Object.keys(bundle.manifest.files)) {
      const packedPath = `opencode/${name}/${path}`;
      assert.equal(
        hashBytes(paths.get(packedPath) ?? Buffer.alloc(0)),
        bundle.manifest.files[path]?.sha256,
        `${packedPath} bytes must match its recorded sha256`,
      );
    }
  }
});

function snapshotTree(root: string): Map<string, Buffer> {
  const snapshot = new Map<string, Buffer>();
  const visit = (dir: string): void => {
    for (const entry of readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
      const path = join(dir, entry.name);
      if (entry.isDirectory()) {
        visit(path);
      } else if (entry.isFile()) {
        snapshot.set(relative(root, path).replaceAll("\\", "/"), readFileSync(path));
      }
    }
  };
  visit(root);
  return snapshot;
}

test("packed bin installs byte-identical Native tree and Install state", async () => {
  const root = repoRoot();
  const { tgz } = packRootPackage(root);
  const packedHome = mkdtempSync(join(tmpdir(), "packed-bin-home-"));
  const checkoutHome = mkdtempSync(join(tmpdir(), "checkout-bin-home-"));
  const npmCache = mkdtempSync(join(tmpdir(), "packed-bin-cache-"));
  const runDir = mkdtempSync(join(tmpdir(), "packed-bin-run-"));
  const packedXdg = join(packedHome, ".config");
  const checkoutXdg = join(checkoutHome, ".config");

  const packedEnv = { ...process.env };
  delete packedEnv.OPENCODE_CONFIG_DIR;
  packedEnv.HOME = packedHome;
  packedEnv.USERPROFILE = packedHome;
  packedEnv.XDG_CONFIG_HOME = packedXdg;
  packedEnv.npm_config_cache = npmCache;
  packedEnv.npm_config_update_notifier = "false";
  packedEnv.npm_config_audit = "false";
  packedEnv.npm_config_fund = "false";

  const runPacked = (cliArgs: string[]): { status: number | null; stdout: string; stderr: string } => {
    const { exe, args } = npmInvocation([
      "exec",
      "--yes",
      "--package",
      pathToFileURL(tgz).href,
      "--",
      "deniz-skills",
      ...cliArgs,
    ]);
    const result = spawnSync(exe, args, { cwd: runDir, env: packedEnv, encoding: "utf8" });
    return { status: result.status, stdout: result.stdout ?? "", stderr: result.stderr ?? "" };
  };

  const packedInstall = runPacked(["install", "--module", "deniz-process", "--yes"]);
  assert.equal(packedInstall.status, 0, `packed install failed:\n${packedInstall.stdout}\n${packedInstall.stderr}`);
  const packedStatus = runPacked(["status"]);
  assert.equal(packedStatus.status, 0, `packed status failed:\n${packedStatus.stdout}\n${packedStatus.stderr}`);
  const packedDestination = join(packedXdg, "opencode");

  const checkoutEnv = { ...process.env };
  delete checkoutEnv.OPENCODE_CONFIG_DIR;
  checkoutEnv.HOME = checkoutHome;
  checkoutEnv.USERPROFILE = checkoutHome;
  checkoutEnv.XDG_CONFIG_HOME = checkoutXdg;
  const checkoutIo: InstallCliIo = {
    packageRoot: root,
    env: checkoutEnv,
    home: checkoutHome,
    platform: process.platform === "win32" ? "windows" : "posix",
  };
  const checkoutInstall = await runInstallCli(["install", "--module", "deniz-process", "--yes"], checkoutIo);
  assert.equal(checkoutInstall.exitCode, 0, `${checkoutInstall.stdout}${checkoutInstall.stderr}`);
  const checkoutStatus = await runInstallCli(["status"], checkoutIo);
  assert.equal(checkoutStatus.exitCode, 0, `${checkoutStatus.stdout}${checkoutStatus.stderr}`);
  const checkoutDestination = join(checkoutXdg, "opencode");

  const packedTree = snapshotTree(packedDestination);
  const checkoutTree = snapshotTree(checkoutDestination);
  assert.deepEqual([...packedTree.keys()].sort(), [...checkoutTree.keys()].sort(), "Native tree paths must match");
  for (const [path, bytes] of packedTree) {
    assert.ok(bytes.equals(checkoutTree.get(path) ?? Buffer.alloc(0)), `${path} bytes must match the repository CLI`);
  }
  const packedState = readFileSync(join(packedDestination, ".deniz-skills", "install.json"));
  const checkoutState = readFileSync(join(checkoutDestination, ".deniz-skills", "install.json"));
  assert.equal(packedState.equals(checkoutState), true, "Install state bytes must match");

  const normalize = (output: string, destination: string): string => output.replaceAll(destination, "<destination>");
  assert.equal(
    normalize(packedStatus.stdout ?? "", packedDestination),
    normalize(checkoutStatus.stdout, checkoutDestination),
    "status output must match the repository CLI",
  );
});
