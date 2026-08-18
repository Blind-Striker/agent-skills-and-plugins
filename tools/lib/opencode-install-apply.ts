import { randomUUID } from "node:crypto";
import {
  chmodSync,
  lstatSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  renameSync,
  rmdirSync,
  rmSync,
  writeFileSync,
  type Stats,
} from "node:fs";
import { dirname, isAbsolute, join, relative, resolve } from "node:path";
import { hashBytes, type FileIdentity, type FileMode, type ModuleBundle, type Sha256 } from "./opencode-bundle.ts";
import type { Plan, PlanOperation } from "./opencode-install-plan.ts";
import {
  EMPTY_INSTALL_STATE,
  loadInstallState,
  observePath,
  serializeInstallState,
  stateDigest,
  validateDestinationRoot,
  validateManagedPath,
  type InstallState,
} from "./opencode-install-state.ts";

export interface InstallerLock {
  path: string;
  release(): void;
}

export type ApplyPhase = "after-backup" | "after-place" | "after-state-commit";

export interface ApplyOptions {
  failAfter?: ApplyPhase;
  beforeOperation?: (operation: PlanOperation) => void;
}

export interface TransactionJournal {
  schemaVersion: 1;
  transactionId: string;
  oldStateDigest: Sha256;
  newStateDigest: Sha256;
  operations: PlanOperation[];
  phase: "prepared" | "files-placed" | "state-committed";
}

export type RecoveryPlan =
  | { kind: "rollback" | "finalize"; transactionDir: string; journal: TransactionJournal }
  | { kind: "blocked"; transactionDir: string; message: string };

const heldLocks = new WeakSet<InstallerLock>();
const SHA256 = /^sha256:[a-f0-9]{64}$/;
const NATIVE_ROOTS = new Set(["agents", "commands", "skills"]);

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isSha256(value: unknown): value is Sha256 {
  return typeof value === "string" && SHA256.test(value);
}

function isENOENT(error: unknown): boolean {
  return error instanceof Error && "code" in error && (error as NodeJS.ErrnoException).code === "ENOENT";
}

function isEEXIST(error: unknown): boolean {
  return error instanceof Error && "code" in error && (error as NodeJS.ErrnoException).code === "EEXIST";
}

function isEPERM(error: unknown): boolean {
  return error instanceof Error && "code" in error && (error as NodeJS.ErrnoException).code === "EPERM";
}

function existsLstat(path: string): boolean {
  try {
    lstatSync(path);
    return true;
  } catch (error) {
    if (isENOENT(error)) {
      return false;
    }
    throw error;
  }
}

function processExists(pid: number): boolean {
  if (!Number.isInteger(pid) || pid <= 0) {
    return false;
  }
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return isEPERM(error);
  }
}

function ownerPath(lockPath: string): string {
  return join(lockPath, "owner.json");
}

function writeOwner(lockPath: string): void {
  writeFileSync(
    ownerPath(lockPath),
    `${JSON.stringify({ pid: process.pid, startedAt: new Date().toISOString() })}\n`,
  );
}

function readOwnerPid(lockPath: string): number | null {
  try {
    const value: unknown = JSON.parse(readFileSync(ownerPath(lockPath), "utf8"));
    if (isRecord(value) && typeof value.pid === "number") {
      return value.pid;
    }
    return null;
  } catch {
    return null;
  }
}

function lockDirectory(destination: string): string {
  return join(validateDestinationRoot(destination), ".deniz-skills", "lock");
}

function createHeldLock(lockPath: string): InstallerLock {
  const lock: InstallerLock = {
    path: lockPath,
    release(): void {
      if (!heldLocks.has(lock)) {
        return;
      }
      heldLocks.delete(lock);
      rmSync(lockPath, { recursive: true, force: true });
    },
  };
  heldLocks.add(lock);
  return lock;
}

function occupyLock(lockPath: string): InstallerLock {
  writeOwner(lockPath);
  return createHeldLock(lockPath);
}

