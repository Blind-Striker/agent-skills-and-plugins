import { readFileSync } from "node:fs";
import { parse as parseYaml } from "yaml";

export type ComponentType = "skill" | "command" | "agent";

export interface CurationItem {
  source: string;
  /** Reject the whole item. For dropping FILES from an item that is taken, see `omit`. */
  exclude?: boolean;
  /**
   * Glob patterns, item-relative and POSIX-spelled, for upstream files to leave behind — author
   * logs, pressure tests, fixtures. Applied to the upstream copy before any overlay or patch, so
   * `body:` describes edits to what survives.
   */
  omit?: string[];
  name?: string;
  as?: ComponentType;
  /**
   * Who pulls the trigger (ADR-0005), for items emitted as skills. Absent is not a default: it
   * means the item states no intent and upstream's own frontmatter passes through untouched.
   */
  invocation?: "auto" | "manual" | "both";
  /**
   * Output names of this item's model-edge targets (ADR-0008). Enforced both ways by validate:
   * a declared name with no matching fact in the shipped body is stale, a fact with no
   * declaration is undeclared — both are errors.
   */
  depends_on?: string[];
  frontmatter?: Record<string, unknown>;
  /** `patch` applies overlays/<plugin>/<item>/overlay.patch; `overlay` replaces whole files. */
  body?: "overlay" | "patch";
  /**
   * Upstream sources whose content this item's body merges in (ADR-0001). Each is blessed like the
   * primary and drift in any of them stops the build. Written either as a bare address — stamped
   * under the same-filename rule — or as `{source, files}` when the merge drew from files the
   * overlay does not itself own, which the filename rule cannot see.
   */
  merged_from?: MergeSource[];
}

/** A declared merge source, normalized: the bare-address spelling arrives here as `{source}`. */
export interface MergeSource {
  source: string;
  /** The files this merge drew from. Absent means the same-filename rule decides. */
  files?: string[];
}

export interface CurationManifest {
  plugin: { name: string; description: string; version: string };
  items: CurationItem[];
  hooks?: { include: string[] };
}

export function loadManifest(path: string): CurationManifest {
  const raw = parseYaml(readFileSync(path, "utf8")) as CurationManifest | null;
  if (!raw?.plugin?.name) {
    throw new Error(`${path}: plugin.name is required`);
  }
  if (!raw.plugin.description) {
    throw new Error(`${path}: plugin.description is required`);
  }
  if (!raw.plugin.version) {
    throw new Error(`${path}: plugin.version is required`);
  }
  raw.items ??= [];
  for (const item of raw.items) {
    if (!item.source) {
      throw new Error(`${path}: every item needs a source`);
    }
    // Both spellings are normalized here so every consumer reads one shape. A bare address is the
    // common case and stays cheap to write; the object form exists for the merge the filename rule
    // cannot cover, and an empty file list means it was written but says nothing.
    const declared = item.merged_from as unknown as (string | MergeSource)[] | undefined;
    if (declared) {
      item.merged_from = declared.map((entry) => {
        if (typeof entry === "string") {
          return { source: entry };
        }
        if (!entry?.source) {
          throw new Error(`${path}: ${item.source}: every merged_from entry needs a source`);
        }
        if (entry.files && !entry.files.length) {
          throw new Error(`${path}: ${item.source}: merged_from ${entry.source} has an empty files list — drop it`);
        }
        return entry;
      });
    }
  }
  return raw;
}
