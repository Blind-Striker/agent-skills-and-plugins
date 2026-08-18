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
import { ordinalCompare } from "./order.ts";

export interface InstallerLock {
  path: string;
  token: string;
  release(): void;
}

export type ApplyPhase = "after-backup" | "after-place" | "after-state-commit";
export type CrashPoint =
  | ApplyPhase
  | "after-state-aside"
  | "during-transaction-candidate"
  | "before-initial-staging"
  | "during-initial-staging";
export type SyscallName = "backup" | "place" | "chmod" | "state-aside" | "state-commit";

export interface ApplyIo {
  deviceId?(path: string): number;
  rmSync?(path: string): void;
  beforeReclaimRename?: () => void;
  beforeAcquireRename?: (candidate: string, lockPath: string) => void;
  beforeAcquireGuardRename?: (candidate: string, guardPath: string) => void;
}

export interface ApplyOptions {
  failAfter?: ApplyPhase;
  crashAfter?: CrashPoint;
  crashAfterSyscall?: SyscallName;
  beforeOperation?: (operation: PlanOperation) => void;
  io?: ApplyIo;
  forceWindowsStateReplace?: boolean;
}

export interface AcquireLockOptions {
  recover?: boolean;
  io?: ApplyIo;
}

export type AppliedAction = "backed-up" | "placed" | "chmodded";

export interface AppliedMutation {
  operationIndex: number;
  path: string;
  kind: PlanOperation["kind"];
  action: AppliedAction;
  identity: FileIdentity;
}

