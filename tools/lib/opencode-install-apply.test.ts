import assert from "node:assert/strict";
import {
  chmodSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  renameSync,
  symlinkSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { test } from "node:test";
import { createModuleManifest, digestFileMap, hashBytes, type ModuleBundle, type ModuleManifest } from "./opencode-bundle.ts";
import {
  applyPlan,
  applyRecovery,
  acquireInstallerLock,
  inspectRecovery,
  type ApplyOptions,
  type InstallerLock,
  type RecoveryPlan,
  type TransactionJournal,
} from "./opencode-install-apply.ts";
import { planReconcile, type Plan } from "./opencode-install-plan.ts";
import {
  loadInstallState,
  observePath,
  serializeInstallState,
  stateDigest,
  type InstallState,
  type ObservedPath,
} from "./opencode-install-state.ts";

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
  manifest: ModuleManifest,
): Record<string, ObservedPath> {
  const observed = Object.create(null) as Record<string, ObservedPath>;
  for (const path of new Set([...Object.keys(state.files), ...Object.keys(manifest.files)])) {
    observed[path] = observePath(destination, path);
  }
  return observed;
}

function readState(destination: string): InstallState {
  return loadInstallState(destination);
}

function assertSameState(destination: string, expected: InstallState): void {
  assert.equal(serializeInstallState(readState(destination)), serializeInstallState(expected));
}

function tryLink(target: string, path: string, type: "file" | "dir" | "junction"): boolean {
  try {
    symlinkSync(target, path, type);
    return true;
  } catch {
    return false;
  }
}

function deadPid(): number {
  const candidate = 2_147_483_647;
  try {
    process.kill(candidate, 0);
  } catch {
    return candidate;
  }
  throw new Error("could not find a dead pid for abandoned-lock tests");
}

function posixMode(path: string): string {
  return (lstatSync(path).mode & 0o111) === 0 ? "100644" : "100755";
}

function requireFindingFree(plan: Plan): Plan {
  if (plan.findings.length > 0) {
    throw new Error(`fixture plan has findings: ${JSON.stringify(plan.findings)}`);
  }
  return plan;
}

function makeInstallFixture(): {
  destination: string;
  bundles: Map<string, ModuleBundle>;
  plan: Plan;
  target: string;
  oldBytes: string;
  oldState: InstallState;
} {
  const root = mkdtempSync(join(tmpdir(), "apply-"));
  const destination = join(root, "config", "opencode");
  const bundleRoot = join(root, "package", "opencode", "deniz-process");
  const target = join(destination, "commands", "alpha.md");
  const oldBytes = "old\n";
  mkdirSync(dirname(target), { recursive: true });
  mkdirSync(join(destination, ".deniz-skills"), { recursive: true });
  mkdirSync(join(bundleRoot, "commands"), { recursive: true });
  writeFileSync(target, oldBytes);
  writeFileSync(join(bundleRoot, "commands", "alpha.md"), "new\n");
  const oldState = stateWithOwnedCommand("deniz-process", "commands/alpha.md", oldBytes);
  writeFileSync(join(destination, ".deniz-skills", "install.json"), serializeInstallState(oldState));
  const manifest = createModuleManifest(bundleRoot, "deniz-process", "0.2.0", () => "100644");
  const observed = observeOwnedPaths(destination, oldState, manifest);
  const plan = requireFindingFree(
    planReconcile(oldState, { "deniz-process": manifest }, observed, {
      kind: "update",
      modules: [],
      all: false,
      platform: "posix",
    }),
  );
  return {
    destination,
    bundles: new Map([["deniz-process", { root: bundleRoot, manifest }]]),
    plan,
    target,
    oldBytes,
    oldState,
  };
}

