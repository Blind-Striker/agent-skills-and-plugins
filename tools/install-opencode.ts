#!/usr/bin/env node
import { lstatSync, readdirSync, readFileSync, realpathSync, rmdirSync } from "node:fs";
import { homedir } from "node:os";
import { basename, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  loadModuleBundles,
  verifyModuleManifest,
  type ModuleBundle,
  type ModuleManifest,
} from "./lib/opencode-bundle.ts";
import {
  acquireInstallerLock,
  applyPlan,
  applyRecovery,
  inspectRecovery,
  type InstallerLock,
  type RecoveryPlan,
} from "./lib/opencode-install-apply.ts";
import { planReconcile, type InstallRequest, type Plan, type PlanFinding } from "./lib/opencode-install-plan.ts";
import {
  loadInstallState,
  observePath,
  resolveDestination,
  type InstallState,
  type ObservedPath,
} from "./lib/opencode-install-state.ts";
import { ordinalCompare } from "./lib/order.ts";

export interface ParsedInstallArgs {
  action: "install" | "update" | "remove" | "status";
  modules: string[];
  all: boolean;
  yes: boolean;
}

export interface CliResult {
  exitCode: number;
  stdout: string;
  stderr: string;
}

export interface InstallCliIo {
  packageRoot: string;
  env: NodeJS.ProcessEnv;
  home: string;
  platform: "posix" | "windows";
}

interface LoadedBundles {
  bundles: Map<string, ModuleBundle>;
  manifests: Record<string, ModuleManifest>;
  findings: string[];
}

const ACTIONS = new Set(["install", "update", "remove", "status"]);

function uniqueSorted(names: string[]): string[] {
  return [...new Set(names)].sort(ordinalCompare);
}

function defaultIo(): InstallCliIo {
  return {
    packageRoot: join(dirname(fileURLToPath(import.meta.url)), ".."),
    env: process.env,
    home: process.env.HOME ?? process.env.USERPROFILE ?? homedir(),
    platform: process.platform === "win32" ? "windows" : "posix",
  };
}

function isENOENT(error: unknown): boolean {
  return error instanceof Error && "code" in error && (error as NodeJS.ErrnoException).code === "ENOENT";
}

function pathExists(path: string): boolean {
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

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function sanitizePackagePaths(message: string, packageRoot: string): string {
  const root = resolve(packageRoot);
  if (root.length < 2) {
    return message;
  }
  const prefixes = [...new Set([root, root.replaceAll("\\", "/"), root.replaceAll("/", "\\")])].sort(
    (left, right) => right.length - left.length,
  );
  let result = message;
  const flags = process.platform === "win32" ? "gi" : "g";
  for (const prefix of prefixes) {
    result = result.replace(new RegExp(`${escapeRegExp(prefix)}[\\\\/]?`, flags), "");
  }
  return result.replaceAll("\\", "/");
}

function fail(error: unknown, packageRoot: string): CliResult {
  const message = error instanceof Error ? error.message : String(error);
  return { exitCode: 1, stdout: "", stderr: `${sanitizePackagePaths(message, packageRoot)}\n` };
}

export function parseInstallArgs(argv: string[]): ParsedInstallArgs {
  const action = argv[0];
  if (action === undefined || !ACTIONS.has(action)) {
    throw new Error(
      action === undefined
        ? "usage: install|update|remove|status [--module <name>] [--all] [--yes]"
        : `unknown action ${action}`,
    );
  }
  const modules: string[] = [];
  let all = false;
  let yes = false;
  for (let index = 1; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === "--yes") {
      yes = true;
      continue;
    }
    if (arg === "--all") {
      all = true;
      continue;
    }
    if (arg === "--module") {
      const name = argv[index + 1];
      if (name === undefined || name.startsWith("-")) {
        throw new Error("--module requires a name");
      }
      modules.push(name);
      index += 1;
      continue;
    }
    throw new Error(`unknown flag ${arg}`);
  }
  if (all && modules.length > 0) {
    throw new Error("--all cannot be combined with --module");
  }
  if (action === "update" && (all || modules.length > 0)) {
    throw new Error("update does not accept --module or --all");
  }
  if (action === "status" && (all || modules.length > 0 || yes)) {
    throw new Error("status does not accept --module, --all, or --yes");
  }
  return { action: action as ParsedInstallArgs["action"], modules, all, yes };
}

