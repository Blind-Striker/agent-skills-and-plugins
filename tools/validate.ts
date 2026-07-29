import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { basename, join, relative } from "node:path";
import { pathToFileURL } from "node:url";
import { parseDoc } from "./lib/frontmatter.ts";
import { loadManifest } from "./lib/manifest.ts";
import { scanSubmodule } from "./lib/scan.ts";

export interface Finding {
  level: "error" | "warn";
  message: string;
}

function* walk(dir: string): Generator<string> {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (e.name === ".git") {
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

// walk() cannot see links: a symlinked directory fails isDirectory() and a symlinked file is
// dropped by every caller's .md filter, so the portability check needs its own pass.
function* walkSymlinks(dir: string): Generator<string> {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (e.name === ".git") {
      continue;
    }
    const p = join(dir, e.name);
    if (e.isSymbolicLink()) {
      yield p;
    } else if (e.isDirectory()) {
      yield* walkSymlinks(p);
    }
  }
}

export function validateRepo(root: string): Finding[] {
  const findings: Finding[] = [];
  const manifests = readdirSync(join(root, "curation"))
    .filter((f) => f.endsWith(".yaml"))
    .sort()
    .map((f) => loadManifest(join(root, "curation", f)));
  const components = readdirSync(join(root, "external"))
    .filter((s) => statSync(join(root, "external", s)).isDirectory())
    .flatMap((s) => scanSubmodule(join(root, "external"), s));
  const bySource = new Map(components.map((c) => [c.sourcePath, c]));
  const firstUse = new Map<string, string>();

  // 1. manifest sources exist, plus the two curation footguns the build resolves silently
  for (const m of manifests) {
    for (const item of m.items) {
      const comp = bySource.get(item.source);
      if (!comp) {
        findings.push({ level: "error", message: `${m.plugin.name}: unknown source ${item.source}` });
      }
      if (item.exclude) {
        continue;
      }
      const outName = item.name ?? comp?.name ?? basename(item.source);
      const ref = `${m.plugin.name}:${outName}`;
      // 1a. same source curated twice: buildRewriteMap keys on the source, so the last item wins
      // and upstream references to the earlier one silently resolve to the later name.
      const earlier = firstUse.get(item.source);
      if (earlier === undefined) {
        firstUse.set(item.source, ref);
      } else {
        findings.push({
          level: "warn",
          message: `${item.source} is curated twice (as ${earlier} and ${ref}) — the rewrite map is last-write-wins, so upstream references to it all resolve to ${ref}; exclude one item if that is not intended`,
        });
      }
      // 1b. dead frontmatter name: the build forces the output name for skills and agents,
      // and a command is addressed by its file name, so this key never takes effect.
      const declared = item.frontmatter?.name;
      if (typeof declared === "string" && declared !== outName) {
        const fate =
          (item.as ?? comp?.type) === "command"
            ? `a command is addressed by its file name (${outName}.md), so it is dead metadata`
            : `the build forces name: ${outName} on the output, so it is discarded`;
        findings.push({
          level: "warn",
          message: `${m.plugin.name}/${outName}: item frontmatter.name is "${declared}" but ${fate} — use the item's own name: field to rename it`,
        });
      }
    }
  }

  const pluginsDir = join(root, "plugins");
  const outputNames = new Map<string, string>();
  const upstreamNs = new Set(components.map((c) => c.namespace));
  for (const m of manifests) {
    upstreamNs.delete(m.plugin.name);
  }

  for (const dir of existsSync(pluginsDir) ? readdirSync(pluginsDir) : []) {
    for (const file of walk(join(pluginsDir, dir))) {
      const rel = relative(root, file).replaceAll("\\", "/");
      // 6. windows-hostile names / length
      if (/[<>:"|?*]/.test(basename(file))) {
        findings.push({ level: "error", message: `${rel}: invalid character for Windows` });
      }
      if (rel.length > 200) {
        findings.push({ level: "warn", message: `${rel}: path longer than 200 chars` });
      }
      if (!file.endsWith(".md")) {
        continue;
      }
      const doc = parseDoc(readFileSync(file, "utf8"));
      // 2. required frontmatter
      if (basename(file) === "SKILL.md") {
        if (!doc.frontmatter.name || !doc.frontmatter.description) {
          findings.push({ level: "error", message: `${rel}: SKILL.md missing name or description` });
        }
        const key = `skill:${String(doc.frontmatter.name)}`;
        if (outputNames.has(key) && outputNames.get(key) !== dir) {
          findings.push({
            level: "error",
            message: `duplicate skill name across plugins: ${String(doc.frontmatter.name)} (${outputNames.get(key)} and ${dir})`,
          });
        }
        outputNames.set(key, dir);
      } else if (/\/(commands|agents)\//.test(rel)) {
        if (!doc.frontmatter.description) {
          findings.push({ level: "error", message: `${rel}: missing description` });
        }
        const kind = rel.includes("/commands/") ? "command" : "agent";
        const key = `${kind}:${basename(file, ".md")}`;
        if (outputNames.has(key) && outputNames.get(key) !== dir) {
          findings.push({
            level: "error",
            message: `duplicate ${kind} name across plugins: ${basename(file, ".md")} (${outputNames.get(key)} and ${dir})`,
          });
        }
        outputNames.set(key, dir);
      }
    }
  }

  // 3. portability of built output + 4. leftover upstream references in it
  const refPattern = /([a-z][a-z0-9-]*):([a-z][a-z0-9-]*)/g;
  for (const outDir of ["plugins", "opencode"]) {
    const dir = join(root, outDir);
    if (!existsSync(dir)) {
      continue;
    }
    // 3. a copied symlink carries an absolute local target, so it dangles in every other clone
    for (const link of walkSymlinks(dir)) {
      findings.push({
        level: "error",
        message: `committed build output must not contain symlinks: ${relative(root, link).replaceAll("\\", "/")}`,
      });
    }
    for (const file of walk(dir)) {
      if (!file.endsWith(".md")) {
        continue;
      }
      const content = readFileSync(file, "utf8");
      for (const match of content.matchAll(refPattern)) {
        if (upstreamNs.has(match[1])) {
          findings.push({
            level: "warn",
            message: `${relative(root, file).replaceAll("\\", "/")}: unrewritten upstream reference ${match[0]} — include it in a manifest or eject and edit the reference out`,
          });
        }
      }
    }
  }

  // 5. marketplace consistency
  const mpPath = join(root, ".claude-plugin", "marketplace.json");
  if (!existsSync(mpPath)) {
    findings.push({ level: "error", message: ".claude-plugin/marketplace.json missing — run npm run build" });
  } else {
    const mp = JSON.parse(readFileSync(mpPath, "utf8")) as { plugins: { name: string }[] };
    const listed = new Set(mp.plugins.map((p) => p.name));
    const built = new Set(existsSync(pluginsDir) ? readdirSync(pluginsDir) : []);
    for (const p of listed) {
      if (!built.has(p)) {
        findings.push({ level: "error", message: `marketplace lists ${p} but plugins/${p} does not exist` });
      }
    }
    for (const p of built) {
      if (!listed.has(p)) {
        findings.push({ level: "error", message: `plugins/${p} exists but is not in marketplace.json` });
      }
    }
  }

  return findings;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) {
  const findings = validateRepo(process.cwd());
  for (const f of findings) {
    console.log(`${f.level.toUpperCase()}: ${f.message}`);
  }
  const errors = findings.filter((f) => f.level === "error").length;
  console.log(`${errors} error(s), ${findings.length - errors} warning(s)`);
  if (errors) {
    process.exit(1);
  }
}
