import { cpSync, mkdirSync, statSync } from "node:fs";
import { basename, join } from "node:path";
import { loadManifest } from "./lib/manifest.ts";

const [plugin, name] = process.argv.slice(2);
if (!plugin || !name) {
  console.error("Usage: npm run eject <plugin> <name>");
  process.exit(1);
}
const root = process.cwd();
const manifest = loadManifest(join(root, "curation", `${plugin}.yaml`));
const item = manifest.items.find((i) => (i.name ?? basename(i.source, ".md")) === name);
if (!item) {
  console.error(`No item with output name '${name}' in curation/${plugin}.yaml`);
  process.exit(1);
}
const src = join(root, "external", item.source);
const dest = join(root, "overlays", plugin, name);
const isDir = statSync(src).isDirectory();
if (isDir) {
  cpSync(src, dest, { recursive: true });
} else {
  mkdirSync(dest, { recursive: true });
  cpSync(src, join(dest, basename(src)));
}
// The build looks the overlay up by the SOURCE file name, not the output name, so the
// hint names the exact file to edit — renaming it there makes the build miss the overlay.
const target = isDir ? `overlays/${plugin}/${name}/` : `overlays/${plugin}/${name}/${basename(src)}`;
console.log(`Ejected ${item.source} -> ${target}`);
console.log(`Next: 1) add 'body: overlay' to that item in curation/${plugin}.yaml`);
console.log(`      2) edit ${target}  3) npm run build`);
