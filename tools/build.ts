import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { basename, join } from "node:path";
import { pathToFileURL } from "node:url";
import { parseDoc, serializeDoc } from "./lib/frontmatter.ts";
import { type CurationItem, type CurationManifest, loadManifest } from "./lib/manifest.ts";
import { buildRewriteMap, rewriteRefs } from "./lib/rewrite.ts";
import { type ComponentInfo, scanSubmodule } from "./lib/scan.ts";

export function buildAll(root: string): string[] {
  const report: string[] = [];
  const manifests = readdirSync(join(root, "curation"))
    .filter((f) => f.endsWith(".yaml"))
    .sort()
    .map((f) => loadManifest(join(root, "curation", f)));
  const components = readdirSync(join(root, "external")).flatMap((s) => scanSubmodule(join(root, "external"), s));

  rmSync(join(root, "plugins"), { recursive: true, force: true });
  rmSync(join(root, "opencode"), { recursive: true, force: true });

  for (const m of manifests) {
    if (m.hooks?.include?.length) {
      throw new Error(`${m.plugin.name}: hooks.include is not implemented yet — keep it empty (YAGNI)`);
    }
    for (const item of m.items) {
      emitItem(root, m, item, components, report);
    }
    emitOwnSkills(root, m, report);
    const pluginDir = join(root, "plugins", m.plugin.name);
    mkdirSync(join(pluginDir, ".claude-plugin"), { recursive: true });
    writeFileSync(join(pluginDir, ".claude-plugin", "plugin.json"), JSON.stringify(m.plugin, null, 2) + "\n");
  }

  writeMarketplace(root, manifests);
  rewriteTree(join(root, "plugins"), buildRewriteMap(manifests, components));
  return report;
}

function emitItem(
  root: string,
  m: CurationManifest,
  item: CurationItem,
  components: ComponentInfo[],
  report: string[],
): void {
  if (item.exclude) {
    return;
  }
  const comp = components.find((c) => c.sourcePath === item.source);
  if (!comp) {
    throw new Error(`${m.plugin.name}: source not found in external/: ${item.source}`);
  }
  const outName = item.name ?? comp.name;
  const outType = item.as ?? comp.type;
  const srcPath = join(root, "external", item.source);
  const overlayDir = join(root, "overlays", m.plugin.name, outName);
  const pluginDir = join(root, "plugins", m.plugin.name);

  if (item.body === "overlay" && !existsSync(overlayDir)) {
    throw new Error(
      `${m.plugin.name}/${outName}: body is overlay but overlays/${m.plugin.name}/${outName}/ is missing — run: npm run eject ${m.plugin.name} ${outName}`,
    );
  }

  if (outType === "skill") {
    if (comp.type !== "skill") {
      throw new Error(`${item.source}: ${comp.type} -> skill conversion not supported`);
    }
    const destDir = join(pluginDir, "skills", outName);
    cpSync(srcPath, destDir, { recursive: true });
    if (item.body === "overlay") {
      cpSync(overlayDir, destDir, { recursive: true, force: true });
    }
    const skillMd = join(destDir, "SKILL.md");
    const doc = parseDoc(readFileSync(skillMd, "utf8"));
    doc.frontmatter = { ...doc.frontmatter, name: outName, ...item.frontmatter };
    writeFileSync(skillMd, serializeDoc(doc));
    report.push(`${m.plugin.name}: skill ${outName} <- ${item.source}${item.body === "overlay" ? " (overlay)" : ""}`);
  } else {
    const srcFile = comp.type === "skill" ? join(srcPath, "SKILL.md") : srcPath;
    let doc = parseDoc(readFileSync(srcFile, "utf8"));
    if (item.body === "overlay") {
      doc = parseDoc(readFileSync(join(overlayDir, basename(srcFile)), "utf8"));
    }
    if (comp.type === "skill") {
      const extras = readdirSync(srcPath).filter((f) => f !== "SKILL.md");
      if (extras.length) {
        report.push(`WARN ${m.plugin.name}/${outName}: dropped in skill->${outType} conversion: ${extras.join(", ")}`);
      }
    }
    const base: Record<string, unknown> =
      outType === "command"
        ? { description: String(doc.frontmatter.description ?? "") }
        : { name: outName, description: String(doc.frontmatter.description ?? "") };
    doc = { frontmatter: { ...base, ...item.frontmatter }, body: doc.body };
    const kindDir = outType === "command" ? "commands" : "agents";
    mkdirSync(join(pluginDir, kindDir), { recursive: true });
    writeFileSync(join(pluginDir, kindDir, `${outName}.md`), serializeDoc(doc));
    report.push(`${m.plugin.name}: ${outType} ${outName} <- ${item.source}`);
  }
}

function emitOwnSkills(root: string, m: CurationManifest, report: string[]): void {
  const ownDir = join(root, "skills", m.plugin.name);
  if (!existsSync(ownDir)) {
    return;
  }
  for (const name of readdirSync(ownDir)) {
    if (!statSync(join(ownDir, name)).isDirectory()) {
      continue;
    }
    cpSync(join(ownDir, name), join(root, "plugins", m.plugin.name, "skills", name), { recursive: true });
    report.push(`${m.plugin.name}: skill ${name} <- skills/ (own)`);
  }
}

function writeMarketplace(root: string, manifests: CurationManifest[]): void {
  const marketplace = {
    name: "deniz-skills",
    owner: { name: "Deniz Irgin", email: "denizirgin@gmail.com" },
    plugins: manifests.map((m) => ({
      name: m.plugin.name,
      source: `./plugins/${m.plugin.name}`,
      description: m.plugin.description,
    })),
  };
  mkdirSync(join(root, ".claude-plugin"), { recursive: true });
  writeFileSync(join(root, ".claude-plugin", "marketplace.json"), JSON.stringify(marketplace, null, 2) + "\n");
}

function rewriteTree(dir: string, map: Map<string, string>): void {
  if (!existsSync(dir)) {
    return;
  }
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) {
      rewriteTree(p, map);
    } else if (e.name.endsWith(".md")) {
      writeFileSync(p, rewriteRefs(readFileSync(p, "utf8"), map));
    }
  }
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) {
  for (const line of buildAll(process.cwd())) {
    console.log(line);
  }
  console.log("Build complete.");
}
