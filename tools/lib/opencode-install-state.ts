import { lstatSync, readFileSync, type Stats } from "node:fs";
import { isAbsolute, join, relative, resolve } from "node:path";
import { hashBytes, type FileIdentity, type FileMode, type Sha256 } from "./opencode-bundle.ts";
import { ordinalCompare } from "./order.ts";

export interface ModuleState {
  version: string;
  digest: Sha256;
}

export interface OwnedFile extends FileIdentity {
  module: string;
}

export interface InstallState {
  schemaVersion: 1;
  modules: Record<string, ModuleState>;
  files: Record<string, OwnedFile>;
}

export const EMPTY_INSTALL_STATE: InstallState = Object.freeze({
  schemaVersion: 1,
  modules: Object.freeze({}),
  files: Object.freeze({}),
});

export type ObservedPath =
  | { kind: "absent" }
  | { kind: "file"; identity: FileIdentity }
  | { kind: "directory" }
  | { kind: "link" }
  | { kind: "blocked"; path: string; actual: "file" | "special" };

export interface ParseInstallStateOptions {
  caseInsensitive?: boolean;
}

const SHA256 = /^sha256:[a-f0-9]{64}$/;
const NATIVE_ROOTS = new Set(["agents", "commands", "skills"]);
const DISTRIBUTION_ROOT_FILES = new Set(["LICENSE", "THIRD_PARTY_NOTICES.md"]);
const THIRD_PARTY_LICENSE = /^third_party\/[a-z0-9-]+\/LICENSE$/;
const TOP_LEVEL_KEYS = new Set(["files", "modules", "schemaVersion"]);
const MODULE_STATE_KEYS = new Set(["digest", "version"]);
const OWNED_FILE_KEYS = new Set(["mode", "module", "sha256"]);

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isFileMode(value: unknown): value is FileMode {
  return value === "100644" || value === "100755";
}

function isSha256(value: unknown): value is Sha256 {
  return typeof value === "string" && SHA256.test(value);
}

function isENOENT(error: unknown): boolean {
  return error instanceof Error && "code" in error && (error as NodeJS.ErrnoException).code === "ENOENT";
}

function nonEmpty(value: string | undefined): value is string {
  return typeof value === "string" && value.length > 0;
}

