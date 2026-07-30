import { basename, dirname, join, relative } from "node:path";
import { matchesGlob } from "node:path";
import type { ComponentType, CurationItem } from "./manifest.ts";
import type { ComponentInfo } from "./scan.ts";

/**
 * What a manifest item becomes: its upstream component, its output name and type, and where its
 * overlay would live. Derived in exactly one place because it used to be derived in three —
 * `collectProblems`, `emitItem` and `validate` each re-computed it, so the fail-fast guarantee held
 * by convention and a per-item rule added to one side only was invisible on the others.
 */
export interface ResolvedItem {
  /** undefined when the source is not in external/ — every caller reports that first. */
  comp: ComponentInfo | undefined;
  outName: string;
  outType: ComponentType;
  /** Absolute path to overlays/<plugin>/<outName>, whether or not it exists. */
  overlayDir: string;
  /** `<plugin>/<outName>` — the identifier every overlay message and lock key is built from. */
  id: string;
}

export function resolveItem(
  root: string,
  plugin: string,
  item: CurationItem,
  components: ComponentInfo[],
): ResolvedItem {
  const comp = components.find((c) => c.sourcePath === item.source);
  // The basename is the last resort for a source the scanner cannot see (a stale manifest entry,
  // a source outside external/). `.md` is stripped so a bare command/agent file resolves to the
  // same output name eject would compute for it.
  const outName = item.name ?? comp?.name ?? basename(item.source, ".md");
  return {
    comp,
    outName,
    outType: item.as ?? comp?.type ?? "skill",
    overlayDir: join(root, "overlays", plugin, outName),
    id: `${plugin}/${outName}`,
  };
}

/**
 * Directory the names inside an overlay — and every `omit` pattern — resolve against upstream. A
 * skill source is already a directory; a bare command/agent file is addressed from its parent,
 * where its own name lives.
 */
export function upstreamBase(root: string, item: CurationItem, comp: ComponentInfo): string {
  const src = join(root, "external", item.source);
  return comp.type === "skill" ? src : dirname(src);
}

/** Item-relative POSIX path — the spelling every `omit` pattern is matched against. */
export function itemRelative(base: string, file: string): string {
  return relative(base, file).replaceAll("\\", "/");
}

/** True when an item-relative path is dropped by the item's `omit` list. */
export function isOmitted(rel: string, omit: string[] | undefined): boolean {
  return omit?.some((p) => matchesGlob(rel, p)) ?? false;
}