function reclaimAbandonedLock(destination: string, lockPath: string): InstallerLock {
  inspectRecovery(destination);
  rmSync(lockPath, { recursive: true, force: true });
  try {
    mkdirSync(lockPath);
  } catch (error) {
    if (isEEXIST(error)) {
      throw new Error("Active installer lock; wait for that process to finish, then retry");
    }
    throw error;
  }
  return occupyLock(lockPath);
}

export function acquireInstallerLock(destination: string): InstallerLock {
  const root = validateDestinationRoot(destination);
  const skillsDir = join(root, ".deniz-skills");
  mkdirSync(skillsDir, { recursive: true });
  const lockPath = join(skillsDir, "lock");
  try {
    mkdirSync(lockPath);
  } catch (error) {
    if (!isEEXIST(error)) {
      throw error;
    }
    const pid = readOwnerPid(lockPath);
    if (pid !== null && processExists(pid)) {
      throw new Error(`Active installer lock held by process ${pid}; wait for that process to finish, then retry`);
    }
    return reclaimAbandonedLock(destination, lockPath);
  }
  return occupyLock(lockPath);
}

function requireHeldLock(lock: InstallerLock, destination: string): void {
  if (!heldLocks.has(lock)) {
    throw new Error("installer lock is not held");
  }
  if (resolve(lock.path) !== resolve(lockDirectory(destination))) {
    throw new Error("installer lock does not match Destination");
  }
  try {
    const stat = lstatSync(lock.path);
    if (stat.isSymbolicLink() || !stat.isDirectory()) {
      throw new Error("installer lock is not held");
    }
  } catch (error) {
    if (error instanceof Error && error.message === "installer lock is not held") {
      throw error;
    }
    throw new Error("installer lock is not held");
  }
}

function denizSkillsDir(destination: string, create: boolean): string | null {
  const root = validateDestinationRoot(destination);
  const dir = join(root, ".deniz-skills");
  try {
    const stat = lstatSync(dir);
    if (stat.isSymbolicLink()) {
      throw new Error(`${dir} must not be a symlink or junction`);
    }
    if (!stat.isDirectory()) {
      throw new Error(`${dir} must be an ordinary directory`);
    }
    return dir;
  } catch (error) {
    if (isENOENT(error)) {
      if (!create) {
        return null;
      }
      mkdirSync(dir, { recursive: true });
      return dir;
    }
    throw error;
  }
}

function listTransactionDirs(deniz: string): string[] {
  const found: string[] = [];
  for (const name of readdirSync(deniz)) {
    if (name === "lock") {
      continue;
    }
    const dir = join(deniz, name);
    const stat = lstatSync(dir);
    if (stat.isSymbolicLink() || !stat.isDirectory()) {
      continue;
    }
    const journalPath = join(dir, "journal.json");
    try {
      const journalStat = lstatSync(journalPath);
      if (!journalStat.isSymbolicLink() && journalStat.isFile()) {
        found.push(dir);
      }
    } catch (error) {
      if (isENOENT(error)) {
        continue;
      }
      throw error;
    }
  }
  return found;
}

function parseJournal(raw: string): TransactionJournal | null {
  let value: unknown;
  try {
    value = JSON.parse(raw) as unknown;
  } catch {
    return null;
  }
  if (!isRecord(value) || value.schemaVersion !== 1) {
    return null;
  }
  if (typeof value.transactionId !== "string" || value.transactionId.length === 0) {
    return null;
  }
  if (!isSha256(value.oldStateDigest) || !isSha256(value.newStateDigest)) {
    return null;
  }
  if (!Array.isArray(value.operations)) {
    return null;
  }
  if (value.phase !== "prepared" && value.phase !== "files-placed" && value.phase !== "state-committed") {
    return null;
  }
  return {
    schemaVersion: 1,
    transactionId: value.transactionId,
    oldStateDigest: value.oldStateDigest,
    newStateDigest: value.newStateDigest,
    operations: value.operations as PlanOperation[],
    phase: value.phase,
  };
}

function currentStateDigest(destination: string): Sha256 | null {
  try {
    return stateDigest(loadInstallState(destination));
  } catch {
    return null;
  }
}

