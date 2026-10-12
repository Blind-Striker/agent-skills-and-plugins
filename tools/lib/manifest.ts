import { readFileSync } from "node:fs";
import { parse as parseYaml } from "yaml";

export type ComponentType = "skill" | "command" | "agent";

/**
 * The harness-neutral intent that a curated item and an original skill both state. Every emitter
 * reads these fields the same way, whichever kind of item states them.
 */
export interface SkillIntent {
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
  /**
   * Output path -> the item-relative file each harness ships there (curation/SCHEMA.md
   * `harness_files`). `null` states that a harness ships no file at that path. The mapped sources
   * themselves ship in no tree.
   */
  harness_files?: HarnessFiles;
}

export interface CurationItem extends SkillIntent {
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
  /**
   * Upstream sources whose content this item's body merges in (ADR-0001). Each is blessed like the
   * primary and drift in any of them stops the build. Written either as a bare address — stamped
   * under the same-filename rule — or as `{source, files}` when the merge drew from files the
   * overlay does not itself own, which the filename rule cannot see.
   */
  merged_from?: MergeSource[];
}

/** The three emitted harness trees, in the order the manifest names them. */
export const HARNESSES = ["claude", "codex", "opencode"] as const;
export type Harness = (typeof HARNESSES)[number];
export type HarnessFiles = Record<string, Record<Harness, string | null>>;

/** A declared merge source, normalized: the bare-address spelling arrives here as `{source}`. */
export interface MergeSource {
  source: string;
  /** The files this merge drew from. Absent means the same-filename rule decides. */
  files?: string[];
}

/**
 * An original skill's declaration (curation/SCHEMA.md `original_skills`). The skill's directory
 * `skills/<plugin>/<name>/` owns content and description; the declaration owns the intent. An
 * original has no upstream posture to pass through, so it always states its invocation.
 */
export type OriginalSkill = SkillIntent & { invocation: NonNullable<SkillIntent["invocation"]> };

export interface CurationManifest {
  plugin: { name: string; description: string; version: string };
  /** Directory name under `skills/<plugin>/` -> that original skill's declaration. */
  original_skills?: Record<string, OriginalSkill>;
  items: CurationItem[];
  hooks?: { include: string[] };
}

/**
 * What an unknown manifest source means and what to do about it (workflow.md "Upstream sync
 * flow"). validate, build, the assemble mirror, and sync all say this sentence, so a pin move that
 * deletes a source gets one instruction wherever it is first noticed.
 */
export const UNKNOWN_SOURCE_RULE =
  "the current pin has no such source. If upstream deleted it, remove the item from the manifest — an exclude is not a tombstone — and give the reason in the commit message and, when another item succeeds it, in that item's comment. If upstream moved it, repoint source:. An exclude for a source that only a later pin has lands with or after that pin move.";

function requireEnum(path: string, source: string, field: string, value: unknown, allowed: readonly string[]): void {
  if (value === undefined) {
    return;
  }
  if (typeof value !== "string" || !allowed.includes(value)) {
    throw new Error(`${path}: ${source}: ${field} must be one of ${allowed.join("|")} (got ${JSON.stringify(value)})`);
  }
}

/** An item-relative POSIX path that stays inside the item folder. */
function itemRelativePath(value: string): boolean {
  return (
    value.length > 0 &&
    !value.startsWith("/") &&
    !value.includes("\\") &&
    !/^[A-Za-z]:/.test(value) &&
    value.split("/").every((segment) => segment !== "" && segment !== "." && segment !== "..")
  );
}

