import {
  cpSync,
  existsSync,
  lstatSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { basename, join, relative } from "node:path";
import { pathToFileURL } from "node:url";
import { parseDoc, serializeDoc } from "./lib/frontmatter.ts";
import { type CurationItem, type CurationManifest, loadManifest } from "./lib/manifest.ts";
import { requireSubmodules } from "./lib/preflight.ts";
import { buildRewriteMap, rewriteRefs } from "./lib/rewrite.ts";
import { type ComponentInfo, scanSubmodule } from "./lib/scan.ts";

export function buildAll(root: string): string[] {
  requireSubmodules(root);
  const report: string[] = [];
  const manifests = readdirSync(join(root, "curation"))
    .filter((f) => f.endsWith(".yaml"))
    .sort()
    .map((f) => loadManifest(join(root, "curation", f)));
  for (const m of manifests) {
    if (m.hooks?.include?.length) {
      throw new Error(`${m.plugin.name}: hooks.include is not implemented yet — keep it empty (YAGNI)`);
    }
  }
  const components = readdirSync(join(root, "external"))
    .filter((s) => statSync(join(root, "external", s)).isDirectory())
    .flatMap((s) => scanSubmodule(join(root, "external"), s));

  // Everything emitItem could fail on is resolved here, BEFORE the rmSync pair below: a failure
  // discovered mid-emit would leave the committed output half-deleted and marketplace.json stale,
  // so a single typo'd source: would cost a `git checkout`. One error lists every problem.
  const problems = collectProblems(root, manifests, components);
  if (problems.length) {
    const header = problems.length > 1 ? `${problems.length} unresolvable curation items, nothing deleted:\n` : "";
    throw new Error(header + problems.join("\n"));
  }

  rmSync(join(root, "plugins"), { recursive: true, force: true });
  rmSync(join(root, "opencode"), { recursive: true, force: true });

  for (const m of manifests) {
    for (const item of m.items) {
      emitItem(root, m, item, components, report);
    }
    emitOwnSkills(root, m, report);
    const pluginDir = join(root, "plugins", m.plugin.name);
    mkdirSync(join(pluginDir, ".claude-plugin"), { recursive: true });
    writeFileSync(join(pluginDir, ".claude-plugin", "plugin.json"), `${JSON.stringify(m.plugin, null, 2)}\n`);
  }

  writeMarketplace(root, manifests);
  rewriteTree(join(root, "plugins"), buildRewriteMap(manifests, components));
  emitOpenCode(root, report);
  return report;
}

/** The single file emitItem reads out of an overlay when the target is a command or an agent. */
function overlayBodyFile(comp: ComponentInfo, item: CurationItem): string {
  return comp.type === "skill" ? "SKILL.md" : basename(item.source);
}

// Mirrors every throw in emitItem, with the identical wording, so the messages a user sees are the
// same whichever side reports them. The emitItem throws stay as unreachable safety nets.
function collectProblems(root: string, manifests: CurationManifest[], components: ComponentInfo[]): string[] {
  const problems: string[] = [];
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const comp = components.find((c) => c.sourcePath === item.source);
      if (!comp) {
        problems.push(`${m.plugin.name}: source not found in external/: ${item.source}`);
        continue;
      }
      const outName = item.name ?? comp.name;
      const outType = item.as ?? comp.type;
      const overlayDir = join(root, "overlays", m.plugin.name, outName);
      if (item.body === "overlay" && !existsSync(overlayDir)) {
        problems.push(
          `${m.plugin.name}/${outName}: body is overlay but overlays/${m.plugin.name}/${outName}/ is missing — run: npm run eject ${m.plugin.name} ${outName}`,
        );
      } else if (item.body === "overlay" && outType !== "skill") {
        // A skill target copies the whole overlay dir, but a conversion reads one file out of it —
        // and the build looks it up by the SOURCE name, so an overlay renamed by hand is invisible.
        const file = overlayBodyFile(comp, item);
        if (!existsSync(join(overlayDir, file))) {
          problems.push(
            `${m.plugin.name}/${outName}: overlays/${m.plugin.name}/${outName}/${file} is missing — a ${outType} overlay is read by its source file name, so do not rename it`,
          );
        }
      }
      if (outType === "skill" && comp.type !== "skill") {
        problems.push(`${item.source}: ${comp.type} -> skill conversion not supported`);
      }
    }
  }
  return problems;
}