function recoveryForDir(destination: string, transactionDir: string): RecoveryPlan {
  let raw: string;
  try {
    const journalStat = lstatSync(join(transactionDir, "journal.json"));
    if (journalStat.isSymbolicLink() || !journalStat.isFile()) {
      return { kind: "blocked", transactionDir, message: "transaction journal is not an ordinary file" };
    }
    raw = readFileSync(join(transactionDir, "journal.json"), "utf8");
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    return { kind: "blocked", transactionDir, message: `unreadable transaction journal: ${message}` };
  }
  const journal = parseJournal(raw);
  if (!journal) {
    return { kind: "blocked", transactionDir, message: "transaction journal is malformed" };
  }
  const digest = currentStateDigest(destination);
  if (digest === journal.oldStateDigest) {
    return { kind: "rollback", transactionDir, journal };
  }
  if (digest === journal.newStateDigest) {
    return { kind: "finalize", transactionDir, journal };
  }
  return {
    kind: "blocked",
    transactionDir,
    message: "Install-state digest matches neither journal digest; recovery is blocked",
  };
}

export function inspectRecovery(destination: string): RecoveryPlan | null {
  const deniz = denizSkillsDir(destination, false);
  if (!deniz) {
    return null;
  }
  const dirs = listTransactionDirs(deniz);
  if (dirs.length === 0) {
    return null;
  }
  if (dirs.length > 1) {
    return {
      kind: "blocked",
      transactionDir: deniz,
      message: "multiple installer transactions are present; recovery is blocked",
    };
  }
  const transactionDir = dirs[0];
  if (transactionDir === undefined) {
    return null;
  }
  return recoveryForDir(destination, transactionDir);
}

function identityMatches(left: FileIdentity, right: FileIdentity, platform: "posix" | "windows"): boolean {
  return left.sha256 === right.sha256 && (platform === "windows" || left.mode === right.mode);
}

function recordedIdentity(state: InstallState, path: string): FileIdentity | null {
  const owned = state.files[path];
  if (!owned) {
    return null;
  }
  return { sha256: owned.sha256, mode: owned.mode };
}

function refuseLinkOrDirectory(path: string, kind: string): never {
  throw new Error(`${path}: managed path must not be a ${kind}`);
}

function recheckOperation(
  destination: string,
  current: InstallState,
  operation: PlanOperation,
  platform: "posix" | "windows",
): void {
  validateManagedPath(destination, operation.path);
  const observed = observePath(destination, operation.path);
  if (observed.kind === "link" || observed.kind === "directory") {
    refuseLinkOrDirectory(operation.path, observed.kind);
  }
  if (operation.kind === "add") {
    if (observed.kind !== "absent") {
      throw new Error(`${operation.path} already exists and is unowned; delete or move it by hand, then retry`);
    }
    return;
  }
  if (operation.kind === "drop-missing-claim") {
    if (observed.kind !== "absent") {
      throw new Error(`${operation.path} was expected to be absent`);
    }
    return;
  }
  if (observed.kind !== "file") {
    throw new Error(
      `${operation.path} was modified locally; restore, move, or delete it by hand, then retry`,
    );
  }
  if (operation.kind === "chmod") {
    const expected: FileIdentity = { sha256: observed.identity.sha256, mode: operation.from };
    const recorded = recordedIdentity(current, operation.path);
    if (
      !recorded ||
      observed.identity.sha256 !== recorded.sha256 ||
      !identityMatches(observed.identity, expected, platform)
    ) {
      throw new Error(
        `${operation.path} was modified locally; restore, move, or delete it by hand, then retry`,
      );
    }
    return;
  }
  const recorded = recordedIdentity(current, operation.path);
  if (!recorded || !identityMatches(observed.identity, recorded, platform)) {
    throw new Error(`${operation.path} was modified locally; restore, move, or delete it by hand, then retry`);
  }
  if (operation.kind === "remove" && !identityMatches(observed.identity, operation.identity, platform)) {
    throw new Error(`${operation.path} was modified locally; restore, move, or delete it by hand, then retry`);
  }
}

function maybeInject(options: ApplyOptions | undefined, phase: ApplyPhase): void {
  if (options?.failAfter === phase) {
    throw new Error(`injected ${phase} failure`);
  }
}