function makeRemoveFixture(): {
  destination: string;
  bundles: Map<string, ModuleBundle>;
  plan: Plan;
  skillFile: string;
  extraFile: string;
  oldState: InstallState;
} {
  const root = mkdtempSync(join(tmpdir(), "apply-remove-"));
  const destination = join(root, "config", "opencode");
  const skillFile = join(destination, "skills", "alpha", "SKILL.md");
  const extraFile = join(destination, "skills", "alpha", "notes.md");
  const oldBytes = "skill\n";
  mkdirSync(dirname(skillFile), { recursive: true });
  mkdirSync(join(destination, ".deniz-skills"), { recursive: true });
  writeFileSync(skillFile, oldBytes);
  writeFileSync(extraFile, "keep me\n");
  const oldState = stateWithOwnedCommand("deniz-process", "skills/alpha/SKILL.md", oldBytes);
  writeFileSync(join(destination, ".deniz-skills", "install.json"), serializeInstallState(oldState));
  const plan = requireFindingFree(
    planReconcile(
      oldState,
      {},
      { "skills/alpha/SKILL.md": observePath(destination, "skills/alpha/SKILL.md") },
      { kind: "remove", modules: ["deniz-process"], all: false, platform: "posix" },
    ),
  );
  return { destination, bundles: new Map(), plan, skillFile, extraFile, oldState };
}

function makeAddFixture(): {
  destination: string;
  bundles: Map<string, ModuleBundle>;
  plan: Plan;
  target: string;
} {
  const root = mkdtempSync(join(tmpdir(), "apply-add-"));
  const destination = join(root, "config", "opencode");
  const bundleRoot = join(root, "package", "opencode", "deniz-process");
  const target = join(destination, "commands", "alpha.md");
  mkdirSync(join(bundleRoot, "commands"), { recursive: true });
  writeFileSync(join(bundleRoot, "commands", "alpha.md"), "new\n");
  const manifest = createModuleManifest(bundleRoot, "deniz-process", "0.2.0", () => "100644");
  const plan = requireFindingFree(
    planReconcile(
      { schemaVersion: 1, modules: {}, files: {} },
      { "deniz-process": manifest },
      { "commands/alpha.md": { kind: "absent" } },
      { kind: "install", modules: ["deniz-process"], all: false, platform: "posix" },
    ),
  );
  return {
    destination,
    bundles: new Map([["deniz-process", { root: bundleRoot, manifest }]]),
    plan,
    target,
  };
}

function makeChmodFixture(): {
  destination: string;
  bundles: Map<string, ModuleBundle>;
  plan: Plan;
  target: string;
  oldState: InstallState;
} {
  const root = mkdtempSync(join(tmpdir(), "apply-chmod-"));
  const destination = join(root, "config", "opencode");
  const bundleRoot = join(root, "package", "opencode", "deniz-process");
  const target = join(destination, "skills", "alpha", "run.sh");
  const bytes = "#!/bin/sh\n";
  mkdirSync(dirname(target), { recursive: true });
  mkdirSync(join(destination, ".deniz-skills"), { recursive: true });
  mkdirSync(join(bundleRoot, "skills", "alpha"), { recursive: true });
  writeFileSync(target, bytes);
  writeFileSync(join(bundleRoot, "skills", "alpha", "run.sh"), bytes);
  chmodSync(target, 0o644);
  const oldState = stateWithOwnedCommand("deniz-process", "skills/alpha/run.sh", bytes, "0.1.0", "100644");
  writeFileSync(join(destination, ".deniz-skills", "install.json"), serializeInstallState(oldState));
  const manifest = createModuleManifest(bundleRoot, "deniz-process", "0.2.0", () => "100755");
  const plan = requireFindingFree(
    planReconcile(oldState, { "deniz-process": manifest }, observeOwnedPaths(destination, oldState, manifest), {
      kind: "update",
      modules: [],
      all: false,
      platform: "posix",
    }),
  );
  return {
    destination,
    bundles: new Map([["deniz-process", { root: bundleRoot, manifest }]]),
    plan,
    target,
    oldState,
  };
}

function withLock(destination: string, fn: (lock: InstallerLock) => void): void {
  const lock = acquireInstallerLock(destination);
  try {
    fn(lock);
  } finally {
    lock.release();
  }
}

function apply(
  destination: string,
  plan: Plan,
  bundles: Map<string, ModuleBundle>,
  options?: ApplyOptions,
): void {
  withLock(destination, (lock) => {
    applyPlan(lock, destination, plan, bundles, options);
  });
}