function formatFinding(finding: PlanFinding): string {
  const parts: string[] = [finding.code];
  if (finding.path !== undefined) {
    parts.push(finding.path);
  }
  if (finding.module !== undefined) {
    parts.push(finding.module);
  }
  parts.push(finding.message);
  return parts.join(" ");
}

function appendSection(lines: string[], title: string, entries: string[]): void {
  if (entries.length === 0) {
    return;
  }
  lines.push("", title, ...entries);
}

function moduleStateLabel(state: InstallState, name: string): string {
  const moduleState = state.modules[name];
  return moduleState ? `${moduleState.version} ${moduleState.digest}` : "unselected";
}

function moduleSnapshot(state: InstallState, name: string): string {
  const moduleState = state.modules[name] ?? null;
  const files = Object.entries(state.files)
    .filter(([, owned]) => owned.module === name)
    .sort(([left], [right]) => ordinalCompare(left, right));
  return JSON.stringify({ moduleState, files });
}

function moduleNamesForPlan(plan: Plan): string[] {
  const names = new Set<string>(plan.affectedModules);
  for (const operation of plan.operations) {
    names.add(operation.module);
  }
  for (const transfer of plan.transfers) {
    names.add(transfer.fromModule);
    names.add(transfer.toModule);
  }
  for (const finding of plan.findings) {
    if (finding.module) {
      names.add(finding.module);
    }
  }
  return [...names].sort(ordinalCompare);
}

function appendModuleSection(lines: string[], title: string, entries: string[]): void {
  if (entries.length > 0) {
    lines.push(`  ${title}:`, ...entries.map((entry) => `    ${entry}`));
  }
}

export function renderPlan(plan: Plan, destination: string): string {
  const lines = [`Plan: ${plan.request.kind}`, `Destination: ${destination}`];
  const moduleNames = moduleNamesForPlan(plan);
  for (const name of moduleNames) {
    lines.push("", `Module: ${name}`);
    const wasSelected = Object.hasOwn(plan.currentState.modules, name);
    const willBeSelected = Object.hasOwn(plan.nextState.modules, name);
    const selection = !wasSelected && willBeSelected ? "add" : wasSelected && !willBeSelected ? "remove" : "unchanged";
    lines.push(`  Selection: ${selection}`);
    const oldState = moduleStateLabel(plan.currentState, name);
    const newState = moduleStateLabel(plan.nextState, name);
    lines.push(`  State: ${oldState === newState ? oldState : `${oldState} -> ${newState}`}`);

    const operations = plan.operations.filter((operation) => operation.module === name);
    const operationPaths = (kind: Plan["operations"][number]["kind"]): string[] =>
      operations
        .filter((operation) => operation.kind === kind)
        .sort((left, right) => ordinalCompare(left.path, right.path))
        .map((operation) => operation.path);
    appendModuleSection(lines, "Add", operationPaths("add"));
    appendModuleSection(lines, "Replace", operationPaths("replace"));
    appendModuleSection(
      lines,
      "Mode",
      operations
        .filter((operation) => operation.kind === "chmod")
        .sort((left, right) => ordinalCompare(left.path, right.path))
        .map((operation) => `${operation.path} ${operation.from} -> ${operation.to}`),
    );
    appendModuleSection(lines, "Remove", operationPaths("remove"));
    appendModuleSection(lines, "Drop missing claims", operationPaths("drop-missing-claim"));
    const transfers = plan.transfers
      .filter((transfer) => transfer.fromModule === name || transfer.toModule === name)
      .sort((left, right) => ordinalCompare(left.path, right.path))
      .map((transfer) => `${transfer.path} ${transfer.fromModule} -> ${transfer.toModule}`);
    appendModuleSection(lines, "Ownership transfers", transfers);
    const findings = plan.findings.filter((finding) => finding.module === name).map(formatFinding);
    appendModuleSection(lines, "Findings", findings);

    const stateChanged = moduleSnapshot(plan.currentState, name) !== moduleSnapshot(plan.nextState, name);
    if (!stateChanged && operations.length === 0 && transfers.length === 0 && findings.length === 0) {
      lines.push("  No changes.");
    }
  }
  const globalFindings = plan.findings.filter((finding) => finding.module === undefined);
  if (globalFindings.length > 0) {
    appendSection(
      lines,
      "Findings:",
      globalFindings.map((finding) => `  ${formatFinding(finding)}`),
    );
  }
  return `${lines.join("\n")}\n`;
}

