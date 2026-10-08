import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { AssembledItem } from "./assemble.ts";
import { type ParsedDoc, parseDoc } from "./frontmatter.ts";
import type { ComponentType, CurationItem, CurationManifest } from "./manifest.ts";

/**
 * Skill frontmatter OpenCode 2 reads besides `disable-model-invocation`, which the adaptation
 * renders as the hide key. Every other key is dropped and reported (ADR-0002, ADR-0006).
 */
export const OPENCODE_SKILL_KEYS: ReadonlySet<string> = new Set(["name", "description", "metadata"]);
/** OpenCode 2's native "do not advertise to the model" key, nested under `metadata`. */
export const OPENCODE_HIDE_KEY = "opencode/autoinvoke";
/** Native OpenCode 2 agent keys; any other key sends the file through the OpenCode 1 migrator. */
export const OPENCODE_AGENT_KEYS: ReadonlySet<string> = new Set([
  "description",
  "mode",
  "model",
  "variant",
  "request",
  "system",
  "permissions",
  "steps",
  "hidden",
  "color",
  "disabled",
]);
export const OPENCODE_AGENT_COLOR = /^#[0-9a-fA-F]{6}$/;

export function openCodeId(plugin: string, name: string): string {
  return `${plugin}.${name}`;
}

export function splitOpenCodeId(id: string, plugins: Iterable<string>): { plugin: string; name: string } | undefined {
  for (const plugin of plugins) {
    const prefix = `${plugin}.`;
    if (id.startsWith(prefix) && id.length > prefix.length) {
      return { plugin, name: id.slice(prefix.length) };
    }
  }
  return undefined;
}

export function openCodeBundlePath(kind: ComponentType, plugin: string, name: string): string {
  const id = openCodeId(plugin, name);
  return kind === "skill" ? `skills/${id}` : `${kind}s/${id}.md`;
}

/** Bundle path -> committed Claude Plugin path whose Git index mode the Bundle file inherits. */
export function claudeCounterpartPath(module: string, bundlePath: string): string | undefined {
  if (!bundlePath.startsWith("skills/")) {
    return undefined;
  }
  const [, folder, ...rest] = bundlePath.split("/");
  const prefix = `${module}.`;
  if (!folder?.startsWith(prefix) || folder.length === prefix.length) {
    throw new Error(`internal error: ${module}/${bundlePath} is not an OpenCode ID path`);
  }
  return ["plugins", module, "skills", folder.slice(prefix.length), ...rest].join("/");
}

const DMI = "disable-model-invocation";

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function sortedKeys(keys: string[]): string[] {
  return keys.sort((left, right) => left.localeCompare(right));
}

/**
 * ADR-0005 on OpenCode 2: `manual` hides; `auto` and `both` advertise; an absent invocation hides
 * only when upstream says `disable-model-invocation: true`, and otherwise passes upstream through.
 */
export function openCodeWouldHide(doc: ParsedDoc, invocation: CurationItem["invocation"]): boolean {
  if (invocation === "manual") {
    return true;
  }
  if (invocation === "auto" || invocation === "both") {
    return false;
  }
  return doc.frontmatter[DMI] === true;
}

export interface OpenCodeSkillAdaptation {
  document: ParsedDoc;
  /** Sorted neutral keys not kept, excluding a consumed upstream `disable-model-invocation`. */
  dropped: string[];
  /** False when the emitted document carries `metadata.opencode/autoinvoke: false`. */
  advertised: boolean;
  transformations: string[];
}

