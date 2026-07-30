import { execFileSync } from "node:child_process";
import { cpSync, existsSync, mkdirSync, mkdtempSync, readdirSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, dirname, join } from "node:path";
import { loadManifest } from "./lib/manifest.ts";
import { loadLock, lockKey, PATCH_FILE, saveLock, stampFiles } from "./lib/overlay.ts";
import { requireSubmodules } from "./lib/preflight.ts";
import { scanSubmodule } from "./lib/scan.ts";

const argv = process.argv.slice(2);
const flags = new Set(argv.filter((a) => a.startsWith("--")));
const [pluginArg, nameArg] = argv.filter((a) => !a.startsWith("--"));
if (!pluginArg || !nameArg) {
  console.error("Usage: npm run eject <plugin> <name> [--patch] [--bless] [--force]");
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
const overlayFiles = () => (existsSync(dest) ? readdirSync(dest).filter((f) => f !== PATCH_FILE) : []);

function stamp(): void {
  const lock = loadLock(root);
  lock[lockKey(plugin, name)] = { source: item.source, files: stampFiles(base, overlayFiles()) };
  saveLock(root, lock);
}

if (flags.has("--bless")) {
  if (!existsSync(dest)) {
    console.error(`Nothing to bless: overlays/${plugin}/${name}/ does not exist`);
    process.exit(1);
  }
  if (existsSync(join(dest, PATCH_FILE))) {
    console.error(`overlays/${plugin}/${name}/ is a patch overlay — patches are self-checking and are never blessed.`);
    console.error("If it stopped applying, re-cut it: delete overlay.patch, then npm run eject --patch");
    process.exit(1);
  }
  stamp();
  console.log(`Blessed overlays/${plugin}/${name}/ against current upstream (${item.source}).`);
  process.exit(0);
}

if (flags.has("--patch")) {
  if ((item.as ?? comp?.type) !== "skill") {
    console.error(`${plugin}/${name}: --patch applies to skill output only — a conversion needs a full-file overlay.`);
    process.exit(1);
  }
  const working = overlayFiles();
  if (!working.length) {
    // Phase 1: lay down a working copy to edit. The patch is cut from it on the second run.
    if (existsSync(join(dest, PATCH_FILE)) && !flags.has("--force")) {
      console.error(
        `overlays/${plugin}/${name}/${PATCH_FILE} already exists — pass --force to re-cut it from scratch.`,
      );
      process.exit(1);
    }
    mkdirSync(dest, { recursive: true });
    cpSync(src, dest, { recursive: true });
    console.log(`Ejected ${item.source} -> overlays/${plugin}/${name}/ (working copy)`);
    console.log("Next: 1) edit the files there");
    console.log(`      2) npm run eject ${plugin} ${name} --patch   (turns your edits into ${PATCH_FILE})`);
    process.exit(0);
  }
  // Phase 2: diff the edited working copy against upstream through a throwaway repo, so the patch
  // carries plain item-relative paths that `git apply -p1` lands on the emitted item directory.
  const tmp = mkdtempSync(join(tmpdir(), "eject-"));
  const git = (...args: string[]) => execFileSync("git", ["-c", "core.fileMode=false", ...args], { cwd: tmp });
  git("init", "-q", ".");
  cpSync(src, tmp, { recursive: true });
  git("add", "-A");
  git("-c", "user.email=eject@local", "-c", "user.name=eject", "commit", "-qm", "upstream");
  cpSync(dest, tmp, { recursive: true, force: true });
  const patch = execFileSync("git", ["-c", "core.fileMode=false", "diff"], { cwd: tmp, encoding: "utf8" });
  rmSync(tmp, { recursive: true, force: true });
  if (!patch.trim()) {
    console.error(`No edits found in overlays/${plugin}/${name}/ — nothing to turn into a patch.`);
    process.exit(1);
  }
  rmSync(dest, { recursive: true, force: true });
  mkdirSync(dest, { recursive: true });
  writeFileSync(join(dest, PATCH_FILE), patch);
  const lock = loadLock(root);
  delete lock[lockKey(plugin, name)];
  saveLock(root, lock);
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
  cpSync(src, dest, { recursive: true });
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
  `Note: a full-file overlay is hash-blessed. After an upstream change: npm run eject ${plugin} ${name} --bless`,
);