function writeLeftoverTransaction(
  destination: string,
  journal: TransactionJournal,
  files: { oldStateBytes: string; newStateBytes: string; backup?: { path: string; bytes: string } },
): string {
  const transactionDir = join(destination, ".deniz-skills", `txn-${journal.transactionId}`);
  mkdirSync(transactionDir, { recursive: true });
  writeFileSync(join(transactionDir, "old-state.json"), files.oldStateBytes);
  writeFileSync(join(transactionDir, "new-state.json"), files.newStateBytes);
  writeFileSync(join(transactionDir, "journal.json"), `${JSON.stringify(journal, null, 2)}\n`);
  if (files.backup) {
    const backupPath = join(transactionDir, "backup", ...files.backup.path.split("/"));
    mkdirSync(dirname(backupPath), { recursive: true });
    writeFileSync(backupPath, files.backup.bytes);
  }
  return transactionDir;
}

test("failure after placing files restores bytes and old state", () => {
  const fixture = makeInstallFixture();
  assert.throws(
    () => apply(fixture.destination, fixture.plan, fixture.bundles, { failAfter: "after-place" }),
    /injected after-place failure/,
  );
  assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
  assertSameState(fixture.destination, fixture.oldState);
  assert.equal(inspectRecovery(fixture.destination), null);
});

test("recovery after state commit finalizes cleanup instead of rolling back", () => {
  const fixture = makeInstallFixture();
  assert.throws(
    () => apply(fixture.destination, fixture.plan, fixture.bundles, { failAfter: "after-state-commit" }),
    /injected after-state-commit failure/,
  );
  assert.equal(readFileSync(fixture.target, "utf8"), "new\n");
  assertSameState(fixture.destination, fixture.plan.nextState);
  const recovery = inspectRecovery(fixture.destination);
  assert.equal(recovery?.kind, "finalize");
});

test("failure after backup restores bytes and old state", () => {
  const fixture = makeInstallFixture();
  assert.throws(
    () => apply(fixture.destination, fixture.plan, fixture.bundles, { failAfter: "after-backup" }),
    /injected after-backup failure/,
  );
  assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
  assertSameState(fixture.destination, fixture.oldState);
  assert.equal(inspectRecovery(fixture.destination), null);
});

test("successful apply replaces bytes, commits next state, and leaves no transaction", () => {
  const fixture = makeInstallFixture();
  apply(fixture.destination, fixture.plan, fixture.bundles);
  assert.equal(readFileSync(fixture.target, "utf8"), "new\n");
  assertSameState(fixture.destination, fixture.plan.nextState);
  assert.equal(inspectRecovery(fixture.destination), null);
});

test("live lock owner blocks a second mutating acquire", () => {
  const fixture = makeInstallFixture();
  const lock = acquireInstallerLock(fixture.destination);
  try {
    assert.throws(() => acquireInstallerLock(fixture.destination), /lock|wait|process/i);
    applyPlan(lock, fixture.destination, fixture.plan, fixture.bundles);
    assert.equal(readFileSync(fixture.target, "utf8"), "new\n");
  } finally {
    lock.release();
  }
});

test("second mutation cannot read or apply a stale snapshot while the lock is held", () => {
  const fixture = makeInstallFixture();
  const lock = acquireInstallerLock(fixture.destination);
  try {
    const stale = fixture.plan;
    assert.throws(() => {
      const stolen = acquireInstallerLock(fixture.destination);
      applyPlan(stolen, fixture.destination, stale, fixture.bundles);
    }, /lock|wait|process/i);
    assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
    assertSameState(fixture.destination, fixture.oldState);
  } finally {
    lock.release();
  }
});

test("applyPlan refuses a lock that is not held", () => {
  const fixture = makeInstallFixture();
  const fake: InstallerLock = { path: join(fixture.destination, ".deniz-skills", "lock"), release() {} };
  assert.throws(() => applyPlan(fake, fixture.destination, fixture.plan, fixture.bundles), /lock/i);
  assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
});

