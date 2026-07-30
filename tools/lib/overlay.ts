import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join, relative, resolve } from "node:path";

/** The one file a `body: patch` overlay directory is allowed to contain. */
export const PATCH_FILE = "overlay.patch";

/** Records what a full-file overlay was written against. Patches are self-checking (ADR-0001). */
export const LOCK_FILE = "overlays.lock.json";

export interface LockEntry {
  source: string;
  /** upstream file name -> git blob SHA of its content when the overlay was written */
  files: Record<string, string>;
}
export type OverlayLock = Record<string, LockEntry>;

export function lockKey(plugin: string, name: string): string {
  return `${plugin}/${name}`;
}

function lockPath(root: string): string {
  return join(root, "overlays", LOCK_FILE);
}

export function loadLock(root: string): OverlayLock {
  const p = lockPath(root);
  return existsSync(p) ? (JSON.parse(readFileSync(p, "utf8")) as OverlayLock) : {};
}

export function saveLock(root: string, lock: OverlayLock): void {
  const sorted = Object.fromEntries(Object.entries(lock).sort(([a], [b]) => a.localeCompare(b)));
  writeFileSync(lockPath(root), `${JSON.stringify(sorted, null, 2)}\n`);
}

/**
 * Content hash of a file, as git would compute it. `hash-object` is pure hashing — it needs no
 * repository, so this works the same in the real tree and in a throwaway test fixture.
 */
export function blobSha(file: string): string {
  return execFileSync("git", ["hash-object", "--", file], { encoding: "utf8" }).trim();
}

function toplevel(dir: string): string | null {
  try {
    return execFileSync("git", ["-C", dir, "rev-parse", "--show-toplevel"], {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"],
    }).trim();
  } catch {
    return null;
  }
}

/**
 * Patches are generated against, and applied to, the item directory root, so paths inside them are
 * item-relative and `-p1` strips git's own `a/`+`b/` prefixes.
 *
 * Inside a work tree git resolves those paths against the REPOSITORY ROOT and silently ignores
 * anything outside the current subdirectory — it exits 0 having changed nothing. Running from the
 * top with `--directory` is what actually lands the patch on the item. Outside a repository (a
 * throwaway fixture, a consumer without git history) the plain cwd form is correct, so both are
 * kept and the exercised one depends on where the tree lives.
 */
function gitApply(dir: string, patch: string, check: boolean): string | null {
  const top = toplevel(dir);
  const rel = top ? relative(resolve(top), resolve(dir)).replaceAll("\\", "/") : "";
  const args = [
    "apply",
    "-p1",
    ...(check ? ["--check"] : []),
    ...(rel ? [`--directory=${rel}`] : []),
    "--",
    resolve(patch),
  ];
  try {
    execFileSync("git", args, { cwd: top ?? dir, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
    return null;
  } catch (e) {
    const err = e as { stderr?: string };
    return (err.stderr ?? String(e)).trim();
  }
}

/** Verifies the patch still applies to pristine upstream without writing anything. */
export function checkPatch(upstreamDir: string, patch: string): string | null {
  return gitApply(upstreamDir, patch, true);
}

export function applyPatch(dir: string, patch: string): string | null {
  return gitApply(dir, patch, false);
}

/**
 * Files a full-file overlay replaces, paired with the upstream content they were written against.
 * Overlay-only additions have no upstream counterpart and are not tracked.
 */
export function stampFiles(upstreamDir: string, overlayFiles: string[]): Record<string, string> {
  const files: Record<string, string> = {};
  for (const f of overlayFiles.sort()) {
    const up = join(upstreamDir, f);
    if (existsSync(up)) {
      files[f] = blobSha(up);
    }
  }
  return files;
}

/** Upstream files whose content no longer matches what the overlay was blessed against. */
export function driftedFiles(upstreamDir: string, entry: LockEntry): string[] {
  return Object.entries(entry.files)
    .filter(([f, sha]) => {
      const up = join(upstreamDir, f);
      return !existsSync(up) || blobSha(up) !== sha;
    })
    .map(([f]) => f);
}