function unknownField(prefix: string, key: string): string {
  return `${prefix}unknown field ${JSON.stringify(key)}`;
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

function nativeTreePathError(path: unknown): string | null {
  const error = relativePathError(path);
  if (error) {
    return error;
  }
  const parts = (path as string).split("/");
  const root = parts[0];
  if (!root || !NATIVE_ROOTS.has(root) || parts.length < 2) {
    return "path must be under skills/, commands/, or agents/";
  }
  return null;
}

export function isNativeTreePath(path: string): boolean {
  return nativeTreePathError(path) === null;
}

export function isDistributionMetadataPath(path: string): boolean {
  return relativePathError(path) === null && (DISTRIBUTION_ROOT_FILES.has(path) || THIRD_PARTY_LICENSE.test(path));
}

function firstCaseAlias(paths: string[]): { path: string; first: string } | null {
  const seen = new Map<string, string>();
  for (const path of paths) {
    const folded = path.toLocaleLowerCase("en-US");
    const first = seen.get(folded);
    if (first !== undefined && first !== path) {
      return { path, first };
    }
    if (first === undefined) {
      seen.set(folded, path);
    }
  }
  return null;
}

function validateInstallState(value: unknown, caseInsensitive: boolean): string | null {
  if (!isRecord(value)) {
    return "must be an object";
  }
  for (const key of Object.keys(value)) {
    if (!TOP_LEVEL_KEYS.has(key)) {
      return unknownField("", key);
    }
  }
  if (value.schemaVersion !== 1) {
    return "schemaVersion must be 1";
  }
  if (!isRecord(value.modules)) {
    return "modules must be an object";
  }
  if (!isRecord(value.files)) {
    return "files must be an object";
  }

  const moduleNames = new Set<string>();
  for (const [name, moduleState] of Object.entries(value.modules)) {
    if (name.length === 0 || name.includes("/") || name.includes("\\") || name.includes("\0")) {
      return `modules.${JSON.stringify(name)}: invalid Module name`;
    }
    if (!isRecord(moduleState)) {
      return `modules.${JSON.stringify(name)} must be an object`;
    }
    for (const key of Object.keys(moduleState)) {
      if (!MODULE_STATE_KEYS.has(key)) {
        return unknownField(`modules.${JSON.stringify(name)}.`, key);
      }
    }
    if (typeof moduleState.version !== "string" || moduleState.version.length === 0) {
      return `modules.${JSON.stringify(name)}.version must be a non-empty string`;
    }
    if (!isSha256(moduleState.digest)) {
      return `modules.${JSON.stringify(name)}.digest must be a sha256 hash`;
    }
    moduleNames.add(name);
  }

  for (const [path, owned] of Object.entries(value.files)) {
    const pathError = nativeTreePathError(path);
    if (pathError) {
      return `files.${JSON.stringify(path)}: ${pathError}`;
    }
    if (!isRecord(owned)) {
      return `files.${JSON.stringify(path)} must be an object`;
    }
    for (const key of Object.keys(owned)) {
      if (!OWNED_FILE_KEYS.has(key)) {
        return unknownField(`files.${JSON.stringify(path)}.`, key);
      }
    }
    if (typeof owned.module !== "string" || owned.module.length === 0) {
      return `files.${JSON.stringify(path)}.module must be a non-empty string`;
    }
    if (!moduleNames.has(owned.module)) {
      return `files.${JSON.stringify(path)}.module is not a selected Module`;
    }
    if (!isSha256(owned.sha256)) {
      return `files.${JSON.stringify(path)}.sha256 must be a sha256 hash`;
    }
    if (!isFileMode(owned.mode)) {
      return `files.${JSON.stringify(path)}.mode must be 100644 or 100755`;
    }
  }

  if (caseInsensitive) {
    const alias = firstCaseAlias(Object.keys(value.files));
    if (alias) {
      return `${alias.path} is a case alias of ${alias.first}`;
    }
  }

  return null;
}

function sortedRecord<T>(record: Record<string, T>): Record<string, T> {
  return Object.fromEntries(Object.entries(record).sort(([left], [right]) => ordinalCompare(left, right))) as Record<
    string,
    T
  >;
}

function normalizeState(state: InstallState): InstallState {
  const modules = Object.create(null) as Record<string, ModuleState>;
  for (const [name, moduleState] of Object.entries(sortedRecord(state.modules))) {
    modules[name] = { version: moduleState.version, digest: moduleState.digest };
  }
  const files = Object.create(null) as Record<string, OwnedFile>;
  for (const [path, owned] of Object.entries(sortedRecord(state.files))) {
    files[path] = { module: owned.module, sha256: owned.sha256, mode: owned.mode };
  }
  return { schemaVersion: 1, modules, files };
}

function isHex(char: string): boolean {
  return (char >= "0" && char <= "9") || (char >= "a" && char <= "f") || (char >= "A" && char <= "F");
}

function parseJsonString(raw: string, start: number): { value: string; next: number } {
  if (raw[start] !== '"') {
    throw new SyntaxError("JSON string must start with a quote");
  }
  let index = start + 1;
  let value = "";
  while (index < raw.length) {
    const char = raw[index];
    if (char === '"') {
      return { value, next: index + 1 };
    }
    if (char === "\\") {
      const marker = raw[index + 1];
      if (marker === undefined) {
        throw new SyntaxError("unterminated JSON string escape");
      }
      if (marker === "u") {
        const hex = raw.slice(index + 2, index + 6);
        if (hex.length < 4 || ![...hex].every(isHex)) {
          throw new SyntaxError("invalid JSON unicode escape");
        }
        value += String.fromCharCode(Number.parseInt(hex, 16));
        index += 6;
        continue;
      }
      const escaped =
        marker === '"' || marker === "\\" || marker === "/"
          ? marker
          : marker === "b"
            ? "\b"
            : marker === "f"
              ? "\f"
              : marker === "n"
                ? "\n"
                : marker === "r"
                  ? "\r"
                  : marker === "t"
                    ? "\t"
                    : null;
      if (escaped === null) {
        throw new SyntaxError("invalid JSON string escape");
      }
      value += escaped;
      index += 2;
      continue;
    }
    if (char === undefined || char.charCodeAt(0) < 0x20) {
      throw new SyntaxError("unescaped control character in JSON string");
    }
    value += char;
    index += 1;
  }
  throw new SyntaxError("unterminated JSON string");
}

function skipJsonWhitespace(raw: string, index: number): number {
  while (index < raw.length) {
    const char = raw[index];
    if (char !== " " && char !== "\t" && char !== "\n" && char !== "\r") {
      break;
    }
    index += 1;
  }
  return index;
}

function isJsonDigit(char: string | undefined): char is string {
  return char !== undefined && char >= "0" && char <= "9";
}

function skipJsonNumber(raw: string, start: number): number {
  let index = start;
  if (raw[index] === "-") {
    index += 1;
  }
  const first = raw[index];
  if (first === "0") {
    index += 1;
  } else if (first !== undefined && first >= "1" && first <= "9") {
    index += 1;
    while (isJsonDigit(raw[index])) {
      index += 1;
    }
  } else {
    throw new SyntaxError("invalid JSON number");
  }
  if (raw[index] === ".") {
    index += 1;
    if (!isJsonDigit(raw[index])) {
      throw new SyntaxError("invalid JSON number");
    }
    index += 1;
    while (isJsonDigit(raw[index])) {
      index += 1;
    }
  }
  if (raw[index] === "e" || raw[index] === "E") {
    index += 1;
    if (raw[index] === "+" || raw[index] === "-") {
      index += 1;
    }
    if (!isJsonDigit(raw[index])) {
      throw new SyntaxError("invalid JSON number");
    }
    index += 1;
    while (isJsonDigit(raw[index])) {
      index += 1;
    }
  }
  return index;
}

function skipJsonLiteral(raw: string, start: number, literal: string): number {
  if (raw.slice(start, start + literal.length) !== literal) {
    throw new SyntaxError("invalid JSON literal");
  }
  return start + literal.length;
}

function rejectDuplicateJsonMembers(raw: string, index: number): number {
  let cursor = skipJsonWhitespace(raw, index);
  const char = raw[cursor];
  if (char === '"') {
    return parseJsonString(raw, cursor).next;
  }
  if (char === "-" || (char !== undefined && char >= "0" && char <= "9")) {
    return skipJsonNumber(raw, cursor);
  }
  if (char === "t") {
    return skipJsonLiteral(raw, cursor, "true");
  }
  if (char === "f") {
    return skipJsonLiteral(raw, cursor, "false");
  }
  if (char === "n") {
    return skipJsonLiteral(raw, cursor, "null");
  }
  if (char === "[") {
    cursor = skipJsonWhitespace(raw, cursor + 1);
    if (raw[cursor] === "]") {
      return cursor + 1;
    }
    while (true) {
      cursor = rejectDuplicateJsonMembers(raw, cursor);
      cursor = skipJsonWhitespace(raw, cursor);
      if (raw[cursor] === ",") {
        cursor += 1;
        continue;
      }
      if (raw[cursor] === "]") {
        return cursor + 1;
      }
      throw new SyntaxError("invalid JSON array");
    }
  }
  if (char === "{") {
    cursor = skipJsonWhitespace(raw, cursor + 1);
    if (raw[cursor] === "}") {
      return cursor + 1;
    }
    const keys = new Set<string>();
    while (true) {
      cursor = skipJsonWhitespace(raw, cursor);
      if (raw[cursor] !== '"') {
        throw new SyntaxError("JSON object member must start with a string key");
      }
      const parsed = parseJsonString(raw, cursor);
      if (keys.has(parsed.value)) {
        throw new Error(`duplicate object member ${JSON.stringify(parsed.value)}`);
      }
      keys.add(parsed.value);
      cursor = skipJsonWhitespace(raw, parsed.next);
      if (raw[cursor] !== ":") {
        throw new SyntaxError("JSON object member must have a colon");
      }
      cursor = rejectDuplicateJsonMembers(raw, cursor + 1);
      cursor = skipJsonWhitespace(raw, cursor);
      if (raw[cursor] === ",") {
        cursor += 1;
        continue;
      }
      if (raw[cursor] === "}") {
        return cursor + 1;
      }
      throw new SyntaxError("invalid JSON object");
    }
  }
  throw new SyntaxError("invalid JSON value");
}

function parseJsonUniqueMembers(raw: string): unknown {
  const next = rejectDuplicateJsonMembers(raw, 0);
  if (skipJsonWhitespace(raw, next) !== raw.length) {
    throw new SyntaxError("unexpected trailing JSON");
  }
  return JSON.parse(raw) as unknown;
}

function requireValidState(value: unknown, caseInsensitive: boolean, source?: string): InstallState {
  const error = validateInstallState(value, caseInsensitive);
  if (error) {
    const prefix = source ? `${source}: ` : "";
    throw new Error(`${prefix}invalid Install state: ${error}`);
  }
  return normalizeState(value as InstallState);
}

export function parseInstallState(raw: string, options: ParseInstallStateOptions = {}): InstallState {
  let value: unknown;
  try {
    value = parseJsonUniqueMembers(raw);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`invalid Install state: ${message}`);
  }
  return requireValidState(value, options.caseInsensitive ?? process.platform === "win32");
}