test("stale destination bytes are refused by the immediate precondition recheck", () => {
  const fixture = makeInstallFixture();
  assert.throws(
    () =>
      apply(fixture.destination, fixture.plan, fixture.bundles, {
        beforeOperation: () => {
          writeFileSync(fixture.target, "edited locally\n");
        },
      }),
    /modified locally|precondition|identity/i,
  );
  assert.equal(readFileSync(fixture.target, "utf8"), "edited locally\n");
  assertSameState(fixture.destination, fixture.oldState);
});

test("a parent symlink injected between plan and apply is refused and not followed", (t) => {
  const fixture = makeInstallFixture();
  const commands = join(fixture.destination, "commands");
  const moved = join(fixture.destination, "commands-real");
  const outside = join(dirname(fixture.destination), "outside.md");
  writeFileSync(outside, "outside\n");
  assert.throws(
    () =>
      apply(fixture.destination, fixture.plan, fixture.bundles, {
        beforeOperation: () => {
          renameSync(commands, moved);
          if (!tryLink(moved, commands, "dir") && !tryLink(moved, commands, "junction")) {
            renameSync(moved, commands);
            t.skip("creating a parent symlink or junction is not permitted");
          }
        },
      }),
    /symlink|junction|link|managed path/i,
  );
  if (lstatSync(commands).isSymbolicLink()) {
    assert.equal(readFileSync(join(moved, "alpha.md"), "utf8"), fixture.oldBytes);
    assert.notEqual(readFileSync(join(moved, "alpha.md"), "utf8"), "new\n");
  }
  assertSameState(fixture.destination, fixture.oldState);
});

test("Windows junction parent is refused by the immediate recheck", (t) => {
  if (process.platform !== "win32") {
    t.skip("junctions are a Windows Destination case");
    return;
  }
  const fixture = makeInstallFixture();
  const commands = join(fixture.destination, "commands");
  const moved = join(fixture.destination, "commands-real");
  assert.throws(
    () =>
      apply(fixture.destination, fixture.plan, fixture.bundles, {
        beforeOperation: () => {
          renameSync(commands, moved);
          if (!tryLink(moved, commands, "junction")) {
            renameSync(moved, commands);
            t.skip("creating junctions is not permitted on this host");
          }
        },
      }),
    /junction|symlink|link|managed path/i,
  );
  assertSameState(fixture.destination, fixture.oldState);
});

test("Windows add refuses a case-alias destination file", (t) => {
  if (process.platform !== "win32") {
    t.skip("case-insensitive destination aliases are a Windows case");
    return;
  }
  const fixture = makeAddFixture();
  mkdirSync(dirname(fixture.target), { recursive: true });
  writeFileSync(join(fixture.destination, "commands", "Alpha.md"), "alias\n");
  assert.throws(() => apply(fixture.destination, fixture.plan, fixture.bundles), /exist|collision|absent|precondition/i);
  assert.equal(readFileSync(join(fixture.destination, "commands", "Alpha.md"), "utf8"), "alias\n");
});

test("mode rollback restores the previous POSIX mode and old state", (t) => {
  const fixture = makeChmodFixture();
  if (fixture.plan.operations.every((operation) => operation.kind !== "chmod")) {
    t.skip("planner did not emit chmod on this host");
    return;
  }
  assert.throws(
    () => apply(fixture.destination, fixture.plan, fixture.bundles, { failAfter: "after-place" }),
    /injected after-place failure/,
  );
  assertSameState(fixture.destination, fixture.oldState);
  if (process.platform === "win32") {
    assert.ok(posixMode(fixture.target) === "100644" || posixMode(fixture.target) === "100755");
    return;
  }
  assert.equal(posixMode(fixture.target), "100644");
});

test("empty-directory pruning keeps unknown content and native roots", () => {
  const fixture = makeRemoveFixture();
  apply(fixture.destination, fixture.plan, fixture.bundles);
  assert.equal(readFileSync(fixture.extraFile, "utf8"), "keep me\n");
  assert.deepEqual(readdirSync(join(fixture.destination, "skills", "alpha")), ["notes.md"]);
  assert.ok(readdirSync(join(fixture.destination, "skills")).includes("alpha"));
  assert.equal(inspectRecovery(fixture.destination), null);
});

