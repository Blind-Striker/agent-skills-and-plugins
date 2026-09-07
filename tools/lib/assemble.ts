import {
  cpSync,
  existsSync,
  lstatSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { basename, join, relative } from "node:path";
import { parseDoc, serializeDoc } from "./frontmatter.ts";
import type { ComponentType, CurationItem, CurationManifest } from "./manifest.ts";
import { applyPatch, PATCH_FILE } from "./overlay.ts";
import { isOmitted, itemRelative, resolveItem } from "./resolve.ts";
import type { ComponentInfo } from "./scan.ts";

/**
 * One item after the shared curation pipeline and before any harness emitter adapts it.
 *
 * `dir` is internal build state, not a distributable tree. Every item is directory-shaped so a
 * command or agent resolved from a skill can retain its dependency closure for targets that emit it
 * as a skill, while Claude Code and OpenCode remain free to emit only their native single-file
 * command/agent shape.
 */
export interface AssembledItem {
  plugin: string;
  source: string;
  sourceType: ComponentType;
  outName: string;
  outType: ComponentType;
  dir: string;
  item?: CurationItem;
  own: boolean;
}

/** The single source-named file an overlay uses when a target shape is command or agent. */
export function overlayBodyFile(comp: ComponentInfo, item: CurationItem): string {
  return comp.type === "skill" ? "SKILL.md" : basename(item.source);
}

/**
 * Upstream files an item leaves behind. Omission precedes overlay/patch materialization so owned
 * changes apply to the closure that actually survives compilation.
 */
function omitFilter(srcRoot: string, item: CurationItem): (src: string) => boolean {
  return (src) => {
    const rel = itemRelative(srcRoot, src);
    if (rel === "") {
      return true;
    }
    // A root skill can expose the submodule gitdir file to the copy. It is repository metadata and
    // contains a machine path, never runtime skill content.
    if (rel === ".git" || rel.startsWith(".git/")) {
      return false;
    }
    return !item.omit?.length || !isOmitted(rel, item.omit);
  };
}

/** cpSync still creates a directory whose every child was filtered out; remove those husks. */
function pruneEmptyDirs(dir: string): void {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (!entry.isDirectory()) {
      continue;
    }
    const path = join(dir, entry.name);
    pruneEmptyDirs(path);
    if (!readdirSync(path).length) {
      rmSync(path, { recursive: true, force: true });
    }
  }
}

// cpSync copies a symlink as an absolute local link. Shared assembly must contain plain portable
// files, so reject links once here and let every emitter consume the same filtered closure.
function skipSymlinks(root: string, label: string, report: string[]): (src: string) => boolean {
  return (src) => {
    if (!lstatSync(src).isSymbolicLink()) {
      return true;
    }
    const rel = relative(root, src).replaceAll("\\", "/");
    report.push(`WARN ${label}: skipped symlink ${rel.startsWith("..") ? basename(src) : rel}`);
    return false;
  };
}

function assemblyDir(stagingRoot: string, plugin: string, outType: ComponentType, outName: string): string {
  return join(stagingRoot, plugin, `${outType}s`, outName);
}

function copySourceClosure(
  root: string,
  stagingDir: string,
  item: CurationItem,
  comp: ComponentInfo,
  plugin: string,
  outName: string,
  report: string[],
): void {
  const sourcePath = join(root, "external", item.source);
  const filter = skipSymlinks(root, `${plugin}/${outName}`, report);
  mkdirSync(stagingDir, { recursive: true });

  if (comp.type === "skill") {
    const keep = omitFilter(sourcePath, item);
    cpSync(sourcePath, stagingDir, { recursive: true, filter: (src) => filter(src) && keep(src) });
    if (item.omit?.length) {
      pruneEmptyDirs(stagingDir);
    }
    return;
  }

  cpSync(sourcePath, join(stagingDir, "SKILL.md"), { filter });
}

function applyBody(
  root: string,
  stagingDir: string,
  item: CurationItem,
  comp: ComponentInfo,
  outType: ComponentType,
  overlayDir: string,
  plugin: string,
  outName: string,
  report: string[],
): void {
  if (item.body === "patch") {
    const error = applyPatch(stagingDir, join(overlayDir, PATCH_FILE));
    if (error) {
      throw new Error(`${plugin}/${outName}: ${PATCH_FILE} failed to apply:\n${error}`);
    }
    return;
  }
  if (item.body !== "overlay") {
    return;
  }

  const filter = skipSymlinks(root, `${plugin}/${outName}`, report);
  if (outType === "skill") {
    cpSync(overlayDir, stagingDir, { recursive: true, force: true, filter });
    return;
  }

  // A conversion overlay owns the primary procedure only. The dependency closure still comes from
  // the selected upstream skill and is retained in assembly for a target that converts it back to a
  // skill, but unrelated overlay files do not silently gain ownership here.
  cpSync(join(overlayDir, overlayBodyFile(comp, item)), join(stagingDir, "SKILL.md"), { force: true, filter });
}

