import { execFileSync } from "node:child_process";
import {
  cpSync,
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { basename, dirname, join } from "node:path";
import { loadManifest } from "./lib/manifest.ts";
import {
  checkPatch,
  listFiles,
  loadLock,
  lockKey,
  PATCH_FILE,
  patchTargets,
  saveLock,
  stampFiles,
} from "./lib/overlay.ts";
import { requireSubmodules } from "./lib/preflight.ts";
import { scanSubmodule } from "./lib/scan.ts";

const argv = process.argv.slice(2);
const flags = new Set(argv.filter((a) => a.startsWith("--")));
const [pluginArg, nameArg] = argv.filter((a) => !a.startsWith("--"));
if (!pluginArg || !nameArg) {
  console.error("Usage: npm run eject -- <plugin> <name> [--patch] [--bless] [--force]");
  process.exit(1);
}
// Re-bound after the guard: a closure below cannot see flow narrowing on the destructured bindings.
const plugin: string = pluginArg;
const name: string = nameArg;
const root = process.cwd();
requireSubmodules(root);
const manifest = loadManifest(join(root, "curation", `${plugin}.yaml`));
// Output names must resolve exactly as build.ts resolves them (item.name ?? the scanner's
// frontmatter name), or the `npm run eject` command that build's missing-overlay error
// prints would not find its own item. The basename is the last resort for sources the
// scanner cannot see (a stale manifest entry, a source outside external/).
const components = readdirSync(join(root, "external"))
  .filter((s) => statSync(join(root, "external", s)).isDirectory())
  .flatMap((s) => scanSubmodule(join(root, "external"), s));
const found = manifest.items.find(
  (i) => (i.name ?? components.find((c) => c.sourcePath === i.source)?.name ?? basename(i.source, ".md")) === name,
);
if (!found) {
  console.error(`No item with output name '${name}' in curation/${plugin}.yaml`);
  process.exit(1);
}
const item = found;
const comp = components.find((c) => c.sourcePath === item.source);
const src = join(root, "external", item.source);
const dest = join(root, "overlays", plugin, name);
const isDir = statSync(src).isDirectory();
// Names inside an overlay resolve against the source directory for a skill, and against the
// containing directory for a bare command/agent file — see build.ts upstreamBase.
const base = isDir ? src : dirname(src);
const patchFile = join(dest, PATCH_FILE);
// What the overlay was written against: the files a full-file overlay replaces, or the paths a
// patch touches. Both are recorded, so upstream can never move under either kind unnoticed.
const stampedNames = (): string[] =>
  existsSync(patchFile) ? patchTargets(readFileSync(patchFile, "utf8")) : listFiles(dest);

// cpSync copies a symlink as a symlink but absolutizes its target, so an ejected link would carry
// a machine-specific path into a committed directory — the same leak build.ts guards against.
const noSymlinks = { filter: (s: string) => !lstatSync(s).isSymbolicLink() };

function stamp(): void {
  const lock = loadLock(root);
  lock[lockKey(plugin, name)] = { source: item.source, files: stampFiles(base, stampedNames()) };
  saveLock(root, lock);
}

if (flags.has("--bless")) {
  if (!existsSync(dest)) {
    console.error(`Nothing to bless: overlays/${plugin}/${name}/ does not exist`);
    process.exit(1);
  }
  stamp();
  console.log(`Blessed overlays/${plugin}/${name}/ against current upstream (${item.source}).`);
  process.exit(0);
}

// npm eats `--`-prefixed arguments unless a `--` separator precedes them, so `npm run eject x y
// --patch` silently arrives here as a plain eject and would create the wrong kind of overlay.
if (item.body && !flags.has("--patch") && !existsSync(dest)) {
  const want = item.body === "patch" ? " --patch" : "";
  console.error(`curation/${plugin}.yaml says 'body: ${item.body}' for ${name}, but no flags reached this command.`);
  console.error(`npm strips them unless you separate them: npm run eject -- ${plugin} ${name}${want}`);
  process.exit(1);
}

if (flags.has("--patch")) {
  if ((item.as ?? comp?.type) !== "skill") {
    console.error(`${plugin}/${name}: --patch applies to skill output only — a conversion needs a full-file overlay.`);
    process.exit(1);
  }
  const working = listFiles(dest);
  if (!working.length) {
    // Phase 1: lay down a working copy to edit. The patch is cut from it on the second run.
    if (existsSync(join(dest, PATCH_FILE)) && !flags.has("--force")) {
      console.error(
        `overlays/${plugin}/${name}/${PATCH_FILE} already exists — pass --force to re-cut it from scratch.`,
      );
      process.exit(1);
    }
    mkdirSync(dest, { recursive: true });
    cpSync(src, dest, { recursive: true, ...noSymlinks });
    console.log(`Ejected ${item.source} -> overlays/${plugin}/${name}/ (working copy)`);
    console.log("Next: 1) edit the files there");
    console.log(`      2) npm run eject -- ${plugin} ${name} --patch   (turns your edits into ${PATCH_FILE})`);
    process.exit(0);
  }
  // Phase 2: diff two snapshots side by side. Committing upstream and copying the working copy
  // over it could not see an added file (untracked) or a deleted one (a copy never removes), and
  // --no-index has no index to hide them in. The a/ and b/ names are exactly what -p1 strips.
  const tmp = mkdtempSync(join(tmpdir(), "eject-"));
  cpSync(src, join(tmp, "a"), { recursive: true, ...noSymlinks });
  cpSync(dest, join(tmp, "b"), { recursive: true, ...noSymlinks });
  let patch = "";
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
      { cwd: tmp, encoding: "utf8", env: { ...process.env, GIT_CONFIG_NOSYSTEM: "1" } },
    );
  } catch (e) {
    // --no-index exits 1 when the trees differ, which is the normal outcome.
    patch = (e as { stdout?: string }).stdout ?? "";
  }
  rmSync(tmp, { recursive: true, force: true });
  if (!patch.trim()) {
    console.error(`No edits found in overlays/${plugin}/${name}/ — nothing to turn into a patch.`);
    process.exit(1);
  }
  mkdirSync(dest, { recursive: true });
  writeFileSync(patchFile, patch);
  // Verify before destroying the working copy: a patch that does not apply to the very upstream it
  // was cut from is a tooling bug, and losing the user's edits to it is unrecoverable.
  const bad = checkPatch(base, patchFile);
  if (bad) {
    rmSync(patchFile, { force: true });
    console.error(`Cut a patch that does not apply back to ${item.source}. Your working copy is untouched.`);
    console.error(bad);
    process.exit(1);
  }
  for (const f of listFiles(dest)) {
    rmSync(join(dest, f), { force: true });
  }
  for (const d of readdirSync(dest).filter((d) => d !== PATCH_FILE)) {
    rmSync(join(dest, d), { recursive: true, force: true });
  }
  stamp();
  console.log(`Cut ${patch.split("\n").length} patch lines -> overlays/${plugin}/${name}/${PATCH_FILE}`);
  console.log(`Next: 1) set 'body: patch' on that item in curation/${plugin}.yaml`);
  console.log("      2) npm run build");
  process.exit(0);
}

if (existsSync(dest) && readdirSync(dest).length && !flags.has("--force")) {
  console.error(`overlays/${plugin}/${name}/ already exists — pass --force to overwrite your edits.`);
  process.exit(1);
}
if (isDir) {
  cpSync(src, dest, { recursive: true, ...noSymlinks });
} else {
  mkdirSync(dest, { recursive: true });
  cpSync(src, join(dest, basename(src)));
}
stamp();
// The build looks the overlay up by the SOURCE file name, not the output name, so the
// hint names the exact file to edit — renaming it there makes the build miss the overlay.
const target = isDir ? `overlays/${plugin}/${name}/` : `overlays/${plugin}/${name}/${basename(src)}`;
console.log(`Ejected ${item.source} -> ${target}`);
console.log(`Next: 1) add 'body: overlay' to that item in curation/${plugin}.yaml`);
console.log(`      2) edit ${target}  3) npm run build`);
console.log(
  `Note: a full-file overlay is hash-blessed. After an upstream change: npm run eject -- ${plugin} ${name} --bless`,
);
