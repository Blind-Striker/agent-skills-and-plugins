import { existsSync, readdirSync } from "node:fs";
import { join } from "node:path";
import type { CurationManifest } from "./manifest.ts";

export interface OwnSkillIdentity {
  plugin: string;
  name: string;
  address: string;
}

export function ownSkillIdentities(root: string, manifests: CurationManifest[]): OwnSkillIdentity[] {
  return manifests
    .flatMap((manifest) => {
      const plugin = manifest.plugin.name;
      const dir = join(root, "skills", plugin);
      if (!existsSync(dir)) {
        return [];
      }
      return readdirSync(dir, { withFileTypes: true })
        .filter((entry) => entry.isDirectory())
        .map((entry) => ({ plugin, name: entry.name, address: `${plugin}:${entry.name}` }));
    })
    .sort((a, b) => a.address.localeCompare(b.address));
}
