import { execFileSync } from "node:child_process";
import {
  cpSync,
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve } from "node:path";
import type { CurationItem } from "./manifest.ts";

const INHERITED_REPOSITORY = [
  "GIT_DIR",
  "GIT_WORK_TREE",
  "GIT_INDEX_FILE",
  "GIT_OBJECT_DIRECTORY",
  "GIT_COMMON_DIR",
  "GIT_ALTERNATE_OBJECT_DIRECTORIES",
];

/**
 * The environment of every git call in the overlay plumbing: no system config, and no repository a
 * caller (a hook, an IDE) left in the environment — those would make an init or add in a throwaway
 * directory act on a real repository. `extra` may set alternates back on purpose.
 */
export function gitEnv(extra: NodeJS.ProcessEnv = {}): NodeJS.ProcessEnv {
  const env: NodeJS.ProcessEnv = { ...process.env, GIT_CONFIG_NOSYSTEM: "1" };
  for (const k of INHERITED_REPOSITORY) {
    delete env[k];
  }
  return { ...env, ...extra };
}

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
  /**
   * True when the entry stamps a `frontmatter:` override on an item that owns no body: `files` then
   * holds the one upstream document the override rewrites, and no overlay directory exists.
   */
  frontmatter?: boolean;
}
export type OverlayLock = Record<string, LockEntry>;

/**
 * An item whose `frontmatter:` override needs its own stamp: it owns no body, so no overlay or patch
 * stamp notices upstream rewriting the document the override describes (curation/SCHEMA.md).
 */
export function stampsFrontmatter(item: CurationItem): boolean {
  return !item.exclude && !item.body && Object.keys(item.frontmatter ?? {}).length > 0;
}

export function lockKey(plugin: string, name: string): string {
  return `${plugin}/${name}`;
}

