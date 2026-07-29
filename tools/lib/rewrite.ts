import { basename } from "node:path";
import type { CurationManifest } from "./manifest.ts";
import type { ComponentInfo } from "./scan.ts";

// How a harness ADDRESSES the component upstream, which is what its references spell out: a skill by
// its directory name, a command or agent by its file name. The frontmatter `name` is not the address
// and diverges from it in 32 of the 223 upstream components, so keying on it missed the real refs.
function addressOf(c: ComponentInfo): string {
  return c.type === "skill" ? basename(c.sourcePath) : basename(c.sourcePath, ".md");
}

export function buildRewriteMap(manifests: CurationManifest[], components: ComponentInfo[]): Map<string, string> {
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
      map.set(`${c.namespace}:${addressOf(c)}`, `${m.plugin.name}:${item.name ?? c.name}`);
    }
  }
  return map;
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

export function rewriteRefs(content: string, map: Map<string, string>): string {
  // Longest first: `sp:foo-bar` must be rewritten before `sp:foo` matches its prefix.
  const entries = [...map].sort(([a], [b]) => b.length - a.length);
  let out = content;
  for (const [key, value] of entries) {
    // Boundary-anchored, so a curated `sp:foo` never eats the prefix of an uncurated `sp:foo-bar`
    // and turn it into a dangling reference. Replacement is a function: values are plain text and
    // must not be read for $-patterns.
    out = out.replace(new RegExp(`(?<![a-z0-9-])${escapeRegExp(key)}(?![a-z0-9-])`, "g"), () => value);
  }
  return out;
}
