import { basename } from "node:path";
import type { ComponentType, CurationManifest } from "./manifest.ts";
import type { OwnSkillIdentity } from "./own-skills.ts";
import { scanRefs } from "./refs.ts";
import type { ComponentInfo } from "./scan.ts";

// How a harness ADDRESSES the component upstream, which is what its references spell out: a skill by
// its directory name, a command or agent by its file name. The frontmatter `name` is not the address
// and diverges from it in 32 of the 223 upstream components, so keying on it missed the real refs.
// `<name>.agent.md` is a double extension, not part of the address — references spell the bare name.
function addressOf(c: ComponentInfo): string {
  return c.type === "skill" ? basename(c.sourcePath) : basename(c.sourcePath, ".md").replace(/\.agent$/, "");
}

/**
 * How the target harness spells a reference to one of our own components. One map serves every
 * output tree: its value is the resolved target, and each style below renders that target.
 *
 * Claude Code addresses a plugin skill as `<plugin>:<name>`; Codex as `$<plugin>:<name>`. The
 * `opencode` style still renders the bare output `name`, unchanged from the OpenCode 1 emitter,
 * until the OpenCode 2 renderer replaces it.
 */
export type RefStyle = "claude" | "opencode" | "codex";

/** The resolved output a reference points at: owning Plugin, output name, and output kind. */
export interface RewriteTarget {
  plugin: string;
  name: string;
  kind: ComponentType;
}

export function buildRewriteMap(
  manifests: CurationManifest[],
  components: ComponentInfo[],
  ownSkills: OwnSkillIdentity[] = [],
): Map<string, RewriteTarget> {
  const bySource = new Map(components.map((c) => [c.sourcePath, c]));
  const map = new Map<string, RewriteTarget>();
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const c = bySource.get(item.source);
      if (!c) {
        continue;
      }
      // The name is our own output name, which build.ts forces onto the emitted dir/file name.
      map.set(`${c.namespace}:${addressOf(c)}`, {
        plugin: m.plugin.name,
        name: item.name ?? c.name,
        kind: item.as ?? c.type,
      });
    }
  }
  for (const own of ownSkills) {
    const value: RewriteTarget = { plugin: own.plugin, name: own.name, kind: "skill" };
    const existing = map.get(own.address);
    if (existing && (existing.plugin !== value.plugin || existing.name !== value.name)) {
      throw new Error(
        `reference identity ${own.address} resolves to both ${existing.plugin}:${existing.name} and ${own.address}`,
      );
    }
    map.set(own.address, value);
  }
  return map;
}

function render(target: RewriteTarget, style: RefStyle): string {
  switch (style) {
    case "claude":
      return `${target.plugin}:${target.name}`;
    case "codex":
      return `$${target.plugin}:${target.name}`;
    case "opencode":
      return target.name;
  }
}

/**
 * One pass over the shared scan (ADR-0008), so the rewrite touches exactly what every other reader
 * calls a reference. The lookup is exact because the scan already returns the maximal address: a
 * curated `sp:foo` cannot eat the prefix of an uncurated `sp:foo-bar`, which is what the old
 * longest-key-first ordering existed to prevent. Everything outside a replaced address is copied
 * byte-for-byte — a pointer's leading slash included, since it sits outside `address`.
 */
export function rewriteRefs(content: string, map: Map<string, RewriteTarget>, style: RefStyle = "claude"): string {
  let out = "";
  let cut = 0;
  for (const ref of scanRefs(content)) {
    const target = map.get(ref.address);
    if (target === undefined) {
      continue;
    }
    // The scanner positions a pointer after its leading slash. Codex renders both semantic edge
    // kinds with `$`, so consume that slash instead of producing the invalid `/$plugin:skill`.
    const start = style === "codex" && ref.kind === "pointer" ? ref.index - 1 : ref.index;
    out += content.slice(cut, start) + render(target, style);
    cut = ref.index + ref.address.length;
  }
  return out + content.slice(cut);
}
