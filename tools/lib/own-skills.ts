import { type Dirent, existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { CLAUDE_INVOCATION_KEYS } from "./claude-target.ts";
import { parseDoc } from "./frontmatter.ts";
import type { CurationManifest } from "./manifest.ts";
import { ordinalCompare } from "./order.ts";

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

const symlinkProblem = (path: string): string =>
  `${path} is a symlink — original skills are real directories, and the build never follows a link`;

/**
 * The directory–declaration contract (curation/SCHEMA.md `original_skills`), checked by the build
 * before any output is deleted and by validate. Every directory under `skills/<plugin>/` pairs with
 * exactly one declaration in that Module's manifest, its SKILL.md names it and leaves both Claude
 * invocation keys to the declaration, and no link stands in for a directory at either level. A file
 * such as `NOTICE.md` is not a skill and is ignored.
 */
export function collectOriginalSkillProblems(root: string, manifests: CurationManifest[]): string[] {
  const problems: string[] = [];
  const skillsRoot = join(root, "skills");
  const folders = new Map<string, Dirent[]>();
  const linkedFolders = new Set<string>();
  for (const entry of existsSync(skillsRoot) ? readdirSync(skillsRoot, { withFileTypes: true }) : []) {
    if (entry.isSymbolicLink()) {
      problems.push(symlinkProblem(`skills/${entry.name}`));
      linkedFolders.add(entry.name);
      continue;
    }
    if (!entry.isDirectory()) {
      continue;
    }
    if (!manifests.some((manifest) => manifest.plugin.name === entry.name)) {
      problems.push(
        `skills/${entry.name}/ matches no curation manifest — the build never reads it; use a Module's plugin name or add curation/${entry.name}.yaml`,
      );
      continue;
    }
    folders.set(entry.name, readdirSync(join(skillsRoot, entry.name), { withFileTypes: true }));
  }

  for (const manifest of manifests) {
    const plugin = manifest.plugin.name;
    const declared = manifest.original_skills ?? {};
    const present = new Set<string>();
    for (const entry of folders.get(plugin) ?? []) {
      const path = `skills/${plugin}/${entry.name}`;
      if (entry.isSymbolicLink()) {
        problems.push(symlinkProblem(path));
        present.add(entry.name);
        continue;
      }
      if (!entry.isDirectory()) {
        continue;
      }
      present.add(entry.name);
      if (!Object.hasOwn(declared, entry.name)) {
        problems.push(
          `${path}/ has no declaration — add original_skills: ${entry.name}: with its invocation to curation/${plugin}.yaml`,
        );
        continue;
      }
      const skillFile = join(root, path, "SKILL.md");
      if (!existsSync(skillFile)) {
        problems.push(`${path}/SKILL.md is missing — an original skill is a directory with a SKILL.md`);
        continue;
      }
      const { frontmatter } = parseDoc(readFileSync(skillFile, "utf8"));
      if (frontmatter.name !== entry.name) {
        problems.push(
          `${path}/SKILL.md: name is ${String(frontmatter.name)} but the directory is ${entry.name} — the directory is the skill's identity in every harness`,
        );
      }
      for (const key of CLAUDE_INVOCATION_KEYS) {
        if (Object.hasOwn(frontmatter, key)) {
          problems.push(
            `${path}/SKILL.md: ${key} is owned by original_skills.${entry.name}.invocation in curation/${plugin}.yaml — remove it`,
          );
        }
      }
    }
    if (linkedFolders.has(plugin)) {
      continue; // the link is the problem; its declarations are judged once it is a real directory
    }
    for (const name of Object.keys(declared)) {
      if (!present.has(name)) {
        problems.push(
          `curation/${plugin}.yaml: original_skills.${name} has no skills/${plugin}/${name}/ directory — remove the declaration or add the skill`,
        );
      }
    }
  }
  return problems.sort(ordinalCompare);
}