export function serializeInstallState(state: InstallState): string {
  const normalized = requireValidState(state, process.platform === "win32");
  return `${JSON.stringify(normalized, null, 2)}\n`;
}

export function stateDigest(state: InstallState): Sha256 {
  return hashBytes(serializeInstallState(state));
}

export function loadInstallState(destination: string, options: ParseInstallStateOptions = {}): InstallState {
  const root = validateDestinationRoot(destination);
  const metadata = join(root, ".deniz-skills");
  try {
    const stat = lstatSync(metadata);
    if (isLinkLike(stat) || !stat.isDirectory()) {
      throw new Error(`${metadata}: .deniz-skills must be an ordinary directory without links`);
    }
  } catch (error) {
    if (isENOENT(error)) {
      return EMPTY_INSTALL_STATE;
    }
    throw error;
  }

  const path = join(metadata, "install.json");
  try {
    const stat = lstatSync(path);
    if (isLinkLike(stat) || !stat.isFile()) {
      throw new Error(`${path}: Install state must be an ordinary file without links`);
    }
  } catch (error) {
    if (isENOENT(error)) {
      return EMPTY_INSTALL_STATE;
    }
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`${path}: unreadable Install state: ${message}`);
  }

  let raw: string;
  try {
    raw = readFileSync(path, "utf8");
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`${path}: unreadable Install state: ${message}`);
  }

  try {
    return parseInstallState(raw, options);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`${path}: ${message}`);
  }
}