test("removing the last owned nested file prunes empty parents but not the native root", () => {
  const root = mkdtempSync(join(tmpdir(), "apply-prune-"));
  const destination = join(root, "config", "opencode");
  const skillFile = join(destination, "skills", "alpha", "SKILL.md");
  mkdirSync(dirname(skillFile), { recursive: true });
  mkdirSync(join(destination, ".deniz-skills"), { recursive: true });
  writeFileSync(skillFile, "skill\n");
  const oldState = stateWithOwnedCommand("deniz-process", "skills/alpha/SKILL.md", "skill\n");
  writeFileSync(join(destination, ".deniz-skills", "install.json"), serializeInstallState(oldState));
  const plan = requireFindingFree(
    planReconcile(
      oldState,
      {},
      { "skills/alpha/SKILL.md": observePath(destination, "skills/alpha/SKILL.md") },
      { kind: "remove", modules: ["deniz-process"], all: false, platform: "posix" },
    ),
  );
  apply(destination, plan, new Map());
  assert.equal(readdirSync(join(destination, "skills")).includes("alpha"), false);
  assert.ok(lstatSync(join(destination, "skills")).isDirectory());
});

test("old-state digest leftover is a rollback recovery and does not resume the plan", () => {
  const fixture = makeInstallFixture();
  writeFileSync(fixture.target, "new\n");
  const transactionDir = writeLeftoverTransaction(
    fixture.destination,
    {
      schemaVersion: 1,
      transactionId: "crash-old",
      oldStateDigest: stateDigest(fixture.oldState),
      newStateDigest: stateDigest(fixture.plan.nextState),
      operations: fixture.plan.operations,
      phase: "files-placed",
    },
    {
      oldStateBytes: serializeInstallState(fixture.oldState),
      newStateBytes: serializeInstallState(fixture.plan.nextState),
      backup: { path: "commands/alpha.md", bytes: fixture.oldBytes },
    },
  );
  const recovery = inspectRecovery(fixture.destination);
  assert.ok(recovery);
  assert.equal(recovery.kind, "rollback");
  if (recovery.kind !== "rollback") {
    return;
  }
  assert.equal(recovery.transactionDir, transactionDir);
  withLock(fixture.destination, (lock) => {
    applyRecovery(lock, fixture.destination, recovery);
  });
  assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
  assertSameState(fixture.destination, fixture.oldState);
  assert.equal(inspectRecovery(fixture.destination), null);
});

test("unknown install-state digest refuses recovery and mutates nothing", () => {
  const fixture = makeInstallFixture();
  const otherBytes = "other\n";
  const otherState = stateWithOwnedCommand("deniz-process", "commands/alpha.md", otherBytes, "9.9.9");
  writeFileSync(fixture.target, otherBytes);
  writeFileSync(join(fixture.destination, ".deniz-skills", "install.json"), serializeInstallState(otherState));
  writeLeftoverTransaction(
    fixture.destination,
    {
      schemaVersion: 1,
      transactionId: "crash-unknown",
      oldStateDigest: stateDigest(fixture.oldState),
      newStateDigest: stateDigest(fixture.plan.nextState),
      operations: fixture.plan.operations,
      phase: "files-placed",
    },
    {
      oldStateBytes: serializeInstallState(fixture.oldState),
      newStateBytes: serializeInstallState(fixture.plan.nextState),
      backup: { path: "commands/alpha.md", bytes: fixture.oldBytes },
    },
  );
  const recovery = inspectRecovery(fixture.destination);
  assert.equal(recovery?.kind, "blocked");
  withLock(fixture.destination, (lock) => {
    assert.throws(() => applyRecovery(lock, fixture.destination, recovery as RecoveryPlan), /blocked|neither|digest/i);
  });
  assert.equal(readFileSync(fixture.target, "utf8"), otherBytes);
  assertSameState(fixture.destination, otherState);
  assert.equal(inspectRecovery(fixture.destination)?.kind, "blocked");
});

