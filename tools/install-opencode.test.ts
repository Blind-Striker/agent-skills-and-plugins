import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { createModuleManifest, digestFileMap, hashBytes } from "./lib/opencode-bundle.ts";
import { acquireInstallerLock, applyPlan, inspectRecovery } from "./lib/opencode-install-apply.ts";
import { planReconcile, type Plan } from "./lib/opencode-install-plan.ts";
import { loadInstallState, observePath, type InstallState, type ObservedPath } from "./lib/opencode-install-state.ts";
import { parseInstallArgs, renderPlan, runInstallCli, type InstallCliIo } from "./install-opencode.ts";

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

test("findings are rendered and block Apply", () => {
  const plan: Plan = {
    request: { kind: "remove", modules: ["deniz-process"], all: false, platform: "posix" },
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

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
