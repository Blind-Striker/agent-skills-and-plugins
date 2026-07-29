import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { basename, dirname, join, relative } from "node:path";
import { parseDoc } from "./frontmatter.ts";
import type { ComponentType } from "./manifest.ts";

export interface ComponentInfo {
  submodule: string;
  namespace: string;
  type: ComponentType;
  name: string;
  description: string;
  sourcePath: string;
  files: number;
  bytes: number;
}

function* walk(dir: string): Generator<string> {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    // Symlinks are mirrors of content that already lives elsewhere in the tree
    // (e.g. aspire-skills republishes skills/ under .github/plugins/), so
    // following them would double-count every component behind the link.
    if (e.name === ".git" || e.name === "node_modules" || e.isSymbolicLink()) {
      continue;
    }
    const p = join(dir, e.name);
    if (e.isDirectory()) {
      yield* walk(p);
    } else {
      yield p;
    }
  }
}

/** Reads `name` from a plugin manifest, or undefined if it is missing, unreadable or malformed. */
function readPluginName(manifest: string): string | undefined {
  if (!existsSync(manifest)) {
    return undefined;
  }
  let name: unknown;
  try {
    name = (JSON.parse(readFileSync(manifest, "utf8")) as { name?: unknown } | null)?.name;
  } catch {
    return undefined;
  }
  return typeof name === "string" && name !== "" ? name : undefined;
}

function findNamespace(startDir: string, stopDir: string): string {
  let dir = startDir;
  while (true) {
    // Canonical layout first, then the bare manifest used by marketplace
    // monorepos such as dotnet/skills (plugins/<name>/plugin.json).
    const name = readPluginName(join(dir, ".claude-plugin", "plugin.json")) ?? readPluginName(join(dir, "plugin.json"));
    if (name !== undefined) {
      return name;
    }
    const parent = dirname(dir);
    if (dir === stopDir || dir === parent) {
      return basename(stopDir);
    }
    dir = parent;
  }
}

function rel(root: string, p: string): string {
  return relative(root, p).replaceAll("\\", "/");
}

export function scanSubmodule(externalDir: string, submoduleArg: string): ComponentInfo[] {
  const submodule = submoduleArg.replace(/[\\/]+$/, "");
  const root = join(externalDir, submodule);
  const out: ComponentInfo[] = [];
  for (const file of walk(root)) {
    const r = rel(root, file);
    if (basename(file) === "SKILL.md") {
      const dir = dirname(file);
      const { frontmatter } = parseDoc(readFileSync(file, "utf8"));
      const all = [...walk(dir)];
      out.push({
        submodule,
        namespace: findNamespace(dir, root),
        type: "skill",
        name: String(frontmatter.name ?? basename(dir)),
        description: String(frontmatter.description ?? ""),
        sourcePath: `${submodule}/${rel(root, dir)}`,
        files: all.length,
        bytes: all.reduce((s, f) => s + statSync(f).size, 0),
      });
    } else if (/(^|\/)(commands|agents)\/[^/]+\.md$/.test(r)) {
      const type: ComponentType = /(^|\/)commands\//.test(r) ? "command" : "agent";
      const { frontmatter } = parseDoc(readFileSync(file, "utf8"));
      out.push({
        submodule,
        namespace: findNamespace(dirname(file), root),
        type,
        name: String(frontmatter.name ?? basename(file, ".md")),
        description: String(frontmatter.description ?? ""),
        sourcePath: `${submodule}/${r}`,
        files: 1,
        bytes: statSync(file).size,
      });
    }
  }
  return out;
}