function writeJournal(transactionDir: string, journal: TransactionJournal): void {
  writeFileSync(join(transactionDir, "journal.json"), `${JSON.stringify(journal, null, 2)}\n`);
}

function relativePathError(path: string): string | null {
  if (path.length === 0 || path.includes("\\") || path.includes("\0") || path.startsWith("/")) {
    return "path must be POSIX root-relative";
  }
  const parts = path.split("/");
  if (parts.some((part) => part.length === 0 || part === "." || part === ".." || (part.length === 2 && part[1] === ":"))) {
    return "path must not contain traversal or normalization segments";
  }
  const root = parts[0];
  if (!root || !NATIVE_ROOTS.has(root) || parts.length < 2) {
    return "path must be under skills/, commands/, or agents/";
  }
  return null;
}

function requireContained(root: string, candidate: string): string {
  const escaped = relative(root, candidate);
  if (escaped === "" || escaped.startsWith("..") || isAbsolute(escaped)) {
    throw new Error(`${candidate} escapes Destination ${root}`);
  }
  return candidate;
}

function bundleSourcePath(root: string, source: string): string {
  const error = relativePathError(source);
  if (error) {
    throw new Error(`${source}: ${error}`);
  }
  const candidate = join(root, ...source.split("/"));
  const escaped = relative(root, candidate);
  if (escaped === "" || escaped.startsWith("..") || escaped.includes("..")) {
    throw new Error(`${source} escapes bundle root`);
  }
  return candidate;
}

function applyMode(path: string, mode: FileMode): void {
  const stat = lstatSync(path);
  if (stat.isSymbolicLink()) {
    throw new Error(`${path}: managed path must not be a symlink or junction`);
  }
  chmodSync(path, mode === "100755" ? 0o755 : 0o644);
}

function ensureParents(destination: string, path: string, created: string[]): string {
  const parts = path.split("/");
  let current = destination;
  for (const [index, part] of parts.entries()) {
    if (index === parts.length - 1) {
      break;
    }
    current = join(current, part);
    requireContained(destination, current);
    try {
      const stat = lstatSync(current);
      if (stat.isSymbolicLink()) {
        throw new Error(`${path}: managed path must not be a symlink or junction`);
      }
      if (!stat.isDirectory()) {
        throw new Error(`${path}: ${current} is not a directory`);
      }
    } catch (error) {
      if (!isENOENT(error)) {
        throw error;
      }
      mkdirSync(current);
      created.push(current);
    }
  }
  return join(destination, ...parts);
}

function pruneEmptyParents(destination: string, path: string): void {
  const parts = path.split("/");
  for (let index = parts.length - 1; index > 1; index -= 1) {
    const dir = join(destination, ...parts.slice(0, index));
    let stat: Stats;
    try {
      stat = lstatSync(dir);
    } catch (error) {
      if (isENOENT(error)) {
        continue;
      }
      throw error;
    }
    if (stat.isSymbolicLink() || !stat.isDirectory()) {
      break;
    }
    if (readdirSync(dir).length > 0) {
      break;
    }
    rmdirSync(dir);
  }
}

function stagedFilePath(transactionDir: string, path: string): string {
  return join(transactionDir, "files", ...path.split("/"));
}

function backupFilePath(transactionDir: string, path: string): string {
  return join(transactionDir, "backup", ...path.split("/"));
}

function unlinkManagedFile(destination: string, path: string): void {
  const destPath = join(destination, ...path.split("/"));
  try {
    const stat = lstatSync(destPath);
    if (stat.isSymbolicLink()) {
      throw new Error(`${path}: managed path must not be a symlink or junction`);
    }
    if (stat.isFile()) {
      rmSync(destPath);
    }
  } catch (error) {
    if (!isENOENT(error)) {
      throw error;
    }
  }
}

function restoreBackup(destination: string, transactionDir: string, path: string, created: string[]): void {
  const backup = backupFilePath(transactionDir, path);
  if (!existsLstat(backup)) {
    return;
  }
  const destPath = validateManagedPath(destination, path);
  unlinkManagedFile(destination, path);
  ensureParents(destination, path, created);
  renameSync(backup, destPath);
}

