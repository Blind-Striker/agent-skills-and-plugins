import { execFileSync } from "node:child_process";
import { existsSync, lstatSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join, relative, resolve } from "node:path";

/** The one file a `body: patch` overlay directory is allowed to contain. */
export const PATCH_FILE = "overlay.patch";

/** Records the upstream content every overlay — of either kind — was written against. */
export const LOCK_FILE = "overlays.lock.json";

export interface LockEntry {
  source: string;
  /** upstream file name -> git blob SHA of its content when the overlay was written */
  files: Record<string, string>;
  /** merge source address -> same-filename stamps; null records "absent when blessed". */
  mergeSources?: Record<string, Record<string, string | null>>;
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
 * Content hash of a file, as git would compute it. `hash-object` needs no repository, so this
 * works the same in the real tree and in a throwaway fixture. `--no-filters` keeps the value a
 * function of the bytes alone: without it the answer depends on the caller's `core.autocrlf` and
 * clean filters, and a lock is worthless if two machines disagree about what it recorded.
 */
export function blobSha(file: string): string {
  return execFileSync("git", ["hash-object", "--no-filters", "--", file], { encoding: "utf8" }).trim();
}

/**
 * Every file under `dir`, as POSIX paths relative to it. Recursive because most upstream skills
 * carry a `references/` or `scripts/` directory, and a flat listing fed directory names to a
 * hasher that can only hash files. Symlinks are skipped for the reason ADR-0001 gives: a copied
 * link absolutizes its target and cannot travel.
 */
export function listFiles(dir: string, prefix = ""): string[] {
  if (!existsSync(dir)) {
    return [];
  }
  const out: string[] = [];
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const rel = prefix ? `${prefix}/${e.name}` : e.name;
    if (lstatSync(join(dir, e.name)).isSymbolicLink()) {
      continue;
    }
    if (e.isDirectory()) {
      out.push(...listFiles(join(dir, e.name), rel));
    } else if (rel !== PATCH_FILE) {
      out.push(rel);
    }
  }
  return out.sort();
}

/**
 * The item-relative paths a patch reads or writes, from its `---`/`+++` headers. Both sides are
 * taken so a rename records each end; `/dev/null` marks a pure add or delete and has no upstream
 * counterpart to stamp.
 */
export function patchTargets(patchText: string): string[] {
  const out = new Set<string>();
  for (const line of patchText.split("\n")) {
    const m = /^(?:---|\+\+\+) (?!\/dev\/null)[ab]\/(.+?)\s*$/.exec(line);
    if (m?.[1]) {
      out.add(m[1]);
    }
  }
  return [...out].sort();
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
    // The host's git config must not decide what a build produces.
    "-c",
    "core.autocrlf=false",
    "-c",
    "core.eol=lf",
    "-c",
    "core.longpaths=true",
    "apply",
    "-p1",
    ...(check ? ["--check"] : []),
    ...(rel ? [`--directory=${rel}`] : []),
    "--",
    resolve(patch),
  ];
  try {
    execFileSync("git", args, {
      cwd: top ?? dir,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
      env: { ...process.env, GIT_CONFIG_NOSYSTEM: "1" },
    });
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
 * The upstream content an overlay was written against. Files the overlay adds have no upstream
 * counterpart and cannot be stamped; everything else is recorded by content hash.
 */
export function stampFiles(upstreamDir: string, files: string[]): Record<string, string> {
  const stamped: Record<string, string> = {};
  for (const f of [...files].sort()) {
    const up = join(upstreamDir, f);
    if (existsSync(up) && lstatSync(up).isFile()) {
      stamped[f] = blobSha(up);
    }
  }
  return stamped;
}

/** The primary lock keys eject would bless for the overlay currently on disk. */
export function liveStampKeys(upstreamDir: string, overlayDir: string): string[] {
  const patch = join(overlayDir, PATCH_FILE);
  const names = existsSync(patch) ? patchTargets(readFileSync(patch, "utf8")) : listFiles(overlayDir);
  return Object.keys(stampFiles(upstreamDir, names));
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

/** Same-filename rule for a merge source: stamp what exists, record what does not. */
export function stampMergeFiles(dir: string, files: string[]): Record<string, string | null> {
  const out: Record<string, string | null> = {};
  for (const f of [...files].sort()) {
    const p = join(dir, f);
    out[f] = existsSync(p) && lstatSync(p).isFile() ? blobSha(p) : null;
  }
  return out;
}

/** Merge-source files that no longer match their stamp — including ones that appeared over a null. */
export function driftedMergeSources(root: string, entry: LockEntry): string[] {
  const msgs: string[] = [];
  for (const [addr, files] of Object.entries(entry.mergeSources ?? {})) {
    const dir = join(root, "external", addr);
    for (const [f, sha] of Object.entries(files)) {
      const p = join(dir, f);
      if (sha === null) {
        if (existsSync(p)) {
          msgs.push(`${addr}: ${f} (appeared upstream)`);
        }
      } else if (!existsSync(p) || blobSha(p) !== sha) {
        msgs.push(`${addr}: ${f}`);
      }
    }
  }
  return msgs.sort();
}
