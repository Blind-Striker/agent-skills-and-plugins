import { createHash } from "node:crypto";
import { existsSync, lstatSync, readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";
import { ordinalCompare } from "./order.ts";

export type FileMode = "100644" | "100755";
export type Sha256 = `sha256:${string}`;

export interface FileIdentity {
  sha256: Sha256;
  mode: FileMode;
}

export interface ModuleManifest {
  schemaVersion: 1;
  module: string;
  version: string;
  digest: Sha256;
  files: Record<string, FileIdentity>;
}

export interface ModuleBundle {
  root: string;
  manifest: ModuleManifest;
}

export interface BundleFinding {
  code:
    | "invalid_manifest"
    | "missing_file"
    | "extra_file"
    | "hash_mismatch"
    | "mode_mismatch"
    | "case_alias"
    | "symlink";
  path: string;
  message: string;
}

export interface VerifyModuleManifestOptions {
  /** Override the host default when verifying a Bundle for a case-insensitive Destination. */
  caseInsensitive?: boolean;
}

type ModeResolver = (path: string) => FileMode;

const SHA256 = /^sha256:[a-f0-9]{64}$/;

export function hashBytes(bytes: Uint8Array | string): Sha256 {
  return `sha256:${createHash("sha256").update(bytes).digest("hex")}`;
}

export function digestFileMap(files: Record<string, FileIdentity>): Sha256 {
  const payload = Object.entries(files)
    .sort(([a], [b]) => ordinalCompare(a, b))
    .map(([path, identity]) => `${path}\0${identity.sha256}\0${identity.mode}\n`)
    .join("");
  return hashBytes(payload);
}

interface WalkedPath {
  path: string;
  symlink: boolean;
}

function* walkTree(root: string, dir = root): Generator<WalkedPath> {
  const names = readdirSync(dir).sort(ordinalCompare);
  for (const name of names) {
    const path = join(dir, name);
    const stat = lstatSync(path);
    if (stat.isSymbolicLink()) {
      yield { path, symlink: true };
    } else if (stat.isDirectory()) {
      yield* walkTree(root, path);
    } else if (stat.isFile() && relativePath(root, path) !== "manifest.json") {
      yield { path, symlink: false };
    }
  }
}

function relativePath(root: string, path: string): string {
  const value = relative(root, path).replaceAll("\\", "/");
  const error = relativePathError(value);
  if (error) {
    throw new Error(`${path}: ${error}`);
  }
  return value;
}

function relativePathError(path: unknown): string | null {
  if (typeof path !== "string" || path.length === 0) {
    return "path must be a non-empty string";
  }
  if (path.includes("\\") || path.includes("\0") || path.startsWith("/")) {
    return "path must be POSIX root-relative";
  }
  const parts = path.split("/");
  if (
    parts.some((part) => part.length === 0 || part === "." || part === ".." || (part.length === 2 && part[1] === ":"))
  ) {
    return "path must not contain traversal or normalization segments";
  }
  return null;
}

function sortedFileMap(files: Record<string, FileIdentity>): Record<string, FileIdentity> {
  return Object.fromEntries(Object.entries(files).sort(([a], [b]) => ordinalCompare(a, b))) as Record<
    string,
    FileIdentity
  >;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isFileMode(value: unknown): value is FileMode {
  return value === "100644" || value === "100755";
}

function isSha256(value: unknown): value is Sha256 {
  return typeof value === "string" && SHA256.test(value);
}

function validateManifest(value: unknown): string | null {
  if (!isRecord(value)) {
    return "must be an object";
  }
  if (value.schemaVersion !== 1) {
    return "schemaVersion must be 1";
  }
  if (typeof value.module !== "string" || value.module.length === 0) {
    return "module must be a non-empty string";
  }
  if (typeof value.version !== "string" || value.version.length === 0) {
    return "version must be a non-empty string";
  }
  if (!isSha256(value.digest)) {
    return "digest must be a sha256 hash";
  }
  if (!isRecord(value.files)) {
    return "files must be an object";
  }

  for (const [path, identity] of Object.entries(value.files)) {
    const pathError = relativePathError(path);
    if (pathError) {
      return `files.${JSON.stringify(path)}: ${pathError}`;
    }
    if (path === "manifest.json") {
      return "files must not include manifest.json";
    }
    if (!isRecord(identity)) {
      return `files.${JSON.stringify(path)} must be an object`;
    }
    if (!isSha256(identity.sha256)) {
      return `files.${JSON.stringify(path)}.sha256 must be a sha256 hash`;
    }
    if (!isFileMode(identity.mode)) {
      return `files.${JSON.stringify(path)}.mode must be 100644 or 100755`;
    }
  }

  const files = value.files as Record<string, FileIdentity>;
  if (digestFileMap(files) !== value.digest) {
    return "digest does not match files";
  }
  return null;
}

function normalizedManifest(value: ModuleManifest): ModuleManifest {
  return {
    schemaVersion: 1,
    module: value.module,
    version: value.version,
    digest: value.digest,
    files: sortedFileMap(value.files),
  };
}

function requireValidManifest(value: unknown, source: string): ModuleManifest {
  const error = validateManifest(value);
  if (error) {
    throw new Error(`${source}: invalid Module manifest: ${error}`);
  }
  const manifest = value as Record<string, unknown>;
  return {
    schemaVersion: 1,
    module: manifest.module as string,
    version: manifest.version as string,
    digest: manifest.digest as Sha256,
    files: sortedFileMap(manifest.files as Record<string, FileIdentity>),
  };
}

export function createModuleManifest(
  root: string,
  module: string,
  version: string,
  resolveMode: ModeResolver,
): ModuleManifest {
  if (module.length === 0) {
    throw new Error("module must be a non-empty string");
  }
  if (version.length === 0) {
    throw new Error("version must be a non-empty string");
  }

  const files = Object.create(null) as Record<string, FileIdentity>;
  for (const entry of walkTree(root)) {
    const path = relativePath(root, entry.path);
    if (entry.symlink) {
      throw new Error(`${path}: bundle must not contain a symlink`);
    }
    const file = entry.path;
    const mode = resolveMode(path);
    if (!isFileMode(mode)) {
      throw new Error(`${path}: mode resolver must return 100644 or 100755`);
    }
    files[path] = { sha256: hashBytes(readFileSync(file)), mode };
  }
  const sortedFiles = sortedFileMap(files);
  return {
    schemaVersion: 1,
    module,
    version,
    digest: digestFileMap(sortedFiles),
    files: sortedFiles,
  };
}

export function loadModuleManifest(path: string): ModuleManifest {
  let raw: unknown;
  try {
    raw = JSON.parse(readFileSync(path, "utf8")) as unknown;
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`${path}: invalid Module manifest: ${message}`);
  }
  return requireValidManifest(raw, path);
}

function finding(code: BundleFinding["code"], path: string, message: string): BundleFinding {
  return { code, path, message };
}

function aliases(paths: string[]): Map<string, string[]> {
  const byFoldedPath = new Map<string, string[]>();
  for (const path of paths) {
    const folded = path.toLocaleLowerCase("en-US");
    const group = byFoldedPath.get(folded);
    if (group) {
      group.push(path);
    } else {
      byFoldedPath.set(folded, [path]);
    }
  }
  return byFoldedPath;
}

function appendAliasFindings(findings: BundleFinding[], paths: Map<string, string[]>): void {
  for (const group of [...paths.values()].filter((value) => value.length > 1)) {
    const sorted = [...group].sort(ordinalCompare);
    const first = sorted[0];
    if (!first) {
      continue;
    }
    for (const path of sorted.slice(1)) {
      findings.push(finding("case_alias", path, `${path} aliases ${first} on a case-insensitive filesystem`));
    }
  }
}

function observedMode(path: string): FileMode {
  return (statSync(path).mode & 0o111) === 0 ? "100644" : "100755";
}

export function verifyModuleManifest(
  root: string,
  manifest: ModuleManifest,
  options: VerifyModuleManifestOptions = {},
): BundleFinding[] {
  const manifestError = validateManifest(manifest);
  if (manifestError) {
    return [finding("invalid_manifest", "manifest.json", manifestError)];
  }

  const expected = normalizedManifest(manifest);
  const expectedPaths = Object.keys(expected.files);
  const actualPaths: string[] = [];
  const symlinkPaths: string[] = [];
  if (existsSync(root)) {
    for (const entry of walkTree(root)) {
      const path = relativePath(root, entry.path);
      if (entry.symlink) {
        symlinkPaths.push(path);
      } else {
        actualPaths.push(path);
      }
    }
  }
  const findings: BundleFinding[] = [];
  const caseInsensitive = options.caseInsensitive ?? process.platform === "win32";

  if (caseInsensitive) {
    const expectedAliases = aliases(expectedPaths);
    const actualAliases = aliases(actualPaths);
    appendAliasFindings(findings, expectedAliases);
    appendAliasFindings(findings, actualAliases);

    for (const path of expectedPaths) {
      const matching = actualAliases.get(path.toLocaleLowerCase("en-US"));
      if (!matching?.length) {
        findings.push(finding("missing_file", path, `${path} is listed by the manifest but is missing`));
        continue;
      }
      const actualPath = matching[0];
      if (!actualPath) {
        continue;
      }
      if (actualPath !== path) {
        findings.push(finding("case_alias", actualPath, `${actualPath} aliases manifest path ${path}`));
      }
      const identity = expected.files[path];
      if (!identity) {
        continue;
      }
      if (hashBytes(readFileSync(join(root, actualPath))) !== identity.sha256) {
        findings.push(finding("hash_mismatch", path, `${path} does not match its recorded sha256`));
      }
      if (process.platform !== "win32" && observedMode(join(root, actualPath)) !== identity.mode) {
        findings.push(finding("mode_mismatch", path, `${path} does not match its recorded mode`));
      }
    }
    for (const path of actualPaths) {
      if (!expectedAliases.has(path.toLocaleLowerCase("en-US"))) {
        findings.push(finding("extra_file", path, `${path} is not listed by the manifest`));
      }
    }
  } else {
    const actual = new Set(actualPaths);
    for (const path of expectedPaths) {
      if (!actual.has(path)) {
        findings.push(finding("missing_file", path, `${path} is listed by the manifest but is missing`));
        continue;
      }
      const identity = expected.files[path];
      if (!identity) {
        continue;
      }
      const file = join(root, path);
      if (hashBytes(readFileSync(file)) !== identity.sha256) {
        findings.push(finding("hash_mismatch", path, `${path} does not match its recorded sha256`));
      }
      if (process.platform !== "win32" && observedMode(file) !== identity.mode) {
        findings.push(finding("mode_mismatch", path, `${path} does not match its recorded mode`));
      }
    }
    for (const path of actualPaths) {
      if (!Object.hasOwn(expected.files, path)) {
        findings.push(finding("extra_file", path, `${path} is not listed by the manifest`));
      }
    }
  }

  for (const path of symlinkPaths) {
    findings.push(finding("symlink", path, `${path} is a symlink`));
  }
  return findings;
}

export function loadModuleBundles(opencodeRoot: string): Map<string, ModuleBundle> {
  const bundles = new Map<string, ModuleBundle>();
  const entries = readdirSync(opencodeRoot, { withFileTypes: true }).sort((a, b) => ordinalCompare(a.name, b.name));
  for (const entry of entries) {
    if (!entry.isDirectory()) {
      continue;
    }
    const root = join(opencodeRoot, entry.name);
    const manifest = loadModuleManifest(join(root, "manifest.json"));
    if (manifest.module !== entry.name) {
      throw new Error(`${root}: directory name ${entry.name} does not match Module manifest ${manifest.module}`);
    }
    bundles.set(manifest.module, { root, manifest });
  }
  return bundles;
}