function renderRecovery(recovery: RecoveryPlan, destination: string): string {
  const lines = ["Recovery", `Destination: ${destination}`, `Recovery: ${recovery.kind}`];
  if (recovery.kind === "blocked") {
    lines.push(`message: ${recovery.message}`);
  } else {
    lines.push(`transaction: ${basename(recovery.transactionDir)}`);
  }
  return `${lines.join("\n")}\n`;
}

function renderStatus(
  destination: string,
  current: InstallState,
  manifests: Record<string, ModuleManifest>,
  plan: Plan | null,
  lock: string,
  recovery: RecoveryPlan | null,
): string {
  const lines = ["Status", `Destination: ${destination}`, "", "Selection:"];
  const names = uniqueSorted(Object.keys(current.modules));
  if (names.length === 0) {
    lines.push("  (empty)");
  } else {
    for (const name of names) {
      const moduleState = current.modules[name];
      if (moduleState === undefined) {
        continue;
      }
      const payload = manifests[name];
      const currency = payload !== undefined && payload.digest === moduleState.digest ? "current" : "differs";
      lines.push(`  ${name} ${moduleState.version} ${moduleState.digest} ${currency}`);
    }
  }
  if (plan !== null && plan.findings.length > 0) {
    appendSection(
      lines,
      "Findings:",
      plan.findings.map((finding) => `  ${formatFinding(finding)}`),
    );
  }
  lines.push("", `Lock: ${lock}`, `Recovery: ${recovery?.kind ?? "none"}`);
  if (recovery?.kind === "blocked") {
    lines.push(`message: ${recovery.message}`);
  }
  return `${lines.join("\n")}\n`;
}

function observeRequiredPaths(
  destination: string,
  current: InstallState,
  manifests: Record<string, ModuleManifest>,
): Record<string, ObservedPath> {
  const paths = new Set<string>();
  for (const path of Object.keys(current.files)) {
    paths.add(path);
  }
  for (const manifest of Object.values(manifests)) {
    for (const path of Object.keys(manifest.files)) {
      paths.add(path);
    }
  }
  const observed = Object.create(null) as Record<string, ObservedPath>;
  for (const path of [...paths].sort(ordinalCompare)) {
    observed[path] = observePath(destination, path);
  }
  return observed;
}

function loadVerifiedBundles(packageRoot: string, platform: InstallCliIo["platform"]): LoadedBundles {
  const bundles = loadModuleBundles(join(packageRoot, "opencode"));
  const manifests = Object.create(null) as Record<string, ModuleManifest>;
  const findings: string[] = [];
  for (const name of [...bundles.keys()].sort(ordinalCompare)) {
    const bundle = bundles.get(name);
    if (bundle === undefined) {
      continue;
    }
    manifests[name] = bundle.manifest;
    for (const finding of verifyModuleManifest(bundle.root, bundle.manifest, {
      caseInsensitive: platform === "windows",
    })) {
      findings.push(`${finding.code} ${name} ${finding.path}: ${finding.message}`);
    }
  }
  return { bundles, manifests, findings };
}

