import type { ComponentType } from "./manifest.ts";

/** Skill frontmatter OpenCode 2 recognises; every other key is reported as dropped. */
export const OPENCODE_SKILL_KEYS: ReadonlySet<string> = new Set([
  "name",
  "description",
  "license",
  "compatibility",
  "metadata",
]);
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
