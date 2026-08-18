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
  type AppliedMutation,
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

function fileIdentityOf(bytes: string, mode: "100644" | "100755" = "100644"): AppliedMutation["identity"] {
  return { sha256: hashBytes(bytes), mode };
}

type LeftoverJournal = Omit<TransactionJournal, "applied" | "createdDirectories" | "stateAside"> & {
  applied?: Array<Omit<AppliedMutation, "identity"> & { identity?: AppliedMutation["identity"] }>;
  createdDirectories?: string[];
  stateAside?: boolean;
};

function completeJournal(
  journal: LeftoverJournal,
  files: { backup?: { path: string; bytes: string }; placedBytes?: string; expectedOldBytes?: string },
): TransactionJournal {
  const backupPath = files.backup?.path;
  const oldIdentity = files.expectedOldBytes
    ? fileIdentityOf(files.expectedOldBytes)
    : files.backup
      ? fileIdentityOf(files.backup.bytes)
      : undefined;
  const placedIdentity = fileIdentityOf(files.placedBytes ?? "new\n");
  const applied: AppliedMutation[] =
    journal.applied?.map((item) => ({
      ...item,
      identity: item.identity ?? (item.action === "backed-up" && oldIdentity ? oldIdentity : placedIdentity),
    })) ??
    (backupPath && oldIdentity
      ? [
          { path: backupPath, action: "backed-up", identity: oldIdentity },
          { path: backupPath, action: "placed", identity: placedIdentity },
        ]
      : []);
  return {
    ...journal,
    applied,
    createdDirectories: journal.createdDirectories ?? [],
    stateAside: journal.stateAside ?? false,
  };
}

function writeLeftoverTransaction(
  destination: string,
  journal: LeftoverJournal,
  files: {
    oldStateBytes: string;
    newStateBytes: string;
    backup?: { path: string; bytes: string };
    placedBytes?: string;
    expectedOldBytes?: string;
  },
): string {
  const full = completeJournal(journal, files);
  const transactionDir = join(destination, ".deniz-skills", `txn-${full.transactionId}`);
  mkdirSync(join(transactionDir, "snapshots"), { recursive: true });
  writeFileSync(join(transactionDir, "old-state.json"), files.oldStateBytes);
  writeFileSync(join(transactionDir, "new-state.json"), files.newStateBytes);
  writeFileSync(join(transactionDir, "snapshots", "000001.json"), `${JSON.stringify(full, null, 2)}\n`);
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
  const fake: InstallerLock = {
    path: join(fixture.destination, ".deniz-skills", "lock"),
    token: "not-held",
    release() {},
  };
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
  assert.ok(readdirSync(join(destination, "skills")).includes("alpha"));
  assert.deepEqual(readdirSync(join(destination, "skills", "alpha")), []);
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
  assert.throws(() => acquireInstallerLock(fixture.destination), /recovery|transaction/i);
  const lock = acquireInstallerLock(fixture.destination, { recover: true });
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

function makeTwoFileFixture(): {
  destination: string;
  bundles: Map<string, ModuleBundle>;
  plan: Plan;
  alpha: string;
  beta: string;
  oldAlpha: string;
  oldBeta: string;
  oldState: InstallState;
} {
  const root = mkdtempSync(join(tmpdir(), "apply-two-"));
  const destination = join(root, "config", "opencode");
  const bundleRoot = join(root, "package", "opencode", "deniz-process");
  const alpha = join(destination, "commands", "alpha.md");
  const beta = join(destination, "commands", "beta.md");
  const oldAlpha = "old-alpha\n";
  const oldBeta = "old-beta\n";
  mkdirSync(dirname(alpha), { recursive: true });
  mkdirSync(join(destination, ".deniz-skills"), { recursive: true });
  mkdirSync(join(bundleRoot, "commands"), { recursive: true });
  writeFileSync(alpha, oldAlpha);
  writeFileSync(beta, oldBeta);
  writeFileSync(join(bundleRoot, "commands", "alpha.md"), "new-alpha\n");
  writeFileSync(join(bundleRoot, "commands", "beta.md"), "new-beta\n");
  const oldIdentities = {
    "commands/alpha.md": { sha256: hashBytes(oldAlpha), mode: "100644" as const },
    "commands/beta.md": { sha256: hashBytes(oldBeta), mode: "100644" as const },
  };
  const oldState: InstallState = {
    schemaVersion: 1,
    modules: { "deniz-process": { version: "0.1.0", digest: digestFileMap(oldIdentities) } },
    files: {
      "commands/alpha.md": { module: "deniz-process", ...oldIdentities["commands/alpha.md"] },
      "commands/beta.md": { module: "deniz-process", ...oldIdentities["commands/beta.md"] },
    },
  };
  writeFileSync(join(destination, ".deniz-skills", "install.json"), serializeInstallState(oldState));
  const manifest = createModuleManifest(bundleRoot, "deniz-process", "0.2.0", () => "100644");
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
    alpha,
    beta,
    oldAlpha,
    oldBeta,
    oldState,
  };
}