// cpSync copies a symlink as a symlink but resolves its target to an ABSOLUTE local path, so a
// copied link is machine-specific and dangles in every other clone. Committed build output must be
// plain files: skip links (a rejected directory link skips its subtree) and report every skip.
function skipSymlinks(root: string, label: string, report: string[]): (src: string) => boolean {
  return (src) => {
    if (!lstatSync(src).isSymbolicLink()) {
      return true;
    }
    const rel = relative(root, src).replaceAll("\\", "/");
    report.push(`WARN ${label}: skipped symlink ${rel.startsWith("..") ? basename(src) : rel}`);
    return false;
  };
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
    const filter = skipSymlinks(root, `${m.plugin.name}/${outName}`, report);
    cpSync(srcPath, destDir, { recursive: true, filter });
    if (item.body === "overlay") {
      cpSync(overlayDir, destDir, { recursive: true, force: true, filter });
    }
    const skillMd = join(destDir, "SKILL.md");
    const doc = parseDoc(readFileSync(skillMd, "utf8"));
    // forced name last: emitted dir names and the rewrite map both key on outName
    doc.frontmatter = { ...doc.frontmatter, ...item.frontmatter, name: outName };
    writeFileSync(skillMd, serializeDoc(doc));
    report.push(`${m.plugin.name}: skill ${outName} <- ${item.source}${item.body === "overlay" ? " (overlay)" : ""}`);
  } else {
    const srcFile = comp.type === "skill" ? join(srcPath, "SKILL.md") : srcPath;
    let doc = parseDoc(readFileSync(srcFile, "utf8"));
    if (item.body === "overlay") {
      doc = parseDoc(readFileSync(join(overlayDir, overlayBodyFile(comp, item)), "utf8"));
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
    const forcedName: Record<string, unknown> = outType === "agent" ? { name: outName } : {};
    doc = { frontmatter: { ...base, ...item.frontmatter, ...forcedName }, body: doc.body };
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
    cpSync(join(ownDir, name), join(root, "plugins", m.plugin.name, "skills", name), {
      recursive: true,
      filter: skipSymlinks(root, `${m.plugin.name}/${name}`, report),
    });
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
  writeFileSync(join(root, ".claude-plugin", "marketplace.json"), `${JSON.stringify(marketplace, null, 2)}\n`);
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

// OpenCode reads SKILL.md natively, so skills copy verbatim; commands/agents keep only
// the frontmatter OpenCode understands and every dropped key is reported (no silent loss).
function emitOpenCode(root: string, report: string[]): void {
  const pluginsDir = join(root, "plugins");
  if (!existsSync(pluginsDir)) {
    return;
  }
  for (const plugin of readdirSync(pluginsDir)) {
    const skillsDir = join(pluginsDir, plugin, "skills");
    if (existsSync(skillsDir)) {
      for (const name of readdirSync(skillsDir)) {
        cpSync(join(skillsDir, name), join(root, "opencode", "skills", name), {
          recursive: true,
          filter: skipSymlinks(root, `opencode/${name}`, report),
        });
      }
    }
    for (const kind of ["commands", "agents"] as const) {
      const dir = join(pluginsDir, plugin, kind);
      if (!existsSync(dir)) {
        continue;
      }
      // `kind` is the output directory (OpenCode documents plural); `outKind` is the singular label
      const outKind = kind === "commands" ? "command" : "agent";
      mkdirSync(join(root, "opencode", kind), { recursive: true });
      for (const f of readdirSync(dir)) {
        const doc = parseDoc(readFileSync(join(dir, f), "utf8"));
        const kept: Record<string, unknown> = { description: doc.frontmatter.description };
        if (outKind === "agent") {
          kept.mode = "subagent";
        }
        const dropped = Object.keys(doc.frontmatter).filter((k) => k !== "description" && k !== "name");
        if (dropped.length) {
          report.push(`opencode ${outKind} ${f}: dropped frontmatter keys: ${dropped.join(", ")}`);
        }
        writeFileSync(join(root, "opencode", kind, f), serializeDoc({ frontmatter: kept, body: doc.body }));
      }
    }
  }
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) {
  for (const line of buildAll(process.cwd())) {
    console.log(line);
  }
  console.log("Build complete.");
}