function requestFrom(args: ParsedInstallArgs, platform: InstallCliIo["platform"]): InstallRequest {
  return {
    kind: args.action === "status" ? "update" : args.action,
    modules: args.modules,
    all: args.all,
    platform,
  };
}

function ownerDirectoryStatus(path: string): "active" | "stale" {
  try {
    const stat = lstatSync(path);
    if (stat.isSymbolicLink() || !stat.isDirectory()) {
      return "stale";
    }
    const ownerPath = join(path, "owner.json");
    const ownerStat = lstatSync(ownerPath);
    if (ownerStat.isSymbolicLink() || !ownerStat.isFile()) {
      return "stale";
    }
    const raw: unknown = JSON.parse(readFileSync(ownerPath, "utf8"));
    if (typeof raw !== "object" || raw === null || !("pid" in raw) || typeof raw.pid !== "number") {
      return "stale";
    }
    try {
      process.kill(raw.pid, 0);
      return "active";
    } catch (error) {
      if (error instanceof Error && "code" in error && (error as NodeJS.ErrnoException).code === "EPERM") {
        return "active";
      }
      return "stale";
    }
  } catch {
    return "stale";
  }
}

function lockStatus(destination: string): "none" | "held" | "abandoned" | "guard-active" | "guard-stale" {
  const guardPath = join(destination, ".deniz-skills", "lock.reclaim");
  if (pathExists(guardPath)) {
    return ownerDirectoryStatus(guardPath) === "active" ? "guard-active" : "guard-stale";
  }
  const lockPath = join(destination, ".deniz-skills", "lock");
  try {
    const stat = lstatSync(lockPath);
    if (stat.isSymbolicLink() || !stat.isDirectory()) {
      return "abandoned";
    }
  } catch {
    return "none";
  }
  return ownerDirectoryStatus(lockPath) === "active" ? "held" : "abandoned";
}

function runStatus(destination: string, loaded: LoadedBundles, platform: InstallCliIo["platform"]): CliResult {
  const recovery = inspectRecovery(destination);
  const current = loadInstallState(destination);
  const lock = lockStatus(destination);
  let plan: Plan | null = null;
  if (Object.keys(current.modules).length > 0) {
    plan = planReconcile(current, loaded.manifests, observeRequiredPaths(destination, current, loaded.manifests), {
      kind: "update",
      modules: [],
      all: false,
      platform,
    });
  }
  const stdout = renderStatus(destination, current, loaded.manifests, plan, lock, recovery);
  const blocked = recovery?.kind === "blocked" || (plan !== null && plan.findings.length > 0);
  return { exitCode: blocked ? 1 : 0, stdout, stderr: "" };
}

function directoryNames(path: string): string[] {
  try {
    return readdirSync(path);
  } catch (error) {
    if (isENOENT(error)) {
      return [];
    }
    throw error;
  }
}

function isEmptyScaffolding(path: string): boolean {
  return directoryNames(path).length === 0;
}

function removeIfEmpty(path: string): void {
  try {
    rmdirSync(path);
  } catch {
    return;
  }
}

function removeCreatedScaffolding(destination: string, destExisted: boolean, denizExisted: boolean): void {
  const deniz = join(destination, ".deniz-skills");
  if (!denizExisted && pathExists(deniz) && isEmptyScaffolding(deniz)) {
    removeIfEmpty(deniz);
  }
  if (!destExisted && pathExists(destination)) {
    const names = directoryNames(destination);
    if (names.length === 0 || (names.length === 1 && names[0] === ".deniz-skills" && isEmptyScaffolding(deniz))) {
      if (pathExists(deniz) && isEmptyScaffolding(deniz)) {
        removeIfEmpty(deniz);
      }
      if (isEmptyScaffolding(destination)) {
        removeIfEmpty(destination);
      }
    }
  }
}

function applyLockedRecovery(lock: InstallerLock, destination: string, recovery: RecoveryPlan): CliResult {
  if (recovery.kind === "blocked") {
    return { exitCode: 1, stdout: renderRecovery(recovery, destination), stderr: "" };
  }
  applyRecovery(lock, destination, recovery);
  return { exitCode: 0, stdout: `${renderRecovery(recovery, destination)}Recovered.\n`, stderr: "" };
}

