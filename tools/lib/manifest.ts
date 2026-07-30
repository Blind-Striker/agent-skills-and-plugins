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
  frontmatter?: Record<string, unknown>;
  /** `patch` applies overlays/<plugin>/<item>/overlay.patch; `overlay` replaces whole files. */
  body?: "overlay" | "patch";
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