/** The `harness_files` grammar, checked at load so every consumer reads one shape. */
function checkHarnessFiles(path: string, source: string, raw: unknown): HarnessFiles {
  const where = `${path}: ${source}: harness_files`;
  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) {
    throw new Error(`${where} must map an output path to its sources`);
  }
  const files = raw as Record<string, unknown>;
  const sources = new Set<string>();
  for (const [output, mapping] of Object.entries(files)) {
    if (typeof mapping !== "object" || mapping === null || Array.isArray(mapping)) {
      throw new Error(`${where} ${output} must map claude, codex, and opencode to a source or null`);
    }
    if (!itemRelativePath(output)) {
      throw new Error(`${where} ${output} is not an item-relative path inside the item folder`);
    }
    if (output === "SKILL.md") {
      throw new Error(`${where} cannot replace SKILL.md, the body every harness shares`);
    }
    const byHarness = mapping as Record<string, unknown>;
    for (const key of Object.keys(byHarness)) {
      if (!(HARNESSES as readonly string[]).includes(key)) {
        throw new Error(`${where} ${output} names an unknown harness ${key}`);
      }
    }
    for (const harness of HARNESSES) {
      if (!(harness in byHarness)) {
        throw new Error(`${where} ${output} states no source for ${harness} — name one, or null to ship no file there`);
      }
      const value = byHarness[harness];
      if (value === null) {
        continue;
      }
      if (typeof value !== "string" || !itemRelativePath(value)) {
        throw new Error(`${where} ${output}: ${harness} source ${JSON.stringify(value)} is not an item-relative path`);
      }
      if (value === "SKILL.md") {
        throw new Error(`${where} ${output}: ${harness} cannot take SKILL.md as a source; the body ships everywhere`);
      }
      sources.add(value);
    }
    if (HARNESSES.every((harness) => byHarness[harness] === null)) {
      throw new Error(`${where} ${output} maps no harness — drop it`);
    }
  }
  for (const output of Object.keys(files)) {
    if (sources.has(output)) {
      throw new Error(`${where} ${output} is also a mapped source, which ships under no name of its own`);
    }
  }
  return files as HarnessFiles;
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
    requireEnum(path, item.source, "invocation", item.invocation, ["auto", "manual", "both"]);
    requireEnum(path, item.source, "as", item.as, ["skill", "command", "agent"]);
    requireEnum(path, item.source, "body", item.body, ["overlay", "patch"]);
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
    if (item.harness_files !== undefined) {
      item.harness_files = checkHarnessFiles(path, item.source, item.harness_files);
    }
  }
  if (raw.original_skills !== undefined) {
    raw.original_skills = loadOriginalSkills(path, raw.original_skills);
  }
  return raw;
}

/** The keys an original-skill declaration may hold; the SKILL.md owns everything else. */
const ORIGINAL_SKILL_KEYS = ["invocation", "depends_on", "harness_files"];

function isMapping(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * The `original_skills` grammar. The key set is closed, so a misspelled key or a curated-only key
 * such as `source` or `frontmatter` is an error rather than a silently ignored line.
 */
function loadOriginalSkills(path: string, raw: unknown): Record<string, OriginalSkill> {
  if (!isMapping(raw)) {
    throw new Error(`${path}: original_skills must map a skill directory name to its declaration`);
  }
  for (const [name, declaration] of Object.entries(raw)) {
    const label = `original_skills.${name}`;
    if (!isMapping(declaration)) {
      throw new Error(`${path}: ${label} must be a mapping`);
    }
    for (const key of Object.keys(declaration)) {
      if (!ORIGINAL_SKILL_KEYS.includes(key)) {
        throw new Error(
          `${path}: ${label}: unknown key ${key} — a declaration holds only ${ORIGINAL_SKILL_KEYS.join(", ")}; the SKILL.md owns content and description`,
        );
      }
    }
    if (declaration.invocation === undefined) {
      throw new Error(`${path}: ${label}: invocation is required (auto|manual|both)`);
    }
    requireEnum(path, label, "invocation", declaration.invocation, ["auto", "manual", "both"]);
    if (declaration.harness_files !== undefined) {
      declaration.harness_files = checkHarnessFiles(path, label, declaration.harness_files);
    }
  }
  return raw as Record<string, OriginalSkill>;
}
