import { basename } from "node:path";
import type { ComponentType, CurationManifest } from "./manifest.ts";
import { openCodeId } from "./opencode-target.ts";
import type { OwnSkillIdentity } from "./own-skills.ts";
import { type SkillToolCall, scanPathClaims, scanRefs, scanSkillToolCalls } from "./refs.ts";
import type { ComponentInfo } from "./scan.ts";

// How a harness ADDRESSES the component upstream, which is what its references spell out: a skill by
// its directory name, a command or agent by its file name. The frontmatter `name` is not the address
// and diverges from it in 32 of the 223 upstream components, so keying on it missed the real refs.
// `<name>.agent.md` is a double extension, not part of the address — references spell the bare name.
export function addressOf(c: ComponentInfo): string {
  return c.type === "skill" ? basename(c.sourcePath) : basename(c.sourcePath, ".md").replace(/\.agent$/, "");
}

/**
 * How the target harness spells a reference to one of our own components. One map serves every
 * output tree: its value is the resolved target, and each style below renders that target.
 *
 * Claude Code addresses a plugin skill as `<plugin>:<name>`; Codex as `$<plugin>:<name>`; OpenCode 2
 * as the dotted ID `<plugin>.<name>`, which contains no `:` and so cannot be re-detected by the scan.
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
      return openCodeId(target.plugin, target.name);
  }
}

/** OpenCode 2's user pointer: `/<id>` runs a command; `@<id>` mentions a skill or an agent. */
function renderPointer(target: RewriteTarget, style: RefStyle): string {
  if (style === "opencode") {
    return `${target.kind === "command" ? "/" : "@"}${render(target, style)}`;
  }
  return render(target, style);
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
    // kinds with `$`, so consume that slash instead of producing the invalid `/$plugin:skill`;
    // OpenCode consumes it too and renders its own prefix, which depends on the target kind.
    const pointer = ref.kind === "pointer";
    const start = pointer && style !== "claude" ? ref.index - 1 : ref.index;
    const rendered = pointer && style !== "claude" ? renderPointer(target, style) : render(target, style);
    out += content.slice(cut, start) + rendered;
    cut = ref.index + ref.address.length;
  }
  return out + content.slice(cut);
}

const CODEX_VERB = { Call: "Invoke", call: "invoke", calls: "invokes", calling: "invoking" } as const;

// Closed dispatch table (references-and-linking.md "Harness phrasing"). Codex names no subagent tool
// or agent type: neither is recorded in this repository's research.
const DISPATCH: { pattern: RegExp; opencode: string; codex: string }[] = [
  { pattern: /Subagent \(general-purpose\)/g, opencode: "Subagent (general)", codex: "Subagent" },
  { pattern: /`general-purpose`(\s+)subagent/g, opencode: "`general`$1subagent", codex: "subagent" },
  { pattern: /`Agent`( calls?)\b/g, opencode: "`subagent`$1", codex: "subagent$1" },
];

/** Claude Code tool words that must not survive in an OpenCode or Codex tree. */
export const CLAUDE_ONLY_VOCABULARY: readonly RegExp[] = [
  /\bskill tool/gi,
  /Subagent \(general-purpose\)/g,
  /`general-purpose`/g,
  /\bgeneral-purpose\s+(?:sub)?agent\b/g,
  /`Agent`/g,
  /\bAgent tool\b/g,
  /\bTask tool\b/g,
  /\bsubagent_type\b/g,
];

export function claudeOnlyVocabulary(content: string): { index: number; match: string }[] {
  return CLAUDE_ONLY_VOCABULARY.flatMap((re) =>
    [...content.matchAll(re)].map((m) => ({ index: m.index, match: m[0] })),
  ).sort((a, b) => a.index - b.index);
}

function renderCall(content: string, call: SkillToolCall, style: "opencode" | "codex"): string {
  const span = content.slice(call.index, call.end);
  if (style === "opencode") {
    return span.replace("the Skill tool", "the `skill` tool");
  }
  const verb = CODEX_VERB[call.verb];
  return call.payloads.length ? `${verb} ${call.payloads.map((p) => `\`${p.text}\``).join(" and ")}` : verb;
}

/** Renders the skill-tool call and dispatch words for one harness; facts stay neutral. */
export function renderHarnessPhrasing(content: string, style: RefStyle): string {
  if (style === "claude") {
    return content;
  }
  let out = "";
  let cut = 0;
  for (const call of scanSkillToolCalls(content)) {
    out += content.slice(cut, call.index) + renderCall(content, call, style);
    cut = call.end;
  }
  out += content.slice(cut);
  for (const row of DISPATCH) {
    out = out.replace(row.pattern, row[style]);
  }
  return out;
}

/** Harness phrasing first, then the facts it left neutral. */
export function localize(content: string, map: Map<string, RewriteTarget>, style: RefStyle): string {
  return rewriteRefs(renderHarnessPhrasing(content, style), map, style);
}

/**
 * OpenCode 2 installs every skill folder under its ID, so a path that names a sibling by its bare
 * folder name would land on nothing. A path is respelled only when `scanPathClaims` returns it — a
 * climb whose `../` count lands exactly on the shared `skills/` directory, or an item-root
 * `skills/<name>/` path, neither starting inside a longer path — and its segment is an emitted
 * skill's bare name; anything else is left for the linker to judge. A path into the item's own
 * folder is respelled too: that folder is renamed. The text may be any bundled file, not only
 * Markdown.
 */
export function rewriteOpenCodePaths(
  content: string,
  depthBelowSkillFolder: number,
  skillIds: Map<string, string>,
): string {
  let out = "";
  let cut = 0;
  for (const claim of scanPathClaims(content, depthBelowSkillFolder)) {
    const id = skillIds.get(claim.segment);
    if (id === undefined) {
      continue;
    }
    out += content.slice(cut, claim.segmentIndex) + id;
    cut = claim.segmentIndex + claim.segment.length;
  }
  return out + content.slice(cut);
}

/** A file the path respelling may read: no NUL byte, and bytes that survive a UTF-8 round trip. */
export function isBundledText(bytes: Buffer): boolean {
  return !bytes.includes(0) && Buffer.from(bytes.toString("utf8"), "utf8").equals(bytes);
}
