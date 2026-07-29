import { cpSync, mkdirSync, readdirSync, statSync } from "node:fs";
import { basename, join } from "node:path";
import { loadManifest } from "./lib/manifest.ts";
import { scanSubmodule } from "./lib/scan.ts";

const [plugin, name] = process.argv.slice(2);
if (!plugin || !name) {
  console.error("Usage: npm run eject <plugin> <name>");
  process.exit(1);
}
const root = process.cwd();
const manifest = loadManifest(join(root, "curation", `${plugin}.yaml`));
// Output names must resolve exactly as build.ts resolves them (item.name ?? the scanner's
// frontmatter name), or the `npm run eject` command that build's missing-overlay error
// prints would not find its own item. The basename is the last resort for sources the
// scanner cannot see (a stale manifest entry, a source outside external/).
const components = readdirSync(join(root, "external"))
  .filter((s) => statSync(join(root, "external", s)).isDirectory())
  .flatMap((s) => scanSubmodule(join(root, "external"), s));
const item = manifest.items.find(
  (i) => (i.name ?? components.find((c) => c.sourcePath === i.source)?.name ?? basename(i.source, ".md")) === name,
);
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