function rollbackFiles(
  destination: string,
  transactionDir: string,
  operations: PlanOperation[],
  created: string[],
): void {
  for (const operation of [...operations].reverse()) {
    if (operation.kind === "add" || operation.kind === "replace") {
      const observed = observePath(destination, operation.path);
      if (observed.kind === "file" && observed.identity.sha256 === operation.identity.sha256) {
        unlinkManagedFile(destination, operation.path);
        pruneEmptyParents(destination, operation.path);
      }
    }
    if (operation.kind === "replace" || operation.kind === "remove") {
      restoreBackup(destination, transactionDir, operation.path, created);
    }
    if (operation.kind === "chmod") {
      const destPath = join(destination, ...operation.path.split("/"));
      if (existsLstat(destPath)) {
        applyMode(destPath, operation.from);
      }
    }
  }
  for (const dir of [...created].reverse()) {
    try {
      if (readdirSync(dir).length === 0) {
        rmdirSync(dir);
      }
    } catch (error) {
      if (!isENOENT(error)) {
        throw error;
      }
    }
  }
}

function restoreInstallState(destination: string, transactionDir: string): void {
  const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
  const backed = join(transactionDir, "backup-install.json");
  if (existsLstat(backed)) {
    if (existsLstat(installPath)) {
      rmSync(installPath);
    }
    renameSync(backed, installPath);
  }
}

function removeTransactionDir(transactionDir: string): void {
  rmSync(transactionDir, { recursive: true, force: true });
}

function stageBundleFile(transactionDir: string, bundles: Map<string, ModuleBundle>, operation: Extract<PlanOperation, { kind: "add" | "replace" }>): void {
  const bundle = bundles.get(operation.module);
  if (!bundle) {
    throw new Error(`${operation.module} is not a provided Module Bundle`);
  }
  const source = bundleSourcePath(bundle.root, operation.source);
  const stat = lstatSync(source);
  if (stat.isSymbolicLink() || !stat.isFile()) {
    throw new Error(`${operation.source}: bundle source must be an ordinary file`);
  }
  const bytes = readFileSync(source);
  if (hashBytes(bytes) !== operation.identity.sha256) {
    throw new Error(`${operation.source}: bundle file hash does not match the Plan`);
  }
  const staged = stagedFilePath(transactionDir, operation.path);
  mkdirSync(dirname(staged), { recursive: true });
  writeFileSync(staged, bytes);
  applyMode(staged, operation.identity.mode);
}

function commitInstallState(destination: string, transactionDir: string): void {
  const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
  const staged = join(transactionDir, "new-state.json");
  const backed = join(transactionDir, "backup-install.json");
  try {
    renameSync(staged, installPath);
    return;
  } catch {
    // Windows cannot rename over an existing file; move the old state aside first.
  }
  if (existsLstat(installPath)) {
    renameSync(installPath, backed);
  }
  try {
    renameSync(staged, installPath);
  } catch (error) {
    if (existsLstat(backed) && !existsLstat(installPath)) {
      try {
        renameSync(backed, installPath);
      } catch {
        // Leave the Destination for Recovery to inspect; the commit did not happen.
      }
    }
    throw error;
  }
}