test("release of a stolen lock token does not delete the competing owner", () => {
  const fixture = makeInstallFixture();
  const lock = acquireInstallerLock(fixture.destination);
  const ownerFile = join(lock.path, "owner.json");
  writeFileSync(
    ownerFile,
    `${JSON.stringify({ pid: process.pid, startedAt: "2026-01-01T00:00:00.000Z", token: "competitor-token" })}\n`,
  );
  lock.release();
  assert.ok(lstatSync(lock.path).isDirectory());
  assert.match(readFileSync(ownerFile, "utf8"), /competitor-token/);
  assert.throws(() => applyPlan(lock, fixture.destination, fixture.plan, fixture.bundles), /lock|token/i);
  assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
  assertSameState(fixture.destination, fixture.oldState);
});

test("a transaction directory without a journal is blocked debris", () => {
  const fixture = makeInstallFixture();
  mkdirSync(join(fixture.destination, ".deniz-skills", "txn-debris"), { recursive: true });
  const recovery = inspectRecovery(fixture.destination);
  assert.equal(recovery?.kind, "blocked");
  assert.match(recovery && recovery.kind === "blocked" ? recovery.message : "", /debris|journal|transaction/i);
});

test("a journal that is a symlink is blocked and not followed", (t) => {
  const fixture = makeInstallFixture();
  const transactionDir = join(fixture.destination, ".deniz-skills", "txn-link");
  mkdirSync(join(transactionDir, "snapshots"), { recursive: true });
  const target = join(fixture.destination, ".deniz-skills", "not-a-journal.txt");
  writeFileSync(target, "not-json\n");
  if (!tryLink(target, join(transactionDir, "snapshots", "000001.json"), "file")) {
    t.skip("creating a journal symlink is not permitted");
    return;
  }
  const recovery = inspectRecovery(fixture.destination);
  assert.equal(recovery?.kind, "blocked");
});