/** Pure: the neutral document is never mutated. The body passes through; rendering comes later. */
export function adaptOpenCodeSkillDocument(
  id: string,
  doc: ParsedDoc,
  invocation: CurationItem["invocation"],
): OpenCodeSkillAdaptation {
  const source = doc.frontmatter;
  const frontmatter: Record<string, unknown> = "name" in source ? {} : { name: id };
  for (const [key, value] of Object.entries(source)) {
    if (OPENCODE_SKILL_KEYS.has(key)) {
      frontmatter[key] = key === "name" ? id : isPlainObject(value) ? { ...value } : value;
    }
  }

  const hide = openCodeWouldHide(doc, invocation);
  const consumedDmi = hide && invocation === undefined;
  const transformations = consumedDmi ? [`${DMI}: true -> metadata.${OPENCODE_HIDE_KEY}: false`] : [];
  if (hide) {
    const metadata = frontmatter.metadata;
    if (metadata !== undefined && !isPlainObject(metadata)) {
      // collectOpenCodeEmissionProblems stops the build before emission reaches this point.
      throw new Error(`internal error: ${id}: metadata must be a mapping to carry ${OPENCODE_HIDE_KEY}`);
    }
    frontmatter.metadata = { ...metadata, [OPENCODE_HIDE_KEY]: false };
  } else if ((invocation === "auto" || invocation === "both") && isPlainObject(frontmatter.metadata)) {
    const { [OPENCODE_HIDE_KEY]: _removed, ...rest } = frontmatter.metadata;
    if (Object.keys(rest).length) {
      frontmatter.metadata = rest;
    } else {
      delete frontmatter.metadata;
    }
  }

  const dropped = sortedKeys(
    Object.keys(source).filter((key) => !OPENCODE_SKILL_KEYS.has(key) && !(consumedDmi && key === DMI)),
  );
  const metadata = frontmatter.metadata;
  const advertised = !(isPlainObject(metadata) && metadata[OPENCODE_HIDE_KEY] === false);
  return { document: { frontmatter, body: doc.body }, dropped, advertised, transformations };
}

function adaptOpenCodeDocument(
  doc: ParsedDoc,
  extra: Record<string, unknown>,
): { document: ParsedDoc; dropped: string[] } {
  const frontmatter: Record<string, unknown> = { description: doc.frontmatter.description, ...extra };
  const dropped = sortedKeys(Object.keys(doc.frontmatter).filter((key) => key !== "description" && key !== "name"));
  return { document: { frontmatter, body: doc.body }, dropped };
}

/** An `as: command` item: frontmatter is exactly `{ description }`. */
export function adaptOpenCodeCommandDocument(doc: ParsedDoc): { document: ParsedDoc; dropped: string[] } {
  return adaptOpenCodeDocument(doc, {});
}

/** An agent: frontmatter is exactly `{ description, mode: subagent }`; `name` is never written. */
export function adaptOpenCodeAgentDocument(doc: ParsedDoc): { document: ParsedDoc; dropped: string[] } {
  return adaptOpenCodeDocument(doc, { mode: "subagent" });
}

/**
 * Emission preflight on the neutral assembled documents: a skill the adaptation would hide needs a
 * `metadata` mapping to carry the hide key. Runs before any generated output is deleted.
 */
export function collectOpenCodeEmissionProblems(manifests: CurationManifest[], assembled: AssembledItem[]): string[] {
  const problems: string[] = [];
  for (const manifest of manifests) {
    const items = assembled
      .filter((item) => item.plugin === manifest.plugin.name && item.outType === "skill")
      .sort((left, right) => left.outName.localeCompare(right.outName));
    for (const item of items) {
      const doc = parseDoc(readFileSync(join(item.dir, "SKILL.md"), "utf8"));
      const metadata = doc.frontmatter.metadata;
      if (openCodeWouldHide(doc, item.item?.invocation) && metadata !== undefined && !isPlainObject(metadata)) {
        problems.push(`${item.plugin}/${item.outName}: metadata must be a mapping to carry ${OPENCODE_HIDE_KEY}`);
      }
    }
  }
  return problems;
}

/**
 * Every emitted skill's bare output name -> its OpenCode ID, across all Modules and original skills.
 * The identity preflight already made bare output names repository-unique, so the key is unambiguous.
 */
export function openCodeSkillIds(assembled: AssembledItem[]): Map<string, string> {
  return new Map(
    assembled
      .filter((item) => item.outType === "skill")
      .map((item) => [item.outName, openCodeId(item.plugin, item.outName)]),
  );
}