test("applyRecovery finalize keeps the committed tree and deletes debris", () => {
  const fixture = makeInstallFixture();
  assert.throws(
    () => apply(fixture.destination, fixture.plan, fixture.bundles, { failAfter: "after-state-commit" }),
    /injected after-state-commit failure/,
  );
  const recovery = inspectRecovery(fixture.destination);
  assert.equal(recovery?.kind, "finalize");
  withLock(fixture.destination, (lock) => {
    applyRecovery(lock, fixture.destination, recovery as RecoveryPlan);
  });
  assert.equal(readFileSync(fixture.target, "utf8"), "new\n");
  assertSameState(fixture.destination, fixture.plan.nextState);
  assert.equal(inspectRecovery(fixture.destination), null);
});

test("applyPlan refuses to resume while a transaction needs recovery", () => {
  const fixture = makeInstallFixture();
  assert.throws(
    () => apply(fixture.destination, fixture.plan, fixture.bundles, { failAfter: "after-state-commit" }),
    /injected after-state-commit failure/,
  );
  assert.throws(() => apply(fixture.destination, fixture.plan, fixture.bundles), /recovery|transaction/i);
  assert.equal(readFileSync(fixture.target, "utf8"), "new\n");
});

test("abandoned lock with a dead owner is reclaimed after inspecting recovery", () => {
  const fixture = makeInstallFixture();
  const lockDir = join(fixture.destination, ".deniz-skills", "lock");
  mkdirSync(lockDir, { recursive: true });
  writeFileSync(
    join(lockDir, "owner.json"),
    `${JSON.stringify({ pid: deadPid(), startedAt: "2020-01-01T00:00:00.000Z" })}\n`,
  );
  const lock = acquireInstallerLock(fixture.destination);
  try {
    applyPlan(lock, fixture.destination, fixture.plan, fixture.bundles);
    assert.equal(readFileSync(fixture.target, "utf8"), "new\n");
  } finally {
    lock.release();
  }
});

test("abandoned lock with leftover recovery is reclaimed without deleting the transaction", () => {
  const fixture = makeInstallFixture();
  writeFileSync(fixture.target, "new\n");
  writeLeftoverTransaction(
    fixture.destination,
    {
      schemaVersion: 1,
      transactionId: "abandoned",
      oldStateDigest: stateDigest(fixture.oldState),
      newStateDigest: stateDigest(fixture.plan.nextState),
      operations: fixture.plan.operations,
      phase: "files-placed",
    },
    {
      oldStateBytes: serializeInstallState(fixture.oldState),
      newStateBytes: serializeInstallState(fixture.plan.nextState),
      backup: { path: "commands/alpha.md", bytes: fixture.oldBytes },
    },
  );
  const lockDir = join(fixture.destination, ".deniz-skills", "lock");
  mkdirSync(lockDir, { recursive: true });
  writeFileSync(
    join(lockDir, "owner.json"),
    `${JSON.stringify({ pid: deadPid(), startedAt: "2020-01-01T00:00:00.000Z" })}\n`,
  );
  const lock = acquireInstallerLock(fixture.destination);
  try {
    const recovery = inspectRecovery(fixture.destination);
    assert.equal(recovery?.kind, "rollback");
    applyRecovery(lock, fixture.destination, recovery as RecoveryPlan);
  } finally {
    lock.release();
  }
  assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
  assertSameState(fixture.destination, fixture.oldState);
});

test("applyPlan refuses a plan that still has findings", () => {
  const fixture = makeInstallFixture();
  const blocked: Plan = { ...fixture.plan, findings: [{ code: "local_modification", message: "blocked", path: "commands/alpha.md" }] };
  assert.throws(() => apply(fixture.destination, blocked, fixture.bundles), /finding/i);
  assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
});

test("first install creates destination files and install state", () => {
  const fixture = makeAddFixture();
  apply(fixture.destination, fixture.plan, fixture.bundles);
  assert.equal(readFileSync(fixture.target, "utf8"), "new\n");
  assertSameState(fixture.destination, fixture.plan.nextState);
  assert.equal(inspectRecovery(fixture.destination), null);
});