test("normal acquire does not reclaim a dead lock when recovery is pending", () => {
  const fixture = makeInstallFixture();
  writeFileSync(fixture.target, "new\n");
  writeLeftoverTransaction(
    fixture.destination,
    {
      schemaVersion: 1,
      transactionId: "pending-recovery",
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
    `${JSON.stringify({ pid: deadPid(), startedAt: "2020-01-01T00:00:00.000Z", token: "dead-token" })}\n`,
  );
  assert.throws(() => acquireInstallerLock(fixture.destination), /recovery|transaction/i);
  assert.equal(inspectRecovery(fixture.destination)?.kind, "rollback");
});

test("recheck happens immediately before each backup, not as a batch", () => {
  const fixture = makeTwoFileFixture();
  assert.throws(
    () =>
      apply(fixture.destination, fixture.plan, fixture.bundles, {
        failAfter: "after-backup",
        beforeOperation: (operation) => {
          if (operation.path === "commands/beta.md") {
            writeFileSync(fixture.alpha, "edited-after-first-recheck\n");
          }
        },
      }),
    /injected after-backup failure/,
  );
  assert.equal(readFileSync(fixture.alpha, "utf8"), fixture.oldAlpha);
  assert.equal(readFileSync(fixture.beta, "utf8"), fixture.oldBeta);
});

test("acquire refuses to create lock data through a .deniz-skills link", (t) => {
  const fixture = makeInstallFixture();
  const deniz = join(fixture.destination, ".deniz-skills");
  const moved = join(fixture.destination, "deniz-real");
  renameSync(deniz, moved);
  if (!tryLink(moved, deniz, "dir") && !tryLink(moved, deniz, "junction")) {
    renameSync(moved, deniz);
    t.skip("creating a .deniz-skills symlink or junction is not permitted");
    return;
  }
  assert.throws(() => acquireInstallerLock(fixture.destination), /symlink|junction|link/i);
});

test("injected device mismatch rejects cross-filesystem rename topology", () => {
  const fixture = makeInstallFixture();
  assert.throws(
    () =>
      apply(fixture.destination, fixture.plan, fixture.bundles, {
        io: {
          deviceId(path: string): number {
            return path.replaceAll("\\", "/").includes("/.deniz-skills") ? 2 : 1;
          },
        },
      }),
    /EXDEV|filesystem|device/i,
  );
  assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
  assertSameState(fixture.destination, fixture.oldState);
});

test("crash after place leaves rollback recovery without in-process undo", () => {
  const fixture = makeInstallFixture();
  assert.throws(
    () => apply(fixture.destination, fixture.plan, fixture.bundles, { crashAfter: "after-place" }),
    /injected crash after-place/,
  );
  assert.equal(readFileSync(fixture.target, "utf8"), "new\n");
  assertSameState(fixture.destination, fixture.oldState);
  const recovery = inspectRecovery(fixture.destination);
  assert.equal(recovery?.kind, "rollback");
  withLock(fixture.destination, (lock) => {
    applyRecovery(lock, fixture.destination, recovery as RecoveryPlan);
  });
  assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
  assertSameState(fixture.destination, fixture.oldState);
  assert.equal(inspectRecovery(fixture.destination), null);
});

test("crash after state commit leaves finalize recovery", () => {
  const fixture = makeInstallFixture();
  assert.throws(
    () => apply(fixture.destination, fixture.plan, fixture.bundles, { crashAfter: "after-state-commit" }),
    /injected crash after-state-commit/,
  );
  assert.equal(readFileSync(fixture.target, "utf8"), "new\n");
  assertSameState(fixture.destination, fixture.plan.nextState);
  assert.equal(inspectRecovery(fixture.destination)?.kind, "finalize");
});

test("crash after moving old Install state aside is precommit rollback", () => {
  const fixture = makeInstallFixture();
  assert.throws(
    () =>
      apply(fixture.destination, fixture.plan, fixture.bundles, {
        forceWindowsStateReplace: true,
        crashAfter: "after-state-aside",
      }),
    /injected crash after-state-aside/,
  );
  assert.equal(existsLstatSafe(join(fixture.destination, ".deniz-skills", "install.json")), false);
  const recovery = inspectRecovery(fixture.destination);
  assert.equal(recovery?.kind, "rollback");
  withLock(fixture.destination, (lock) => {
    applyRecovery(lock, fixture.destination, recovery as RecoveryPlan);
  });
  assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
  assertSameState(fixture.destination, fixture.oldState);
});

test("rollback undoes only mutations recorded as applied", () => {
  const fixture = makeTwoFileFixture();
  writeFileSync(fixture.alpha, "new-alpha\n");
  writeFileSync(fixture.beta, "new-beta\n");
  writeLeftoverTransaction(
    fixture.destination,
    {
      schemaVersion: 1,
      transactionId: "partial",
      oldStateDigest: stateDigest(fixture.oldState),
      newStateDigest: stateDigest(fixture.plan.nextState),
      operations: fixture.plan.operations,
      phase: "files-placed",
      applied: [
        { path: "commands/alpha.md", action: "backed-up" },
        { path: "commands/alpha.md", action: "placed" },
      ],
    },
    {
      oldStateBytes: serializeInstallState(fixture.oldState),
      newStateBytes: serializeInstallState(fixture.plan.nextState),
      backup: { path: "commands/alpha.md", bytes: fixture.oldAlpha },
      placedBytes: "new-alpha\n",
    },
  );
  const recovery = inspectRecovery(fixture.destination);
  assert.equal(recovery?.kind, "rollback");
  withLock(fixture.destination, (lock) => {
    applyRecovery(lock, fixture.destination, recovery as RecoveryPlan);
  });
  assert.equal(readFileSync(fixture.alpha, "utf8"), fixture.oldAlpha);
  assert.equal(readFileSync(fixture.beta, "utf8"), "new-beta\n");
});

test("recovery refuses a journal whose backup evidence is missing", () => {
  const fixture = makeInstallFixture();
  writeFileSync(fixture.target, "new\n");
  writeLeftoverTransaction(
    fixture.destination,
    {
      schemaVersion: 1,
      transactionId: "no-backup",
      oldStateDigest: stateDigest(fixture.oldState),
      newStateDigest: stateDigest(fixture.plan.nextState),
      operations: fixture.plan.operations,
      phase: "files-placed",
      applied: [
        { path: "commands/alpha.md", action: "backed-up" },
        { path: "commands/alpha.md", action: "placed" },
      ],
    },
    {
      oldStateBytes: serializeInstallState(fixture.oldState),
      newStateBytes: serializeInstallState(fixture.plan.nextState),
    },
  );
  const recovery = inspectRecovery(fixture.destination);
  assert.equal(recovery?.kind, "rollback");
  withLock(fixture.destination, (lock) => {
    assert.throws(() => applyRecovery(lock, fixture.destination, recovery as RecoveryPlan), /backup/i);
  });
  assert.equal(readFileSync(fixture.target, "utf8"), "new\n");
  assertSameState(fixture.destination, fixture.oldState);
});

test("malformed journal operations block recovery", () => {
  const fixture = makeInstallFixture();
  const transactionDir = join(fixture.destination, ".deniz-skills", "txn-bad-op");
  mkdirSync(transactionDir, { recursive: true });
  mkdirSync(join(transactionDir, "snapshots"), { recursive: true });
  writeFileSync(
    join(transactionDir, "snapshots", "000001.json"),
    `${JSON.stringify({
      schemaVersion: 1,
      transactionId: "bad-op",
      oldStateDigest: stateDigest(fixture.oldState),
      newStateDigest: stateDigest(fixture.plan.nextState),
      operations: [{ kind: "replace", path: "../escape", module: "deniz-process", source: "x", identity: { sha256: hashBytes("x"), mode: "100644" } }],
      phase: "prepared",
      applied: [],
      createdDirectories: [],
      stateAside: false,
    })}\n`,
  );
  assert.equal(inspectRecovery(fixture.destination)?.kind, "blocked");
});

test("rollback of an add removes directories created by this transaction", () => {
  const root = mkdtempSync(join(tmpdir(), "apply-add-nested-"));
  const destination = join(root, "config", "opencode");
  const bundleRoot = join(root, "package", "opencode", "deniz-process");
  mkdirSync(join(bundleRoot, "skills", "alpha"), { recursive: true });
  writeFileSync(join(bundleRoot, "skills", "alpha", "SKILL.md"), "new\n");
  const manifest = createModuleManifest(bundleRoot, "deniz-process", "0.2.0", () => "100644");
  const plan = requireFindingFree(
    planReconcile(
      { schemaVersion: 1, modules: {}, files: {} },
      { "deniz-process": manifest },
      { "skills/alpha/SKILL.md": { kind: "absent" } },
      { kind: "install", modules: ["deniz-process"], all: false, platform: "posix" },
    ),
  );
  assert.throws(
    () => apply(destination, plan, new Map([["deniz-process", { root: bundleRoot, manifest }]]), { failAfter: "after-place" }),
    /injected after-place failure/,
  );
  assert.equal(existsLstatSafe(join(destination, "skills", "alpha", "SKILL.md")), false);
  assert.equal(existsLstatSafe(join(destination, "skills", "alpha")), false);
});

function existsLstatSafe(path: string): boolean {
  try {
    lstatSync(path);
    return true;
  } catch {
    return false;
  }
}

function makeDropMissingFixture(): {
  destination: string;
  plan: Plan;
  target: string;
  oldState: InstallState;
} {
  const root = mkdtempSync(join(tmpdir(), "apply-drop-"));
  const destination = join(root, "config", "opencode");
  const target = join(destination, "commands", "alpha.md");
  mkdirSync(join(destination, "commands"), { recursive: true });
  mkdirSync(join(destination, ".deniz-skills"), { recursive: true });
  const oldState = stateWithOwnedCommand("deniz-process", "commands/alpha.md", "old\n");
  writeFileSync(join(destination, ".deniz-skills", "install.json"), serializeInstallState(oldState));
  const plan = requireFindingFree(
    planReconcile(
      oldState,
      {},
      { "commands/alpha.md": { kind: "absent" } },
      { kind: "remove", modules: ["deniz-process"], all: false, platform: "posix" },
    ),
  );
  return { destination, plan, target, oldState };
}

test("reclaim-in-progress blocks a second stale acquire", () => {
  const fixture = makeInstallFixture();
  const lockDir = join(fixture.destination, ".deniz-skills", "lock");
  mkdirSync(lockDir, { recursive: true });
  writeFileSync(
    join(lockDir, "owner.json"),
    `${JSON.stringify({ pid: deadPid(), startedAt: "2020-01-01T00:00:00.000Z", token: "dead-token" })}\n`,
  );
  mkdirSync(join(fixture.destination, ".deniz-skills", "lock.reclaim"));
  assert.throws(() => acquireInstallerLock(fixture.destination), /reclaim/i);
  assert.match(readFileSync(join(lockDir, "owner.json"), "utf8"), /dead-token/);
});

test("stale reclaim rereads liveness before renaming the lock", () => {
  const fixture = makeInstallFixture();
  const lockDir = join(fixture.destination, ".deniz-skills", "lock");
  mkdirSync(lockDir, { recursive: true });
  writeFileSync(
    join(lockDir, "owner.json"),
    `${JSON.stringify({ pid: deadPid(), startedAt: "2020-01-01T00:00:00.000Z", token: "dead-token" })}\n`,
  );
  assert.throws(
    () =>
      acquireInstallerLock(fixture.destination, {
        io: {
          beforeReclaimRename: () => {
            writeFileSync(
              join(lockDir, "owner.json"),
              `${JSON.stringify({ pid: process.pid, startedAt: "2026-01-01T00:00:00.000Z", token: "now-live" })}\n`,
            );
          },
        },
      }),
    /wait|process|live/i,
  );
  assert.match(readFileSync(join(lockDir, "owner.json"), "utf8"), /now-live/);
});

test("release renames the owned lock to a tombstone and survives injected rm failure", () => {
  const fixture = makeInstallFixture();
  const lock = acquireInstallerLock(fixture.destination, {
    io: {
      rmSync: () => {
        throw new Error("injected rm failure");
      },
    },
  });
  const token = lock.token;
  assert.throws(() => lock.release(), /injected rm failure/);
  assert.equal(existsLstatSafe(lock.path), false);
  const tombstone = join(fixture.destination, ".deniz-skills", `lock.released-${token}`);
  assert.ok(lstatSync(tombstone).isDirectory());
  const next = acquireInstallerLock(fixture.destination);
  assert.notEqual(next.token, token);
  next.release();
});

test("crash after backup syscall is inferred and rolled back without deleting the only backup", () => {
  const fixture = makeInstallFixture();
  assert.throws(
    () => apply(fixture.destination, fixture.plan, fixture.bundles, { crashAfterSyscall: "backup" }),
    /injected crash after backup syscall/,
  );
  assert.equal(existsLstatSafe(fixture.target), false);
  const recovery = inspectRecovery(fixture.destination);
  assert.equal(recovery?.kind, "rollback");
  assert.ok(recovery);
  withLock(fixture.destination, (lock) => {
    applyRecovery(lock, fixture.destination, recovery as RecoveryPlan);
  });
  assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
  assertSameState(fixture.destination, fixture.oldState);
  assert.equal(inspectRecovery(fixture.destination), null);
});

test("a reappeared drop-missing-claim file blocks commit and rolls back", () => {
  const fixture = makeDropMissingFixture();
  assert.throws(
    () =>
      apply(fixture.destination, fixture.plan, new Map(), {
        beforeOperation: (operation) => {
          if (operation.kind === "drop-missing-claim") {
            writeFileSync(fixture.target, "reappeared\n");
          }
        },
      }),
    /absent|reappeared|modified/i,
  );
  assert.equal(readFileSync(fixture.target, "utf8"), "reappeared\n");
  assertSameState(fixture.destination, fixture.oldState);
});

test("malformed journal snapshot debris is blocked", () => {
  const fixture = makeInstallFixture();
  const transactionDir = join(fixture.destination, ".deniz-skills", "txn-malformed");
  mkdirSync(join(transactionDir, "snapshots"), { recursive: true });
  writeFileSync(join(transactionDir, "snapshots", "000001.json"), "{");
  assert.equal(inspectRecovery(fixture.destination)?.kind, "blocked");
});

test("a journal snapshot that is a symlink is blocked", (t) => {
  const fixture = makeInstallFixture();
  const transactionDir = join(fixture.destination, ".deniz-skills", "txn-snap-link");
  mkdirSync(join(transactionDir, "snapshots"), { recursive: true });
  const target = join(fixture.destination, ".deniz-skills", "not-a-journal.txt");
  writeFileSync(target, "not-json\n");
  if (!tryLink(target, join(transactionDir, "snapshots", "000001.json"), "file")) {
    t.skip("creating a journal snapshot symlink is not permitted");
    return;
  }
  assert.equal(inspectRecovery(fixture.destination)?.kind, "blocked");
});

test("a temp journal snapshot that is a symlink is blocked debris", (t) => {
  const fixture = makeInstallFixture();
  writeLeftoverTransaction(
    fixture.destination,
    {
      schemaVersion: 1,
      transactionId: "tmp-link",
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
  const snapshots = join(fixture.destination, ".deniz-skills", "txn-tmp-link", "snapshots");
  mkdirSync(snapshots, { recursive: true });
  const target = join(fixture.destination, ".deniz-skills", "tmp-target.txt");
  writeFileSync(target, "tmp\n");
  if (!tryLink(target, join(snapshots, "000002.deadbeef.tmp"), "file")) {
    t.skip("creating a temp snapshot symlink is not permitted");
    return;
  }
  assert.equal(inspectRecovery(fixture.destination)?.kind, "blocked");
});

test("backup digest mismatch blocks recovery and keeps the transaction", () => {
  const fixture = makeInstallFixture();
  writeFileSync(fixture.target, "new\n");
  const transactionDir = writeLeftoverTransaction(
    fixture.destination,
    {
      schemaVersion: 1,
      transactionId: "bad-digest",
      oldStateDigest: stateDigest(fixture.oldState),
      newStateDigest: stateDigest(fixture.plan.nextState),
      operations: fixture.plan.operations,
      phase: "files-placed",
    },
    {
      oldStateBytes: serializeInstallState(fixture.oldState),
      newStateBytes: serializeInstallState(fixture.plan.nextState),
      backup: { path: "commands/alpha.md", bytes: "not-the-old-bytes\n" },
      expectedOldBytes: fixture.oldBytes,
    },
  );
  const recovery = inspectRecovery(fixture.destination);
  assert.equal(recovery?.kind, "rollback");
  withLock(fixture.destination, (lock) => {
    assert.throws(() => applyRecovery(lock, fixture.destination, recovery as RecoveryPlan), /digest|backup|identity/i);
  });
  assert.equal(readFileSync(fixture.target, "utf8"), "new\n");
  assert.ok(lstatSync(transactionDir).isDirectory());
});

test("finalize refuses when a removed path is still present", () => {
  const fixture = makeRemoveFixture();
  const newState = fixture.plan.nextState;
  writeFileSync(join(fixture.destination, ".deniz-skills", "install.json"), serializeInstallState(newState));
  writeLeftoverTransaction(
    fixture.destination,
    {
      schemaVersion: 1,
      transactionId: "finalize-remove",
      oldStateDigest: stateDigest(fixture.oldState),
      newStateDigest: stateDigest(newState),
      operations: fixture.plan.operations,
      phase: "state-committed",
      applied: [
        {
          path: "skills/alpha/SKILL.md",
          action: "backed-up",
          identity: { sha256: hashBytes("skill\n"), mode: "100644" },
        },
      ],
    },
    {
      oldStateBytes: serializeInstallState(fixture.oldState),
      newStateBytes: serializeInstallState(newState),
      backup: { path: "skills/alpha/SKILL.md", bytes: "skill\n" },
    },
  );
  const recovery = inspectRecovery(fixture.destination);
  assert.equal(recovery?.kind, "finalize");
  withLock(fixture.destination, (lock) => {
    assert.throws(() => applyRecovery(lock, fixture.destination, recovery as RecoveryPlan), /remove|present|finalize/i);
  });
  assert.equal(readFileSync(fixture.skillFile, "utf8"), "skill\n");
});

test("recovery refuses EXDEV topology before mutating", () => {
  const fixture = makeInstallFixture();
  writeFileSync(fixture.target, "new\n");
  writeLeftoverTransaction(
    fixture.destination,
    {
      schemaVersion: 1,
      transactionId: "exdev",
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
  assert.equal(recovery?.kind, "rollback");
  withLock(fixture.destination, (lock) => {
    assert.throws(
      () =>
        applyRecovery(lock, fixture.destination, recovery as RecoveryPlan, {
          io: {
            deviceId(path: string): number {
              return path.replaceAll("\\", "/").includes("/.deniz-skills") ? 2 : 1;
            },
          },
        }),
      /EXDEV|filesystem|device/i,
    );
  });
  assert.equal(readFileSync(fixture.target, "utf8"), "new\n");
  assertSameState(fixture.destination, fixture.oldState);
});
