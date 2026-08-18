import { randomUUID } from "node:crypto";
import {
  chmodSync,
  closeSync,
  fsyncSync,
  lstatSync,
  mkdirSync,
  openSync,
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
  parseInstallState,
  serializeInstallState,
  stateDigest,
  validateDestinationRoot,
  validateManagedPath,
  type InstallState,
} from "./opencode-install-state.ts";

export interface InstallerLock {
  path: string;
  token: string;
  release(): void;
}

export type ApplyPhase = "after-backup" | "after-place" | "after-state-commit";
export type CrashPoint = ApplyPhase | "after-state-aside";

export interface ApplyIo {
  deviceId?(path: string): number;
}

export interface ApplyOptions {
  failAfter?: ApplyPhase;
  crashAfter?: CrashPoint;
  beforeOperation?: (operation: PlanOperation) => void;
  io?: ApplyIo;
  forceWindowsStateReplace?: boolean;
}

export interface AcquireLockOptions {
  recover?: boolean;
}

export type AppliedAction = "backed-up" | "placed" | "chmodded";

export interface AppliedMutation {
  path: string;
  action: AppliedAction;
}

export interface TransactionJournal {
  schemaVersion: 1;
  transactionId: string;
  oldStateDigest: Sha256;
  newStateDigest: Sha256;
  operations: PlanOperation[];
  phase: "prepared" | "files-placed" | "state-committed";
  applied: AppliedMutation[];
  createdDirectories: string[];
  stateAside: boolean;
}

export type RecoveryPlan =
  | { kind: "rollback" | "finalize"; transactionDir: string; journal: TransactionJournal }
  | { kind: "blocked"; transactionDir: string; message: string };

interface LockOwner {
  pid: number;
  startedAt: string;
  token: string;
}

const SHA256 = /^sha256:[a-f0-9]{64}$/;
const NATIVE_ROOTS = new Set(["agents", "commands", "skills"]);
const JOURNAL_KEYS = new Set([
  "applied",
  "createdDirectories",
  "newStateDigest",
  "oldStateDigest",
  "operations",
  "phase",
  "schemaVersion",
  "stateAside",
  "transactionId",
]);

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isSha256(value: unknown): value is Sha256 {
  return typeof value === "string" && SHA256.test(value);
}

