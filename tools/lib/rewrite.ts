import type { CurationManifest } from "./manifest.ts";
import type { ComponentInfo } from "./scan.ts";

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
      map.set(`${c.namespace}:${c.name}`, `${m.plugin.name}:${item.name ?? c.name}`);
    }
  }
  return map;
}

export function rewriteRefs(content: string, map: Map<string, string>): string {
  // Longest first: `sp:foo-bar` must be rewritten before `sp:foo` matches its prefix.
  const entries = [...map].sort(([a], [b]) => b.length - a.length);
  let out = content;
  for (const [key, value] of entries) {
    out = out.replaceAll(key, value);
  }
  return out;
}