function runMutation(args: ParsedInstallArgs, destination: string, loaded: LoadedBundles, io: InstallCliIo): CliResult {
  if (!args.yes) {
    const recovery = inspectRecovery(destination);
    if (recovery) {
      const rendered = renderRecovery(recovery, destination);
      return { exitCode: recovery.kind === "blocked" ? 1 : 0, stdout: rendered, stderr: "" };
    }
    const current = loadInstallState(destination);
    const plan = planReconcile(
      current,
      loaded.manifests,
      observeRequiredPaths(destination, current, loaded.manifests),
      requestFrom(args, io.platform),
    );
    return { exitCode: plan.findings.length > 0 ? 1 : 0, stdout: renderPlan(plan, destination), stderr: "" };
  }

  const destExisted = pathExists(destination);
  const denizExisted = pathExists(join(destination, ".deniz-skills"));
  const recoveryPeek = inspectRecovery(destination);
  let lock: InstallerLock | undefined;
  try {
    lock = acquireInstallerLock(destination, recoveryPeek ? { recover: true } : {});
    const recovery = inspectRecovery(destination);
    if (recovery) {
      return applyLockedRecovery(lock, destination, recovery);
    }

    const current = loadInstallState(destination);
    const plan = planReconcile(
      current,
      loaded.manifests,
      observeRequiredPaths(destination, current, loaded.manifests),
      requestFrom(args, io.platform),
    );
    const rendered = renderPlan(plan, destination);
    if (plan.findings.length > 0) {
      return { exitCode: 1, stdout: rendered, stderr: "" };
    }
    applyPlan(lock, destination, plan, loaded.bundles);
    return { exitCode: 0, stdout: rendered, stderr: "" };
  } finally {
    lock?.release();
    removeCreatedScaffolding(destination, destExisted, denizExisted);
  }
}

function execute(argv: string[], io: InstallCliIo): CliResult {
  const args = parseInstallArgs(argv);
  const loaded = loadVerifiedBundles(io.packageRoot, io.platform);
  if (loaded.findings.length > 0) {
    return {
      exitCode: 1,
      stdout: `Findings:\n${loaded.findings.map((finding) => `  ${finding}`).join("\n")}\n`,
      stderr: "",
    };
  }
  const destination = resolveDestination(io.env, io.home);
  if (args.action === "status") {
    return runStatus(destination, loaded, io.platform);
  }
  return runMutation(args, destination, loaded, io);
}

export async function runInstallCli(argv: string[], io?: InstallCliIo): Promise<CliResult> {
  const resolved = io ?? defaultIo();
  try {
    return execute(argv, resolved);
  } catch (error) {
    return fail(error, resolved.packageRoot);
  }
}

/**
 * True when the process was launched with this module as its entry point. POSIX npm installs
 * expose the bin as a symlink: `import.meta.url` resolves to the physical module while
 * `process.argv[1]` keeps the symlink path, so a raw string comparison would exit silently.
 * Comparing canonical real paths makes the check symlink-safe; when either path cannot be
 * resolved (e.g. this module was imported, or the entry was removed) this returns false so the
 * imported module never auto-runs.
 */
export function isDirectEntryPoint(argv1: string | undefined, moduleUrl: string): boolean {
  if (argv1 === undefined || argv1.length === 0) {
    return false;
  }
  try {
    return realpathSync(argv1) === realpathSync(fileURLToPath(moduleUrl));
  } catch {
    return false;
  }
}

if (isDirectEntryPoint(process.argv[1], import.meta.url)) {
  const result = await runInstallCli(process.argv.slice(2));
  if (result.stdout.length > 0) {
    process.stdout.write(result.stdout);
  }
  if (result.stderr.length > 0) {
    process.stderr.write(result.stderr);
  }
  process.exitCode = result.exitCode;
}