export type EjectStep = "overlay" | "patch" | "bless" | "rebase";
const EJECT_FLAGS: Record<EjectStep, string> = {
  overlay: "",
  patch: " --patch",
  bless: " --bless",
  rebase: " --patch --rebase",
};
/** The one spelling of every eject step a message names (workflow.md "Manifest and body-edit flow"). */
export function ejectCommand(plugin: string, name: string, step: EjectStep): string {
  return `npm run eject -- ${plugin} ${name}${EJECT_FLAGS[step]}`;
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
 * `core.longpaths` is set here because gitEnv() drops the system config that sets it on Windows,
 * and a stamped file deep in a checkout can pass 260 characters.
 */
export function blobSha(file: string): string {
  return execFileSync("git", ["-c", "core.longpaths=true", "hash-object", "--no-filters", "--", file], {
    encoding: "utf8",
    env: gitEnv(),
  }).trim();
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
      env: gitEnv(),
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
function gitApply(dir: string, patch: string, extra: string[], env: NodeJS.ProcessEnv = {}): string | null {
  const top = toplevel(dir);
  // git reports its top as a real path; a caller's path through a symlink, junction, subst drive,
  // or 8.3 short name would otherwise make --directory climb out of the repository.
  const rel = top ? relative(realpathSync.native(top), realpathSync.native(dir)).replaceAll("\\", "/") : "";
  const args = [
    // The host's git config must not decide what a build produces.
    "-c",
    "core.autocrlf=false",
    "-c",
    "core.eol=lf",
    "-c",
    "core.longpaths=true",
    // A host apply.whitespace=fix would rewrite the content a patch lands.
    "-c",
    "apply.whitespace=nowarn",
    "apply",
    "-p1",
    ...extra,
    ...(rel ? [`--directory=${rel}`] : []),
    "--",
    resolve(patch),
  ];
  try {
    execFileSync("git", args, {
      cwd: top ?? dir,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
      env: gitEnv(env),
    });
    return null;
  } catch (e) {
    const err = e as { stderr?: string };
    return (err.stderr ?? String(e)).trim();
  }
}

/** Verifies the patch still applies to pristine upstream without writing anything. */
export function checkPatch(upstreamDir: string, patch: string): string | null {
  return gitApply(upstreamDir, patch, ["--check"]);
}

export function applyPatch(dir: string, patch: string): string | null {
  return gitApply(dir, patch, []);
}

/**
 * Copy filter for every upstream copy: no symlinks (ADR-0001: a copied link absolutizes its target)
 * and no root `.git`, because a root item's gitfile would point git commands in the copy at the
 * real submodule (assemble.ts omits it for the same reason).
 */
export function copyUpstream(src: string): (s: string) => boolean {
  const gitfile = join(src, ".git");
  return (s) => s !== gitfile && !lstatSync(s).isSymbolicLink();
}

/** Splits a patch into one chunk per file at its column-0 `diff --git ` headers. */
export function splitPatch(text: string): string[] {
  const starts = [...text.matchAll(/^diff --git /gm)].map((m) => m.index);
  return starts.map((s, i) => text.slice(s, starts[i + 1] ?? text.length));
}

/** The item-relative path a chunk writes: its `+++ b/` side, else `--- a/`, else the header. */
export function chunkTarget(chunk: string): string {
  return (
    /^\+\+\+ b\/(.+?)\s*$/m.exec(chunk)?.[1] ??
    /^--- a\/(.+?)\s*$/m.exec(chunk)?.[1] ??
    /^diff --git a\/(\S+)/.exec(chunk)?.[1] ??
    ""
  );
}

// diff3 markers only. `=======` is left out because it is also a Markdown setext underline.
const ADDED_MARKER = /^\+(?:<{7}|\|{7}|>{7})(?: |$)/m;

/** Replay residue a cut must never ship: added conflict-marker lines and added `.rej` files. */
export function cutResidue(patch: string): string[] {
  const out: string[] = [];
  for (const chunk of splitPatch(patch)) {
    const target = chunkTarget(chunk);
    if (target.endsWith(".rej")) {
      out.push(`${target}: reject file`);
    } else if (ADDED_MARKER.test(chunk)) {
      out.push(`${target}: conflict marker`);
    }
  }
  return out;
}

/**
 * The patch that turns `upstreamDir` into `workDir`: two snapshots side by side, because a commit
 * plus copy could not see an added (untracked) or deleted (a copy never removes) file, and
 * --no-index has no index to hide them in. The a/ and b/ names are exactly what -p1 strips.
 * Returns "" when the trees are equal.
 */
export function cutPatch(upstreamDir: string, workDir: string): string {
  const tmp = mkdtempSync(join(tmpdir(), "eject-"));
  try {
    cpSync(upstreamDir, join(tmp, "a"), { recursive: true, filter: copyUpstream(upstreamDir) });
    // The old patch sits beside the working copy during a rebase; it is never part of the edits.
    const oldPatch = join(workDir, PATCH_FILE);
    cpSync(workDir, join(tmp, "b"), {
      recursive: true,
      filter: (s) => s !== oldPatch && !lstatSync(s).isSymbolicLink(),
    });
    try {
      // Pin the format: diff.noprefix or diff.mnemonicPrefix would break the -p1 contract, and
      // diff.context would silently narrow the anchor a patch is matched on.
      execFileSync(
        "git",
        // --no-prefix plus directories literally named a and b yields the `a/x` `b/x` paths -p1
        // strips; git's own prefix would stack on top of them and land the hunk one level off.
        [
          "-c",
          "core.autocrlf=false",
          "-c",
          "core.eol=lf",
          "diff",
          "--no-ext-diff",
          "--no-textconv",
          "--no-prefix",
          "-U8",
          "--no-index",
          "--",
          "a",
          "b",
        ],
        { cwd: tmp, encoding: "utf8", env: gitEnv() },
      );
      return "";
    } catch (e) {
      // --no-index exits 1 when the trees differ, which is the normal outcome.
      return (e as { stdout?: string }).stdout ?? "";
    }
  } finally {
    rmSync(tmp, { recursive: true, force: true });
  }
}

export type ReplayStatus = "applied" | "merged" | "conflict" | "rejected" | "gone";
export interface ReplayResult {
  file: string;
  status: ReplayStatus;
  regions?: number;
  reject?: string;
  detail?: string;
}

function seedGit(work: string, args: string[]): string {
  return execFileSync("git", ["-c", "core.autocrlf=false", "-c", "core.eol=lf", "-c", "core.longpaths=true", ...args], {
    cwd: work,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
    env: gitEnv(),
  });
}

/** Absolute object directory of the repository holding `dir`; a relative one silently disables --3way. */
function objectsOf(dir: string): string | null {
  try {
    return execFileSync("git", ["-C", dir, "rev-parse", "--path-format=absolute", "--git-path", "objects"], {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"],
      env: gitEnv(),
    }).trim();
  } catch {
    return null;
  }
}

const firstLine = (s: string | null): string => (s ?? "").split("\n").find((l) => l.trim()) ?? "";
const hunks = (s: string): number => (s.match(/^@@ /gm) ?? []).length;

/**
 * Replays an old patch onto `work`, a copy of the current upstream item, one file at a time
 * (workflow.md "Manifest and body-edit flow"). The old pin's blobs live in the submodule, so the
 * throwaway repository borrows its objects; status is read from the index and the files, never from
 * git's wording.
 */
export function replayPatch(work: string, upstreamDir: string, patchText: string): ReplayResult[] {
  seedGit(work, ["init", "-q"]);
  mkdirSync(join(work, ".git", "info"), { recursive: true });
  writeFileSync(join(work, ".git", "info", "attributes"), "* -text\n");
  seedGit(work, ["config", "merge.conflictStyle", "diff3"]);
  // --force: an item's own .gitignore, info/exclude, or the host's core.excludesFile must not keep
  // a file out of the index, or its --3way merge fails as "does not exist in index".
  seedGit(work, ["add", "-A", "--force"]);
  const objects = objectsOf(upstreamDir);
  const alternates = objects ? { GIT_ALTERNATE_OBJECT_DIRECTORIES: objects } : {};
  const scratch = mkdtempSync(join(tmpdir(), "replay-"));
  try {
    return splitPatch(patchText).map((chunk, i): ReplayResult => {
      const file = chunkTarget(chunk);
      const p = join(scratch, `${i}.patch`);
      writeFileSync(p, chunk);
      if (gitApply(work, p, ["--check"]) === null && gitApply(work, p, []) === null) {
        return { file, status: "applied" };
      }
      const three = gitApply(work, p, ["--3way"], alternates);
      if (seedGit(work, ["ls-files", "-u", "--", file]).trim()) {
        const regions = (readFileSync(join(work, file), "utf8").match(/^<<<<<<< /gm) ?? []).length;
        return { file, status: "conflict", regions };
      }
      if (three === null) {
        // Reached only after --check failed, so this is a real merge.
        return { file, status: "merged" };
      }
      const reject = `${file}.rej`;
      const rejected = gitApply(work, p, ["--reject"]);
      if (existsSync(join(work, reject))) {
        const k = hunks(readFileSync(join(work, reject), "utf8"));
        return { file, status: "rejected", reject, detail: `${k} of ${hunks(chunk)} hunks: ${firstLine(three)}` };
      }
      mkdirSync(dirname(join(work, reject)), { recursive: true });
      writeFileSync(join(work, reject), chunk);
      return existsSync(join(work, file))
        ? { file, status: "rejected", reject, detail: `whole file: ${firstLine(rejected)}` }
        : { file, status: "gone", reject };
    });
  } finally {
    rmSync(scratch, { recursive: true, force: true });
    rmSync(join(work, ".git"), { recursive: true, force: true });
  }
}

/** What a stamp records for a path: the blob SHA of a regular file, null for anything else. */
export function fileStamp(p: string): string | null {
  return existsSync(p) && lstatSync(p).isFile() ? blobSha(p) : null;
}

/**
 * The upstream content an overlay was written against. Files the overlay adds have no upstream
 * counterpart and cannot be stamped; everything else is recorded by content hash.
 */
export function stampFiles(upstreamDir: string, files: string[]): Record<string, string> {
  const stamped: Record<string, string> = {};
  for (const f of [...files].sort()) {
    const sha = fileStamp(join(upstreamDir, f));
    if (sha !== null) {
      stamped[f] = sha;
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
    .filter(([f, sha]) => fileStamp(join(upstreamDir, f)) !== sha)
    .map(([f]) => f);
}

/** Same-filename rule for a merge source: stamp what exists, record what does not. */
export function stampMergeFiles(dir: string, files: string[]): Record<string, string | null> {
  const out: Record<string, string | null> = {};
  for (const f of [...files].sort()) {
    out[f] = fileStamp(join(dir, f));
  }
  return out;
}

/** Merge-source files that no longer match their stamp — including ones that appeared over a null. */
export function driftedMergeSources(root: string, entry: LockEntry): string[] {
  const msgs: string[] = [];
  for (const [addr, files] of Object.entries(entry.mergeSources ?? {})) {
    const dir = join(root, "external", addr);
    for (const [f, sha] of Object.entries(files)) {
      const now = fileStamp(join(dir, f));
      if (now !== sha) {
        msgs.push(sha === null ? `${addr}: ${f} (appeared upstream)` : `${addr}: ${f}`);
      }
    }
  }
  return msgs.sort();
}
