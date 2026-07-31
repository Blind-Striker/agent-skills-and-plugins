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
   * Upstream addresses whose content this item's body merges in (ADR-0001). Each is blessed like
   * the primary under the same-filename rule; drift in any source stops the build.
   */
  merged_from?: string[];
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
  }
  return raw;
}