export interface JournalIntent {
  syscall: SyscallName;
  operationIndex?: number;
  path?: string;
  identity?: FileIdentity;
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
  pruneCandidates: string[];
  stateAside: boolean;
  intent?: JournalIntent;
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
const SNAPSHOT_NAME = /^(\d{6})\.json$/;
const TEMP_SNAPSHOT = /^\d{6}\.[0-9a-fA-F-]+\.tmp$/;
const JOURNAL_KEYS = new Set([
  "applied",
  "createdDirectories",
  "intent",
  "newStateDigest",
  "oldStateDigest",
  "operations",
  "phase",
  "pruneCandidates",
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

function isEPERM(error: unknown): boolean {
  return error instanceof Error && "code" in error && (error as NodeJS.ErrnoException).code === "EPERM";
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

function isUnsupportedDirFsync(error: unknown): boolean {
  if (!(error instanceof Error) || !("code" in error)) {
    return false;
  }
  const code = (error as NodeJS.ErrnoException).code;
  return (
    code === "ENOTSUP" ||
    code === "EOPNOTSUPP" ||
    code === "EINVAL" ||
    code === "EISDIR" ||
    (process.platform === "win32" && (code === "EPERM" || code === "EACCES"))
  );
}

function fsyncDirectory(dir: string): void {
  try {
    const fd = openSync(dir, "r");
    try {
      fsyncSync(fd);
    } finally {
      closeSync(fd);
    }
  } catch (error) {
    if (isUnsupportedDirFsync(error)) {
      return;
    }
    throw error;
  }
}

function writeFlushed(path: string, bytes: string | Uint8Array, flag = "w"): void {
  writeFileSync(path, bytes, { flag, flush: true });
}

function relativePathError(path: string): string | null {
  if (path.length === 0 || path.includes("\\") || path.includes("\0") || path.startsWith("/")) {
    return "path must be POSIX root-relative";
  }
  const parts = path.split("/");
  if (
    parts.some((part) => part.length === 0 || part === "." || part === ".." || (part.length === 2 && part[1] === ":"))
  ) {
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

function removeTree(path: string, io?: ApplyIo): void {
  if (io?.rmSync) {
    io.rmSync(path);
    return;
  }
  rmSync(path, { recursive: true, force: true });
}

function createHeldLock(lockPath: string, token: string, io?: ApplyIo): InstallerLock {
  let tombstonePath: string | undefined;
  const expectedTombstone = `${lockPath}.released-${token}`;
  const lock: InstallerLock = {
    path: lockPath,
    token,
    release(): void {
      if (tombstonePath === undefined && existsLstat(lockPath)) {
        const owner = readOwner(lockPath);
        if (owner && owner.token === token) {
          try {
            renameSync(lockPath, expectedTombstone);
            tombstonePath = expectedTombstone;
          } catch (error) {
            if (!isENOENT(error)) {
              const message = error instanceof Error ? error.message : String(error);
              throw new Error(`failed to release installer lock: ${message}`);
            }
          }
        }
      }
      if (tombstonePath === undefined && existsLstat(expectedTombstone)) {
        tombstonePath = expectedTombstone;
      }
      if (tombstonePath === undefined) {
        return;
      }
      const tombstoneOwner = readOwner(tombstonePath);
      if (!tombstoneOwner || tombstoneOwner.token !== token) {
        return;
      }
      try {
        removeTree(tombstonePath, io);
        tombstonePath = undefined;
      } catch (error) {
        if (isENOENT(error)) {
          tombstonePath = undefined;
          return;
        }
        const message = error instanceof Error ? error.message : String(error);
        throw new Error(`failed to release installer lock: ${message}`);
      }
    },
  };
  return lock;
}

function cleanCandidateDebris(deniz: string): void {
  for (const name of readdirSync(deniz)) {
    const transactionCandidate = name.startsWith("txn.candidate-");
    if (!transactionCandidate && !name.startsWith("lock.candidate-") && !name.startsWith("lock.reclaim.candidate-")) {
      continue;
    }
    const candidate = join(deniz, name);
    requireOrdinaryDir(candidate, "installer lock candidate");
    const owner = readOwner(candidate);
    if (!transactionCandidate && owner && processExists(owner.pid)) {
      throw new Error(`installer lock candidate is active in process ${owner.pid}`);
    }
    rmSync(candidate, { recursive: true, force: true });
  }
}

function releaseOwnedDirectory(path: string, token: string, label: string): void {
  if (!existsLstat(path)) {
    return;
  }
  requireOrdinaryDir(path, label);
  const owner = readOwner(path);
  if (!owner || owner.token !== token) {
    return;
  }
  const tombstone = `${path}.released-${token}`;
  renameSync(path, tombstone);
  const movedOwner = readOwner(tombstone);
  if (!movedOwner || movedOwner.token !== token) {
    throw new Error(`${label} token changed during release`);
  }
  rmSync(tombstone, { recursive: true, force: true });
  fsyncDirectory(dirname(path));
}

function acquireReclaimGuard(deniz: string, io?: ApplyIo): InstallerLock {
  const guardPath = join(deniz, "lock.reclaim");
  while (true) {
    if (existsLstat(guardPath)) {
      requireOrdinaryDir(guardPath, "installer lock acquire/reclaim guard");
      const owner = readOwner(guardPath);
      if (!owner) {
        throw new Error("installer lock acquire/reclaim guard is ownerless or malformed");
      }
      if (processExists(owner.pid)) {
        throw new Error(`installer lock acquire/reclaim is active in process ${owner.pid}`);
      }
      const reread = readOwner(guardPath);
      if (!reread || reread.token !== owner.token || processExists(reread.pid)) {
        throw new Error("installer lock acquire/reclaim guard changed while checking staleness");
      }
      const tombstone = `${guardPath}.reclaimed-${owner.token}-${randomUUID()}`;
      try {
        renameSync(guardPath, tombstone);
      } catch (error) {
        if (isENOENT(error)) {
          continue;
        }
        throw error;
      }
      const movedOwner = readOwner(tombstone);
      if (!movedOwner || movedOwner.token !== owner.token) {
        throw new Error("installer lock acquire/reclaim guard token changed during reclaim");
      }
      rmSync(tombstone, { recursive: true, force: true });
      fsyncDirectory(deniz);
      continue;
    }

    const token = randomUUID();
    const candidate = join(deniz, `lock.reclaim.candidate-${token}`);
    mkdirSync(candidate);
    try {
      writeOwner(candidate, { pid: process.pid, startedAt: new Date().toISOString(), token });
      fsyncDirectory(candidate);
      io?.beforeAcquireGuardRename?.(candidate, guardPath);
      try {
        renameSync(candidate, guardPath);
      } catch (error) {
        if (existsLstat(guardPath)) {
          rmSync(candidate, { recursive: true, force: true });
          continue;
        }
        throw error;
      }
      fsyncDirectory(deniz);
      requireOrdinaryDir(guardPath, "installer lock acquire/reclaim guard");
      const guardOwner = readOwner(guardPath);
      if (!guardOwner || guardOwner.token !== token) {
        throw new Error("installer lock acquire/reclaim guard token changed during acquire");
      }
      return {
        path: guardPath,
        token,
        release(): void {
          releaseOwnedDirectory(guardPath, token, "installer lock acquire/reclaim guard");
        },
      };
    } catch (error) {
      if (existsLstat(candidate)) {
        rmSync(candidate, { recursive: true, force: true });
      }
      throw error;
    }
  }
}

export function acquireInstallerLock(destination: string, options: AcquireLockOptions = {}): InstallerLock {
  const { deniz } = ensureDestinationTree(destination);
  const lockPath = join(deniz, "lock");
  const guard = acquireReclaimGuard(deniz, options.io);
  try {
    if (existsLstat(lockPath)) {
      requireOrdinaryDir(lockPath, "installer lock");
      const owner = readOwner(lockPath);
      if (!owner) {
        throw new Error("installer lock is ownerless or malformed; refuse to reclaim");
      }
      if (processExists(owner.pid)) {
        throw new Error(
          `Active installer lock held by process ${owner.pid}; wait for that process to finish, then retry`,
        );
      }
      const recovery = inspectRecovery(destination);
      if (recovery && options.recover !== true) {
        throw new Error("interrupted transaction requires Recovery; acquire the lock for Recovery, then retry");
      }
      options.io?.beforeReclaimRename?.();
      const reread = readOwner(lockPath);
      if (!reread) {
        throw new Error("installer lock is ownerless or malformed; refuse to reclaim");
      }
      if (processExists(reread.pid)) {
        throw new Error(
          `Active installer lock held by process ${reread.pid}; wait for that process to finish, then retry`,
        );
      }
      if (reread.token !== owner.token) {
        throw new Error("installer lock token changed during reclaim");
      }
      const moved = `${lockPath}.reclaimed-${reread.token}`;
      renameSync(lockPath, moved);
      rmSync(moved, { recursive: true, force: true });
    }
    cleanCandidateDebris(deniz);
    const token = randomUUID();
    const candidate = join(deniz, `lock.candidate-${token}`);
    mkdirSync(candidate);
    writeOwner(candidate, { pid: process.pid, startedAt: new Date().toISOString(), token });
    options.io?.beforeAcquireRename?.(candidate, lockPath);
    renameSync(candidate, lockPath);
    requireOrdinaryDir(lockPath, "installer lock");
    return createHeldLock(lockPath, token, options.io);
  } finally {
    guard.release();
  }
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
  if (typeof value.operationIndex !== "number" || !Number.isInteger(value.operationIndex) || value.operationIndex < 0) {
    return null;
  }
  if (
    value.kind !== "add" &&
    value.kind !== "replace" &&
    value.kind !== "remove" &&
    value.kind !== "chmod" &&
    value.kind !== "drop-missing-claim"
  ) {
    return null;
  }
  const identity = parseIdentity(value.identity);
  if (!identity) {
    return null;
  }
  return {
    operationIndex: value.operationIndex,
    path: value.path,
    kind: value.kind,
    action: value.action,
    identity,
  };
}

function parseIntent(value: unknown): JournalIntent | undefined {
  if (value === undefined) {
    return undefined;
  }
  if (!isRecord(value) || typeof value.syscall !== "string") {
    return undefined;
  }
  if (
    value.syscall !== "backup" &&
    value.syscall !== "place" &&
    value.syscall !== "chmod" &&
    value.syscall !== "state-aside" &&
    value.syscall !== "state-commit"
  ) {
    return undefined;
  }
  const intent: JournalIntent = { syscall: value.syscall };
  if (typeof value.operationIndex === "number" && Number.isInteger(value.operationIndex) && value.operationIndex >= 0) {
    intent.operationIndex = value.operationIndex;
  }
  if (typeof value.path === "string") {
    if (relativePathError(value.path)) {
      return undefined;
    }
    intent.path = value.path;
  }
  if (value.identity !== undefined) {
    const identity = parseIdentity(value.identity);
    if (!identity) {
      return undefined;
    }
    intent.identity = identity;
  }
  return intent;
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
  if (!Array.isArray(value.createdDirectories) || !Array.isArray(value.pruneCandidates)) {
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
  const pruneCandidates: string[] = [];
  for (const item of value.pruneCandidates) {
    if (!isCreatedDirectoryPath(item) || item.split("/").length <= 1) {
      return null;
    }
    pruneCandidates.push(item);
  }
  const intent = parseIntent(value.intent);
  if (value.intent !== undefined && intent === undefined) {
    return null;
  }
  const journal: TransactionJournal = {
    schemaVersion: 1,
    transactionId: value.transactionId,
    oldStateDigest: value.oldStateDigest,
    newStateDigest: value.newStateDigest,
    operations,
    phase: value.phase,
    applied,
    createdDirectories,
    pruneCandidates,
    stateAside: value.stateAside,
  };
  if (intent) {
    journal.intent = intent;
  }
  return journal;
}

function digestOfStateFile(path: string): Sha256 | null {
  try {
    requireOrdinaryFile(path, "Install state");
    return stateDigest(parseInstallState(readFileSync(path, "utf8")));
  } catch {
    return null;
  }
}

function observedFileIdentity(path: string): FileIdentity {
  const stat = requireOrdinaryFile(path, path);
  return {
    sha256: hashBytes(readFileSync(path)),
    mode: (stat.mode & 0o111) === 0 ? "100644" : "100755",
  };
}

function identityMatches(left: FileIdentity, right: FileIdentity, platform: "posix" | "windows"): boolean {
  return left.sha256 === right.sha256 && (platform === "windows" || left.mode === right.mode);
}

function hostPlatform(): "posix" | "windows" {
  return process.platform === "win32" ? "windows" : "posix";
}

function snapshotsDir(transactionDir: string): string {
  return join(transactionDir, "snapshots");
}

function allowedAction(kind: PlanOperation["kind"], action: AppliedAction): boolean {
  if (action === "backed-up") {
    return kind === "replace" || kind === "remove";
  }
  if (action === "placed") {
    return kind === "add" || kind === "replace";
  }
  return kind === "chmod";
}

function actionOrder(action: AppliedAction): number {
  if (action === "backed-up") {
    return 1;
  }
  if (action === "placed" || action === "chmodded") {
    return 2;
  }
  return 0;
}

function createdDirAllowed(dir: string, operations: PlanOperation[]): boolean {
  return operations.some((operation) => {
    if (operation.kind !== "add" && operation.kind !== "replace") {
      return false;
    }
    return operation.path.startsWith(`${dir}/`);
  });
}

function postCommitPruneCandidates(operations: PlanOperation[]): string[] {
  const candidates = new Set<string>();
  for (const operation of operations) {
    if (operation.kind !== "remove" && operation.kind !== "drop-missing-claim") {
      continue;
    }
    const parts = operation.path.split("/");
    parts.pop();
    while (parts.length > 1) {
      candidates.add(parts.join("/"));
      parts.pop();
    }
  }
  return [...candidates].sort((left, right) => {
    const depth = right.split("/").length - left.split("/").length;
    return depth === 0 ? ordinalCompare(left, right) : depth;
  });
}

function validateJournalSemantics(journal: TransactionJournal): string | null {
  const seen = new Map<number, AppliedAction[]>();
  for (const mutation of journal.applied) {
    const operation = journal.operations[mutation.operationIndex];
    if (!operation) {
      return "applied entry references a missing operation";
    }
    if (operation.path !== mutation.path || operation.kind !== mutation.kind) {
      return "applied entry does not match its operation";
    }
    if (!allowedAction(mutation.kind, mutation.action)) {
      return "applied action is not valid for its operation kind";
    }
    const prior = seen.get(mutation.operationIndex) ?? [];
    const last = prior[prior.length - 1];
    if (last && actionOrder(mutation.action) <= actionOrder(last)) {
      return "applied actions are not monotonic";
    }
    if (mutation.action === "placed" && mutation.kind === "replace" && !prior.includes("backed-up")) {
      return "replace was placed without a backup";
    }
    prior.push(mutation.action);
    seen.set(mutation.operationIndex, prior);
  }
  for (const dir of journal.createdDirectories) {
    if (!createdDirAllowed(dir, journal.operations)) {
      return "createdDirectories contains an unrelated path";
    }
  }
  if (JSON.stringify(journal.pruneCandidates) !== JSON.stringify(postCommitPruneCandidates(journal.operations))) {
    return "pruneCandidates does not match removed managed paths";
  }
  return null;
}

function validateSnapshotTransition(previous: TransactionJournal, current: TransactionJournal): string | null {
  if (current.transactionId !== previous.transactionId) {
    return "contiguous journal snapshots changed transaction identity";
  }
  if (current.oldStateDigest !== previous.oldStateDigest || current.newStateDigest !== previous.newStateDigest) {
    return "contiguous journal snapshots changed state digests";
  }
  if (JSON.stringify(current.operations) !== JSON.stringify(previous.operations)) {
    return "contiguous journal snapshots rewrote operations";
  }
  const phaseOrder = { prepared: 0, "files-placed": 1, "state-committed": 2 } as const;
  if (phaseOrder[current.phase] < phaseOrder[previous.phase]) {
    return "contiguous journal snapshots regressed phase";
  }
  if (
    current.applied.length < previous.applied.length ||
    previous.applied.some((mutation, index) => JSON.stringify(current.applied[index]) !== JSON.stringify(mutation))
  ) {
    return "contiguous journal snapshots omitted or rewrote applied evidence";
  }
  if (
    current.createdDirectories.length < previous.createdDirectories.length ||
    previous.createdDirectories.some((dir, index) => current.createdDirectories[index] !== dir)
  ) {
    return "contiguous journal snapshots omitted or rewrote created-directory evidence";
  }
  if (JSON.stringify(current.pruneCandidates) !== JSON.stringify(previous.pruneCandidates)) {
    return "contiguous journal snapshots changed post-commit prune candidates";
  }
  if (current.applied.length > previous.applied.length) {
    const appended = current.applied.slice(previous.applied.length);
    const intent = previous.intent;
    const action =
      intent?.syscall === "backup"
        ? "backed-up"
        : intent?.syscall === "place"
          ? "placed"
          : intent?.syscall === "chmod"
            ? "chmodded"
            : undefined;
    const mutation = appended[0];
    if (
      appended.length !== 1 ||
      !intent ||
      !action ||
      !mutation ||
      current.intent !== undefined ||
      mutation.action !== action ||
      mutation.operationIndex !== intent.operationIndex ||
      mutation.path !== intent.path ||
      !intent.identity ||
      !exactIdentityMatches(mutation.identity, intent.identity)
    ) {
      return "contiguous journal snapshots appended applied evidence without its write-ahead intent";
    }
  }
  if (
    current.createdDirectories.length > previous.createdDirectories.length &&
    (previous.intent !== undefined ||
      current.intent?.syscall !== "place" ||
      current.applied.length !== previous.applied.length ||
      current.phase !== previous.phase)
  ) {
    return "contiguous journal snapshots appended created-directory evidence outside a place intent";
  }
  if (
    previous.intent &&
    (previous.intent.syscall === "backup" ||
      previous.intent.syscall === "place" ||
      previous.intent.syscall === "chmod") &&
    current.applied.length === previous.applied.length
  ) {
    return "contiguous journal snapshots cleared operation intent without applied evidence";
  }
  if (previous.intent?.syscall === "state-aside") {
    if (
      current.intent !== undefined ||
      !current.stateAside ||
      current.phase !== previous.phase ||
      current.applied.length !== previous.applied.length
    ) {
      return "contiguous journal snapshots did not complete state-aside intent through its allowed transition";
    }
  }
  if (previous.intent?.syscall === "state-commit") {
    if (current.intent !== undefined || current.phase !== "state-committed" || current.stateAside) {
      return "contiguous journal snapshots did not complete state-commit intent through its allowed transition";
    }
  }
  if (!previous.stateAside && current.stateAside && previous.intent?.syscall !== "state-aside") {
    return "contiguous journal snapshots added stateAside evidence without state-aside intent";
  }
  if (previous.stateAside && !current.stateAside && current.phase !== "state-committed") {
    return "contiguous journal snapshots removed stateAside evidence before state commit";
  }
  return null;
}

interface StateEvidence {
  bytes: Buffer;
  state: InstallState;
}

function readStateEvidenceFile(
  path: string,
  expectedDigest: Sha256,
  label: string,
): { evidence: StateEvidence } | { blocked: string } {
  try {
    requireOrdinaryFile(path, label);
    const bytes = readFileSync(path);
    const parsed = parseInstallState(bytes.toString("utf8"));
    if (hashBytes(bytes) !== expectedDigest || stateDigest(parsed) !== expectedDigest) {
      return { blocked: `${label} bytes do not match the journal digest` };
    }
    return { evidence: { bytes, state: parsed } };
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    return { blocked: `${label} is invalid: ${message}` };
  }
}

function exactIdentityMatches(left: FileIdentity, right: FileIdentity): boolean {
  return left.sha256 === right.sha256 && left.mode === right.mode;
}

function ownedIdentity(state: InstallState, path: string): FileIdentity | null {
  const owned = state.files[path];
  return owned ? { sha256: owned.sha256, mode: owned.mode } : null;
}

function operationStateError(operation: PlanOperation, oldState: InstallState, newState: InstallState): string | null {
  const oldOwned = oldState.files[operation.path];
  const newOwned = newState.files[operation.path];
  if (operation.kind === "add") {
    if (
      oldOwned ||
      !newOwned ||
      newOwned.module !== operation.module ||
      !exactIdentityMatches(newOwned, operation.identity)
    ) {
      return `${operation.path}: add operation does not match old/new Install state`;
    }
    return null;
  }
  if (operation.kind === "replace") {
    if (
      !oldOwned ||
      !newOwned ||
      newOwned.module !== operation.module ||
      !exactIdentityMatches(newOwned, operation.identity)
    ) {
      return `${operation.path}: replace operation does not match old/new Install state`;
    }
    return null;
  }
  if (operation.kind === "remove") {
    if (
      !oldOwned ||
      oldOwned.module !== operation.module ||
      !exactIdentityMatches(oldOwned, operation.identity) ||
      newOwned
    ) {
      return `${operation.path}: remove operation does not match old/new Install state`;
    }
    return null;
  }
  if (operation.kind === "chmod") {
    if (
      !oldOwned ||
      !newOwned ||
      oldOwned.sha256 !== newOwned.sha256 ||
      oldOwned.mode !== operation.from ||
      newOwned.mode !== operation.to ||
      newOwned.module !== operation.module
    ) {
      return `${operation.path}: chmod operation does not match old/new Install state`;
    }
    return null;
  }
  if (!oldOwned || oldOwned.module !== operation.module || newOwned) {
    return `${operation.path}: drop-missing-claim operation does not match old/new Install state`;
  }
  return null;
}

function expectedAppliedMutations(
  journal: TransactionJournal,
  oldState: InstallState,
  newState: InstallState,
): AppliedMutation[] | { blocked: string } {
  const paths = new Set<string>();
  for (const operation of journal.operations) {
    if (paths.has(operation.path)) {
      return { blocked: `${operation.path}: operation list contains duplicate managed paths` };
    }
    paths.add(operation.path);
    const stateError = operationStateError(operation, oldState, newState);
    if (stateError) {
      return { blocked: stateError };
    }
  }
  const expected: AppliedMutation[] = [];
  for (const [operationIndex, operation] of journal.operations.entries()) {
    if (operation.kind !== "replace" && operation.kind !== "remove") {
      continue;
    }
    const identity = ownedIdentity(oldState, operation.path);
    if (!identity) {
      return { blocked: `${operation.path}: old Install state identity is missing` };
    }
    expected.push({ operationIndex, path: operation.path, kind: operation.kind, action: "backed-up", identity });
  }
  for (const [operationIndex, operation] of journal.operations.entries()) {
    if (operation.kind !== "add" && operation.kind !== "replace") {
      continue;
    }
    expected.push({
      operationIndex,
      path: operation.path,
      kind: operation.kind,
      action: "placed",
      identity: operation.identity,
    });
  }
  for (const [operationIndex, operation] of journal.operations.entries()) {
    if (operation.kind !== "chmod") {
      continue;
    }
    const identity = ownedIdentity(newState, operation.path);
    if (!identity) {
      return { blocked: `${operation.path}: new Install state identity is missing` };
    }
    expected.push({ operationIndex, path: operation.path, kind: operation.kind, action: "chmodded", identity });
  }
  return expected;
}

function validateJournalAgainstStates(
  journal: TransactionJournal,
  oldState: InstallState,
  newState: InstallState,
): string | null {
  const expected = expectedAppliedMutations(journal, oldState, newState);
  if ("blocked" in expected) {
    return expected.blocked;
  }
  if (journal.applied.length > expected.length) {
    return "applied evidence exceeds the operation list";
  }
  for (const [index, mutation] of journal.applied.entries()) {
    const wanted = expected[index];
    if (!wanted || JSON.stringify(mutation) !== JSON.stringify(wanted)) {
      return "applied evidence does not match the operation's expected old/new identity";
    }
  }
  if (journal.phase !== "prepared" && journal.applied.length !== expected.length) {
    return `${journal.phase} snapshot omits applied operation evidence`;
  }
  const intent = journal.intent;
  if (intent?.syscall === "backup" || intent?.syscall === "place" || intent?.syscall === "chmod") {
    const action = intent.syscall === "backup" ? "backed-up" : intent.syscall === "place" ? "placed" : "chmodded";
    const wanted = expected[journal.applied.length];
    if (
      !wanted ||
      wanted.action !== action ||
      intent.operationIndex !== wanted.operationIndex ||
      intent.path !== wanted.path ||
      !intent.identity ||
      !exactIdentityMatches(intent.identity, wanted.identity)
    ) {
      return "write-ahead intent does not match the next expected operation action and identity";
    }
  }
  if (intent?.syscall === "state-aside" || intent?.syscall === "state-commit") {
    if (
      journal.phase !== "files-placed" ||
      journal.applied.length !== expected.length ||
      intent.operationIndex !== undefined ||
      intent.path !== undefined ||
      intent.identity !== undefined
    ) {
      return "Install-state write-ahead intent is not valid for this transaction phase";
    }
  }
  if (journal.phase === "prepared" && (intent?.syscall === "state-aside" || intent?.syscall === "state-commit")) {
    return "prepared snapshot cannot carry Install-state intent";
  }
  if (journal.stateAside && (journal.phase !== "files-placed" || intent?.syscall === "state-aside")) {
    return "stateAside evidence is not valid for this transaction phase";
  }
  if (journal.phase === "state-committed" && (journal.stateAside || intent !== undefined)) {
    return "state-committed snapshot contains regressed transaction evidence";
  }
  return null;
}

function validatePersistedBackupEvidence(
  transactionDir: string,
  journal: TransactionJournal,
  oldState: InstallState,
): string | null {
  for (const [operationIndex, operation] of journal.operations.entries()) {
    if (operation.kind !== "replace" && operation.kind !== "remove") {
      continue;
    }
    const backup = backupFilePath(transactionDir, operation.path);
    const backedUp = journal.applied.some(
      (mutation) => mutation.operationIndex === operationIndex && mutation.action === "backed-up",
    );
    const pendingBackup =
      journal.intent?.syscall === "backup" &&
      journal.intent.operationIndex === operationIndex &&
      journal.intent.path === operation.path;
    if (!existsLstat(backup)) {
      if (backedUp) {
        return `backup of ${operation.path} is missing despite applied evidence`;
      }
      continue;
    }
    if (!backedUp && !pendingBackup) {
      return `backup of ${operation.path} was omitted from applied and intent evidence`;
    }
    try {
      requireOrdinaryFile(backup, `backup of ${operation.path}`);
      const expected = ownedIdentity(oldState, operation.path);
      if (!expected || !identityMatches(observedFileIdentity(backup), expected, hostPlatform())) {
        return `backup of ${operation.path} does not match expected old bytes and mode`;
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      return `backup of ${operation.path} is invalid: ${message}`;
    }
  }
  return null;
}

function loadLatestSnapshot(transactionDir: string): { journal: TransactionJournal } | { blocked: string } {
  const dir = snapshotsDir(transactionDir);
  if (!existsLstat(dir)) {
    return { blocked: "transaction journal is missing" };
  }
  try {
    requireOrdinaryDir(dir, "journal snapshots");
  } catch {
    return { blocked: "journal snapshots must be an ordinary directory" };
  }
  const bySeq = new Map<number, TransactionJournal>();
  for (const name of readdirSync(dir)) {
    const entry = join(dir, name);
    const stat = lstatSync(entry);
    if (TEMP_SNAPSHOT.test(name)) {
      if (stat.isSymbolicLink() || !stat.isFile()) {
        return { blocked: "temp journal snapshot debris is not an ordinary file" };
      }
      continue;
    }
    const match = SNAPSHOT_NAME.exec(name);
    if (!match) {
      return { blocked: `unresolved journal debris ${JSON.stringify(name)}` };
    }
    if (stat.isSymbolicLink() || !stat.isFile()) {
      return { blocked: "transaction journal is not an ordinary file" };
    }
    const journal = parseJournal(readFileSync(entry, "utf8"));
    if (!journal) {
      return { blocked: "transaction journal is malformed" };
    }
    const semantic = validateJournalSemantics(journal);
    if (semantic) {
      return { blocked: semantic };
    }
    const seq = Number(match[1]);
    bySeq.set(seq, journal);
  }
  if (bySeq.size === 0) {
    return { blocked: "transaction journal is missing" };
  }
  const seqs = [...bySeq.keys()].sort((left, right) => left - right);
  if (seqs[0] !== 1) {
    return { blocked: "journal snapshots are not a valid prefix" };
  }
  for (let index = 1; index < seqs.length; index += 1) {
    const previous = seqs[index - 1];
    const current = seqs[index];
    if (previous === undefined || current === undefined || current !== previous + 1) {
      return { blocked: "journal snapshots are not a valid prefix" };
    }
    const previousJournal = bySeq.get(previous);
    const currentJournal = bySeq.get(current);
    if (!previousJournal || !currentJournal) {
      return { blocked: "transaction journal is missing" };
    }
    const transition = validateSnapshotTransition(previousJournal, currentJournal);
    if (transition) {
      return { blocked: transition };
    }
  }
  const latestSeq = seqs[seqs.length - 1];
  if (latestSeq === undefined) {
    return { blocked: "transaction journal is missing" };
  }
  const latest = bySeq.get(latestSeq);
  if (!latest) {
    return { blocked: "transaction journal is missing" };
  }
  const oldStateEvidence = readStateEvidenceFile(
    join(transactionDir, "old-state.json"),
    latest.oldStateDigest,
    "immutable old Install state",
  );
  if ("blocked" in oldStateEvidence) {
    return oldStateEvidence;
  }
  const newStatePath = join(transactionDir, "new-state.json");
  const newStateEvidence = readStateEvidenceFile(newStatePath, latest.newStateDigest, "immutable new Install state");
  if ("blocked" in newStateEvidence) {
    return newStateEvidence;
  }
  for (const seq of seqs) {
    const journal = bySeq.get(seq);
    if (!journal) {
      return { blocked: "transaction journal is missing" };
    }
    const stateSemantic = validateJournalAgainstStates(
      journal,
      oldStateEvidence.evidence.state,
      newStateEvidence.evidence.state,
    );
    if (stateSemantic) {
      return { blocked: stateSemantic };
    }
  }
  const backupSemantic = validatePersistedBackupEvidence(transactionDir, latest, oldStateEvidence.evidence.state);
  if (backupSemantic) {
    return { blocked: backupSemantic };
  }
  return { journal: latest };
}

function destAbs(destination: string, path: string): string {
  return join(destination, ...path.split("/"));
}

function inferredMutation(
  journal: TransactionJournal,
  intent: JournalIntent,
  action: AppliedAction,
): AppliedMutation | { blocked: string } {
  if (!intent.path || !intent.identity) {
    return { blocked: "journal intent is missing path or identity" };
  }
  const operationIndex =
    intent.operationIndex ??
    journal.operations.findIndex(
      (operation) => operation.path === intent.path && allowedAction(operation.kind, action),
    );
  const operation = journal.operations[operationIndex];
  if (!operation || operation.path !== intent.path) {
    return { blocked: "journal intent does not match an operation" };
  }
  return {
    operationIndex,
    path: intent.path,
    kind: operation.kind,
    action,
    identity: intent.identity,
  };
}

function inferIntent(
  destination: string,
  transactionDir: string,
  journal: TransactionJournal,
): TransactionJournal | { blocked: string } {
  const intent = journal.intent;
  if (!intent) {
    return journal;
  }
  const next: TransactionJournal = {
    ...journal,
    applied: [...journal.applied],
  };
  delete next.intent;
  if (intent.syscall === "backup" && intent.path && intent.identity) {
    const backup = backupFilePath(transactionDir, intent.path);
    const dest = destAbs(destination, intent.path);
    if (existsLstat(backup) && !existsLstat(dest)) {
      const mutation = inferredMutation(journal, intent, "backed-up");
      if ("blocked" in mutation) {
        return mutation;
      }
      next.applied.push(mutation);
      return next;
    }
    if (existsLstat(dest) && !existsLstat(backup)) {
      return next;
    }
    return { blocked: "backup syscall window is ambiguous; recovery is blocked" };
  }
  if (intent.syscall === "place" && intent.path && intent.identity) {
    const dest = destAbs(destination, intent.path);
    const staged = stagedFilePath(transactionDir, intent.path);
    if (existsLstat(dest)) {
      const observed = observedFileIdentity(dest);
      if (observed.sha256 === intent.identity.sha256) {
        const mutation = inferredMutation(journal, intent, "placed");
        if ("blocked" in mutation) {
          return mutation;
        }
        next.applied.push(mutation);
        return next;
      }
    }
    if (!existsLstat(dest) && existsLstat(staged)) {
      return next;
    }
    return { blocked: "place syscall window is ambiguous; recovery is blocked" };
  }
  if (intent.syscall === "chmod" && intent.path && intent.identity) {
    const dest = destAbs(destination, intent.path);
    if (!existsLstat(dest)) {
      return { blocked: "chmod syscall window is ambiguous; recovery is blocked" };
    }
    const observed = observedFileIdentity(dest);
    if (
      observed.sha256 === intent.identity.sha256 &&
      (hostPlatform() === "windows" || observed.mode === intent.identity.mode)
    ) {
      const mutation = inferredMutation(journal, intent, "chmodded");
      if ("blocked" in mutation) {
        return mutation;
      }
      next.applied.push(mutation);
    }
    return next;
  }
  if (intent.syscall === "state-aside") {
    const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
    const backed = join(transactionDir, "backup-install.json");
    if (!existsLstat(installPath) && existsLstat(backed) && digestOfStateFile(backed) === journal.oldStateDigest) {
      next.stateAside = true;
      return next;
    }
    if (existsLstat(installPath) && digestOfStateFile(installPath) === journal.oldStateDigest) {
      return next;
    }
    return { blocked: "state-aside syscall window is ambiguous; recovery is blocked" };
  }
  if (intent.syscall === "state-commit") {
    const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
    if (existsLstat(installPath) && digestOfStateFile(installPath) === journal.newStateDigest) {
      next.phase = "state-committed";
      next.stateAside = false;
      return next;
    }
    if (!existsLstat(installPath)) {
      next.stateAside = true;
      return next;
    }
    return next;
  }
  return { blocked: "unsupported journal intent" };
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

function isLockArtifact(name: string): boolean {
  return (
    name === "lock" ||
    name === "lock.reclaim" ||
    name.startsWith("lock.candidate-") ||
    name.startsWith("lock.reclaim.candidate-") ||
    name.startsWith("lock.reclaim.reclaimed-") ||
    name.startsWith("lock.reclaim.released-") ||
    name.startsWith("lock.released-") ||
    name.startsWith("lock.reclaimed-")
  );
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
    if (isLockArtifact(name)) {
      if (stat.isSymbolicLink() || !stat.isDirectory()) {
        return blocked(deniz, `${name} must be an ordinary directory`);
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
    if (name.startsWith("txn.candidate-")) {
      if (stat.isSymbolicLink() || !stat.isDirectory()) {
        return blocked(entry, "transaction staging candidate is not an ordinary directory");
      }
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
  const loaded = loadLatestSnapshot(transactionDir);
  if ("blocked" in loaded) {
    return blocked(transactionDir, loaded.blocked);
  }
  const inferred = inferIntent(destination, transactionDir, loaded.journal);
  if ("blocked" in inferred) {
    return blocked(transactionDir, inferred.blocked);
  }
  const semantic = validateJournalSemantics(inferred);
  if (semantic) {
    return blocked(transactionDir, semantic);
  }
  let evidence: { oldState: StateEvidence; newState: StateEvidence };
  try {
    evidence = requireTransactionStateEvidence(transactionDir, inferred);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    return blocked(transactionDir, message);
  }
  const stateSemantic = validateJournalAgainstStates(inferred, evidence.oldState.state, evidence.newState.state);
  if (stateSemantic) {
    return blocked(transactionDir, stateSemantic);
  }
  const backupSemantic = validatePersistedBackupEvidence(transactionDir, inferred, evidence.oldState.state);
  if (backupSemantic) {
    return blocked(transactionDir, backupSemantic);
  }
  return classifyRecovery(destination, transactionDir, inferred);
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
  if (observed.kind === "link" || observed.kind === "directory" || observed.kind === "blocked") {
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
    if (
      !recorded ||
      observed.identity.sha256 !== recorded.sha256 ||
      !identityMatches(observed.identity, { sha256: recorded.sha256, mode: operation.from }, platform)
    ) {
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

function throwIfSyscallCrash(options: ApplyOptions | undefined, syscall: SyscallName): void {
  if (options?.crashAfterSyscall === syscall) {
    throw new Error(`injected crash after ${syscall} syscall`);
  }
}

function isInjectedCrash(error: unknown): boolean {
  return error instanceof Error && error.message.startsWith("injected crash ");
}

function writeSnapshot(transactionDir: string, journal: TransactionJournal, seq: { value: number }): void {
  const dir = snapshotsDir(transactionDir);
  mkdirSync(dir, { recursive: true });
  seq.value += 1;
  const name = String(seq.value).padStart(6, "0");
  const tmp = join(dir, `${name}.${randomUUID()}.tmp`);
  writeFlushed(tmp, `${JSON.stringify(journal, null, 2)}\n`);
  renameSync(tmp, join(dir, `${name}.json`));
  fsyncDirectory(dir);
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

function unlinkManagedFile(destination: string, path: string, io?: ApplyIo): void {
  const destPath = destAbs(destination, path);
  try {
    const stat = lstatSync(destPath);
    if (stat.isSymbolicLink()) {
      throw new Error(`${path}: managed path must not be a symlink or junction`);
    }
    if (stat.isFile()) {
      if (io?.rmSync) {
        io.rmSync(destPath);
      } else {
        rmSync(destPath);
      }
    }
  } catch (error) {
    if (!isENOENT(error)) {
      throw error;
    }
  }
}

function restoreBackup(
  destination: string,
  transactionDir: string,
  path: string,
  created: string[],
  expected: FileIdentity,
): void {
  const backup = backupFilePath(transactionDir, path);
  requireOrdinaryFile(backup, `backup of ${path}`);
  if (!identityMatches(observedFileIdentity(backup), expected, hostPlatform())) {
    throw new Error(`backup of ${path} does not match expected old bytes and mode`);
  }
  validateManagedPath(destination, path);
  const observed = observePath(destination, path);
  if (observed.kind === "file" && identityMatches(observed.identity, expected, hostPlatform())) {
    return;
  }
  if (observed.kind !== "absent") {
    throw new Error(`${path} has ambiguous or unexpected destination evidence; recovery is blocked`);
  }
  const destPath = ensureParents(destination, path, created);
  writeFlushed(destPath, readFileSync(backup));
  applyMode(destPath, expected.mode);
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

function pruneRemovedDirectories(destination: string, candidates: string[]): void {
  for (const dir of candidates) {
    const parts = dir.split("/");
    if (parts.length <= 1 || !NATIVE_ROOTS.has(parts[0] ?? "")) {
      throw new Error(`${dir}: invalid post-commit prune candidate`);
    }
    const abs = requireContained(destination, join(destination, ...parts));
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

function requireTransactionStateEvidence(
  transactionDir: string,
  journal: TransactionJournal,
): { oldState: StateEvidence; newState: StateEvidence } {
  const oldState = readStateEvidenceFile(
    join(transactionDir, "old-state.json"),
    journal.oldStateDigest,
    "immutable old Install state",
  );
  if ("blocked" in oldState) {
    throw new Error(oldState.blocked);
  }
  const newState = readStateEvidenceFile(
    join(transactionDir, "new-state.json"),
    journal.newStateDigest,
    "immutable new Install state",
  );
  if ("blocked" in newState) {
    throw new Error(newState.blocked);
  }
  return { oldState: oldState.evidence, newState: newState.evidence };
}

function mutationActions(journal: TransactionJournal, operationIndex: number): Set<AppliedAction> {
  return new Set(
    journal.applied.filter((mutation) => mutation.operationIndex === operationIndex).map((mutation) => mutation.action),
  );
}

function observedMatchesIdentity(observed: ReturnType<typeof observePath>, expected: FileIdentity): boolean {
  return observed.kind === "file" && identityMatches(observed.identity, expected, hostPlatform());
}

function preflightRollbackStateEvidence(
  destination: string,
  transactionDir: string,
  journal: TransactionJournal,
  oldState: StateEvidence,
): void {
  const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
  const backedState = join(transactionDir, "backup-install.json");
  if (existsLstat(backedState)) {
    requireOrdinaryFile(backedState, "backed-up Install state");
    if (!readFileSync(backedState).equals(oldState.bytes)) {
      throw new Error("backed-up Install state does not match exact old state bytes");
    }
  }
  if (existsLstat(installPath)) {
    requireOrdinaryFile(installPath, "Install state");
    if (!readFileSync(installPath).equals(oldState.bytes)) {
      throw new Error("Install state has ambiguous or unexpected rollback evidence");
    }
    return;
  }
  if (!existsLstat(backedState) && journal.oldStateDigest !== stateDigest(EMPTY_INSTALL_STATE)) {
    throw new Error("exact old Install state evidence is missing; recovery is blocked");
  }
}

function preflightRollbackEvidence(
  destination: string,
  journal: TransactionJournal,
  oldState: InstallState,
  newState: InstallState,
): void {
  for (const [operationIndex, operation] of journal.operations.entries()) {
    if (operation.kind === "drop-missing-claim") {
      continue;
    }
    validateManagedPath(destination, operation.path);
    const observed = observePath(destination, operation.path);
    const actions = mutationActions(journal, operationIndex);
    const oldIdentity = ownedIdentity(oldState, operation.path);
    const newIdentity = ownedIdentity(newState, operation.path);
    let allowed = false;
    if (operation.kind === "add") {
      allowed =
        observed.kind === "absent" ||
        (actions.has("placed") && !!newIdentity && observedMatchesIdentity(observed, newIdentity));
    } else if (operation.kind === "replace") {
      allowed =
        (!!oldIdentity && observedMatchesIdentity(observed, oldIdentity)) ||
        (actions.has("backed-up") && observed.kind === "absent") ||
        (actions.has("placed") && !!newIdentity && observedMatchesIdentity(observed, newIdentity));
    } else if (operation.kind === "remove") {
      allowed =
        (!!oldIdentity && observedMatchesIdentity(observed, oldIdentity)) ||
        (actions.has("backed-up") && observed.kind === "absent");
    } else if (operation.kind === "chmod") {
      allowed =
        (!!oldIdentity && observedMatchesIdentity(observed, oldIdentity)) ||
        (actions.has("chmodded") && !!newIdentity && observedMatchesIdentity(observed, newIdentity));
    }
    if (!allowed) {
      throw new Error(`${operation.path} has ambiguous or unexpected destination evidence; recovery is blocked`);
    }
  }
}

function rollbackApplied(destination: string, transactionDir: string, journal: TransactionJournal, io?: ApplyIo): void {
  const evidence = requireTransactionStateEvidence(transactionDir, journal);
  for (const mutation of [...journal.applied].reverse()) {
    validateManagedPath(destination, mutation.path);
    const operation = journal.operations[mutation.operationIndex];
    if (!operation || operation.path !== mutation.path || operation.kind !== mutation.kind) {
      throw new Error(`${mutation.path}: applied evidence does not match its operation`);
    }
    if (mutation.action === "placed") {
      const observed = observePath(destination, mutation.path);
      const expectedNew = ownedIdentity(evidence.newState.state, mutation.path);
      const expectedOld = ownedIdentity(evidence.oldState.state, mutation.path);
      if (expectedNew && observedMatchesIdentity(observed, expectedNew)) {
        unlinkManagedFile(destination, mutation.path, io);
      } else if (
        observed.kind !== "absent" &&
        !(operation.kind === "replace" && expectedOld && observedMatchesIdentity(observed, expectedOld))
      ) {
        throw new Error(`${mutation.path} has ambiguous or unexpected destination evidence; recovery is blocked`);
      }
      continue;
    }
    if (mutation.action === "backed-up") {
      const expectedOld = ownedIdentity(evidence.oldState.state, mutation.path);
      if (!expectedOld) {
        throw new Error(`${mutation.path}: old Install state identity is missing`);
      }
      restoreBackup(destination, transactionDir, mutation.path, [], expectedOld);
      continue;
    }
    if (operation.kind === "chmod") {
      const destPath = destAbs(destination, mutation.path);
      const observed = observePath(destination, mutation.path);
      const expectedOld = ownedIdentity(evidence.oldState.state, mutation.path);
      const expectedNew = ownedIdentity(evidence.newState.state, mutation.path);
      if (expectedOld && observedMatchesIdentity(observed, expectedOld)) {
        continue;
      }
      if (expectedNew && observedMatchesIdentity(observed, expectedNew)) {
        applyMode(destPath, operation.from);
        continue;
      }
      throw new Error(`${mutation.path} has ambiguous or unexpected destination evidence; recovery is blocked`);
    }
  }
  const backedState = join(transactionDir, "backup-install.json");
  const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
  if (existsLstat(backedState)) {
    requireOrdinaryFile(backedState, "backed-up Install state");
    const backedBytes = readFileSync(backedState);
    if (!backedBytes.equals(evidence.oldState.bytes)) {
      throw new Error("backed-up Install state does not match exact old state bytes");
    }
    if (existsLstat(installPath)) {
      requireOrdinaryFile(installPath, "Install state");
      if (!readFileSync(installPath).equals(evidence.oldState.bytes)) {
        throw new Error("Install state has ambiguous or unexpected rollback evidence");
      }
    } else {
      writeFlushed(installPath, backedBytes);
    }
  }
  pruneCreatedDirectories(destination, journal.createdDirectories);
}

function verifyRollbackComplete(destination: string, transactionDir: string, journal: TransactionJournal): void {
  const evidence = requireTransactionStateEvidence(transactionDir, journal);
  const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
  if (existsLstat(installPath)) {
    requireOrdinaryFile(installPath, "Install state");
    if (
      !readFileSync(installPath).equals(evidence.oldState.bytes) ||
      digestOfStateFile(installPath) !== journal.oldStateDigest
    ) {
      throw new Error("rollback did not restore the old Install state");
    }
  } else if (journal.oldStateDigest !== stateDigest(EMPTY_INSTALL_STATE)) {
    throw new Error("rollback did not restore the old Install state");
  }
  for (const operation of journal.operations) {
    if (operation.kind === "drop-missing-claim") {
      continue;
    }
    const observed = observePath(destination, operation.path);
    if (operation.kind === "add") {
      if (observed.kind !== "absent") {
        throw new Error(`rollback did not restore absence of ${operation.path}`);
      }
      continue;
    }
    const expected = ownedIdentity(evidence.oldState.state, operation.path);
    if (!expected || !observedMatchesIdentity(observed, expected)) {
      throw new Error(`rollback did not restore exact old bytes and mode for ${operation.path}`);
    }
  }
}

function verifyFinalizeComplete(destination: string, transactionDir: string, journal: TransactionJournal): void {
  const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
  const newStateEvidence = readStateEvidenceFile(
    join(transactionDir, "new-state.json"),
    journal.newStateDigest,
    "immutable new Install state",
  );
  if ("blocked" in newStateEvidence) {
    throw new Error(`finalize refused: ${newStateEvidence.blocked}`);
  }
  requireOrdinaryFile(installPath, "committed Install state");
  const installBytes = readFileSync(installPath);
  if (
    !installBytes.equals(newStateEvidence.evidence.bytes) ||
    digestOfStateFile(installPath) !== journal.newStateDigest
  ) {
    throw new Error("finalize refused: Install state is not the committed journal digest");
  }
  const platform = hostPlatform();
  for (const operation of journal.operations) {
    const observed = observePath(destination, operation.path);
    if (operation.kind === "remove" || operation.kind === "drop-missing-claim") {
      if (observed.kind !== "absent") {
        throw new Error(`finalize refused: ${operation.path} is still present`);
      }
      continue;
    }
    if (operation.kind === "add" || operation.kind === "replace") {
      if (observed.kind !== "file" || !identityMatches(observed.identity, operation.identity, platform)) {
        throw new Error(`finalize refused: ${operation.path} does not match the committed Plan`);
      }
      continue;
    }
    if (operation.kind !== "chmod") {
      continue;
    }
    const expected = ownedIdentity(newStateEvidence.evidence.state, operation.path);
    if (observed.kind !== "file" || !expected || !identityMatches(observed.identity, expected, platform)) {
      throw new Error(`finalize refused: ${operation.path} content or mode does not match the committed chmod`);
    }
  }
}

function requireBackupEvidence(transactionDir: string, journal: TransactionJournal): void {
  const platform = hostPlatform();
  for (const mutation of journal.applied) {
    if (mutation.action !== "backed-up") {
      continue;
    }
    const backup = backupFilePath(transactionDir, mutation.path);
    requireOrdinaryFile(backup, `backup of ${mutation.path}`);
    const observed = observedFileIdentity(backup);
    if (!identityMatches(observed, mutation.identity, platform)) {
      throw new Error(`backup of ${mutation.path} does not match the expected identity`);
    }
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

function verifyStagedFile(path: string, expected: FileIdentity, managedPath: string): void {
  if (!identityMatches(observedFileIdentity(path), expected, hostPlatform())) {
    throw new Error(`${managedPath}: staged file bytes or mode do not match the Plan`);
  }
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

function nearestExistingAncestor(path: string): string {
  let current = path;
  while (!existsLstat(current)) {
    const parent = dirname(current);
    if (parent === current) {
      throw new Error(`no existing ancestor for ${path}`);
    }
    current = parent;
  }
  return current;
}

function requireManagedDevice(destination: string, managedPath: string, io: ApplyIo | undefined): void {
  requireSameDevice(destination, nearestExistingAncestor(destAbs(destination, managedPath)), io);
}

function requireDestinationTopology(
  destination: string,
  deniz: string,
  io: ApplyIo | undefined,
  managedPaths: string[] = [],
): void {
  requireSameDevice(destination, deniz, io);
  for (const native of NATIVE_ROOTS) {
    const path = join(destination, native);
    if (existsLstat(path)) {
      requireOrdinaryDir(path, native);
      requireSameDevice(destination, path, io);
    }
  }
  for (const managedPath of managedPaths) {
    requireManagedDevice(destination, managedPath, io);
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

function stagedInstallStatePath(transactionDir: string): string {
  return join(transactionDir, "staged-install.json");
}

function verifyStagedInstallState(transactionDir: string, journal: TransactionJournal): void {
  const immutable = join(transactionDir, "new-state.json");
  const staged = stagedInstallStatePath(transactionDir);
  requireOrdinaryFile(immutable, "immutable new Install state");
  requireOrdinaryFile(staged, "staged Install state");
  const expectedBytes = readFileSync(immutable);
  const stagedBytes = readFileSync(staged);
  if (
    hashBytes(expectedBytes) !== journal.newStateDigest ||
    hashBytes(stagedBytes) !== journal.newStateDigest ||
    !expectedBytes.equals(stagedBytes) ||
    stateDigest(parseInstallState(stagedBytes.toString("utf8"))) !== journal.newStateDigest
  ) {
    throw new Error("staged Install state bytes and digest do not match the Plan");
  }
}

function commitInstallState(
  destination: string,
  transactionDir: string,
  journal: TransactionJournal,
  seq: { value: number },
  options: ApplyOptions | undefined,
): void {
  const installPath = join(validateDestinationRoot(destination), ".deniz-skills", "install.json");
  const staged = stagedInstallStatePath(transactionDir);
  const backed = join(transactionDir, "backup-install.json");
  verifyStagedInstallState(transactionDir, journal);
  if (process.platform !== "win32" && options?.forceWindowsStateReplace !== true) {
    journal.intent = { syscall: "state-commit" };
    writeSnapshot(transactionDir, journal, seq);
    verifyStagedInstallState(transactionDir, journal);
    requireSameDevice(destination, transactionDir, options?.io);
    verifyStagedInstallState(transactionDir, journal);
    renameOrThrow(staged, installPath);
    throwIfSyscallCrash(options, "state-commit");
    delete journal.intent;
    return;
  }
  journal.intent = { syscall: "state-aside" };
  writeSnapshot(transactionDir, journal, seq);
  if (existsLstat(installPath)) {
    requireOrdinaryFile(installPath, "Install state");
    renameOrThrow(installPath, backed);
    throwIfSyscallCrash(options, "state-aside");
  }
  journal.stateAside = true;
  delete journal.intent;
  writeSnapshot(transactionDir, journal, seq);
  throwIfCrash(options, "after-state-aside");
  journal.intent = { syscall: "state-commit" };
  writeSnapshot(transactionDir, journal, seq);
  verifyStagedInstallState(transactionDir, journal);
  requireSameDevice(destination, transactionDir, options?.io);
  verifyStagedInstallState(transactionDir, journal);
  renameOrThrow(staged, installPath);
  throwIfSyscallCrash(options, "state-commit");
  delete journal.intent;
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

  const installPath = join(deniz, "install.json");
  if (existsLstat(installPath)) {
    requireOrdinaryFile(installPath, "Install state");
  }
  const oldBytes = existsLstat(installPath)
    ? readFileSync(installPath)
    : Buffer.from(serializeInstallState(EMPTY_INSTALL_STATE));
  if (hashBytes(oldBytes) !== stateDigest(current)) {
    throw new Error("Install state bytes are not in canonical serialized form");
  }

  requireDestinationTopology(root, deniz, options?.io);
  const transactionId = randomUUID();
  const transactionDir = join(deniz, `txn-${transactionId}`);
  const transactionCandidate = join(deniz, `txn.candidate-${transactionId}`);

  let committed = false;
  const seq = { value: 0 };
  const journal: TransactionJournal = {
    schemaVersion: 1,
    transactionId,
    oldStateDigest: stateDigest(current),
    newStateDigest: stateDigest(plan.nextState),
    operations: plan.operations,
    phase: "prepared",
    applied: [],
    createdDirectories: [],
    pruneCandidates: postCommitPruneCandidates(plan.operations),
    stateAside: false,
  };
  let transactionVisible = false;

  try {
    mkdirSync(transactionCandidate);
    requireOrdinaryDir(transactionCandidate, "transaction staging candidate");
    writeOwner(transactionCandidate, {
      pid: process.pid,
      startedAt: new Date().toISOString(),
      token: lock.token,
    });
    requireSameDevice(root, transactionCandidate, options?.io);
    throwIfCrash(options, "during-transaction-candidate");
    const newStateBytes = serializeInstallState(plan.nextState);
    writeFlushed(join(transactionCandidate, "old-state.json"), oldBytes);
    writeFlushed(join(transactionCandidate, "new-state.json"), newStateBytes);
    writeFlushed(stagedInstallStatePath(transactionCandidate), newStateBytes);
    writeSnapshot(transactionCandidate, journal, seq);
    fsyncDirectory(transactionCandidate);
    renameOrThrow(transactionCandidate, transactionDir);
    fsyncDirectory(deniz);
    transactionVisible = true;
    requireOrdinaryDir(transactionDir, "transaction");
    requireSameDevice(root, transactionDir, options?.io);
    throwIfCrash(options, "before-initial-staging");
    let stagedFiles = 0;
    for (const operation of plan.operations) {
      if (operation.kind === "add" || operation.kind === "replace") {
        stageBundleFile(transactionDir, bundles, operation);
        stagedFiles += 1;
        if (stagedFiles === 1) {
          throwIfCrash(options, "during-initial-staging");
        }
      }
    }

    for (const operation of plan.operations) {
      if (operation.kind !== "replace" && operation.kind !== "remove") {
        continue;
      }
      requireHeldLock(lock, destination);
      options?.beforeOperation?.(operation);
      recheckOperation(destination, current, operation, plan.request.platform);
      const destPath = validateManagedPath(destination, operation.path);
      requireManagedDevice(root, operation.path, options?.io);
      const oldIdentity = recordedIdentity(current, operation.path) ?? operation.identity;
      const operationIndex = plan.operations.indexOf(operation);
      journal.intent = { syscall: "backup", operationIndex, path: operation.path, identity: oldIdentity };
      writeSnapshot(transactionDir, journal, seq);
      const backup = backupFilePath(transactionDir, operation.path);
      mkdirSync(dirname(backup), { recursive: true });
      recheckOperation(destination, current, operation, plan.request.platform);
      requireManagedDevice(root, operation.path, options?.io);
      if (!identityMatches(observedFileIdentity(destPath), oldIdentity, hostPlatform())) {
        throw new Error(`${operation.path}: backup source does not match expected old bytes and mode`);
      }
      renameOrThrow(destPath, backup);
      if (!identityMatches(observedFileIdentity(backup), oldIdentity, hostPlatform())) {
        throw new Error(`backup of ${operation.path} does not match expected old bytes and mode`);
      }
      throwIfSyscallCrash(options, "backup");
      delete journal.intent;
      journal.applied.push({
        operationIndex,
        path: operation.path,
        kind: operation.kind,
        action: "backed-up",
        identity: oldIdentity,
      });
      writeSnapshot(transactionDir, journal, seq);
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
      requireManagedDevice(root, operation.path, options?.io);
      const staged = stagedFilePath(transactionDir, operation.path);
      verifyStagedFile(staged, operation.identity, operation.path);
      const operationIndex = plan.operations.indexOf(operation);
      journal.intent = { syscall: "place", operationIndex, path: operation.path, identity: operation.identity };
      writeSnapshot(transactionDir, journal, seq);
      verifyStagedFile(staged, operation.identity, operation.path);
      requireSameDevice(root, transactionDir, options?.io);
      requireManagedDevice(root, operation.path, options?.io);
      recheckOperation(destination, current, operation, plan.request.platform, true);
      verifyStagedFile(staged, operation.identity, operation.path);
      renameOrThrow(staged, destPath);
      if (!identityMatches(observedFileIdentity(destPath), operation.identity, hostPlatform())) {
        throw new Error(`${operation.path}: placed file bytes or mode do not match the Plan`);
      }
      throwIfSyscallCrash(options, "place");
      delete journal.intent;
      journal.applied.push({
        operationIndex,
        path: operation.path,
        kind: operation.kind,
        action: "placed",
        identity: operation.identity,
      });
      writeSnapshot(transactionDir, journal, seq);
    }
    for (const operation of plan.operations) {
      if (operation.kind !== "chmod") {
        continue;
      }
      requireHeldLock(lock, destination);
      options?.beforeOperation?.(operation);
      recheckOperation(destination, current, operation, plan.request.platform);
      const destPath = destAbs(destination, operation.path);
      requireManagedDevice(root, operation.path, options?.io);
      const recorded = recordedIdentity(current, operation.path);
      const identity: FileIdentity = {
        sha256: recorded?.sha256 ?? hashBytes(readFileSync(destPath)),
        mode: operation.to,
      };
      const operationIndex = plan.operations.indexOf(operation);
      journal.intent = { syscall: "chmod", operationIndex, path: operation.path, identity };
      writeSnapshot(transactionDir, journal, seq);
      recheckOperation(destination, current, operation, plan.request.platform);
      requireManagedDevice(root, operation.path, options?.io);
      recheckOperation(destination, current, operation, plan.request.platform);
      applyMode(destPath, operation.to);
      if (!identityMatches(observedFileIdentity(destPath), identity, hostPlatform())) {
        throw new Error(`${operation.path}: chmod result does not match expected content and mode`);
      }
      throwIfSyscallCrash(options, "chmod");
      delete journal.intent;
      journal.applied.push({
        operationIndex,
        path: operation.path,
        kind: operation.kind,
        action: "chmodded",
        identity,
      });
      writeSnapshot(transactionDir, journal, seq);
    }
    journal.phase = "files-placed";
    writeSnapshot(transactionDir, journal, seq);
    throwIfFail(options, "after-place");
    throwIfCrash(options, "after-place");

    requireHeldLock(lock, destination);
    for (const operation of plan.operations) {
      if (operation.kind === "drop-missing-claim") {
        options?.beforeOperation?.(operation);
        recheckOperation(destination, current, operation, plan.request.platform);
      }
    }
    const currentInstallExists = existsLstat(installPath);
    const exactOldState = readFileSync(join(transactionDir, "old-state.json"));
    if (
      stateDigest(loadInstallState(destination)) !== journal.oldStateDigest ||
      (currentInstallExists && !readFileSync(installPath).equals(exactOldState)) ||
      (!currentInstallExists && journal.oldStateDigest !== stateDigest(EMPTY_INSTALL_STATE))
    ) {
      throw new Error("Install-state digest changed before commit");
    }
    commitInstallState(destination, transactionDir, journal, seq, options);
    committed = true;
    journal.phase = "state-committed";
    journal.stateAside = false;
    delete journal.intent;
    writeSnapshot(transactionDir, journal, seq);
    throwIfFail(options, "after-state-commit");
    throwIfCrash(options, "after-state-commit");

    verifyFinalizeComplete(destination, transactionDir, journal);
    pruneRemovedDirectories(root, journal.pruneCandidates);
    pruneCreatedDirectories(root, journal.createdDirectories);
    removeTransactionDir(transactionDir);
  } catch (error) {
    if (!transactionVisible) {
      if (!isInjectedCrash(error) && existsLstat(transactionCandidate)) {
        removeTree(transactionCandidate, options?.io);
      }
      throw error;
    }
    if (!committed && !isInjectedCrash(error)) {
      try {
        const evidence = requireTransactionStateEvidence(transactionDir, journal);
        const journalSemantic = validateJournalAgainstStates(journal, evidence.oldState.state, evidence.newState.state);
        if (journalSemantic) {
          throw new Error(journalSemantic);
        }
        requireBackupEvidence(transactionDir, journal);
        preflightRollbackStateEvidence(destination, transactionDir, journal, evidence.oldState);
        preflightRollbackEvidence(destination, journal, evidence.oldState.state, evidence.newState.state);
        rollbackApplied(destination, transactionDir, journal, options?.io);
        pruneCreatedDirectories(destination, journal.createdDirectories);
        verifyRollbackComplete(destination, transactionDir, journal);
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

export function applyRecovery(
  lock: InstallerLock,
  destination: string,
  recovery: RecoveryPlan,
  options?: ApplyOptions,
): void {
  requireHeldLock(lock, destination);
  const { root, deniz } = ensureDestinationTree(destination);
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
  requireDestinationTopology(
    root,
    deniz,
    options?.io,
    current.journal.operations
      .filter((operation) => operation.kind !== "drop-missing-claim")
      .map((operation) => operation.path),
  );
  requireSameDevice(root, current.transactionDir, options?.io);
  if (current.kind === "finalize") {
    verifyFinalizeComplete(destination, current.transactionDir, current.journal);
    pruneRemovedDirectories(root, current.journal.pruneCandidates);
    removeTransactionDir(current.transactionDir);
    return;
  }
  requireBackupEvidence(current.transactionDir, current.journal);
  const evidence = requireTransactionStateEvidence(current.transactionDir, current.journal);
  preflightRollbackStateEvidence(destination, current.transactionDir, current.journal, evidence.oldState);
  preflightRollbackEvidence(destination, current.journal, evidence.oldState.state, evidence.newState.state);
  rollbackApplied(destination, current.transactionDir, current.journal, options?.io);
  verifyRollbackComplete(destination, current.transactionDir, current.journal);
  removeTransactionDir(current.transactionDir);
}