export function resolveDestination(env: Record<string, string | undefined>, home?: string): string {
  if (nonEmpty(env.OPENCODE_CONFIG_DIR)) {
    throw new Error("OPENCODE_CONFIG_DIR is set; unset it or do not use this installer");
  }
  if (nonEmpty(env.XDG_CONFIG_HOME)) {
    return join(env.XDG_CONFIG_HOME, "opencode");
  }
  const resolvedHome = nonEmpty(env.HOME) ? env.HOME : home;
  if (!nonEmpty(resolvedHome)) {
    throw new Error("HOME is not set");
  }
  return join(resolvedHome, ".config", "opencode");
}

function isLinkLike(stat: Stats): boolean {
  return stat.isSymbolicLink();
}

function observedMode(stat: Stats): FileMode {
  return (stat.mode & 0o111) === 0 ? "100644" : "100755";
}

function requireContained(root: string, candidate: string): string {
  const escaped = relative(root, candidate);
  if (escaped === "" || escaped.startsWith("..") || isAbsolute(escaped)) {
    throw new Error(`${candidate} escapes Destination ${root}`);
  }
  return candidate;
}

export function validateDestinationRoot(destination: string): string {
  const absolute = resolve(destination);
  let stat: Stats;
  try {
    stat = lstatSync(absolute);
  } catch (error) {
    if (isENOENT(error)) {
      return absolute;
    }
    throw error;
  }
  if (isLinkLike(stat)) {
    throw new Error(`Destination root must not be a symlink or junction: ${absolute}`);
  }
  if (!stat.isDirectory()) {
    throw new Error(`Destination must be an ordinary directory: ${absolute}`);
  }
  return absolute;
}

export function validateManagedPath(destination: string, path: string): string {
  const pathError = nativeTreePathError(path);
  if (pathError) {
    throw new Error(pathError);
  }
  const root = validateDestinationRoot(destination);
  let current = root;
  const parts = path.split("/");
  for (const [index, part] of parts.entries()) {
    const next = join(current, part);
    requireContained(root, next);
    try {
      const stat = lstatSync(next);
      if (isLinkLike(stat)) {
        throw new Error(`${path}: managed path must not be a symlink or junction`);
      }
      if (index < parts.length - 1 && !stat.isDirectory()) {
        throw new Error(`${path}: ${next} is not a directory`);
      }
      current = next;
    } catch (error) {
      if (isENOENT(error)) {
        current = next;
        continue;
      }
      throw error;
    }
  }
  return current;
}

export function observePath(destination: string, path: string): ObservedPath {
  const pathError = nativeTreePathError(path);
  if (pathError) {
    throw new Error(pathError);
  }
  const root = validateDestinationRoot(destination);
  let current = root;
  const parts = path.split("/");
  for (const [index, part] of parts.entries()) {
    current = join(current, part);
    let stat: Stats;
    try {
      stat = lstatSync(current);
    } catch (error) {
      if (isENOENT(error)) {
        return { kind: "absent" };
      }
      throw error;
    }
    if (isLinkLike(stat)) {
      return { kind: "link" };
    }
    if (index < parts.length - 1) {
      if (!stat.isDirectory()) {
        return {
          kind: "blocked",
          path: parts.slice(0, index + 1).join("/"),
          actual: stat.isFile() ? "file" : "special",
        };
      }
      continue;
    }
    if (stat.isFile()) {
      return {
        kind: "file",
        identity: { sha256: hashBytes(readFileSync(current)), mode: observedMode(stat) },
      };
    }
    if (stat.isDirectory()) {
      return { kind: "directory" };
    }
    return { kind: "link" };
  }
  return { kind: "absent" };
}