function normalizePrimary(stagingDir: string, item: CurationItem, outType: ComponentType, outName: string): void {
  const path = join(stagingDir, "SKILL.md");
  let doc = parseDoc(readFileSync(path, "utf8"));
  if (outType === "skill") {
    // Invocation remains neutral assembly metadata. Harness-specific flags are applied only by an
    // emitter; the common document owns curation overrides and stable output identity.
    doc.frontmatter = { ...doc.frontmatter, ...item.frontmatter, name: outName };
  } else {
    const base: Record<string, unknown> =
      outType === "command"
        ? { description: String(doc.frontmatter.description ?? "") }
        : { name: outName, description: String(doc.frontmatter.description ?? "") };
    const forcedName: Record<string, unknown> = outType === "agent" ? { name: outName } : {};
    doc = { frontmatter: { ...base, ...item.frontmatter, ...forcedName }, body: doc.body };
  }
  writeFileSync(path, serializeDoc(doc));
}

function assembleCuratedItem(
  root: string,
  stagingRoot: string,
  manifest: CurationManifest,
  item: CurationItem,
  components: ComponentInfo[],
  report: string[],
): AssembledItem | undefined {
  if (item.exclude) {
    return undefined;
  }
  const { comp, outName, outType, overlayDir } = resolveItem(root, manifest.plugin.name, item, components);
  if (!comp) {
    throw new Error(`${manifest.plugin.name}: source not found in external/: ${item.source}`);
  }
  if (item.body && !existsSync(overlayDir)) {
    throw new Error(
      `${manifest.plugin.name}/${outName}: body is ${item.body} but overlays/${manifest.plugin.name}/${outName}/ is missing — run: npm run eject -- ${manifest.plugin.name} ${outName}`,
    );
  }
  if (outType === "skill" && comp.type !== "skill") {
    throw new Error(`${item.source}: ${comp.type} -> skill conversion not supported`);
  }

  const dir = assemblyDir(stagingRoot, manifest.plugin.name, outType, outName);
  copySourceClosure(root, dir, item, comp, manifest.plugin.name, outName, report);
  applyBody(root, dir, item, comp, outType, overlayDir, manifest.plugin.name, outName, report);
  normalizePrimary(dir, item, outType, outName);

  if (comp.type === "skill" && outType !== "skill") {
    const sourcePath = join(root, "external", item.source);
    const extras = readdirSync(sourcePath).filter((file) => file !== "SKILL.md");
    if (extras.length) {
      report.push(
        `WARN ${manifest.plugin.name}/${outName}: dropped in skill->${outType} conversion: ${extras.join(", ")}`,
      );
    }
  }
  report.push(
    `${manifest.plugin.name}: ${outType} ${outName} <- ${item.source}${item.body === "overlay" && outType === "skill" ? " (overlay)" : ""}`,
  );

  return {
    plugin: manifest.plugin.name,
    source: item.source,
    sourceType: comp.type,
    outName,
    outType,
    dir,
    item,
    own: false,
  };
}

function assembleOwnSkills(
  root: string,
  stagingRoot: string,
  manifest: CurationManifest,
  report: string[],
): AssembledItem[] {
  const ownDir = join(root, "skills", manifest.plugin.name);
  if (!existsSync(ownDir)) {
    return [];
  }
  const assembled: AssembledItem[] = [];
  for (const name of readdirSync(ownDir)) {
    if (!statSync(join(ownDir, name)).isDirectory()) {
      continue;
    }
    const dir = assemblyDir(stagingRoot, manifest.plugin.name, "skill", name);
    cpSync(join(ownDir, name), dir, {
      recursive: true,
      filter: skipSymlinks(root, `${manifest.plugin.name}/${name}`, report),
    });
    report.push(`${manifest.plugin.name}: skill ${name} <- skills/ (own)`);
    assembled.push({
      plugin: manifest.plugin.name,
      source: `skills/${manifest.plugin.name}/${name}`,
      sourceType: "skill",
      outName: name,
      outType: "skill",
      dir,
      own: true,
    });
  }
  return assembled;
}

/** Resolve and assemble the complete authored estate once for every target emitter. */
export function assembleItems(
  root: string,
  stagingRoot: string,
  manifests: CurationManifest[],
  components: ComponentInfo[],
  report: string[],
): AssembledItem[] {
  const assembled: AssembledItem[] = [];
  for (const manifest of manifests) {
    for (const item of manifest.items) {
      const result = assembleCuratedItem(root, stagingRoot, manifest, item, components, report);
      if (result) {
        assembled.push(result);
      }
    }
    assembled.push(...assembleOwnSkills(root, stagingRoot, manifest, report));
  }
  return assembled;
}
