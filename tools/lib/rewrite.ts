import { basename } from "node:path";
import type { CurationManifest } from "./manifest.ts";
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
 * How the target harness spells a reference to one of our own components — the other half of the
 * rewrite, and the reason there is one map per output tree rather than one shared map.
 *
 * Claude Code addresses a plugin skill as `<plugin>:<name>`. OpenCode has no plugin concept and a
 * flat namespace: it addresses a skill by its `name` alone, so the qualified form is not merely
 * redundant there, it resolves to nothing.
 */
export type RefStyle = "claude" | "opencode";

export function buildRewriteMap(
  manifests: CurationManifest[],
  components: ComponentInfo[],
  style: RefStyle = "claude",
): Map<string, string> {
  const bySource = new Map(components.map((c) => [c.sourcePath, c]));
  const map = new Map<string, string>();
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const c = bySource.get(item.source);
      if (!c) {
        continue;
      }
      // The value is our own output name, which build.ts forces onto the emitted dir/file name.
      const outName = item.name ?? c.name;
      map.set(`${c.namespace}:${addressOf(c)}`, style === "opencode" ? outName : `${m.plugin.name}:${outName}`);
    }
  }
  return map;
}

/**
 * One pass over the shared scan (ADR-0008), so the rewrite touches exactly what every other reader
 * calls a reference. The lookup is exact because the scan already returns the maximal address: a
 * curated `sp:foo` cannot eat the prefix of an uncurated `sp:foo-bar`, which is what the old
 * longest-key-first ordering existed to prevent. Everything outside a replaced address is copied
 * byte-for-byte — a pointer's leading slash included, since it sits outside `address`.
 */
export function rewriteRefs(content: string, map: Map<string, string>): string {
  let out = "";
  let cut = 0;
  for (const ref of scanRefs(content)) {
    const value = map.get(ref.address);
    if (value === undefined) {
      continue;
    }
    out += content.slice(cut, ref.index) + value;
    cut = ref.index + ref.address.length;
  }
  return out + content.slice(cut);
}
