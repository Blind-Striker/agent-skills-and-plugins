import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { basename, join, relative } from "node:path";
import { pathToFileURL } from "node:url";
import { parseDoc } from "./lib/frontmatter.ts";
import { loadManifest } from "./lib/manifest.ts";
import { listFiles, loadLock, LOCK_FILE, PATCH_FILE } from "./lib/overlay.ts";
import { requireSubmodules } from "./lib/preflight.ts";
import { resolveItem } from "./lib/resolve.ts";
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
  requireSubmodules(root);
  const findings: Finding[] = [];
  const manifests = readdirSync(join(root, "curation"))
    .filter((f) => f.endsWith(".yaml"))
    .sort()
    .map((f) => loadManifest(join(root, "curation", f)));
  const components = readdirSync(join(root, "external"))
    .filter((s) => statSync(join(root, "external", s)).isDirectory())
    .flatMap((s) => scanSubmodule(join(root, "external"), s));
  const firstUse = new Map<string, string>();

  // 1. manifest sources exist, plus the two curation footguns the build resolves silently
  for (const m of manifests) {
    for (const item of m.items) {
      const { comp, outName, outType } = resolveItem(root, m.plugin.name, item, components);
      if (!comp) {
        findings.push({ level: "error", message: `${m.plugin.name}: unknown source ${item.source}` });
      }
      if (item.exclude) {
        continue;
      }
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
          outType === "command"
            ? `a command is addressed by its file name (${outName}.md), so it is dead metadata`
            : `the build forces name: ${outName} on the output, so it is discarded`;
        findings.push({
          level: "warn",
          message: `${m.plugin.name}/${outName}: item frontmatter.name is "${declared}" but ${fate} — use the item's own name: field to rename it`,
        });
      }
    }
  }

  // 1c. overlay wiring. The build consults an overlay ONLY when its item says `body:`, so an
  // overlay no item claims is skipped in silence and pristine upstream ships in its place — every
  // hash and patch guard ADR-0001 specifies bypassed by simply never being consulted. That is the
  // one overlay failure the build cannot report, because from its side nothing happened.
  const claimed = new Map<string, { body: "overlay" | "patch" | undefined; exclude: boolean; overlayDir: string }>();
  for (const m of manifests) {
    for (const item of m.items) {
      const { id, overlayDir } = resolveItem(root, m.plugin.name, item, components);
      claimed.set(id, { body: item.body, exclude: item.exclude === true, overlayDir });
    }
  }
  const overlaysDir = join(root, "overlays");
  for (const plugin of existsSync(overlaysDir) ? readdirSync(overlaysDir) : []) {
    const pluginDir = join(overlaysDir, plugin);
    if (!statSync(pluginDir).isDirectory()) {
      continue; // overlays.lock.json lives beside the plugin directories
    }
    for (const name of readdirSync(pluginDir)) {
      if (!statSync(join(pluginDir, name)).isDirectory()) {
        continue;
      }
      const id = `${plugin}/${name}`;
      const entry = claimed.get(id);
      if (!entry) {
        findings.push({
          level: "error",
          message: `overlays/${id}/ matches no curated item — the build never reads it; delete it, or fix the item's name in curation/${plugin}.yaml`,
        });
      } else if (!entry.body && !entry.exclude) {
        // An excluded item is exempt: nothing is emitted for it, so nothing can silently ship.
        findings.push({
          level: "error",
          message: `overlays/${id}/ exists but its item declares no body: — the build ships pristine upstream and every overlay guard is bypassed; add body: overlay or body: patch in curation/${plugin}.yaml`,
        });
      }
    }
  }
  // A lock entry claims a guard over an overlay that is not there. Nothing ships wrong, so this is
  // rot rather than a bypass — but a lock nobody can trace back to a directory is how the next
  // stale entry hides.
  const lock = loadLock(root);
  for (const key of Object.keys(lock)) {
    if (!existsSync(join(overlaysDir, ...key.split("/")))) {
      findings.push({
        level: "warn",
        message: `overlays/${LOCK_FILE} records ${key} but overlays/${key}/ does not exist — drop the entry`,
      });
    }
  }
  // `eject --patch` cuts the patch and then deletes the working copy it came from. Anything left
  // beside overlay.patch is read by nothing, and the next person to edit it is not told so. A
  // warning rather than an error: `--force` re-cutting passes through this state legitimately.
  for (const [id, entry] of claimed) {
    if (entry.body !== "patch" || !existsSync(join(entry.overlayDir, PATCH_FILE))) {
      continue;
    }
    const stranded = listFiles(entry.overlayDir);
    if (stranded.length) {
      findings.push({
        level: "warn",
        message: `overlays/${id}/ holds ${stranded.join(", ")} beside ${PATCH_FILE} — a cut patch leaves no working copy, and the build reads only the patch`,
      });
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
        const ns = match[1];
        if (ns !== undefined && upstreamNs.has(ns)) {
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