function isFileMode(value: unknown): value is FileMode {
  return value === "100644" || value === "100755";
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

function isWindowsReplaceError(error: unknown): boolean {
  return isEPERM(error) || isEEXIST(error);
}

function isEXDEV(error: unknown): boolean {
  return error instanceof Error && "code" in error && (error as NodeJS.ErrnoException).code === "EXDEV";
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

function fsyncDirectory(dir: string): void {
  try {
    const fd = openSync(dir, "r");
    try {
      fsyncSync(fd);
    } finally {
      closeSync(fd);
    }
  } catch {
    // Directory fsync is not supported on every host.
  }
}

function writeFlushed(path: string, bytes: string | Uint8Array, flag = "w"): void {
  writeFileSync(path, bytes, { flag, flush: true });
}

function atomicReplaceFile(path: string, bytes: string | Uint8Array): void {
  const tmp = `${path}.${randomUUID()}.tmp`;
  writeFlushed(tmp, bytes);
  try {
    renameSync(tmp, path);
  } catch (error) {
    if (!isWindowsReplaceError(error)) {
      rmSync(tmp, { force: true });
      throw error;
    }
    rmSync(path, { force: true });
    renameSync(tmp, path);
  }
  fsyncDirectory(dirname(path));
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

function isCreatedDirectoryPath(value: unknown): value is string {
  if (typeof value !== "string" || value.length === 0 || value.includes("\\") || value.includes("\0")) {
    return false;
  }
  const parts = value.split("/");
  if (parts.some((part) => part.length === 0 || part === "." || part === "..")) {
    return false;
  }
  const root = parts[0];
  return root !== undefined && NATIVE_ROOTS.has(root);
}

function requireContained(root: string, candidate: string): string {
  const escaped = relative(root, candidate);
  if (escaped === "" || escaped.startsWith("..") || isAbsolute(escaped)) {
    throw new Error(`${candidate} escapes Destination ${root}`);
  }
  return candidate;
}

function requireOrdinaryDir(path: string, label: string): Stats {
  const stat = lstatSync(path);
  if (stat.isSymbolicLink()) {
    throw new Error(`${label} must not be a symlink or junction: ${path}`);
  }
  if (!stat.isDirectory()) {
    throw new Error(`${label} must be an ordinary directory: ${path}`);
  }
  return stat;
}

function requireOrdinaryFile(path: string, label: string): Stats {
  const stat = lstatSync(path);
  if (stat.isSymbolicLink()) {
    throw new Error(`${label} must not be a symlink or junction: ${path}`);
  }
  if (!stat.isFile()) {
    throw new Error(`${label} must be an ordinary file: ${path}`);
  }
  return stat;
}

function ensureDestinationTree(destination: string): { root: string; deniz: string } {
  const root = validateDestinationRoot(destination);
  if (!existsLstat(root)) {
    mkdirSync(root, { recursive: true });
  }
  requireOrdinaryDir(root, "Destination");
  const deniz = join(root, ".deniz-skills");
  if (!existsLstat(deniz)) {
    mkdirSync(deniz);
  }
  requireOrdinaryDir(deniz, ".deniz-skills");
  return { root, deniz };
}

function ownerFile(lockPath: string): string {
  return join(lockPath, "owner.json");
}

function writeOwner(lockPath: string, owner: LockOwner): void {
  writeFlushed(ownerFile(lockPath), `${JSON.stringify(owner)}\n`, "wx");
}

function readOwner(lockPath: string): LockOwner | null {
  try {
    requireOrdinaryFile(ownerFile(lockPath), "lock owner");
    const value: unknown = JSON.parse(readFileSync(ownerFile(lockPath), "utf8"));
    if (
      !isRecord(value) ||
      typeof value.pid !== "number" ||
      typeof value.startedAt !== "string" ||
      typeof value.token !== "string" ||
      value.token.length === 0
    ) {
      return null;
    }
    return { pid: value.pid, startedAt: value.startedAt, token: value.token };
  } catch {
    return null;
  }
}

function lockDirectory(destination: string): string {
  return join(validateDestinationRoot(destination), ".deniz-skills", "lock");
}

function createHeldLock(lockPath: string, token: string): InstallerLock {
  const lock: InstallerLock = {
    path: lockPath,
    token,
    release(): void {
      const owner = readOwner(lockPath);
      if (!owner || owner.token !== token) {
        return;
      }
      try {
        rmSync(lockPath, { recursive: true });
      } catch (error) {
        if (isENOENT(error)) {
          return;
        }
        const message = error instanceof Error ? error.message : String(error);
        throw new Error(`failed to release installer lock: ${message}`);
      }
    },
  };
  return lock;
}

function occupyNewLock(lockPath: string): InstallerLock {
  const token = randomUUID();
  writeOwner(lockPath, { pid: process.pid, startedAt: new Date().toISOString(), token });
  return createHeldLock(lockPath, token);
}

function reclaimAbandonedLock(lockPath: string, oldToken: string): InstallerLock {
  const moved = `${lockPath}.reclaimed-${oldToken}`;
  try {
    renameSync(lockPath, moved);
  } catch {
    throw new Error("Active installer lock; wait for that process to finish, then retry");
  }
  rmSync(moved, { recursive: true, force: true });
  mkdirSync(lockPath);
  requireOrdinaryDir(lockPath, "installer lock");
  return occupyNewLock(lockPath);
}

export function acquireInstallerLock(destination: string, options: AcquireLockOptions = {}): InstallerLock {
  const { deniz } = ensureDestinationTree(destination);
  const lockPath = join(deniz, "lock");
  try {
    mkdirSync(lockPath);
  } catch (error) {
    if (!isEEXIST(error)) {
      throw error;
    }
    requireOrdinaryDir(lockPath, "installer lock");
    const owner = readOwner(lockPath);
    if (owner && processExists(owner.pid)) {
      throw new Error(`Active installer lock held by process ${owner.pid}; wait for that process to finish, then retry`);
    }
    const recovery = inspectRecovery(destination);
    if (recovery && options.recover !== true) {
      throw new Error("interrupted transaction requires Recovery; acquire the lock for Recovery, then retry");
    }
    return reclaimAbandonedLock(lockPath, owner?.token ?? `unknown-${randomUUID()}`);
  }
  requireOrdinaryDir(lockPath, "installer lock");
  return occupyNewLock(lockPath);
}

function requireHeldLock(lock: InstallerLock, destination: string): void {
  if (resolve(lock.path) !== resolve(lockDirectory(destination))) {
    throw new Error("installer lock does not match Destination");
  }
  try {
    requireOrdinaryDir(lock.path, "installer lock");
  } catch {
    throw new Error("installer lock is not held");
  }
  const owner = readOwner(lock.path);
  if (!owner || owner.token !== lock.token) {
    throw new Error("installer lock token does not match the on-disk owner");
  }
}

function blocked(transactionDir: string, message: string): RecoveryPlan {
  return { kind: "blocked", transactionDir, message };
}

function parseIdentity(value: unknown): FileIdentity | null {
  if (!isRecord(value) || !isSha256(value.sha256) || !isFileMode(value.mode)) {
    return null;
  }
  if (Object.keys(value).some((key) => key !== "mode" && key !== "sha256")) {
    return null;
  }
  return { sha256: value.sha256, mode: value.mode };
}

function parsePlanOperation(value: unknown): PlanOperation | null {
  if (!isRecord(value) || typeof value.path !== "string" || typeof value.module !== "string") {
    return null;
  }
  if (relativePathError(value.path) || value.module.length === 0) {
    return null;
  }
  if (value.kind === "add" || value.kind === "replace") {
    if (typeof value.source !== "string" || relativePathError(value.source)) {
      return null;
    }
    const identity = parseIdentity(value.identity);
    if (!identity) {
      return null;
    }
    return { kind: value.kind, path: value.path, module: value.module, source: value.source, identity };
  }
  if (value.kind === "remove") {
    const identity = parseIdentity(value.identity);
    if (!identity) {
      return null;
    }
    return { kind: "remove", path: value.path, module: value.module, identity };
  }
  if (value.kind === "chmod") {
    if (!isFileMode(value.from) || !isFileMode(value.to)) {
      return null;
    }
    return { kind: "chmod", path: value.path, module: value.module, from: value.from, to: value.to };
  }
  if (value.kind === "drop-missing-claim") {
    return { kind: "drop-missing-claim", path: value.path, module: value.module };
  }
  return null;
}

function parseApplied(value: unknown): AppliedMutation | null {
  if (!isRecord(value) || typeof value.path !== "string") {
    return null;
  }
  if (relativePathError(value.path)) {
    return null;
  }
  if (value.action !== "backed-up" && value.action !== "placed" && value.action !== "chmodded") {
    return null;
  }
  return { path: value.path, action: value.action };
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
  for (const key of Object.keys(value)) {
    if (!JOURNAL_KEYS.has(key)) {
      return null;
    }
  }
  if (typeof value.transactionId !== "string" || value.transactionId.length === 0) {
    return null;
  }
  if (!isSha256(value.oldStateDigest) || !isSha256(value.newStateDigest)) {
    return null;
  }
  if (value.phase !== "prepared" && value.phase !== "files-placed" && value.phase !== "state-committed") {
    return null;
  }
  if (typeof value.stateAside !== "boolean" || !Array.isArray(value.operations) || !Array.isArray(value.applied)) {
    return null;
  }
  if (!Array.isArray(value.createdDirectories)) {
    return null;
  }
  const operations: PlanOperation[] = [];
  for (const item of value.operations) {
    const parsed = parsePlanOperation(item);
    if (!parsed) {
      return null;
    }
    operations.push(parsed);
  }
  const applied: AppliedMutation[] = [];
  for (const item of value.applied) {
    const parsed = parseApplied(item);
    if (!parsed) {
      return null;
    }
    applied.push(parsed);
  }
  const createdDirectories: string[] = [];
  for (const item of value.createdDirectories) {
    if (!isCreatedDirectoryPath(item)) {
      return null;
    }
    createdDirectories.push(item);
  }
  return {
    schemaVersion: 1,
    transactionId: value.transactionId,
    oldStateDigest: value.oldStateDigest,
    newStateDigest: value.newStateDigest,
    operations,
    phase: value.phase,
    applied,
    createdDirectories,
    stateAside: value.stateAside,
  };
}

function digestOfStateFile(path: string): Sha256 | null {
  try {
    requireOrdinaryFile(path, "Install state");
    return stateDigest(parseInstallState(readFileSync(path, "utf8")));
  } catch {
    return null;
  }
}

function classifyRecovery(destination: string, transactionDir: string, journal: TransactionJournal): RecoveryPlan {
  const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
  try {
    const stat = lstatSync(installPath);
    if (stat.isSymbolicLink() || !stat.isFile()) {
      return blocked(transactionDir, "Install state must be an ordinary file");
    }
    const digest = digestOfStateFile(installPath);
    if (digest === journal.oldStateDigest) {
      return { kind: "rollback", transactionDir, journal };
    }
    if (digest === journal.newStateDigest) {
      return { kind: "finalize", transactionDir, journal };
    }
    return blocked(transactionDir, "Install-state digest matches neither journal digest; recovery is blocked");
  } catch (error) {
    if (!isENOENT(error)) {
      return blocked(transactionDir, "unreadable Install state");
    }
  }
  const backed = join(transactionDir, "backup-install.json");
  if (existsLstat(backed)) {
    const asideDigest = digestOfStateFile(backed);
    if (asideDigest === journal.oldStateDigest) {
      return { kind: "rollback", transactionDir, journal };
    }
  }
  if (journal.oldStateDigest === stateDigest(EMPTY_INSTALL_STATE)) {
    return { kind: "rollback", transactionDir, journal };
  }
  return blocked(transactionDir, "Install-state digest matches neither journal digest; recovery is blocked");
}

export function inspectRecovery(destination: string): RecoveryPlan | null {
  const root = validateDestinationRoot(destination);
  const deniz = join(root, ".deniz-skills");
  if (!existsLstat(deniz)) {
    return null;
  }
  requireOrdinaryDir(deniz, ".deniz-skills");
  const txns: string[] = [];
  for (const name of readdirSync(deniz)) {
    const entry = join(deniz, name);
    const stat = lstatSync(entry);
    if (name === "install.json") {
      if (stat.isSymbolicLink() || !stat.isFile()) {
        return blocked(deniz, "install.json must be an ordinary file");
      }
      continue;
    }
    if (name === "lock") {
      if (stat.isSymbolicLink() || !stat.isDirectory()) {
        return blocked(deniz, "lock must be an ordinary directory");
      }
      continue;
    }
    if (name.startsWith("txn-")) {
      if (stat.isSymbolicLink() || !stat.isDirectory()) {
        return blocked(entry, "transaction debris is not an ordinary directory");
      }
      txns.push(entry);
      continue;
    }
    return blocked(entry, `unresolved transaction debris ${JSON.stringify(name)}`);
  }
  if (txns.length === 0) {
    return null;
  }
  if (txns.length > 1) {
    return blocked(deniz, "multiple installer transactions are present; recovery is blocked");
  }
  const transactionDir = txns[0];
  if (transactionDir === undefined) {
    return null;
  }
  const journalPath = join(transactionDir, "journal.json");
  if (!existsLstat(journalPath)) {
    return blocked(transactionDir, "transaction journal is missing");
  }
  try {
    requireOrdinaryFile(journalPath, "transaction journal");
  } catch {
    return blocked(transactionDir, "transaction journal is not an ordinary file");
  }
  const journal = parseJournal(readFileSync(journalPath, "utf8"));
  if (!journal) {
    return blocked(transactionDir, "transaction journal is malformed");
  }
  return classifyRecovery(destination, transactionDir, journal);
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

function recheckOperation(
  destination: string,
  current: InstallState,
  operation: PlanOperation,
  platform: "posix" | "windows",
  expectAbsent = false,
): void {
  validateManagedPath(destination, operation.path);
  const observed = observePath(destination, operation.path);
  if (observed.kind === "link" || observed.kind === "directory") {
    throw new Error(`${operation.path}: managed path must not be a ${observed.kind}`);
  }
  if (expectAbsent || operation.kind === "add" || operation.kind === "drop-missing-claim") {
    if (observed.kind !== "absent") {
      throw new Error(
        operation.kind === "add"
          ? `${operation.path} already exists and is unowned; delete or move it by hand, then retry`
          : `${operation.path} was expected to be absent`,
      );
    }
    return;
  }
  if (observed.kind !== "file") {
    throw new Error(`${operation.path} was modified locally; restore, move, or delete it by hand, then retry`);
  }
  if (operation.kind === "chmod") {
    const recorded = recordedIdentity(current, operation.path);
    if (!recorded || observed.identity.sha256 !== recorded.sha256 || !identityMatches(observed.identity, { sha256: recorded.sha256, mode: operation.from }, platform)) {
      throw new Error(`${operation.path} was modified locally; restore, move, or delete it by hand, then retry`);
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

function throwIfFail(options: ApplyOptions | undefined, phase: ApplyPhase): void {
  if (options?.failAfter === phase) {
    throw new Error(`injected ${phase} failure`);
  }
}

function throwIfCrash(options: ApplyOptions | undefined, point: CrashPoint): void {
  if (options?.crashAfter === point) {
    throw new Error(`injected crash ${point}`);
  }
}

function isInjectedCrash(error: unknown): boolean {
  return error instanceof Error && error.message.startsWith("injected crash ");
}

function writeJournal(transactionDir: string, journal: TransactionJournal): void {
  atomicReplaceFile(join(transactionDir, "journal.json"), `${JSON.stringify(journal, null, 2)}\n`);
}

function bundleSourcePath(root: string, source: string): string {
  const error = relativePathError(source);
  if (error) {
    throw new Error(`${source}: ${error}`);
  }
  const candidate = join(root, ...source.split("/"));
  const escaped = relative(root, candidate);
  if (escaped === "" || escaped.startsWith("..")) {
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

function posixJoin(parts: string[]): string {
  return parts.join("/");
}

function ensureParents(destination: string, path: string, created: string[]): string {
  const parts = path.split("/");
  let current = destination;
  const walked: string[] = [];
  for (const [index, part] of parts.entries()) {
    if (index === parts.length - 1) {
      break;
    }
    current = join(current, part);
    walked.push(part);
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
      const relativeDir = posixJoin(walked);
      if (!created.includes(relativeDir)) {
        created.push(relativeDir);
      }
    }
  }
  return join(destination, ...parts);
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
  requireOrdinaryFile(backup, `backup of ${path}`);
  validateManagedPath(destination, path);
  unlinkManagedFile(destination, path);
  const destPath = ensureParents(destination, path, created);
  renameSync(backup, destPath);
}

function pruneCreatedDirectories(destination: string, created: string[]): void {
  for (const dir of [...created].reverse()) {
    const parts = dir.split("/");
    if (parts.length <= 1) {
      continue;
    }
    const abs = join(destination, ...parts);
    try {
      const stat = lstatSync(abs);
      if (stat.isSymbolicLink() || !stat.isDirectory()) {
        continue;
      }
      if (readdirSync(abs).length === 0) {
        rmdirSync(abs);
      }
    } catch (error) {
      if (!isENOENT(error)) {
        throw error;
      }
    }
  }
}

function operationFor(journal: TransactionJournal, path: string, kinds: PlanOperation["kind"][]): PlanOperation | undefined {
  return journal.operations.find((operation) => operation.path === path && kinds.includes(operation.kind));
}

function rollbackApplied(destination: string, transactionDir: string, journal: TransactionJournal): void {
  for (const mutation of [...journal.applied].reverse()) {
    if (mutation.action === "placed") {
      const operation = operationFor(journal, mutation.path, ["add", "replace"]);
      if (operation && (operation.kind === "add" || operation.kind === "replace")) {
        const observed = observePath(destination, mutation.path);
        if (observed.kind === "file" && observed.identity.sha256 === operation.identity.sha256) {
          unlinkManagedFile(destination, mutation.path);
        }
      }
      continue;
    }
    if (mutation.action === "backed-up") {
      restoreBackup(destination, transactionDir, mutation.path, []);
      continue;
    }
    const operation = operationFor(journal, mutation.path, ["chmod"]);
    if (operation && operation.kind === "chmod") {
      const destPath = join(destination, ...mutation.path.split("/"));
      if (existsLstat(destPath)) {
        applyMode(destPath, operation.from);
      }
    }
  }
  const backedState = join(transactionDir, "backup-install.json");
  const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
  if (existsLstat(backedState)) {
    requireOrdinaryFile(backedState, "backed-up Install state");
    if (existsLstat(installPath)) {
      requireOrdinaryFile(installPath, "Install state");
      rmSync(installPath);
    }
    renameSync(backedState, installPath);
  }
  pruneCreatedDirectories(destination, journal.createdDirectories);
}

function verifyRollbackComplete(destination: string, journal: TransactionJournal): void {
  const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
  if (existsLstat(installPath)) {
    if (digestOfStateFile(installPath) !== journal.oldStateDigest) {
      throw new Error("rollback did not restore the old Install state");
    }
    return;
  }
  if (journal.oldStateDigest !== stateDigest(EMPTY_INSTALL_STATE)) {
    throw new Error("rollback did not restore the old Install state");
  }
}

function verifyFinalizeComplete(destination: string, journal: TransactionJournal): void {
  const digest = digestOfStateFile(join(validateDestinationRoot(destination), ".deniz-skills", "install.json"));
  if (digest !== journal.newStateDigest) {
    throw new Error("finalize refused: Install state is not the committed journal digest");
  }
  for (const mutation of journal.applied) {
    if (mutation.action !== "placed") {
      continue;
    }
    const operation = operationFor(journal, mutation.path, ["add", "replace"]);
    if (!operation || (operation.kind !== "add" && operation.kind !== "replace")) {
      throw new Error(`finalize refused: missing place operation for ${mutation.path}`);
    }
    const observed = observePath(destination, mutation.path);
    if (observed.kind !== "file" || observed.identity.sha256 !== operation.identity.sha256) {
      throw new Error(`finalize refused: ${mutation.path} does not match the committed Plan`);
    }
  }
}

function requireBackupEvidence(transactionDir: string, journal: TransactionJournal): void {
  for (const mutation of journal.applied) {
    if (mutation.action !== "backed-up") {
      continue;
    }
    requireOrdinaryFile(backupFilePath(transactionDir, mutation.path), `backup of ${mutation.path}`);
  }
}

function removeTransactionDir(transactionDir: string): void {
  requireOrdinaryDir(transactionDir, "transaction");
  rmSync(transactionDir, { recursive: true, force: true });
}

function stageBundleFile(
  transactionDir: string,
  bundles: Map<string, ModuleBundle>,
  operation: Extract<PlanOperation, { kind: "add" | "replace" }>,
): void {
  const bundle = bundles.get(operation.module);
  if (!bundle) {
    throw new Error(`${operation.module} is not a provided Module Bundle`);
  }
  const source = bundleSourcePath(bundle.root, operation.source);
  requireOrdinaryFile(source, "bundle source");
  const bytes = readFileSync(source);
  if (hashBytes(bytes) !== operation.identity.sha256) {
    throw new Error(`${operation.source}: bundle file hash does not match the Plan`);
  }
  const staged = stagedFilePath(transactionDir, operation.path);
  mkdirSync(dirname(staged), { recursive: true });
  writeFlushed(staged, bytes);
  applyMode(staged, operation.identity.mode);
}

function deviceIdOf(path: string, io: ApplyIo | undefined): number {
  if (io?.deviceId) {
    return io.deviceId(path);
  }
  return lstatSync(path).dev;
}

function requireSameDevice(left: string, right: string, io: ApplyIo | undefined): void {
  if (deviceIdOf(left, io) !== deviceIdOf(right, io)) {
    throw new Error(`EXDEV: ${right} is not on the same filesystem as ${left}`);
  }
}

function requireDestinationTopology(destination: string, deniz: string, io: ApplyIo | undefined): void {
  requireSameDevice(destination, deniz, io);
  for (const native of NATIVE_ROOTS) {
    const path = join(destination, native);
    if (existsLstat(path)) {
      requireOrdinaryDir(path, native);
      requireSameDevice(destination, path, io);
    }
  }
}

function renameOrThrow(from: string, to: string): void {
  try {
    renameSync(from, to);
  } catch (error) {
    if (isEXDEV(error)) {
      throw new Error(`EXDEV: ${to} is not on the same filesystem as ${from}`);
    }
    throw error;
  }
}

function commitInstallState(
  destination: string,
  transactionDir: string,
  journal: TransactionJournal,
  options: ApplyOptions | undefined,
): void {
  const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
  const staged = join(transactionDir, "new-state.json");
  const backed = join(transactionDir, "backup-install.json");
  requireOrdinaryFile(staged, "staged Install state");
  if (options?.forceWindowsStateReplace !== true) {
    try {
      renameOrThrow(staged, installPath);
      return;
    } catch (error) {
      if (!isWindowsReplaceError(error)) {
        throw error;
      }
    }
  }
  journal.stateAside = true;
  writeJournal(transactionDir, journal);
  if (existsLstat(installPath)) {
    requireOrdinaryFile(installPath, "Install state");
    renameOrThrow(installPath, backed);
  }
  throwIfCrash(options, "after-state-aside");
  renameOrThrow(staged, installPath);
}

export function applyPlan(
  lock: InstallerLock,
  destination: string,
  plan: Plan,
  bundles: Map<string, ModuleBundle>,
  options?: ApplyOptions,
): void {
  requireHeldLock(lock, destination);
  const { root, deniz } = ensureDestinationTree(destination);
  if (plan.findings.length > 0) {
    throw new Error("plan has findings; refuse to apply");
  }
  if (inspectRecovery(destination)) {
    throw new Error("interrupted transaction requires Recovery; apply Recovery only, then retry");
  }

  requireHeldLock(lock, destination);
  const current = loadInstallState(destination);
  if (plan.operations.length === 0 && stateDigest(current) === stateDigest(plan.nextState)) {
    return;
  }

  requireDestinationTopology(root, deniz, options?.io);
  const transactionId = randomUUID();
  const transactionDir = join(deniz, `txn-${transactionId}`);
  mkdirSync(transactionDir);
  requireOrdinaryDir(transactionDir, "transaction");
  requireSameDevice(root, transactionDir, options?.io);

  let committed = false;
  const journal: TransactionJournal = {
    schemaVersion: 1,
    transactionId,
    oldStateDigest: stateDigest(current),
    newStateDigest: stateDigest(plan.nextState),
    operations: plan.operations,
    phase: "prepared",
    applied: [],
    createdDirectories: [],
    stateAside: false,
  };

  try {
    const installPath = join(deniz, "install.json");
    const oldBytes = existsLstat(installPath)
      ? readFileSync(installPath)
      : Buffer.from(serializeInstallState(EMPTY_INSTALL_STATE));
    if (existsLstat(installPath)) {
      requireOrdinaryFile(installPath, "Install state");
    }
    writeFlushed(join(transactionDir, "old-state.json"), oldBytes);
    writeFlushed(join(transactionDir, "new-state.json"), serializeInstallState(plan.nextState));
    for (const operation of plan.operations) {
      if (operation.kind === "add" || operation.kind === "replace") {
        stageBundleFile(transactionDir, bundles, operation);
      }
    }
    writeJournal(transactionDir, journal);

    for (const operation of plan.operations) {
      if (operation.kind !== "replace" && operation.kind !== "remove") {
        continue;
      }
      requireHeldLock(lock, destination);
      options?.beforeOperation?.(operation);
      recheckOperation(destination, current, operation, plan.request.platform);
      const destPath = validateManagedPath(destination, operation.path);
      requireSameDevice(root, destPath, options?.io);
      const backup = backupFilePath(transactionDir, operation.path);
      mkdirSync(dirname(backup), { recursive: true });
      renameOrThrow(destPath, backup);
      journal.applied.push({ path: operation.path, action: "backed-up" });
      writeJournal(transactionDir, journal);
    }
    throwIfFail(options, "after-backup");
    throwIfCrash(options, "after-backup");

    for (const operation of plan.operations) {
      if (operation.kind !== "add" && operation.kind !== "replace") {
        continue;
      }
      requireHeldLock(lock, destination);
      if (operation.kind === "add") {
        options?.beforeOperation?.(operation);
      }
      recheckOperation(destination, current, operation, plan.request.platform, true);
      validateManagedPath(destination, operation.path);
      const destPath = ensureParents(destination, operation.path, journal.createdDirectories);
      requireSameDevice(root, dirname(destPath), options?.io);
      renameOrThrow(stagedFilePath(transactionDir, operation.path), destPath);
      journal.applied.push({ path: operation.path, action: "placed" });
      writeJournal(transactionDir, journal);
    }
    for (const operation of plan.operations) {
      if (operation.kind !== "chmod") {
        continue;
      }
      requireHeldLock(lock, destination);
      options?.beforeOperation?.(operation);
      recheckOperation(destination, current, operation, plan.request.platform);
      applyMode(join(destination, ...operation.path.split("/")), operation.to);
      journal.applied.push({ path: operation.path, action: "chmodded" });
      writeJournal(transactionDir, journal);
    }
    journal.phase = "files-placed";
    writeJournal(transactionDir, journal);
    throwIfFail(options, "after-place");
    throwIfCrash(options, "after-place");

    requireHeldLock(lock, destination);
    if (stateDigest(loadInstallState(destination)) !== journal.oldStateDigest) {
      throw new Error("Install-state digest changed before commit");
    }
    commitInstallState(destination, transactionDir, journal, options);
    committed = true;
    journal.phase = "state-committed";
    journal.stateAside = false;
    writeJournal(transactionDir, journal);
    throwIfFail(options, "after-state-commit");
    throwIfCrash(options, "after-state-commit");

    pruneCreatedDirectories(root, journal.createdDirectories);
    removeTransactionDir(transactionDir);
  } catch (error) {
    if (!committed && !isInjectedCrash(error)) {
      try {
        rollbackApplied(destination, transactionDir, journal);
        pruneCreatedDirectories(destination, journal.createdDirectories);
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
  ensureDestinationTree(destination);
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
    verifyFinalizeComplete(destination, current.journal);
    removeTransactionDir(current.transactionDir);
    return;
  }
  requireBackupEvidence(current.transactionDir, current.journal);
  rollbackApplied(destination, current.transactionDir, current.journal);
  verifyRollbackComplete(destination, current.journal);
  removeTransactionDir(current.transactionDir);
}