export function applyPlan(
  lock: InstallerLock,
  destination: string,
  plan: Plan,
  bundles: Map<string, ModuleBundle>,
  options?: ApplyOptions,
): void {
  requireHeldLock(lock, destination);
  validateDestinationRoot(destination);
  if (plan.findings.length > 0) {
    throw new Error("plan has findings; refuse to apply");
  }
  if (inspectRecovery(destination)) {
    throw new Error("interrupted transaction requires Recovery; apply Recovery only, then retry");
  }

  const current = loadInstallState(destination);
  if (plan.operations.length === 0 && stateDigest(current) === stateDigest(plan.nextState)) {
    return;
  }

  const deniz = denizSkillsDir(destination, true);
  if (!deniz) {
    throw new Error("Destination .deniz-skills directory could not be created");
  }

  const transactionId = randomUUID();
  const transactionDir = join(deniz, `txn-${transactionId}`);
  mkdirSync(transactionDir);
  const created: string[] = [];
  let committed = false;
  const journal: TransactionJournal = {
    schemaVersion: 1,
    transactionId,
    oldStateDigest: stateDigest(current),
    newStateDigest: stateDigest(plan.nextState),
    operations: plan.operations,
    phase: "prepared",
  };

  try {
    const installPath = join(deniz, "install.json");
    const oldBytes = existsLstat(installPath) ? readFileSync(installPath, "utf8") : serializeInstallState(EMPTY_INSTALL_STATE);
    writeFileSync(join(transactionDir, "old-state.json"), oldBytes);
    writeFileSync(join(transactionDir, "new-state.json"), serializeInstallState(plan.nextState));
    for (const operation of plan.operations) {
      if (operation.kind === "add" || operation.kind === "replace") {
        stageBundleFile(transactionDir, bundles, operation);
      }
    }
    writeJournal(transactionDir, journal);

    validateDestinationRoot(destination);
    for (const operation of plan.operations) {
      options?.beforeOperation?.(operation);
      recheckOperation(destination, current, operation, plan.request.platform);
    }

    for (const operation of plan.operations) {
      if (operation.kind !== "replace" && operation.kind !== "remove") {
        continue;
      }
      const destPath = validateManagedPath(destination, operation.path);
      const backup = backupFilePath(transactionDir, operation.path);
      mkdirSync(dirname(backup), { recursive: true });
      renameSync(destPath, backup);
    }
    maybeInject(options, "after-backup");

    for (const operation of plan.operations) {
      if (operation.kind !== "add" && operation.kind !== "replace") {
        continue;
      }
      validateManagedPath(destination, operation.path);
      const destPath = ensureParents(destination, operation.path, created);
      renameSync(stagedFilePath(transactionDir, operation.path), destPath);
    }
    for (const operation of plan.operations) {
      if (operation.kind === "chmod") {
        applyMode(join(destination, ...operation.path.split("/")), operation.to);
      }
    }
    journal.phase = "files-placed";
    writeJournal(transactionDir, journal);
    maybeInject(options, "after-place");

    commitInstallState(destination, transactionDir);
    committed = true;
    journal.phase = "state-committed";
    writeJournal(transactionDir, journal);
    maybeInject(options, "after-state-commit");

    for (const operation of plan.operations) {
      if (operation.kind === "remove") {
        pruneEmptyParents(destination, operation.path);
      }
    }
    removeTransactionDir(transactionDir);
  } catch (error) {
    if (!committed) {
      try {
        rollbackFiles(destination, transactionDir, plan.operations, created);
        restoreInstallState(destination, transactionDir);
        removeTransactionDir(transactionDir);
      } catch (rollbackError) {
        const first = error instanceof Error ? error.message : String(error);
        const second = rollbackError instanceof Error ? rollbackError.message : String(rollbackError);
        throw new Error(`${first}; rollback also failed: ${second}`);
      }
    }
    throw error;
  }
}

export function applyRecovery(lock: InstallerLock, destination: string, recovery: RecoveryPlan): void {
  requireHeldLock(lock, destination);
  validateDestinationRoot(destination);
  const current = inspectRecovery(destination);
  if (!current) {
    throw new Error("no installer transaction requires Recovery");
  }
  if (current.kind !== recovery.kind || current.transactionDir !== recovery.transactionDir) {
    throw new Error("Recovery Plan is stale; inspect Recovery again");
  }
  if (current.kind === "blocked") {
    throw new Error(current.message);
  }
  if (current.kind === "finalize") {
    removeTransactionDir(current.transactionDir);
    return;
  }
  rollbackFiles(destination, current.transactionDir, current.journal.operations, []);
  const oldStatePath = join(current.transactionDir, "old-state.json");
  if (existsLstat(oldStatePath) && currentStateDigest(destination) !== current.journal.oldStateDigest) {
    const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
    writeFileSync(installPath, readFileSync(oldStatePath));
  }
  restoreInstallState(destination, current.transactionDir);
  removeTransactionDir(current.transactionDir);
}
